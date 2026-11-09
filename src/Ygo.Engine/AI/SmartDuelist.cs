using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// A generic heuristic bot that plays any deck sensibly: plus-advantage cards first, removal when
    /// it helps, summons that improve the board, attacks it can win, and disruption saved for the
    /// opponent's plays. Uses only information a player at the table would have.
    /// Anything it cannot answer well falls back to <see cref="RandomDuelist"/>.
    /// A <see cref="Personality"/> shifts its thresholds to give each character a distinct style.
    /// </summary>
    public sealed partial class SmartDuelist : IDuelist
    {
        private const int MaxActionsPerPhase = 30;
        /// <summary>Actions a phase may take: the usual cap, or a playbook's (a loop that runs long on purpose).</summary>
        private int ActionCap => _playbook?.ActionsPerPhase ?? MaxActionsPerPhase;
        /// <summary>
        /// Chaining the same effect more often than this in one turn stops: an effect that keeps getting negated (Skill
        /// Drain, Imperial Order) and costs nothing could otherwise be activated forever.
        /// </summary>
        private const int MaxSameChainPerTurn = 3;
        private readonly Dictionary<(uint code, ulong description), int> _chainedThisTurn = new Dictionary<(uint, ulong), int>();
        private const int UnknownCardValue = 1200;

        private readonly string _name;
        private readonly DuelContext _ctx;
        private readonly ICardDataProvider _cards;
        private readonly RandomDuelist _fallback;
        private readonly Action<string>? _log;
        private readonly Personality _style;
        private readonly Random _rng;
        private readonly Playbook? _playbook;
        private readonly TurnPlanner? _planner;
        private PlaybookView? _view;
        // The Extra Deck / procedure summon just chosen, so its material selection can be tied to it.
        private uint _lastSpecialSummonCode;

        private int _turnSeen = -1;
        private int _actionsThisPhase;
        private int _extraSummonsThisTurn;
        private int _respondedSerial = -1;
        private readonly Dictionary<string, int> _tries = new Dictionary<string, int>();
        private (int controller, uint sequence)? _attackTarget;
        // Battle memory for the current turn, by card (see BattleKey): monsters an attack failed to clear, and the last one attacked.
        private readonly HashSet<long> _survivors = new HashSet<long>();
        /// <summary>Our attackers whose attack we called off this turn (no target worth it: Avramax draws every attack).</summary>
        private readonly HashSet<long> _calledOff = new HashSet<long>();
        /// <summary>The zone of the attacker just declared (for its ATK while it picks a target).</summary>
        private uint? _lastAttacker;
        private long? _lastAttackedKey;

        /// <summary>
        /// The battle memory tells monsters apart by card (DuelContext.MonsterIdentity), so a new monster in a zone used
        /// earlier this turn (a Reborn Tengu that replaced itself, a fresh summon) still gets attacked, and still attacks.
        /// Off only for the A/B that measures it: then by zone, as before.
        /// </summary>
        public bool UseCardIdentity { get; set; } = true;

        /// <summary>
        /// Attack sense for monsters battle can't destroy: the text is read whatever its case ("Cannot be destroyed by
        /// battle" starts Yubel's and Spirit of Yubel's), and one whose controller takes no battle damage from it isn't
        /// worth attacking for chip damage either (Yubel burns the attacker). Off only for the A/B that measures it.
        /// </summary>
        public bool UseIndestructibleSense { get; set; } = true;

        /// <summary>
        /// Read-only reporting for the replay analyzer (docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md, section 3c): each option
        /// the bot scores, with the score it already computed and the rule that ruled it out. Null in play. The calls only
        /// pass values the decision has already worked out; they never change what the bot does.
        /// </summary>
        public Action<OptionNote>? Notes { get; set; }

        /// <summary>What the opponent shows (the planner's risk inputs), as the bot sees it now. Read-only.</summary>
        public TurnPlanner.Exposure CurrentExposure(int me) => Exposure(me);

        private void Note(byte[] response, string label, double score, string? rejected = null, (int, uint)? target = null) =>
            Notes?.Invoke(new OptionNote { Response = response, Label = label, Score = score, Rejected = rejected, Target = target });

        /// <summary>
        /// Attacks that pay off through a battle trigger: one into a monster that wears down each time it battles (B.E.S. Big
        /// Core: "if this card battled: Remove 1 counter from this card. If you cannot, destroy it"), and one by a monster that
        /// tags out after battling (Gladiator Beast: "At the end of the Battle Phase, if this card attacked or was attacked: ...
        /// Special Summon 1 "Gladiator Beast" monster from your Deck"), while the Deck still holds one. Off only for its A/B.
        /// </summary>
        public bool UseBattleTriggerSense { get; set; } = true;

        /// <summary>The key a monster is kept under in the battle memory: its card identity, or (UseCardIdentity off) its zone.</summary>
        private long BattleKey(int controller, uint seq) => UseCardIdentity ? _ctx.MonsterIdentity(controller, seq) : seq;

        public SmartDuelist(string name, int seed, DuelContext context, Action<string>? log = null, Personality? personality = null,
            Playbook? playbook = null, TurnPlanner? planner = null)
        {
            _name = name;
            _playbook = playbook;
            _planner = planner;
            _style = personality ?? Personality.Balanced;
            _rng = new Random(seed ^ 0x5EED);
            _ctx = context;
            _cards = context.Cards;
            _fallback = new RandomDuelist(name, seed, context.Cards, log);
            _log = log;
        }

        public void OnPhaseChanged()
        {
            _actionsThisPhase = 0;
            _tries.Clear();
            _fallback.OnPhaseChanged();
        }

        public byte[] Respond(Prompt prompt, int attempt)
        {
            // If the core rejected our answer, let the random bot find any legal one.
            if (attempt > 0) return _fallback.Respond(prompt, attempt - 1);

            if (Board.Turn != _turnSeen)
            {
                _turnSeen = Board.Turn;
                _extraSummonsThisTurn = 0;
                _survivors.Clear();
                _attackedThisTurn.Clear();
                _calledOff.Clear();
                _lastAttackedKey = null;
                _chainedThisTurn.Clear();
            }

            if (_playbook != null)
            {
                _view ??= new PlaybookView(_ctx, prompt.Player);
                _playbook.Attach(_view);
            }

            // Lower-skill characters make beginner's mistakes on judgement calls: they miss chances to respond, play
            // their turn in a random order, and pick a decent card rather than the best one. They never pick a move a
            // human wouldn't (negating their own card, destroying their own Spell).
            if (IsJudgementCall(prompt) && _rng.NextDouble() < (1 - _style.Skill) * 0.6 && Sloppy(prompt) is byte[] sloppy)
                return Noted(prompt, sloppy);

            return Noted(prompt, prompt switch
            {
                IdlePrompt p => Idle(p),
                BattlePrompt p => Battle(p),
                ChainPrompt p => Chain(p),
                SelectCardPrompt p => SelectCards(p),
                TributePrompt p => Tribute(p),
                SelectUnselectPrompt p => SelectUnselect(p),
                SumPrompt p => PlannedSum(p) ?? _fallback.Respond(p, 0),
                EffectYesNoPrompt p => EffectYesNo(p),
                YesNoPrompt _ => Responses.YesNo(true),
                OptionPrompt p => Responses.Int32(_playbook?.OptionIndex(p.Options) ?? 0),
                PositionPrompt p => Position(p),
                PlacePrompt p => Place(p),
                AnnounceCardPrompt p => AnnounceCard(p) ?? _fallback.Respond(p, 0),
                AnnounceNumberPrompt p => Responses.Int32(p.Options.Count - 1),
                _ => _fallback.Respond(prompt, 0),
            });
        }

        /// <summary>The card we're activating, for its target selections (its chain link doesn't exist until they're made).</summary>
        private uint _activatingCode;
        /// <summary>The card we're summoning from the idle menu, for where it goes (Mekk-Knights care about columns).</summary>
        private uint _summoningCode;

        private byte[] Noted(Prompt prompt, byte[] response)
        {
            if (response.Length < 4) return response;
            int value = BitConverter.ToInt32(response, 0);
            if (prompt is IdlePrompt ip && (IdleAction)(value & 0xffff) == IdleAction.Activate && (value >> 16) < ip.Activatable.Count)
                _activatingCode = ip.Activatable[value >> 16].Card.Code;
            if (prompt is IdlePrompt sp)
            {
                var action = (IdleAction)(value & 0xffff);
                int index = value >> 16;
                _summoningCode = action == IdleAction.SpecialSummon && index < sp.SpSummonable.Count ? sp.SpSummonable[index].Code
                    : action == IdleAction.Summon && index < sp.Summonable.Count ? sp.Summonable[index].Code : 0;
            }
            else if (prompt is ChainPrompt cp && value >= 0 && value < cp.Chains.Count)
                _activatingCode = cp.Chains[value].Card.Code;
            return response;
        }

        /// <summary>A beginner's version of a decision, or null to play it properly.</summary>
        private byte[]? Sloppy(Prompt prompt)
        {
            switch (prompt)
            {
                case ChainPrompt p:
                    return p.Forced ? null : Responses.NoChain(); // missed the window
                case IdlePrompt p:
                {
                    // Something reasonable, in a random order: a summon or set, or an activation the bot would make anyway.
                    var options = new List<byte[]>();
                    for (int i = 0; i < p.Summonable.Count; i++) options.Add(Responses.Idle(IdleAction.Summon, i));
                    for (int i = 0; i < p.MonsterSettable.Count; i++) options.Add(Responses.Idle(IdleAction.SetMonster, i));
                    bool main1 = Board.Phase == Phase.Main1;
                    for (int i = 0; i < p.Activatable.Count; i++)
                        if (MainPhaseActivationScore(p.Activatable[i], p.Player, main1) > 0) options.Add(Responses.Idle(IdleAction.Activate, i));
                    options.RemoveAll(o => Blunder(p, o, p.Player) != null);
                    return options.Count == 0 ? null : options[_rng.Next(options.Count)];
                }
                case SelectCardPrompt p:
                {
                    // A decent card: one from the better half, not always the best.
                    var hint = _ctx.PromptHint[p.Player & 1];
                    if (hint == DuelContext.HintAttackTarget) return null;
                    var ranked = Rank(p.Cards, hint, p.Player);
                    int count = DesiredCount(hint, p.Min, p.Max, p.Cards, p.Player);
                    if (count != 1 || ranked.Count < 2) return null;
                    var good = ranked.Take(Math.Max(1, (ranked.Count + 1) / 2)).Where(i => SelectionScore(p.Cards[i], hint, p.Player) > 0).ToList();
                    return good.Count == 0 ? null : Responses.Cards(new[] { good[_rng.Next(good.Count)] });
                }
                default:
                    return null;
            }
        }

        private static bool IsJudgementCall(Prompt prompt) =>
            prompt is IdlePrompt || prompt is BattlePrompt || prompt is ChainPrompt
            || prompt is SelectCardPrompt || prompt is SelectUnselectPrompt || prompt is TributePrompt;

        // ---------------------------------------------------------------- Personality-driven thresholds

        private double Knob(double low, double high, double knob) => Personality.Lerp(low, high, knob);

        /// <summary>An attacker must beat this ATK before it will swing into a face-down monster.</summary>
        private double SetMonsterAttackThreshold => Knob(1900, 1100, _style.Aggression);
        /// <summary>Positive for aggressive characters: they summon in attack position against bigger monsters.</summary>
        private double AttackPositionBias => 500 * (_style.Aggression - _style.Caution);
        private double BoardWipeMargin => Knob(2500, 0, _style.Spending);
        private double MinimumRemovalTarget => Knob(2500, 1, _style.Spending);
        private double ExtraDeckUpgradeMargin => Knob(1200, 0, _style.Spending);
        private int ExtraSummonsPerTurn => (int)Math.Round(Knob(2, 6, _style.Spending));
        private double HalfLifePointThreat => Knob(2600, 1200, _style.Spending);
        /// <summary>Patient characters ignore minor plays and keep disruption for real threats.</summary>
        private double DisruptionThreshold => Knob(0, 1600, _style.Patience);

        // ---------------------------------------------------------------- Main Phase

        private byte[] Idle(IdlePrompt p)
        {
            int me = p.Player;
            _lastSpecialSummonCode = 0;

            // Facing a taxing negator (Light and Darkness Dragon, Apollousa): feed it bait before anything that matters.
            if (_actionsThisPhase < ActionCap && TaxBait(p, me, dryRun: false, out var bait) is byte[] baitResponse)
            {
                _actionsThisPhase++;
                Decide(bait);
                return baitResponse;
            }

            // Skilled characters look ahead: the planner tests each action in a scratch copy of the duel and
            // overrides the normal choice only when its line is clearly better. (Not in the middle of a combo route:
            // the route is the line.)
            if (_planner != null && Board.TurnPlayer == me && Board.Phase == Phase.Main1 && _actionsThisPhase < ActionCap
                && _playbook?.RouteStepLocked(p) != true && _playbook?.RuleLocked(p) != true)
            {
                var usual = IdleCore(p, dryRun: true);
                var planned = _planner.Choose(p, me, usual, Exposure(me), out var why, Board.Turn);
                if (planned != null && Blunder(p, planned, me) is string notThat)
                {
                    Decide($"(planned line rejected: {notThat})");
                    planned = null;
                }
                if (planned != null)
                {
                    _actionsThisPhase++;
                    NoteSpecialSummon(planned, p);
                    Decide($"(planned) {why}");
                    return planned;
                }
            }
            return IdleCore(p, dryRun: false);
        }

        /// <summary>The normal Main Phase choice. With <paramref name="dryRun"/> it only reports it, changing no state.</summary>
        private byte[] IdleCore(IdlePrompt p, bool dryRun)
        {
            int me = p.Player;
            bool main1 = Board.Phase == Phase.Main1;

            // Deck knowledge next: the playbook's combo rules. Draw / search cards still go before the combo,
            // since they may find more pieces.
            // (Only ones the bot will really play: one it holds back as a blunder mustn't stall the combo all phase.)
            bool advantageAvailable = Enumerable.Range(0, p.Activatable.Count).Any(i =>
                p.Activatable[i] is var o && _cards.Get(o.Card.Code) is CardInfo info && (info.Type & CardType.Monster) == 0
                && CardTraits.Of(info).Has(Trait.Advantage) && _playbook?.Vetoes(IdleAction.Activate, o.Card.Code, o.Description) != true
                && Blunder(p, Responses.Idle(IdleAction.Activate, i), me) == null);
            if (_playbook != null && (!advantageAvailable || _playbook.RouteStepReady(p)) && _actionsThisPhase < ActionCap)
            {
                var planned = _playbook.Idle(p, commit: !dryRun, out var plan);
                if (planned != null && Blunder(p, planned, me) is string notThat)
                {
                    if (!dryRun) Decide($"(playbook step skipped: {notThat})");
                    planned = null;
                }
                if (planned != null)
                {
                    if (dryRun) return planned;
                    _actionsThisPhase++;
                    NoteSpecialSummon(planned, p);
                    Decide($"[{plan}]");
                    return planned;
                }
            }
            if (!dryRun && _playbook != null && Environment.GetEnvironmentVariable("YGO_DEBUG_PLAYBOOK") == "1")
                Decide($"(no rule) spsummonable: {string.Join(", ", p.SpSummonable.Select(c => Name(c.Code)))}; activatable: {string.Join(", ", p.Activatable.Select(o => Name(o.Card.Code)))}");
            double bestScore = 0;
            byte[]? best = null;
            string bestKey = "", bestWhat = "";

            void Consider(double score, string key, byte[] response, string what)
            {
                if (score <= bestScore) { if (Notes != null) Note(response, what, score); return; }
                if (Blunder(p, response, me) is string blunder) { if (Notes != null) Note(response, what, score, blunder); return; }
                if (_tries.TryGetValue(key, out var n) && n >= 2) { if (Notes != null) Note(response, what, score, "it did nothing useful twice"); return; } // stop
                if (Notes != null) Note(response, what, score);
                bestScore = score;
                best = response;
                bestKey = key;
                bestWhat = what;
            }

            if (_actionsThisPhase < ActionCap)
            {
                _spellTrapWipeReady = false;
                _spellTrapWipeReady = p.Activatable.Any(o => _cards.Get(o.Card.Code) is CardInfo w && CardTraits.Of(w).Has(Trait.SpellTrapRemoval)
                                                             && DestroysAllSpellTraps(w) && MainPhaseActivationScore(o, me, main1) > 0);
                // The opponent has a face-up monster that answers any activation by removing a monster (S:P Little Knight)
                // and hasn't used it this turn: attack first, and play the Spells the attack doesn't need afterwards.
                string? answer = UseActivationWindowSense && _planner == null && main1 && Board.TurnPlayer == me && p.CanBattlePhase
                                 && AttackPaysNow(me) ? ActivationAnswer(me) : null;
                for (int i = 0; i < p.Activatable.Count; i++)
                {
                    var o = p.Activatable[i];
                    if (_playbook?.Vetoes(IdleAction.Activate, o.Card.Code, o.Description) == true) continue;
                    if (Pump(_cards.Get(o.Card.Code)) != null) continue; // ATK pumps wait for the battle they turn
                    var score = MainPhaseActivationScore(o, me, main1);
                    if (answer != null && score > 0 && WaitsForBattle(o, me))
                    {
                        if (Notes != null) Note(Responses.Idle(IdleAction.Activate, i), $"activates {Name(o.Card.Code)}", score,
                            $"waits until after the Battle Phase: {answer} would answer it");
                        continue;
                    }
                    if (score > 0)
                        Consider(score, $"act:{o.Card.Code}:{o.Description}", Responses.Idle(IdleAction.Activate, i), $"activates {Name(o.Card.Code)}");
                }

                ConsiderNormalSummon(p, me, Consider);

                for (int i = 0; i < p.SpSummonable.Count; i++)
                {
                    var c = p.SpSummonable[i];
                    var info = _cards.Get(c.Code);
                    if (info == null || _playbook?.Vetoes(IdleAction.SpecialSummon, c.Code) == true) continue;
                    double score;
                    if (c.Location == Location.Extra)
                    {
                        // Extra Deck summons spend several monsters for one, so only make them when they
                        // break through a wall or clearly upgrade the board.
                        int myBest = MyBestAttack(me), oppWall = OpponentBestAttack(me);
                        bool breaksWall = info.Attack > oppWall && myBest <= oppWall && HasFaceUpMonsters(1 - me);
                        bool upgrade = info.Attack >= myBest + ExtraDeckUpgradeMargin;
                        // Many Extra Deck monsters earn their place with effects, not ATK (Zoodiac Xyz have "?" ATK,
                        // Broadbull searches, Dracossack destroys, Dante mills): those are worth making in Main Phase 1
                        // even when their printed ATK is low. Spent-material and once-per-turn limits still apply.
                        var traits = CardTraits.Of(info);
                        // Not Link monsters: a Link spends its materials outright, and the generic bot links badly
                        // (rating it lost Link-heavy decks a lot); deck playbooks handle Link lines.
                        bool engine = main1 && (info.Type & CardType.Link) == 0
                                      && (traits & (Trait.Advantage | Trait.MonsterRemoval | Trait.SpellTrapRemoval
                                                    | Trait.Negate | Trait.SpecialSummon)) != 0;
                        if (_extraSummonsThisTurn >= ExtraSummonsPerTurn) continue;
                        if (!engine && (info.Attack < 1500 || !(breaksWall || upgrade))) continue;
                        score = 65 + info.Attack / 1000.0 + (engine ? 1.5 : 0);
                    }
                    else
                    {
                        score = 68 + info.Attack / 1000.0;
                    }
                    Consider(score, $"sp:{c.Code}", Responses.Idle(IdleAction.SpecialSummon, i), $"Special Summons {info.Name}");
                }

                int freeSpellZones = 5 - Board.Players[me].SpellTraps.Take(5).Count(c => c != null);
                for (int i = 0; i < p.SpellSettable.Count && freeSpellZones > 0; i++)
                {
                    var info = _cards.Get(p.SpellSettable[i].Code);
                    if (info == null || _playbook?.Vetoes(IdleAction.SetSpellTrap, info.Code) == true) continue;
                    var traits = CardTraits.Of(info);
                    bool worthSetting = (info.Type & CardType.Trap) != 0 || traits.Has(Trait.QuickPlay);
                    if (worthSetting)
                        Consider(30, $"set:{info.Code}:{i}", Responses.Idle(IdleAction.SetSpellTrap, i), $"sets {info.Name}");
                }

                int oppBest = OpponentBestAttack(me);
                for (int i = 0; i < p.Repositionable.Count; i++)
                {
                    var c = p.Repositionable[i];
                    var live = Live(c);
                    var info = _cards.Get(c.Code);
                    if (live == null || info == null) continue;
                    if (!live.IsFaceUp && (info.Attack >= oppBest || main1))
                        Consider(45, $"flip:{c.Sequence}", Responses.Idle(IdleAction.ChangePosition, i), $"Flip Summons {info.Name}");
                    else if (live.IsFaceUp && live.IsDefense && main1 && live.Attack > oppBest)
                        Consider(40, $"pos:{c.Sequence}", Responses.Idle(IdleAction.ChangePosition, i), $"switches {info.Name} to attack");
                    else if (live.IsFaceUp && !live.IsDefense && !main1 && live.Attack < oppBest)
                        Consider(35, $"pos:{c.Sequence}", Responses.Idle(IdleAction.ChangePosition, i), $"switches {info.Name} to defense");
                }
            }

            if (best != null)
            {
                if (dryRun) return best;
                _tries[bestKey] = _tries.TryGetValue(bestKey, out var n) ? n + 1 : 1;
                _actionsThisPhase++;
                if (bestKey.StartsWith("sp:"))
                {
                    _lastSpecialSummonCode = uint.Parse(bestKey.Substring(3));
                    if (Location.Extra == p.SpSummonable.FirstOrDefault(c => $"sp:{c.Code}" == bestKey).Location)
                        _extraSummonsThisTurn++;
                }
                Decide(bestWhat);
                return best;
            }

            if (main1 && p.CanBattlePhase && HasAttackers(me))
            {
                if (!dryRun) Decide("goes to the Battle Phase");
                return Responses.Idle(IdleAction.ToBattlePhase);
            }
            if (p.CanEndPhase)
            {
                if (!dryRun) Decide("ends the turn");
                return Responses.Idle(IdleAction.ToEndPhase);
            }
            return Responses.Idle(IdleAction.ToBattlePhase);
        }

        /// <summary>Remembers a Special Summon just chosen, so its material selection can follow the playbook.</summary>
        private void NoteSpecialSummon(byte[] response, IdlePrompt p)
        {
            var chosen = BitConverter.ToInt32(response, 0);
            if ((chosen & 0xffff) != (int)IdleAction.SpecialSummon) return;
            var card = p.SpSummonable[chosen >> 16];
            _lastSpecialSummonCode = card.Code;
            if (card.Location == Location.Extra) _extraSummonsThisTurn++;
        }

        private double MainPhaseActivationScore(ChainOption o, int me, bool main1)
        {
            var info = _cards.Get(o.Card.Code);
            if (info == null) return 30;
            var t = CardTraits.Of(info);
            bool monster = (info.Type & CardType.Monster) != 0;
            int opp = 1 - me;

            if (monster)
            {
                // Hand traps are for the opponent's turn; field/GY/hand ignition effects are value.
                if (o.Card.Location == Location.Hand && t.Has(Trait.HandTrap)) return 0;
                return 70;
            }

            // A set draw Trap waits for the end of their turn (see Chain); one in hand is Set by the usual rules.
            if (DrawTrap(info, t) && o.Card.Location == Location.SZone) return 0;
            if (t.Has(Trait.Advantage) || RecoversNow(info, t)) return 95;
            // Wipes go before anything we summon this turn.
            if (t.Has(Trait.BoardWipe))
                return SideValue(opp, me) > SideValue(me, me) + BoardWipeMargin ? 97 : 0;
            if (t.Has(Trait.MonsterRemoval))
            {
                if (!HasMonsters(opp) || SideValue(opp, me) < MinimumRemovalTarget) return 0;
                if (!t.Has(Trait.OneSided) && SideValue(me, me) >= SideValue(opp, me)) return 0;
                return 80;
            }
            if (t.Has(Trait.SpellTrapRemoval))
            {
                // A card that clears every Spell/Trap is worth it when the opponent has more of them; then it goes first
                // and one-for-one removal waits (no MST before Heavy Storm).
                if (DestroysAllSpellTraps(info))
                    return SpellTrapCount(opp) > SpellTrapCount(me) - 1 && HasSpellTraps(opp) ? 78 : 0;
                if (!HasSpellTraps(opp) || _spellTrapWipeReady) return 0;
                return 75;
            }
            if (t.Has(Trait.Revive)) return BestGraveMonsterAttack() >= 1500 ? 88 : 0;
            if (t.Has(Trait.SpecialSummon)) return 72;

            bool isTrap = (info.Type & CardType.Trap) != 0;
            // A Quick-Play whose whole effect is Setting cards from the Deck (Set Rotation: the deck's Field Spells) is deck
            // advantage, not a trick: held for the opponent's turn it only comes too late for this turn's plays.
            // Only into an empty Field Zone: a Field Spell Set over our own face-up one sends ours to the GY (Nightmare Throne,
            // Necrovalley, the Magnificent Map).
            if (UseDeckSetSense && t.Has(Trait.QuickPlay) && !isTrap && SetsFromDeck.IsMatch(info.Text)
                && (Board.Players[me].SpellTraps.Count <= 5 || Board.Players[me].SpellTraps[5] == null)) return 90;
            if (t.Has(Trait.Negate) && t.Has(Trait.OneSided) && !isTrap && !t.Has(Trait.QuickPlay))
                return main1 && HasFaceUpMonsters(opp) ? 60 : 0; // e.g. normal spells that negate the opponent's board
            if (t.Has(Trait.Negate) || isTrap || t.Has(Trait.QuickPlay)) return 0; // hold for the opponent's turn
            return 50; // equips, field and continuous spells, Fusion spells
        }

        /// <summary>
        /// A Normal (or Continuous, Field...) Spell that returns cards from the GY to the hand for nothing in hand (The Warrior
        /// Returning Alive: the owner's Vera summoned Gearfried rather than fetch Goblin Attack Force first) is played in
        /// the Main Phase like a draw or a search (UseRecoverySense). Only that Main Phase score: Traps and Quick-Plays
        /// keep their timing, the playbook's gate, chain windows, GY effects and card values read the plain Advantage trait.
        /// </summary>
        private bool RecoversNow(CardInfo info, Trait t) =>
            UseRecoverySense && t.Has(Trait.Recover) && (info.Type & CardType.Trap) == 0 && !t.Has(Trait.QuickPlay);

        /// <summary>Spells/Traps that return cards from the GY to the hand count as advantage in the Main Phase (see RecoversNow). Off only for its A/B.</summary>
        public bool UseRecoverySense { get; set; } = true;

        /// <summary>A card whose text opens by Setting cards from the Deck ("Set 2 Field Spells ... from your Deck": Set Rotation).</summary>
        private static readonly System.Text.RegularExpressions.Regex SetsFromDeck = new System.Text.RegularExpressions.Regex(
            @"^Set (?:1|2|up to \d)\b[^.]*?from your Deck", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Plays a Quick-Play that only Sets cards from the Deck in the Main Phase (see SetsFromDeck). Off only for its A/B.</summary>
        public bool UseDeckSetSense { get; set; } = true;

        /// <summary>
        /// A face-up Quick Effect that answers any activation of ours by removing a monster ("When your opponent activates a
        /// card or effect (Quick Effect): You can target 2 face-up monsters on the field ...; banish both": S:P Little Knight).
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex RemovesOnActivation = new System.Text.RegularExpressions.Regex(
            @"When (?:your opponent activates a card or effect|a card or effect is activated|another card or effect is activated|your opponent activates a (?:Spell|Trap|Spell/Trap)(?: Card)?(?: or effect)?) \(Quick Effect\): You can (?:target [^;.]*?(?:monsters?|cards?) (?:on the field|your opponent controls)[^;.]*;\s*(?:banish|destroy|return|shuffle)|(?:(?!this card)[^.;])*;\s*(?:banish|destroy|return|shuffle) [^.]*?(?:on the field|your opponent controls))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Attacks before optional Spells while the opponent shows an unused on-activation answer (see RemovesOnActivation). Off only for its A/B.</summary>
        public bool UseActivationWindowSense { get; set; } = true;

        /// <summary>The name of the opponent's face-up, unnegated monster that would answer an activation and hasn't been used this turn, or null.</summary>
        private string? ActivationAnswer(int me)
        {
            int opp = 1 - me;
            foreach (var (seq, card) in Monsters(opp))
                if (card.IsFaceUp && !card.IsNegated && _cards.Get(card.Code) is CardInfo info && RemovesOnActivation.IsMatch(info.Text)
                    && !_ctx.MonsterActivatedThisTurn(opp, seq))
                    return info.Name;
            return null;
        }

        /// <summary>One of our face-up Attack Position monsters has an attack worth making now (or the opponent has no monsters).</summary>
        private bool AttackPaysNow(int me)
        {
            int opp = 1 - me;
            var targets = Monsters(opp).ToList();
            foreach (var (seq, mine) in Monsters(me))
            {
                if (!mine.IsFaceUp || mine.IsDefense) continue;
                if (targets.Count == 0) return true;
                int value = MonsterValue(mine, me, me, (int)seq);
                if (targets.Any(t => AttackBenefit(mine.Attack, t.card, value, me, t.seq, mine) > 0)) return true;
            }
            return false;
        }

        /// <summary>
        /// A Spell/Trap the attack doesn't need, so it can wait for Main Phase 2: not monster removal, a wipe, a negation, a
        /// summon, an Equip or Field Spell, or an ATK pump (those clear or help the attack).
        /// </summary>
        private bool WaitsForBattle(ChainOption o, int me)
        {
            if (_cards.Get(o.Card.Code) is not CardInfo info || (info.Type & CardType.Monster) != 0) return false;
            if ((info.Type & (0x40000 | 0x80000)) != 0 || Pump(info) != null) return false; // Equip, Field
            var t = CardTraits.Of(info);
            // Spell/Trap removal clears the way while the opponent has set cards (Heavy Storm, MST before the attack).
            if (t.Has(Trait.SpellTrapRemoval) && OpponentSetSpellTraps(me) > 0) return false;
            return (t & (Trait.MonsterRemoval | Trait.BoardWipe | Trait.Negate | Trait.SpecialSummon | Trait.Revive)) == 0;
        }

        private void ConsiderNormalSummon(IdlePrompt p, int me, Action<double, string, byte[], string> consider)
        {
            int opp = 1 - me;
            int oppBest = OpponentBestAttack(me);
            var myMonsterValues = Monsters(me).Select(m => MonsterValue(m.card, me, me, (int)m.seq)).OrderBy(v => v).ToList();

            for (int i = 0; i < p.Summonable.Count; i++)
            {
                var c = p.Summonable[i];
                var info = _cards.Get(c.Code);
                if (info == null || _playbook?.Vetoes(IdleAction.Summon, c.Code) == true) continue;
                var t = CardTraits.Of(info);
                int tributes = info.Level >= 7 ? 2 : info.Level >= 5 ? 1 : 0;
                if (tributes > myMonsterValues.Count) continue;
                int cost = myMonsterValues.Take(tributes).Sum();
                if (tributes > 0 && info.Attack - cost <= 300) continue;

                if (t.Has(Trait.HandTrap)) continue; // a hand trap is worth far more in hand than as a body
                double score = 60 + info.Attack / 1000.0 + (t.Has(Trait.OnSummonTrigger) ? 2 : 0);
                // Low on LP, nobody risks a weak monster in Attack Position: it gets attacked and the damage could be fatal.
                bool lowLp = Board.Players[me].LifePoints <= Math.Max(2000, oppBest);
                double bias = lowLp ? Math.Min(0, AttackPositionBias) : AttackPositionBias;
                bool attackWorthy = info.Attack + bias >= oppBest || !HasMonsters(opp) || (t.Has(Trait.OnSummonTrigger) && !lowLp);
                if (attackWorthy)
                {
                    consider(score, $"ns:{c.Code}", Responses.Idle(IdleAction.Summon, i), $"Normal Summons {info.Name}");
                    continue;
                }
                int set = p.MonsterSettable.FindIndex(m => m.Code == c.Code && m.Sequence == c.Sequence);
                if (set >= 0)
                    consider(score - 5, $"mset:{c.Code}", Responses.Idle(IdleAction.SetMonster, set), $"sets {info.Name}");
            }
        }

        // ---------------------------------------------------------------- Battle Phase

        private byte[] Battle(BattlePrompt p)
        {
            int me = p.Player, opp = 1 - me;
            var targets = Monsters(opp).ToList();

            // If the monster we just attacked is still standing, attacking it again won't help this turn.
            if (_lastAttackedKey is long attacked && targets.FirstOrDefault(t => BattleKey(opp, t.seq) == attacked) is var (survivorSeq, survivor)
                && survivor != null && !(UseBattleTriggerSense && KnownMonster(survivor, opp, survivorSeq) is CardInfo worn && WearsDown.IsMatch(worn.Text)))
                _survivors.Add(attacked);
            _lastAttackedKey = null;

            var usual = BattleCore(p, out var usualTarget, out var usualWhat);
            // Skilled characters test each attack (and stopping) in a scratch copy of the duel.
            if (_planner != null && _planner.PlanBattle && Board.TurnPlayer == me)
            {
                var planned = _planner.ChooseAttack(p, me, usual, usualTarget, Exposure(me), Board, out var target, out var why,
                    p.Attackers.Select(a => a.Sequence).Where(seq => _calledOff.Contains(BattleKey(me, seq))).ToList());
                if (planned != null)
                {
                    _attackTarget = target;
                    Decide($"(planned) {why}");
                    NoteAttacker(p, planned);
                    return planned;
                }
            }
            _attackTarget = usualTarget;
            Decide(usualWhat);
            NoteAttacker(p, usual);
            return usual;
        }

        private void NoteAttacker(BattlePrompt p, byte[] response)
        {
            int value = BitConverter.ToInt32(response, 0);
            int index = value >> 16;
            _lastAttacker = (BattleAction)(value & 0xffff) == BattleAction.Attack && index < p.Attackers.Count ? p.Attackers[index].Sequence : null;
            if (_lastAttacker is uint seq) _attackedThisTurn.Add(BattleKey(p.Player, seq));
        }

        /// <summary>Our monsters (see BattleKey) that have declared an attack this turn.</summary>
        private readonly HashSet<long> _attackedThisTurn = new HashSet<long>();

        /// <summary>The normal Battle Phase choice and attack target, without changing any state.</summary>
        private byte[] BattleCore(BattlePrompt p, out (int controller, uint sequence)? target, out string what)
        {
            int me = p.Player, opp = 1 - me;
            target = null;
            var targets = Monsters(opp).ToList();

            var order = Enumerable.Range(0, p.Attackers.Count)
                .OrderByDescending(i => Live(p.Attackers[i])?.Attack ?? 0)
                .ToList();
            foreach (var i in order)
            {
                var attacker = p.Attackers[i];
                if (_calledOff.Contains(BattleKey(me, attacker.Sequence))) continue;
                var live = Live(attacker);
                int atk = live?.Attack ?? 0;
                if (targets.Count == 0 || p.CanDirectAttack[i])
                {
                    what = $"attacks directly with {Name(attacker.Code)}";
                    if (Notes != null) Note(Responses.Battle(BattleAction.Attack, i), what, atk);
                    return Responses.Battle(BattleAction.Attack, i);
                }

                int attackerValue = live == null ? 0 : MonsterValue(live, me, me);
                // Cautious characters only attack into set Spell/Traps when the payoff is big.
                double bestBenefit = _style.Caution * 180 * OpponentSetSpellTraps(me);
                foreach (var (seq, card) in targets)
                {
                    if (_survivors.Contains(BattleKey(opp, seq)))
                    {
                        if (Notes != null) Note(Responses.Battle(BattleAction.Attack, i), $"attacks {Name(card.Code)} with {Name(attacker.Code)}", 0,
                            "it survived an attack this turn", (opp, seq));
                        continue;
                    }
                    var benefit = AttackBenefit(atk, card, attackerValue, me, seq, live);
                    if (Notes != null) Note(Responses.Battle(BattleAction.Attack, i), $"attacks {Name(card.Code)} with {Name(attacker.Code)}", benefit,
                        benefit > bestBenefit ? null : $"not worth it (needs over {bestBenefit:0})", (opp, seq));
                    if (benefit > bestBenefit)
                    {
                        bestBenefit = benefit;
                        target = (opp, seq);
                    }
                }
                // Not worth it as it stands, but a pump in hand (Aleister: +1000 to a Fusion Monster) gets over the top: attack,
                // and the pump goes in during the battle (PumpTurnsBattle).
                int pumped = live == null ? 0 : PumpInHand(me, live);
                if (target == null && pumped > 0)
                    foreach (var (seq, card) in targets)
                    {
                        if (_survivors.Contains(BattleKey(opp, seq))) continue;
                        var benefit = AttackBenefit(atk + pumped, card, attackerValue, me, seq, live);
                        if (benefit > bestBenefit)
                        {
                            bestBenefit = benefit;
                            target = (opp, seq);
                        }
                    }
                if (target != null)
                {
                    what = $"attacks with {Name(attacker.Code)}";
                    return Responses.Battle(BattleAction.Attack, i);
                }
            }

            if (!p.CanMain2 && !p.CanEndPhase && p.Attackers.Count > 0)
            {
                // A forced attack (a card says monsters must attack, as Tenpai Dragon's do): no attack looked good, so
                // the strongest attacker goes at the target it fares best against (or directly).
                int i = order[0];
                var live = Live(p.Attackers[i]);
                int atk = live?.Attack ?? 0, attackerValue = live == null ? 0 : MonsterValue(live, me, me);
                if (targets.Count > 0 && !p.CanDirectAttack[i])
                    target = (opp, targets.OrderByDescending(t => AttackBenefit(atk, t.card, attackerValue, me, t.seq, live)).First().seq);
                what = $"must attack: attacks with {Name(p.Attackers[i].Code)}";
                return Responses.Battle(BattleAction.Attack, i);
            }
            if (p.CanMain2)
            {
                what = p.Attackers.Count == 0 ? "goes to Main Phase 2 (nothing can attack)" : $"goes to Main Phase 2 ({p.Attackers.Count} could attack)";
                return Responses.Battle(BattleAction.ToMain2);
            }
            what = "ends the turn";
            return Responses.LeaveBattle(p);
        }

        /// <summary>What the opponent shows publicly: cards in hand and set Spell/Traps.</summary>
        private TurnPlanner.Exposure Exposure(int me) => new TurnPlanner.Exposure
        {
            OpponentHand = Board.Players[1 - me].Hand.Count,
            OpponentSetCards = OpponentSetSpellTraps(me),
            OpponentShownInterruptions = _ctx.InterruptionsShown[(1 - me) & 1] > 0,
        };

        /// <summary>
        /// Its controller takes no battle damage from it, in any case: "You take no (battle) damage from battles involving this
        /// card" (Yubel), "Neither player takes (any) battle damage from attacks involving this card" (Number F0: Utopic Future,
        /// Sandaion), "... this Attack Position card" (Metaion).
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex NoBattleDamage = new System.Text.RegularExpressions.Regex(
            @"(?:\byou take no|neither player takes(?: any)?) (?:battle )?damage from (?:battles|attacks) involving this (?:Attack Position )?card",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>It sends its controller's battle damage back to the attacker (Arahime, Ohime, Uzuhime the Manifested Mikanko, Number 92).</summary>
        private static readonly System.Text.RegularExpressions.Regex ReflectsBattleDamage = new System.Text.RegularExpressions.Regex(
            @"your opponent takes any battle damage you would have taken",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// "Cannot be destroyed by battle" as a sentence of its own, in any case (Yubel, Spirit of Yubel, The Ultimate Nightmare:
        /// "Cannot be destroyed by battle."; "..., also"; "... or card effects."). Not with a condition after it ("Cannot be
        /// destroyed by battle with a monster Special Summoned from the Extra Deck": Dogmatika Ecclesia dies to the rest) or
        /// before it ("● 3+: Cannot be destroyed by battle"; "Defense Position: ...").
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex AlwaysSurvivesBattle = new System.Text.RegularExpressions.Regex(
            @"(?:^|[.)]\s+)(?:this card )?cannot be destroyed by battle(?:[.;]|$|, (?:also|and)\b| or [^.,;]*?effects?(?:[.;]|$|, (?:also|and)\b))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline
                                                                  | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>A monster each battle wears down (B.E.S.): "if this card battled: Remove 1 counter ... If you cannot, destroy it".</summary>
        private static readonly System.Text.RegularExpressions.Regex WearsDown = new System.Text.RegularExpressions.Regex(
            @"(?:if this card battled|if this card attacks or is attacked)[^.]*?remove 1 counter[^.]*\.\s*If you cannot, destroy it",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>A monster that tags out after battling (Gladiator Beast): group 1 is the archetype it brings from the Deck.</summary>
        private static readonly System.Text.RegularExpressions.Regex TagsOut = new System.Text.RegularExpressions.Regex(
            @"At the end of the Battle Phase, if this card (?:battled|attacked or was attacked)[^.]*?Special Summon (?:1|2) ""([^""]+)"" monsters? from your Deck",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>What a tag-out after battle is worth (a fresh monster from the Deck for the one that leaves).</summary>
        private const double TagOutValue = 400;

        /// <summary>
        /// The attacker tags out after this battle, so even an attack that destroys nothing pays (Gladiator Beast): only
        /// while our Deck still holds a monster of the archetype it brings, other than itself.
        /// </summary>
        private bool TagsOutAfterBattle(FieldCard? attacker, int me)
        {
            if (!UseBattleTriggerSense || attacker == null || _cards.Get(attacker.Code) is not CardInfo info) return false;
            var m = TagsOut.Match(info.Text);
            if (!m.Success) return false;
            var archetype = m.Groups[1].Value;
            return Board.Players[me].Deck.Any(c => c != null && c.Code != attacker.Code && (c.Type & CardType.Monster) != 0
                                                   && _cards.Get(c.Code)?.Name.Contains(archetype, StringComparison.OrdinalIgnoreCase) == true);
        }

        private double AttackBenefit(int atk, FieldCard target, int attackerValue, int me, uint seq, FieldCard? attacker = null)
        {
            var benefit = AttackBenefitCore(atk, target, attackerValue, me, seq, out bool attackerSurvives);
            // A Gladiator Beast that battles and lives tags out for a fresh monster: worth it even when nothing dies, as long
            // as the attack costs nothing (no damage back from a higher DEF: the planner, playing it out, finds that swap a loss).
            if (attackerSurvives && TagsOutAfterBattle(attacker, me) && !(target.IsDefense && target.Defense > atk))
                benefit = Math.Max(benefit, TagOutValue);
            return benefit;
        }

        /// <summary>The attack's worth; <paramref name="attackerSurvives"/>: a known face-up target, and our attacker isn't destroyed.</summary>
        private double AttackBenefitCore(int atk, FieldCard target, int attackerValue, int me, uint seq, out bool attackerSurvives)
        {
            attackerSurvives = target.IsFaceUp && (target.IsDefense || atk > target.Attack);
            var known = KnownMonster(target, 1 - me, seq);
            // (The lowercase match, conditions and all, is as before; the fix adds only the unconditional sentence in any case.)
            bool always = known != null && UseIndestructibleSense && AlwaysSurvivesBattle.IsMatch(known.Text);
            bool indestructible = known != null && (known.Text.Contains("cannot be destroyed by battle") || always);
            if (!target.IsFaceUp)
            {
                if (known == null) return atk > SetMonsterAttackThreshold ? 400 : -1;
                // A face-down card we have already seen: attack only if it actually dies.
                return !indestructible && atk > known.Defense ? CardValue(known, null) : -1;
            }
            int value = MonsterValue(target, 1 - me, me);
            if (indestructible)
            {
                // Its controller takes no battle damage from it (Yubel, The Ultimate Nightmare, Spirit of Yubel, Utopic Future), or
                // we take it instead (the Manifested Mikanko): nothing to gain. Only for the unconditional sentence (Hu-Li the
                // Jewel Mikanko's needs an Equip Card).
                if (always && (NoBattleDamage.IsMatch(known!.Text) || ReflectsBattleDamage.IsMatch(known.Text))) return -1;
                // It wears down each time it battles (B.E.S.): each attack our attacker lives through takes a counter, and the
                // last one destroys it.
                if (UseBattleTriggerSense && WearsDown.IsMatch(known!.Text) && attackerSurvives)
                    return value / (target.Counters + 1.0) + (!target.IsDefense ? (atk - target.Attack) / 4.0 : -Math.Max(0, target.Defense - atk) / 2.0);
                return !target.IsDefense && atk > target.Attack ? (atk - target.Attack) / 4.0 : -1; // only chip damage
            }
            if (!target.IsDefense)
            {
                if (atk > target.Attack) return value + (atk - target.Attack) / 2.0;
                if (atk == target.Attack) return value - attackerValue + Knob(-500, 400, _style.Aggression); // a trade
                return -1;
            }
            return atk > target.Defense ? value : -1;
        }

        // ---------------------------------------------------------------- Chains

        private byte[] Chain(ChainPrompt p)
        {
            int me = p.Player, opp = 1 - me;
            if (p.Chains.Count == 0) return Responses.NoChain();

            var last = _ctx.Chain.Count > 0 ? _ctx.Chain[_ctx.Chain.Count - 1] : null;
            bool respondable = _ctx.LastEventPlayer == opp && _ctx.EventSerial != _respondedSerial;
            bool myTurn = Board.TurnPlayer == me;
            // The opponent is negating a link of ours (Ash Blossom on our search): answering that answer saves our card.
            bool theyNegateUs = last != null && last.Controller == opp && AnswersActivations(_cards.Get(last.Code))
                                && _ctx.Chain.Any(l => l.Controller == me);

            int best = -1;
            double bestScore = 0;
            for (int i = 0; i < p.Chains.Count; i++)
            {
                var o = p.Chains[i];
                if (!p.Forced && _playbook?.Vetoes(IdleAction.Activate, o.Card.Code, o.Description) == true)
                { if (Notes != null) Note(Responses.Int32(i), $"chains {Name(o.Card.Code)}", 0, "the playbook vetoes it"); continue; }
                if (!p.Forced && _chainedThisTurn.TryGetValue((o.Card.Code, o.Description), out var times) && times >= MaxSameChainPerTurn)
                { if (Notes != null) Note(Responses.Int32(i), $"chains {Name(o.Card.Code)}", 0, $"chained {times} times this turn"); continue; }
                if (!p.Forced && ChainBlunder(o, me, p.Chains.Select(c => c.Card)))
                { if (Notes != null) Note(Responses.Int32(i), $"chains {Name(o.Card.Code)}", 0, "a chain blunder"); continue; }
                var info = _cards.Get(o.Card.Code);
                var t = CardTraits.Of(info);
                bool monster = info != null && (info.Type & CardType.Monster) != 0;
                bool fromHand = o.Card.Location == Location.Hand;
                double score = 0;

                if (monster && !fromHand && !t.Has(Trait.HandTrap))
                {
                    // Field / GY monster effects: triggers and quick effects that build value.
                    score = last == null || last.Controller == me ? 50 : 45;
                }
                else if (!monster && (o.Card.Location & (Location.Grave | Location.Removed)) != 0
                         && (t.Has(Trait.SpecialSummon) || t.Has(Trait.Advantage)) && !t.Has(Trait.Negate))
                {
                    // A Spell/Trap's effect from the GY that summons or adds (an Unchained Trap destroyed while Set
                    // summons from the Deck): free value, on either turn.
                    score = 45;
                }
                else if (respondable)
                {
                    score = 70;
                    var text = info?.Text ?? "";
                    // Hand traps answer activations (Maxx "C"-style cards also answer Special Summons), not plain summons.
                    if (fromHand && _ctx.LastEvent != DuelEventKind.Activation
                        && !(_ctx.LastEvent == DuelEventKind.SpecialSummon && text.Contains("Special Summons")))
                        score = 0;
                    // Expensive answers (e.g. Solemn Judgment's half LP) are saved for real threats.
                    if (text.Contains("pay half your LP", StringComparison.OrdinalIgnoreCase) && ThreatValue(_ctx.LastEventCode) < HalfLifePointThreat) score = 0;
                    if (_ctx.LastEvent != DuelEventKind.Attack && ThreatValue(_ctx.LastEventCode) < DisruptionThreshold) score = 0;
                    if (t.Has(Trait.BoardWipe) && SideValue(me, me) >= SideValue(opp, me)) score = 0;
                    if (t.Has(Trait.MonsterRemoval) && !t.Has(Trait.Negate) && !HasMonsters(opp)) score = 0;
                    if (info != null && info.Text.Contains("opponent's GY") && (last == null || last.Location != Location.Grave))
                        score = 0; // GY disruption only answers GY effects
                    // A negate, or a GY negate like Called by the Grave (the hand trap is in the GY by now), on their negate.
                    if (theyNegateUs && (AnswersActivations(info) || NegatesFromGrave(info))) score = 80;
                }
                else if (last == null && t.Has(Trait.Advantage) && myTurn && !t.Has(Trait.HandTrap))
                {
                    // A set Trap that only draws (Jar of Greed) waits for the end of their turn: a bluff until then.
                    score = DrawTrap(info, t) ? 0 : 40;
                }
                else if (last == null && !myTurn && Board.Phase == Phase.End && DrawTrap(info, t))
                {
                    score = 35;
                }
                else if (last == null && !myTurn && Board.Phase == Phase.End
                         && (t.Has(Trait.SpellTrapRemoval) && HasSpellTraps(opp) || t.Has(Trait.MonsterRemoval) && HasMonsters(opp)))
                {
                    score = 30; // use leftover removal at the end of the opponent's turn
                }

                // The deck's own timing for this card (a playbook chain rule) comes first.
                if (_playbook?.ChainRule(o.Card.Code) is string timed)
                {
                    if (Notes != null) Note(Responses.Int32(i), $"chains {Name(o.Card.Code)} [{timed}]", 88);
                    if (88 > bestScore) { bestScore = 88; best = i; Decide($"[{timed}]"); }
                    continue;
                }

                // Nibiru: its window is after their fifth summon, not an activation (the hand-trap rule above scores it 0).
                // It Tributes every face-up monster: worth it when theirs are worth more than ours, and the token it gives
                // them (the Tributed monsters' combined ATK) is one Nibiru (3000) stops or that can't take most of our LP.
                if (fromHand && info != null && SummonCountTrap.IsMatch(info.Text))
                {
                    int token = Monsters(me).Concat(Monsters(opp)).Where(m => m.card.IsFaceUp).Sum(m => m.card.Attack);
                    bool tokenSafe = token <= info.Attack || token < Board.Players[me].LifePoints * 0.6;
                    score = !myTurn && tokenSafe && FaceUpValue(opp, me) > FaceUpValue(me, me) + 500 ? 85 : 0;
                }

                // An ATK pump (Aleister's hand effect): only in a battle it turns or for lethal, never just because a
                // window opened (it would chain to any opponent's activation like a hand trap).
                if (Pump(info) is { } pump) score = PumpTurnsBattle(me, pump) ? 90 : 0;

                // A negate answers the opponent: never our own link (Koa'ki Meiru Guardian negating our Raptite) or our
                // own summon (Royal Oppression on our own Special Summon).
                if ((last != null ? last.Controller == me : _ctx.LastEventPlayer == me) && AnswersActivations(info)) score = 0;

                if (Notes != null) Note(Responses.Int32(i), $"chains {Name(o.Card.Code)}", score);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0 && p.Forced) best = 0;
            if (Environment.GetEnvironmentVariable("YGO_DEBUG_CHAINS") == "1")
                Decide($"(chain options: {string.Join(", ", p.Chains.Select(c => Name(c.Card.Code)))}; picked {(best < 0 ? "none" : Name(p.Chains[best].Card.Code))})");
            if (best < 0) return Responses.NoChain();
            // Beginners often miss their window to respond.
            if (!p.Forced && _rng.NextDouble() < (1 - _style.Skill) * 0.5) return Responses.NoChain();
            if (respondable) _respondedSerial = _ctx.EventSerial;
            var key = (p.Chains[best].Card.Code, p.Chains[best].Description);
            _chainedThisTurn[key] = (_chainedThisTurn.TryGetValue(key, out var n) ? n : 0) + 1;
            Decide($"chains {Name(p.Chains[best].Card.Code)}");
            return Responses.Int32(best);
        }

        /// <summary>A hand trap that answers a summon count (Nibiru: "Normal or Special Summoned 5 or more monsters this turn").</summary>
        private static readonly System.Text.RegularExpressions.Regex SummonCountTrap = new System.Text.RegularExpressions.Regex(
            @"Summoned \d or more monsters this turn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>What a player's face-up monsters are worth (Nibiru Tributes them all).</summary>
        private double FaceUpValue(int controller, int me) =>
            Monsters(controller).Where(m => m.card.IsFaceUp).Sum(m => (double)MonsterValue(m.card, controller, me, (int)m.seq));

        private static readonly System.Text.RegularExpressions.Regex NegatesActivation = new System.Text.RegularExpressions.Regex(
            @"negate (the|that|its) (activation|effect|activated effect)|negate (the|that) (Summon|Special Summon)|negate that card",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>A negate that answers someone else's card or effect ("negate the activation"), not "Special Summon it, but negate its effects".</summary>
        private static bool AnswersActivations(CardInfo? info) =>
            info != null && NegatesActivation.IsMatch(info.Text) && !info.Text.Contains("but negate its effects")
            && !info.Text.Contains("but its effects are negated");

        /// <summary>A Trap whose only use is drawing (Jar of Greed): no removal, negation or summoning.</summary>
        private static bool DrawTrap(CardInfo? info, Trait t) =>
            info != null && (info.Type & CardType.Trap) != 0 && t.Has(Trait.Draw)
            && !t.Has(Trait.MonsterRemoval) && !t.Has(Trait.SpellTrapRemoval) && !t.Has(Trait.Negate) && !t.Has(Trait.SpecialSummon);

        /// <summary>Banishes or negates a monster in the GY with its effects (Called by the Grave, Crossout Designator-style).</summary>
        private static bool NegatesFromGrave(CardInfo? info) =>
            info != null && (info.Text.Contains("in either GY") || info.Text.Contains("in your opponent's GY")) && info.Text.Contains("negated");

        // ---------------------------------------------------------------- Selections

        private byte[] SelectCards(SelectCardPrompt p)
        {
            int me = p.Player;
            var hint = _ctx.PromptHint[me & 1];

            if (hint == DuelContext.HintAttackTarget)
            {
                int idx = _attackTarget is var (controller, seq)
                    ? p.Cards.FindIndex(c => c.Controller == controller && c.Sequence == seq && c.Location == Location.MZone)
                    : -1;
                if (idx < 0)
                {
                    var attackerLive = _lastAttacker is uint a && a < Board.Players[me].Monsters.Count ? Board.Players[me].Monsters[(int)a] : null;
                    int atk = attackerLive?.Attack ?? Board.Players[me].Monsters.Where(m => m != null).Select(m => m!.Attack).DefaultIfEmpty(0).Max();
                    double Benefit(int i) => Live(p.Cards[i]) is FieldCard t ? AttackBenefit(atk, t, 0, me, p.Cards[i].Sequence, attackerLive) : 0;
                    idx = Enumerable.Range(0, p.Cards.Count).OrderByDescending(Benefit).First();
                    // The target we meant isn't offered (Avramax draws every attack) and none left is worth it: call it off.
                    // Not when the attack is forced (Nightmare Pain: must attack, and only a Yubel monster): the core lets the
                    // selection be cancelled, but it only asks the same Battle Phase menu again, so the bot would loop forever.
                    if (p.Cancelable && Benefit(idx) < 0 && UseRulesSense && !_ctx.AttackForced[me & 1])
                    {
                        if (_lastAttacker is uint off) _calledOff.Add(BattleKey(me, off));
                        Decide($"calls off the attack: no target worth it ({Name(p.Cards[idx].Code)} only)");
                        return Responses.CancelSelection();
                    }
                }
                _lastAttackedKey = BattleKey(p.Cards[idx].Controller, p.Cards[idx].Sequence);
                return Responses.Cards(new[] { idx });
            }

            var ranked = Rank(p.Cards, hint, me);
            int count = _playbook?.TakeCount(hint, SelectionSource != 0 ? SelectionSource : _ctx.ActivatingCode, p.Cards, me, p.Min, p.Max)
                        ?? DesiredCount(hint, p.Min, p.Max, p.Cards, me);
            var picks = ranked.Take(count).ToList();
            var usual = Responses.Cards(picks);
            if (Environment.GetEnvironmentVariable("YGO_DEBUG_CHAINS") == "1")
                Decide($"(select hint {hint} from {SelectionSource}: {string.Join(", ", p.Cards.Select(c => Name(c.Code)))}; picks {string.Join(", ", picks.Select(i => Name(p.Cards[i].Code)))})");
            if (!CanPlanSelection(me) || count == 0) return usual;
            return Planned(p, me, usual, count == 1
                ? Distinct(p.Cards, Enumerable.Range(0, p.Cards.Count)).Select(i => (Responses.Cards(new[] { i }), $"pick {Name(p.Cards[i].Code)}"))
                : Swaps(p.Cards, picks, ranked.Skip(count), (a, b) => true), several: count > 1);
        }

        // ---------------------------------------------------------------- Planned selections

        /// <summary>Skilled characters try the options of a selection in scratch duels when it is theirs to make in their own turn.</summary>
        private bool CanPlanSelection(int me) => _planner != null && Board.TurnPlayer == me && Board.Phase == Phase.Main1
                                                 && _playbook?.InRouteStep != true; // a combo route's step makes its own picks

        private byte[] Planned(Prompt p, int me, byte[] usual, IEnumerable<(byte[] response, string label)> options, bool several)
        {
            var planned = _planner!.ChooseSelection(p, me, usual, options, several, Exposure(me), out var why);
            if (planned == null) return usual;
            Decide($"(planned) {why}");
            return planned;
        }

        /// <summary>The first index of each different card (same card in the same place counts once).</summary>
        private static IEnumerable<int> Distinct(List<CardRef> cards, IEnumerable<int> indices)
        {
            var seen = new HashSet<(uint, byte, uint)>();
            return indices.Where(i => seen.Add((cards[i].Code, cards[i].Controller, cards[i].Location)));
        }

        /// <summary>The usual picks with one card swapped for one left out, least-wanted pick first.</summary>
        private IEnumerable<(byte[] response, string label)> Swaps(List<CardRef> cards, List<int> picks, IEnumerable<int> others, Func<int, int, bool> canSwap)
        {
            foreach (var other in Distinct(cards, others))
                for (int k = picks.Count - 1; k >= 0; k--)
                {
                    if (!canSwap(picks[k], other) || cards[picks[k]].Code == cards[other].Code) continue;
                    var alt = new List<int>(picks) { [k] = other };
                    yield return (Responses.Cards(alt), $"use {Name(cards[other].Code)} instead of {Name(cards[picks[k]].Code)}");
                }
        }

        /// <summary>Synchro / Ritual sums: the usual set, and the best sets without each of its cards.</summary>
        private byte[]? PlannedSum(SumPrompt p)
        {
            var usual = Sum(p, out var picks);
            if (usual == null || !CanPlanSelection(p.Player)) return usual;
            IEnumerable<(byte[], string)> Options()
            {
                foreach (var k in picks)
                    if (Sum(p, out _, exclude: k) is byte[] alt)
                        yield return (alt, $"materials without {Name(p.Cards[k].Code)}");
            }
            return Planned(p, p.Player, usual, Options(), several: true);
        }

        private byte[] Tribute(TributePrompt p)
        {
            var picked = new List<int>();
            int total = 0;
            var hint = _ctx.PromptHint[p.Player & 1];
            foreach (var i in Enumerable.Range(0, p.Cards.Count).OrderByDescending(i => SelectionScore(p.Cards[i], hint == 0 ? DuelContext.HintTribute : hint, p.Player)))
            {
                if (total >= p.Min || picked.Count >= p.Max) break;
                picked.Add(i);
                total += p.ReleaseValues[i];
            }
            var usual = Responses.Cards(picked);
            if (!CanPlanSelection(p.Player) || picked.Count == 0) return usual;
            var left = Enumerable.Range(0, p.Cards.Count).Where(i => !picked.Contains(i));
            return Planned(p, p.Player, usual, Swaps(p.Cards, picked, left, (a, b) => p.ReleaseValues[a] == p.ReleaseValues[b]), several: true);
        }

        private byte[] SelectUnselect(SelectUnselectPrompt p)
        {
            var usual = SelectUnselectCore(p);
            if (!CanPlanSelection(p.Player) || p.Selectable.Count == 0) return usual;
            var options = Distinct(p.Selectable, Enumerable.Range(0, p.Selectable.Count))
                .Select(i => (Responses.PickOne(i), $"select {Name(p.Selectable[i].Code)}")).ToList();
            if (p.Finishable) options.Add((Responses.CancelSelection(), "finish selecting"));
            return Planned(p, p.Player, usual, options, several: true);
        }

        private byte[] SelectUnselectCore(SelectUnselectPrompt p)
        {
            int me = p.Player;
            var hint = _ctx.PromptHint[me & 1];
            bool cost = IsCostHint(hint);
            int selected = p.Unselectable.Count;

            if (p.Finishable && (cost || selected >= p.Max || p.Selectable.Count == 0))
                return Responses.CancelSelection();
            if (p.Selectable.Count > 0)
            {
                int pick = Rank(p.Selectable, hint, me).First();
                // Removal picked one at a time (Twin Twisters' "up to 2"): stop once only our own cards are left.
                if (p.Finishable && selected > 0 && IsHarmHint(hint) && p.Selectable[pick].Controller == me && p.Selectable.Any(c => c.Controller != me) == false)
                    return Responses.CancelSelection();
                return Responses.PickOne(pick);
            }
            if (p.Finishable || p.Cancelable) return Responses.CancelSelection();
            return Responses.PickOne(p.Selectable.Count);
        }

        /// <summary>Synchro / Ritual style "levels must add up" selections: cheapest valid set.</summary>
        private byte[]? Sum(SumPrompt p) => Sum(p, out _);

        private byte[]? Sum(SumPrompt p, out List<int> picks, int exclude = -1)
        {
            var hint = _ctx.PromptHint[p.Player & 1];
            var order = Enumerable.Range(0, p.Cards.Count).Where(i => i != exclude)
                .OrderByDescending(i => SelectionScore(p.Cards[i], hint, p.Player)).ToList();
            List<int>? best = null;
            int bestCost = int.MaxValue, nodes = 0;
            var current = new List<int>();

            void Search(int from, int cost)
            {
                if (++nodes > 50000 || cost >= bestCost) return;
                if (current.Count > 0 && RandomDuelist.SumIsValid(p, current))
                {
                    best = new List<int>(current);
                    bestCost = cost;
                    return;
                }
                if (current.Count >= Math.Max(p.Max, 6)) return;
                for (int k = from; k < order.Count; k++)
                {
                    current.Add(order[k]);
                    Search(k + 1, cost + k + 1); // earlier in the preference order = cheaper
                    current.RemoveAt(current.Count - 1);
                }
            }

            Search(0, 0);
            picks = best ?? new List<int>();
            return best == null ? null : Responses.Cards(best);
        }

        private List<int> Rank(List<CardRef> cards, ulong hint, int me)
        {
            // OrderByDescending works out each key once, first to last, and sorts stably: the same as scoring first.
            var scores = new double[cards.Count];
            for (int i = 0; i < cards.Count; i++) scores[i] = SelectionScore(cards[i], hint, me);
            if (Notes != null)
                for (int i = 0; i < cards.Count; i++) Note(Responses.Cards(new[] { i }), $"picks {Name(cards[i].Code)}", scores[i]);
            return Enumerable.Range(0, cards.Count).OrderByDescending(i => scores[i]).ToList();
        }

        private double SelectionScore(CardRef card, ulong hint, int me) =>
            BaseSelectionScore(card, hint, me) + (_playbook?.SelectionBias(card, hint, SelectionSource) ?? 0);

        /// <summary>The card a selection belongs to: the resolving effect, or the summon just chosen (for materials).</summary>
        private uint SelectionSource => _ctx.ResolvingCode != 0 ? _ctx.ResolvingCode : _lastSpecialSummonCode;

        private double BaseSelectionScore(CardRef card, ulong hint, int me)
        {
            int value = Value(card, me, hint);
            // Fusion materials banished from the GY (Invocation) cost little: the GY's before the hand and field.
            if (hint == DuelContext.HintFusionMaterial && (card.Location & (Location.Grave | Location.Removed)) != 0) return -value / 4.0;
            if (IsCostHint(hint)) return -value;
            // A target for our own support (Eagle Booster's "unaffected", "cannot be destroyed", "gains ATK"): our best
            // monster, never the opponent's.
            if (hint == DuelContext.HintTarget && Supports(_cards.Get(_activatingCode)) && (card.Location & Location.MZone) != 0)
                return card.Controller == me ? value + 2000 : -value - 2000;
            // An Equip Spell that helps what it's equipped to (Axe of Despair: "the equipped monster gains 1000 ATK"): ours.
            if ((hint == DuelContext.HintEquip || hint == DuelContext.HintTarget) && BoostsEquipped(_cards.Get(_activatingCode))
                && (card.Location & Location.MZone) != 0)
                return card.Controller == me ? value + 2000 : -value - 2000;
            if (card.Controller != me) return value + 2000; // hurt the opponent's best card
            bool onBoardOrHand = (card.Location & (Location.MZone | Location.SZone | Location.Hand)) != 0;
            if (IsHarmHint(hint) && onBoardOrHand) return -value; // forced to lose one of ours: the cheapest
            // Banishing from our own Extra Deck is a cost (Pot of Prosperity's): the monsters we'd never summon go.
            if (hint == DuelContext.HintRemove && card.Location == Location.Extra) return -value;
            // Bringing out a risky monster by effect (a revive, a summon from the Deck) is the same race as summoning it.
            if (hint == DuelContext.HintSpSummon && RiskyByEffect(card, me)) return DoomedValue;
            return value + SearchBonus(card, hint, me);
        }

        private static readonly System.Text.RegularExpressions.Regex SupportTarget = new System.Text.RegularExpressions.Regex(
            @"[Tt]arget \d+ (face-up )?monsters? (on the field|you control)[^.]*?;[^.]*?(unaffected by|cannot be destroyed|cannot be targeted|gains? \d+ ATK)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>A card whose targeted effect helps its target (and doesn't also weaken it, like Forbidden Lance).</summary>
        private static bool Supports(CardInfo? info) =>
            info != null && (SupportTarget.IsMatch(info.Text) && !info.Text.Contains("loses") && !info.Text.Contains("negate")
                             && !info.Text.Contains("destroy that") && !info.Text.Contains("take control")
                             || BoostsEquipped(info));

        private static readonly System.Text.RegularExpressions.Regex PumpText = new System.Text.RegularExpressions.Regex(
            @"target 1 ([^;.]*?)monsters? you control; (it|that monster) gains (\d+) ATK(/DEF)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// A Quick Effect that only pumps one of our monsters' ATK for the turn ("send this card from your hand to the GY,
        /// then target 1 Fusion Monster you control; it gains 1000 ATK/DEF until the end of this turn": Aleister the
        /// Invoker): how much, whether DEF too, and whether only a Fusion Monster can take it. Null for anything else.
        /// </summary>
        internal static (int amount, bool defense, bool fusionOnly)? Pump(CardInfo? info)
        {
            if (info == null || !info.Text.Contains("(Quick Effect)")) return null;
            var m = PumpText.Match(info.Text);
            if (!m.Success) return null;
            return (int.Parse(m.Groups[3].Value), m.Groups[4].Success, m.Groups[1].Value.Contains("Fusion"));
        }

        /// <summary>
        /// Whether a pump turns the battle in progress for us: our monster wins it (or survives it) only with the boost,
        /// or the boost makes the damage lethal. A pump spent anywhere else (an open window, the Main Phase) is wasted:
        /// it lasts only this turn, and the card is worth more held (Aleister is also the next Invocation's material).
        /// </summary>
        private bool PumpTurnsBattle(int me, (int amount, bool defense, bool fusionOnly) pump)
        {
            if (_ctx.Battle is not var (attacker, target)) return false;
            FieldCard? At(CardRef r) => r.Sequence < Board.Players[r.Controller].Monsters.Count ? Board.Players[r.Controller].Monsters[(int)r.Sequence] : null;
            var attacking = At(attacker);
            var attacked = target is CardRef t ? At(t) : null;
            if (attacking == null) return false;
            bool weAttack = attacker.Controller == me;
            var mine = weAttack ? attacking : attacked;
            var theirs = weAttack ? attacked : attacking;
            if (mine == null || (mine.Position & (Ygo.Engine.Position.FaceUpAttack | Ygo.Engine.Position.FaceUpDefense)) == 0) return false;
            if (pump.fusionOnly && (mine.Type & CardType.Fusion) == 0) return false;
            int n = pump.amount, lp = Board.Players[1 - me].LifePoints;
            if (weAttack && theirs == null)
            {
                // A direct attack made lethal, counting the rest of the turn's direct damage: our other monsters in Attack
                // Position that haven't attacked yet (nothing of theirs is left to block).
                int rest = 0;
                var ours = Board.Players[me].Monsters;
                for (int s = 0; s < ours.Count; s++)
                    if (ours[s] is FieldCard c && c != mine && (c.Position & Ygo.Engine.Position.FaceUpAttack) != 0 && !_attackedThisTurn.Contains(BattleKey(me, (uint)s)))
                        rest += c.Attack;
                return mine.Attack + rest < lp && mine.Attack + rest + n >= lp;
            }
            if (theirs == null || (theirs.Position & (Ygo.Engine.Position.FaceUpAttack | Ygo.Engine.Position.FaceUpDefense)) == 0) return false; // unknown stats
            bool theirDefense = (theirs.Position & Ygo.Engine.Position.FaceUpDefense) != 0;
            if (weAttack)
            {
                int stat = theirDefense ? theirs.Defense : theirs.Attack;
                if (mine.Attack <= stat && mine.Attack + n > stat) return true; // over the top of their monster
                return !theirDefense && mine.Attack > stat && mine.Attack - stat < lp && mine.Attack + n - stat >= lp; // lethal
            }
            // They attack our monster: the boost lets it survive (and win).
            if ((mine.Position & Ygo.Engine.Position.FaceUpDefense) != 0)
                return pump.defense && mine.Defense < theirs.Attack && mine.Defense + n >= theirs.Attack;
            return mine.Attack <= theirs.Attack && mine.Attack + n > theirs.Attack;
        }

        /// <summary>The ATK an attacker can count on from pumps in our hand (Aleister the Invoker: +1000 to a Fusion Monster).</summary>
        private int PumpInHand(int me, FieldCard attacker) =>
            Board.Players[me].Hand.Where(c => c != null).Select(c => Pump(_cards.Get(c!.Code)))
                .Where(q => q is { } pump && (!pump.fusionOnly || (attacker.Type & CardType.Fusion) != 0))
                .Select(q => q!.Value.amount).DefaultIfEmpty(0).Max();

        /// <summary>An Equip Spell that only helps the monster it's equipped to ("the equipped monster gains 1000 ATK").</summary>
        private static bool BoostsEquipped(CardInfo? info) =>
            info != null && (info.Type & 0x40000) != 0 && info.Text.Contains("quipped monster gains")
            && !info.Text.Contains("loses") && !info.Text.Contains("cannot attack") && !info.Text.Contains("take control")
            && !info.Text.Contains("your opponent controls");

        private int DesiredCount(ulong hint, int min, int max, List<CardRef> cards, int me)
        {
            if (IsCostHint(hint)) return Math.Min(min, max);
            bool beneficial = hint == DuelContext.HintAddToHand || hint == DuelContext.HintSpSummon || hint == DuelContext.HintToField;
            // "Up to 2" removal (Twin Twisters): as many of the opponent's cards as there are, not one of ours to make up the count.
            int theirs = cards.Count(c => c.Controller != me);
            if (IsHarmHint(hint) && theirs > 0) return Math.Min(max, Math.Max(min, theirs));
            if (beneficial) return max;
            return Math.Min(Math.Max(min, 1), max);
        }

        private int SearchBonus(CardRef card, ulong hint, int me)
        {
            if (hint != DuelContext.HintAddToHand) return 0;
            var hand = Board.Players[me].Hand;
            int bonus = 0;
            if (hand.Any(h => h != null && h.Code == card.Code)) bonus -= 800;
            var info = _cards.Get(card.Code);
            bool handHasMonster = hand.Any(h => h != null && (h.Type & CardType.Monster) != 0);
            if (info != null && (info.Type & CardType.Monster) != 0 && !handHasMonster) bonus += 600;
            return bonus;
        }

        private static bool IsCostHint(ulong hint) =>
            hint == DuelContext.HintRelease || hint == DuelContext.HintDiscard || hint == DuelContext.HintTribute
            || hint == DuelContext.HintFusionMaterial || hint == DuelContext.HintSynchroMaterial
            || hint == DuelContext.HintXyzMaterial || hint == DuelContext.HintLinkMaterial
            || hint == DuelContext.HintDetachMaterial || hint == DuelContext.HintDeattachFrom;

        private static bool IsHarmHint(ulong hint) =>
            hint == DuelContext.HintDestroy || hint == DuelContext.HintRemove || hint == DuelContext.HintToGrave
            || hint == DuelContext.HintReturnToHand || hint == DuelContext.HintToDeck || hint == DuelContext.HintNegate
            || hint == DuelContext.HintFaceDown || hint == DuelContext.HintPosition || hint == DuelContext.HintTarget
            || hint == DuelContext.HintControl;

        private byte[] Position(PositionPrompt p)
        {
            var info = _cards.Get(p.Code);
            int oppBest = OpponentBestAttack(p.Player);
            // "?" stats (printed 0/0, e.g. Zoodiac Xyz) are set from the materials once summoned: they attack.
            bool computedStats = info != null && info.Attack == 0 && info.Defense == 0 && info.IsExtraDeckMonster;
            bool lowLp = Board.Players[p.Player].LifePoints <= Math.Max(2000, oppBest);
            double bias = lowLp ? Math.Min(0, AttackPositionBias) : AttackPositionBias;
            bool attack = info == null || computedStats || info.Attack + bias >= oppBest
                          || info.Attack >= info.Defense && !HasMonsters(1 - p.Player);
            uint[] preference = attack
                ? new[] { Ygo.Engine.Position.FaceUpAttack, Ygo.Engine.Position.FaceUpDefense, Ygo.Engine.Position.FaceDownDefense, Ygo.Engine.Position.FaceDownAttack }
                : new[] { Ygo.Engine.Position.FaceUpDefense, Ygo.Engine.Position.FaceDownDefense, Ygo.Engine.Position.FaceUpAttack, Ygo.Engine.Position.FaceDownAttack };
            foreach (var pos in preference)
                if ((p.Positions & pos) != 0) return Responses.Int32((int)pos);
            return Responses.Int32((int)Ygo.Engine.Position.FaceUpAttack);
        }

        private byte[] Place(PlacePrompt p)
        {
            byte self = p.Player, opp = (byte)(1 - p.Player);
            var zones = new List<(byte player, uint location, byte sequence)>();
            // Own zones center-out; when disabling, the opponent's first.
            var players = p.DisableField ? new[] { (opp, 16), (self, 0) } : new[] { (self, 0), (opp, 16) };
            byte[] centerOut = { 2, 1, 3, 0, 4, 5, 6, 7 };
            // A monster that works off its column (Mekk-Knight Blue Sky searches one card per opposing card there; Red Moon
            // and Yellow Star destroy in it): our Main Monster Zones facing the most opposing cards first.
            var placing = _cards.Get(_summoningCode);
            bool byColumn = !p.DisableField && placing != null && placing.Text.Contains("column");
            int Facing(byte seq)
            {
                if (Presentation.Columns.Of(self, Location.MZone, seq) is not int column) return 0;
                var theirs = Board.Players[opp];
                int n = 0;
                for (int s = 0; s < theirs.Monsters.Count && s < 7; s++)
                    if (theirs.Monsters[s] != null && Presentation.Columns.Of(opp, Location.MZone, s) == column) n++;
                for (int s = 0; s < theirs.SpellTraps.Count && s < 5; s++)
                    if (theirs.SpellTraps[s] != null && Presentation.Columns.Of(opp, Location.SZone, s) == column) n++;
                return n;
            }
            foreach (var (player, shift) in players)
            {
                var monsterOrder = player == self && byColumn ? centerOut.Where(s => s < 5).OrderByDescending(Facing).Concat(centerOut.Where(s => s >= 5)).ToArray() : centerOut;
                foreach (var seq in monsterOrder)
                    if (seq < 7 && (p.BlockedZones & (1u << (shift + seq))) == 0) zones.Add((player, Location.MZone, seq));
                foreach (var seq in centerOut)
                    if ((p.BlockedZones & (1u << (shift + 8 + seq))) == 0) zones.Add((player, Location.SZone, seq));
            }
            return Responses.Places(zones.Take(p.Count));
        }

        private byte[]? AnnounceCard(AnnounceCardPrompt p)
        {
            // Declare something the opponent has shown, most-seen first.
            var opp = Board.Players[1 - p.Player];
            var seen = opp.Graveyard.Concat(opp.Banished).Concat(opp.Monsters).Concat(opp.SpellTraps)
                .Where(c => c != null && c.IsFaceUp || c != null && opp.Graveyard.Contains(c))
                .GroupBy(c => c!.Code)
                .OrderByDescending(g => g.Count())
                .Select(g => _cards.Get(g.Key))
                .FirstOrDefault(info => info != null && CardDeclaration.IsDeclarable(info, p.Opcodes));
            if (seen == null) return null;
            Decide($"declares {seen.Name}");
            return Responses.Int32((int)seen.Code);
        }

        // ---------------------------------------------------------------- Board knowledge

        private BoardState Board => _ctx.Board;

        private IEnumerable<(uint seq, FieldCard card)> Monsters(int player)
        {
            var list = Board.Players[player].Monsters;
            for (int i = 0; i < list.Count; i++)
                if (list[i] is FieldCard c) yield return ((uint)i, c);
        }

        private bool HasMonsters(int player) => Monsters(player).Any();
        private bool HasFaceUpMonsters(int player) => Monsters(player).Any(m => m.card.IsFaceUp);
        private static bool DestroysAllSpellTraps(CardInfo info) =>
            info.Text.Contains("all Spell") || info.Text.Contains("all Spells") || info.Text.Contains("all other Spell");

        /// <summary>Set while scoring the Main Phase: a Spell/Trap wipe can be activated and is worth it.</summary>
        private bool _spellTrapWipeReady;

        private int SpellTrapCount(int player) => Board.Players[player].SpellTraps.Take(5).Count(c => c != null);

        private bool HasSpellTraps(int player) => Board.Players[player].SpellTraps.Take(6).Any(c => c != null);
        private int OpponentSetSpellTraps(int me) =>
            Board.Players[1 - me].SpellTraps.Take(5).Count(c => c != null && !c.IsFaceUp);

        private bool HasAttackers(int me) => Monsters(me).Any(m => m.card.IsFaceUp && !m.card.IsDefense);
        private int MyBestAttack(int me) => Monsters(me).Select(m => m.card.Attack).DefaultIfEmpty(0).Max();

        /// <summary>Strongest face-up attack among the opponent's monsters (face-down ones are unknown).</summary>
        private int OpponentBestAttack(int me) =>
            Monsters(1 - me).Where(m => m.card.IsFaceUp).Select(m => m.card.Attack).DefaultIfEmpty(0).Max();

        /// <summary>How dangerous a just-summoned / activated card is; unknown cards count as average.</summary>
        private int ThreatValue(uint code)
        {
            var info = code == 0 ? null : _cards.Get(code);
            return info == null ? UnknownCardValue : CardValue(info, null, inHand: true);
        }

        private int SideValue(int player, int me) => Monsters(player).Sum(m => MonsterValue(m.card, player, me, (int)m.seq));

        private int BestGraveMonsterAttack() =>
            Board.Players.SelectMany(p => p.Graveyard)
                .Where(c => c != null && (c.Type & CardType.Monster) != 0)
                .Select(c => _cards.Get(c!.Code)?.Attack ?? 0)
                .DefaultIfEmpty(0).Max();

        private FieldCard? Live(CardRef c)
        {
            var p = Board.Players[c.Controller & 1];
            List<FieldCard?>? list = (c.Location & 0x7f) switch
            {
                Location.MZone => p.Monsters,
                Location.SZone => p.SpellTraps,
                Location.Hand => p.Hand,
                Location.Grave => p.Graveyard,
                Location.Removed => p.Banished,
                _ => null,
            };
            return list != null && c.Sequence < list.Count ? list[(int)c.Sequence] : null;
        }

        /// <summary>An opponent's Normal / Quick-Play Spell or Normal Trap mid-resolution: next to worthless as a removal target.</summary>
        private const int ResolvingSpellTrapValue = 50;

        /// <summary>What this card is worth to its controller, using only information visible to <paramref name="me"/>.</summary>
        private int Value(CardRef c, int me, ulong hint = 0)
        {
            var live = Live(c);
            bool hidden = c.Controller != me
                          && (c.Location & (Location.Grave | Location.Removed)) == 0
                          && !(live?.IsFaceUp ?? (c.Position & (Ygo.Engine.Position.FaceUpAttack | Ygo.Engine.Position.FaceUpDefense)) != 0);
            if (hidden) return UnknownCardValue;
            var info = _cards.Get(c.Code);
            if (info == null) return UnknownCardValue;
            if (c.Controller == me && (c.Location & Location.MZone) != 0 && live != null && Doomed(live, me)) return DoomedValue;
            if (c.Controller == me && (c.Location & Location.MZone) != 0 && live != null && Stuck(me, c.Sequence)) return StuckValue;
            var value = CardValue(info, (c.Location & Location.MZone) != 0 ? live : null, inHand: c.Location == Location.Hand);
            if (c.Controller == me && (c.Location & Location.MZone) != 0 && _playbook?.IsBoss(c.Code) == true) value += BossBonus;
            // A face-up Continuous / Field / Equip card is working against someone every turn
            // (locks like Gravity Bind or Messenger of Peace), so it is the best removal target. So is the opponent's
            // Normal Spell or Trap that stays on the field (Swords of Revealing Light, Altergeist Manifestation): one that
            // says so, or one still face-up that isn't on the chain (it has resolved and stayed). Only for destroying or
            // banishing it: bounced, it's simply cast again (Swords restarts its lock), so a return to the hand and every
            // other selection value it as before. (Our own are valued as before too.)
            const uint ContinuousFieldEquip = 0x20000 | 0x80000 | 0x40000;
            bool lasting = (info.Type & ContinuousFieldEquip) != 0
                           || UseRemovalTiming && (hint == DuelContext.HintDestroy || hint == DuelContext.HintRemove)
                              && c.Controller != me && (info.Type & CardType.Monster) == 0 && c.Location == Location.SZone
                              && (!CardTraits.LeavesAfterResolving(info) || !OnChain(c.Controller, (int)c.Sequence, c.Code));
            if (c.Location == Location.SZone && live != null && live.IsFaceUp && lasting)
                value = Math.Max(value, 1800);
            // Any other face-up Spell/Trap of theirs is resolving right now and leaves anyway: banishing or destroying it
            // doesn't stop it (Exosister Returnia on a chained Terraforming), so it's the last thing to aim removal at.
            else if (c.Controller != me && c.Location == Location.SZone && live != null && live.IsFaceUp
                     && (info.Type & CardType.Monster) == 0)
                value = ResolvingSpellTrapValue;
            return value;
        }

        /// <summary>The card in an opponent's zone if it is face-up or was seen face-up and never left.</summary>
        private CardInfo? KnownMonster(FieldCard card, int controller, uint seq)
        {
            if (card.IsFaceUp) return _cards.Get(card.Code);
            var remembered = seq < 7 ? _ctx.RememberedMonster[controller & 1, seq] : 0;
            return remembered == 0 ? null : _cards.Get(remembered);
        }

        private int MonsterValue(FieldCard card, int controller, int me, int seq = -1)
        {
            if (controller != me && !card.IsFaceUp) return UnknownCardValue;
            if (controller == me && Doomed(card, me)) return DoomedValue;
            if (controller == me && seq >= 0 && Stuck(me, (uint)seq)) return StuckValue;
            var info = _cards.Get(card.Code);
            if (info == null) return UnknownCardValue;
            if (controller == me && Liability(card, info)) return LiabilityValue;
            return CardValue(info, card) + (controller == me && _playbook?.IsBoss(card.Code) == true ? BossBonus : 0);
        }

        /// <summary>Hand traps are only worth a premium while they sit in hand; elsewhere they are just bodies.</summary>
        private static int CardValue(CardInfo info, FieldCard? live, bool inHand = false)
        {
            var t = CardTraits.Of(info);
            if ((info.Type & CardType.Monster) != 0)
            {
                int v = Math.Max(0, live?.Attack ?? info.Attack);
                if ((info.Type & 0x20) != 0) v += 300;
                if (info.IsExtraDeckMonster) v += 400;
                if (t.Has(Trait.Advantage) || t.Has(Trait.SpecialSummon)) v += 300;
                if (t.Has(Trait.HandTrap) && inHand) v = Math.Max(v, 1600);
                return v;
            }
            if (t.Has(Trait.BoardWipe)) return 1700;
            if (t.Has(Trait.MonsterRemoval) || t.Has(Trait.SpellTrapRemoval)) return 1500;
            if (t.Has(Trait.Advantage)) return 1400;
            if (t.Has(Trait.Negate)) return 1300;
            return 1000;
        }

        private string Name(uint code) => _cards.Get(code)?.Name ?? code.ToString();

        private void Decide(string what) => _log?.Invoke($"{_name} {what}");

        /// <summary>An optional effect (a single trigger, an "if you do" choice): yes unless it costs what we lack or only hurts us.</summary>
        private byte[] EffectYesNo(EffectYesNoPrompt p)
        {
            bool yes = !PaysWhatWeLack(p) && !(_cards.Get(p.Card.Code) is CardInfo yn && !SpellTrapGraveEffect(p.Card, yn)
                                                && RemovalBlunder(p.Card, yn, p.Player) != null);
            if (Environment.GetEnvironmentVariable("YGO_DEBUG_CHAINS") == "1") Decide($"(use {Name(p.Card.Code)}'s effect? {(yes ? "yes" : "no")})");
            return Responses.YesNo(yes);
        }
    }
}
