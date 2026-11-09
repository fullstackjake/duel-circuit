using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.AI;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// One line of a scene: who says it (a character id, "partner", "you", or "" for narration) and what. A line can be
    /// for one kind of campaign only: <see cref="Mode"/> "era" (era-limited play, where your team answers to the ban
    /// lists) or "open" (the usual campaign, where nobody does); "" is for both.
    /// </summary>
    public readonly struct StoryLine
    {
        public readonly string Speaker, Text, Mode;
        public StoryLine(string speaker, string text, string mode = "") { Speaker = speaker; Text = text; Mode = mode; }

        /// <summary>Whether this line is shown in a campaign that is (or isn't) era-limited.</summary>
        public bool ShownIn(bool eraLimited) => Mode.Length == 0 || (Mode == "era") == eraLimited;
    }

    /// <summary>A story moment: shown once, when its trigger happens.</summary>
    public sealed class StoryScene
    {
        public string Id = "";
        /// <summary>
        /// "start" (a new campaign), "firstloss" (the save's first recorded loss, on a circuit match's or a guest duel's
        /// results: <see cref="Story.LossEvents"/>), "clear:&lt;circuit&gt;", "open:&lt;tag cup&gt;", "cupclear:&lt;tag cup&gt;", "partner" (the
        /// first tag duel with any partner) or "partner:&lt;character&gt;", "level:&lt;n&gt;", "ngplus", "guest:&lt;character&gt;" (Duel
        /// pressed on a special guest's row, before the versus screen), "guestbeat:&lt;character&gt;" (the first one-on-one win
        /// against a special guest, on its results: <see cref="Story.GuestBeatEvent"/>), "finale" (the post-game's strands closed:
        /// <see cref="Story.FinaleDue"/>, raised as the campaign screen opens), and the card
        /// unlocks' "beat:&lt;character&gt;", "bond:&lt;character&gt;:3|5", "setcomplete" and "setcomplete:&lt;set code&gt;".
        /// The Back Alley's (<see cref="BackAlley.Triggers"/>; docs/dev/ALLEY-SPEC-2026-11-01.md section 12): "alley" (it opens,
        /// after the first cup clear's scenes), "alleyvisit" (its tab first shown), "open:&lt;table&gt;" (a table's panel first
        /// opens), "alleyclear:&lt;table&gt;", "headliner:&lt;id&gt;" (before your first duel against one), "headlinerbeat:&lt;id&gt;",
        /// "combobreaker:&lt;id&gt;", "encore", "fence" (the van's first visit), "antewin", "antelose", "rail", "buyback",
        /// "railgone", "turncap", "firstturn", "unlisted", "brawl", "midnight", "bracketwin:Alley Brawl",
        /// "bracketwin:Midnight Open" (plain "bracketwin" stays the Locals'), "pennywin" and "heldcard" (Orvel's Table
        /// cleared and cup-goat-clear seen, whichever comes second).
        /// The Champion's Ladder's (<see cref="Campaign.Ladder"/>; docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 6): "ladder"
        /// (the first campaign-screen Open with it open: <see cref="Campaign.Ladder.OpenDue"/>), "ladderseat:&lt;character&gt;" (Duel
        /// pressed against a rung's holder or a visitor, before the versus, until their seat is taken), "ladderbeat:&lt;character&gt;"
        /// (the first ladder win against them; Ilse's rung falling), "ladderrung:&lt;n&gt;" (rung n first climbed) and "laddertop" (the
        /// top first reached, after its rung's two).
        /// </summary>
        public string Trigger = "";
        public List<StoryLine> Lines = new List<StoryLine>();
        /// <summary>
        /// Where it plays, as story.txt's "place: &lt;key&gt;" line names it (one of <see cref="StoryPlaces.Known"/>), or "" for
        /// the default by its trigger's family (<see cref="StoryPlaces.Default"/>). Read it through <see cref="StoryPlaces.Of"/>.
        /// </summary>
        public string Place = "";
        /// <summary>
        /// Its gates, from story.txt's "after: &lt;scene id&gt;" and "before: &lt;scene id&gt;" lines (any number of each; all must hold):
        /// it plays only once every <see cref="After"/> scene has been seen, and only while no <see cref="Before"/> scene has.
        /// See <see cref="Story"/> for when they're read.
        /// </summary>
        public List<string> After = new List<string>(), Before = new List<string>();
        /// <summary>Whether it has any gate.</summary>
        public bool Gated => After.Count + Before.Count > 0;
        /// <summary>Shown again each time it triggers (partner introductions are once per partner, not once ever).</summary>
        public bool PerPartner => Trigger == "partner";
    }

    /// <summary>
    /// The campaign's story (campaign/story.txt): short scenes with the shop's regulars, your rival, your partners and
    /// the champions of each band, each shown once when its trigger happens. Text can use {partner} (your partner's
    /// name), {partnerdeck}, {partnerline} (the partner's own "partner:" line; a line using it is skipped for a partner
    /// without one: <see cref="Shows"/>), {you} ("you") and {level}; the Back Alley's scenes {nick}, {table}, {handle}, {grade} and {sold}
    /// (the card Moth sold off the rail) from the result's fill (<see cref="AlleyResult.Fill"/>).
    /// Profile.Seen remembers what you've seen.
    /// <para>
    /// <b>Gates</b> (<see cref="StoryScene.After"/>, <see cref="StoryScene.Before"/>; docs/dev/story-review/STRUCTURE-2026-11-06.md D6),
    /// the shop lines' after:/before: (campaign/shop.txt) for scenes:
    /// </para>
    /// <list type="bullet">
    /// <item>"after: &lt;id&gt;" holds once scene &lt;id&gt; has been seen, "before: &lt;id&gt;" while it hasn't; a scene may have any number
    /// of each and plays only when all of them hold. They're read when the scene's trigger fires (<see cref="Pending"/>), against
    /// <see cref="Profile.Seen"/> plus the scenes due ahead of it in the same batch (story order), since those play first: so a
    /// quit mid-batch and a replay of the rest give the same answer.</item>
    /// <item>A gate that fails skips the scene and leaves it unseen. Two variants on one trigger with opposite gates
    /// ("before: X" and "after: X") therefore play exactly one each time that trigger fires.</item>
    /// <item>A skipped scene is never played later by itself: only its own trigger firing again reads its gates again (most
    /// triggers fire once; guest:, headliner: and the like fire until their scene is seen). So the "after: X" variant also
    /// takes "before: &lt;the other variant's id&gt;" (<see cref="GateProblems"/> asks for it): then exactly one ever plays, even
    /// when the trigger fires again after X, and an old save that saw the original before its twin existed never gets the twin.</item>
    /// <item>A gate names a scene id from this file, as the shop's do (<see cref="GateProblems"/>), not any other key in
    /// Seen (lessons, shop lines, alley marks), and not the generic "partner" scene (seen once per partner, never by its bare
    /// id). A partner's own scene ("partner:&lt;id&gt;") replaces the generic one whether or not its gates hold, so a gated
    /// partner scene needs its twin.</item>
    /// <item>Gates change no save field: an old save reads them against the Seen it has, and an ungated scene plays as before.</item>
    /// </list>
    /// </summary>
    public sealed class Story
    {
        public IReadOnlyList<StoryScene> Scenes { get; }

        public Story(IReadOnlyList<StoryScene> scenes) => Scenes = scenes;

        public static Story Empty => new Story(new List<StoryScene>());

        /// <summary>
        /// Reads story.txt: "scene: &lt;id&gt;" starts a scene, "when: &lt;trigger&gt;" sets its trigger, "place: &lt;key&gt;" where it
        /// plays (optional: <see cref="StoryPlaces"/>), "after: &lt;scene id&gt;" / "before: &lt;scene id&gt;" its gates (optional,
        /// repeatable: see <see cref="Story"/>), and every other
        /// "speaker: text" line is a line of dialogue (speaker "-" for narration). "speaker [era]: text" is shown only in
        /// era-limited play, "speaker [open]: text" only outside it. "#" starts a comment.
        /// </summary>
        public static Story Parse(string text)
        {
            var scenes = new List<StoryScene>();
            StoryScene? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                if (key == "scene") { current = new StoryScene { Id = value }; scenes.Add(current); continue; }
                if (current == null) continue;
                if (key == "when") { current.Trigger = value; continue; }
                if (key == "place") { current.Place = value.ToLowerInvariant(); continue; }
                if (key == "after") { current.After.Add(value); continue; }
                if (key == "before") { current.Before.Add(value); continue; }
                var mode = "";
                var tag = System.Text.RegularExpressions.Regex.Match(key, @"^(.*?)\s*\[(era|open)\]$");
                if (tag.Success) { key = tag.Groups[1].Value; mode = tag.Groups[2].Value; }
                current.Lines.Add(new StoryLine(key == "-" ? "" : key, value, mode));
            }
            return new Story(scenes.Where(s => s.Trigger.Length > 0 && s.Lines.Count > 0).ToList());
        }

        public static Story Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;

        /// <summary>
        /// The scenes these events trigger that haven't been seen and whose gates hold, in story order. A "partner" scene is
        /// the generic first-tag-duel scene for <paramref name="partner"/> (used when they have no scene of their own). Gates
        /// (<see cref="StoryScene.After"/>, <see cref="StoryScene.Before"/>) read <see cref="Profile.Seen"/> plus the scenes
        /// already due in this batch; a scene they skip stays unseen (see <see cref="Story"/>).
        /// </summary>
        public List<StoryScene> Pending(Profile profile, IEnumerable<string> events, string partner = "")
        {
            var happened = new HashSet<string>(events, StringComparer.OrdinalIgnoreCase);
            var due = new List<StoryScene>();
            var dueIds = new HashSet<string>();
            foreach (var scene in Scenes)
            {
                if (!happened.Contains(scene.Trigger) || profile.Seen.Contains(SeenKey(scene, partner))) continue;
                // A partner's own scene replaces the generic one.
                if (scene.PerPartner && Scenes.Any(s => s.Trigger.Equals("partner:" + partner, StringComparison.OrdinalIgnoreCase))) continue;
                if (scene.Gated && !GatesHold(scene, id => profile.Seen.Contains(id) || dueIds.Contains(id))) continue;
                due.Add(scene);
                dueIds.Add(scene.Id);
            }
            return due;
        }

        /// <summary>Whether <paramref name="scene"/>'s gates hold, given which scene ids count as seen.</summary>
        public static bool GatesHold(StoryScene scene, Func<string, bool> seen) => scene.After.All(seen) && !scene.Before.Any(seen);

        /// <summary>
        /// What's wrong with the scenes' gates (empty: nothing): a gate naming no scene in the file (the shop's rule: Seen's other
        /// keys don't count), the generic "partner" scene (never seen by its bare id), or the scene itself; a scene both after
        /// and before the same one (it could never play); a gated id that two scenes share (whose seeing would it mean?); and
        /// two variants on one trigger (one "before: X", one "after: X") where the "after: X" one isn't also "before:" the other,
        /// so a trigger that fires again once X is seen would play it after the first.
        /// </summary>
        public List<string> GateProblems()
        {
            var problems = new List<string>();
            var byId = Scenes.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var scene in Scenes.Where(s => s.Gated))
            {
                foreach (var id in scene.After.Concat(scene.Before).Distinct())
                {
                    if (!byId.TryGetValue(id, out var named)) problems.Add($"{scene.Id}: gate names no scene {id}");
                    else if (named.Any(s => s.PerPartner)) problems.Add($"{scene.Id}: gate names {id}, the generic partner scene (seen once per partner, never as {id})");
                    else if (named.Count > 1) problems.Add($"{scene.Id}: gate names {id}, an id {named.Count} scenes share");
                    if (id == scene.Id) problems.Add($"{scene.Id}: gates on itself");
                }
                foreach (var id in scene.After.Intersect(scene.Before)) problems.Add($"{scene.Id}: both after: and before: {id} (it can never play)");
                foreach (var other in Scenes.Where(o => o != scene && o.Trigger == scene.Trigger && o.Before.Intersect(scene.After).Any()))
                    if (!scene.Before.Contains(other.Id))
                        problems.Add($"{scene.Id}: a variant of {other.Id} (both on {scene.Trigger}, after: and before: {other.Before.Intersect(scene.After).First()}) without before: {other.Id}");
            }
            return problems;
        }

        /// <summary>Remembers a scene as seen.</summary>
        public static void MarkSeen(Profile profile, StoryScene scene, string partner = "") => profile.Seen.Add(SeenKey(scene, partner));

        private static string SeenKey(StoryScene scene, string partner) => scene.PerPartner ? $"{scene.Id}:{partner}" : scene.Id;

        /// <summary>Every placeholder <see cref="Fill"/> fills (story.txt's scenes may use these and no others).</summary>
        public static readonly string[] Placeholders = { "partnerline", "partner", "partnerdeck", "you", "level", "card", "set", "nick", "table", "handle", "grade", BackAlley.SoldKey };

        /// <summary>
        /// Fills in {partner}, {partnerdeck} (<paramref name="partnerDeck"/>: the deck's name as shown), {you}, {level},
        /// and a card unlock's {card} and {set} (the <see cref="Grant"/> the scene's trigger is about: see
        /// <see cref="CardUnlocks.For"/>; its <see cref="Grant.CardName"/> and <see cref="Grant.SetName"/>). With no grant,
        /// {card} is <paramref name="fill"/>'s (an alley result's: <see cref="AlleyResult.Fill"/>, the stake), as are {table},
        /// {handle}, {grade} and {sold} (the card Moth sold off the rail, for railgone); {nick} is Baz's chalk name for you
        /// (<see cref="BackAlley.Nick(Profile)"/>). The fill is passed with the scenes it's for, so nothing carries over to later ones.
        /// </summary>
        public static string Fill(string text, Personality? partner, Profile profile, string? partnerDeck = null, string? card = null, string? set = null,
            IReadOnlyDictionary<string, string>? fill = null)
        {
            string Last(string key, string fallback) => fill != null && fill.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;
            // The partner's own line first, so it can use {partnerdeck} and {you} itself (a line with it is skipped when they have none: Shows).
            if (text.Contains(PartnerLineToken)) text = text.Replace(PartnerLineToken, partner?.PartnerLine ?? "");
            text = text
                .Replace("{partner}", partner?.Name ?? "your partner")
                .Replace("{partnerdeck}", partnerDeck ?? partner?.Decks.FirstOrDefault() ?? "their deck")
                .Replace("{you}", "you")
                .Replace("{level}", profile.Level.ToString())
                .Replace("{card}", !string.IsNullOrEmpty(card) ? card : Last("card", "card"))
                .Replace("{set}", string.IsNullOrEmpty(set) ? "the set" : set);
            if (text.IndexOf('{') < 0) return text;
            return text
                .Replace("{nick}", BackAlley.Nick(profile))
                .Replace("{table}", Last("table", "the table"))
                .Replace("{handle}", Last("handle", "your opponent"))
                .Replace("{grade}", Last("grade", ""))
                .Replace("{sold}", Last(BackAlley.SoldKey, "your card"));
        }

        public const string PartnerLineToken = "{partnerline}";

        /// <summary>
        /// Whether a line is shown with <paramref name="partner"/>: not when it uses {partnerline} and they have no "partner:"
        /// line (so the generic first-partner scene skips it rather than show an empty line).
        /// </summary>
        public static bool Shows(StoryLine line, Personality? partner) =>
            !line.Text.Contains(PartnerLineToken) || (partner != null && partner.PartnerLine.Length > 0);

        /// <summary>The save's first loss (scene first-loss): raised by <see cref="LossEvents"/>.</summary>
        public const string FirstLossEvent = "firstloss";

        /// <summary>
        /// The events a lost (or won, or drawn) circuit match or guest duel raises once it's recorded: "firstloss" when you lost
        /// (<paramref name="winner"/> 1) and it's the save's first recorded loss of any kind (<see cref="Profile.Losses"/> sums to
        /// 1). A save that lost before never sees it; Seen makes it once ever.
        /// </summary>
        public static List<string> LossEvents(Profile profile, int winner) =>
            winner == 1 && profile.Losses.Values.Sum() == 1 ? new List<string> { FirstLossEvent } : new List<string>();

        /// <summary>The post-game's last scene (<see cref="FinaleDue"/>), and its trigger.</summary>
        public const string FinaleEvent = "finale", FinaleScene = "finale";
        /// <summary>The strands' last scenes: the Library's (the Every Era epilogue at the corkboard) and No Limits' (the Invitational won).</summary>
        public const string LibraryStrandScene = "every-era-corkboard", NoLimitsStrandScene = "nl-invitational-win";

        /// <summary>
        /// Whether the finale is due: the Library's strand (<see cref="LibraryStrandScene"/>) and the alley's
        /// (<see cref="BackAlley.ClosingScene"/>) seen, plus No Limits' (<see cref="NoLimitsStrandScene"/>) only while its table is
        /// ready (so a build that ships it switches that strand on by itself), and the finale not seen yet.
        /// </summary>
        public static bool FinaleDue(Profile profile, AlleyData? alley)
        {
            if (profile.Seen.Contains(FinaleScene)) return false;
            if (!profile.Seen.Contains(LibraryStrandScene) || !profile.Seen.Contains(BackAlley.ClosingScene)) return false;
            bool noLimitsReady = alley != null && alley.Tables.Any(t => t.Tier == AlleyTier.NoLimits && t.Ready);
            return !noLimitsReady || profile.Seen.Contains(NoLimitsStrandScene);
        }

        /// <summary>The trigger a special guest's first duel raises before the versus screen ("guest:&lt;character&gt;").</summary>
        public static string GuestEvent(string character) => "guest:" + character;

        /// <summary>
        /// The trigger a special guest's first one-on-one win raises on its results ("guestbeat:&lt;character&gt;"), beside the card
        /// unlocks' events. It's the guest's own: "beat:&lt;character&gt;" is the signature gift's, paid once ever at the first win
        /// anywhere, so for a guest you also meet in the circuits (Vera's rematch) it was raised long before. Raised again on
        /// the first win of each New Game+ cycle (the guest's prize key starts over); Seen keeps its scene once.
        /// </summary>
        public static string GuestBeatEvent(string character) => "guestbeat:" + character;

        /// <summary>The events a guest duel's result raises for the guest (<see cref="GuestBeatEvent"/>: a first one-on-one win).</summary>
        public static List<string> GuestEvents(Guest guest, bool tag, int winner, DuelReward reward) =>
            winner == 0 && !tag && reward.FirstWin ? new List<string> { GuestBeatEvent(guest.Character.Id) } : new List<string>();

        /// <summary>The events a circuit result raises: circuits cleared, Tag Cups opened, levels reached.</summary>
        public static List<string> Events(Circuits circuits, int circuit, DuelReward reward)
        {
            var events = new List<string>();
            if (reward.ClearedCircuit != null)
            {
                events.Add("clear:" + reward.ClearedCircuit.Name);
                events.AddRange(circuits.TagCups.Where(t => t.After == circuit).Select(t => "open:" + t.Name));
            }
            events.AddRange(reward.LevelUps.Select(u => "level:" + u.Level));
            return events;
        }
    }

    /// <summary>
    /// Where a story scene plays: the backdrop the story screen draws behind it (docs/dev/TODO's story backgrounds). A scene names
    /// its place with "place: &lt;key&gt;" in story.txt; without one it gets its trigger family's (<see cref="Default"/>). The
    /// main street's places draw in its colors, the alley's (<see cref="IsAlley"/>) in the Back Alley's.
    /// </summary>
    public static class StoryPlaces
    {
        /// <summary>The shop: the counter and the register, shelves of binders behind (circuit and shop scenes).</summary>
        public const string ShopFloor = "shop-floor";
        /// <summary>The shop after closing, at night: the door and its bell, the street lamp through the glass.</summary>
        public const string ShopNight = "shop-night";
        /// <summary>Out front: the corkboard by the window, bracket sheets and the old signed card.</summary>
        public const string Corkboard = "corkboard";
        /// <summary>The corkboard after the Every Era final: the plain card moved to the top, the gold-signed one under it.</summary>
        public const string CorkboardTop = "corkboard-top";
        /// <summary>The back room's working side: the stockroom door, boxes, one bulb, and the shelf with the empty slot.</summary>
        public const string BackRoom = "back-room";
        /// <summary>The Format Library: Hollis's bookcase of year binders (the Library's scenes).</summary>
        public const string Library = "library";
        /// <summary>The Every Era final's stage: the long table, six binders down it, one lamp, a chair at the end.</summary>
        public const string EveryEra = "every-era";
        /// <summary>A tournament hall (the circuits' big rooms): rows of tables under hanging lights.</summary>
        public const string Hall = "hall";
        /// <summary>The Back Alley: brick, string lights, the fire escapes, crates (the alley's scenes).</summary>
        public const string Alley = "alley";
        /// <summary>The Basement Game: a stairwell under the laundromat, the dryers' portholes overhead.</summary>
        public const string Basement = "basement";
        /// <summary>Moth's van: the open hatch, the rail of toploaders, the one lit glass shelf.</summary>
        public const string Van = "van";
        /// <summary>No Limits' bus depot: strip lights, the route board, one warm bulb over the end chair.</summary>
        public const string Depot = "depot";

        /// <summary>Every place a scene can name, main street's first.</summary>
        public static readonly IReadOnlyList<string> Known = new[] { ShopFloor, ShopNight, Corkboard, CorkboardTop, BackRoom, Library, EveryEra, Hall, Alley, Basement, Van, Depot };

        public static bool IsKnown(string place) => Known.Contains(place);

        /// <summary>Whether a place is the Back Alley's (drawn in its theme) rather than the main street's.</summary>
        public static bool IsAlley(string place) => place is Alley or Basement or Van or Depot;

        /// <summary>The Every Era final's name, for the triggers when no Library is loaded (library.txt's `final:`).</summary>
        public const string FinalName = "Every Era";

        /// <summary>Where <paramref name="scene"/> plays: its own place, else its trigger's default (<see cref="Default"/>).</summary>
        public static string Of(StoryScene scene, AlleyData? alley = null, Library? library = null) =>
            scene.Place.Length > 0 ? scene.Place : Default(scene.Trigger, alley, library);

        /// <summary>
        /// The place a trigger's family plays in when its scene names none: No Limits' at the Depot (but the Brawl's
        /// out-of-towners in the alley), the van's first visit at the van, the alley's in the alley, the Library's (its cups,
        /// stamps, Champion's tables, Locals, the final's opening, the Champion's Ladder) in the Library, the Every Era final's rounds on its stage, and
        /// everything else (circuits, Tag Cups, partners, gifts, bonds, levels) on the shop floor. With no data, an
        /// "open:"/"cupclear:" naming an alley table or a Library cup can't be told from a Tag Cup's and plays on the shop floor
        /// (the story screen passes the loaded data).
        /// </summary>
        public static string Default(string trigger, AlleyData? alley = null, Library? library = null)
        {
            if (trigger.StartsWith(BackAlley.OutOfTownerEvent + ":") || trigger == BackAlley.OutOfTownerEvent) return Alley;
            if (BackAlley.IsNoLimitsTrigger(trigger)) return Depot;
            if (trigger == BackAlley.FenceEvent) return Van;
            if (IsAlleyTrigger(trigger, alley)) return Alley;
            var final = library != null && library.Final.Name.Length > 0 ? library.Final.Name : FinalName;
            if (trigger == "open:" + final || trigger == "cupclear:" + final || trigger.StartsWith("round:")) return EveryEra;
            if (trigger == Campaign.Ladder.OpenEvent || trigger == Campaign.Ladder.TopEvent || trigger.StartsWith(Campaign.Ladder.SeatPrefix)
                || trigger.StartsWith(Campaign.Ladder.BeatPrefix) || trigger.StartsWith(Campaign.Ladder.RungEventPrefix)) return Library;
            if (trigger is "library" or "final" or "stamp" or Brackets.EntryEvent or Brackets.WinEvent
                || trigger.StartsWith("stamp:") || trigger.StartsWith("table:")) return Library;
            if (library != null && (trigger.StartsWith("open:") || trigger.StartsWith("cupclear:") || trigger.StartsWith(Brackets.WinEvent + ":")))
            {
                var name = trigger.Substring(trigger.IndexOf(':') + 1);
                if (library.IndexOf(name) >= 0 || library.Cups.Any(c => name == c.Name + " Locals")) return Library;
            }
            return ShopFloor;
        }

        /// <summary>
        /// Whether a trigger is the alley's: <see cref="BackAlley.IsStoryTrigger"/> with its data; without it, the triggers
        /// that need none (alley, alleyvisit, the ante's, the brackets' entries) and the alley-only families (alleyclear:,
        /// headliner:, headlinerbeat:, combobreaker:, and a named bracket's win: the Locals' title is plain "bracketwin").
        /// </summary>
        private static bool IsAlleyTrigger(string trigger, AlleyData? alley)
        {
            if (alley != null && alley.Tables.Count > 0) return BackAlley.IsStoryTrigger(trigger, alley);
            return BackAlley.Triggers(AlleyData.Empty).Contains(trigger) || trigger.StartsWith("alleyclear:") || trigger.StartsWith("headliner:")
                || trigger.StartsWith("headlinerbeat:") || trigger.StartsWith(BackAlley.ComboBreakerEvent) || trigger.StartsWith(Brackets.WinEvent + ":");
        }
    }
}
