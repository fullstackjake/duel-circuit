using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// Rules sense: plays no competent duelist makes, whatever their style or skill, vetoed on every path to a decision
    /// (the normal choice, the planner's line, the deck playbook, a beginner's sloppy pick, a chain response). A style
    /// can be rash or timid, and take a risky card into a race it expects to win; it can't summon a card that will burn
    /// it to death first, pay LP it doesn't have, summon an outsider that makes its own archetype destroy itself, or
    /// mill its own Deck away.
    /// </summary>
    public sealed partial class SmartDuelist
    {
        /// <summary>
        /// Rules sense on (the default) or off (YGO_RULES_SENSE=0: the CPUs as they were before it, for measuring what it
        /// changes; this also restores the old skill 0.7 bar for using a deck's playbook).
        /// </summary>
        public static readonly bool RulesSense = Environment.GetEnvironmentVariable("YGO_RULES_SENSE") != "0";

        /// <summary>This CPU's rules sense (for measurements that turn it off on one side only).</summary>
        public bool UseRulesSense { get; set; } = RulesSense;

        /// <summary>Cards to keep in the Deck: below this, a duelist stops spending their Deck on effects.</summary>
        private const int MinimumDeck = 4;

        /// <summary>Damage our own cards on the field will deal us at our next Standby Phase.</summary>
        private int OwnUpkeep(int me) =>
            Board.Players[me].Monsters.Concat(Board.Players[me].SpellTraps)
                .Where(c => c != null && c.IsFaceUp)
                .Sum(c => Drawbacks.UpkeepDamage(_cards.Get(c!.Code)));

        /// <summary>The LP we can afford to pay or lose and still survive our own upkeep.</summary>
        private int SpareLifePoints(int me) => Board.Players[me].LifePoints - OwnUpkeep(me);

        /// <summary>Why a Main Phase action would be a blunder, or null if it's fine.</summary>
        private string? Blunder(IdlePrompt p, byte[] response, int me)
        {
            if (!UseRulesSense) return null;
            int value = BitConverter.ToInt32(response, 0);
            var action = (IdleAction)(value & 0xffff);
            int index = value >> 16;
            CardRef? card = action switch
            {
                IdleAction.Summon when index < p.Summonable.Count => p.Summonable[index],
                IdleAction.SpecialSummon when index < p.SpSummonable.Count => p.SpSummonable[index],
                IdleAction.SetMonster when index < p.MonsterSettable.Count => p.MonsterSettable[index],
                IdleAction.Activate when index < p.Activatable.Count => p.Activatable[index].Card,
                _ => null,
            };
            if (card is not { } c || _cards.Get(c.Code) is not CardInfo info) return null;
            int upkeep = Drawbacks.UpkeepDamage(info);
            if (action == IdleAction.Activate)
            {
                // Placing a card that burns us each Standby Phase (a Dark Contract) when we can't afford two of them.
                if (upkeep > 0 && c.Location == Location.Hand && upkeep * 2 >= SpareLifePoints(me))
                    return $"{info.Name} would cost us {upkeep} LP every Standby Phase, at {Board.Players[me].LifePoints} LP";
                // A set Trap that only draws (Jar of Greed) is a bluff until the end of their turn (the planner likes the card now).
                if (c.Location == Location.SZone && Board.TurnPlayer == me && DrawTrap(info, CardTraits.Of(info)))
                    return $"{info.Name} waits for the end of the opponent's turn";
                return SupportBlunder(info, me) ?? RemovalBlunder(c, info, me) ?? TaxBlunder(c, info, me, p.Activatable.Select(o => o.Card)) ?? CostBlunder(info, me)
                    ?? HandCostBlunder(c, info, me);
            }

            if (action == IdleAction.SpecialSummon && c.Location == Location.Extra && DowngradeBlunder(info, me) is string down) return down;
            // A hand trap (Ash Blossom, Effect Veiler) works from the hand; Summoned or Set it's a weak body.
            if ((action == IdleAction.Summon || action == IdleAction.SetMonster) && CardTraits.Of(info).Has(Trait.HandTrap))
                return $"{info.Name} is a hand trap: it's kept in hand";

            // A monster that burns its owner each Standby Phase, or locks our own Special Summons: a race. Worth it when
            // it should win before it kills us (see Race); not when it just hands the opponent the duel.
            bool locks = Drawbacks.LocksSpecialSummons(info);
            if ((upkeep > 0 || locks) && action != IdleAction.SetMonster && Race(info, me, upkeep, locks) is string lost)
                return lost;

            // An outsider while our monsters insist on their own archetype: they'd destroy themselves. (Extra Deck
            // summons are fine: they use up those monsters as materials.)
            if (c.Location != Location.Extra)
                foreach (var (_, mine) in Monsters(me))
                {
                    if (!mine.IsFaceUp || _cards.Get(mine.Code) is not CardInfo own) continue;
                    var archetype = Drawbacks.OnlyWith(own);
                    if (archetype != null && !Drawbacks.IsMember(info, archetype))
                        return $"{info.Name} isn't a \"{archetype}\" monster: {own.Name} would destroy itself";
                }
            return null;
        }

        /// <summary>
        /// The race a risky monster starts: how many of our turns it takes to win with it on the field (the clock),
        /// against how many of our turns we survive its upkeep on top of our own (the fuse). It's played when the clock
        /// beats the fuse within a few turns: aggressive duelists accept a longer race and cut it close, cautious ones
        /// want a short race with a turn to spare. A Special Summon lock gets a shorter window (it stops a combo deck
        /// cold). When the opponent is about to win anyway, the gamble is taken. Returns why not, or null to go ahead.
        /// </summary>
        private string? Race(CardInfo info, int me, int upkeep, bool locks, int? freeTurns = null)
        {
            int opp = 1 - me;
            int clock = TurnsToWin(info, me);
            int fuse = upkeep > 0 ? TurnsToSurvive(me, upkeep, freeTurns ?? Drawbacks.FreeUpkeepTurns(info)) : int.MaxValue;
            int window = (int)Math.Round(Knob(2, 4, _style.Aggression));
            // A lock on our own Special Summons stops the deck's engine for as long as it stays (Acid Golem kept a
            // Burning Abyss deck from summoning while it burned them out): only as the finisher this turn.
            if (locks) window = 1;
            int margin = _style.Caution > 0.6 ? 1 : 0;
            // Their attackers already add up to our LP: we're losing next turn unless something changes.
            int theirPush = Monsters(opp).Where(m => m.card.IsFaceUp && !m.card.IsDefense).Sum(m => m.card.Attack);
            bool desperate = theirPush >= Board.Players[me].LifePoints;
            if (desperate) { window = Math.Max(window, 2); margin = 0; }
            // We attack this turn (clock 1 = this turn's Battle Phase); the upkeep first hits at our next Standby
            // Phase, so a fuse of 0 still allows a win this turn.
            bool wins = clock <= window && clock <= fuse + 1 - margin;
            if (wins) return null;
            return clock == int.MaxValue
                ? $"{info.Name} can't win the race ({(upkeep > 0 ? $"{upkeep} LP each Standby Phase" : "it stops our Special Summons")}, and no damage gets through)"
                : $"{info.Name} would need {clock} turns to win, {(upkeep > 0 ? $"its upkeep kills us in {fuse + 1}" : "too long under its Special Summon lock")}";
        }

        /// <summary>
        /// Our turns needed to win with this monster added: our attackers' damage per turn against the opponent's LP.
        /// Against monsters, only what gets past their biggest one counts. int.MaxValue: no damage gets through.
        /// </summary>
        private int TurnsToWin(CardInfo added, int me)
        {
            int opp = 1 - me;
            var attackers = Monsters(me).Where(m => m.card.IsFaceUp && !m.card.IsDefense).Select(m => m.card.Attack).ToList();
            if (!added.Text.Contains("cannot attack") || added.Text.Contains("While this card has no")) attackers.Add(added.Attack);
            var walls = Monsters(opp).Select(m => m.card.IsFaceUp ? (m.card.IsDefense ? m.card.Defense : m.card.Attack) : 1500).ToList();
            int perTurn;
            if (walls.Count == 0) perTurn = attackers.Sum();
            else
            {
                // Each attacker beats a wall or doesn't; damage is what's left over (direct once the walls are gone is
                // too hopeful to count).
                int wall = walls.Max();
                perTurn = attackers.Where(a => a > wall).Sum(a => a - wall);
            }
            if (perTurn <= 0) return int.MaxValue;
            int lp = Board.Players[opp].LifePoints;
            int turns = (lp + perTurn - 1) / perTurn;
            // Summoned in Main Phase 2, or on the duel's first turn (nobody attacks then): its first attack is next turn.
            return Board.Phase == Phase.Main2 || Board.Turn <= 1 ? turns + 1 : turns;
        }

        /// <summary>
        /// How many of our Standby Phases we get through with this upkeep added to what our cards already cost us (after
        /// its free turns, such as an Xyz detaching materials instead of paying).
        /// </summary>
        private int TurnsToSurvive(int me, int upkeep, int freeTurns)
        {
            int lp = Board.Players[me].LifePoints, existing = OwnUpkeep(me);
            for (int turn = 1; turn <= 20; turn++)
            {
                lp -= existing + (turn > freeTurns ? upkeep : 0);
                if (lp <= 0) return turn - 1;
            }
            return 20;
        }

        /// <summary>
        /// A risky monster an effect would Special Summon for us that loses its race. Out of the GY or banishment an Xyz
        /// Monster comes back with no materials, so it has no free turns (an Acid Golem revived by Call of the Haunted
        /// can't attack, locks our Special Summons and burns 2000 a turn).
        /// </summary>
        private bool RiskyByEffect(CardRef card, int me)
        {
            if (!UseRulesSense) return false;
            if (card.Controller != me || _cards.Get(card.Code) is not CardInfo info) return false;
            int upkeep = Drawbacks.UpkeepDamage(info);
            bool locks = Drawbacks.LocksSpecialSummons(info);
            if (upkeep <= 0 && !locks) return false;
            bool fromExtra = (card.Location & Location.Extra) != 0;
            bool canAttack = fromExtra || !info.Text.Contains("While this card has no");
            var stand = canAttack ? info : new CardInfo { Code = info.Code, Name = info.Name, Text = "cannot attack", Attack = 0 };
            return Race(stand, me, upkeep, locks, freeTurns: fromExtra ? Drawbacks.FreeUpkeepTurns(info) : 0) != null;
        }

        /// <summary>What a doomed liability is worth to us: less than nothing, so it's the first to go as a tribute or material.</summary>
        private const int DoomedValue = -2000;
        /// <summary>A monster of ours that only costs LP now (an upkeep with no material left to pay it): tribute it first.</summary>
        private const int LiabilityValue = -1000;

        /// <summary>
        /// A monster of ours that burns us every Standby Phase with nothing left to pay it with (Number 30: Acid Golem
        /// with no Xyz Material): worth less than nothing, so it's the first tribute or material, and a Tribute Summon
        /// that gets rid of it is worth making. (See <see cref="Doomed"/> for one that kills us next turn.)
        /// </summary>
        private bool Liability(FieldCard card, CardInfo info) =>
            UseRulesSense && card.IsFaceUp && Drawbacks.UpkeepDamage(info) > 0 && (Drawbacks.FreeUpkeepTurns(info) == 0 || card.OverlayCount == 0);
        /// <summary>A monster of ours locked down by the opponent (see <see cref="Stuck"/>): little more than a material.</summary>
        private const int StuckValue = 300;
        /// <summary>What a deck's boss is worth on top of its card value: it's built on, not spent.</summary>
        private const int BossBonus = 2000;

        /// <summary>
        /// A monster of ours a lasting card of the opponent's has locked down: Nightmare Wheel, Spellbinding Circle, an
        /// Equip that stops it attacking or negates it. Spending it as material is fine. A lock on everything (Swords of
        /// Revealing Light) targets nothing, so it doesn't count.
        /// </summary>
        private bool Stuck(int me, uint seq)
        {
            if (!UseRulesSense) return false;
            var theirs = Board.Players[1 - me];
            foreach (var lockCard in theirs.SpellTraps.Concat(theirs.Monsters))
            {
                if (lockCard == null || !lockCard.IsFaceUp) continue;
                bool aimed = lockCard.Targets.Any(t => t.controller == me && (t.location & 0x7f) == Location.MZone && t.sequence == seq)
                             || lockCard.EquippedTo is { } e && e.controller == me && (e.location & 0x7f) == Location.MZone && e.sequence == seq;
                if (!aimed) continue;
                var text = _cards.Get(lockCard.Code)?.Text ?? "";
                if (text.Contains("cannot attack") || text.Contains("negate its effects") || text.Contains("its effects are negated")) return true;
            }
            return false;
        }

        /// <summary>
        /// An Extra Deck summon that can only make the board weaker: whatever materials it takes, one is worth more than
        /// what it brings (a boss into a lesser Link). The cheapest possible set is a lower bound: if even its priciest
        /// monster is worth more, every set is a downgrade.
        /// </summary>
        private string? DowngradeBlunder(CardInfo info, int me)
        {
            if (!UseRulesSense || !info.IsExtraDeckMonster) return null;
            int needed = (info.Type & CardType.Link) != 0 && (info.Level & 0xff) <= 1 ? 1 : 2;
            var pool = Monsters(me).Where(m => m.card.IsFaceUp)
                .Select(m => (m.card, value: MonsterValue(m.card, me, me, (int)m.seq))).OrderBy(x => x.value).ToList();
            if (pool.Count < needed) return null;
            var priciest = pool[needed - 1];
            int gain = CardValue(info, null) + (info.Attack <= 0 ? 2500 : 0) // "?" ATK (Apollousa, Zoodiac): set by the materials
                       + (_playbook?.IsBoss(info.Code) == true ? BossBonus : 0);
            return priciest.value > gain ? $"{info.Name} would use up {Name(priciest.card.Code)}, worth more than it" : null;
        }

        /// <summary>"target 1 card you control; destroy it": removal aimed only at our own cards, on purpose.</summary>
        private static readonly System.Text.RegularExpressions.Regex OwnTarget = new System.Text.RegularExpressions.Regex(
            @"target 1 [^.;]*?you control;");

        private static readonly System.Text.RegularExpressions.Regex StatCap = new System.Text.RegularExpressions.Regex(
            @"destroy all (face-up )?monsters[^.;]*?with (?<stat>ATK|DEF) (less than or equal to|lower than|equal to or less than) (?<cap>[^.;]+)");
        private static readonly System.Text.RegularExpressions.Regex CapOrLess = new System.Text.RegularExpressions.Regex(
            @"destroy all (face-up )?monsters[^.;]*?with (?<cap>\d+) or less (?<stat>ATK|DEF)");

        /// <summary>
        /// "Destroy all monsters with DEF up to X" (Burst Breath) that can't reach any of the opponent's monsters, or
        /// costs us more than them. X is the printed number, else our strongest monster's ATK (what we'd Tribute).
        /// </summary>
        private string? ThresholdBlunder(CardInfo info, int me)
        {
            var m = StatCap.Match(info.Text);
            if (!m.Success) m = CapOrLess.Match(info.Text);
            if (!m.Success) return null;
            bool def = m.Groups["stat"].Value == "DEF";
            var number = System.Text.RegularExpressions.Regex.Match(m.Groups["cap"].Value, @"\d+");
            int cap = number.Success ? int.Parse(number.Value) : MyBestAttack(me);
            bool oneSided = info.Text.Contains("your opponent controls");
            int Lost(int player) => Monsters(player)
                .Where(x => x.card.IsFaceUp && !(def && (x.card.Type & CardType.Link) != 0) && (def ? x.card.Defense : x.card.Attack) <= cap)
                .Sum(x => Math.Max(1, MonsterValue(x.card, player, me, (int)x.seq)));
            int theirs = Lost(1 - me), ours = oneSided ? 0 : Lost(me);
            if (theirs == 0) return $"{info.Name} can't reach any of the opponent's monsters ({(def ? "DEF" : "ATK")} up to {cap})";
            return ours > theirs ? $"{info.Name} would cost us more than the opponent" : null;
        }

        /// <summary>
        /// A liability of ours that will kill us at our next Standby Phase (its upkeep, with nothing left to dodge it,
        /// is more than our LP can take): a competent duelist gets rid of it while they still can.
        /// </summary>
        private bool Doomed(FieldCard card, int me)
        {
            if (!UseRulesSense) return false;
            if (!card.IsFaceUp || card.OverlayCount > 0) return false;
            var info = _cards.Get(card.Code);
            return Drawbacks.UpkeepDamage(info) > 0 && SpareLifePoints(me) <= 0;
        }

        private static readonly System.Text.RegularExpressions.Regex BanishFromHand = new System.Text.RegularExpressions.Regex(
            @"banish 1 (DARK|LIGHT|EARTH|WATER|FIRE|WIND|Normal) [Mm]onster from your hand, or, if you do not have any in your hand, send your entire hand",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// A draw that then costs a monster of a kind from the hand, or the whole hand without one (Allure of Darkness,
        /// Common Charity): not without one in hand to pay with, nor when the only one is our only monster (a
        /// Blackwing CPU banished its only Blackwing that way and never summoned again).
        /// </summary>
        private string? HandCostBlunder(CardRef self, CardInfo info, int me)
        {
            var m = BanishFromHand.Match(info.Text);
            if (!m.Success) return null;
            uint attribute = m.Groups[1].Value switch
            {
                "EARTH" => 0x1, "WATER" => 0x2, "FIRE" => 0x4, "WIND" => 0x8, "LIGHT" => 0x10, "DARK" => 0x20, _ => 0,
            };
            bool Fits(CardInfo c) => (c.Type & CardType.Monster) != 0 && (attribute != 0 ? (c.Attribute & attribute) != 0 : (c.Type & 0x10) != 0);
            var hand = Board.Players[me].Hand.Select((card, i) => (card, i))
                .Where(h => h.card != null && !(self.Location == Location.Hand && h.i == self.Sequence))
                .Select(h => _cards.Get(h.card!.Code)).Where(c => c != null).Select(c => c!).ToList();
            var fitting = hand.Where(Fits).ToList();
            if (fitting.Count == 0)
                return $"{info.Name} would send our hand ({(hand.Count > 0 ? $"{hand.Count} card{(hand.Count == 1 ? "" : "s")} and " : "")}both draws) to the GY unless we draw a {m.Groups[1].Value} monster";
            if (fitting.Count == 1 && hand.Count(c => (c.Type & CardType.Monster) != 0) == 1)
                return $"{info.Name} would banish {fitting[0].Name}, our only monster, unless we draw another {m.Groups[1].Value} monster";
            return null;
        }

        /// <summary>Damage the opponent's cards deal us each time we activate something (Red-Eyes Flare Metal Dragon).</summary>
        private int ActivationTax(int me) =>
            Board.Players[1 - me].Monsters.Concat(Board.Players[1 - me].SpellTraps)
                .Where(c => c != null && c.IsFaceUp)
                .Sum(c => Drawbacks.ActivationTax(_cards.Get(c!.Code)));

        /// <summary>
        /// Why using a card would be a blunder because of what it costs in LP (its own cost or self-damage, plus any tax the
        /// opponent's cards charge per activation), or null.
        /// </summary>
        private string? CostBlunder(CardInfo info, int me)
        {
            if (!UseRulesSense) return null;
            // Cards off the top of our own Deck: never so many that we'd soon have nothing to draw. A big cost keeps as
            // many back as it takes (a second Pot of Desires at 18 cards would leave 8 and deck out a Sky Striker deck).
            int deckCost = Drawbacks.DeckCost(info);
            int deck = Board.Players[me].DeckCount;
            if (deckCost > 0 && deck - deckCost < Math.Max(MinimumDeck, deckCost)) return $"{info.Name} takes {deckCost} cards from our Deck and only {deck} are left";
            // Damage to both players (Ring of Destruction): not when it would take us out with them.
            int lp0 = Board.Players[me].LifePoints;
            if (Drawbacks.HurtsBothPlayers(info) && OpponentBestAttack(me) >= lp0)
                return $"{info.Name} hurts both players, and their best monster has {OpponentBestAttack(me)} ATK against our {lp0} LP";
            int cost = Math.Max(Drawbacks.LifePointCost(info), UseSelfDamageSense ? Drawbacks.SelfDamage(info) : 0);
            int tax = ActivationTax(me);
            if (cost + tax <= 0) return null;
            int lp = Board.Players[me].LifePoints;
            if (cost + tax >= lp)
                return tax > 0 ? $"{info.Name} would cost {cost + tax} LP with the opponent's {tax}-per-activation burn, and we have {lp}"
                    : $"{info.Name} costs {cost} LP and we have {lp}";
            if (cost + tax >= SpareLifePoints(me)) return $"{info.Name} costs {cost + tax} LP, and our own cards will take {OwnUpkeep(me)} more";
            return null;
        }

        /// <summary>
        /// Counts the damage an effect deals us when its text leaves out "you" ("and if you do, take 400 damage": Zephyros the
        /// Elite killed Fern Vasquez at 300 LP in the misplay hunt). Off only for its A/B.
        /// </summary>
        public bool UseSelfDamageSense { get; set; } = true;

        /// <summary>Whether a chain option is a blunder (only the LP cost matters there).</summary>
        private bool ChainBlunder(ChainOption o, int me, IEnumerable<CardRef> available) => UseRulesSense && _cards.Get(o.Card.Code) is CardInfo info
            && (CostBlunder(info, me) != null || SupportBlunder(info, me) != null
                || !SpellTrapGraveEffect(o.Card, info) && RemovalBlunder(o.Card, info, me) != null
                || TaxBlunder(o.Card, info, me, available) != null);

        /// <summary>
        /// A Spell/Trap's effect from the GY or banishment: not the removal it was played for (an Unchained Trap destroyed
        /// while Set summons from the Deck), so removal sense doesn't apply to it.
        /// </summary>
        private static bool SpellTrapGraveEffect(CardRef at, CardInfo info) =>
            (info.Type & CardType.Monster) == 0 && (at.Location & (Location.Grave | Location.Removed)) != 0;

        /// <summary>
        /// Removal that could only hit our own cards right now: Mystical Space Typhoon ("1 Spell/Trap on the field") with
        /// no Spell/Trap of the opponent's, Mekk-Knight Red Moon ("a face-up monster in this card's column") with only our
        /// own monster in its column. (Removal of "your opponent's" cards can't be activated without a target anyway.)
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex FusionByMaterials = new System.Text.RegularExpressions.Regex(
            @"Fusion Summon 1 [^.]*by (banishing|shuffling) (the )?Fusion Materials", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private string? RemovalBlunder(CardRef at, CardInfo info, int me)
        {
            if (!UseRulesSense) return null;
            if (ThresholdBlunder(info, me) is string reach) return reach;
            var t = CardTraits.Of(info);
            var text = info.Text;
            // "1 card on the field" (Dark Armed Dragon, Raigeki Break): either kind.
            bool anyCard = text.Contains("card on the field") || text.Contains("cards on the field");
            if (!t.Has(Trait.MonsterRemoval) && !t.Has(Trait.SpellTrapRemoval) && !anyCard) return null;
            if (t.Has(Trait.BoardWipe) || DestroysAllSpellTraps(info) || text.Contains("all cards on the field")) return null;
            // Destroying a card of our own is the play itself (Unchained Twins, Fire King Island: it summons or floats).
            if (OwnTarget.IsMatch(text)) return null;
            // A Fusion Summon that banishes or shuffles away its materials (Lubellion, Albion) uses our own cards on purpose.
            // (Only that wording: Mirrorjade's banish names Fusion Materials too, and it does hit our own monster.)
            if (FusionByMaterials.IsMatch(text)) return null;
            if (_playbook?.TargetsOwn(info.Code) == true) return null; // the deck's own plan (Radiant Typhoon's MST)
            // A Pendulum monster activated from the hand is being placed as a Scale, not using its Pendulum Effect yet.
            if ((info.Type & 0x1000000) != 0 && (info.Type & CardType.Monster) != 0 && at.Location == Location.Hand) return null;
            if (!text.Contains("destroy") && !text.Contains("banish")) return null; // Book of Moon on our own monster is a trick
            int opp = 1 - me;
            bool faceUp = text.Contains("face-up monster");
            bool Monster(uint seq, FieldCard m, int? column) =>
                (!faceUp || m.IsFaceUp) && (column == null || Presentation.Columns.Of(opp, Location.MZone, (int)seq) == column);
            bool SpellTrap(int seq, int? column) => column == null ? seq < 6 : seq < 5 && Presentation.Columns.Of(opp, Location.SZone, seq) == column;
            int? col = text.Contains("in this card's column") ? Presentation.Columns.Of(at.Controller, at.Location & 0x7f, (int)at.Sequence) : null;
            var theirSpellTraps = Board.Players[opp].SpellTraps;
            if (SpentOnResolving(at, info, me)) return $"{info.Name} could only hit a card that resolves anyway";
            if (col == null && !text.Contains("on the field")) return null;
            bool hits = (t.Has(Trait.MonsterRemoval) || anyCard) && Monsters(opp).Any(m => Monster(m.seq, m.card, col))
                        || (t.Has(Trait.SpellTrapRemoval) || anyCard) && Enumerable.Range(0, theirSpellTraps.Count).Any(i => theirSpellTraps[i] != null && SpellTrap(i, col));
            return hits ? null : col != null ? $"{info.Name} has nothing of the opponent's in its column" : $"{info.Name} could only hit our own cards";
        }

        /// <summary>
        /// The removal-timing sense (on with rules sense; off on one side only for the A/B that measures it). Off, the
        /// CPU plays as before it: spends removal on a card that resolves anyway, and rates a Normal Spell that stays on
        /// the field (Swords of Revealing Light) as one that's leaving.
        /// </summary>
        public bool UseRemovalTiming { get; set; } = true;

        /// <summary>
        /// Pure Spell/Trap removal (Mystical Space Typhoon, Dust Tornado, Twin Twisters: nothing else in its text) whose
        /// only targets of the opponent's are their links waiting on the chain that resolve just the same off the field
        /// (Pot of Greed, Dark Hole, Monster Reborn, Reckless Greed): destroying one there doesn't stop it, so the removal
        /// is a card spent for nothing. (The Beginner CPUs chained MST to Dark Hole.) A Set card, a Continuous, Field or
        /// Equip card, a card that stays (Swords of Revealing Light) or needs to (Shade Brigandine summons itself) is a
        /// real target. Not when our card is going anyway: one of their links targets it, or wipes Spells/Traps
        /// (Dust Tornado answering the MST aimed at it, or Heavy Storm, costs nothing).
        /// </summary>
        private bool SpentOnResolving(CardRef at, CardInfo info, int me)
        {
            if (!UseRemovalTiming || !CardTraits.PureSpellTrapRemoval(info) || TargetedByOpponent(at, me)) return false;
            if (_ctx.Chain.Any(l => l.Controller != me && _cards.Get(l.Code) is CardInfo wipe && CardTraits.WipesSpellTraps(wipe))) return false;
            int opp = 1 - me;
            bool faceUpOnly = info.Text.Contains("face-up Spell"), setOnly = info.Text.Contains("Set Spell");
            var theirs = Board.Players[opp].SpellTraps;
            var reachable = Enumerable.Range(0, Math.Min(6, theirs.Count))
                .Where(i => theirs[i] is FieldCard c && (!faceUpOnly || c.IsFaceUp) && (!setOnly || !c.IsFaceUp)).ToList();
            return reachable.Count > 0 && reachable.All(i => theirs[i]!.IsFaceUp && WaitingResolvesOffField(opp, i, theirs[i]!.Code));
        }

        /// <summary>The opponent's card in this Spell & Trap Zone is a link on the chain that resolves the same off the field.</summary>
        private bool WaitingResolvesOffField(int controller, int seq, uint code) =>
            OnChain(controller, seq, code) && _cards.Get(code) is CardInfo info && CardTraits.ResolvesOffField(info);

        /// <summary>The card in this Spell & Trap Zone is a link of the current chain (activated there).</summary>
        private bool OnChain(int controller, int seq, uint code) =>
            _ctx.Chain.Any(l => l.Code == code && (l.Card.Controller & 1) == (controller & 1) && (l.Card.Location & 0x7f) == Location.SZone
                                && l.Card.Sequence == seq);

        /// <summary>Our card on the field is the target of one of the opponent's links on the chain (it's about to go).</summary>
        private bool TargetedByOpponent(CardRef at, int me) =>
            (at.Location & (Location.MZone | Location.SZone)) != 0
            && _ctx.Chain.Any(l => l.Controller != me && l.Targets.Any(x => (x.Controller & 1) == (at.Controller & 1)
                                                                            && (x.Location & 0x7f) == (at.Location & 0x7f) && x.Sequence == at.Sequence));

        /// <summary>A support effect (Eagle Booster: "unaffected by card effects") with no face-up monster of ours to support.</summary>
        private string? SupportBlunder(CardInfo info, int me) =>
            UseRulesSense && Supports(info) && !Monsters(me).Any(m => m.card.IsFaceUp)
                ? $"{info.Name} could only help the opponent's monster" : null;

        /// <summary>A "pay LP or ..." choice: never pay what we can't survive.</summary>
        private bool PaysWhatWeLack(EffectYesNoPrompt p) =>
            _cards.Get(p.Card.Code) is CardInfo info && CostBlunder(info, p.Player) != null;
    }
}
