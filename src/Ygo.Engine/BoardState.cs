using System.Collections.Generic;
using Ygo.Engine.Protocol;

namespace Ygo.Engine
{
    public sealed class PlayerBoard
    {
        /// <summary>7 slots: Main Monster Zones 0-4, Extra Monster Zones 5-6. Null = empty.</summary>
        public List<FieldCard?> Monsters = new List<FieldCard?>();
        /// <summary>8 slots: Spell/Trap Zones 0-4, Field Zone 5, Pendulum Zones 6-7. Null = empty.</summary>
        public List<FieldCard?> SpellTraps = new List<FieldCard?>();
        public List<FieldCard?> Hand = new List<FieldCard?>();
        public List<FieldCard?> Graveyard = new List<FieldCard?>();
        public List<FieldCard?> Banished = new List<FieldCard?>();
        public List<FieldCard?> ExtraDeck = new List<FieldCard?>();
        /// <summary>Main Deck contents (codes only). Bots may read their own; the order is not meaningful to them.</summary>
        public List<FieldCard?> Deck = new List<FieldCard?>();
        public int DeckCount;
        public int LifePoints = 8000;
        /// <summary>How this player's LP last changed (the duel screen's change chip shows its reason).</summary>
        public LpChange LastLpChange;
    }

    /// <summary>
    /// What a front end draws: zones from live core queries, plus turn/phase/LP tracked from messages.
    /// </summary>
    /// <summary>What changed a player's LP: battle damage, effect damage, a cost they paid, LP gained, or LP set outright.</summary>
    public enum LpChange { None, Battle, Effect, Cost, Gain, Set }

    public sealed class BoardState
    {
        /// <summary>Between an attack and the end of its damage step: damage now is battle damage.</summary>
        private bool _inBattle;
        public PlayerBoard[] Players = { new PlayerBoard(), new PlayerBoard() };

        /// <summary>
        /// The card at a place, if both players can see it (face-up on the field, in a Graveyard, face-up banished, or
        /// revealed in hand); 0 otherwise. For naming targets without leaking hidden cards.
        /// </summary>
        public uint PublicCodeAt(Protocol.CardRef place)
        {
            if (place.Controller > 1) return 0;
            var p = Players[place.Controller];
            var list = (place.Location & 0x7f) switch
            {
                Location.MZone => p.Monsters, Location.SZone => p.SpellTraps, Location.Grave => p.Graveyard,
                Location.Removed => p.Banished, Location.Hand => p.Hand, _ => null,
            };
            if (list == null || place.Sequence >= list.Count || list[(int)place.Sequence] is not FieldCard card) return 0;
            return PublicCode(card, place.Location);
        }

        /// <summary>A card's code if both players can see it where it is, else 0.</summary>
        public static uint PublicCode(FieldCard card, uint location)
        {
            bool visible = (location & 0x7f) switch
            {
                Location.Grave => true,
                Location.Hand => card.IsPublic,
                Location.MZone or Location.SZone or Location.Removed => card.IsFaceUp,
                _ => false,
            };
            return visible ? card.Code : 0;
        }

        /// <summary>The public card at a place, asked of the core directly (when no board is kept up to date).</summary>
        public static uint PublicCodeAt(Duel duel, Protocol.CardRef place)
        {
            if (place.Controller > 1 || (place.Location & 0x80) != 0) return 0;
            var list = duel.QueryLocation(place.Controller, place.Location & 0x7f);
            return place.Sequence < list.Count && list[(int)place.Sequence] is FieldCard card ? PublicCode(card, place.Location) : 0;
        }
        public int Turn;
        public int TurnPlayer;
        public ushort Phase;
        /// <summary>-1 while the duel is running; 0/1 winner; 2 draw.</summary>
        public int Winner = -1;

        public void Observe(byte[] message)
        {
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.NewTurn:
                    Turn++;
                    TurnPlayer = r.U8();
                    _inBattle = false;
                    break;
                case Msg.NewPhase:
                    Phase = r.U16();
                    _inBattle = false;
                    break;
                case Msg.Attack:
                    _inBattle = true;
                    break;
                case Msg.AttackDisabled:
                case Msg.DamageStepEnd:
                    _inBattle = false;
                    break;
                case Msg.Damage:
                case Msg.PayLpCost:
                {
                    var p = Players[r.U8()];
                    p.LifePoints = System.Math.Max(0, p.LifePoints - (int)r.U32());
                    p.LastLpChange = message[0] == Msg.PayLpCost ? LpChange.Cost : _inBattle ? LpChange.Battle : LpChange.Effect;
                    break;
                }
                case Msg.Recover:
                {
                    var p = Players[r.U8()];
                    p.LifePoints += (int)r.U32();
                    p.LastLpChange = LpChange.Gain;
                    break;
                }
                case Msg.LpUpdate:
                {
                    var p = Players[r.U8()];
                    p.LifePoints = (int)r.U32();
                    p.LastLpChange = LpChange.Set;
                    break;
                }
                case Msg.Win:
                    Winner = r.U8();
                    break;
            }
        }

        public void Refresh(Duel duel)
        {
            for (byte i = 0; i < 2; i++)
            {
                var p = Players[i];
                p.Monsters = duel.QueryLocation(i, Location.MZone);
                p.SpellTraps = duel.QueryLocation(i, Location.SZone);
                p.Hand = duel.QueryLocation(i, Location.Hand);
                p.Graveyard = duel.QueryLocation(i, Location.Grave);
                p.Banished = duel.QueryLocation(i, Location.Removed);
                p.ExtraDeck = duel.QueryLocation(i, Location.Extra);
                p.Deck = duel.QueryLocation(i, Location.Deck, QueryParser.Code | QueryParser.Type);
                p.DeckCount = p.Deck.Count;
            }
        }
    }
}
