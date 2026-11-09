using Ygo.Engine;
using Ygo.Engine.Campaign;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s story places (the story backdrops): every "place:" key story.txt uses is one the story screen draws
/// (<see cref="StoryPlaces.Known"/>), every scene resolves to one with the game's data, the trigger families get their
/// defaults (circuits the shop floor, the alley's the alley, No Limits' the depot, the Library's the Library), and the
/// scenes the text puts somewhere (the Every Era epilogue's back room and corkboard) play there.
/// </summary>
public static class StoryPlacesTest
{
    public static int Run(ICardDataProvider cards, string root, Library library)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Console.WriteLine("\nStory places:");
        var path = Path.Combine(root, "campaign", "story.txt");
        var story = Story.Load(path);
        var alley = AlleyData.Load(Path.Combine(root, "campaign", AlleyData.FileName), cards);

        // Every key written is known, and every one written was read (a scene's "place:" line, not a speaker called "place").
        int written = File.ReadLines(path).Count(l => l.TrimStart().StartsWith("place:"));
        var tagged = story.Scenes.Where(sc => sc.Place.Length > 0).ToList();
        var unknown = tagged.Where(sc => !StoryPlaces.IsKnown(sc.Place)).Select(sc => $"{sc.Id}: {sc.Place}").ToList();
        var asSpeaker = story.Scenes.SelectMany(sc => sc.Lines).Count(l => l.Speaker == "place");
        Check($"story.txt names a place for {tagged.Count} scenes ({written} place lines), every key known{(unknown.Count > 0 ? " (unknown: " + string.Join(", ", unknown) + ")" : "")}",
            tagged.Count == written && written > 0 && unknown.Count == 0 && asSpeaker == 0);
        var odd = Story.Parse("scene: x\nwhen: start\nplace: The-Moon\n-: Hi.");
        Check($"a place key reads lower-case and an unknown one is refused (\"{odd.Scenes.FirstOrDefault()?.Place}\")",
            odd.Scenes.Count == 1 && odd.Scenes[0].Place == "the-moon" && !StoryPlaces.IsKnown("the-moon") && odd.Scenes[0].Lines.Count == 1);

        // Each scene's place with the data, and how many play where.
        var places = story.Scenes.Select(sc => (sc, place: StoryPlaces.Of(sc, alley, library))).ToList();
        var bad = places.Where(p => !StoryPlaces.IsKnown(p.place)).Select(p => p.sc.Id).ToList();
        var counts = StoryPlaces.Known.Select(k => $"{k} {places.Count(p => p.place == k)}").ToList();
        var unused = StoryPlaces.Known.Where(k => places.All(p => p.place != k)).ToList();
        Check($"all {story.Scenes.Count} scenes play in a known place ({string.Join(", ", counts)}){(unused.Count > 0 ? "; never used: " + string.Join(", ", unused) : "")}",
            bad.Count == 0 && unused.Count == 0);

        // The trigger families' defaults (a scene that names no place).
        string D(string trigger) => StoryPlaces.Default(trigger, alley, library);
        var cup = library.Cups[0].Name;
        var table = alley.Tables.FirstOrDefault(t => t.Tier != AlleyTier.NoLimits)?.Name ?? "";
        var expected = new (string trigger, string place)[]
        {
            ("start", StoryPlaces.ShopFloor), ("clear:Beginner Circuit", StoryPlaces.ShopFloor), ("partner:vera", StoryPlaces.ShopFloor),
            ("beat:vera", StoryPlaces.ShopFloor), ("level:10", StoryPlaces.ShopFloor), ("open:Card Shop Tag Night", StoryPlaces.ShopFloor),
            ("library", StoryPlaces.Library), ("open:" + cup, StoryPlaces.Library), ("cupclear:" + cup, StoryPlaces.Library), ("stamp", StoryPlaces.Library),
            ("locals", StoryPlaces.Library), ("bracketwin", StoryPlaces.Library), ("bracketwin:" + cup + " Locals", StoryPlaces.Library),
            ("open:" + library.Final.Name, StoryPlaces.EveryEra), ("round:4", StoryPlaces.EveryEra),
            (BackAlley.VisitEvent, StoryPlaces.Alley), ("open:" + table, StoryPlaces.Alley), ("alleyclear:" + table, StoryPlaces.Alley),
            ("bracketwin:Alley Brawl", StoryPlaces.Alley), (BackAlley.FenceEvent, StoryPlaces.Van), (BackAlley.OutOfTownerEvent + ":junia", StoryPlaces.Alley),
            (BackAlley.PilotEvent + "junia", StoryPlaces.Depot), ("open:No Limits", StoryPlaces.Depot), (BackAlley.InvitationalWinEvent, StoryPlaces.Depot),
        };
        var wrong = expected.Where(e => D(e.trigger) != e.place).Select(e => $"{e.trigger} -> {D(e.trigger)}, not {e.place}").ToList();
        Check($"the trigger families' defaults: circuits and shop the shop floor, the Library's the library, the final's rounds its stage, the alley's the alley, "
              + $"the van's first visit the van, No Limits' the depot ({expected.Length} triggers{(wrong.Count > 0 ? "; " + string.Join("; ", wrong) : "")})", wrong.Count == 0);
        // Without the data an alley table's or a Library cup's open: can't be told from a Tag Cup's; the families that need none still resolve.
        Check($"without the data the families that need none still resolve (alleyvisit {StoryPlaces.Default(BackAlley.VisitEvent)}, pilot: {StoryPlaces.Default(BackAlley.PilotEvent + "junia")}, "
              + $"round: {StoryPlaces.Default("round:2")}), and an open: of a Library cup falls back to the shop floor ({StoryPlaces.Default("open:" + cup)})",
            StoryPlaces.Default(BackAlley.VisitEvent) == StoryPlaces.Alley && StoryPlaces.Default(BackAlley.PilotEvent + "junia") == StoryPlaces.Depot
            && StoryPlaces.Default("round:2") == StoryPlaces.EveryEra && StoryPlaces.Default("open:" + cup) == StoryPlaces.ShopFloor);

        // Where the text says: the final's arrival after closing, then its two epilogue scenes in different rooms.
        string At(string id) => places.FirstOrDefault(p => p.sc.Id == id).place ?? "";
        var epilogue = places.Where(p => p.sc.Trigger == "cupclear:" + library.Final.Name).ToList();
        Check($"the Every Era epilogue's {epilogue.Count} scenes play in different places ({string.Join(", ", epilogue.Select(p => $"{p.sc.Id} {p.place}"))}); "
              + $"the final arrives at the shop after closing ({At("every-era-final")}) and its rules on the stage ({At("every-era-rules")})",
            epilogue.Count == 2 && epilogue[0].place == StoryPlaces.BackRoom && epilogue[1].place == StoryPlaces.Corkboard
            && At("every-era-final") == StoryPlaces.ShopNight && At("every-era-rules") == StoryPlaces.EveryEra);
        // Upstairs scenes on alley triggers name the shop; the alley's own scenes stay in its places.
        var upstairs = new[] { "alley-upstairs", "held-card-home", "nl-invitational-home", "unlisted" };
        var alleyScenes = places.Where(p => BackAlley.IsStoryTrigger(p.sc.Trigger, alley)).ToList();
        var mainStreet = alleyScenes.Where(p => !StoryPlaces.IsAlley(p.place)).Select(p => p.sc.Id).ToList();
        Check($"of the {alleyScenes.Count} alley scenes, the ones upstairs or in the back room play there ({string.Join(", ", mainStreet)}); the rest in the alley's places",
            upstairs.All(mainStreet.Contains) && mainStreet.All(id => places.First(p => p.sc.Id == id).sc.Place.Length > 0));
        return failures;
    }
}
