namespace Ygo.Engine
{
    // Subset of ocgapi_constants.h. Add values here as the game needs them.

    public static class Location
    {
        public const uint Deck = 0x01;
        public const uint Hand = 0x02;
        public const uint MZone = 0x04;
        public const uint SZone = 0x08;
        public const uint Grave = 0x10;
        public const uint Removed = 0x20;
        public const uint Extra = 0x40;
        public const uint Overlay = 0x80;

        public static string Name(uint loc)
        {
            switch (loc & 0x7f)
            {
                case Deck: return "Deck";
                case Hand: return "Hand";
                case MZone: return "Monster Zone";
                case SZone: return "Spell/Trap Zone";
                case Grave: return "GY";
                case Removed: return "Banished";
                case Extra: return "Extra Deck";
                default: return $"loc 0x{loc:x}";
            }
        }
    }

    public static class Position
    {
        public const uint FaceUpAttack = 0x1;
        public const uint FaceDownAttack = 0x2;
        public const uint FaceUpDefense = 0x4;
        public const uint FaceDownDefense = 0x8;
    }

    public static class CardType
    {
        public const uint Monster = 0x1;
        public const uint Spell = 0x2;
        public const uint Trap = 0x4;
        public const uint Normal = 0x10;
        public const uint Effect = 0x20;
        public const uint QuickPlay = 0x10000;
        public const uint Counter = 0x100000;
        public const uint Fusion = 0x40;
        public const uint Ritual = 0x80;
        public const uint Synchro = 0x2000;
        public const uint Token = 0x4000;
        public const uint Xyz = 0x800000;
        public const uint Pendulum = 0x1000000;
        public const uint Link = 0x4000000;

        public const uint ExtraDeckTypes = Fusion | Synchro | Xyz | Link;
    }

    public static class Phase
    {
        public const ushort Draw = 0x01;
        public const ushort Standby = 0x02;
        public const ushort Main1 = 0x04;
        public const ushort BattleStart = 0x08;
        public const ushort BattleStep = 0x10;
        public const ushort Damage = 0x20;
        public const ushort DamageCal = 0x40;
        public const ushort Battle = 0x80;
        public const ushort Main2 = 0x100;
        public const ushort End = 0x200;

        public static string Name(ushort phase)
        {
            switch (phase)
            {
                case Draw: return "Draw Phase";
                case Standby: return "Standby Phase";
                case Main1: return "Main Phase 1";
                case BattleStart: return "Battle Phase";
                case BattleStep: return "Battle Step";
                case Damage: return "Damage Step";
                case DamageCal: return "Damage Calculation";
                case Battle: return "End of Battle";
                case Main2: return "Main Phase 2";
                case End: return "End Phase";
                default: return $"phase 0x{phase:x}";
            }
        }
    }

    public static class DuelFlags
    {
        public const ulong PZone = 0x800;
        public const ulong EMZone = 0x2000;
        public const ulong FsxMMZone = 0x4000;
        public const ulong TrapMonstersNotUseZone = 0x8000;
        public const ulong TriggerOnlyInLocation = 0x20000;

        /// <summary>
        /// A Deck an effect looked through is shuffled after the effect resolves, found or not (rulebook v9; card text that
        /// keeps the Deck's order wins: top or bottom cards, DisableShuffleCheck). Duel Circuit's own patch to the core
        /// (patches/ygopro-core/0003): upstream shuffles only when a card left the Deck. In <see cref="ModeMR5"/>, so every new
        /// duel has it; a replay keeps the flags it was recorded with, so older replays play back as they were.
        /// See docs/dev/SHUFFLE-2026-11-07.md.
        /// </summary>
        public const ulong ShuffleAfterDeckLook = 0x4000000000000000UL;

        /// <summary>
        /// Current Master Rule (MR5): what modern TCG/OCG play uses, plus <see cref="ShuffleAfterDeckLook"/>; the flags every
        /// new duel starts from.
        /// </summary>
        public const ulong ModeMR5 = PZone | EMZone | FsxMMZone | TrapMonstersNotUseZone | TriggerOnlyInLocation | ShuffleAfterDeckLook;

        /// <summary>
        /// Player 1 takes the first turn. Duel Circuit's own patch to the core (patches/ygopro-core, built by
        /// tools/build_core.sh): upstream always starts with player 0.
        /// </summary>
        public const ulong SecondPlayerStarts = 0x8000000000000000UL;
    }

    public enum DuelStatus
    {
        End = 0,
        Awaiting = 1,
        Continue = 2,
    }

    public static class Msg
    {
        public const byte Retry = 1;
        public const byte Hint = 2;
        public const byte Start = 4;
        public const byte Win = 5;
        public const byte SelectBattleCmd = 10;
        public const byte SelectIdleCmd = 11;
        public const byte SelectEffectYn = 12;
        public const byte SelectYesNo = 13;
        public const byte SelectOption = 14;
        public const byte SelectCard = 15;
        public const byte SelectChain = 16;
        public const byte SelectPlace = 18;
        public const byte SelectPosition = 19;
        public const byte SelectTribute = 20;
        public const byte SortChain = 21;
        public const byte SelectCounter = 22;
        public const byte SelectSum = 23;
        public const byte SelectDisfield = 24;
        public const byte SortCard = 25;
        public const byte SelectUnselectCard = 26;
        public const byte ConfirmCards = 31;
        public const byte ShuffleDeck = 32;
        public const byte ShuffleHand = 33;
        public const byte NewTurn = 40;
        public const byte NewPhase = 41;
        public const byte Move = 50;
        public const byte PosChange = 53;
        public const byte Set = 54;
        public const byte Summoning = 60;
        public const byte SpSummoning = 62;
        public const byte FlipSummoning = 64;
        public const byte Chaining = 70;
        public const byte ChainSolving = 72;
        public const byte ChainSolved = 73;
        public const byte ChainEnd = 74;
        public const byte ChainNegated = 75;
        public const byte ChainDisabled = 76;
        public const byte Draw = 90;
        public const byte Damage = 91;
        public const byte Recover = 92;
        public const byte LpUpdate = 94;
        public const byte PayLpCost = 100;
        public const byte Attack = 110;
        public const byte AttackDisabled = 112;
        public const byte DamageStepEnd = 114;
        public const byte RockPaperScissors = 132;
        public const byte CardHint = 160;
        public const byte AnnounceRace = 140;
        public const byte AnnounceAttrib = 141;
        public const byte AnnounceCard = 142;
        public const byte AnnounceNumber = 143;
        /// <summary>Tag duels: a team's other duelist takes over (their Deck, hand and Extra Deck swap in).</summary>
        public const byte TagSwap = 161;
    }
}
