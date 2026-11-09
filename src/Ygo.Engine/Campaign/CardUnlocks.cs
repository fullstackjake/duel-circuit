using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>What kind of card unlock a <see cref="Grant"/> is.</summary>
    public enum GrantKind
    {
        /// <summary>A rival's signature, the first time you beat them (at its commonest printing).</summary>
        RivalGift,
        /// <summary>A spare from their deck after a later win (15%, first playthrough only).</summary>
        Drop,
        /// <summary>Bond 3: one trade at the counter, offered (or, from <see cref="CardUnlocks.Trade"/>, taken).</summary>
        BondTrade,
        /// <summary>Bond 5: their signature again, at its rarest printing.</summary>
        BondGift,
        /// <summary>A bounty on Gus's board claimed: a card from your Recipe (or owed until you set one).</summary>
        Bounty,
        /// <summary>A bounty claimed with no Recipe set: the band's one Card Token.</summary>
        BountyToken,
        /// <summary>Every card of a shop set owned: its rarest-printed card, framed, and a poster.</summary>
        SetComplete,
    }

    /// <summary>One card unlock paid, held or offered: what the results screen lists and the story fills its scene with.</summary>
    public sealed class Grant
    {
        public GrantKind Kind;
        /// <summary>The character it's from ("" for the shop's own: bounties, set completion).</summary>
        public string Character = "";
        /// <summary>The card (base code; 0 for a trade offer or a token) and the rarity of the copy paid.</summary>
        public uint Code;
        public Rarity Rarity;
        /// <summary>The results line: "Vera's gift: Gearfried the Swordmaster · Raw · Near Mint".</summary>
        public string Text = "";
        /// <summary>The story event it raises ("beat:vera", "bond:vera:3", "bond:vera:5", "setcomplete:LOB"), or "".</summary>
        public string Event = "";
        /// <summary>For the story's {card} and {set}.</summary>
        public string CardName = "", SetName = "";
        public string SetCode = "";
        /// <summary>Held, not paid yet: a gift not in the shop's era, or a bounty waiting for a Recipe.</summary>
        public bool Owed;
        /// <summary>The copy paid (null when none was, or a 4th copy wasn't kept).</summary>
        public CardCopy? Copy;

        public override string ToString() => Text;
    }

    /// <summary>
    /// A bounty on Gus's board (campaign/bounties.txt): every one of its checks must hold in a one-on-one circuit duel
    /// you win in its band. Checks: finisher:normal|extra|effect (a Normal Monster's attack, an Extra Deck monster's
    /// attack, or effect damage took their last LP), nodamage, byturn:N (won by turn N), comeback:LP (your LP fell to LP
    /// or less), bighit:N (one hit of N or more), turn:N (N damage in one turn), summons:N (in one turn), traps:N
    /// (activated), second (you went second), own (your own deck, no loaner).
    /// </summary>
    public sealed class Bounty
    {
        public string Id = "";
        /// <summary>The board's band ("Beginner"...).</summary>
        public string Band = "";
        public List<string> Checks = new List<string>();
        public string Text = "";

        public static readonly string[] Kinds = { "finisher", "nodamage", "byturn", "comeback", "bighit", "turn", "summons", "traps", "second", "own" };

        /// <summary>Whether a check is one the board knows (and its number, where it takes one, reads).</summary>
        public static bool Known(string check)
        {
            var (kind, arg) = Split(check);
            switch (kind)
            {
                case "finisher": return arg is "normal" or "extra" or "effect";
                case "nodamage": case "second": case "own": return arg.Length == 0;
                case "byturn": case "comeback": case "bighit": case "turn": case "summons": case "traps":
                    return int.TryParse(arg, out var n) && n > 0;
                default: return false;
            }
        }

        /// <summary>Whether this duel meets every check (<paramref name="loaner"/>: you played a loaner deck).</summary>
        public bool Met(DuelTally t, bool loaner) => Checks.All(c => Holds(c, t, loaner));

        public static bool Holds(string check, DuelTally t, bool loaner)
        {
            var (kind, arg) = Split(check);
            int.TryParse(arg, out var n);
            switch (kind)
            {
                case "finisher":
                    return arg switch
                    {
                        "normal" => t.Finisher == DuelTally.Finish.Normal,
                        "extra" => t.Finisher == DuelTally.Finish.Extra,
                        "effect" => t.Finisher == DuelTally.Finish.Effect,
                        _ => false,
                    };
                case "nodamage": return t.DamageTaken == 0;
                case "byturn": return t.Turns > 0 && t.Turns <= n;
                case "comeback": return t.LowestLp <= n;
                case "bighit": return t.BiggestHit >= n;
                case "turn": return t.MostDamageInATurn >= n;
                case "summons": return t.MostSummonsInATurn >= n;
                case "traps": return t.TrapsActivated >= n;
                case "second": return t.WentSecond;
                case "own": return !loaner;
                default: return false;
            }
        }

        private static (string kind, string arg) Split(string check)
        {
            var colon = check.IndexOf(':');
            return colon < 0 ? (check.Trim().ToLowerInvariant(), "") : (check.Substring(0, colon).Trim().ToLowerInvariant(), check.Substring(colon + 1).Trim().ToLowerInvariant());
        }
    }

    /// <summary>
    /// Gus's bounty board (campaign/bounties.txt): "band: &lt;band&gt;[, &lt;circuit band&gt;...]" (circuits of the later bands
    /// count on the first one's board: "band: Master, Gauntlet"), then "bounty: &lt;id&gt; | &lt;checks&gt; | &lt;text&gt;" lines.
    /// </summary>
    public sealed class BountyBoard
    {
        public const string FileName = "bounties.txt";

        public IReadOnlyList<Bounty> All { get; }
        private readonly Dictionary<string, string> _boardOf;

        private BountyBoard(List<Bounty> all, Dictionary<string, string> boardOf)
        {
            All = all;
            _boardOf = boardOf;
        }

        public static BountyBoard Empty => new BountyBoard(new List<Bounty>(), new Dictionary<string, string>());

        public static BountyBoard Parse(string text)
        {
            var all = new List<Bounty>();
            var boardOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string band = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                if (key == "band")
                {
                    var names = value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
                    band = names.FirstOrDefault() ?? "";
                    foreach (var n in names) boardOf[n] = band;
                }
                else if (key == "bounty" && band.Length > 0)
                {
                    var parts = value.Split('|');
                    if (parts.Length < 3) continue;
                    all.Add(new Bounty
                    {
                        Id = parts[0].Trim(), Band = band, Text = string.Join("|", parts.Skip(2)).Trim(),
                        Checks = parts[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                    });
                }
            }
            return new BountyBoard(all, boardOf);
        }

        public static BountyBoard Load(string path) => System.IO.File.Exists(path) ? Parse(System.IO.File.ReadAllText(path)) : Empty;

        /// <summary>The board a circuit's band counts on ("Gauntlet" is "Master"), or "" when it has none.</summary>
        public string BoardOf(string circuitBand) => _boardOf.TryGetValue(circuitBand ?? "", out var b) ? b : "";

        /// <summary>The bounties a circuit of this band counts toward.</summary>
        public List<Bounty> For(string circuitBand)
        {
            var board = BoardOf(circuitBand);
            return All.Where(b => b.Band == board).ToList();
        }

        /// <summary>The boards, in file order.</summary>
        public IEnumerable<string> Bands => All.Select(b => b.Band).Distinct();

        /// <summary>
        /// The circuit whose clear puts the board up, and the story scene that introduces it (campaign/story.txt's
        /// gus-lessons, "when: clear:Beginner Circuit"): Gus hangs the board beside his lessons.
        /// </summary>
        public const string OpensAfter = "Beginner Circuit", IntroScene = "gus-lessons";

        /// <summary>
        /// Whether Gus's board is up: once <see cref="OpensAfter"/> is cleared (or its <see cref="IntroScene"/> was seen, or
        /// in New Game+, both of which outlast the circuits' reset). Before that the board shows nothing and pays nothing.
        /// A save already past the clear keeps its board. The campaign screen's Bounties button asks this.
        /// </summary>
        public static bool IsOpen(Profile profile, Circuits circuits) => IsOpen(profile, circuits, null);

        /// <summary>
        /// <see cref="IsOpen(Profile, Circuits)"/> for a result just recorded: the duel that clears <see cref="OpensAfter"/>
        /// comes before the scene that puts the board up, so it doesn't count toward a bounty.
        /// </summary>
        public static bool IsOpen(Profile profile, Circuits circuits, DuelReward? justRecorded)
        {
            if (profile.Cycle > 0 || profile.Seen.Contains(IntroScene)) return true;
            int gate = GateCircuit(circuits);
            if (gate < 0 || !circuits.IsCleared(gate, profile)) return false;
            // By name, as GateCircuit finds it: the reward may come from another Circuits instance (a reload, a tool).
            var cleared = justRecorded?.ClearedCircuit;
            return cleared == null || !string.Equals(cleared.Name, circuits.All[gate].Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The circuit <see cref="OpensAfter"/> names (the first one when no circuit has that name), -1 for none.</summary>
        public static int GateCircuit(Circuits circuits)
        {
            if (circuits.All.Count == 0) return -1;
            for (int i = 0; i < circuits.All.Count; i++)
                if (string.Equals(circuits.All[i].Name, OpensAfter, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }
    }

    /// <summary>One row of the bounty board as the campaign screen shows it.</summary>
    public sealed class BountyRow
    {
        public Bounty Bounty = new Bounty();
        public bool Claimed;
    }

    /// <summary>
    /// Card unlocks (CARD-UNLOCKS-SPEC-2026-11-01): rivals' gifts, drops, bond trades and gifts, Gus's bounties and set
    /// completion. They pay cards (gift copies: <see cref="CopySource.Reward"/>, Raw Near Mint, paid 0, so they sell for
    /// 0) or a Card Token, never DP; each pays once per save (keys in <see cref="Profile.Unlocks"/>, kept through New
    /// Game+); every roll comes from the save's salt and duel count, so a replayed run gets the same cards.
    /// </summary>
    public sealed class CardUnlocks
    {
        /// <summary>The chance a later win drops a spare from their deck (per mille), in the first playthrough only.</summary>
        public const int DropPerMille = 150;
        public const int DeckBond = TagPartners.DeckBond, SharpBond = TagPartners.SharpBond;

        private readonly ICardDataProvider _cards;
        private readonly PrintingIndex? _printings;
        private readonly Circuits _circuits;
        private readonly ISet<uint> _noPass;
        private readonly Func<string, Deck?> _loadDeck;
        private readonly GuestList? _guests;
        private readonly Dictionary<string, Personality> _roster;
        private readonly Dictionary<string, uint> _signatures = new Dictionary<string, uint>();
        private readonly Dictionary<string, HashSet<string>> _deckNames = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, List<uint>> _deckCards = new Dictionary<string, List<uint>>();
        /// <summary>Each shop set's distinct cards, and each card's rarest printing in it.</summary>
        private readonly List<(PackSet set, Dictionary<uint, Rarity> cards)> _sets = new List<(PackSet, Dictionary<uint, Rarity>)>();

        public BountyBoard Bounties { get; }

        /// <param name="loadDeck">A deck by name (null when there's none): the characters' decks, for drops, trades and the signature check.</param>
        /// <param name="shop">The shop's booster sets, for set completion (null: no set completion).</param>
        public CardUnlocks(ICardDataProvider cards, PrintingIndex? printings, Circuits circuits, IEnumerable<Personality> roster, ISet<uint> noPass,
            Func<string, Deck?> loadDeck, PackCatalog? shop = null, BountyBoard? bounties = null, GuestList? guests = null, Library? library = null)
        {
            _cards = cards;
            _printings = printings;
            _circuits = circuits;
            _noPass = noPass;
            _loadDeck = loadDeck;
            _guests = guests;
            Bounties = bounties ?? BountyBoard.Empty;
            _roster = roster.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());

            var byName = cards.All.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Alias != 0).First().Code, StringComparer.OrdinalIgnoreCase);
            foreach (var p in _roster.Values)
                if (p.Signature.Length > 0)
                    _signatures[p.Id] = byName.TryGetValue(p.Signature, out var code) ? Banlist.BaseCode(code, cards) : 0;

            void Deck(Personality who, string deck)
            {
                if (deck.Length == 0) return;
                if (!_deckNames.TryGetValue(who.Id, out var set)) _deckNames[who.Id] = set = new HashSet<string>();
                set.Add(deck);
            }
            foreach (var p in _roster.Values) foreach (var d in p.Decks) Deck(p, d);
            foreach (var m in circuits.All.SelectMany(c => c.Matches)) Deck(m.Character, m.Deck);
            foreach (var (lead, partner) in circuits.TagCups.SelectMany(t => t.Matches)) { Deck(lead.Character, lead.Deck); Deck(partner.Character, partner.Deck); }
            if (guests != null)
                foreach (var g in guests.All)
                {
                    foreach (var d in g.Decks) Deck(g.Character, d);
                    if (g.HasTag) Deck(g.Partner, g.PartnerDeck);
                }
            if (library != null)
            {
                foreach (var m in library.Cups.SelectMany(c => c.Matches)) Deck(m.Character, m.Deck);
                foreach (var r in library.Rounds) Deck(r.Match.Character, r.Match.Deck);
            }

            if (shop != null)
            {
                foreach (var set in shop.Sets)
                {
                    var best = new Dictionary<uint, Rarity>();
                    foreach (var pair in set.Pool)
                        foreach (var code in pair.Value)
                            best[code] = best.TryGetValue(code, out var r) && r > pair.Key ? r : pair.Key;
                    _sets.Add((set, best));
                }
                var names = shop.Sets.GroupBy(s => s.Info.Code).ToDictionary(g => g.Key, g => g.First().Info.Name);
                Posters.SetNames = code => names.TryGetValue(code, out var n) ? n : null;
            }
        }

        // ---- Looking things up ----------------------------------------------------------------------

        /// <summary>A character's signature card (base code), or 0 when they have none (or its name didn't resolve).</summary>
        public uint SignatureOf(string id) => _signatures.TryGetValue(id, out var c) ? c : 0;

        /// <summary>Characters with a signature line, by id (0: the name didn't resolve).</summary>
        public IReadOnlyDictionary<string, uint> Signatures => _signatures;

        /// <summary>Every deck a character plays anywhere in the campaign.</summary>
        public IReadOnlyCollection<string> DecksOf(string id) => _deckNames.TryGetValue(id, out var s) ? s : (IReadOnlyCollection<string>)Array.Empty<string>();

        /// <summary>The cards (base codes, sorted) in any of a character's decks.</summary>
        public IReadOnlyList<uint> DeckCards(string id)
        {
            if (_deckCards.TryGetValue(id, out var list)) return list;
            var codes = new HashSet<uint>();
            foreach (var name in DecksOf(id))
                if (_loadDeck(name) is Deck d)
                    foreach (var c in d.Main.Concat(d.Extra).Concat(d.Side)) codes.Add(Banlist.BaseCode(c, _cards));
            return _deckCards[id] = codes.OrderBy(c => c).ToList();
        }

        /// <summary>
        /// The circuit where you first meet a character: their first circuit match; else the circuit that opens their Tag
        /// Cup; else, for a guest, the one their milestone comes in ("lessons": the first; "band:X": the first after
        /// band X; "final": the last, the Every Era final coming after it). -1 when they're nowhere.
        /// </summary>
        public int MeetCircuit(string id)
        {
            for (int i = 0; i < _circuits.All.Count; i++)
                if (_circuits.All[i].Matches.Any(m => m.Character.Id == id)) return i;
            var cup = _circuits.TagCups.FirstOrDefault(t => t.Matches.Any(m => m.lead.Character.Id == id || m.partner.Character.Id == id));
            if (cup != null) return cup.After;
            var guest = _guests?.All.FirstOrDefault(g => g.Character.Id == id || (g.HasTag && g.Partner.Id == id));
            if (guest == null) return -1;
            var rule = guest.Unlock;
            if (rule.Equals("final", StringComparison.OrdinalIgnoreCase)) return _circuits.All.Count - 1;
            if (rule.StartsWith("band:", StringComparison.OrdinalIgnoreCase))
            {
                var band = rule.Substring(5).Trim();
                int last = Enumerable.Range(0, _circuits.All.Count).Where(i => _circuits.All[i].Band.Equals(band, StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(-1).Max();
                return last < 0 ? -1 : Math.Min(last + 1, _circuits.All.Count - 1);
            }
            if (rule.StartsWith("tagwins:", StringComparison.OrdinalIgnoreCase)) return _circuits.TagCups.Count > 0 ? _circuits.TagCups[0].After : 0;
            return _circuits.All.Count > 0 ? 0 : -1;
        }

        /// <summary>
        /// What's wrong with the signatures (empty: nothing): each must resolve to a card, be in one of the character's
        /// decks, be first printed by the era of the circuit where you meet them, not be on the no-pass list, not be
        /// Forbidden on that circuit's list, and not be anyone else's signature.
        /// </summary>
        public List<string> SignatureProblems(IEnumerable<Banlist> lists)
        {
            var problems = new List<string>();
            var listByName = lists.GroupBy(l => l.Name).ToDictionary(g => g.Key, g => g.First());
            foreach (var pair in _signatures.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var who = _roster[pair.Key];
                var name = who.Signature;
                if (pair.Value == 0) { problems.Add($"{who.Id}: \"{name}\" isn't a card"); continue; }
                if (!DeckCards(who.Id).Contains(pair.Value)) problems.Add($"{who.Id}: {name} isn't in their decks ({string.Join(", ", DecksOf(who.Id))})");
                int meet = MeetCircuit(who.Id);
                if (meet < 0) { problems.Add($"{who.Id}: never met in the campaign"); continue; }
                var circuit = _circuits.All[meet];
                int year = _printings?.FirstYear(pair.Value, _cards.Get(pair.Value)?.Alias ?? 0) ?? 0;
                if (_printings != null && (year == 0 || year > circuit.Era)) problems.Add($"{who.Id}: {name} first printed {(year == 0 ? "never" : year.ToString())}, met in {circuit.Name} ({circuit.Era})");
                if (_noPass.Contains(pair.Value)) problems.Add($"{who.Id}: {name} is on the no-pass list");
                if (circuit.Banlist.Length > 0 && listByName.TryGetValue(circuit.Banlist, out var list) && list.Allowed(pair.Value, _cards) == 0)
                    problems.Add($"{who.Id}: {name} is Forbidden on {circuit.Banlist}");
                var twin = _signatures.Where(o => o.Key != pair.Key && o.Value == pair.Value).Select(o => o.Key).FirstOrDefault();
                if (twin != null) problems.Add($"{who.Id}: {name} is {twin}'s signature too");
            }
            return problems;
        }

        private string Name(uint code) => _cards.Get(code)?.Name ?? code.ToString();
        private string Who(string id) => _roster.TryGetValue(id, out var p) ? p.Name : id;
        private uint Alias(uint code) => _cards.Get(code)?.Alias ?? 0;
        private Rarity Commonest(uint code) => _printings?.Commonest(code, Alias(code)) ?? Rarity.Common;
        private Rarity Rarest(uint code) => _printings?.Rarest(code, Alias(code)) ?? Rarity.Common;
        private int Cost(uint code) => CardTrader.Cost(code, _printings, _cards);

        /// <summary>Whether the shop could sell the card now (always, outside era-limited play).</summary>
        /// <summary>
        /// Whether a card was out by the shop's year, in any mode: a spare a duelist slips you is from their time, never a
        /// later card from their deck's Extra Deck (InEra only holds back era-limited play).
        /// </summary>
        private bool ShopHas(Profile profile, uint code)
        {
            if (_printings == null) return true;
            int year = _printings.FirstYear(code, Alias(code));
            return year > 0 && year <= _circuits.ShopYear(profile);
        }

        public bool InEra(Profile profile, uint code) =>
            _printings == null || RecipeShop.InEra(code, profile, _circuits.ShopYear(profile), _printings, _cards);

        /// <summary>A stable hash of a string (character ids into seeds).</summary>
        public static uint StableHash(string text)
        {
            uint h = 2166136261;
            foreach (var ch in text) { h ^= ch; h *= 16777619; }
            return h;
        }

        /// <summary>Adds a gift copy (Raw Near Mint, paid 0) under the 3-copy rule; a 4th copy that isn't better isn't kept (and pays nothing).</summary>
        private CardCopy? Give(Profile profile, uint code, Rarity rarity)
        {
            var copy = profile.Collection.NewCopy(code, rarity, CopySource.Reward);
            copy.Paid = 0;
            // A gift from the duelist's time (spec 4.2): the newest printing at that rarity by the band's era, else the newest.
            if (_printings != null) profile.Collection.Stamp(copy, CopyPrints.Shop(code, rarity, _circuits.BandEra(profile), _printings, _cards), _printings);
            return profile.Collection.Take(copy).Kept ? copy : null; // a replaced copy's DP is never paid
        }

        private static string Look(CardCopy? copy, Rarity rarity, bool showRarity) =>
            (showRarity ? Rarities.Name(rarity) + " · " : "") + (copy != null ? Grades.RawLabel(copy.Grade) : Grades.RawLabel(Grades.Fresh) + " (a 4th copy: you keep your three)");

        // ---- After a duel -------------------------------------------------------------------------

        /// <summary>
        /// The card unlocks a campaign result pays (added to <see cref="DuelReward.Grants"/> and returned). Call it after
        /// the result is recorded and before saving, in every result branch: circuits, Tag Cups, Library cups, Every Era
        /// rounds and guests. For each win the result counted (<see cref="DuelReward.WinsAdded"/>): the first pays the
        /// rival's signature (held as "giftowed" while it isn't in the shop's era), a later one in the first playthrough
        /// drops a Common or Rare from their deck 15% of the time, the 3rd offers a trade and the 5th pays the signature
        /// at its rarest printing (Kit, the shop regular, has no bond rewards). <paramref name="band"/> is the circuit's
        /// band for a one-on-one circuit duel ("" for anything else): a win there checks <paramref name="tally"/> against
        /// the band's bounties once Gus's board is up (<see cref="BountyBoard.IsOpen(Profile, Circuits, DuelReward)"/>: not
        /// before the Beginner Circuit's clear, nor on the clearing duel itself). Then owed gifts and bounties are paid, and sets checked.
        /// </summary>
        public List<Grant> AfterDuel(Profile profile, DuelReward reward, string band, DuelTally? tally, bool loaner, bool tag)
        {
            var grants = new List<Grant>();
            foreach (var (id, wins) in reward.WinsAdded) Bonds(profile, id, wins, grants, allowDrop: true);
            if (!tag && band.Length > 0 && tally != null && reward.WinsAdded.Count > 0 && (tally.Winner < 0 || tally.Won)
                && BountyBoard.IsOpen(profile, _circuits, reward))
            {
                var board = Bounties.BoardOf(band);
                foreach (var bounty in Bounties.For(band))
                    if (!profile.Unlocks.Contains("bounty:" + bounty.Id) && bounty.Met(tally, loaner))
                    {
                        profile.Unlocks.Add("bounty:" + bounty.Id);
                        grants.Add(PayBounty(profile, bounty, board));
                    }
            }
            grants.AddRange(PayOwed(profile));
            grants.AddRange(CheckSets(profile));
            reward.Grants.AddRange(grants);
            return grants;
        }

        /// <summary>The gifts, drop and bond events for one character's win count.</summary>
        private void Bonds(Profile profile, string id, int wins, List<Grant> grants, bool allowDrop)
        {
            if (wins <= 0) return;
            bool regular = id == TagPartners.ShopRegularId;
            uint sig = SignatureOf(id);
            bool gifted = false;
            if (sig != 0 && !profile.Unlocks.Contains("gift:" + id) && !profile.Unlocks.Contains("giftowed:" + id))
            {
                gifted = true;
                if (InEra(profile, sig)) grants.Add(RivalGift(profile, id, sig));
                else
                {
                    profile.Unlocks.Add("giftowed:" + id);
                    grants.Add(new Grant
                    {
                        Kind = GrantKind.RivalGift, Character = id, Code = sig, Rarity = Commonest(sig), Owed = true, CardName = Name(sig),
                        Text = $"{Who(id)}'s gift: {Name(sig)}, held at the counter until it's in the shop's era",
                    });
                }
            }
            if (!gifted && allowDrop && wins >= 2 && profile.Cycle == 0 && Drop(profile, id) is Grant drop) grants.Add(drop);
            if (regular) return;
            if (wins >= DeckBond && !profile.Unlocks.Contains("bondtrade:" + id))
            {
                profile.Unlocks.Add("bondtrade:" + id);
                grants.Add(new Grant
                {
                    Kind = GrantKind.BondTrade, Character = id, Event = $"bond:{id}:3",
                    Text = $"Bond {DeckBond} with {Who(id)}: one trade for a card from their decks (Trade, by the partner picker)",
                });
            }
            if (sig != 0 && wins >= SharpBond && !profile.Unlocks.Contains("bondgift:" + id) && !profile.Unlocks.Contains("bondgiftowed:" + id))
            {
                if (InEra(profile, sig)) grants.Add(BondGift(profile, id, sig));
                else
                {
                    profile.Unlocks.Add("bondgiftowed:" + id);
                    grants.Add(new Grant
                    {
                        Kind = GrantKind.BondGift, Character = id, Code = sig, Rarity = Rarest(sig), Owed = true, CardName = Name(sig),
                        Text = $"{Who(id)}'s bond gift: {Name(sig)}, held at the counter until it's in the shop's era",
                    });
                }
            }
        }

        private Grant RivalGift(Profile profile, string id, uint sig)
        {
            profile.Unlocks.Remove("giftowed:" + id);
            profile.Unlocks.Add("gift:" + id);
            // Three copies already: the gift is the rarest printing instead, so it upgrades your least rare copy.
            var rarity = profile.Collection.Owned(sig) >= Collection.MaxCopies ? Rarest(sig) : Commonest(sig);
            var copy = Give(profile, sig, rarity);
            return new Grant
            {
                Kind = GrantKind.RivalGift, Character = id, Code = sig, Rarity = rarity, Copy = copy, CardName = Name(sig), Event = "beat:" + id,
                Text = $"{Who(id)}'s gift: {Name(sig)} · {Look(copy, rarity, false)}",
            };
        }

        private Grant BondGift(Profile profile, string id, uint sig)
        {
            profile.Unlocks.Remove("bondgiftowed:" + id);
            profile.Unlocks.Add("bondgift:" + id);
            var rarity = Rarest(sig);
            var copy = Give(profile, sig, rarity);
            return new Grant
            {
                Kind = GrantKind.BondGift, Character = id, Code = sig, Rarity = rarity, Copy = copy, CardName = Name(sig), Event = $"bond:{id}:5",
                Text = $"{Who(id)}'s bond gift: {Name(sig)} · {Look(copy, rarity, true)}",
            };
        }

        /// <summary>The 15% drop: a card from their decks you have fewer than 3 of, ever printed at Common or Rare, in era, not no-pass.</summary>
        private Grant? Drop(Profile profile, string id)
        {
            uint seed = Hash.Mix(profile.Collection.Salt, (uint)profile.DuelsPlayed, StableHash(id));
            if (seed % 1000 >= DropPerMille) return null;
            uint sig = SignatureOf(id);
            var candidates = DeckCards(id).Where(c => c != sig && !_noPass.Contains(c) && Commonest(c) <= Rarity.NormalParallelRare
                && profile.Collection.Owned(c) < Collection.MaxCopies && InEra(profile, c) && ShopHas(profile, c)).ToList();
            if (candidates.Count == 0) return null;
            var code = candidates[(int)(Hash.Mix(seed, 0xD50Fu) % (uint)candidates.Count)];
            var rarity = Commonest(code);
            var copy = Give(profile, code, rarity);
            return new Grant
            {
                Kind = GrantKind.Drop, Character = id, Code = code, Rarity = rarity, Copy = copy, CardName = Name(code),
                Text = $"{Who(id)} slipped you a spare: {Name(code)}.",
            };
        }

        // ---- Bounties -----------------------------------------------------------------------------

        /// <summary>
        /// The card a bounty pays now: from your Recipe's missing cards (<see cref="Profile.Wanted"/>) in era and not
        /// no-pass, the one missing most copies, then the dearest in tokens, then the lowest code; 0 when there's none.
        /// </summary>
        public uint BountyCard(Profile profile) =>
            // A wishlist from one of your own recipes (an imported .ydk) pays nothing: they're a plan, not a source of cards
            // (docs/dev/YDK-IMPORT-2026-11-07.md), so a bounty then pays as if no Recipe were set.
            MyRecipes.IsMine(profile.WishlistFor) ? 0u : profile.Wanted().Where(c => !_noPass.Contains(c) && profile.Collection.Owned(c) < Collection.MaxCopies && InEra(profile, c))
                .OrderByDescending(c => profile.Wishlist[c] - profile.Collection.Count(c)).ThenByDescending(Cost).ThenBy(c => c)
                .DefaultIfEmpty(0u).First();

        private Grant PayBounty(Profile profile, Bounty bounty, string board)
        {
            var grant = new Grant { Kind = GrantKind.Bounty };
            uint code = BountyCard(profile);
            if (code != 0) return PayBountyCard(profile, code, $"Gus's bounty ({bounty.Text})", grant);
            if (!profile.Unlocks.Contains("bountytoken:" + board))
            {
                profile.Unlocks.Add("bountytoken:" + board);
                profile.Tokens++;
                grant.Kind = GrantKind.BountyToken;
                grant.Text = $"Gus's bounty ({bounty.Text}): +1 Card Token (set a Recipe and the next ones pay its cards)";
                return grant;
            }
            profile.BountyOwed++;
            grant.Owed = true;
            grant.Text = $"Gus's bounty ({bounty.Text}): owed, paid from your Recipe once you set one";
            return grant;
        }

        private Grant PayBountyCard(Profile profile, uint code, string what, Grant grant)
        {
            var rarity = Commonest(code);
            var copy = Give(profile, code, rarity);
            grant.Code = code;
            grant.Rarity = rarity;
            grant.Copy = copy;
            grant.CardName = Name(code);
            grant.Text = $"{what}: {Name(code)} from your Recipe";
            return grant;
        }

        /// <summary>Whether Gus's board is up (<see cref="BountyBoard.IsOpen(Profile, Circuits)"/>): the Bounties button shows only then.</summary>
        public bool BountiesOpen(Profile profile) => BountyBoard.IsOpen(profile, _circuits);

        /// <summary>
        /// The board for the band you're in now (the newest open circuit's): its bounties, each open or claimed; none
        /// before the board is up (<see cref="BountiesOpen"/>).
        /// </summary>
        public List<BountyRow> Board(Profile profile)
        {
            if (!BountiesOpen(profile)) return new List<BountyRow>();
            var band = BoardBand(profile);
            return Bounties.All.Where(b => b.Band == band).Select(b => new BountyRow { Bounty = b, Claimed = profile.Unlocks.Contains("bounty:" + b.Id) }).ToList();
        }

        /// <summary>The board's band now: the band of the newest circuit you've opened ("" when it has no board).</summary>
        public string BoardBand(Profile profile)
        {
            int open = Enumerable.Range(0, _circuits.All.Count).Where(i => _circuits.IsUnlocked(i, profile)).DefaultIfEmpty(-1).Max();
            return open < 0 ? "" : Bounties.BoardOf(_circuits.All[open].Band);
        }

        /// <summary>Bounties on the board now that are still open (the Bounties button's count).</summary>
        public int OpenBounties(Profile profile) => Board(profile).Count(r => !r.Claimed);

        /// <summary>What the next bounty pays, for the board: "Pays: Sangan from your Recipe" or "Pays: 1 Card Token (set a Recipe first)".</summary>
        public string BountyPays(Profile profile)
        {
            uint code = BountyCard(profile);
            if (code != 0) return $"Pays: {Name(code)} from your Recipe";
            if (MyRecipes.IsMine(profile.WishlistFor))
                return profile.Unlocks.Contains("bountytoken:" + BoardBand(profile))
                    ? "Pays: a card from an unlocked Recipe (your own recipes don't pay bounties)"
                    : "Pays: 1 Card Token (your own recipes don't pay bounties)";
            return profile.Unlocks.Contains("bountytoken:" + BoardBand(profile))
                ? "Pays: a card from your Recipe (set a Recipe first)"
                : "Pays: 1 Card Token (set a Recipe first)";
        }

        // ---- Owed, catching up --------------------------------------------------------------------

        /// <summary>
        /// Pays what's held: gifts now in the shop's era, and owed bounties while your Recipe has a card to pay. Runs at
        /// the end of <see cref="AfterDuel"/>; call it too when a Recipe is set (the deck editor) and when the campaign
        /// screen opens (then save).
        /// </summary>
        public List<Grant> PayOwed(Profile profile)
        {
            var grants = new List<Grant>();
            foreach (var key in profile.Unlocks.Where(k => k.StartsWith("giftowed:") || k.StartsWith("bondgiftowed:")).OrderBy(k => k, StringComparer.Ordinal).ToList())
            {
                bool bond = key.StartsWith("bond");
                var id = key.Substring(key.IndexOf(':') + 1);
                uint sig = SignatureOf(id);
                if (sig == 0 || !InEra(profile, sig)) continue;
                grants.Add(bond ? BondGift(profile, id, sig) : RivalGift(profile, id, sig));
            }
            while (profile.BountyOwed > 0 && BountyCard(profile) is uint code && code != 0)
            {
                profile.BountyOwed--;
                grants.Add(PayBountyCard(profile, code, "Gus's bounty, owed", new Grant { Kind = GrantKind.Bounty }));
            }
            return grants;
        }

        /// <summary>
        /// A save from before card unlocks (<see cref="Profile.UnlocksCaughtUp"/> false): pays, once, the first-win gifts
        /// for duelists it already beat, and the bond 3 trades and bond 5 gifts it already reached ("Gus kept these for
        /// you"). Call it after loading, then save; a new save never needs it. Returns nothing once it has run.
        /// </summary>
        public List<Grant> CatchUp(Profile profile)
        {
            var grants = new List<Grant>();
            if (profile.UnlocksCaughtUp) return grants;
            profile.UnlocksCaughtUp = true;
            foreach (var pair in profile.Wins.Where(w => w.Value > 0).OrderBy(w => w.Key, StringComparer.Ordinal).ToList())
                Bonds(profile, pair.Key, pair.Value, grants, allowDrop: false);
            grants.AddRange(CheckSets(profile));
            return grants;
        }

        // ---- Bond 3: one trade --------------------------------------------------------------------

        /// <summary>Whether a character's bond 3 trade is on offer (offered, not taken yet).</summary>
        public bool CanTrade(Profile profile, string id) =>
            profile.Unlocks.Contains("bondtrade:" + id) && !profile.Unlocks.Contains("bondtraded:" + id);

        /// <summary>
        /// The cards a character will trade you (base codes, dearest first): from their decks, in era, not no-pass, not
        /// their signature, and ones you have fewer than 3 of. Empty when no trade is on offer.
        /// </summary>
        public List<uint> TradeOffers(Profile profile, string id)
        {
            if (!CanTrade(profile, id)) return new List<uint>();
            uint sig = SignatureOf(id);
            return DeckCards(id).Where(c => c != sig && !_noPass.Contains(c) && profile.Collection.Owned(c) < Collection.MaxCopies && InEra(profile, c))
                .OrderByDescending(Cost).ThenBy(Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Your copies you could give for <paramref name="take"/>: worth at least its token cost, not at the Grading Desk,
        /// and spare (your saved decks still have enough without it: <paramref name="neededByDecks"/>, the copies they need).
        /// </summary>
        public List<CardCopy> TradeGives(Profile profile, uint take, Func<uint, int> neededByDecks)
        {
            int cost = Cost(take);
            return profile.Collection.All.Where(c => c.Code != take && !c.AtDesk && Cost(c.Code) >= cost && profile.Collection.Count(c.Code) - 1 >= neededByDecks(c.Code))
                .OrderBy(c => Cost(c.Code)).ThenBy(c => profile.Collection.Value(c)).ThenBy(c => c.Code).ThenBy(c => c.Id).ToList();
        }

        /// <summary>
        /// The bond 3 trade: <paramref name="give"/> goes, one copy of <paramref name="take"/> comes (Raw Near Mint at its
        /// commonest printing, paid 0). Null with the reason in <paramref name="refused"/> when it can't: no trade on
        /// offer, a card they don't offer, a copy worth fewer tokens, one at the desk, or one a saved deck needs.
        /// </summary>
        public Grant? Trade(Profile profile, string id, uint take, CardCopy give, Func<uint, int> neededByDecks, out string? refused)
        {
            refused = !CanTrade(profile, id) ? $"{Who(id)} has no trade on offer"
                : !TradeOffers(profile, id).Contains(take) ? $"{Who(id)} doesn't trade {Name(take)}"
                : !profile.Collection.CopiesOf(give.Code).Contains(give) ? "that copy isn't in your collection"
                : give.AtDesk ? "that copy is at the Grading Desk"
                : Cost(give.Code) < Cost(take) ? $"{Name(give.Code)} is worth fewer Card Tokens than {Name(take)}"
                : profile.Collection.Count(give.Code) - 1 < neededByDecks(give.Code) ? $"a saved deck needs that {Name(give.Code)}"
                : null;
            if (refused != null) return null;
            profile.Collection.Remove(give);
            profile.Unlocks.Add("bondtraded:" + id);
            var rarity = Commonest(take);
            var copy = Give(profile, take, rarity);
            return new Grant
            {
                Kind = GrantKind.BondTrade, Character = id, Code = take, Rarity = rarity, Copy = copy, CardName = Name(take),
                Text = $"Traded {Name(give.Code)} to {Who(id)} for {Name(take)} · {Look(copy, rarity, false)}",
            };
        }

        // ---- Set completion -----------------------------------------------------------------------

        /// <summary>
        /// A shop set's progress as <see cref="CheckSets"/> counts it: the set's distinct cards you own
        /// (<see cref="Collection.Owns"/>, copies at the Grading Desk included) over its distinct cards. (0, 0) for a set
        /// that isn't a real shop set (house packs, imports).
        /// </summary>
        public (int owned, int total) Progress(Profile profile, string setCode)
        {
            foreach (var (set, cards) in _sets)
                if (set.Info.Code == setCode) return (cards.Keys.Count(profile.Collection.Owns), cards.Count);
            return (0, 0);
        }

        /// <summary>
        /// The card completing the set pays (or paid: <see cref="Profile.SetsDone"/>), as <see cref="CheckSets"/> picks it:
        /// its rarest-printed card that isn't on the no-pass list, ties to the lowest code. 0 for an unknown set.
        /// </summary>
        public uint FramedCard(string setCode, Profile? profile = null)
        {
            if (profile != null && profile.SetsDone.TryGetValue(setCode, out var done)) return done;
            foreach (var (set, cards) in _sets)
                if (set.Info.Code == setCode && cards.Count > 0) return Framed(cards).code;
            return 0;
        }

        private (uint code, Rarity rarity, bool pays) Framed(Dictionary<uint, Rarity> cards)
        {
            var pick = cards.Where(c => !_noPass.Contains(c.Key)).OrderByDescending(c => c.Value).ThenBy(c => c.Key).Select(c => (code: c.Key, rarity: c.Value)).FirstOrDefault();
            return pick.code != 0 ? (pick.code, pick.rarity, true) : (cards.Keys.Min(), Rarity.Common, false);
        }

        /// <summary>
        /// Shop sets you now own every card of: each pays, once, its rarest-printed card (no-pass cards skipped; ties to the
        /// lowest code) at that rarity, records it in <see cref="Profile.SetsDone"/> and earns the set's poster. Call it
        /// after every pack opening, structure deck, trade (Card Trader and bond) and used-market buy, then save.
        /// </summary>
        public List<Grant> CheckSets(Profile profile)
        {
            var grants = new List<Grant>();
            foreach (var (set, cards) in _sets)
            {
                var code = set.Info.Code;
                if (profile.SetsDone.ContainsKey(code) || cards.Count == 0 || !cards.Keys.All(profile.Collection.Owns)) continue;
                var pick = Framed(cards);
                var framed = pick.code;
                profile.SetsDone[code] = framed;
                var grant = new Grant { Kind = GrantKind.SetComplete, Code = framed, SetCode = code, SetName = set.Info.Name, CardName = Name(framed), Event = "setcomplete:" + code };
                if (pick.pays)
                {
                    grant.Rarity = pick.rarity;
                    grant.Copy = Give(profile, pick.code, pick.rarity);
                    grant.Text = $"Set complete: {set.Info.Name}! Gus framed a {Name(framed)} for you · {Look(grant.Copy, pick.rarity, true)} · new poster";
                }
                else grant.Text = $"Set complete: {set.Info.Name}! New poster";
                grants.Add(grant);
            }
            return grants;
        }

        // ---- Story --------------------------------------------------------------------------------

        /// <summary>
        /// The story events these grants raise, in order: "beat:&lt;id&gt;", "bond:&lt;id&gt;:3", "bond:&lt;id&gt;:5",
        /// "setcomplete:&lt;set code&gt;" and "setcomplete" (any set). Add them to the result's events.
        /// </summary>
        public static List<string> Events(IEnumerable<Grant> grants)
        {
            var events = new List<string>();
            foreach (var g in grants)
            {
                if (g.Event.Length > 0 && !events.Contains(g.Event)) events.Add(g.Event);
                if (g.Kind == GrantKind.SetComplete && !events.Contains("setcomplete")) events.Add("setcomplete");
            }
            return events;
        }

        /// <summary>The grant a scene's trigger is about (for its {card} and {set}), or null.</summary>
        public static Grant? For(string trigger, IEnumerable<Grant> grants) =>
            grants.FirstOrDefault(g => g.Event.Equals(trigger, StringComparison.OrdinalIgnoreCase))
            ?? (trigger.Equals("setcomplete", StringComparison.OrdinalIgnoreCase) ? grants.FirstOrDefault(g => g.Kind == GrantKind.SetComplete) : null);
    }
}
