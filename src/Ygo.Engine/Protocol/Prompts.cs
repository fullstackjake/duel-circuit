using System;
using System.Collections.Generic;

namespace Ygo.Engine.Protocol
{
    // Typed versions of every message where the core waits for a player decision.
    // Layouts follow ygopro-core playerop.cpp; field order matters.

    public abstract class Prompt
    {
        public byte Player;
    }

    public sealed class ChainOption
    {
        public CardRef Card;
        public ulong Description;
        public byte ClientMode;
    }

    public sealed class IdlePrompt : Prompt
    {
        public List<CardRef> Summonable = new List<CardRef>();
        public List<CardRef> SpSummonable = new List<CardRef>();
        public List<CardRef> Repositionable = new List<CardRef>();
        public List<CardRef> MonsterSettable = new List<CardRef>();
        public List<CardRef> SpellSettable = new List<CardRef>();
        public List<ChainOption> Activatable = new List<ChainOption>();
        public bool CanBattlePhase;
        public bool CanEndPhase;
        public bool CanShuffle;
    }

    public sealed class BattlePrompt : Prompt
    {
        public List<ChainOption> Activatable = new List<ChainOption>();
        public List<CardRef> Attackers = new List<CardRef>();
        public List<bool> CanDirectAttack = new List<bool>();
        public bool CanMain2;
        public bool CanEndPhase;
    }

    public sealed class EffectYesNoPrompt : Prompt
    {
        public CardRef Card;
        public ulong Description;
    }

    public sealed class YesNoPrompt : Prompt
    {
        public ulong Description;
    }

    public sealed class OptionPrompt : Prompt
    {
        public List<ulong> Options = new List<ulong>();
    }

    public sealed class SelectCardPrompt : Prompt
    {
        public bool Cancelable;
        public int Min, Max;
        public List<CardRef> Cards = new List<CardRef>();
    }

    public sealed class TributePrompt : Prompt
    {
        public bool Cancelable;
        public int Min, Max;
        public List<CardRef> Cards = new List<CardRef>();
        public List<byte> ReleaseValues = new List<byte>();
    }

    public sealed class SelectUnselectPrompt : Prompt
    {
        public bool Finishable;
        public bool Cancelable;
        public int Min, Max;
        public List<CardRef> Selectable = new List<CardRef>();
        public List<CardRef> Unselectable = new List<CardRef>();
    }

    public sealed class ChainPrompt : Prompt
    {
        public byte SpeCount;
        public bool Forced;
        public List<ChainOption> Chains = new List<ChainOption>();
    }

    public sealed class PlacePrompt : Prompt
    {
        public int Count;
        /// <summary>Bitmask of zones that are NOT available. Low 16 bits = prompted player, high 16 = opponent.</summary>
        public uint BlockedZones;
        public bool DisableField;
    }

    public sealed class PositionPrompt : Prompt
    {
        public uint Code;
        public uint Positions;
    }

    public sealed class CounterPrompt : Prompt
    {
        public ushort CounterType;
        public int Count;
        public List<CardRef> Cards = new List<CardRef>();
        public List<ushort> Counters = new List<ushort>();
    }

    public sealed class SumPrompt : Prompt
    {
        /// <summary>True: sum must equal Target exactly. False: sum must reach Target (ritual-style).</summary>
        public bool Exact;
        public int Target;
        public int Min, Max;
        public List<CardRef> MustCards = new List<CardRef>();
        public List<uint> MustValues = new List<uint>();
        public List<CardRef> Cards = new List<CardRef>();
        public List<uint> Values = new List<uint>();
    }

    public sealed class SortPrompt : Prompt
    {
        public bool IsChain;
        public List<CardRef> Cards = new List<CardRef>();
    }

    public sealed class AnnounceRacePrompt : Prompt
    {
        public int Count;
        public ulong Available;
    }

    public sealed class AnnounceAttributePrompt : Prompt
    {
        public int Count;
        public uint Available;
    }

    public sealed class AnnounceCardPrompt : Prompt
    {
        /// <summary>Reverse-polish filter program; see CardDeclaration.</summary>
        public List<ulong> Opcodes = new List<ulong>();
    }

    public sealed class AnnounceNumberPrompt : Prompt
    {
        public List<ulong> Options = new List<ulong>();
    }

    public sealed class RockPaperScissorsPrompt : Prompt { }

    public static class PromptParser
    {
        public static bool IsPrompt(byte type)
        {
            switch (type)
            {
                case Msg.SelectBattleCmd: case Msg.SelectIdleCmd: case Msg.SelectEffectYn: case Msg.SelectYesNo:
                case Msg.SelectOption: case Msg.SelectCard: case Msg.SelectChain: case Msg.SelectPlace:
                case Msg.SelectPosition: case Msg.SelectTribute: case Msg.SortChain: case Msg.SelectCounter:
                case Msg.SelectSum: case Msg.SelectDisfield: case Msg.SortCard: case Msg.SelectUnselectCard:
                case Msg.RockPaperScissors: case Msg.AnnounceRace: case Msg.AnnounceAttrib:
                case Msg.AnnounceCard: case Msg.AnnounceNumber:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Parses a full message (type byte first).</summary>
        public static Prompt Parse(byte[] message)
        {
            var type = message[0];
            var r = new MessageReader(message, 1);
            var player = r.U8();
            Prompt prompt = type switch
            {
                Msg.SelectIdleCmd => ParseIdle(r),
                Msg.SelectBattleCmd => ParseBattle(r),
                Msg.SelectEffectYn => ParseEffectYesNo(r),
                Msg.SelectYesNo => new YesNoPrompt { Description = r.U64() },
                Msg.SelectOption => ParseOptions(r, new OptionPrompt()),
                Msg.SelectCard => ParseSelectCard(r),
                Msg.SelectTribute => ParseTribute(r),
                Msg.SelectUnselectCard => ParseSelectUnselect(r),
                Msg.SelectChain => ParseChain(r),
                Msg.SelectPlace => new PlacePrompt { Count = r.U8(), BlockedZones = r.U32() },
                Msg.SelectDisfield => new PlacePrompt { Count = r.U8(), BlockedZones = r.U32(), DisableField = true },
                Msg.SelectPosition => new PositionPrompt { Code = r.U32(), Positions = r.U8() },
                Msg.SelectCounter => ParseCounter(r),
                Msg.SelectSum => ParseSum(r),
                Msg.SortCard => ParseSort(r, false),
                Msg.SortChain => ParseSort(r, true),
                Msg.AnnounceRace => new AnnounceRacePrompt { Count = r.U8(), Available = r.U64() },
                Msg.AnnounceAttrib => new AnnounceAttributePrompt { Count = r.U8(), Available = r.U32() },
                Msg.AnnounceCard => ParseAnnounceCard(r),
                Msg.AnnounceNumber => ParseAnnounceNumber(r),
                Msg.RockPaperScissors => new RockPaperScissorsPrompt(),
                _ => throw new NotSupportedException($"Message {type} is not a prompt"),
            };
            prompt.Player = player;
            return prompt;
        }

        private static CardRef ShortRef(MessageReader r, bool wideSequence)
        {
            var code = r.U32();
            var controller = r.U8();
            var location = r.U8();
            var sequence = wideSequence ? r.U32() : r.U8();
            return new CardRef(code, controller, location, sequence);
        }

        private static List<ChainOption> ParseShortChains(MessageReader r)
        {
            var list = new List<ChainOption>();
            var count = r.U32();
            for (int i = 0; i < count; i++)
                list.Add(new ChainOption { Card = ShortRef(r, true), Description = r.U64(), ClientMode = r.U8() });
            return list;
        }

        private static IdlePrompt ParseIdle(MessageReader r)
        {
            var p = new IdlePrompt();
            ReadRefs(r, p.Summonable, true);
            ReadRefs(r, p.SpSummonable, true);
            ReadRefs(r, p.Repositionable, false);
            ReadRefs(r, p.MonsterSettable, true);
            ReadRefs(r, p.SpellSettable, true);
            p.Activatable = ParseShortChains(r);
            p.CanBattlePhase = r.U8() != 0;
            p.CanEndPhase = r.U8() != 0;
            p.CanShuffle = r.U8() != 0;
            return p;
        }

        private static void ReadRefs(MessageReader r, List<CardRef> into, bool wideSequence)
        {
            var count = r.U32();
            for (int i = 0; i < count; i++) into.Add(ShortRef(r, wideSequence));
        }

        private static BattlePrompt ParseBattle(MessageReader r)
        {
            var p = new BattlePrompt { Activatable = ParseShortChains(r) };
            var count = r.U32();
            for (int i = 0; i < count; i++)
            {
                p.Attackers.Add(ShortRef(r, false));
                p.CanDirectAttack.Add(r.U8() != 0);
            }
            p.CanMain2 = r.U8() != 0;
            p.CanEndPhase = r.U8() != 0;
            return p;
        }

        private static EffectYesNoPrompt ParseEffectYesNo(MessageReader r)
        {
            var code = r.U32();
            return new EffectYesNoPrompt { Card = r.LocInfo(code), Description = r.U64() };
        }

        private static OptionPrompt ParseOptions(MessageReader r, OptionPrompt prompt)
        {
            var count = r.U8();
            for (int i = 0; i < count; i++) prompt.Options.Add(r.U64());
            return prompt;
        }

        private static SelectCardPrompt ParseSelectCard(MessageReader r)
        {
            var p = new SelectCardPrompt { Cancelable = r.U8() != 0, Min = (int)r.U32(), Max = (int)r.U32() };
            var count = r.U32();
            for (int i = 0; i < count; i++) p.Cards.Add(r.LocInfo(r.U32()));
            return p;
        }

        private static TributePrompt ParseTribute(MessageReader r)
        {
            var p = new TributePrompt { Cancelable = r.U8() != 0, Min = (int)r.U32(), Max = (int)r.U32() };
            var count = r.U32();
            for (int i = 0; i < count; i++)
            {
                p.Cards.Add(ShortRef(r, true));
                p.ReleaseValues.Add(r.U8());
            }
            return p;
        }

        private static SelectUnselectPrompt ParseSelectUnselect(MessageReader r)
        {
            var p = new SelectUnselectPrompt
            {
                Finishable = r.U8() != 0, Cancelable = r.U8() != 0, Min = (int)r.U32(), Max = (int)r.U32(),
            };
            var count = r.U32();
            for (int i = 0; i < count; i++) p.Selectable.Add(r.LocInfo(r.U32()));
            count = r.U32();
            for (int i = 0; i < count; i++) p.Unselectable.Add(r.LocInfo(r.U32()));
            return p;
        }

        private static ChainPrompt ParseChain(MessageReader r)
        {
            var p = new ChainPrompt { SpeCount = r.U8(), Forced = r.U8() != 0 };
            r.U32(); // hint timing (self)
            r.U32(); // hint timing (opponent)
            var count = r.U32();
            for (int i = 0; i < count; i++)
            {
                var card = r.LocInfo(r.U32());
                p.Chains.Add(new ChainOption { Card = card, Description = r.U64(), ClientMode = r.U8() });
            }
            return p;
        }

        private static CounterPrompt ParseCounter(MessageReader r)
        {
            var p = new CounterPrompt { CounterType = r.U16(), Count = r.U16() };
            var count = r.U32();
            for (int i = 0; i < count; i++)
            {
                p.Cards.Add(ShortRef(r, false));
                p.Counters.Add(r.U16());
            }
            return p;
        }

        private static SumPrompt ParseSum(MessageReader r)
        {
            var p = new SumPrompt { Exact = r.U8() == 0, Target = (int)r.U32(), Min = (int)r.U32(), Max = (int)r.U32() };
            var count = r.U32();
            for (int i = 0; i < count; i++)
            {
                p.MustCards.Add(r.LocInfo(r.U32()));
                p.MustValues.Add(r.U32());
            }
            count = r.U32();
            for (int i = 0; i < count; i++)
            {
                p.Cards.Add(r.LocInfo(r.U32()));
                p.Values.Add(r.U32());
            }
            return p;
        }

        private static SortPrompt ParseSort(MessageReader r, bool isChain)
        {
            var p = new SortPrompt { IsChain = isChain };
            var count = r.U32();
            for (int i = 0; i < count; i++)
            {
                var code = r.U32();
                var controller = r.U8();
                var location = r.U32();
                var sequence = r.U32();
                p.Cards.Add(new CardRef(code, controller, location, sequence));
            }
            return p;
        }

        private static AnnounceCardPrompt ParseAnnounceCard(MessageReader r)
        {
            var p = new AnnounceCardPrompt();
            var count = r.U8();
            for (int i = 0; i < count; i++) p.Opcodes.Add(r.U64());
            return p;
        }

        private static AnnounceNumberPrompt ParseAnnounceNumber(MessageReader r)
        {
            var p = new AnnounceNumberPrompt();
            var count = r.U8();
            for (int i = 0; i < count; i++) p.Options.Add(r.U64());
            return p;
        }
    }
}
