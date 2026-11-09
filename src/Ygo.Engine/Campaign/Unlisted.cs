using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// The Unlisted binder (docs/dev/ALLEY-SPEC-2026-11-01.md section 12; never "the 13th"): Hollis's binder of the
    /// contraband decks you've beaten, after the 12 cups on the Library shelf. A page is added the first time you beat
    /// one: a regular's (one page per table and base, the first you beat, as you met it), a headliner's (one each, its
    /// encores and wildcard sittings included), an authored deck's (one each: Orvel's house deck, Nyx's). Pages never count
    /// toward cup clears, stamps or the final. A new page raises "unlisted" (its scene plays once, at the first). Saved as "unlisted" lines
    /// (<see cref="Profile.UnlistedPages"/>); New Game+ keeps them.
    /// </summary>
    public static class Unlisted
    {
        public const string Event = "unlisted";

        /// <summary>Penny's page key: her own, as an authored page's (her warriors are a Dumpster Row base, whose regular gets its page too).</summary>
        public const string PennyKey = "authored:" + BackAlley.PennyId;

        /// <summary>
        /// A beaten opponent's page key: "&lt;table&gt;:&lt;base&gt;" for a regular, "hl:&lt;id&gt;" for a headliner, "nl:&lt;pilot&gt;" for a
        /// No Limits build (one page per build, whatever seat it sat at), "authored:&lt;deck&gt;", and <see cref="PennyKey"/> for
        /// Penny (a regular's page otherwise: her splash, rebuilt as met).
        /// </summary>
        public static string KeyOf(AlleyOpponent o) => o.Kind switch
        {
            "regular" when o.Character == BackAlley.PennyId => PennyKey,
            "regular" => $"{o.Table}:{o.Base}",
            "authored" => $"authored:{o.Base}",
            _ when o.IsBuild || o.Kind == BackAlley.BuildKind => $"{Ante.NoLimitsOwner}{o.Character}",
            _ => $"hl:{o.Character}",
        };

        /// <summary>
        /// Adds the page for an opponent you just beat (as you met it: its handle, table, base and splash); false when that
        /// page exists, or it isn't a contraband deck (a regular whose splash added nothing).
        /// </summary>
        public static bool Add(Profile profile, AlleyOpponent beaten)
        {
            if (beaten.Kind == "regular" && beaten.Added.Count == 0) return false;
            var key = KeyOf(beaten);
            if (profile.UnlistedPages.Any(p => p.Key == key)) return false;
            profile.UnlistedPages.Add(new UnlistedPage
            {
                Key = key, Table = beaten.Table, Kind = beaten.IsHeadliner ? "headliner" : beaten.Kind, Base = beaten.Base,
                Handle = beaten.Handle.Length > 0 ? beaten.Handle : beaten.Character, Added = beaten.Added.ToList(), Cut = beaten.Cut.ToList(),
            });
            return true;
        }

        /// <summary>
        /// For a save from before the binder (Phase A): a page for every headliner already beaten that has none, as written
        /// (no "unlisted" event: nothing plays). Regulars can't be backfilled (their decks were never saved). Call at load;
        /// the pages added (save when any).
        /// </summary>
        public static int Backfill(Profile profile, AlleyData data)
        {
            int added = 0;
            foreach (var h in data.Headliners)
            {
                if (!(profile.Headliners.TryGetValue(h.Id, out var st) && st.Beaten > 0)) continue;
                var t = data.Tables.FirstOrDefault(x => x.Headliners.Contains(h.Id)) ?? (data.IndexOf(h.Table) is var ti && ti >= 0 ? data.Tables[ti] : null);
                if (t == null) continue;
                if (Add(profile, new AlleyOpponent { Table = t.Id, Kind = "headliner", Character = h.Id, Handle = h.Handle, Base = h.Deck, IsBuild = h.IsBuild })) added++;
            }
            return added;
        }

        /// <summary>The binder's pages, in the order you won them.</summary>
        public static IReadOnlyList<UnlistedPage> Pages(Profile profile) => profile.UnlistedPages;

        /// <summary>
        /// A page's deck as you met it (a regular rebuilt from its base, cuts and adds, as <see cref="BackAlley.DeckOf"/> builds
        /// it; a headliner's or authored deck as written), named by its handle: for "Open as a new deck" (the editor builds it
        /// from what you own).
        /// </summary>
        public static Deck DeckOf(UnlistedPage page, AlleyContext ctx)
        {
            var o = new AlleyOpponent
            {
                Table = page.Table, Kind = page.Kind, Base = page.Base, Handle = page.Handle, Added = page.Added.ToList(), Cut = page.Cut.ToList(),
            };
            var deck = BackAlley.DeckOf(o, ctx);
            deck.Name = page.Handle;
            return deck;
        }

        /// <summary>The page's contraband, to mark on its list: a regular's splash; a headliner's or authored deck's ever-Forbidden cards (base codes).</summary>
        public static List<uint> Contraband(UnlistedPage page, AlleyContext ctx)
        {
            if (page.Kind == "regular") return page.Added.Select(c => Banlist.BaseCode(c, ctx.Cards)).Distinct().ToList();
            var forbidden = new HashSet<uint>(ctx.Pool(9999));
            var deck = DeckOf(page, ctx);
            return deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, ctx.Cards)).Where(forbidden.Contains).Distinct().ToList();
        }
    }
}
