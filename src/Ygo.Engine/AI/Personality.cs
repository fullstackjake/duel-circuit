using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// How a bot character plays. Every knob is 0..1 and shifts a threshold the smart bot already
    /// uses, so a profile changes style without changing the rules engine or card knowledge.
    /// Profiles are plain-text files (see personalities/*.profile) so new characters need no code.
    /// </summary>
    public sealed class Personality
    {
        public string Id = "balanced";
        public string Name = "Duelist";
        public string Title = "";

        /// <summary>Attacks into unknown monsters, accepts even trades, summons in attack position.</summary>
        public double Aggression = 0.5;
        /// <summary>Fear of the opponent's set cards; prefers setting monsters and safe attacks.</summary>
        public double Caution = 0.5;
        /// <summary>Saves hand traps and traps for the opponent's strongest plays instead of the first one.</summary>
        public double Patience = 0.5;
        /// <summary>Willingness to spend board wipes, removal, and Extra Deck summons.</summary>
        public double Spending = 0.5;
        /// <summary>1 = always plays its best line; lower values make deliberate random choices.</summary>
        public double Skill = 1.0;

        public List<string> Decks = new List<string>();
        public string Intro = "";
        /// <summary>Their line when they turn up a second time in the same circuit (empty: the intro again).</summary>
        public string Rematch = "";
        public string WinLine = "";
        public string LoseLine = "";

        /// <summary>What they say before a match: the rematch line when they've already played earlier in the circuit.</summary>
        public string IntroFor(bool again) => again && Rematch.Length > 0 ? Rematch : Intro;
        /// <summary>
        /// Things they say during a duel, by moment: "summon" (they bring out something big), "strike" (they hit you
        /// hard), "hit" (they take a big hit), "danger" (their LP falls to 1000 or less). Each key can repeat.
        /// </summary>
        public Dictionary<string, List<string>> Lines = new Dictionary<string, List<string>>();
        /// <summary>What they look like (for the character list and, one day, portraits).</summary>
        public string Look = "";
        /// <summary>Place on the campaign ladder (1 = first opponent); 0 = not in the campaign.</summary>
        public int Ladder;
        /// <summary>
        /// Their signature card's name ("signature:" in the profile; "" for none): the first-win gift and the bond 5
        /// gift (see Campaign.CardUnlocks, which resolves it to a card).
        /// </summary>
        public string Signature = "";
        /// <summary>
        /// What they say as your tag partner the first time ("partner:" in the profile; "" for none): the story's {partnerline}
        /// (Campaign.Story.Fill; a line using it is skipped for a partner without one).
        /// </summary>
        public string PartnerLine = "";

        public string DisplayName => string.IsNullOrEmpty(Title) ? Name : $"{Name}, {Title}";

        /// <summary>
        /// Skilled characters look ahead: they plan their Main Phase with the <see cref="TurnPlanner"/>
        /// and use their deck's playbook, so they find combo lines instead of just playing cards one by one.
        /// </summary>
        public bool PlansAhead => Skill >= 0.9;

        /// <summary>A copy to adjust (the lists are shared).</summary>
        public Personality Copy() => (Personality)MemberwiseClone();
        /// <summary>Knows its deck's combo lines (uses the deck's playbook, if it has one).</summary>
        // Every character past a rookie (skill 0.5) knows how their own deck works; skill still sets how well they
        // read the duel (planning ahead) and how often they slip on timing.
        public bool KnowsDeck => Skill >= (SmartDuelist.RulesSense ? 0.5 : 0.7);

        public static Personality Balanced => new Personality();

        /// <summary>
        /// A short summary of the knobs in plain dueling words, at most two, e.g. "attacks hard, fires traps early · skill 3/10".
        /// (Spending isn't shown: it doesn't read as something you see across the table.)
        /// </summary>
        public string StyleSummary
        {
            get
            {
                var traits = new List<string>();
                if (Aggression >= 0.7) traits.Add("attacks hard");
                else if (Aggression <= 0.3) traits.Add("plays defense");
                if (Caution >= 0.7) traits.Add("careful of traps");
                else if (Caution <= 0.2) traits.Add("walks into traps");
                if (Patience >= 0.7) traits.Add("saves its traps");
                else if (Patience <= 0.25) traits.Add("fires traps early");
                if (traits.Count == 0) traits.Add("balanced");
                return $"{string.Join(", ", traits.Take(2))} · skill {(int)Math.Round(Skill * 10)}/10";
            }
        }

        /// <summary>Linear blend: <paramref name="low"/> at knob 0, <paramref name="high"/> at knob 1.</summary>
        public static double Lerp(double low, double high, double knob) => low + (high - low) * Math.Max(0, Math.Min(1, knob));

        /// <summary>
        /// Parses "key: value" lines. Lists are comma-separated; '#' starts a comment.
        /// Unknown keys are ignored so older builds can read newer files.
        /// </summary>
        public static Personality Parse(string text, string id)
        {
            var p = new Personality { Id = id, Name = id };
            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "name": p.Name = value; break;
                    case "title": p.Title = value; break;
                    case "aggression": p.Aggression = Knob(value); break;
                    case "caution": p.Caution = Knob(value); break;
                    case "patience": p.Patience = Knob(value); break;
                    case "spending": p.Spending = Knob(value); break;
                    case "skill": p.Skill = Knob(value); break;
                    case "decks":
                        p.Decks.Clear();
                        foreach (var d in value.Split(','))
                            if (d.Trim().Length > 0) p.Decks.Add(d.Trim());
                        break;
                    case "intro": p.Intro = value; break;
                    case "rematch": p.Rematch = value; break;
                    case "win": p.WinLine = value; break;
                    case "lose": p.LoseLine = value; break;
                    case "summon":
                    case "strike":
                    case "hit":
                    case "danger":
                    // A headliner's show moments (docs/dev/HEADLINERS-2026-11-01.md): its route starting, paying off, fizzling.
                    case "combo":
                    case "payoff":
                    case "fizzle":
                        if (!p.Lines.TryGetValue(key, out var lines)) p.Lines[key] = lines = new List<string>();
                        lines.Add(value);
                        break;
                    case "look": p.Look = value; break;
                    case "ladder": int.TryParse(value, out p.Ladder); break;
                    case "signature": p.Signature = value; break;
                    case "partner": p.PartnerLine = value; break;
                }
            }
            return p;
        }

        public static Personality Load(string path) =>
            Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

        public static List<Personality> LoadFolder(string folder)
        {
            var list = new List<Personality>();
            foreach (var file in Directory.GetFiles(folder, "*.profile"))
                list.Add(Load(file));
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        private static double Knob(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, Math.Min(1, v)) : 0.5;
    }
}
