using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Audio;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Which music theme (<see cref="Sfx.Themes"/>) a campaign duel plays, and in which key. Bosses get their own:
    /// each circuit's last match its band's theme (every circuit in a band a different key), all four Tier 0 Gauntlet
    /// matches the Gauntlet's (a semitone higher each match), each Tag Cup final the Tag Cup theme, and each special
    /// guest theirs, one on one or in tag. Everything else plays the everyday duel music.
    /// </summary>
    public static class BossMusic
    {
        public const string Everyday = "duel";

        private static readonly Dictionary<string, string> Bands = new Dictionary<string, string>
        {
            ["Beginner"] = "beginner", ["Novice"] = "novice", ["Intermediate"] = "intermediate", ["Advanced"] = "advanced",
            ["Expert"] = "expert", ["Master"] = "master", ["Gauntlet"] = "gauntlet",
        };

        /// <summary>Keys for the bosses sharing a theme, in campaign order: home, a minor third up, a tone down, a fourth up.</summary>
        private static readonly int[] Keys = { 0, 3, -2, 5 };

        public static (string theme, int transpose) ForMatch(Circuits circuits, int circuit, int match)
        {
            var c = circuits.All[circuit];
            if (!Bands.TryGetValue(c.Band, out var theme)) return (Everyday, 0);
            if (theme == "gauntlet") return (theme, match);
            if (match != c.Matches.Count - 1) return (Everyday, 0);
            int nth = circuits.All.Take(circuit).Count(o => o.Band == c.Band);
            return (theme, Keys[nth % Keys.Length]);
        }

        public static (string theme, int transpose) ForTagCup(Circuits circuits, int cup, int match) =>
            match == circuits.TagCups[cup].Matches.Count - 1 ? ("tagcup", Keys[cup % Keys.Length]) : (Everyday, 0);

        public static (string theme, int transpose) ForGuest(Guest guest) =>
            Sfx.Themes.ContainsKey(guest.Character.Id) ? (guest.Character.Id, 0) : (Everyday, 0);

        /// <summary>The Back Alley's everyday duel music (its tables' `music:` default).</summary>
        public const string Alley = "alley";

        /// <summary>
        /// An alley duel's theme (docs/dev/ALLEY-SPEC-2026-11-01.md section 13): a table whose `music:` is its own boss theme
        /// plays it at its host's seat (Orvel's Table, one seat: "orvel"; Nyx's Table, the last seat: "nyx"), and the alley's
        /// everyday music elsewhere. At a regular table, seat 4 (the closer or headliner) and an encore (seat 0) play the
        /// alley music a fourth up; seats 1-3 play it as it is.
        /// </summary>
        public static (string theme, int transpose) ForAlley(AlleyData data, int table, int seat)
        {
            if (table < 0 || table >= data.Tables.Count) return (Alley, 0);
            var t = data.Tables[table];
            if (t.Music.Length > 0 && t.Music != Alley && Sfx.Themes.ContainsKey(t.Music))
                return seat == t.SeatCount || t.SeatCount == 1 ? (t.Music, 0) : (Alley, 0);
            return seat == BackAlley.Matches || seat == 0 ? (Alley, 5) : (Alley, 0);
        }

        /// <summary>Every boss duel and its theme, for checks: (where, opponent, theme, transpose).</summary>
        public static List<(string where, string who, string theme, int transpose)> All(Circuits circuits, GuestList guests)
        {
            var list = new List<(string, string, string, int)>();
            for (int c = 0; c < circuits.All.Count; c++)
                for (int m = 0; m < circuits.All[c].Matches.Count; m++)
                    if (ForMatch(circuits, c, m) is var (theme, key) && theme != Everyday)
                        list.Add(($"{circuits.All[c].Name}, match {m + 1}", circuits.All[c].Matches[m].Character.Name, theme, key));
            for (int t = 0; t < circuits.TagCups.Count; t++)
                for (int m = 0; m < circuits.TagCups[t].Matches.Count; m++)
                    if (ForTagCup(circuits, t, m) is var (theme, key) && theme != Everyday)
                    {
                        var (lead, partner) = circuits.TagCups[t].Matches[m];
                        list.Add(($"{circuits.TagCups[t].Name}, final", $"{lead.Character.Name} & {partner.Character.Name}", theme, key));
                    }
            foreach (var g in guests.All)
            {
                var (theme, key) = ForGuest(g);
                list.Add(("Special guest", g.Character.Name, theme, key));
            }
            return list;
        }
    }
}
