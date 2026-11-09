using System.Collections.Concurrent;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;
using Ygo.Engine.Replays;

namespace Ygo.Harness;

/// <summary>
/// `--audit [--games N] [--seed N] [--out folder]`: plays campaign characters against each other with their own decks
/// and counts plays no competent duelist makes (a style can be rash or timid; these are rules and deck blunders):
/// damage dealt to yourself by your own cards, losing to your own card, attacking into a bigger face-up monster with no
/// trick involved, ending a turn with lethal on board against an empty field, destroying more of your own monsters than
/// the opponent's with your own effect, and decking out. Games with a blunder are saved as replays to look at with
/// --show-replay. `--band Beginner` draws the pairings from that band's circuit matches instead (each character with
/// the deck it plays there). Also counted: pure Spell/Trap removal (MST, Dust Tornado) spent on a Normal or Quick-Play
/// Spell or Normal Trap while it is on the chain, when it resolves the same off the field (Pot of Greed, Dark Hole).
/// </summary>
public static class BlunderAudit
{
    internal sealed class Finding
    {
        public string Kind = "", Who = "", Card = "", Text = "";
        public int Game, Turn;
        /// <summary>The player (team) the finding is about, 0 or 1; -1 if not known.</summary>
        public int Player = -1;
    }

    /// <summary>Passes a bot's answers through, showing each decision to the watcher.</summary>
    internal sealed class Spy : IDuelist
    {
        private readonly IDuelist _inner;
        private readonly Action<Prompt, byte[]> _seen;
        public Spy(IDuelist inner, Action<Prompt, byte[]> seen) { _inner = inner; _seen = seen; }
        public void OnPhaseChanged() => _inner.OnPhaseChanged();
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var answer = _inner.Respond(prompt, attempt);
            if (answer != null && attempt == 0) _seen(prompt, answer);
            return answer;
        }
    }

    /// <summary>Watches one duel's messages (and queries its board) for blunders.</summary>
    internal sealed class Watcher
    {
        private readonly Duel _duel;
        private readonly ICardDataProvider _cards;
        private readonly string[] _names;
        private readonly ChainTracker _chain = new();
        public readonly List<Finding> Findings = new();
        private readonly int _game;
        private int _turn, _turnPlayer;
        private ushort _phase;
        private readonly int[] _lp = { 8000, 8000 };
        private (string card, bool own)?[] _lastLoss = new (string, bool)?[2];
        // A risky attack waits to see whether a trick (any chain) gets involved before it counts.
        private Finding? _pendingAttack;
        // Lethal check: the attack-position monsters a player had when their Main Phase 1 ended.
        private int _lethalAtk = -1, _lethalOppLp;
        private bool _oppEmpty;
        private readonly int[] _ownLost = new int[2], _oppLost = new int[2];
        private string _chainCard = "";
        // Risky monsters summoned (a Standby Phase upkeep or a lock on your own Special Summons): who, and when.
        private readonly List<(int player, string card, int turn, int lp)> _risky = new();
        private bool _attackedThisTurn;
        // Damage a link dealt to its own controller waits for the link to finish: it counts only if the link didn't
        // also hurt the opponent (Tremendous Fire: 1000 to them, 500 to you).
        private sealed class LinkDamage { public int Self, Opp, Player; public string Card = ""; }
        private readonly Dictionary<int, LinkDamage> _linkDamage = new();
        private int _chainOwner = -1;
        private readonly List<(int controller, uint code, int attack)> _materials = new();
        // Who caused what (the messages say it): the player who activated each chain link (MSG_CHAINING's chain player,
        // not the card's controller: anyone may pay for Royal Oppression), cards each player's Deck lost to each player's
        // effects, and whether a monster reached a player's field from the opponent this turn (a Kaiju handed over).
        private readonly Dictionary<int, int> _activator = new();
        private readonly int[,] _deckTakenBy = new int[2, 2];
        private readonly bool[] _handedOver = new bool[2];

        /// <summary>Who activated a chain link: the chain player from MSG_CHAINING, else the card's controller.</summary>
        private int ActivatorOf(TrackedLink link) => _activator.TryGetValue(link.Number, out var p) ? p : link.Controller & 1;

        /// <summary>The link behind what is happening now: the one resolving, else the one being activated (its costs).</summary>
        private TrackedLink? CurrentLink => _chain.ResolvingLink ?? _chain.Links.LastOrDefault(l => l.State == LinkState.Activating) ?? _chain.Links.LastOrDefault();

        public Watcher(Duel duel, ICardDataProvider cards, string[] names, int game)
        {
            _duel = duel;
            _cards = cards;
            _names = names;
            _game = game;
            _chain.CodeAt = place => BoardState.PublicCodeAt(duel, place);
        }

        private string Name(uint code) => _cards.Get(code)?.Name ?? $"#{code}";

        private void Add(string kind, int player, string card, string text) =>
            Findings.Add(new Finding { Kind = kind, Who = _names[player & 1], Card = card, Text = text, Game = _game, Turn = _turn, Player = player & 1 });

        /// <summary>
        /// A Battle Phase decision: leaving it (or ending the turn) while the monsters that could still attack directly
        /// add up to the opponent's LP, against an empty field.
        /// </summary>
        public void Decided(Prompt prompt, byte[] answer)
        {
            if (prompt is not BattlePrompt p) return;
            var action = (BattleAction)(BitConverter.ToInt32(answer, 0) & 0xffff);
            if (action == BattleAction.Attack || action == BattleAction.Activate) return;
            int me = p.Player & 1, opp = 1 - me;
            if (Monsters(opp).Count > 0) return;
            int total = 0;
            for (int i = 0; i < p.Attackers.Count; i++)
                if (p.CanDirectAttack.Count <= i || p.CanDirectAttack[i])
                    total += At(p.Attackers[i])?.Attack ?? 0;
            if (total >= _lp[opp] && _lp[opp] > 0)
                Add("missed lethal", me, "", $"left the Battle Phase with {total} ATK able to attack directly, opponent at {_lp[opp]} LP");
        }

        private FieldCard? At(CardRef place)
        {
            var list = _duel.QueryLocation(place.Controller, place.Location & 0x7f);
            return place.Sequence < list.Count ? list[(int)place.Sequence] : null;
        }

        /// <summary>
        /// The resolving link of the other player that just moved this card out of a Spell & Trap Zone by its effect,
        /// when the card is a Normal / Quick-Play / Ritual Spell or a Normal / Counter Trap waiting on the chain that
        /// resolves the same off the field, and the removal is pure Spell/Trap removal; otherwise null.
        /// </summary>
        public TrackedLink? WastedOnResolving(uint code, CardRef from, uint reason)
        {
            if ((from.Location & 0x7f) != Location.SZone || (from.Location & 0x80) != 0 || (reason & 0x40) == 0) return null;
            if (_chain.ResolvingLink is not { } by || by.Controller == (from.Controller & 1)) return null;
            var info = _cards.Get(code);
            // The bot's rule (SmartDuelist.SpentOnResolving): pure Spell/Trap removal (MST, Dust Tornado: nothing else in
            // its text) on a card that resolves the same off the field (no "this card" in its text: not Shade Brigandine).
            if (info == null || !CardTraits.ResolvesOffField(info) || _cards.Get(by.Code) is not { } remover || !CardTraits.PureSpellTrapRemoval(remover)) return null;
            bool waiting = _chain.Links.Any(l => l.State == LinkState.Waiting && l.Code == code && l.Controller == (from.Controller & 1)
                                                 && (l.Location & 0x7f) == Location.SZone && l.Sequence == from.Sequence);
            // Not when the removal was going anyway: one of their links targeted it (Dust Tornado answering the MST on it),
            // or wipes Spells/Traps (Heavy Storm).
            bool doomed = _chain.Links.Any(l => l.Controller != by.Controller && (l.Targets.Any(t => t.Controller == by.Controller
                                                 && (t.Location & 0x7f) == (by.Location & 0x7f) && t.Sequence == by.Sequence)
                                                 || _cards.Get(l.Code) is { } wipe && CardTraits.WipesSpellTraps(wipe)));
            return waiting && !doomed ? by : null;
        }

        private static string Kind(CardInfo info) =>
            (info.Type & CardType.Trap) != 0 ? ((info.Type & 0x100000) != 0 ? "a Counter Trap" : "a Normal Trap")
            : (info.Type & 0x10000) != 0 ? "a Quick-Play Spell" : (info.Type & 0x80) != 0 ? "a Ritual Spell" : "a Normal Spell";

        private List<FieldCard> Monsters(int player) => _duel.QueryLocation((byte)player, Location.MZone).Where(c => c != null).Select(c => c!).ToList();

        public void Handle(byte[] m)
        {
            _chain.Handle(m);
            var r = new MessageReader(m, 1);
            switch (m[0])
            {
                case Msg.NewTurn:
                    _turn++;
                    _turnPlayer = r.U8() & 1;
                    _handedOver[0] = _handedOver[1] = false;
                    _pendingAttack = null;
                    _lethalAtk = -1;
                    _attackedThisTurn = false;
                    break;
                case Msg.NewPhase:
                {
                    var phase = r.U16();
                    if (_pendingAttack != null) { Findings.Add(_pendingAttack); _pendingAttack = null; }
                    // Main Phase 1 ending: what could attack, and whether the opponent's field is empty.
                    if (_phase == Phase.Main1 && _turn > 1)
                    {
                        var mine = Monsters(_turnPlayer).Where(c => c.IsFaceUp && !c.IsDefense).ToList();
                        _oppEmpty = Monsters(1 - _turnPlayer).Count == 0;
                        _lethalAtk = mine.Sum(c => c.Attack);
                        _lethalOppLp = _lp[1 - _turnPlayer];
                    }
                    _phase = phase;
                    break;
                }
                case 93: // MSG_EQUIP: an Equip that only helps put on the opponent's monster
                {
                    var equip = r.LocInfo(0);
                    var target = r.LocInfo(0);
                    var list = _duel.QueryLocation(equip.Controller, equip.Location & 0x7f);
                    var card = equip.Sequence < list.Count ? list[(int)equip.Sequence] : null;
                    var text = card == null ? "" : _cards.Get(card.Code)?.Text ?? "";
                    bool boost = text.Contains("quipped monster gains") && !text.Contains("loses") && !text.Contains("cannot attack")
                                 && !text.Contains("take control") && !text.Contains("your opponent controls");
                    if (card != null && boost && (target.Controller & 1) != (equip.Controller & 1))
                        Add("boosted the opponent's monster", equip.Controller, Name(card.Code), $"equipped {Name(card.Code)} to the opponent's monster");
                    break;
                }
                case Msg.Summoning:
                case Msg.SpSummoning:
                {
                    var c = r.LocInfo(r.U32());
                    var info = _cards.Get(c.Code);
                    // An Extra Deck summon whose materials included a stronger monster than it (a boss into a lesser Link).
                    if (m[0] == Msg.SpSummoning && info != null && info.IsExtraDeckMonster && info.Attack > 0)
                    {
                        var stronger = _materials.Where(x => x.controller == (c.Controller & 1) && x.attack > info.Attack + 300).OrderByDescending(x => x.attack).FirstOrDefault();
                        if (stronger.code != 0)
                            Add("downgraded a monster", c.Controller, Name(c.Code), $"used {Name(stronger.code)} ({stronger.attack} ATK) to summon {Name(c.Code)} ({info.Attack} ATK)");
                    }
                    _materials.Clear();
                    if (Drawbacks.UpkeepDamage(info) > 0 || Drawbacks.LocksSpecialSummons(info))
                        _risky.Add((c.Controller & 1, Name(c.Code), _turn, _lp[c.Controller & 1]));
                    break;
                }
                case Msg.Chaining:
                {
                    // MSG_CHAINING: code, the card's place, then the chain player (who activated it), its place, the description, the link number.
                    var chainCode = r.U32();
                    r.LocInfo(chainCode);
                    int chainPlayer = r.U8() & 1;
                    r.U8(); r.U32(); r.U64();
                    _activator[(int)r.U32()] = chainPlayer;
                }
                    _pendingAttack = null; // a trick or an effect is involved: not a plain bad attack
                    if (_chain.Links.Count == 1)
                    {
                        _ownLost[0] = _ownLost[1] = _oppLost[0] = _oppLost[1] = 0;
                        var first = _chain.Links[0];
                        _chainCard = Name(first.Code);
                        _chainOwner = first.Controller;
                    }
                    break;
                case Msg.ChainSolved:
                {
                    int number = r.U8();
                    if (_linkDamage.TryGetValue(number, out var d))
                    {
                        if (d.Self > 0 && d.Opp == 0) Add("self damage", d.Player, d.Card, $"took {d.Self} from their own {d.Card} ({_lp[d.Player & 1]} LP left)");
                        _linkDamage.Remove(number);
                    }
                    break;
                }
                case Msg.ChainNegated:
                case Msg.ChainDisabled:
                {
                    // A player negating their own link (Koa'ki Meiru Guardian on their own Raptite).
                    int number = r.U8();
                    var victim = _chain.Links.FirstOrDefault(l => l.Number == number);
                    var by = victim?.NegatedBy > 0 ? _chain.Links.FirstOrDefault(l => l.Number == victim.NegatedBy) : null;
                    // Who activated each (the chain player): Royal Oppression ("Either player can pay 800 LP") is often the opponent's doing.
                    bool anyPlayer = by != null && (_cards.Get(by.Code)?.Text ?? "") is var byText && (byText.Contains("Any player can") || byText.Contains("Either player can"));
                    if (victim != null && by != null && ActivatorOf(by) == ActivatorOf(victim) && by.Code != victim.Code && !victim.NegatedByTarget
                        && (!anyPlayer || _activator.ContainsKey(by.Number)))
                        Add("negated own effect", victim.Controller, Name(by.Code), $"{Name(by.Code)} negated their own {Name(victim.Code)}");
                    break;
                }
                case Msg.ChainEnd:
                    // An effect of yours that destroyed more of your monsters than the opponent's.
                    for (int p = 0; p < 2; p++)
                        if (_ownLost[p] > 0 && _ownLost[p] > _oppLost[p])
                            Add("own effect cost more", p, _chainCard, $"lost {_ownLost[p]} of their own monsters to their own chain, the opponent {_oppLost[p]}");
                    _ownLost[0] = _ownLost[1] = _oppLost[0] = _oppLost[1] = 0;
                    break;
                case Msg.Attack:
                {
                    if (_pendingAttack != null) { Findings.Add(_pendingAttack); _pendingAttack = null; }
                    _attackedThisTurn = true;
                    var a = r.LocInfo(0);
                    var t = r.LocInfo(0);
                    if (t.Location == 0) break;
                    var attacker = At(a);
                    var target = At(t);
                    if (attacker == null || target == null || !target.IsFaceUp || !attacker.IsFaceUp) break;
                    bool loses = target.IsDefense ? target.Defense > attacker.Attack : target.Attack > attacker.Attack;
                    if (loses)
                        _pendingAttack = new Finding
                        {
                            Kind = "attack into a bigger monster", Who = _names[a.Controller & 1], Card = Name(attacker.Code), Game = _game, Turn = _turn,
                            Text = $"{Name(attacker.Code)} ({attacker.Attack}) attacked {Name(target.Code)} ({(target.IsDefense ? $"DEF {target.Defense}" : $"ATK {target.Attack}")})",
                        };
                    break;
                }
                case Msg.Damage:
                {
                    int p = r.U8() & 1;
                    int amount = (int)r.U32();
                    _lp[p] = Math.Max(0, _lp[p] - amount);
                    var link = _chain.ResolvingLink;
                    bool own = link != null && ActivatorOf(link) == p;
                    var card = link != null ? Name(link.Code) : "battle";
                    _lastLoss[p] = (card, own);
                    if (link != null)
                    {
                        if (!_linkDamage.TryGetValue(link.Number, out var d)) _linkDamage[link.Number] = d = new LinkDamage { Card = card, Player = link.Controller };
                        if (own) d.Self += amount; else d.Opp += amount;
                    }
                    break;
                }
                case Msg.PayLpCost:
                {
                    int p = r.U8() & 1;
                    _lp[p] = Math.Max(0, _lp[p] - (int)r.U32());
                    // A cost is paid by who activated the link; one with no link of theirs (an upkeep) names no card.
                    _lastLoss[p] = (CurrentLink is { } l && ActivatorOf(l) == p ? Name(l.Code) : "", true);
                    break;
                }
                case Msg.Recover:
                {
                    int p = r.U8() & 1;
                    _lp[p] += (int)r.U32();
                    break;
                }
                case Msg.LpUpdate:
                {
                    int p = r.U8() & 1;
                    _lp[p] = (int)r.U32();
                    break;
                }
                case Msg.Move:
                {
                    var code = r.U32();
                    var from = r.LocInfo(code);
                    var to = r.LocInfo(code);
                    uint reason = r.AtEnd ? 0 : r.U32();
                    // Cards leaving a Deck by an effect (not a draw): counted by who caused it (who activated the link).
                    if ((from.Location & 0x7f) == Location.Deck && (from.Location & 0x80) == 0 && (to.Location & 0x7f) != Location.Deck
                        && CurrentLink is { } deckLink)
                        _deckTakenBy[from.Controller & 1, ActivatorOf(deckLink)]++;
                    // A monster put on a player's field from the opponent's side (a Kaiju handed over, control taken).
                    // A token made there by the opponent's effect counts too.
                    if ((to.Location & 0x7f) == Location.MZone && (to.Location & 0x80) == 0
                        && ((from.Location & 0x7f) != 0 ? (from.Controller & 1) != (to.Controller & 1) : CurrentLink is { } made && ActivatorOf(made) != (to.Controller & 1)))
                        _handedOver[to.Controller & 1] = true;
                    // Materials leaving the field for a summon (REASON_MATERIAL), remembered until it happens.
                    if ((from.Location & 0x7f) == Location.MZone && (from.Location & 0x80) == 0 && (reason & 0x8) != 0)
                        _materials.Add((from.Controller & 1, code, _cards.Get(code)?.Attack ?? 0));
                    // A Spell/Trap destroyed by its own controller's effect, not its own card's (MST on your own MST).
                    if ((from.Location & 0x7f) == Location.SZone && (from.Location & 0x80) == 0 && (reason & 0x1) != 0 && (reason & 0x40) != 0
                        && _chain.ResolvingLink is { } stLink && stLink.Controller == (from.Controller & 1) && stLink.Code != code
                        // Only removal that could have hit either side (MST: "1 Spell/Trap on the field"); Double Cyclone
                        // and Rokket Tracer must take a card "you control".
                        && (_cards.Get(stLink.Code)?.Text ?? "") is var removal && removal.Contains("on the field") && !removal.Contains("you control"))
                    {
                        var destroyed = _cards.Get(code);
                        Add("destroyed own Spell/Trap", from.Controller & 1, Name(stLink.Code),
                            $"{Name(stLink.Code)} destroyed their own {(destroyed != null && (from.Position & 0xA) != 0 ? "Set " : "")}{Name(code)}");
                    }
                    // Removal spent on a card that resolves anyway: a Normal or Quick-Play Spell, or a Normal Trap, still
                    // waiting on the chain (destroying or banishing it there doesn't stop it, and it leaves once it has
                    // resolved). A Continuous, Field or Equip card on the chain is fair game: off the field, it does nothing.
                    if (WastedOnResolving(code, from, reason) is { } wasted)
                    {
                        var leaving = _cards.Get(code);
                        Add("removal on a card that resolves anyway", wasted.Controller, Name(wasted.Code),
                            $"{Name(wasted.Code)} {((to.Location & 0x7f) == Location.Grave ? "destroyed" : "removed")} the opponent's {Name(code)} while it was on the chain"
                            + (leaving != null ? $" ({Kind(leaving)})" : ""));
                    }
                    if ((from.Location & 0x7f) != Location.MZone || (from.Location & 0x80) != 0 || (to.Location & 0x7f) != Location.Grave || (reason & 0x1) == 0) break;
                    int owner = from.Controller & 1;
                    var link = _chain.ResolvingLink;
                    // Destroyed by a rule of its own (Burning Abyss when you control another monster), outside battle; not when the
                    // opponent caused it by putting a monster on that field this turn (a Kaiju handed over).
                    if (link == null && (reason & 0x20) == 0 && (reason & 0x40) != 0 && !_handedOver[owner])
                        Add("destroyed by its own rule", owner, Name(code), $"{Name(code)} destroyed itself (outside a chain or battle)");
                    if (link == null) break;
                    if (link.Controller == owner && link.Code != code) _ownLost[owner]++;
                    else if (link.Controller != owner) _oppLost[link.Controller & 1]++;
                    break;
                }
                case 55: // MSG_SWAP: two monsters trade controllers (Creature Swap): each side got one from the other
                    _handedOver[0] = _handedOver[1] = true;
                    break;
                case Msg.Win:
                {
                    int winner = r.U8();
                    int reason = r.U8();
                    if (winner > 1) break;
                    int loser = 1 - winner;
                    foreach (var (player, card, turn, lp) in _risky)
                        Findings.Add(new Finding
                        {
                            Kind = player == winner ? "risky summon, then won" : "risky summon, then lost", Who = _names[player], Card = card, Player = player,
                            Game = _game, Turn = turn, Text = $"summoned at {lp} LP on turn {turn}; the duel ended on turn {_turn}",
                        });
                    if (_lastLoss[loser] is { own: true } last && _lp[loser] <= 0)
                        Add("lost to own card", loser, last.card, last.card.Length > 0 ? $"the last LP they lost was to their own {last.card}"
                            : "the last LP they lost paid a cost with no card named (an upkeep or a forced payment)");
                    if (reason == 2)
                    {
                        // Who emptied the Deck: the opponent's effects milling or banishing it is their win, not the loser's blunder.
                        int byThem = _deckTakenBy[loser, winner], byOwn = _deckTakenBy[loser, loser];
                        if (byThem > byOwn && byThem >= 5)
                            Add("decked out by the opponent", loser, "", $"ran out of cards to draw; the opponent's effects took {byThem} cards from their Deck (their own {byOwn})");
                        else Add("decked out", loser, "", $"ran out of cards to draw ({byOwn} cards left their Deck to their own effects, {byThem} to the opponent's)");
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// `--audit-replays &lt;folder&gt;`: every replay in a folder (games played in the app) through the same watchers, so
    /// both players' blunders in real games show up. Prints each finding with its replay, turn and player.
    /// </summary>
    public static int RunReplays(ICardDataProvider cards, IScriptProvider scripts, string folder)
    {
        var files = Directory.GetFiles(folder, "*.replay", SearchOption.AllDirectories).OrderBy(f => File.GetLastWriteTimeUtc(f)).ToList();
        Console.WriteLine($"{files.Count} replays in {folder}");
        int total = 0;
        for (int g = 0; g < files.Count; g++)
        {
            var replay = Replay.Load(files[g]);
            if (replay == null) { Console.WriteLine($"  {Path.GetFileName(files[g])}: not a replay"); continue; }
            using var duel = replay.Recipe(cards, scripts).Create();
            var names = replay.Names.Select((n, i) => replay.HumanSeat == i ? $"{n} (you)" : n).ToArray();
            var watcher = new Watcher(duel, cards, names, g);
            var feed = new ReplayFeed(replay.Responses);
            var runner = new DuelRunner(duel, new Spy(new ReplayDuelist(feed), watcher.Decided), new Spy(new ReplayDuelist(feed), watcher.Decided));
            YataLock.Attach(runner, duel);
            runner.MessageReceived += watcher.Handle;
            try
            {
                while (runner.Step() && !(runner.IsWaiting && feed.Finished)) { }
            }
            catch (Exception ex) { Console.WriteLine($"  {Path.GetFileName(files[g])}: {ex.Message}"); }
            Console.WriteLine($"  {Path.GetFileName(files[g])}: {replay.Names[0]} vs {replay.Names[1]}, winner {replay.Winner}, {replay.Turns} turns, {watcher.Findings.Count} findings");
            foreach (var f in watcher.Findings) Console.WriteLine($"     turn {f.Turn,2}  {f.Kind}: {f.Who}: {f.Text}");
            total += watcher.Findings.Count;
        }
        Console.WriteLine($"{total} findings");
        return 0;
    }

    /// <summary>
    /// The pairings to draw from: every character with its first deck, or (`--band Beginner`) the matches of that
    /// band's circuits in campaign/circuits.txt, each character with the deck it plays there.
    /// </summary>
    private static List<(Personality style, string deck)> Roster(string root, Dictionary<string, string> deckFiles, string? band)
    {
        var characters = Directory.GetFiles(Path.Combine(root, "personalities"), "*.profile").Select(Personality.Load).ToDictionary(p => p.Id);
        if (band == null)
            return characters.Values.Where(p => p.Decks.Count > 0 && deckFiles.ContainsKey(p.Decks[0])).OrderBy(p => p.Id).Select(p => (p, p.Decks[0])).ToList();
        var roster = new List<(Personality, string)>();
        string current = "";
        foreach (var raw in File.ReadAllLines(Path.Combine(root, "campaign", "circuits.txt")))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.StartsWith("band:")) current = line[5..].Trim();
            else if (line.StartsWith("match:") && current.Equals(band, StringComparison.OrdinalIgnoreCase))
            {
                var parts = line[6..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && characters.TryGetValue(parts[0], out var who) && deckFiles.ContainsKey(parts[1])
                    && !roster.Any(r => r.Item1.Id == who.Id && r.Item2 == parts[1]))
                    roster.Add((who, parts[1]));
            }
        }
        return roster;
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed, string? outFolder, int onlyGame = -1, string? band = null)
    {
        // Decks by name, wherever they sit under decks/ (the historical ones are in decks/history).
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var roster = Roster(root, deckFiles, band);
        Console.WriteLine(band == null ? $"{roster.Count} characters with decks" : $"{roster.Count} pairings in the {band} band: {string.Join(", ", roster.Select(r => $"{r.style.Id} {r.deck}"))}");
        Deck LoadDeck(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var findings = new ConcurrentBag<Finding>();
        var bySkill = new ConcurrentDictionary<string, int>();
        int played = 0, errors = 0;
        var keeper = ReplayKeeper.Current ?? (outFolder != null ? new ReplayKeeper(outFolder, "flagged", int.MaxValue) : null);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        // --audit-game N: just that game, with the bots' reasoning printed.
        Parallel.For(onlyGame >= 0 ? onlyGame : 0, onlyGame >= 0 ? onlyGame + 1 : games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
        {
            Action<string>? log = onlyGame >= 0 ? Console.WriteLine : null;
            var rng = new Random(seed * 1000 + g);
            var (a, deckA) = roster[rng.Next(roster.Count)];
            var (b, deckB) = roster[rng.Next(roster.Count)];
            var styles = new[] { a, b };
            var decks = new[] { LoadDeck(deckA), LoadDeck(deckB) };
            var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)(seed * 1000 + g) }, Deck0 = decks[0], Deck1 = decks[1] };
            try
            {
                using var duel = recipe.Create();
                var context = new DuelContext(duel, cards);
                var answers = new ResponseLog();
                IDuelist Bot(int seat)
                {
                    var style = styles[seat];
                    var playbook = style.KnowsDeck ? Playbooks.ForDeck(decks[seat]) : null;
                    var planner = style.PlansAhead ? new TurnPlanner(recipe, answers, style, () => style.KnowsDeck ? Playbooks.ForDeck(decks[seat]) : null) : null;
                    return new SmartDuelist(style.Name, g * 2 + seat, context, log, style, playbook, planner);
                }
                var names = new[] { $"{a.Name} ({decks[0].Name}, skill {a.Skill})", $"{b.Name} ({decks[1].Name}, skill {b.Skill})" };
                var watcher = new Watcher(duel, cards, names, g);
                var runner = new DuelRunner(duel, new Spy(Bot(0), watcher.Decided), new Spy(Bot(1), watcher.Decided), answers);
                runner.MessageReceived += context.Observe;
                YataLock.Attach(runner, duel);
                runner.MessageReceived += watcher.Handle;
                if (log != null)
                {
                    var narrator = new DuelNarrator(cards, a.Name, b.Name);
                    narrator.CodeAt = place => BoardState.PublicCodeAt(duel, place);
                    narrator.Line += line => Console.WriteLine("   | " + line);
                    runner.MessageReceived += narrator.Handle;
                }
                var result = runner.Run(8000);
                Interlocked.Increment(ref played);
                foreach (var f in watcher.Findings) findings.Add(f);
                // Games with a finding are saved (--out, or --keep-replays with its --keep mode), each with its bots' sidecar.
                // --out keeps its old rule (any finding, game<N>.replay overwritten); --keep-replays' flagged mode needs a low-or-worse finding.
                bool flagged = ReplayKeeper.Current == null ? watcher.Findings.Count > 0
                    : watcher.Findings.Any(f => ReplayAnalyzer.Weight(ReplayAnalyzer.Severity(f.Kind, f.Text)) >= 1);
                if (keeper != null && (keeper.Mode == "all" || (keeper.Mode == "stalls" ? result.Winner < 0 : flagged)))
                    keeper.Save("audit", $"game{g}", recipe, answers, new[] { a.Name, b.Name },
                        new[] { SeatSpec.Of(0, a, decks[0], g * 2, a.PlansAhead), SeatSpec.Of(1, b, decks[1], g * 2 + 1, b.PlansAhead) },
                        result, fileName: $"game{g}", overwrite: ReplayKeeper.Current == null);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                Console.Error.WriteLine($"game {g}: {ex.Message}");
            }
        });

        var all = findings.ToList();
        Console.WriteLine($"{played} games ({errors} errors) in {timer.Elapsed:mm\\:ss}: {all.Count} findings, {all.Select(f => f.Game).Distinct().Count()} games with at least one");
        foreach (var kind in all.GroupBy(f => f.Kind).OrderByDescending(k => k.Count()))
        {
            Console.WriteLine();
            Console.WriteLine($"== {kind.Key}: {kind.Count()} ({kind.Select(f => f.Game).Distinct().Count()} games)");
            foreach (var card in kind.GroupBy(f => f.Card).OrderByDescending(c => c.Count()).Take(8))
                Console.WriteLine($"   {card.Count(),4}  {(card.Key.Length > 0 ? card.Key : "-")}   e.g. game {card.First().Game} turn {card.First().Turn}: {card.First().Who}: {card.First().Text}");
        }
        return 0;
    }
}
