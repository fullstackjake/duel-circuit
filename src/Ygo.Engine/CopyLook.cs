using System.Collections.Generic;
using Ygo.Engine.Data;

namespace Ygo.Engine
{
    /// <summary>How a copy looks in a duel: what a replay records per deck slot, so it shows the same copies later.</summary>
    public readonly struct CopyLook
    {
        public readonly Rarity Rarity;
        /// <summary>1-10; 0 when the slot has no copy of yours behind it (a borrowed card, a CPU's card).</summary>
        public readonly int Grade;
        public readonly uint Seed;
        /// <summary>Its slab's grade (0: Raw).</summary>
        public readonly int Slab;

        public CopyLook(Rarity rarity, int grade, uint seed, int slab = 0)
        {
            Rarity = rarity;
            Grade = grade;
            Seed = seed;
            Slab = slab;
        }

        public bool IsCopy => Grade > 0;

        /// <summary>The grade anyone can see: the slab's, or a Raw copy's word (Raw 9 and 10 both read Mint).</summary>
        public int Shown => Slab > 0 ? Slab : System.Math.Min(Grade, 9);
    }

    /// <summary>
    /// Which copy each card in a duel is. Before the duel starts every card a deck adds is stamped with its deck and slot
    /// in the core's spare per-card "cover" field (see <see cref="Duel.AddDeck"/>); the core moves card objects rather
    /// than remaking them, so the stamp follows the card through draws, shuffles, summons and back, and queries return
    /// it (<see cref="FieldCard.Cover"/>). A live duel and its replay stamp the same way.
    /// </summary>
    public sealed class CopyLooks
    {
        /// <summary>Marks our stamps (skill cards use the field for their own numbers).</summary>
        public const uint Tag = 0x59000000;

        /// <summary>Per deck added (0 Deck0, 1 Deck1, 2 Partner0, 3 Partner1): a look per main- then extra-deck slot.</summary>
        private readonly IReadOnlyList<CopyLook[]?> _decks;

        public CopyLooks(IReadOnlyList<CopyLook[]?> decks) => _decks = decks;

        public static uint Cover(int deck, int slot) => Tag | (uint)deck << 16 | (uint)(slot + 1);

        /// <summary>The copy behind a stamp, or null (not ours, no looks for that deck, or a slot with no copy of yours).</summary>
        public CopyLook? Of(uint cover)
        {
            if ((cover & 0xFF000000) != Tag) return null;
            int deck = (int)(cover >> 16 & 0xFF), slot = (int)(cover & 0xFFFF) - 1;
            if (deck >= _decks.Count || _decks[deck] is not CopyLook[] looks || slot < 0 || slot >= looks.Length) return null;
            return looks[slot].IsCopy ? looks[slot] : (CopyLook?)null;
        }

        public CopyLook? Of(FieldCard? card) => card == null ? null : Of(card.Cover);
    }
}
