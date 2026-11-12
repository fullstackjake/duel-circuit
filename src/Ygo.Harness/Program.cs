using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Data;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;
using Ygo.Harness;

// Usage:
//   dotnet run --project src/Ygo.Harness -- [--seed N] [--games N] [--p0 BOT] [--p1 BOT]
//       [--peak-check <pilot|all>]   (No Limits builds: the use rate of each restored card and pick, gain over base, speed against base; docs/dev/NO-LIMITS-SPEC-2026-11-02.md 5.1)
//       [--nolimits-test]            (only the No Limits checks of --campaign-test)
//       [--matrix] [--styles] [--decks] [--phases] [--export-cards out.bin] [deck0.ydk deck1.ydk]
//       [--export-printings data/ygoprodeck/cardinfo.json out.bin]
//       [--download-pictures data/ygoprodeck/cardinfo.json mods/cards/images [--limit N]]
// --decks plays every deck in decks/ against every other (balanced bots, seats alternate).
// --ui-test plays seat 0 through the human-play choice lists with random clicks (tests the UI path).
// --goldfish plays deck0 (going first) against a dummy that only sets monsters, comparing the generic
//   bot with the expert bot (generic + deck playbook): turn-1 end board and turns to win.
// --combo-goldfish deck: a headliner's combo route against the dummy (docs/dev/HEADLINERS-2026-11-01.md); --save-replay keeps
//   the best finished duel as its Watch the combo clip. --combo-interrupt deck: the same against one era-fair answer each.
// BOT is random, smart (balanced), or a character id from personalities/ (e.g. vera).
// One game prints a full play-by-play. More than one game runs quietly and reports totals,
// which is a quick way to stress-test the engine and bots against many board states.
// --matrix runs every bot pairing (both seats) and prints win rates.

var seed = 1;
var games = 1;
var showPhases = false;
string? exportPath = null;
var bots = new[] { "smart", "smart" };
var matrix = false;
var styles = false;
var deckRoundRobin = false;
var uiTest = false;
var responseMode = Ygo.Engine.Interactive.ResponseMode.On;
var goldfish = false;
var deckBuildingTest = false;
var replayTest = false;
var campaignTest = false;
var noLimitsTest = false;
var copiesTest = false;
string? libraryMode = null, alleyMode = null, peakCheck = null;
var splashCount = 6;
string? copiesReplays = null;
var rateDecks = false;
var campaignSim = false;
var tagTest = false;
var lessonTest = false;
var yataTest = false;
var senseTest = false;
var firstTurnTest = false;
var negatorTest = false;
var glossaryTest = false;
var tagsTest = false;
(string deck, string opponent)? deckAb = null;
bool wallStudy = false, wholePool = false, circuitStudy = false;
string? roundRobin = null, turn1 = null, comboGoldfish = null;
(string deck, string opponents)? routesAb = null, playbookAb = null;
var only = "";
var advicePath = "";
int studyDecks = 2, studyPartners = 2, studyKit = 1;
var studyAs = new WallStudy.YouAs();
int[]? studySeeds = null; int studyFirst = -1, studyMatch = 0;
(string deck, string variants)? deckVariants = null;
string? auditReplays = null;
var humanTurnTest = false;
string? showReplay = null;
var audit = false;
var difficulty = false;
var lateDecks = false;
var playbookTest = false;
var lateFrom = 14;
string? auditOut = null, auditBand = null;
var auditGame = -1;
var starterName = "classic";
var pilot = "casual";
var rounds = 40;
GoldfishStats? goldfishStats = null;
UiTestStats? uiStats = null;
var deckPaths = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed": seed = int.Parse(args[++i]); break;
        case "--games": games = int.Parse(args[++i]); break;
        case "--phases": showPhases = true; break;
        case "--export-cards": exportPath = args[++i]; break;
        case "--export-printings": return PrintingsImport.Run(args[++i], args[++i]);
        case "--export-sounds":
        case "--sound-export": return SoundExport.Run(args[++i]);
        case "--boss-music": return SoundExport.Bosses(FindProjectRoot());
        case "--characters": return CharacterSheet.Run(FindProjectRoot(), args[++i]);
        case "--ladder-test": return LadderTest.Run(FindProjectRoot()); // the Champion's Ladder (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 9)
        case "--download-pictures":
        {
            var info = args[++i];
            var folder = args[++i];
            int limit = i + 2 < args.Length && args[i + 1] == "--limit" ? int.Parse(args[i += 2]) : 0;
            return PictureDownload.Run(info, folder, limit);
        }
        case "--p0": bots[0] = args[++i]; break;
        case "--p1": bots[1] = args[++i]; break;
        case "--matrix": matrix = true; break;
        case "--styles": styles = true; break;
        case "--decks": deckRoundRobin = true; break;
        case "--ui-test": uiTest = true; break;
        case "--responses": responseMode = Enum.Parse<Ygo.Engine.Interactive.ResponseMode>(args[++i], ignoreCase: true); break;
        case "--goldfish": goldfish = true; break;
        case "--deckbuilding-test": deckBuildingTest = true; break;
        case "--extra-gaps": break;
        case "--lesson":
        {
            // `--lesson <character> <deck> <rival character> <rival deck>`: makes a Replay Lesson and prints its key turn.
            var root0 = FindProjectRoot();
            var cards0 = SqliteCardDatabase.LoadFolder(Path.Combine(root0, "vendor", "BabelCDB"));
            var scripts0 = new FileScriptProvider(Path.Combine(root0, "vendor", "CardScripts"));
            var people = Personality.LoadFolder(Path.Combine(root0, "personalities")).ToDictionary(p => p.Id);
            Deck Find(string n) => Deck.LoadYdk(Directory.GetFiles(Path.Combine(root0, "decks"), n + ".ydk", SearchOption.AllDirectories).First());
            var watch = Stopwatch.StartNew();
            var lesson = Ygo.Engine.Replays.ReplayLesson.Make(people[args[i + 1]], Find(args[i + 2]), args[i + 2], people[args[i + 3]], Find(args[i + 4]), args[i + 4], cards0, scripts0);
            Console.WriteLine($"{lesson.Replay.Title}  key {lesson.Summary}  ({watch.Elapsed.TotalSeconds:F1}s)");
            foreach (var m in Ygo.Engine.Replays.ReplayMoments.Find(lesson.Replay, cards0, scripts0).Where(m => m.Turn == lesson.Turn))
                Console.WriteLine($"  tick {m.Tick}: {m.Text}");
            return 0;
        }
        case "--profile-check" when i + 1 < args.Length:
        {
            // Loads a save (an old one migrates its card counts to copies), saves it to a temporary file and loads that
            // back, and reports the copies by grade; the save itself is only read.
            var loaded = Ygo.Engine.Campaign.Profile.Load(args[i + 1]);
            if (loaded == null) { Console.WriteLine($"no save at {args[i + 1]}"); return 1; }
            var temp = Path.Combine(Path.GetTempPath(), $"ygo-profile-check-{Guid.NewGuid():N}.txt");
            loaded.Save(temp);
            var back = Ygo.Engine.Campaign.Profile.Load(temp)!;
            File.Delete(temp);
            string Print(Ygo.Engine.Campaign.Collection c) => string.Join(";", c.All.OrderBy(x => x.Id).Select(x => $"{x.Code}/{x.Rarity}/{x.Grade}/{x.Seed}/{x.Id}"));
            var copies = loaded.Collection.All.ToList();
            Console.WriteLine($"{copies.Count} copies of {loaded.Collection.Distinct} cards, {loaded.Dp:N0} DP");
            foreach (var g in copies.GroupBy(c => c.Grade).OrderByDescending(g => g.Key))
                Console.WriteLine($"  grade {g.Key,2} ({Ygo.Engine.Campaign.Grades.Word(g.Key)}): {g.Count()}");
            foreach (var g in copies.GroupBy(c => c.Source)) Console.WriteLine($"  from {g.Key}: {g.Count()}");
            bool same = Print(back.Collection) == Print(loaded.Collection) && back.Dp == loaded.Dp;
            Console.WriteLine(same ? "saved and loaded back unchanged" : "CHANGED after saving and loading back");
            return same ? 0 : 1;
        }
        case "--sfx-wav" when i + 1 < args.Length:
        {
            // Writes the sound effects whose names start with a prefix as WAV files (quick; `--export-sounds` writes all, themes too).
            var outDir = args[i + 1];
            var prefix = i + 2 < args.Length ? args[i + 2] : "";
            Directory.CreateDirectory(outDir);
            foreach (var (name, make) in Ygo.Engine.Audio.Sfx.Effects.Where(e => e.Key.StartsWith(prefix)))
            {
                var samples = make();
                Ygo.Engine.Audio.Sfx.WriteWav(Path.Combine(outDir, name + ".wav"), samples);
                Console.WriteLine($"{name}: {samples.Length / (double)Ygo.Engine.Audio.Sfx.Rate:F2} s, peak {samples.Max(Math.Abs):F2}");
            }
            return 0;
        }
        case "--replay-test": replayTest = true; break;
        case "--circuits": StudyFiles.Circuits = Path.GetFullPath(args[++i]); break;
        case "--alley-gen": alleyMode = "gen"; break;
        case "--brawl-gen": alleyMode = "brawl:" + args[++i]; break;
        case "--alley-rate": alleyMode = "rate"; break;
        case "--endless-test": alleyMode = "endless"; break;
        case "--bracket-sim": alleyMode = "bracket"; break;
        case "--bracket-calibrate": alleyMode = "calibrate"; break;
        case "--splashes": splashCount = int.Parse(args[++i]); break;
        case "--library-check": libraryMode = "check"; break;
        case "--locals-check": libraryMode = "locals"; break;
        case "--prize-check": libraryMode = "prize"; break;
        case "--library-plan": libraryMode = "plan"; break;
        case "--library-study": libraryMode = "study"; break;
        case "--library-summary": libraryMode = "summary"; break;
        case "--copies-test":
            copiesTest = true;
            if (i + 1 < args.Length && Directory.Exists(args[i + 1])) copiesReplays = args[++i];
            break;
        case "--campaign-test": campaignTest = true; break;
        case "--nolimits-test": noLimitsTest = true; break;
        case "--peak-check": peakCheck = args[++i]; break;
        case "--rate-decks": rateDecks = true; break;
        case "--rate-anchored": i++; break; // see below (PeakRating)
        case "--skilled": break; // read by --rate-decks
        case "--campaign-sim": campaignSim = true; break;
        case "--postgame-sim": case "--arbitrage": break; // see below
        case "--alley-probe": break; // see below (docs/dev/ALLEY-ECONOMY-2026-11-01.md)
        case "--alley-study": break; // see below (docs/dev/ALLEY-SPEC-2026-11-01.md section 16, A3)
        case "--tag-test": tagTest = true; break;
        case "--lesson-test": lessonTest = true; break;
        case "--yata-test": yataTest = true; break;
        case "--sense-test": senseTest = true; break;
        case "--removal-timing-test": break; // see below (RemovalTimingTest)
        case "--forced-attack-test": break; // see below (ForcedAttackTest)
        case "--shuffle-test": break; // see below (ShuffleTest)
        case "--import-test": break; // see below (ImportTest)
        case "--ydk-test": if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) i++; break; // see below (YdkImportTest)
        case "--card-search-test": case "--write-baseline": break; // see below (CardSearchTest)
        case "--battle-misplay-test": break; // see below (BattleMisplayTest)
        case "--hunt-audit": break; // see below (MisplayHunt)
        case "--misplay-hunt-test": break; // see below (MisplayHuntTest)
        case "--battle-misplay-ab": i++; break; // see below (BattleMisplayTest.RunAb)
        case "--against": i++; break; // with --battle-misplay-ab
        case "--fix": i++; break; // with --battle-misplay-ab: battle, deckset, triggers, selfdamage, recovery, window, lethal, basecodes, hunt, huntall or all
        case "--removal-ab": i++; break; // see below (RemovalTimingTest.RunAb)
        case "--first-turn-test": firstTurnTest = true; break;
        case "--negator-test": negatorTest = true; break;
        case "--glossary-test": glossaryTest = true; break;
        case "--tags-test": tagsTest = true; break;
        case "--deck-ab": deckAb = (args[++i], args[++i]); break;
        case "--wall-study": wallStudy = true; break;
        case "--circuit-study": circuitStudy = true; break;
        case "--ocg-imports": break; // see below
        case "--texts-check": case "--pack-ev": break; // see below
        case "--texts": i++; break; // read by --texts-check
        case "--cv2-dump": i++; break; // see below (card versions phase 2: the determinism proof; temporary)
        case "--loaner-check": break; // see below
        case "--show-loaner": i += 2; break; // see below
        case "--round-robin": roundRobin = args[++i]; break;
        case "--turn1": turn1 = args[++i]; break;
        case "--routes-ab": routesAb = (args[++i], args[++i]); break;
        case "--playbook-ab": playbookAb = (args[++i], args[++i]); break;
        case "--only": only = args[++i]; break;
        case "--combo-goldfish": comboGoldfish = args[++i]; break;
        case "--combo-interrupt": case "--save-replay": case "--headliner-test": if (args[i] == "--combo-interrupt") i++; break; // see below (Headliners phase 3)
        case "--pool": wholePool = args[++i] == "all"; break;
        case "--loaners": studyDecks = int.Parse(args[++i]); break;
        case "--partners": studyPartners = int.Parse(args[++i]); break;
        case "--kit": studyKit = int.Parse(args[++i]); break;
        case "--write": if (!args.Contains("--rate-anchored")) advicePath = args[++i]; break; // a flag for --rate-anchored
        case "--option": studyAs.Option = args[++i]; break;
        case "--you-plan": studyAs.Plans = args[++i] != "off"; break;
        case "--you-chain": studyAs.ChainAuto = args[++i] == "auto"; break;
        case "--you-seat": studyAs.AppSeat = args[++i] == "app"; break;
        case "--seeds": { var r = args[++i].Split('-').Select(int.Parse).ToArray(); studySeeds = Enumerable.Range(r[0], r[^1] - r[0] + 1).ToArray(); break; }
        case "--first": studyFirst = args[++i] == "them" ? 1 : 0; break;
        case "--match": studyMatch = int.Parse(args[++i]); break;
        case "--remeasure": studyAs.Remeasure = true; break;
        case "--era-limited": studyAs.EraLimited = true; break;
        case "--deck-variants": deckVariants = (args[++i], args[++i]); break;
        case "--audit-replays": auditReplays = args[++i]; break;
        case "--human-turn-test": humanTurnTest = true; break;
        case "--show-replay": showReplay = args[++i]; break;
        // The replay analyzer (ReplayAnalyzer, docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md) and --keep-replays (ReplayKeeper): read below.
        case "--analyze-replay": case "--side": case "--jobs": case "--keep-replays": case "--keep": case "--keep-max": i++; break;
        case "--analyze-test": case "--fast": case "--as-played": case "--stand-in": case "--pics-absolute": break;
        case "--pics": if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) i++; break;
        case "--audit": audit = true; break;
        case "--difficulty": difficulty = true; break;
        case "--late-decks": lateDecks = true; break;
        case "--playbook-test": playbookTest = true; break;
        case "--from": lateFrom = int.Parse(args[++i]); break;
        case "--out": auditOut = args[++i]; break;
        case "--band": auditBand = args[++i]; break; // --audit: one band's circuit matches
        case "--audit-game": audit = true; auditGame = int.Parse(args[++i]); break;
        case "--starter": starterName = args[++i]; break;
        case "--pilot": pilot = args[++i]; break;
        case "--rounds": rounds = int.Parse(args[++i]); break;
        case "--save": case "--load": case "--persona": case "--cups": case "--snapshots": i++; break; // read by --campaign-sim / --postgame-sim / --shop-probe
        case "--seat": i++; break; // read by --alley-study
        // The shop's prices for this run (docs/dev/SHOP-PRICES-2026-11-01.md; ShopProbe.Apply): base, sel, novice, year, foil2, all.
        case "--prices": Console.WriteLine($"Prices: {ShopProbe.Apply(args[++i])}"); break;
        case "--shop-probe": case "--a0": break; // see below
        default: deckPaths.Add(args[i]); break;
    }
}

// `--import-test`: the card picture importer (PictureImport, docs/dev/CARD-IMPORT-SPEC-2026-11-12.md Part A), on stand-in files in a temp folder.
if (args.Contains("--import-test")) return ImportTest.Run();

var root = FindProjectRoot();
if (deckPaths.Count == 0)
    deckPaths.AddRange(new[] { Path.Combine(root, "decks", "classic.ydk"), Path.Combine(root, "decks", "modern.ydk") });
if (deckPaths.Count != 2)
{
    Console.Error.WriteLine("Pass exactly two .ydk deck files (or none for the defaults).");
    return 1;
}

Console.WriteLine($"ocgcore version {Duel.CoreVersion}");
var loadTimer = Stopwatch.StartNew();
var cards = SqliteCardDatabase.LoadFolder(Path.Combine(root, "vendor", "BabelCDB"));
var scripts = new FileScriptProvider(Path.Combine(root, "vendor", "CardScripts"));
Console.WriteLine($"Loaded {cards.Count} cards in {loadTimer.ElapsedMilliseconds} ms");
ReplayKeeper.Current = ReplayKeeper.FromArgs(args); // --keep-replays <dir>: the CPU-vs-CPU runs save their duels for --analyze-replay

if (exportPath != null)
{
    // A fresh clone has no unity/Assets/StreamingAssets yet: everything in it is generated.
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exportPath))!);
    using (var file = File.Create(exportPath))
        CardPack.Write(cards.All, file);
    Console.WriteLine($"Wrote card pack to {exportPath} ({new FileInfo(exportPath).Length / 1024} KB)");
    return 0;
}

if (deckBuildingTest) return DeckBuildingTest.Run(cards, scripts, root);
// `--ocg-imports`: the card shop's OCG import packs (campaign/ocg-imports.txt).
if (args.Contains("--ocg-imports")) return OcgImports.Run(cards, scripts, root);
// `--texts-check [--texts <file>]`: campaign/texts.txt, the card text timeline, and the decks' `#originals` lint (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md).
if (args.Contains("--texts-check")) return TextsCheck.Run(cards, root, Array.IndexOf(args, "--texts") is var tx and >= 0 ? Path.GetFullPath(args[tx + 1]) : null);
// `--pack-ev [--seed N] [--games N]`: the same seeded packs with the 4th-copy rule as it was and as it is (card versions phase 2, spec 7 loop 3).
if (args.Contains("--pack-ev")) return PackEv.Run(cards, root, seed, games > 1 ? games : 60);
if (Array.IndexOf(args, "--cv2-dump") is var cvd and >= 0) return Cv2Dump.Run(cards, root, Path.GetFullPath(args[cvd + 1]));
// `--loaner-check`: every loaner under every circuit list it can be borrowed for is legal.
if (args.Contains("--loaner-check")) return LoanerCheck.Run(cards, root);
if (Array.IndexOf(args, "--show-loaner") is var sl and >= 0) return LoanerCheck.Show(cards, root, args[sl + 1], args[sl + 2]);
// `--extra-gaps`: every built-in deck's Extra Deck monsters its Main Deck looks unable to make (tuning the check).
if (args.Contains("--extra-gaps"))
{
    foreach (var file in Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories).OrderBy(f => f))
        foreach (var gap in Ygo.Engine.DeckBuilding.DeckAnalysis.ExtraDeckGaps(Deck.LoadYdk(file), cards))
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(file)}: {cards.Get(gap.code)?.Name}: {gap.reason}");
    return 0;
}
if (replayTest) return ReplayTest.Run(cards, scripts, root, games, seed);
if (copiesTest) return CopiesTest.Run(cards, scripts, root, games, seed, copiesReplays);
if (libraryMode == "check") return LibraryStudy.Check(cards, root);
if (libraryMode == "locals") return BracketStudy.LocalsCheck(cards, root);
if (libraryMode == "prize") return BracketStudy.PrizeCheck(cards, root, games > 1 ? games : 1000);
if (libraryMode == "plan") return LibraryStudy.Plan(cards, root);
if (libraryMode == "study") return LibraryStudy.Run(cards, scripts, root, games, seed, only);
if (libraryMode == "summary") return LibraryStudy.Summary(root);
if (comboGoldfish != null) return ComboGoldfish.Run(cards, scripts, root, comboGoldfish, games, seed, args.Contains("--save-replay"));
// `--combo-interrupt <deck> [--games N]`: the goldfish against a stand-in holding one era-fair answer; `--headliner-test`: HeadlinerTest alone.
if (Array.IndexOf(args, "--combo-interrupt") is var ci and >= 0) return ComboInterrupt.Run(cards, scripts, root, args[ci + 1], games > 1 ? games : 200, seed);
if (args.Contains("--headliner-test")) return HeadlinerTest.Run(cards, scripts, root);
if (alleyMode == "gen") return AlleyStudy.Gen(cards, root, only, seed, splashCount);
// `--brawl-gen <base>[,...] [--seed N]`: the out-of-towners' Brawl decks as the splash builder makes them (No Limits section 8), to freeze into the recipes.
if (alleyMode != null && alleyMode.StartsWith("brawl:")) return BrawlGen.Run(cards, root, alleyMode.Substring(6), seed);
if (alleyMode == "rate") return AlleyStudy.Rate(cards, scripts, root, only, seed, splashCount, games);
if (alleyMode == "endless") return AlleyStudy.Endless(cards, scripts, root, seed, games);
// `--bracket-sim [--games N]`: Library Locals and the alley's Brawl and Midnight Open (ALLEY-SPEC section 11), N brackets a row (at least 20,000).
if (alleyMode == "bracket") return AlleyStudy.BracketSim(cards, root, seed, Math.Max(games, 20000));
if (alleyMode == "calibrate") return AlleyStudy.BracketCalibrate(cards, scripts, root, seed, games);
if (campaignTest) return CampaignTest.Run(cards, root);
// `--nolimits-test`: only the No Limits checks of the Back Alley's (docs/dev/NO-LIMITS-SPEC-2026-11-02.md 14.3, C3); `--campaign-test` runs them too.
if (noLimitsTest) return AlleyTest.RunNoLimits(cards, root);
// `--peak-check <pilot|all> [--games N]`: the No Limits builds' playbook gates (PeakCheck): use rates, gain over base, speed against it.
if (peakCheck != null) return PeakCheck.Run(cards, scripts, root, peakCheck, games > 1 ? games : 20, seed);
if (rateDecks) return DeckRating.Run(cards, scripts, root, rounds, seed, args.Contains("--skilled"));
// `--rate-anchored <folder|.ydk>[,...] [--rounds N] [--skilled] [--write]`: new decks rated against the frozen pool (No Limits P3).
if (Array.IndexOf(args, "--rate-anchored") is var ra and >= 0)
    return PeakRating.Run(cards, scripts, root, args[ra + 1], args.Contains("--rounds") ? rounds : PeakRating.DefaultRounds, seed, args.Contains("--skilled"), args.Contains("--write"));
if (lessonTest) return LessonTest.Run(cards, scripts);
if (yataTest) return YataTest.Run(cards, scripts);
if (senseTest) return SenseTest.Run(cards, scripts);
if (args.Contains("--removal-timing-test")) return RemovalTimingTest.Run(cards, scripts);
// `--forced-attack-test`: a forced attack whose only legal target the CPU doesn't want (Nightmare Pain and Yubel): it attacks, never loops.
if (args.Contains("--forced-attack-test")) return ForcedAttackTest.Run(cards, scripts);
if (args.Contains("--shuffle-test")) return ShuffleTest.Run(cards, scripts);
// `--ydk-test [folder]`: the .ydk import's reader and My recipes (docs/dev/YDK-IMPORT-2026-11-07.md); folder defaults to build/ydk-test.
if (Array.IndexOf(args, "--ydk-test") is var yt and >= 0)
    return YdkImportTest.Run(cards, root, yt + 1 < args.Length && !args[yt + 1].StartsWith("--") ? args[yt + 1] : null);
// `--card-search-test [--write-baseline]`: the deck editor's card search (docs/dev/CARD-SEARCH-SPEC-2026-11-07.md section 10); --write-baseline
//   rewrites its old-query fixture (src/Ygo.Harness/fixtures/card-search-baseline.txt) from the current search after a card database bump.
if (args.Contains("--card-search-test")) return CardSearchTest.Run(cards, root, args.Contains("--write-baseline"));
// `--misplay-hunt-test`: the misplays the 2026-11-06 hunt fixed, each on a set-up board (docs/dev/MISPLAY-HUNT-2026-11-06.md).
if (args.Contains("--misplay-hunt-test")) return MisplayHuntTest.Run(cards, scripts);
// `--hunt-audit`: the audit's blunder watchers over every character with every deck it plays (the misplay hunt's gathering run).
if (args.Contains("--hunt-audit"))
    return MisplayHunt.Run(cards, scripts, root, games > 1 ? games : 400, seed, only.Length > 0 ? only : null);
// `--battle-misplay-test`: a new monster in a zone already attacked this turn gets attacked; Yubel's "Cannot be destroyed by battle" isn't attacked into.
if (args.Contains("--battle-misplay-test")) return BattleMisplayTest.Run(cards, scripts);
if (Array.IndexOf(args, "--battle-misplay-ab") is var battleAb and >= 0)
    return BattleMisplayTest.RunAb(cards, scripts, root, args[battleAb + 1], Array.IndexOf(args, "--against") is var vs and >= 0 ? args[vs + 1] : null,
        games > 1 ? games : 200, seed, Array.IndexOf(args, "--fix") is var fx and >= 0 ? args[fx + 1] : "battle");
if (Array.IndexOf(args, "--removal-ab") is var removalAb and >= 0) return RemovalTimingTest.RunAb(cards, scripts, root, args[removalAb + 1], games > 1 ? games : 200, seed);
if (firstTurnTest) return FirstTurnTest.Run(cards, scripts);
if (glossaryTest) return GlossaryTest.Run(cards);
if (tagsTest) return TagsTest.Run(cards);
if (deckVariants is { } dv) return DeckVariants.Run(cards, scripts, root, dv.deck, dv.variants, games > 1 ? games : 1580, seed);
if (playbookAb is { } pab) return RoundRobin.PlaybookAb(cards, scripts, root, pab.deck, pab.opponents, games > 1 ? games : 8, seed);
if (routesAb is { } rab) return RoundRobin.RoutesAb(cards, scripts, root, rab.deck, rab.opponents, games > 1 ? games : 8, seed);
if (turn1 != null) return Turn1Boards.Run(cards, scripts, root, turn1, games > 1 ? games : 6, seed);
if (roundRobin != null) return RoundRobin.Run(cards, scripts, root, roundRobin, games > 1 ? games : 12, seed);
if (wallStudy) return WallStudy.Run(cards, scripts, root, games > 1 ? games : 16, seed, only, wholePool, studyDecks, studyPartners, studyKit, advicePath, studyAs);
if (circuitStudy) return CircuitStudy.Run(cards, scripts, root, games > 1 ? games : 24, seed, only, studyAs.Option, studyAs.EraLimited, advicePath,
    studyAs.ChainAuto, studyAs.AppSeat, studySeeds, studyFirst, studyMatch);
if (deckAb is { } ab) return DifficultyTest.DeckAb(cards, scripts, root, ab.deck, ab.opponent, games > 1 ? games : 40, seed);
if (auditReplays != null) return BlunderAudit.RunReplays(cards, scripts, auditReplays);
if (humanTurnTest) return HumanTurnTest.Run(cards, scripts, root);
if (negatorTest) return NegatorTest.Run(cards, scripts, root, games > 1 ? games : 6);
if (lateDecks) return DifficultyTest.LateDecks(cards, scripts, root, games > 1 ? games : 6, seed, lateFrom);
if (playbookTest) return DifficultyTest.PlaybookValue(cards, scripts, root, games > 1 ? games : 16, seed, lateFrom,
    args.Contains("--pilot") ? pilot : "skilled");
if (difficulty) return DifficultyTest.Run(cards, scripts, root, games > 1 ? games : 12, seed);
if (audit) return BlunderAudit.Run(cards, scripts, root, games > 1 ? games : 60, seed, auditOut, auditGame, auditBand);
// `--show-replay <file>`: a saved replay's play-by-play (what each side did, turn by turn).
if (args.Contains("--analyze-replay")) return ReplayAnalyzer.Run(cards, scripts, root, args);
if (args.Contains("--analyze-test")) return ReplayAnalyzerTest.Run(cards, scripts, root);
if (showReplay != null)
{
    var replay = Ygo.Engine.Replays.Replay.Load(showReplay) ?? throw new InvalidOperationException("Not a replay");
    using var duel = replay.Recipe(cards, scripts).Create();
    var feed = new Ygo.Engine.Replays.ReplayFeed(replay.Responses);
    var runner = new DuelRunner(duel, new Ygo.Engine.Replays.ReplayDuelist(feed), new Ygo.Engine.Replays.ReplayDuelist(feed));
    YataLock.Attach(runner, duel);
    var narrator = new DuelNarrator(cards, replay.Names[0], replay.Names[1]) { ShowPhases = true };
    narrator.CodeAt = place => BoardState.PublicCodeAt(duel, place);
    narrator.Line += Console.WriteLine;
    runner.MessageReceived += narrator.Handle;
    Console.WriteLine($"{replay.Title}: {replay.DeckNames[0]} vs {replay.DeckNames[1]}");
    runner.Run();
    return 0;
}
if (tagTest) return TagTest.Run(cards, scripts, root, games > 1 ? games : 20, seed);
string? ArgAfter(string flag) => Array.IndexOf(args, flag) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
if (args.Contains("--postgame-sim"))
    return PostGameSim.Run(cards, root, ArgAfter("--load"), ArgAfter("--persona") ?? "all", seed, int.TryParse(ArgAfter("--cups"), out var cupsAt) ? cupsAt : 10);
if (args.Contains("--arbitrage")) return Arbitrage.Run(cards, root, ArgAfter("--load"), seed, only);
// `--alley-study [--only table] [--games N] [--option deck] [--load save] [--seat N]`: you against each alley seat as the game builds it, with the
// turn cap (ALLEY-SPEC A3; Orvel's and Nyx's tables since B3 and C3; --seat N keeps an authored table's seat N only).
if (args.Contains("--alley-study")) return AlleyStudy.Study(cards, scripts, root, only, seed, games, studyAs.Option, ArgAfter("--load"), int.TryParse(ArgAfter("--seat"), out var seatOnly) ? seatOnly : 0);
// `--alley-probe [--seed N] [--games K]`: the Back Alley's DP numbers and loops, modeled before it's built (K splash decks an event).
if (args.Contains("--alley-probe")) return AlleyProbe.Run(cards, root, seed, games > 1 ? games : 12, only);
if (args.Contains("--shop-probe")) return ShopProbe.Run(cards, root, ArgAfter("--snapshots"), seed, args.Contains("--a0"));
if (campaignSim) return CampaignSim.Run(cards, scripts, root, starterName, pilot, seed, ArgAfter("--save"));

var decks = deckPaths.Select(Deck.LoadYdk).ToArray();
var personalities = Personality.LoadFolder(Path.Combine(root, "personalities")).ToDictionary(p => p.Id);
foreach (var deck in decks)
{
    var unknown = deck.Main.Concat(deck.Extra).Where(c => cards.Get(c) == null).Distinct().ToList();
    Console.WriteLine($"Deck '{deck.Name}': {deck.Main.Count} main / {deck.Extra.Count} extra" +
                      (unknown.Count > 0 ? $"  (unknown codes: {string.Join(", ", unknown)})" : ""));
}

if (goldfish)
{
    var testDeck = decks[0];
    var dummyDeck = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
    decks = new[] { testDeck, dummyDeck };
    Console.WriteLine($"\nGoldfish: {testDeck.Name} going first vs a dummy that sets one monster per turn, {games} games each");
    Console.WriteLine($"{"bot",-10} {"monsters",9} {"total ATK",10} {"extra",6} {"interrupts",11} {"win turn",9} {"won",5}");
    foreach (var kind in new[] { "smart", "expert" })
    {
        goldfishStats = new GoldfishStats();
        for (int g = 0; g < games; g++) RunGames(kind, "dummy", 1, false, seed + g);
        Console.WriteLine($"{kind,-10} {goldfishStats.Avg(goldfishStats.Monsters),9:F2} {goldfishStats.Avg(goldfishStats.TotalAttack),10:F0} " +
                          $"{goldfishStats.Avg(goldfishStats.ExtraMonsters),6:F2} {goldfishStats.Avg(goldfishStats.Interrupts),11:F2} " +
                          $"{goldfishStats.AvgWinTurn,9:F1} {goldfishStats.Wins,5}");
    }
    return 0;
}

if (uiTest)
{
    // Every deck in the human seat, against the balanced bot with every other deck.
    uiStats = new UiTestStats();
    var all = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk").OrderBy(f => f).Select(Deck.LoadYdk).ToList();
    var timer0 = Stopwatch.StartNew();
    int played = 0, crashes = 0, finished = 0;
    for (int g = 0; g < games; g++)
    {
        decks = new[] { all[g % all.Count], all[(g / all.Count + 1 + g) % all.Count] };
        var st = RunGames("clicker", "smart", 1, false, seed + g);
        played++;
        crashes += st.Crashed;
        finished += st.Wins[0] + st.Wins[1] + st.Wins[2];
    }
    Console.WriteLine($"\n{played} games with a scripted clicker in seat 0 ({timer0.Elapsed.TotalSeconds:F1}s): {finished} finished, {crashes} crashed");
    uiStats.Print();
    return crashes > 0 ? 2 : 0;
}

if (deckRoundRobin)
{
    var all = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk").OrderBy(f => f).Select(Deck.LoadYdk).ToList();
    var score = new double[all.Count, all.Count];
    int unfinished = 0;
    for (int a = 0; a < all.Count; a++)
        for (int b = a + 1; b < all.Count; b++)
        {
            int winsA = 0;
            for (int g = 0; g < games; g++)
            {
                // Alternate who goes first so neither deck gets the turn-1 advantage every game.
                bool aFirst = g % 2 == 0;
                decks = aFirst ? new[] { all[a], all[b] } : new[] { all[b], all[a] };
                var st = RunGames("smart", "smart", 1, false, seed + g);
                winsA += aFirst ? st.Wins[0] : st.Wins[1];
                unfinished += st.Wins[2];
            }
            score[a, b] = winsA / (double)games;
            score[b, a] = 1 - score[a, b];
        }
    Console.WriteLine($"\nRow deck's win rate against column deck ({games} games per pair, balanced bots)");
    Console.Write($"{"",-12}");
    foreach (var d in all) Console.Write($"{d.Name,12}");
    Console.WriteLine($"{"average",10}");
    for (int a = 0; a < all.Count; a++)
    {
        Console.Write($"{all[a].Name,-12}");
        double sum = 0;
        for (int b = 0; b < all.Count; b++)
        {
            if (a == b) { Console.Write($"{"-",12}"); continue; }
            sum += score[a, b];
            Console.Write($"{100 * score[a, b],11:F0}%");
        }
        Console.WriteLine($"{100 * sum / (all.Count - 1),9:F0}%");
    }
    Console.WriteLine($"draws: {unfinished}");
    return 0;
}

if (styles)
{
    // Every character plays a mirror match against the balanced bot, alternating seats,
    // so the differences below come from play style rather than deck choice.
    var styleDeck = decks[1];
    decks = new[] { styleDeck, styleDeck };
    Console.WriteLine($"\nMirror: {decks[0].Name}, {games} games per character vs balanced (seats alternate)");
    Console.WriteLine($"{"character",-28} {"own deck",-11} {"own%",5} {"win%",6} {"atk/turn",9} {"sets/g",7} {"disrupt/g",10} {"answers ATK",12} {"extra/g",8} {"turns",6}");
    foreach (var id in new[] { "smart" }.Concat(personalities.Keys))
    {
        var obs = new StyleTotals();
        int wins = 0, ownWins = 0;
        // 1) Style metrics in the shared mirror.
        decks = new[] { styleDeck, styleDeck };
        for (int g = 0; g < games; g++)
        {
            int seat = g % 2;
            var pair = seat == 0 ? (id, "smart") : ("smart", id);
            var st = RunGames(pair.Item1, pair.Item2, 1, false, seed + g, obs.ForSeat(seat));
            wins += st.Wins[seat];
        }
        // 2) Strength with the character's own main deck (balanced bot gets the same deck).
        var ownDeckName = id == "smart" ? styleDeck.Name : personalities[id].Decks.FirstOrDefault() ?? styleDeck.Name;
        var ownDeck = Deck.LoadYdk(Path.Combine(root, "decks", ownDeckName + ".ydk"));
        decks = new[] { ownDeck, ownDeck };
        for (int g = 0; g < games; g++)
        {
            int seat = g % 2;
            var pair = seat == 0 ? (id, "smart") : ("smart", id);
            ownWins += RunGames(pair.Item1, pair.Item2, 1, false, seed + 50000 + g).Wins[seat];
        }
        var name = id == "smart" ? "balanced (baseline)" : personalities[id].DisplayName;
        Console.WriteLine($"{name,-28} {ownDeckName,-11} {100.0 * ownWins / games,4:F0}% {100.0 * wins / games,5:F0}% {obs.AttacksPerTurn,9:F2} {obs.Per(obs.MonsterSets, games),7:F2} {obs.Per(obs.Disruptions, games),10:F2} {obs.AnsweredStrength,12:F0} {obs.Per(obs.ExtraSummons, games),8:F2} {obs.Turns / (double)games,6:F1}");
    }
    return 0;
}

if (matrix)
{
    Console.WriteLine($"\n{"pairing",-36} {decks[0].Name + " wins",14} {decks[1].Name + " wins",14} {"avg turns",10} {"errors",7}");
    foreach (var pairing in new[] { ("random", "random"), ("smart", "random"), ("random", "smart"), ("smart", "smart") })
    {
        var stats = RunGames(pairing.Item1, pairing.Item2, games, false);
        var label = $"{pairing.Item1} ({decks[0].Name}) vs {pairing.Item2} ({decks[1].Name})";
        Console.WriteLine($"{label,-36} {Pct(stats.Wins[0], games),14} {Pct(stats.Wins[1], games),14} {stats.AverageTurns,10:F1} {stats.Errors + stats.Crashed,7}");
    }
    return 0;
}

var verbose = games == 1;
var timer = Stopwatch.StartNew();
var summary = RunGames(bots[0], bots[1], games, verbose);
if (!verbose)
{
    Console.WriteLine($"\n{games} games ({bots[0]} vs {bots[1]}) in {timer.Elapsed.TotalSeconds:F1}s");
    Console.WriteLine($"  {decks[0].Name} wins: {summary.Wins[0]}   {decks[1].Name} wins: {summary.Wins[1]}   draws/unfinished: {summary.Wins[2]}");
    Console.WriteLine($"  average turns: {summary.AverageTurns:F1}");
    Console.WriteLine($"  script errors: {summary.Errors}   crashed games: {summary.Crashed}");
    if (TurnPlanner.Searches > 0)
        Console.WriteLine($"  combo search: {TurnPlanner.Searches} searches, {TurnPlanner.SearchRollouts} lines played, average {TurnPlanner.SearchMilliseconds / (double)TurnPlanner.Searches:F0} ms");
    if (TurnPlanner.TotalDecisions > 0)
        Console.WriteLine($"  planner: {TurnPlanner.TotalDecisions} decisions, average {TurnPlanner.TotalMilliseconds / (double)TurnPlanner.TotalDecisions:F0} ms, slowest {TurnPlanner.SlowestMilliseconds} ms"
            + (PublicResponder.Activations > 0 ? $", opponent responded {PublicResponder.Activations} times in rollouts" : ""));
}
return summary.Crashed > 0 ? 2 : 0;

Stats RunGames(string bot0, string bot1, int count, bool verbose, int? firstSeed = null, StyleObserver? observer = null)
{
    var stats = new Stats();
    int totalTurns = 0;
    for (int g = 0; g < count; g++)
    {
        int gameSeed = (firstSeed ?? seed) + g;
        var log = new DuelNarrator(cards, decks[0].Name, decks[1].Name) { ShowPhases = showPhases };
        log.Line += Console.WriteLine;
        var board = new BoardState();
        var errors = new List<string>();

        using var duel = new Duel(cards, scripts, new DuelSettings { Seed = (ulong)gameSeed });
        log.CodeAt = place => BoardState.PublicCodeAt(duel, place);
        duel.Log += (text, type) =>
        {
            // type 0 = script/engine error, 1 = Debug.Message from a script, 2 = core debug
            if (type == 0) errors.Add(text);
            if (verbose) Console.WriteLine($"  [core:{type}] {text}");
        };

        duel.AddDeck(0, decks[0]);
        duel.AddDeck(1, decks[1]);

        Action<string>? decisions = verbose ? s => Console.WriteLine($"    > {s}") : null;
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = decks[0], Deck1 = decks[1] };
        IDuelist Make(string kind, int seat)
        {
            if (kind == "random") return new RandomDuelist(decks[seat].Name, gameSeed * 2 + seat, cards, decisions);
            if (kind == "smart") return new SmartDuelist(decks[seat].Name, gameSeed * 2 + seat, context, decisions);
            if (kind == "expert" || kind == "expert-basic" || kind == "expert-combo" || kind == "expert-nopick" || kind == "expert-multi" || kind == "expert-noopp" || kind.StartsWith("expert-pick"))
            {
                var deckName = decks[seat].Name;
                var planner = new TurnPlanner(recipe, answers, Personality.Balanced, () => Playbooks.ForDeck(deckName));
                if (kind == "expert-basic") { planner.ModelInterruptions = false; planner.PlanBattle = false; planner.ComboSearch = false; planner.PlanSelections = false; planner.ModelOpponent = false; }
                if (kind == "expert-nopick") planner.PlanSelections = false;
                if (kind == "expert-multi") planner.PlanMultiSelections = true;
                if (kind == "expert-noopp") planner.ModelOpponent = false;
                if (kind == "expert-combo") planner.ComboSearch = true;
                if (kind.StartsWith("expert-pick"))
                {
                    // expert-pick-400 sets the selection margin (expert-nopick turns selection planning off).
                    planner.PlanSelections = true;
                    if (kind.Length > 12 && double.TryParse(kind.Substring(12), out var margin)) planner.SelectionMargin = margin;
                }
                return new SmartDuelist(deckName, gameSeed * 2 + seat, context, decisions,
                    playbook: Playbooks.ForDeck(deckName), planner: planner);
            }
            if (kind == "playbook")
                return new SmartDuelist(decks[seat].Name, gameSeed * 2 + seat, context, decisions, playbook: Playbooks.ForDeck(decks[seat]));
            if (kind == "dummy") return new DummyDuelist(gameSeed * 2 + seat, cards);
            if (kind == "clicker")
            {
                var human = new HumanDuelist(context, gameSeed);
                human.Presenter.AskForZones = true; // like the game: you pick where cards go
                human.Presenter.Mode = responseMode;
                return new ScriptedClicker(human, gameSeed * 2 + seat, uiStats ?? new UiTestStats());
            }
            if (!personalities.TryGetValue(kind, out var style))
                throw new ArgumentException($"Unknown bot '{kind}'. Use random, smart, expert, or one of: {string.Join(", ", personalities.Keys)}");
            if (!style.PlansAhead)
                return new SmartDuelist(style.Name, gameSeed * 2 + seat, context, decisions, style,
                    style.KnowsDeck ? Playbooks.ForDeck(decks[seat]) : null);
            var styleDeck = decks[seat].Name;
            return new SmartDuelist(style.Name, gameSeed * 2 + seat, context, decisions, style,
                Playbooks.ForDeck(styleDeck), new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(styleDeck)));
        }
        var runner = new DuelRunner(duel, Make(bot0, 0), Make(bot1, 1), answers);
        runner.MessageReceived += context.Observe;
        if (verbose) runner.MessageReceived += log.Handle;
        // YGO_EVENTS=1 also prints the animation events the game would play (checks DuelEvent parsing).
        if (verbose && Environment.GetEnvironmentVariable("YGO_EVENTS") == "1")
            runner.MessageReceived += m => { var e = DuelEvent.Parse(m); if (e != null) Console.WriteLine($"    [event] {e}"); };
        runner.MessageReceived += board.Observe;
        if (observer != null) runner.MessageReceived += m => observer.Observe(m, cards);
        if (goldfishStats != null)
        {
            var gs = goldfishStats;
            bool snapped = false;
            runner.MessageReceived += m =>
            {
                // Snapshot seat 0's board when the dummy's first turn begins (end of our turn 1).
                if (m[0] == Msg.NewTurn && m[1] == 1 && !snapped)
                {
                    snapped = true;
                    var b = new BoardState();
                    b.Refresh(duel);
                    gs.Snapshot(b.Players[0], cards);
                }
            };
        }

        try
        {
            var result = runner.Run();
            stats.Wins[result.Winner is 0 or 1 ? result.Winner : 2]++;
            totalTurns += result.Turns;
            if (goldfishStats != null && result.Winner == 0) goldfishStats.Won(result.Turns);
            if (result.Winner < 0)
                Console.WriteLine($"  game with seed {gameSeed} ({decks[0].Name} vs {decks[1].Name}) stopped without a winner after {result.Turns} turns, {result.Prompts} prompts");
            if (verbose)
            {
                Console.WriteLine($"\nTurns: {result.Turns}   Prompts answered: {result.Prompts}   Rejected answers: {result.Retries}");
                Console.WriteLine($"Summons: {log.Summons}   Effect activations: {log.Activations}");
                board.Refresh(duel);
                PrintBoard(board, cards, decks);
            }
        }
        catch (Exception ex)
        {
            stats.Crashed++;
            Console.WriteLine($"Game {g + 1} (seed {gameSeed}) failed: {ex.Message}");
        }

        stats.Errors += errors.Count;
        foreach (var e in errors.Distinct().Take(5))
            Console.WriteLine($"  script error (seed {gameSeed}): {e}");
        // Vanilla (Normal) monsters have no script by design, so only report cards that should have one.
        var missing = duel.MissingScripts.Where(name => !IsVanillaScript(name, cards)).ToList();
        if (missing.Count > 0)
            Console.WriteLine($"  missing scripts: {string.Join(", ", missing.Take(10))}");
    }
    stats.AverageTurns = count - stats.Crashed > 0 ? totalTurns / (double)(count - stats.Crashed) : 0;
    return stats;
}

static string Pct(int wins, int games) => $"{wins} ({100.0 * wins / Math.Max(1, games):F0}%)";

static void PrintBoard(BoardState board, ICardDataProvider cards, Deck[] decks)
{
    string Describe(FieldCard? c) => c == null ? "-" :
        $"{cards.Get(c.Code)?.Name ?? c.Code.ToString()}{(c.IsFaceUp ? "" : " (set)")}" +
        ((c.Type & CardType.Monster) != 0 && c.IsFaceUp ? $" {c.Attack}/{c.Defense}" : "");
    Console.WriteLine($"\nFinal board (turn {board.Turn}):");
    for (int i = 0; i < 2; i++)
    {
        var p = board.Players[i];
        Console.WriteLine($"  {decks[i].Name}: LP {p.LifePoints}, deck {p.DeckCount}, hand {p.Hand.Count}, GY {p.Graveyard.Count}, banished {p.Banished.Count}, extra {p.ExtraDeck.Count}");
        Console.WriteLine($"    monsters: {string.Join(" | ", p.Monsters.Select(Describe))}");
        Console.WriteLine($"    spells/traps: {string.Join(" | ", p.SpellTraps.Select(Describe))}");
    }
}

static bool IsVanillaScript(string name, ICardDataProvider cards)
{
    const uint TypeNormal = 0x10;
    if (!name.StartsWith('c') || !uint.TryParse(name[1..^4], out var code)) return false;
    if (code == 0) return true; // the core probes c0.lua for internal placeholder cards
    var card = cards.Get(code);
    return card != null && (card.Type & TypeNormal) != 0 && (card.Type & CardType.Pendulum) == 0;
}

static string FindProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "vendor")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the project root (folder containing vendor/)");
}

sealed class Stats
{
    public int[] Wins = new int[3];
    public int Errors;
    public int Crashed;
    public double AverageTurns;
}

/// <summary>Counts style-revealing actions for one seat across games.</summary>
sealed class StyleObserver
{
    private readonly StyleTotals _totals;
    private readonly int _seat;
    private int _turnPlayer;
    private readonly List<(uint code, int controller)> _chain = new();

    public StyleObserver(StyleTotals totals, int seat)
    {
        _totals = totals;
        _seat = seat;
    }

    public void Observe(byte[] m, ICardDataProvider cards)
    {
        var r = new Ygo.Engine.Protocol.MessageReader(m, 1);
        switch (m[0])
        {
            case Msg.NewTurn:
                _turnPlayer = r.U8();
                _totals.Turns++;
                if (_turnPlayer == _seat) _totals.OwnTurns++;
                break;
            case Msg.Attack:
                if (r.LocInfo(0).Controller == _seat) _totals.Attacks++;
                break;
            case Msg.Set:
            {
                var card = r.LocInfo(r.U32());
                if (card.Controller == _seat && card.Location == Location.MZone) _totals.MonsterSets++;
                break;
            }
            case Msg.Chaining:
            {
                var code = r.U32();
                r.LocInfo(code);
                var controller = r.U8();
                if (controller == _seat && _turnPlayer != _seat)
                {
                    _totals.Disruptions++;
                    // What did we answer? The opponent's link right before ours.
                    if (_chain.Count > 0 && _chain[^1].controller != _seat)
                    {
                        _totals.AnsweredLinks++;
                        _totals.AnsweredAttackSum += Math.Max(cards.Get(_chain[^1].code)?.Attack ?? 0, 0);
                    }
                }
                _chain.Add((code, controller));
                break;
            }
            case Msg.ChainEnd:
                _chain.Clear();
                break;
            case Msg.SpSummoning:
            {
                var card = r.LocInfo(r.U32());
                if (card.Controller == _seat && cards.Get(card.Code)?.IsExtraDeckMonster == true) _totals.ExtraSummons++;
                break;
            }
        }
    }
}

sealed class StyleTotals
{
    public int Attacks, OwnTurns, Turns, MonsterSets, Disruptions, ExtraSummons, AnsweredLinks;
    public long AnsweredAttackSum;
    /// <summary>Average ATK of the opponent cards this seat chained against (spells count as 0).</summary>
    public double AnsweredStrength => AnsweredLinks == 0 ? 0 : AnsweredAttackSum / (double)AnsweredLinks;
    public StyleObserver ForSeat(int seat) => new StyleObserver(this, seat);
    public double AttacksPerTurn => OwnTurns == 0 ? 0 : Attacks / (double)OwnTurns;
    public double Per(int count, int games) => count / (double)Math.Max(1, games);
}

/// <summary>Turn-1 end board and speed of a deck against the dummy.</summary>
sealed class GoldfishStats
{
    // Cards that can stop an opponent's play on their turn.
    private static readonly HashSet<uint> Interrupters = new()
    {
        10443957, // Cyber Dragon Infinity
        29301450, // S:P Little Knight
        44508094, // Stardust Dragon
        84013237, // Number 39: Utopia
        90590303, // Number 41: Bagooska
        65741786, // I:P Masquerena
        50588353, // Crystron Halqifibrax
    };

    public List<double> Monsters = new(), TotalAttack = new(), ExtraMonsters = new(), Interrupts = new();
    public int Wins;
    private int _winTurns;

    public double Avg(List<double> values) => values.Count == 0 ? 0 : values.Average();
    public double AvgWinTurn => Wins == 0 ? 0 : _winTurns / (double)Wins;

    public void Won(int turns)
    {
        Wins++;
        _winTurns += turns;
    }

    public void Snapshot(PlayerBoard board, ICardDataProvider cards)
    {
        var monsters = board.Monsters.Where(m => m != null).Select(m => m!).ToList();
        Monsters.Add(monsters.Count);
        TotalAttack.Add(monsters.Where(m => m.IsFaceUp).Sum(m => m.Attack));
        ExtraMonsters.Add(monsters.Count(m => cards.Get(m.Code)?.IsExtraDeckMonster == true));
        int interrupts = monsters.Count(m => Interrupters.Contains(m.Code));
        interrupts += board.SpellTraps.Take(5).Count(c => c != null && !c.IsFaceUp); // set traps / quick-plays
        Interrupts.Add(interrupts);
    }
}

