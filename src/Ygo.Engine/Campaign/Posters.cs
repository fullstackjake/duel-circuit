using System;
using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.Campaign
{
    /// <summary>One poster for your plate: a card's artwork framed as an event bill.</summary>
    public sealed class Poster
    {
        public string Id = "";
        public string Title = "";
        /// <summary>The small line under the title ("Gus's Card Shop · open late").</summary>
        public string Tagline = "";
        /// <summary>The card whose artwork the poster frames (until posters have art of their own).</summary>
        public uint Card;
        /// <summary>The poster's ink color (title band and the fallback when the card has no picture), as #RRGGBB.</summary>
        public string Ink = "#B8432F";
        /// <summary>How you earn it, as the Locker shows it while it's locked.</summary>
        public string Unlock = "";
        /// <summary>Whether you've earned it.</summary>
        public Func<Circuits, Profile, bool> Earned = (c, p) => true;
        /// <summary>
        /// A Seen key the Locker waits for before it lists the poster at all ("" for none): No Limits' posters wait for the wall
        /// (<see cref="BackAlley.WallEvent"/>), so the Locker doesn't spoil what's behind it (<see cref="Posters.Shown"/>).
        /// </summary>
        public string HiddenUntil = "";
    }

    /// <summary>
    /// Posters: what your plate shows in a duel, where a duelist has a portrait. You pick one in the Locker; each band you
    /// clear pins a new one. "emblem" keeps the gold emblem. The post-game adds more (see POST-GAME-PLAN, "Posters").
    /// </summary>
    public static class Posters
    {
        /// <summary>The choice that keeps the gold emblem instead of a poster.</summary>
        public const string Emblem = "emblem";

        public static readonly IReadOnlyList<Poster> All = new[]
        {
            new Poster { Id = "shop-bell", Title = "Shop Bell", Tagline = "Gus's Card Shop · open late", Card = 40640057, Ink = "#C8901E", Unlock = "Yours from the start" },
            new Poster { Id = "rookie-cup", Title = "Rookie Cup", Tagline = "Beginner Circuit champion", Card = 89631139, Ink = "#2F6FB8",
                Unlock = "Clear the Beginner band", Earned = (c, p) => BandCleared(c, p, "Beginner") },
            new Poster { Id = "odd-couples", Title = "Odd Couples", Tagline = "Novice Tag Cup · two decks, one team", Card = 69140098, Ink = "#7A4FB8",
                Unlock = "Clear the Novice Tag Cup", Earned = (c, p) => TagCupCleared(c, p, "Novice Tag Cup") },
            new Poster { Id = "different-sky", Title = "Different Sky", Tagline = "Synchro, Xyz and Link: the Intermediate years", Card = 44508094, Ink = "#2F9AA8",
                Unlock = "Clear the Intermediate band", Earned = (c, p) => BandCleared(c, p, "Intermediate") },
            new Poster { Id = "golden-age", Title = "Golden Age", Tagline = "Advanced band · the formats people still argue about", Card = 9596126, Ink = "#B8862F",
                Unlock = "Clear the Advanced band", Earned = (c, p) => BandCleared(c, p, "Advanced") },
            new Poster { Id = "houston-2026", Title = "Houston 2026", Tagline = "Master League · the decks that topped the YCS", Card = 86066372, Ink = "#B82F4F",
                Unlock = "Clear the Master band", Earned = (c, p) => BandCleared(c, p, "Master") },
            new Poster { Id = "tier-0", Title = "Tier 0", Tagline = "Champion of the Gauntlet", Card = 73956664, Ink = "#1F8A5A",
                Unlock = "Clear the Tier 0 Gauntlet", Earned = (c, p) => BandCleared(c, p, "Gauntlet") },
            new Poster { Id = "every-era", Title = "Every Era", Tagline = "Six formats, six decks you built", Card = 71625222, Ink = "#B8862F",
                Unlock = "Win the Every Era final", Earned = (c, p) => Library?.FinalCleared(p) == true },
            // The Back Alley (docs/dev/ALLEY-SPEC-2026-11-01.md section 13).
            new Poster { Id = "no-list", Title = "NO LIST", Tagline = "The Back Alley · one copy of anything", Card = 55144522, Ink = "#FF4FA3",
                Unlock = "Find the Back Alley", Earned = (c, p) => p.Seen.Contains("alley-kit") },
            new Poster { Id = "lost-card", Title = "LOST CARD", Tagline = "Moth's rail · five duels to claim it", Card = 4031928, Ink = "#6A6F78",
                Unlock = "Lose a card at Orvel's Table",
                Earned = (c, p) => p.Rail.Count > 0 || p.DpSpent.ContainsKey(Ledger.AlleyRail) || p.Seen.Contains("orvel-first-loss") || p.Seen.Contains("first-railgone") },
            new Poster { Id = "orvels-marker", Title = "Orvel's Marker", Tagline = "The House table · paid in full", Card = 68231287, Ink = "#C8A24A",
                Unlock = "Beat Orvel's Table", Earned = (c, p) => p.AlleyTables.TryGetValue("orvels-table", out var t) && t.Cleared },
            new Poster { Id = "held", Title = "HELD", Tagline = "Moth's rail · August 2005", Card = MothsVan.HeldCode, Ink = "#8A8A80",
                Unlock = "Settle an old claim", Earned = (c, p) => p.Seen.Contains(BackAlley.HeldHomeScene) },
            // Combo Breakers (docs/dev/HEADLINERS-2026-11-01.md): one per ready headliner (HeadlinerTest holds the two lists equal: a
            // headliner set ready: yes gets its line here), earned by stopping its route before the payoff and going on to win
            // (BackAlley.Record's comboBroken). Not ready (HEADLINERS phase 3): trishula-trish, scientist-turtle.
            Breaker("yata-mags", "Yata Mags", 82301904, "#5A3FB8"),
            Breaker("mass-driver-moss", "Mass Driver Moss", 34906152, "#4F8A3F"),
            Breaker("gateway-gwen", "Gateway Gwen", 27970830, "#C83F3F"),
            Breaker("master-plan-marlo", "Master Plan Marlo", 91258852, "#B83F3F"),
            Breaker("gumblar-gail", "Gumblar Gail", 22593417, "#8A3FB8"),
            Breaker("return-rook", "Return Rook", 27174286, "#3F7FB8"), // Return from the Different Dimension (the smaller show, 2026-11-05)
            // No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 11): the-wall, earned on the table's first clear, and invitational-bill,
            // earned with the Invitational's title. Before midnight-bill, so Posters.All[^1] stays midnight-bill (-campaigncheck's locked poster).
            new Poster { Id = "the-wall", Title = "THE WALL", Tagline = "No Limits · behind Nyx's wall", Card = 89023486, Ink = "#F2C14E",
                HiddenUntil = BackAlley.WallEvent, Unlock = "Clear No Limits", Earned = (c, p) => p.AlleyTables.TryGetValue("no-limits", out var t) && t.Cleared },
            new Poster { Id = "brawl-bill", Title = "Alley Brawl", Tagline = "Eight seats, one bell", Card = 12580477, Ink = "#FF4FA3",
                Unlock = "Win the Alley Brawl", Earned = (c, p) => p.AlleyBrackets.TryGetValue("brawl", out var r) && r.Titles > 0 },
            // Glow-Up Bulb, a piece of Remy's (seed 1): the spec's Kewl Tune Rotary has no picture in a stock install.
            new Poster { Id = "invitational-bill", Title = "No Limits Invitational", Tagline = "Sixteen seats · both sides of the wall", Card = 67441435, Ink = "#9FB4C7",
                HiddenUntil = BackAlley.WallEvent, Unlock = "Win the No Limits Invitational", Earned = (c, p) => p.AlleyBrackets.TryGetValue(Brackets.InvitationalKind, out var r) && r.Titles > 0 },
            // The Champion's Ladder's top (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 5): Stairway to a Fabled Realm.
            Ladder.MakePoster(p => p.Ladder),
            new Poster { Id = "midnight-bill", Title = "Midnight Open", Tagline = "Sixteen seats · best of three at two in the morning", Card = 53129443, Ink = "#3FE0D0",
                Unlock = "Win the Midnight Open", Earned = (c, p) => p.AlleyBrackets.TryGetValue("midnight", out var r) && r.Titles > 0 },
        };

        public static Poster? Find(string id) => All.FirstOrDefault(p => p.Id == id);

        /// <summary>
        /// The Library the Every Era poster asks (set by <see cref="Campaign.Library.Load"/>; a later load, New Game+ or a
        /// reload, replaces it): earned once <see cref="Campaign.Library.FinalCleared"/> holds, whatever the final is called
        /// and however many rounds it has. Null (no Library loaded yet) leaves the poster unearned.
        /// </summary>
        public static Library? Library { get; set; }

        /// <summary>The prefix of a Combo Breaker poster's id ("breaker:yata-mags"); the headliner's id follows.</summary>
        public const string BreakerPrefix = "breaker:";

        /// <summary>A headliner's Combo Breaker poster (null: it has none).</summary>
        public static Poster? BreakerFor(string headliner) => Find(BreakerPrefix + headliner);

        /// <summary>A Combo Breaker poster: framing the card the combo turns on, earned when <see cref="HeadlinerState.Breaker"/> is set.</summary>
        public static Poster Breaker(string headliner, string handle, uint card, string ink) => new Poster
        {
            Id = BreakerPrefix + headliner, Title = "COMBO BREAKER", Tagline = $"{handle} · stopped before the payoff", Card = card, Ink = ink,
            Unlock = $"Stop {handle}'s combo before its payoff, then win",
            Earned = (c, p) => p.Headliners.TryGetValue(headliner, out var s) && s.Breaker > 0,
        };

        /// <summary>A poster by id, a completed set's ("set:&lt;code&gt;") included.</summary>
        public static Poster? Find(string id, Profile profile) => Find(id) ?? ForSets(profile).FirstOrDefault(p => p.Id == id);

        /// <summary>
        /// A set's name by its code, for set posters' titles (set by <see cref="CardUnlocks"/> from the shop's sets; the
        /// code itself without it).
        /// </summary>
        public static Func<string, string?>? SetNames;

        /// <summary>The prefix of a completed set's poster id ("set:LOB").</summary>
        public const string SetPrefix = "set:";

        /// <summary>
        /// A poster for each shop set you've completed (<see cref="Profile.SetsDone"/>), framing the card Gus framed for
        /// it and titled with the set's name, in set code order (the save keeps no completion order).
        /// </summary>
        public static List<Poster> ForSets(Profile profile) => profile.SetsDone.OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new Poster
            {
                Id = SetPrefix + s.Key, Title = SetNames?.Invoke(s.Key) ?? s.Key, Tagline = "Every card in the set · framed at Gus's", Card = s.Value,
                Ink = "#5A6F8A", Unlock = "Own every card of the set", Earned = (c, p) => p.SetsDone.ContainsKey(s.Key),
            }).ToList();

        /// <summary>Every poster the Locker can show for this save: <see cref="All"/>, then the completed sets'.</summary>
        public static List<Poster> AllFor(Profile profile) => All.Concat(ForSets(profile)).ToList();

        public static bool IsUnlocked(Poster poster, Circuits circuits, Profile profile) => poster.Earned(circuits, profile);

        /// <summary>Whether the Locker lists a poster yet (<see cref="Poster.HiddenUntil"/> seen, or none to wait for).</summary>
        public static bool Shown(Poster poster, Profile profile) => poster.HiddenUntil.Length == 0 || profile.Seen.Contains(poster.HiddenUntil);

        public static List<Poster> Unlocked(Circuits circuits, Profile profile) => AllFor(profile).Where(p => p.Earned(circuits, profile)).ToList();

        public static string SeenKey(Poster poster) => "poster:" + poster.Id;

        /// <summary>Posters you've earned but not yet seen in the Locker (the first is yours from the start, so never new).</summary>
        public static List<Poster> New(Circuits circuits, Profile profile) =>
            Unlocked(circuits, profile).Skip(1).Where(p => !profile.Seen.Contains(SeenKey(p))).ToList();

        /// <summary>The poster on your plate: your pick if you've earned it, the first poster when you haven't picked; null for the emblem.</summary>
        public static Poster? Current(Circuits circuits, Profile profile)
        {
            if (profile.Poster == Emblem) return null;
            var picked = Find(profile.Poster, profile);
            return picked != null && picked.Earned(circuits, profile) ? picked : All[0];
        }

        private static bool BandCleared(Circuits circuits, Profile profile, string band)
        {
            var inBand = Enumerable.Range(0, circuits.All.Count).Where(i => circuits.All[i].Band == band).ToList();
            return inBand.Count > 0 && inBand.All(i => circuits.IsCleared(i, profile));
        }

        private static bool TagCupCleared(Circuits circuits, Profile profile, string name)
        {
            int cup = circuits.TagCups.Select((t, i) => (t, i)).Where(x => x.t.Name == name).Select(x => x.i).DefaultIfEmpty(-1).First();
            return cup >= 0 && circuits.IsTagCupCleared(cup, profile);
        }
    }
}
