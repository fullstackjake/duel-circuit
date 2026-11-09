using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;
using Ygo.Engine.Replays;

namespace Ygo.Harness;

/// <summary>
/// `--analyze-replay &lt;file|folder&gt; [--out dir] [--side cpu|both|0|1] [--fast] [--as-played] [--stand-in] [--pics [dir]] [--jobs N]`
/// (docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md): a replay played back decision by decision, with nothing hidden, each CPU
/// decision given a second opinion by a fresh bot of that character at full strength, and the BlunderAudit watchers'
/// findings, written as a self-contained HTML page (a folder: one page each, an index and analysis.json).
/// The analyzer only reads replays and never changes how a bot plays: the shadow bot's answers are never sent to the duel.
/// </summary>
public static partial class ReplayAnalyzer
{
    public sealed class Options
    {
        public string Side = "cpu";
        public bool Fast, AsPlayed, StandIn;
        /// <summary>The pictures folder (null: no pictures).</summary>
        public string? Pics;
        /// <summary>--pics-absolute: link pictures by an absolute file:// URL (needed when they aren't under the home folder with the pages).</summary>
        public bool PicsAbsolute;
        public int Jobs = Environment.ProcessorCount;
        public int MaxPrompts = 40000;
        /// <summary>The project root, scrubbed (with the home folder) from everything a page shows.</summary>
        public string Root = "";
    }

    /// <summary>What the analyzer needs from the project: cards, scripts, the roster and the decks.</summary>
    public sealed class Setup
    {
        public ICardDataProvider Cards = null!;
        public IScriptProvider Scripts = null!;
        public string Root = "";
        public List<Personality> Roster = new List<Personality>();
        /// <summary>Deck files by name, and by contents (the sorted main and extra codes), for naming a deck's playbook.</summary>
        public Dictionary<string, string> DeckByName = new Dictionary<string, string>();
        public Dictionary<string, string> DeckByContents = new Dictionary<string, string>();
        /// <summary>Each deck file's Main Deck cards, for the nearest built-in deck to an edited one (an alley splash deck).</summary>
        public List<(string name, HashSet<uint> main)> DeckCards = new List<(string, HashSet<uint>)>();

        /// <summary>The built-in deck sharing the most Main Deck cards with <paramref name="deck"/> (at least 60% of either), or null.</summary>
        public string? Nearest(Deck deck)
        {
            var mine = deck.Main.ToHashSet();
            if (mine.Count == 0) return null;
            var best = DeckCards.Select(d => (d.name, share: d.main.Intersect(mine).Count() / (double)Math.Max(mine.Count, d.main.Count)))
                .OrderByDescending(x => x.share).ThenBy(x => x.name, StringComparer.Ordinal).FirstOrDefault();
            return best.share >= 0.6 ? best.name : null;
        }

        public static Setup Load(ICardDataProvider cards, IScriptProvider scripts, string root)
        {
            var s = new Setup { Cards = cards, Scripts = scripts, Root = root, Roster = Personality.LoadFolder(Path.Combine(root, "personalities")) };
            foreach (var file in Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories).OrderBy(f => f))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                s.DeckByName.TryAdd(name, file);
                try
                {
                    var deck = Deck.LoadYdk(file);
                    s.DeckByContents.TryAdd(Contents(deck), name);
                    if (!s.DeckCards.Any(d => d.name == name)) s.DeckCards.Add((name, deck.Main.ToHashSet()));
                }
                catch (Exception) { }
            }
            return s;
        }

        public static string Contents(Deck d) => string.Join(",", d.Main.OrderBy(c => c)) + "|" + string.Join(",", d.Extra.OrderBy(c => c));

        /// <summary>The full-strength stand-in for your seat: the QA advisor's style (the top ladder character who plans and knows decks).</summary>
        public Personality StandIn() =>
            Roster.Where(p => p.Ladder > 0 && p.PlansAhead && p.KnowsDeck).OrderByDescending(p => p.Skill).FirstOrDefault()?.Copy() ?? Personality.Balanced;
    }

    // ------------------------------------------------------------ Command

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string[] args)
    {
        string? After(string flag) => Array.IndexOf(args, flag) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        var input = After("--analyze-replay");
        if (input == null || input.StartsWith("--"))
        {
            Console.Error.WriteLine("Usage: --analyze-replay <file|folder> [--out dir] [--side cpu|both|0|1] [--fast] [--as-played] [--stand-in] [--pics [dir]] [--jobs N]");
            return 1;
        }
        input = Path.GetFullPath(input);
        var o = new Options { Fast = args.Contains("--fast"), AsPlayed = args.Contains("--as-played"), StandIn = args.Contains("--stand-in"), Root = root };
        o.Side = After("--side") ?? "cpu";
        if (o.Side is not ("cpu" or "both" or "0" or "1"))
        {
            Console.Error.WriteLine($"--side {o.Side}: use cpu, both, 0 or 1");
            return 1;
        }
        if (int.TryParse(After("--jobs"), out var jobs) && jobs > 0) o.Jobs = jobs;
        if (args.Contains("--pics"))
        {
            var given = After("--pics");
            o.Pics = given != null && !given.StartsWith("--") ? Path.GetFullPath(given) : FindPictures();
            if (o.Pics == null || !Directory.Exists(o.Pics))
            {
                Console.Error.WriteLine($"No pictures folder found{(given != null && !given.StartsWith("--") ? $" at {given}" : "")}: pass --pics <folder of <card id>.jpg files>, or leave --pics off for text only.");
                return 1;
            }
            o.PicsAbsolute = args.Contains("--pics-absolute");
        }

        // Inputs: one file, or every replay under a folder.
        List<string> files;
        string baseDir;
        if (File.Exists(input)) { files = new List<string> { input }; baseDir = Path.GetDirectoryName(input)!; }
        else if (Directory.Exists(input))
        {
            baseDir = input;
            files = Directory.GetFiles(input, "*.replay", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(input, "*.txt", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains("YGOR1:")))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
        }
        else
        {
            Console.Error.WriteLine($"Not found: {input}. Pass a .replay file, a .txt holding a replay code, or a folder of them.");
            return 1;
        }
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No replays in {input} (looked for *.replay and .txt files holding a YGOR1: code).");
            return 1;
        }

        var outDir = Path.GetFullPath(After("--out") ?? Path.Combine(root, "build", "analysis", DateTime.Now.ToString("yyyy-MM-dd-HHmm")));
        if (SafePaths.IsInside(outDir, baseDir) || SafePaths.IsInside(outDir, SafePaths.SaveRoot))
        {
            Console.Error.WriteLine($"--out {outDir} is inside the replays being read (or the game's save folder); pick a folder elsewhere (the default is build/analysis/).");
            return 1;
        }
        Directory.CreateDirectory(outDir);
        // Pictures are linked relative to the pages, never climbing out of the home folder (that would name the account);
        // anywhere else needs --pics-absolute, which links them by an absolute file:// URL.
        if (o.Pics != null && !o.PicsAbsolute && !(SafePaths.IsInside(o.Pics, SafePaths.Home) && SafePaths.IsInside(outDir, SafePaths.Home)))
        {
            Console.Error.WriteLine("--pics: the pictures and --out must both be under your home folder to be linked relatively; pass --pics-absolute to link them by their full path.");
            return 1;
        }

        var setup = Setup.Load(cards, scripts, root);
        var timer = Stopwatch.StartNew();
        var results = new Analysis?[files.Count];
        var labels = files.Select(f => Path.GetRelativePath(baseDir, f)).ToArray();
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, i =>
        {
            Replay? replay = null;
            try
            {
                replay = Replay.Load(files[i]);
                if (replay == null) { results[i] = new Analysis { File = labels[i], Error = "not a replay code" }; return; }
                results[i] = Analyze(replay, labels[i], BotSidecar.Load(BotSidecar.PathFor(files[i])), setup, o, Version(File.ReadAllText(files[i])));
            }
            catch (Exception ex)
            {
                results[i] = new Analysis { File = labels[i], Title = replay?.Title ?? "", Error = SafePaths.Scrub(ex.Message, root) };
            }
            foreach (var w in new[] { results[i]! }) for (int k = 0; k < w.Warnings.Count; k++) w.Warnings[k] = SafePaths.Scrub(w.Warnings[k], root);
        });

        // Pages, the index and analysis.json.
        // The index and the summary keep their names: a replay called "index" gets "index-2.html".
        var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "index.html", "analysis.json" };
        foreach (var a in results)
        {
            if (a == null) continue;
            var stem = Safe(Path.ChangeExtension(a.File, null)!.Replace(Path.DirectorySeparatorChar, '-'));
            var page = stem + ".html";
            for (int n = 2; !pages.Add(page); n++) page = $"{stem}-{n}.html";
            a.Page = page;
            if (a.Error == null) File.WriteAllText(Path.Combine(outDir, page), Page(a, o, outDir));
        }
        var ok = results.Where(a => a != null && a.Error == null).Select(a => a!).ToList();
        if (files.Count > 1 || Directory.Exists(input))
        {
            File.WriteAllText(Path.Combine(outDir, "index.html"), Index(results.Where(a => a != null).Select(a => a!).ToList(), o, timer.Elapsed.TotalSeconds));
        }
        File.WriteAllText(Path.Combine(outDir, "analysis.json"), SummaryJson(results.Where(a => a != null).Select(a => a!).ToList(), o));

        // The console: a line per replay, and its flags (low and up).
        foreach (var a in results)
        {
            if (a == null) continue;
            if (a.Error != null) { Console.WriteLine($"{a.File}: could not be played back: {a.Error}"); continue; }
            Console.WriteLine($"{a.File}: {a.Title} · {a.Decisions.Count} decisions, {a.Analyzed} with a second opinion ({a.Agreed} agreed), " +
                              $"flags {a.Count("high")} high / {a.Count("medium")} medium / {a.Count("low")} low · {a.Seconds:F1}s");
            foreach (var w in a.Warnings) Console.WriteLine($"   warning: {w}");
            if (files.Count == 1)
                foreach (var (d, f) in a.Decisions.SelectMany(d => d.Flags.Select(f => (d, f))).Where(x => Weight(x.f.S) >= 1))
                    Console.WriteLine($"   turn {d.Turn,2}  {f.S,-6} {f.K}: {d.Who}: {f.T}");
        }
        Console.WriteLine($"{ok.Count} of {files.Count} replays analyzed in {timer.Elapsed.TotalSeconds:F1}s ({(o.Fast ? "fast" : "with the planner")}, side {o.Side}); " +
                          $"pages in {Path.GetRelativePath(root, outDir)}");
        return ok.Count == files.Count ? 0 : 2;
    }

    /// <summary>The game's save folder (the app's mods), never written to.</summary>
    private static string AppSave() => SafePaths.SaveRoot;

    /// <summary>Card pictures the way the app finds them: the app's mods/cards/images, settings' edopro_pics, then EDOPro's usual places.</summary>
    private static string? FindPictures()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var mods = Path.Combine(AppSave(), "YgoCampaign", "mods");
        var candidates = new List<string> { Path.Combine(mods, "cards", "images") };
        var settings = Path.Combine(mods, "settings.txt");
        if (File.Exists(settings))
            foreach (var line in File.ReadAllLines(settings))
                if (line.TrimStart().StartsWith("edopro_pics:"))
                {
                    var v = line[(line.IndexOf(':') + 1)..].Trim();
                    candidates.Add(v.StartsWith("~") ? Path.Combine(home, v[1..].TrimStart('/')) : v);
                }
        candidates.AddRange(new[] { "ProjectIgnis", "Applications/ProjectIgnis", "Downloads/ProjectIgnis", "Games/ProjectIgnis" }
            .Select(d => Path.Combine(home, d, "pics")));
        candidates.Add("/Applications/ProjectIgnis/pics");
        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>The format version inside a replay code (Replay.Read checks it but doesn't keep it); 0 if unreadable.</summary>
    public static int Version(string text)
    {
        var s = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var start = s.IndexOf("YGOR1:", StringComparison.Ordinal);
        if (start < 0) return 0;
        s = s[(start + 6)..];
        if (s.LastIndexOf('@') is var at and >= 0) s = s[..at];
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try
        {
            using var raw = new MemoryStream(Convert.FromBase64String(s));
            using var deflate = new DeflateStream(raw, CompressionMode.Decompress);
            var head = new byte[8];
            int read = 0;
            while (read < 8 && deflate.Read(head, read, 8 - read) is var n and > 0) read += n;
            return read == 8 ? BitConverter.ToInt32(head, 4) : 0;
        }
        catch (Exception) { return 0; }
    }

    private static string Safe(string s) =>
        new string(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('-');

    // ------------------------------------------------------------ Results

    /// <summary>One replay's analysis: the data the page is drawn from (serialized into it as JSON).</summary>
    public sealed class Analysis
    {
        public string File = "", Page = "", Title = "", Date = "", Result = "", Mats = "";
        public string? Error;
        public int Version, Turns, Winner = -1, CardCount, CardsNow;
        /// <summary>What playback reached: its last turn and its winner (-1 if the core didn't end it).</summary>
        public int PlayedTurns, PlayedWinner = -1, Prompts;
        public bool Tag;
        public List<Seat> Seats = new List<Seat>();
        public List<string> Warnings = new List<string>();
        public List<object> Items = new List<object>();
        public List<Decision> Decisions = new List<Decision>();
        public List<Flag> Unattached = new List<Flag>();
        public Dictionary<string, CardData> Cards = new Dictionary<string, CardData>();
        public int Analyzed, Agreed;
        /// <summary>--as-played: the first decision after the shadow was rebuilt (its RNG restarts), and the agreement before it; -1 if never.</summary>
        public int DriftFrom = -1, AnalyzedBeforeDrift, AgreedBeforeDrift;
        /// <summary>The first decision after which playback can't be trusted (a divergence); -1 if none.</summary>
        public int DivergedFrom = -1;
        public double Seconds;
        public bool Diverged;
        public int Count(string severity) => Decisions.Sum(d => d.Flags.Count(f => f.S == severity));
    }

    public sealed class Seat
    {
        public int Slot, Team;
        public string Name = "", Character = "", Deck = "", Playbook = "", Opinion = "", Note = "";
        public bool Human, Analyzed;
        public double Skill;
        public List<uint> Main = new List<uint>(), Extra = new List<uint>();
    }

    public sealed class CardData
    {
        public string N = "", T = "", X = "";
        public int A, D, L;
        public uint Al;
    }

    public sealed class Flag
    {
        public string S = "", K = "", T = "", Src = "";
        /// <summary>For a flag no decision of its player's carries: the turn it was raised on.</summary>
        public int Turn;
    }

    /// <summary>A heuristic score: the bot's words for the option, its score, and what ruled it out.</summary>
    public sealed class NoteRow
    {
        public string L = "";
        public double S;
        public string? R;
    }

    /// <summary>A planner score row: final (F), plain rollout (Pl), interrupted rollout (In), risk (R), regret (G).</summary>
    public sealed class ScoreRow
    {
        public string L = "";
        public double F, Pl, R;
        /// <summary>Regret against the best scored row; null for a row the planner couldn't play out.</summary>
        public double? G;
        public double? In;
        /// <summary>The usual play, the planner's choice, what was played, and the bot's pick.</summary>
        public bool U, C, P, B;
    }

    public sealed class Option
    {
        public string L = "";
        public uint C;
        /// <summary>Hidden from the one deciding (the label says "a face-down card"; C names it).</summary>
        public bool H;
        public bool P, B;
    }

    public sealed class Decision
    {
        public string K = "d";
        public int Id, Turn, Team, Slot, Retried;
        public string Phase = "", Who = "", Kind = "", Title = "", Detail = "", Played = "", Mode = "";
        public string? Bot, Why;
        public bool? Agree;
        public bool Fresh, Minor, Mine;
        public List<Option> Options = new List<Option>();
        public List<Flag> Flags = new List<Flag>();
        public Snapshot Board = null!;
        /// <summary>The bot's heuristic scores for the options it weighed (SmartDuelist.Notes).</summary>
        public List<NoteRow>? Notes;
        /// <summary>The planner's rollout scores (its own decision, or the analyzer's second look), best first.</summary>
        public List<ScoreRow>? Scores;
        /// <summary>The best score minus the played option's, in the planner's board points; null without scores.</summary>
        public double? Regret;
        public string? ScoreWhy;
        internal List<OptionNote>? RawNotes;
        internal PlannerDecision? RawPlanned;
        internal TurnPlanner.Exposure Exposure;
        internal Func<ResponseLog, TurnPlanner>? Review;
        internal int LogPrefix;
        internal ulong Hint;
        internal byte[] PlayedBytes = Array.Empty<byte>();
        /// <summary>The first answer given (the one the shadow's own first answer is compared with); PlayedBytes is the accepted one.</summary>
        internal byte[] FirstBytes = Array.Empty<byte>();
        internal bool Judgement, FollowUp, Repeat;
        /// <summary>After a playback divergence (unverified), or after the shadow's first rebuild under --as-played.</summary>
        public bool Suspect, AfterDrift;
        internal byte[]? BotBytes;
        internal ChoiceSet? Set;
        internal Prompt Prompt = null!;
    }

    /// <summary>The whole board at a prompt. A card is [code, flags (1 face-down, 2 defense, 4 negated, 8 revealed), ATK, DEF, materials].</summary>
    public sealed class Snapshot
    {
        public int Tp;
        public string Ph = "";
        public int[] Lp = new int[2];
        public SnapPlayer[] P = new SnapPlayer[2];
        public List<SnapLink> Ch = new List<SnapLink>();
    }

    public sealed class SnapPlayer
    {
        public List<int[]?> H = new(), M = new(), S = new(), G = new(), B = new(), X = new();
        public int D, XN;
    }

    public sealed class SnapLink
    {
        public uint C;
        public int P;
        public List<uint> T = new List<uint>();
    }

    public static int Weight(string severity) => severity switch { "high" => 3, "medium" => 2, "low" => 1, _ => 0 };

    /// <summary>The watchers' finding kinds by severity (spec section 2); self damage is medium only from 1000.</summary>
    public static string Severity(string kind, string text = "") => kind switch
    {
        "missed lethal" or "lost to own card" or "decked out" => "high",
        "downgraded a monster" or "risky summon, then lost" => "low",
        "risky summon, then won" or "decked out by the opponent" => "info",
        "self damage" => System.Text.RegularExpressions.Regex.Match(text, @"took (\d+)") is { Success: true } m && int.Parse(m.Groups[1].Value) < 1000 ? "low" : "medium",
        _ => "medium",
    };

    /// <summary>Rollouts the planner couldn't answer score -1e7; anything at or below this is no score at all.</summary>
    private const double Unanswerable = -5e6;
    /// <summary>A score gap this big means one line wins (or avoids a loss) where the other doesn't (wins score 1e6).</summary>
    private const double Decisive = 500000;

    /// <summary>A real score: finite and nowhere near the sentinels (double.MinValue/MaxValue stand in for "no score").</summary>
    private static bool Real(double v) => double.IsFinite(v) && Math.Abs(v) < 1e15;

    /// <summary>
    /// The planner's "(+N over the usual play)" when the usual play went unscored (it fell past the candidate cap): N is then
    /// measured from double.MinValue, a 309-digit number. Said in words instead.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex UnscoredMargin =
        new System.Text.RegularExpressions.Regex(@"\(\+\d{16,} over the usual play\)");

    // ------------------------------------------------------------ Analysis

    /// <summary>Plays <paramref name="replay"/> back and analyzes it.</summary>
    public static Analysis Analyze(Replay replay, string label, BotSidecar? sidecar, Setup setup, Options o, int version = 0)
    {
        var timer = Stopwatch.StartNew();
        var cards = setup.Cards;
        var a = new Analysis
        {
            File = label, Version = version, Turns = replay.Turns, Winner = replay.Winner, Tag = replay.IsTag, CardCount = replay.CardCount,
            CardsNow = cards.All.Count(), Date = replay.Date.ToString("yyyy-MM-dd HH:mm"),
            Mats = string.Join(" / ", replay.Mats.Where(m => !string.IsNullOrEmpty(m))),
        };
        if (replay.HumanSeat is 0 or 1) replay.Names[replay.HumanSeat] = "You"; // never a typed name
        a.Title = replay.Title;
        a.Result = replay.Winner == 2 ? "a draw" : replay.Winner >= 0 ? $"{TeamName(replay, replay.Winner)} won" : "unfinished";
        if (replay.CardCount > 0 && replay.CardCount != a.CardsNow)
            a.Warnings.Add($"recorded with {replay.CardCount} cards in the database, now {a.CardsNow}: changed cards can make playback diverge");

        var recipe = replay.Recipe(cards, setup.Scripts);
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var feed = new ReplayFeed(replay.Responses);
        var log = new ResponseLog();
        var run = new Playback(a, replay, duel, context, feed, log, setup, o, recipe, sidecar);

        // The duelists: 0/1 the teams' leads, 2/3 their partners.
        int slots = replay.IsTag ? 4 : 2;
        var seats = new AnalyzedSeat[slots];
        for (int slot = 0; slot < slots; slot++) seats[slot] = run.MakeSeat(slot);
        DuelRunner runner;
        TagSeat[]? tagSeats = null;
        if (replay.IsTag)
        {
            var (ta, tb) = TagDuel.Seats(seats[0], seats[2], seats[1], seats[3], replay.Settings.FirstPlayer);
            tagSeats = new[] { ta, tb };
            runner = new DuelRunner(duel, ta, tb, log);
        }
        else runner = new DuelRunner(duel, seats[0], seats[1], log);
        run.Runner = runner;
        runner.MessageReceived += context.Observe;
        if (tagSeats != null) { runner.MessageReceived += tagSeats[0].Observe; runner.MessageReceived += tagSeats[1].Observe; }
        YataLock.Attach(runner, duel);
        // The alley's turn cap: a capped duel's recording stops at turn 40 and the cap decides it (turn 41 never starts).
        if (sidecar?.TurnCap == true || (replay.Turns == TurnCap.AlleyTurns && replay.Winner >= 0)) TurnCap.Attach(runner, duel);
        run.Attach(tagSeats);

        string? error = null;
        try
        {
            while (runner.Step() && !(runner.IsWaiting && run.FeedEnded) && runner.Result.Prompts < o.MaxPrompts) { }
        }
        catch (Exception ex) { error = ex.Message; }
        run.FinishAll();
        if (error == null) run.ScoreAll(); else run.DropSets();
        a.PlayedTurns = run.Turn;
        a.PlayedWinner = runner.IsFinished ? runner.Result.Winner : -1;
        a.Prompts = runner.Result.Prompts;

        // Did the playback reproduce the recording?
        if (error != null) { a.Diverged = true; a.Warnings.Add($"playback stopped on turn {run.Turn}: {error}"); }
        else if (runner.IsFinished && replay.Winner >= 0 && runner.Result.Winner != replay.Winner)
        { a.Diverged = true; a.Warnings.Add($"playback diverged: the recording says {a.Result}, playback ended with {(runner.Result.Winner == 2 ? "a draw" : TeamName(replay, runner.Result.Winner) + " winning")} on turn {run.Turn}"); }
        else if (!runner.IsFinished && replay.Winner >= 0)
        {
            if (run.Turn == replay.Turns) a.Warnings.Add($"the recording ends on turn {run.Turn} before the core's end (a concession or a house rule decided it)");
            else { a.Diverged = true; a.Warnings.Add($"playback diverged: the recording ran out on turn {run.Turn} of {replay.Turns}"); }
        }
        if (!feed.Finished && runner.IsFinished) a.Warnings.Add($"{replay.Responses.Count - feed.Next} recorded answers were left over when the duel ended");
        // Rows past a divergence are unverified: badged on the page, and left out of the index's ranking. An error or a recording that
        // ran out points at the last turn; a different result could have come from anywhere, so then every row is unverified.
        if (a.Diverged)
        {
            bool resultDiffers = error == null && runner.IsFinished;
            a.DivergedFrom = resultDiffers ? 0 : a.Decisions.FirstOrDefault(d => d.Turn >= run.Turn)?.Id ?? a.Decisions.Count;
            foreach (var d in a.Decisions) if (d.Id >= a.DivergedFrom) d.Suspect = true;
        }

        a.Analyzed = a.Decisions.Count(d => d.Agree != null);
        a.Agreed = a.Decisions.Count(d => d.Agree == true);
        if (a.DriftFrom >= 0)
        {
            foreach (var d in a.Decisions) if (d.Id >= a.DriftFrom) d.AfterDrift = true;
            a.AnalyzedBeforeDrift = a.Decisions.Count(d => d.Agree != null && d.Id < a.DriftFrom);
            a.AgreedBeforeDrift = a.Decisions.Count(d => d.Agree == true && d.Id < a.DriftFrom);
            a.Warnings.Add($"as played: the shadow was rebuilt at decision #{a.DriftFrom} (its random choices restart), so rows after it are marked; " +
                           $"before it the bots agreed on {a.AgreedBeforeDrift} of {a.AnalyzedBeforeDrift}");
        }
        else { a.AnalyzedBeforeDrift = a.Analyzed; a.AgreedBeforeDrift = a.Agreed; }
        // Card data for every card the page names.
        var codes = new HashSet<uint>();
        foreach (var s in a.Seats) { codes.UnionWith(s.Main); codes.UnionWith(s.Extra); }
        foreach (var d in a.Decisions)
        {
            foreach (var op in d.Options) if (op.C != 0) codes.Add(op.C);
            foreach (var p in d.Board.P) foreach (var list in new[] { p.H, p.M, p.S, p.G, p.B, p.X }) foreach (var c in list) if (c != null && c[0] != 0) codes.Add((uint)c[0]);
            foreach (var l in d.Board.Ch) { codes.Add(l.C); codes.UnionWith(l.T); }
        }
        foreach (var code in codes)
            if (code != 0 && cards.Get(code) is CardInfo info)
                a.Cards[code.ToString()] = new CardData
                {
                    N = info.Name, T = TypeLine(info), X = info.Text, A = info.Attack, D = info.Defense,
                    L = (int)(info.Level & 0xff), Al = info.Alias,
                };
        a.Seconds = timer.Elapsed.TotalSeconds;
        return a;
    }

    private static string TeamName(Replay r, int team) =>
        r.IsTag ? $"{r.Names[team & 1]} & {r.PartnerNames![team & 1]}" : r.Names[team & 1];

    private static string TypeLine(CardInfo info)
    {
        if ((info.Type & CardType.Monster) == 0) return (info.Type & CardType.Trap) != 0 ? "Trap" : "Spell";
        var kinds = new List<string>();
        if ((info.Type & 0x40) != 0) kinds.Add("Fusion");
        if ((info.Type & 0x2000) != 0) kinds.Add("Synchro");
        if ((info.Type & 0x800000) != 0) kinds.Add("Xyz");
        if ((info.Type & 0x4000000) != 0) kinds.Add("Link");
        if ((info.Type & 0x80) != 0) kinds.Add("Ritual");
        if ((info.Type & 0x1000000) != 0) kinds.Add("Pendulum");
        kinds.Add("Monster");
        return string.Join(" ", kinds);
    }

    /// <summary>A duelist's seat in the playback: hands out the recorded answers, and asks its shadow bot first.</summary>
    private sealed class AnalyzedSeat : IDuelist
    {
        public readonly Playback Owner;
        public readonly Seat Info;
        public Func<ShadowBot>? MakeShadow;
        public ShadowBot? Shadow;
        /// <summary>A planner for the second look at a decision (ScoreOptions), from the duel as it stood then; null in --fast.</summary>
        public Func<ResponseLog, TurnPlanner>? MakeReview;
        public bool RebuildPending;
        public int FreshTurn = -1;
        public Decision? Last;
        public string BotName = "";
        /// <summary>The last pick the bot disagreed with, by turn (a repeat of it is flagged as info).</summary>
        public (int turn, string pick) LastWish = (-1, "");

        public AnalyzedSeat(Playback owner, Seat info) { Owner = owner; Info = info; }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var recorded = Owner.Feed.Take();
            if (recorded == null) { Owner.FeedEnded = true; return null; }
            Owner.Decide(this, prompt, attempt, recorded);
            return recorded;
        }

        public void OnPhaseChanged() => Shadow?.Asker.OnPhaseChanged();
    }

    /// <summary>A shadow bot and what it reported while answering the current prompt.</summary>
    private sealed class ShadowBot
    {
        public SmartDuelist Bot = null!;
        /// <summary>What answers the prompts: the bot, or the bot inside the run's wrapper (asked-only, app seat).</summary>
        public IDuelist Asker = null!;
        public readonly List<string> Lines = new List<string>();
        public readonly List<OptionNote> Notes = new List<OptionNote>();
        public readonly List<PlannerDecision> Planned = new List<PlannerDecision>();
    }

    /// <summary>The state of one playback.</summary>
    private sealed class Playback
    {
        public readonly Analysis A;
        public readonly Replay Replay;
        public readonly Duel Duel;
        public readonly DuelContext Context;
        public readonly ReplayFeed Feed;
        public readonly ResponseLog Log;
        public readonly Setup Setup;
        public readonly Options O;
        public readonly DuelRecipe Recipe;
        public readonly BotSidecar? Sidecar;
        public DuelRunner Runner = null!;
        public bool FeedEnded;
        public int Turn, TurnPlayer;
        private readonly PromptPresenter _presenter;
        private BlunderAudit.Watcher _watcher = null!;
        private int _seenFindings;
        private Decision? _lastDecision;
        private readonly List<AnalyzedSeat> _seats = new List<AnalyzedSeat>();
        private DuelNarrator _narrator = null!;
        private string[] _teamNames = null!;

        public Playback(Analysis a, Replay replay, Duel duel, DuelContext context, ReplayFeed feed, ResponseLog log, Setup setup, Options o, DuelRecipe recipe,
            BotSidecar? sidecar)
        {
            A = a; Replay = replay; Duel = duel; Context = context; Feed = feed; Log = log; Setup = setup; O = o; Recipe = recipe; Sidecar = sidecar;
            _presenter = new PromptPresenter(context) { AskForZones = true };
        }

        // ---- Who sits where, and their shadow bots

        public AnalyzedSeat MakeSeat(int slot)
        {
            var r = Replay;
            int team = slot & 1;
            bool partner = slot >= 2;
            var info = new Seat
            {
                Slot = slot, Team = team,
                Name = partner ? r.PartnerNames![team] : r.Names[team],
                Character = partner ? r.PartnerCharacters![team] : r.Characters[team],
                Deck = partner ? r.PartnerDeckNames![team] : r.DeckNames[team],
            };
            var deck = partner ? r.Partners![team] : r.Decks[team];
            info.Main = deck.Main.ToList();
            info.Extra = deck.Extra.ToList();
            info.Human = !partner && r.HumanSeat == team;
            var seat = new AnalyzedSeat(this, info);
            _seats.Add(seat);
            A.Seats.Add(info);

            info.Analyzed = O.Side switch { "cpu" => !info.Human, "both" => true, _ => O.Side == team.ToString() };
            if (!info.Analyzed) { info.Opinion = info.Human ? "off (your seat; --side both judges it)" : "off (not the side asked for)"; return seat; }

            var spec = Sidecar?.Seats.FirstOrDefault(s => s.Slot == slot);
            if (spec != null && !spec.Bot) { info.Analyzed = false; info.Opinion = "off (not a bot seat)"; return seat; }
            if (spec?.Note.Length > 0) info.Note = spec.Note;
            Personality? style = null;
            string why;
            if (spec != null) { style = spec.ToPersonality(); why = "from the run's sidecar"; }
            else if (info.Human) { style = Setup.StandIn(); why = $"the stand-in: {style.Name} playing your deck"; }
            else if (Setup.Roster.FirstOrDefault(p => p.Id == info.Character) is Personality known)
            {
                style = known.Copy();
                why = $"{known.Name}'s profile";
            }
            else if (info.Character == BackAlley.RegularId
                     && Setup.Roster.FirstOrDefault(p => p.Decks.FirstOrDefault() == PlaybookName(info.Deck, deck)) is Personality owner)
            {
                style = owner.Copy();
                style.Id = BackAlley.RegularId;
                style.Name = info.Name;
                why = $"an alley regular, with {owner.Name}'s knobs (the deck's owner)";
            }
            else if (O.StandIn) { style = Personality.Balanced; why = "unknown character: the balanced personality (--stand-in)"; }
            else
            {
                info.Analyzed = false;
                info.Opinion = $"off (character '{info.Character}' isn't in personalities/; --stand-in uses the balanced personality)";
                return seat;
            }
            info.Skill = style.Skill;
            if (!O.AsPlayed) style.Skill = 1.0;
            // The sidecar's playbook as recorded ("" when the run's bot had none); otherwise found from the deck.
            var playbook = spec != null ? spec.Playbook : PlaybookName(info.Deck, deck);
            var wrapper = spec?.Wrapper ?? "";
            int wrapperSeed = spec?.WrapperSeed ?? 0;
            if (wrapper.Length > 0) info.Note = (info.Note.Length > 0 ? info.Note + "; " : "") + $"the run wrapped this seat ({wrapper}); the shadow is wrapped the same way";
            info.Playbook = Playbooks.ForDeck(playbook) != null ? playbook : "";
            int seed = spec?.Seed ?? unchecked((int)(r.Settings.Seed * 4 + (ulong)slot));
            bool planner = !O.Fast && (O.AsPlayed && spec != null ? spec.Planner : style.PlansAhead);
            var styleFinal = style;
            seat.BotName = style.Name;
            seat.MakeShadow = () =>
            {
                var shadow = new ShadowBot();
                var book = playbook.Length > 0 && (styleFinal.PlansAhead || styleFinal.KnowsDeck) ? Playbooks.ForDeck(playbook) : null;
                var plan = planner ? new TurnPlanner(Recipe, Log, styleFinal, () => playbook.Length > 0 ? Playbooks.ForDeck(playbook) : null) : null;
                // The read-only callbacks (chunk 2): every option the bot scores, and every planned decision's candidates.
                if (plan != null) plan.Scored = shadow.Planned.Add;
                shadow.Bot = new SmartDuelist(styleFinal.Name, seed, Context, shadow.Lines.Add, styleFinal, book, plan) { Notes = shadow.Notes.Add };
                shadow.Asker = wrapper switch
                {
                    "asked-only" => new WallStudy.AskedOnly(shadow.Bot, Context),
                    "app-seat" => new CircuitStudy.AppSeat(Context, wrapperSeed, shadow.Bot),
                    _ => shadow.Bot,
                };
                return shadow;
            };
            if (!O.Fast) seat.MakeReview = prefix => new TurnPlanner(Recipe, prefix, styleFinal, () => playbook.Length > 0 ? Playbooks.ForDeck(playbook) : null);
            seat.Shadow = seat.MakeShadow();
            info.Opinion = $"on: {why}, skill {(O.AsPlayed ? styleFinal.Skill : 1.0):0.##}{(planner ? ", with the planner" : ", no planner")}" +
                           $"{(info.Playbook.Length > 0 ? $", playbook {info.Playbook}" : ", no playbook")}";
            return seat;
        }

        /// <summary>The name a deck's playbook is found by: its own, without " (loaner)", or the built-in deck with the same cards.</summary>
        private string PlaybookName(string name, Deck deck)
        {
            if (Playbooks.ForDeck(name) != null) return name;
            var stripped = name.Replace(" (loaner)", "").Trim();
            if (Playbooks.ForDeck(stripped) != null) return stripped;
            if (Setup.DeckByContents.TryGetValue(Setup.Contents(deck), out var same)) return same;
            return Setup.Nearest(deck) ?? stripped; // an edited deck (an alley splash): the built-in deck it was made from
        }

        // ---- Messages: the play-by-play, turns, the watchers

        public void Attach(TagSeat[]? tag)
        {
            _teamNames = new[] { Replay.Names[0], Replay.Names[1] };
            _watcher = new BlunderAudit.Watcher(Duel, Setup.Cards, _teamNames, 0);
            _narrator = new DuelNarrator(Setup.Cards, Replay.Names[0], Replay.Names[1]) { ShowPhases = true };
            _narrator.CodeAt = place => BoardState.PublicCodeAt(Duel, place);
            _narrator.Line += line =>
            {
                if (line.TrimStart().StartsWith("===")) return; // the turn heading is drawn from NewTurn
                var text = line.Trim();
                A.Items.Add(text.StartsWith("--") ? new { k = "p", t = text.Trim('-', ' ') } : new { k = "l", t = text });
            };
            Runner.MessageReceived += m =>
            {
                if (m[0] == Msg.NewTurn)
                {
                    Turn++;
                    TurnPlayer = m[1] & 1;
                    var lp = Context.Board.Players;
                    A.Items.Add(new { k = "t", n = Turn, p = TurnPlayer, w = ActiveName(TurnPlayer, tag), lp = new[] { lp[0].LifePoints, lp[1].LifePoints } });
                }
                if (tag != null && m[0] == Msg.TagSwap)
                {
                    int team = m[1] & 1;
                    _narrator.SetName(team, ActiveName(team, tag));
                }
                // The answer just given was accepted (anything but MSG_RETRY follows it): the watcher sees that one.
                if (m[0] != Msg.Retry) FlushWatch();
                _narrator.Handle(m);
                _watcher.Handle(m);
                AttachFindings();
            };
        }

        private string ActiveName(int team, TagSeat[]? tag) =>
            tag == null ? Replay.Names[team] : TagDuel.Role(tag[team]) == 0 ? Replay.Names[team] : Replay.PartnerNames![team];

        private (Prompt prompt, byte[] answer)? _pendingWatch;

        private void FlushWatch()
        {
            if (_pendingWatch is not { } w) return;
            _pendingWatch = null;
            _watcher.Decided(w.prompt, w.answer);
        }

        /// <summary>
        /// New watcher findings go on the last decision their player (team) made in the turn they're about (the summon's turn,
        /// for risky summons); one with no such decision is listed on its own, with its turn.
        /// </summary>
        private void AttachFindings()
        {
            while (_seenFindings < _watcher.Findings.Count)
            {
                var f = _watcher.Findings[_seenFindings++];
                var flag = new Flag { S = Severity(f.Kind, f.Text), K = f.Kind, T = f.Text + (f.Card.Length > 0 && !f.Text.Contains(f.Card) ? $" ({f.Card})" : ""),
                                      Src = "watcher", Turn = f.Turn };
                int team = f.Player >= 0 ? f.Player : Array.IndexOf(_teamNames, f.Who);
                var target = team >= 0 ? A.Decisions.LastOrDefault(d => d.Turn == f.Turn && d.Team == team) : _lastDecision;
                if (target != null) target.Flags.Add(flag);
                else { flag.T = $"turn {f.Turn}, {(team >= 0 ? _teamNames[team] : f.Who)}: {flag.T}"; A.Unattached.Add(flag); }
            }
        }

        // ---- Decisions

        public void Decide(AnalyzedSeat seat, Prompt prompt, int attempt, byte[] recorded)
        {
            if (attempt > 0 && seat.Last != null)
            {
                // The core rejected the previous answer: this one replaces it. The shadow is asked too (and its answer dropped),
                // as the original bot was, so its fallback's random choices stay in step.
                seat.Last.Retried++;
                seat.Last.PlayedBytes = recorded;
                if (seat.Shadow != null) try { seat.Shadow.Asker.Respond(prompt, attempt); } catch (Exception) { }
                _pendingWatch = (prompt, recorded);
                return;
            }
            if (seat.Last != null) Finish(seat, seat.Last);

            var d = new Decision
            {
                Id = A.Decisions.Count, Turn = Turn, Team = prompt.Player & 1, Slot = seat.Info.Slot, Who = seat.Info.Name,
                Phase = Phase.Name(Context.Board.Phase), Kind = Kind(prompt), Prompt = prompt, PlayedBytes = recorded, FirstBytes = recorded,
                Mine = Context.Board.TurnPlayer == (prompt.Player & 1),
            };
            d.Board = Snap();
            try
            {
                var set = _presenter.Present(prompt);
                d.Set = set;
                d.Title = set.Title;
                d.Detail = set.Detail;
                d.Mode = set.Mode.ToString().ToLowerInvariant();
                foreach (var c in set.Choices)
                {
                    var op = new Option { L = c.Label };
                    if (c.Card is CardRef card)
                    {
                        op.C = card.Code != 0 ? card.Code : RealCode(card);
                        op.H = c.Label.Contains("a face-down card") || c.Label.Contains("a card in hand");
                    }
                    d.Options.Add(op);
                }
                d.Minor = prompt is PlacePrompt or PositionPrompt or SortPrompt or CounterPrompt
                          || (set.Choices.Count <= 1 && !set.CanCancel && !set.CanFinish);
            }
            catch (Exception ex)
            {
                d.Title = $"{prompt.GetType().Name} ({ex.Message})";
                d.Minor = true;
            }

            // The second opinion: the shadow answers first (its answer never reaches the duel).
            if (seat.Shadow != null)
            {
                if (seat.RebuildPending)
                {
                    seat.Shadow = seat.MakeShadow!();
                    seat.RebuildPending = false;
                    seat.FreshTurn = Turn;
                    if (O.AsPlayed && A.DriftFrom < 0) A.DriftFrom = A.Decisions.Count;
                }
                d.Fresh = seat.FreshTurn == Turn;
                var shadow = seat.Shadow;
                shadow.Lines.Clear();
                shadow.Notes.Clear();
                shadow.Planned.Clear();
                try { d.BotBytes = shadow.Asker.Respond(prompt, 0); }
                catch (Exception ex) { d.Why = $"the bot failed to answer: {ex.Message}"; }
                // The bot's log lines start with its name ("<name> activates ..."): keep the words after it.
                var lines = shadow.Lines;
                if (lines.Count > 0)
                    d.Why = UnscoredMargin.Replace(string.Join(" · ", lines.Select(l => l.StartsWith(seat.BotName + " ") ? l[(seat.BotName.Length + 1)..] : l)),
                        "(the usual play had no score to compare)");
                d.RawNotes = shadow.Notes.ToList();
                d.RawPlanned = shadow.Planned.LastOrDefault();
                d.Exposure = shadow.Bot.CurrentExposure(prompt.Player & 1);
                d.Review = seat.MakeReview;
            }
            d.LogPrefix = Log.Responses.Count;
            d.Hint = Context.PromptHint[prompt.Player & 1];
            _pendingWatch = (prompt, recorded);
            A.Decisions.Add(d);
            A.Items.Add(d);
            seat.Last = d;
            _lastDecision = d;
        }

        public void DropSets()
        {
            foreach (var d in A.Decisions) d.Set = null;
        }

        // ---- Scores and regret (chunk 2)

        /// <summary>
        /// For each decision with a second opinion: the bot's heuristic scores, the planner's scores for the options (its own
        /// decision, or a second look at the played option and the bot's pick when the planner didn't score both), the
        /// played option's regret, and the disagreement's severity by that regret (spec section 2).
        /// </summary>
        public void ScoreAll()
        {
            var ds = A.Decisions;
            for (int k = 0; k < ds.Count; k++)
            {
                var d = ds[k];
                try { if (d.BotBytes != null) Score(d, k); }
                catch (Exception ex) { d.ScoreWhy = $"no scores: {ex.Message}"; }
            }
            DropSets();
        }

        private static bool IsAttack(Prompt p, byte[] r) =>
            p is BattlePrompt && r.Length >= 4 && (BattleAction)(BitConverter.ToInt32(r, 0) & 0xffff) == BattleAction.Attack;

        /// <summary>The target of the attack declared at decision <paramref name="k"/>: the attack-target pick that followed, else direct.</summary>
        private ((int, uint)? target, bool direct) PlayedTarget(int k)
        {
            var d = A.Decisions[k];
            for (int j = k + 1; j < A.Decisions.Count && j <= k + 8; j++)
            {
                var n = A.Decisions[j];
                if (n.Slot != d.Slot) continue;
                if (n.Prompt is SelectCardPrompt && n.Hint == DuelContext.HintAttackTarget && n.Set != null)
                {
                    var picks = Decode(n.Prompt, n.Set, n.PlayedBytes, out _);
                    if (picks.Count == 1 && picks[0] < n.Set.Choices.Count && n.Set.Choices[picks[0]].Card is CardRef c) return ((c.Controller, c.Sequence), false);
                    return (null, false);
                }
                if (n.Prompt is YesNoPrompt yn && yn.Description == DuelContext.DescDirectAttack)
                    return (null, n.PlayedBytes.Length >= 4 && BitConverter.ToInt32(n.PlayedBytes, 0) == 1);
                if (n.Prompt is BattlePrompt or IdlePrompt) break;
            }
            return (null, true);
        }

        private void Score(Decision d, int k)
        {
            if (d.RawNotes is { Count: > 0 } notes)
                d.Notes = notes.GroupBy(n => n.Label).Select(g => g.Last())
                    .OrderBy(n => n.Rejected != null).ThenByDescending(n => n.Score).Take(40)
                    .Select(n => new NoteRow { L = n.Label, S = Math.Round(n.Score, 1), R = n.Rejected }).ToList();

            int me = d.Prompt.Player & 1;
            bool attack = IsAttack(d.Prompt, d.PlayedBytes);
            var (playedTarget, playedDirect) = attack ? PlayedTarget(k) : (null, false);
            bool Same(PlannerScore x, byte[] r, (int, uint)? t, bool direct, bool isAttack) =>
                x.Response.SequenceEqual(r) && (!isAttack || (direct || t == null ? x.Direct || x.Target == null : Equals(x.Target, t)));

            var pd = d.RawPlanned is { Diverged: false } ok ? ok : null;
            List<PlannerScore>? scores = pd?.Scores;
            // The bot's pick is the row for the answer it gave: the planner's choice, or the usual play, when that's what it answered
            // (after a blunder check vetoed the planned line it gave something else, which may have no row).
            PlannerScore? botPick = pd == null || d.BotBytes == null ? null
                : new[] { pd.Chosen }.Concat(pd.Scores.Where(x => x.Usual)).Concat(pd.Scores).FirstOrDefault(x => x != null && x.Response.SequenceEqual(d.BotBytes));
            int played = scores?.FindIndex(x => Same(x, d.PlayedBytes, playedTarget, playedDirect, attack)) ?? -1;
            bool fromPlanner = pd != null;
            string source = pd != null ? $"the planner's own decision ({pd.Scores.Count} options played out, margin {pd.Margin:0})" : "";

            // A second look: the played option and the bot's pick, played out the same way, when the planner didn't score the played one.
            bool supported = d.Prompt is IdlePrompt or BattlePrompt or SelectCardPrompt or SelectUnselectPrompt or SumPrompt or TributePrompt;
            if ((played < 0 || botPick == null) && d.Agree == false && d.Mine && supported && d.Review != null)
            {
                var botBytes = d.BotBytes!;
                (int, uint)? botTarget = botPick?.Target;
                bool botDirect = botPick?.Direct ?? false;
                if (botPick == null && IsAttack(d.Prompt, botBytes))
                {
                    var best = d.RawNotes?.Where(n => n.Rejected == null && n.Response.SequenceEqual(botBytes)).OrderByDescending(n => n.Score).FirstOrDefault();
                    botTarget = best?.Target;
                    botDirect = botTarget == null;
                }
                var prefix = new ResponseLog();
                prefix.Responses.AddRange(Log.Responses.Take(d.LogPrefix));
                var review = d.Review(prefix);
                var options = new List<(byte[], string, (int controller, uint sequence)?, bool)>
                    { (d.PlayedBytes, "played", playedTarget, playedDirect), (botBytes, "bot", botTarget, botDirect) };
                var second = review.ScoreOptions(d.Prompt, me, options, d.Exposure, ToBoard(d.Board));
                if (second != null)
                {
                    fromPlanner = false;
                    scores = second;
                    botPick = second[1];
                    played = 0;
                    source = "a second look: the played option and the bot's pick, played out by the planner";
                }
                else source = "no scores: the planner's replay of this point diverged";
            }
            if (scores == null || scores.Count == 0) { if (source.Length > 0) d.ScoreWhy = source; return; }
            // A rollout the planner couldn't answer scores -1e7, and a sentinel or non-finite score is no score either.
            bool Scored(PlannerScore x) => Real(x.Final) && x.Final > Unanswerable;
            if (!scores.Any(Scored)) { d.ScoreWhy = source + " · no option could be played out"; return; }

            double bestFinal = scores.Where(Scored).Max(x => x.Final);
            d.Scores = scores.Select(x => new ScoreRow
            {
                L = ScoreLabel(d, x), F = Real(x.Final) ? Math.Round(x.Final) : Unanswerable * 2, Pl = Real(x.Plain) ? Math.Round(x.Plain) : Unanswerable * 2,
                In = x.Interrupted is double i && Real(i) ? Math.Round(i) : null, R = Real(x.Risk) ? Math.Round(x.Risk) : 0, G = Scored(x) ? Math.Round(bestFinal - x.Final) : null, U = fromPlanner && x.Usual, C = fromPlanner && pd?.Chosen == x,
                P = played >= 0 && scores[played] == x, B = botPick == x,
            }).OrderByDescending(r => r.F).ToList();
            d.ScoreWhy = source + (played < 0 ? " · the played option wasn't among them" : "")
                         + (scores.Any(x => !Scored(x)) ? " · rows marked unanswerable couldn't be played out" : "");
            if (played < 0 || !Scored(scores[played])) return;
            d.Regret = Math.Round(bestFinal - scores[played].Final);

            // The disagreement's severity follows the regret.
            if (d.Flags.FirstOrDefault(f => f.K == "bot disagrees") is Flag flag)
            {
                double g = d.Regret.Value;
                var bySize = g >= 2000 ? "high" : g >= 800 ? "medium" : g >= 400 ? "low" : "info";
                // The caps hold whatever the regret: minor and non-judgement prompts and repeats are information, a fresh follow-up is at most low.
                flag.S = d.Minor || !d.Judgement || d.Repeat ? "info" : d.Fresh && d.FollowUp && Weight(bySize) > 1 ? "low" : bySize;
                flag.T += g >= Decisive ? " · the planner's rollouts: the best line wins or avoids a loss where the played one doesn't"
                    : g >= 1 ? $" · regret {g:0} (the planner rates the best option {g:0} points higher)"
                    : " · the planner rates the played option at least as high";
            }
        }

        private string ScoreLabel(Decision d, PlannerScore x)
        {
            var picks = d.Set == null ? new List<int>() : Decode(d.Prompt, d.Set, x.Response, out var special);
            var label = picks.Count > 0 ? string.Join(", ", picks.Select(i => i < d.Options.Count ? OptionText(d.Options[i]) : $"option {i + 1}")) : x.Label;
            if (IsAttack(d.Prompt, x.Response))
            {
                if (x.Target is var (controller, seq) && controller is 0 or 1 && seq < d.Board.P[controller].M.Count && d.Board.P[controller].M[(int)seq] is int[] card)
                    label += $" → {Setup.Cards.Get((uint)card[0])?.Name ?? "a monster"}";
                else label += " → directly";
            }
            return label;
        }

        /// <summary>The monsters of a snapshot as a board (the planner's battle risk reads the attacker's ATK).</summary>
        private static BoardState ToBoard(Snapshot s)
        {
            var b = new BoardState();
            for (int i = 0; i < 2; i++)
                b.Players[i].Monsters = s.P[i].M.Select(a => a == null ? null : new FieldCard { Code = (uint)a[0], Attack = a[2], Defense = a[3] }).ToList();
            return b;
        }

        public void FinishAll()
        {
            FlushWatch();
            foreach (var s in _seats) if (s.Last != null) Finish(s, s.Last);
            AttachFindings();
        }

        /// <summary>Settles a decision once its final answer is known: what was played, the bot's pick, and whether they agree.</summary>
        private void Finish(AnalyzedSeat seat, Decision d)
        {
            var (played, _) = Describe(d, d.PlayedBytes, mark: 1);
            d.Played = played;
            if (d.Retried > 0)
                d.Played += $" (played after {d.Retried} rejected{(d.FirstBytes.SequenceEqual(d.PlayedBytes) ? "" : $"; first answer: {Describe(d, d.FirstBytes, mark: 0).text}")})";
            d.Judgement = d.Prompt is IdlePrompt or BattlePrompt or ChainPrompt or SelectCardPrompt or SelectUnselectPrompt or TributePrompt or SumPrompt or EffectYesNoPrompt;
            // Fresh memory (the bot was rebuilt after an earlier disagreement this turn) caps the follow-ups (selections, chains) at low.
            d.FollowUp = d.Prompt is not (IdlePrompt or BattlePrompt);
            if (d.BotBytes != null)
            {
                var (botText, botKey) = Describe(d, d.BotBytes, mark: 2);
                d.Bot = botText;
                // The shadow's first answer is compared with the first answer given: a retry came from the bot's fallback, as it would again.
                d.Agree = botKey == Describe(d, d.FirstBytes, mark: 0).key;
                if (d.Agree == false)
                {
                    // The same pick again in the same turn (the bot keeps wanting the play it suggested): flagged once.
                    bool repeat = seat.LastWish == (Turn, botText);
                    d.Repeat = repeat;
                    seat.LastWish = (Turn, botText);
                    var severity = d.Minor || (d.Fresh && d.FollowUp) || repeat || !d.Judgement ? "info" : "low";
                    d.Flags.Insert(0, new Flag
                    {
                        S = severity, K = "bot disagrees", Src = "bot",
                        T = $"the bot would: {botText}" + (repeat ? " (again: it wanted this earlier in the turn)"
                            : d.Fresh ? " (fresh memory: the bot was rebuilt after an earlier disagreement this turn)" : ""),
                    });
                    seat.RebuildPending = true;
                }
            }
        }

        // ---- Reading answers

        /// <summary>An answer in words, and a key for comparing two answers (selections as sets).</summary>
        /// <summary><paramref name="mark"/>: 1 marks the picked options as played, 2 as the bot's, 0 neither.</summary>
        private (string text, string key) Describe(Decision d, byte[] r, int mark)
        {
            var set = d.Set;
            var picks = Decode(d.Prompt, set, r, out var special);
            foreach (var i in picks) if (i >= 0 && i < d.Options.Count) { if (mark == 2) d.Options[i].B = true; else if (mark == 1) d.Options[i].P = true; }
            if (special != null) return (special, "s:" + special);
            if (picks.Count == 0) return ($"answer {BitConverter.ToString(r)}", "r:" + BitConverter.ToString(r));
            var labels = picks.Select(i => i >= 0 && i < d.Options.Count ? OptionText(d.Options[i]) : $"option {i + 1}");
            bool ordered = d.Prompt is SortPrompt;
            var keyed = ordered ? picks : picks.OrderBy(i => i).ToList();
            return (string.Join(ordered ? " then " : ", ", labels), "p:" + string.Join(",", keyed));
        }

        private string OptionText(Option op) =>
            op.H && op.C != 0 && Setup.Cards.Get(op.C) is CardInfo info ? $"{op.L} [{info.Name}]" : op.L;

        /// <summary>Which of the presented choices an answer picks (or a special answer: no chain, cancel, finish, a declared card).</summary>
        private List<int> Decode(Prompt prompt, ChoiceSet? set, byte[] r, out string? special)
        {
            special = null;
            var picks = new List<int>();
            if (set == null) return picks;
            if (set.CanCancel && set.Cancel().SequenceEqual(r)) { special = prompt is ChainPrompt ? "no response" : "cancel"; return picks; }
            if (set.CanFinish && set.Finish().SequenceEqual(r)) { special = prompt is SortPrompt ? "the default order" : "finish"; return picks; }
            switch (prompt)
            {
                case AnnounceCardPrompt when r.Length >= 4:
                    special = $"declares {Setup.Cards.Get(BitConverter.ToUInt32(r, 0))?.Name ?? BitConverter.ToUInt32(r, 0).ToString()}";
                    return picks;
                case SelectCardPrompt or TributePrompt or SumPrompt when r.Length >= 8 && BitConverter.ToInt32(r, 0) == 0:
                {
                    int count = (int)BitConverter.ToUInt32(r, 4);
                    if (r.Length == 8 + 4 * count)
                    {
                        for (int k = 0; k < count; k++) picks.Add((int)BitConverter.ToUInt32(r, 8 + 4 * k));
                        return picks;
                    }
                    break;
                }
                case SelectCardPrompt or TributePrompt or SumPrompt when r.Length == 4 && BitConverter.ToInt32(r, 0) == -1:
                    special = "cancel";
                    return picks;
                case SortPrompt when r.Length == set.Choices.Count:
                    return Enumerable.Range(0, r.Length).OrderBy(i => r[i]).ToList();
                case PlacePrompt when r.Length % 3 == 0 && r.Length > 3:
                    for (int k = 0; k < r.Length; k += 3)
                    {
                        var zone = Responses.Places(new[] { (r[k], (uint)r[k + 1], r[k + 2]) });
                        for (int i = 0; i < set.Choices.Count; i++)
                            if (set.Choose(i).SequenceEqual(zone)) { picks.Add(i); break; }
                    }
                    return picks;
            }
            for (int i = 0; i < set.Choices.Count; i++)
            {
                byte[] built;
                try { built = set.Choose(i); } catch (Exception) { continue; }
                if (built.SequenceEqual(r)) { picks.Add(i); return picks; }
            }
            return picks;
        }

        private static string Kind(Prompt p) => p switch
        {
            IdlePrompt => "main", BattlePrompt => "battle", ChainPrompt => "chain", SelectCardPrompt or SelectUnselectPrompt or TributePrompt or SumPrompt => "select",
            EffectYesNoPrompt or YesNoPrompt => "yesno", OptionPrompt => "option", PlacePrompt => "zone", PositionPrompt => "position", SortPrompt => "order",
            _ => "other",
        };

        // ---- The board, with nothing hidden

        private uint RealCode(CardRef c)
        {
            if (c.Controller > 1 || (c.Location & 0x80) != 0) return 0;
            var list = Duel.QueryLocation(c.Controller, c.Location & 0x7f);
            return c.Sequence < list.Count && list[(int)c.Sequence] is FieldCard card ? card.Code : 0;
        }

        private Snapshot Snap()
        {
            var board = new BoardState();
            board.Refresh(Duel);
            var lp = Context.Board;
            var s = new Snapshot { Tp = lp.TurnPlayer & 1, Ph = Phase.Name(lp.Phase), Lp = new[] { lp.Players[0].LifePoints, lp.Players[1].LifePoints } };
            // Face-down and Defense only mean something on the field (and face-down for banished cards); revealed only in the hand.
            static int[]? Card(FieldCard? c, char where)
            {
                if (c == null) return null;
                bool field = where is 'm' or 's';
                int flags = ((field || where == 'b') && !c.IsFaceUp ? 1 : 0) | (where == 'm' && c.IsDefense ? 2 : 0) | (field && c.IsNegated ? 4 : 0)
                            | (where == 'h' && c.IsPublic ? 8 : 0);
                return new[] { (int)c.Code, flags, c.Attack, c.Defense, c.OverlayCount };
            }
            for (int i = 0; i < 2; i++)
            {
                var p = board.Players[i];
                var sp = new SnapPlayer { D = p.DeckCount, XN = p.ExtraDeck.Count(c => c != null) };
                sp.H = p.Hand.Select(c => Card(c, 'h')).ToList();
                sp.M = p.Monsters.Select(c => Card(c, 'm')).ToList();
                sp.S = p.SpellTraps.Select(c => Card(c, 's')).ToList();
                sp.G = p.Graveyard.Select(c => Card(c, 'g')).ToList();
                sp.B = p.Banished.Select(c => Card(c, 'b')).ToList();
                sp.X = p.ExtraDeck.Where(c => c != null && c.IsFaceUp).Select(c => Card(c, 'x')).ToList();
                s.P[i] = sp;
            }
            foreach (var link in Context.Chain)
                s.Ch.Add(new SnapLink { C = link.Code, P = link.Controller, T = link.Targets.Select(t => t.Code != 0 ? t.Code : RealCode(t)).ToList() });
            return s;
        }
    }
}
