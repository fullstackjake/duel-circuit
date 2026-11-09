using System.Globalization;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Replays;

namespace Ygo.Harness;

/// <summary>
/// How one seat's bot was built, so the replay analyzer can rebuild it exactly (docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md,
/// section 1). Slots: 0 and 1 are the teams' leads, 2 and 3 their tag partners (the app's seed salts).
/// </summary>
public sealed class SeatSpec
{
    public int Slot;
    public string Character = "", Name = "", Deck = "", Playbook = "", Note = "";
    /// <summary>How the seat was wrapped in the run: "asked-only" (WallStudy.AskedOnly: chains it isn't asked about pass), "app-seat"
    /// (CircuitStudy.AppSeat: the app's prompt panel around the stand-in), or "".</summary>
    public string Wrapper = "";
    /// <summary>The app seat's own seed (its HumanDuelist's), for "app-seat".</summary>
    public int WrapperSeed;
    public double Skill = 1, Aggression = 0.5, Caution = 0.5, Patience = 0.5, Spending = 0.5;
    public int Seed;
    public bool Planner;
    /// <summary>False for a seat that isn't a SmartDuelist (a dummy, a scripted seat): no second opinion.</summary>
    public bool Bot = true;

    /// <summary>
    /// A SmartDuelist seat: <paramref name="playbook"/> is the name the bot's playbook was looked up by; it is written empty
    /// when no playbook answers to it (Playbooks.Off, or none for that deck), so the analyzer rebuilds the bot without one.
    /// </summary>
    public static SeatSpec Of(int slot, Personality? style, string playbook, int seed, bool planner, string note = "", string wrapper = "", int wrapperSeed = 0)
    {
        var s = style ?? Personality.Balanced;
        return new SeatSpec
        {
            Slot = slot, Character = s.Id, Name = s.Name, Deck = playbook, Playbook = Playbooks.ForDeck(playbook) != null ? playbook : "", Seed = seed,
            Planner = planner, Note = note, Wrapper = wrapper, WrapperSeed = wrapperSeed,
            Skill = s.Skill, Aggression = s.Aggression, Caution = s.Caution, Patience = s.Patience, Spending = s.Spending,
        };
    }

    public static SeatSpec Of(int slot, Personality? style, Deck deck, int seed, bool planner, string note = "", string wrapper = "", int wrapperSeed = 0) =>
        Of(slot, style, Playbooks.RecipeOf(deck), seed, planner, note, wrapper, wrapperSeed);

    public Personality ToPersonality() => new Personality
    {
        Id = Character.Length > 0 ? Character : "balanced", Name = Name.Length > 0 ? Name : "Duelist",
        Skill = Skill, Aggression = Aggression, Caution = Caution, Patience = Patience, Spending = Spending,
    };
}

/// <summary>The `.replay.bot` sidecar: one line per seat, `key=value` pairs separated by "; ".</summary>
public sealed class BotSidecar
{
    public string Study = "";
    public bool TurnCap;
    public List<SeatSpec> Seats = new List<SeatSpec>();

    public static string PathFor(string replayPath) => replayPath + ".bot";

    public void Save(string path)
    {
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "# Duel Circuit: how each seat's bot was built (read by --analyze-replay)",
            $"study: {Study}",
            $"turncap: {(TurnCap ? "on" : "off")}",
        };
        foreach (var s in Seats.OrderBy(s => s.Slot))
            lines.Add($"seat {s.Slot}: bot={(s.Bot ? "on" : "off")}; character={s.Character}; name={Clean(s.Name)}; playbook={Clean(s.Playbook)}; " +
                      string.Format(inv, "skill={0:0.###}; aggression={1:0.###}; caution={2:0.###}; patience={3:0.###}; spending={4:0.###}; ",
                          s.Skill, s.Aggression, s.Caution, s.Patience, s.Spending) +
                      $"seed={s.Seed}; planner={(s.Planner ? "on" : "off")}" + (s.Wrapper.Length > 0 ? $"; wrapper={s.Wrapper}; wrapseed={s.WrapperSeed}" : "")
                      + (s.Note.Length > 0 ? $"; note={Clean(s.Note)}" : ""));
        File.WriteAllLines(path, lines);
    }

    private static string Clean(string s) => s.Replace(";", ",").Replace("\n", " ");

    public static BotSidecar? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var car = new BotSidecar();
        var inv = CultureInfo.InvariantCulture;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("study:")) { car.Study = line[6..].Trim(); continue; }
            if (line.StartsWith("turncap:")) { car.TurnCap = line[8..].Trim() == "on"; continue; }
            if (!line.StartsWith("seat ")) continue;
            var colon = line.IndexOf(':');
            if (colon < 0 || !int.TryParse(line[5..colon].Trim(), out var slot)) continue;
            var s = new SeatSpec { Slot = slot };
            foreach (var pair in line[(colon + 1)..].Split(';'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var key = pair[..eq].Trim();
                var value = pair[(eq + 1)..].Trim();
                double D() => double.TryParse(value, NumberStyles.Float, inv, out var d) ? d : 0.5;
                switch (key)
                {
                    case "bot": s.Bot = value == "on"; break;
                    case "character": s.Character = value; break;
                    case "name": s.Name = value; break;
                    case "playbook": s.Playbook = value; if (value.Length > 0) s.Deck = value; break;
                    case "skill": s.Skill = D(); break;
                    case "aggression": s.Aggression = D(); break;
                    case "caution": s.Caution = D(); break;
                    case "patience": s.Patience = D(); break;
                    case "spending": s.Spending = D(); break;
                    case "seed": s.Seed = int.TryParse(value, out var n) ? n : 0; break;
                    case "planner": s.Planner = value == "on"; break;
                    case "note": s.Note = value; break;
                    case "wrapper": s.Wrapper = value; break;
                    case "wrapseed": s.WrapperSeed = int.TryParse(value, out var ws) ? ws : 0; break;
                }
            }
            car.Seats.Add(s);
        }
        return car;
    }
}

/// <summary>
/// `--keep-replays &lt;dir&gt; [--keep flagged|stalls|all] [--keep-max 200]`: the harness runs that play CPU-vs-CPU duels save
/// them as replays (with a <see cref="BotSidecar"/>) for `--analyze-replay`. Opt-in: <see cref="Current"/> is null unless
/// the flag is given, and keeping only records what was played (it never changes a duel).
/// </summary>
public sealed class ReplayKeeper
{
    public static ReplayKeeper? Current;

    public readonly string Folder;
    /// <summary>"flagged" (a BlunderAudit watcher finding), "stalls" (the prompt cap stopped it) or "all".</summary>
    public readonly string Mode;
    public readonly int Max;
    private int _kept;
    public int Kept => _kept;

    public ReplayKeeper(string folder, string mode, int max)
    {
        Folder = Path.GetFullPath(folder);
        Mode = mode;
        Max = max;
        Directory.CreateDirectory(Folder);
    }

    /// <summary>The keeper the command line asks for, or null.</summary>
    /// <summary>The keeper the command line asks for, or null; a bad value ends the run with exit code 1.</summary>
    public static ReplayKeeper? FromArgs(string[] args)
    {
        string? After(string flag) => Array.IndexOf(args, flag) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        if (!args.Contains("--keep-replays")) return null;
        var folder = After("--keep-replays");
        var mode = After("--keep") ?? "flagged";
        var maxText = After("--keep-max");
        if (Check(folder, mode, maxText, out var max) is string why) { Console.Error.WriteLine(why); Environment.Exit(1); return null; }
        var keeper = new ReplayKeeper(folder!, mode, max);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Console.WriteLine(keeper.Summary());
        return keeper;
    }

    /// <summary>What's wrong with the --keep-replays arguments, or null (then <paramref name="max"/> is the cap).</summary>
    public static string? Check(string? folder, string mode, string? maxText, out int max)
    {
        max = 200;
        if (folder == null || folder.StartsWith("--")) return "--keep-replays <dir>: name the folder to save the duels in.";
        if (SafePaths.IsInside(folder, SafePaths.SaveRoot)) return $"--keep-replays {folder}: that's inside the game's save folder; pick a folder elsewhere (build/... works).";
        if (mode is not ("flagged" or "stalls" or "all")) return $"--keep {mode}: use flagged, stalls or all.";
        if (maxText != null && (!int.TryParse(maxText, out max) || max <= 0)) return $"--keep-max {maxText}: give a number above 0.";
        return null;
    }

    public string Summary() =>
        $"--keep-replays: kept {Math.Min(_kept, Max)} in {Folder}" + (_kept > Max ? $", {_kept - Max} dropped by --keep-max {Max}" : "");

    /// <summary>A duel being watched for keeping.</summary>
    public sealed class Watch
    {
        internal string Study = "";
        internal string Label = "";
        internal DuelRecipe Recipe = null!;
        internal ResponseLog Log = null!;
        internal string[] Names = null!;
        internal SeatSpec[] Seats = null!;
        internal bool TurnCap;
        internal BlunderAudit.Watcher? Watcher;
    }

    /// <summary>
    /// Starts watching a duel (call before running it). <paramref name="names"/> are the teams' names; <paramref name="seats"/>
    /// how each bot was built. In flagged mode a BlunderAudit watcher listens to the duel's messages.
    /// </summary>
    public Watch Begin(string study, string label, DuelRecipe recipe, Duel duel, ResponseLog log, string[] names,
        SeatSpec[] seats, bool turnCap = false)
    {
        var w = new Watch { Study = study, Label = label, Recipe = recipe, Log = log, Names = names, Seats = seats, TurnCap = turnCap };
        if (Mode == "flagged") w.Watcher = new BlunderAudit.Watcher(duel, recipe.Cards, names, 0);
        return w;
    }

    /// <summary>
    /// A seat for the duel, shown to the watcher (its Battle Phase answers: the missed-lethal check), as --audit's seats are.
    /// The answers pass through untouched; without a watcher the seat itself comes back.
    /// </summary>
    public static IDuelist Wrap(Watch? w, IDuelist seat) => w?.Watcher is { } watcher ? new BlunderAudit.Spy(seat, watcher.Decided) : seat;

    /// <summary>Lets the watcher read the duel's messages (call once the runner exists).</summary>
    public static void Attach(Watch? w, DuelRunner runner)
    {
        if (w?.Watcher != null) runner.MessageReceived += w.Watcher.Handle;
    }

    /// <summary>Keeps the duel if it matches the mode (and the cap isn't reached).</summary>
    public void Finish(Watch? w, DuelResult result)
    {
        if (w == null) return;
        bool keep = Mode switch
        {
            "all" => true,
            "stalls" => result.Winner < 0,
            // A finding of low severity or more (a risky summon that still won is only information).
            _ => w.Watcher?.Findings.Any(f => ReplayAnalyzer.Weight(ReplayAnalyzer.Severity(f.Kind, f.Text)) >= 1) == true,
        };
        if (keep) Save(w.Study, w.Label, w.Recipe, w.Log, w.Names, w.Seats, result, w.TurnCap);
    }

    /// <summary>Saves a duel as a replay plus its sidecar, unless the cap is reached; returns the path or null.</summary>
    /// <summary>With <paramref name="overwrite"/> a file of the same name is replaced (--audit --out, as before); otherwise numbered.</summary>
    public string? Save(string study, string label, DuelRecipe recipe, ResponseLog log, string[] names, SeatSpec[] seats, DuelResult result,
        bool turnCap = false, string? fileName = null, bool overwrite = false)
    {
        if (Interlocked.Increment(ref _kept) > Max) return null;
        var replay = Build(recipe, log, names, seats, result);
        var stem = fileName ?? Safe($"{study}-{label}");
        string path;
        lock (this)
        {
            path = Path.Combine(Folder, stem + ".replay");
            if (!overwrite) for (int n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"{stem}-{n}.replay");
            replay.Save(path);
        }
        new BotSidecar { Study = study, TurnCap = turnCap, Seats = seats.ToList() }.Save(BotSidecar.PathFor(path));
        return path;
    }

    /// <summary>The replay of a duel played from <paramref name="recipe"/> with <paramref name="log"/>'s answers.</summary>
    public static Replay Build(DuelRecipe recipe, ResponseLog log, string[] names, SeatSpec[] seats, DuelResult result)
    {
        SeatSpec? Slot(int s) => seats.FirstOrDefault(x => x.Slot == s);
        var replay = new Replay
        {
            Settings = recipe.Settings,
            Names = new[] { names[0], names[1] },
            Characters = new[] { Slot(0)?.Character ?? "", Slot(1)?.Character ?? "" },
            CardCount = recipe.Cards.All.Count(),
            Responses = log.Responses.ToList(),
            Winner = result.Winner,
            Turns = result.Turns,
        };
        if (recipe.IsTag)
        {
            // The recipe puts whoever starts first in each team's core slot 0 (TagDuel.Recipe); the replay keeps leads and partners.
            bool aStarts = recipe.Settings.FirstPlayer == 0;
            var leadA = aStarts ? recipe.Deck0 : recipe.Partner0!;
            var partnerA = aStarts ? recipe.Partner0! : recipe.Deck0;
            var leadB = aStarts ? recipe.Partner1! : recipe.Deck1;
            var partnerB = aStarts ? recipe.Deck1 : recipe.Partner1!;
            replay.Decks = new[] { leadA.Clone(), leadB.Clone() };
            replay.Partners = new[] { partnerA.Clone(), partnerB.Clone() };
            replay.PartnerNames = new[] { Slot(2)?.Name ?? "", Slot(3)?.Name ?? "" };
            replay.PartnerCharacters = new[] { Slot(2)?.Character ?? "", Slot(3)?.Character ?? "" };
            replay.PartnerDeckNames = new[] { partnerA.Name, partnerB.Name };
        }
        else replay.Decks = new[] { recipe.Deck0.Clone(), recipe.Deck1.Clone() };
        replay.DeckNames = new[] { replay.Decks[0].Name, replay.Decks[1].Name };
        return replay;
    }

    private static string Safe(string s) =>
        new string(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('-');
}
