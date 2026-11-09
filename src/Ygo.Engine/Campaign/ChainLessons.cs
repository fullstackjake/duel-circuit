using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Campaign
{
    public enum LessonOutcome { Playing, Passed, Failed }

    /// <summary>
    /// Gus's chain lessons: three short duels from a set position that teach how chains work. A 3-link chain that
    /// resolves backwards, catching the End Phase with the response mode on, and chaining to your own card. Each lesson
    /// knows its board, what Gus plays, and how to judge what you did from the core's own messages.
    /// </summary>
    public sealed class ChainLesson
    {
        public string Id = "";
        public string Title = "";
        /// <summary>The idea in one line, for the lesson list.</summary>
        public string Teaches = "";
        /// <summary>The card shown beside it in the list.</summary>
        public uint Showcase;
        /// <summary>What Gus says before the lesson.</summary>
        public List<string> Intro = new List<string>();
        /// <summary>Gus's line when you get it right.</summary>
        public string PassLine = "";
        public List<PlacedCard> Setup = new List<PlacedCard>();
        public Deck[] Decks = { new Deck(), new Deck() };
        /// <summary>Gus's script: cards he activates (or chains) whenever he can, cards he sets, monsters he Normal Summons,
        /// and whether he attacks.</summary>
        public uint[] GusActivates = new uint[0];
        public uint[] GusSets = new uint[0];
        public uint[] GusSummons = new uint[0];
        public bool GusAttacks;
        /// <summary>Both players' Life Points at the start.</summary>
        public uint StartingLP = 8000;
        internal System.Func<LessonJudge> NewJudge = () => new LessonJudge();

        public LessonJudge Judge() => NewJudge();

        /// <summary>Marks a passed lesson in <see cref="Profile.Seen"/>.</summary>
        public string SeenKey => "lesson:" + Id;

        public DuelRecipe Recipe(ICardDataProvider cards, IScriptProvider scripts) => new DuelRecipe
        {
            Cards = cards, Scripts = scripts,
            Settings = new DuelSettings { Seed = 1, StartingHand = 0, StartingLP = StartingLP },
            Deck0 = Decks[0].Clone(), Deck1 = Decks[1].Clone(),
            Setup = Setup.ToList(),
        };

        public ScriptedDuelist Gus(ICardDataProvider cards)
        {
            var gus = new ScriptedDuelist(1, cards);
            foreach (var c in GusActivates) gus.Activate.Add(c);
            foreach (var c in GusSets) gus.Set.Add(c);
            foreach (var c in GusSummons) gus.NormalSummon.Add(c);
            gus.Attacks = GusAttacks;
            return gus;
        }
    }

    /// <summary>
    /// Watches a lesson duel and decides when it's passed or failed, with a hint for the next step meanwhile.
    /// </summary>
    public class LessonJudge
    {
        protected readonly ChainTracker Chain = new ChainTracker();
        public LessonOutcome Outcome { get; protected set; }
        /// <summary>What to do next (while playing), or why it went wrong (failed).</summary>
        public string Hint { get; protected set; } = "";
        /// <summary>
        /// What the hint is about, for the board to point at: "hand:&lt;code&gt;" or "field:&lt;code&gt;" (your card),
        /// "phase" (the turn disc), "lp:&lt;player&gt;", or "" for nothing.
        /// </summary>
        public string Pointer { get; protected set; } = "";
        public int Turn { get; private set; }

        public void Handle(byte[] message)
        {
            if (Outcome != LessonOutcome.Playing) return;
            Chain.Handle(message);
            if (message[0] == Msg.NewTurn) Turn++;
            Observe(message);
        }

        protected virtual void Observe(byte[] message) { }

        protected void Pass(string hint) { Outcome = LessonOutcome.Passed; Hint = hint; Pointer = ""; }
        protected void Fail(string why) { Outcome = LessonOutcome.Failed; Hint = why; Pointer = ""; }

        protected static (uint code, CardRef from, CardRef to)? Move(byte[] message)
        {
            if (message[0] != Msg.Move) return null;
            var r = new MessageReader(message, 1);
            var code = r.U32();
            return (code, r.LocInfo(code), r.LocInfo(code));
        }
    }

    public static class ChainLessons
    {
        public const uint ReinforcementOfTheArmy = 32807846, AshBlossom = 14558127, CalledByTheGrave = 24224830,
            MysticalSpaceTyphoon = 5318639, TorrentialTribute = 53582587, DarkHole = 53129443,
            InterdimensionalMatterTransporter = 36261276, BlueEyes = 89631139, SummonedSkull = 70781052,
            DarkMagician = 46986414, JarOfGreed = 83968380, Gearfried = 423705, MaraudingCaptain = 2460565, CelticGuardian = 91152256,
            GiantSoldierOfStone = 13039848, AxeOfDespair = 40619825, SakuretsuArmor = 56120475, BattleOx = 5053103;

        private static Deck Filler(params uint[] extra)
        {
            var d = new Deck { Name = "lesson" };
            d.Main.AddRange(extra);
            while (d.Main.Count < 10) d.Main.Add(GiantSoldierOfStone);
            return d;
        }

        public static readonly IReadOnlyList<ChainLesson> All = new[]
        {
            new ChainLesson
            {
                Id = "last-in-first-out",
                Title = "Last in, first out",
                Teaches = "A chain of three resolves backwards: the last card in goes first.",
                Showcase = CalledByTheGrave,
                Intro =
                {
                    "Every card you play can be answered. Answer the answer, and you've got a chain.",
                    "Activate Reinforcement of the Army. I'll answer with Ash Blossom. Then you answer me.",
                    "Here's the trick: a chain resolves backwards. The last card in goes first.",
                },
                PassLine = "Link 3 went first and stopped my Ash, so your search at link 1 went through. Last in, first out.",
                Setup =
                {
                    new PlacedCard(0, ReinforcementOfTheArmy, Location.Hand),
                    new PlacedCard(0, CalledByTheGrave, Location.Hand),
                    new PlacedCard(1, AshBlossom, Location.Hand),
                },
                Decks = new[] { Filler(Gearfried, MaraudingCaptain, CelticGuardian), Filler() },
                GusActivates = new[] { AshBlossom },
                NewJudge = () => new LastInFirstOut(),
            },
            new ChainLesson
            {
                Id = "end-phase",
                Title = "Catch the End Phase",
                Teaches = "Chain Auto vs ON: using a set card at the very end of your opponent's turn.",
                Showcase = JarOfGreed,
                Intro =
                {
                    "A set card is a question your opponent has to answer. Keep it set as long as you can.",
                    "Your Jar of Greed draws a card. Use it at the very end of my turn: it's a bluff all turn, and you'll draw a fresh card for yours.",
                    "On Auto the game only stops when someone plays something, or for cards made for a moment, like Mystical Space Typhoon in the End Phase. Jar of Greed isn't one.",
                    "So end your turn, then turn Chain to ON (click the Chain button, bottom right). It asks at every window. Click Don't chain until it says End Phase.",
                },
                PassLine = "You kept me guessing all turn and drew at the last moment. Chain ON is for the moments nobody announces.",
                Setup =
                {
                    new PlacedCard(0, JarOfGreed, Location.SZone, 2, Position.FaceDownAttack | Position.FaceDownDefense),
                },
                Decks = new[] { Filler(), Filler() },
                NewJudge = () => new EndPhase(),
            },
            new ChainLesson
            {
                Id = "your-own-chain",
                Title = "Chain to your own card",
                Teaches = "Answering your own Dark Hole to save your Blue-Eyes.",
                Showcase = InterdimensionalMatterTransporter,
                Intro =
                {
                    "You can answer your own cards too. Watch.",
                    "Dark Hole destroys every monster, your Blue-Eyes included.",
                    "So chain your Interdimensional Matter Transporter to your own Dark Hole and banish Blue-Eyes first. It comes back in the End Phase.",
                    "After your own card the game asks only if something of yours can answer it. If it ever doesn't, hold Shift (or LT) as you play.",
                },
                PassLine = "My monsters are gone and your Blue-Eyes is coming back. That's chaining to your own card.",
                Setup =
                {
                    new PlacedCard(0, BlueEyes, Location.MZone, 2),
                    new PlacedCard(0, InterdimensionalMatterTransporter, Location.SZone, 2, Position.FaceDownAttack | Position.FaceDownDefense),
                    new PlacedCard(0, DarkHole, Location.Hand),
                    new PlacedCard(1, SummonedSkull, Location.MZone, 1),
                    new PlacedCard(1, DarkMagician, Location.MZone, 3),
                },
                Decks = new[] { Filler(), Filler() },
                NewJudge = () => new YourOwnChain(),
            },
        };

        /// <summary>
        /// Gus's guided first duel, for a brand-new player: the board, the phases, and answering a card from the prompt.
        /// Two turns at 2000 Life Points each: summon and set, answer his attack with a Trap, then equip and attack
        /// directly for the win. Offered after the campaign's opening scene, and first in the Lessons list.
        /// </summary>
        public static readonly ChainLesson FirstDuel = new ChainLesson
        {
            Id = "first-duel",
            Title = "Your first duel",
            Teaches = "The board, the phases, and answering a card: a short guided duel.",
            Showcase = CelticGuardian,
            Intro =
            {
                "A practice duel, you and me. 2000 Life Points each, so it's quick. Bring mine to 0 and you win.",
                "Your side is the bottom half. Monsters go in the front row, Spells and Traps in the back row, and your hand sits along the bottom.",
                "A turn runs in phases: Draw, Standby, Main 1, Battle, Main 2, End. You play cards in your Main Phases and attack in the Battle Phase. The disc on the right shows where we are.",
                "In my lessons the green box up top says what to do next, and a gold ring points at it. In a real duel, the cards you can play glow gold. Go on, you go first.",
            },
            PassLine = "Summon, set, answer, attack. That's a duel. The rest is just more cards.",
            StartingLP = 2000,
            Setup =
            {
                new PlacedCard(0, CelticGuardian, Location.Hand),
                new PlacedCard(0, SakuretsuArmor, Location.Hand),
                new PlacedCard(0, AxeOfDespair, Location.Hand),
                new PlacedCard(1, BattleOx, Location.Hand),
            },
            Decks = new[] { Filler(), Filler() },
            GusSummons = new[] { BattleOx },
            GusAttacks = true,
            NewJudge = () => new FirstDuelJudge(),
        };

        /// <summary>A lesson by id, the first duel included.</summary>
        public static ChainLesson? Find(string id) => id == FirstDuel.Id ? FirstDuel : All.FirstOrDefault(l => l.Id == id);

        /// <summary>
        /// The first duel, step by step: each hint follows what you've done so far (in any order), and points at the card
        /// or control it's about.
        /// </summary>
        private sealed class FirstDuelJudge : LessonJudge
        {
            private bool _summoned, _set, _armorUsed, _attacked, _equipped, _battle;
            private ushort _phase;

            public FirstDuelJudge() { Next(); }

            private void Say(string hint, string pointer) { Hint = hint; Pointer = pointer; }

            /// <summary>The hint for where you are now.</summary>
            private void Next()
            {
                if (Turn <= 1)
                {
                    if (!_summoned) Say("Your hand is along the bottom. Click Celtic Guardian, then Summon: monsters fight from the front row.", "hand:" + CelticGuardian);
                    else if (!_set) Say("Now click Sakuretsu Armor, then Set. A Trap waits face-down in the back row for the right moment.", "hand:" + SakuretsuArmor);
                    else Say("Going first, you can't attack on turn 1. Click the turn disc, then End, to end your turn.", "phase");
                }
                else if (Turn == 2)
                {
                    if (_armorUsed) Say("Your Trap answered his attack: that's a chain. Let his turn finish.", "");
                    else if (_attacked) Say("Gus's attack is over. Let his turn finish.", "");
                    else Say("Gus's turn: he draws, plays in his Main Phase, then battles. Watch for his attack.", "lp:1");
                }
                else if (!_summoned) Say("Your turn, and you drew a card. Click Celtic Guardian, then Summon.", "hand:" + CelticGuardian);
                else if (!_equipped) Say("Your turn, and you drew a card. Click Axe of Despair, then Activate, and equip it to Celtic Guardian: +1000 ATK.", "hand:" + AxeOfDespair);
                else if (!_battle) Say("Click the turn disc, then Battle, to go to the Battle Phase.", "phase");
                else Say("Click Celtic Guardian, then Attack. Gus has no monsters, so it hits his Life Points directly.", "field:" + CelticGuardian);
            }

            protected override void Observe(byte[] message)
            {
                var r = new MessageReader(message, 1);
                switch (message[0])
                {
                    case Msg.Win:
                        if (r.U8() == 0) Pass("Gus's Life Points hit 0: you won your first duel.");
                        else Fail("Your Life Points hit 0. Keep Sakuretsu Armor for Gus's attack, then hit back on your turn.");
                        return;
                    case Msg.NewTurn:
                        if (Turn >= 4)
                        {
                            Fail("Your turn ended before Gus's Life Points hit 0. Equip Axe of Despair, go to the Battle Phase, and attack directly.");
                            return;
                        }
                        break;
                    case Msg.NewPhase:
                        _phase = r.U16();
                        if (Turn >= 3 && (_phase & (Phase.BattleStart | Phase.BattleStep | Phase.Battle)) != 0) _battle = true;
                        break;
                    case Msg.Summoning:
                        if (r.U32() == CelticGuardian) _summoned = true;
                        break;
                    case Msg.Set:
                        if (r.U32() == SakuretsuArmor) _set = true;
                        break;
                    case Msg.Attack:
                    {
                        var attacker = r.LocInfo(0);
                        if (attacker.Controller == 1 && !_armorUsed)
                        {
                            _attacked = true;
                            Say("Gus attacks! The game asks if you want to answer. Choose Sakuretsu Armor to destroy his attacker.", "");
                            return;
                        }
                        break;
                    }
                    case Msg.Chaining:
                    {
                        var code = Chain.Links.LastOrDefault()?.Code ?? 0;
                        if (code == SakuretsuArmor) _armorUsed = true;
                        if (code == AxeOfDespair) _equipped = true;
                        break;
                    }
                    case Msg.Move:
                        if (Move(message) is { } m && m.code == CelticGuardian && (m.from.Location & 0x7f) == Location.MZone
                            && (m.to.Location & 0x7f) == Location.Grave)
                        {
                            Fail("Battle Ox destroyed Celtic Guardian. When Gus attacks, the game asks if you want to answer: choose Sakuretsu Armor.");
                            return;
                        }
                        break;
                    default:
                        return;
                }
                Next();
            }
        }



        private sealed class LastInFirstOut : LessonJudge
        {
            public LastInFirstOut() { Hint = "Activate Reinforcement of the Army."; }

            protected override void Observe(byte[] message)
            {
                if (message[0] == Msg.NewTurn && Turn > 1)
                {
                    Fail("The turn ended. Activate Reinforcement of the Army on your turn.");
                    return;
                }
                if (message[0] == Msg.Chaining)
                {
                    var last = Chain.Links.Last();
                    Hint = last.Code == AshBlossom ? "Gus chained Ash Blossom. Answer it: chain Called by the Grave and banish Ash from his GY."
                        : last.Code == CalledByTheGrave ? "Three links. Watch them resolve: 3, then 2, then 1."
                        : Hint;
                }
                if (message[0] != Msg.ChainEnd) return;
                var links = Chain.LastChain;
                var search = links.FirstOrDefault(l => l.Code == ReinforcementOfTheArmy);
                if (search == null) return;
                if (search.State is LinkState.Negated or LinkState.EffectNegated)
                    Fail("Ash resolved first and stopped your search. Chain Called by the Grave to Ash, so it resolves before Ash does.");
                else if (links.Count >= 3) Pass("Link 3 resolved first, negating Ash at link 2, so link 1 found your Warrior.");
            }
        }

        private sealed class EndPhase : LessonJudge
        {
            private ushort _phase;

            public EndPhase() { Hint = "End your turn. Then turn Chain to ON and wait for Gus's End Phase."; }

            protected override void Observe(byte[] message)
            {
                var r = new MessageReader(message, 1);
                if (message[0] == Msg.NewPhase) _phase = r.U16();
                if (message[0] == Msg.NewTurn && Turn == 2) Hint = "Gus's turn. With Chain ON, click Don't chain until the End Phase, then activate Jar of Greed.";
                if (message[0] == Msg.NewTurn && Turn >= 3)
                {
                    Fail("Gus's turn ended without a pause: on Auto, nothing he did needed an answer. Click the Chain button (bottom right) until it says ON, then try again.");
                    return;
                }
                if (message[0] != Msg.Chaining || Chain.Links.Last().Code != JarOfGreed) return;
                if (Turn < 2) Fail("That was on your own turn. Keep it set as a bluff, and draw at the end of Gus's turn.");
                else if (_phase == Phase.End) Pass("Jar of Greed in Gus's End Phase: a bluff all turn, and a fresh card for yours.");
                else Pass("That works. Even better, wait for the End Phase: the longer it stays set, the longer Gus has to wonder.");
            }
        }

        private sealed class YourOwnChain : LessonJudge
        {
            private bool _lost, _banished;

            public YourOwnChain() { Hint = "Activate Dark Hole, then chain Interdimensional Matter Transporter to it."; }

            protected override void Observe(byte[] message)
            {
                if (message[0] == Msg.NewTurn && Turn > 1)
                {
                    Fail("The turn ended. Activate Dark Hole and chain Interdimensional Matter Transporter to it.");
                    return;
                }
                if (Move(message) is { } m && m.code == BlueEyes && (m.from.Location & 0x7f) == Location.MZone)
                {
                    if ((m.to.Location & 0x7f) == Location.Grave) _lost = true;
                    if ((m.to.Location & 0x7f) == Location.Removed) _banished = true;
                }
                if (message[0] != Msg.ChainEnd) return;
                var links = Chain.LastChain;
                if (links.All(l => l.Code != DarkHole)) return;
                if (_lost)
                    Fail("Dark Hole took Blue-Eyes too. When the game asks \"Chain?\" after your Dark Hole, choose Interdimensional Matter Transporter.");
                else if (_banished && links.Count >= 2) Pass("Your Transporter resolved first and Blue-Eyes left the field before Dark Hole hit.");
                else if (_banished) Pass("Banishing Blue-Eyes first works too. Chaining does it in one go, before your opponent can answer in between.");
            }
        }
    }
}
