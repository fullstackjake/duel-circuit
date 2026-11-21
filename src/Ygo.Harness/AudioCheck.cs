using System.Globalization;
using Ygo.Engine.Audio;

namespace Ygo.Harness;

/// <summary>
/// `--audio-check [--write-baseline]` (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md section M): renders every sound in memory
/// and checks it. Rows: 1 every effect renders cleanly and the same twice; 2 loudness, length and peak against
/// src/Ygo.Harness/fixtures/audio-baseline.txt; 3 the families' loudness rules; 4 every theme and the menu loop (lengths,
/// seams, loudness, peaks); 5 the boss list; 6 the tuning tables; 7 the output limiter (Ygo.Engine.Audio.Limiter, the
/// game's own code) on the review's pile-ups; 8 warnings only: the small-speaker table and aliasing (pass 2's M5).
/// Lines read "audio check: ok|FAIL|warn &lt;what&gt;"; exit 1 on any FAIL. Writes nothing unless --write-baseline.
/// </summary>
public static class AudioCheck
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static int _fails, _warns;

    private static void Ok(string what) => Console.WriteLine($"audio check: ok {what}");
    private static void Fail(string what) { _fails++; Console.WriteLine($"audio check: FAIL {what}"); }
    private static void Warn(string what) { _warns++; Console.WriteLine($"audio check: warn {what}"); }
    private static void Check(bool ok, string what) { if (ok) Ok(what); else Fail(what); }
    private static string F(double x, string format = "F1") => x.ToString(format, Inv);

    public static string BaselinePath(string root) => Path.Combine(root, "src", "Ygo.Harness", "fixtures", "audio-baseline.txt");

    public static int Run(string[] args)
    {
        _fails = _warns = 0;
        bool writeBaseline = args.Contains("--write-baseline");
        string root = FindRoot();
        const int rate = Sfx.Rate;

        // ---- 1. Every effect renders, cleanly and the same twice ----------------------------------------------------
        var effects = new Dictionary<string, float[]>();
        var momentary = new Dictionary<string, double>();
        int problems1 = 0;
        foreach (var (name, make) in Sfx.Effects)
        {
            var a = make();
            var b = make();
            effects[name] = a;
            var why = new List<string>();
            if (a.Any(x => float.IsNaN(x) || float.IsInfinity(x))) why.Add("NaN or infinity");
            float peak = a.Length == 0 ? 0 : a.Max(x => Math.Abs(x));
            double seconds = a.Length / (double)rate;
            if (peak > 0.96f) why.Add($"peak {F(peak, "F3")} > 0.96");
            if (a.Length > 0 && (Math.Abs(a[0]) > 0.01f || Math.Abs(a[^1]) > 0.01f)) why.Add($"ends {F(a[0], "F3")} / {F(a[^1], "F3")} over 0.01");
            if (seconds < 0.01 || seconds > 3.0) why.Add($"length {F(seconds, "F3")} s outside 0.01-3.0");
            if (!a.AsSpan().SequenceEqual(b)) why.Add("two renders differ");
            if (why.Count > 0) { problems1++; Fail($"1 effect {name}: {string.Join("; ", why)}"); }
            momentary[name] = why.Contains("NaN or infinity") ? double.NaN : Loudness.MomentaryMax(a, rate);
        }
        if (problems1 == 0)
        {
            var peaks = effects.Values.Select(s => s.Max(x => Math.Abs(x))).ToList();
            var lengths = effects.Values.Select(s => s.Length / (double)rate).ToList();
            Ok($"1 effects: {effects.Count} render, no NaN, peaks {F(peaks.Min(), "F2")}-{F(peaks.Max(), "F2")}, lengths {F(lengths.Min(), "F3")}-{F(lengths.Max(), "F2")} s, clean ends, identical twice");
        }

        // ---- 2. Against the baseline ----------------------------------------------------------------------------------
        string baselinePath = BaselinePath(root);
        if (writeBaseline)
        {
            WriteBaseline(baselinePath, effects, momentary);
            Console.WriteLine($"audio check: wrote {effects.Count} effects to {baselinePath}");
        }
        CheckBaseline(baselinePath, effects, momentary);

        // ---- 3. Families -----------------------------------------------------------------------------------------------
        CheckFamilies(momentary);

        // ---- 4. Themes and the menu loop -------------------------------------------------------------------------------
        var rendered = CheckThemes();
        CheckThemeList(baselinePath);

        // ---- 5. The boss list ------------------------------------------------------------------------------------------
        {
            var (bosses, expected) = SoundExport.BossList(root);
            var everyday = bosses.Where(b => b.theme == Ygo.Engine.Campaign.BossMusic.Everyday || !Sfx.Themes.ContainsKey(b.theme))
                .Select(b => b.who).Distinct().ToList();
            Check(SoundExport.BossListOk(bosses, expected),
                $"5 boss list: {bosses.Count} boss duels (expected {expected}), "
                + (everyday.Count == 0 ? "each with its own theme" : $"on the everyday theme: {string.Join(", ", everyday)}"));
        }

        // ---- 6. Tuning -------------------------------------------------------------------------------------------------
        CheckTuning();

        // ---- 7. The limiter --------------------------------------------------------------------------------------------
        CheckLimiter(effects, rendered["duel"]);

        // ---- 8. Warnings: small speakers and aliasing ------------------------------------------------------------------
        CheckWarnings(effects);

        Console.WriteLine($"audio check: {(_fails == 0 ? "all fine" : $"{_fails} FAIL")}, {_warns} warn");
        return _fails > 0 ? 1 : 0;
    }

    // ---- Row 2 -------------------------------------------------------------------------------------------------------

    private static void WriteBaseline(string path, Dictionary<string, float[]> effects, Dictionary<string, double> momentary)
    {
        var lines = new List<string>
        {
            "# The effects' loudness baseline for --audio-check (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md section M, row 2).",
            "# One effect a line: name, momentary max (LUFS, BS.1770 over the sound and 0.5 s of silence), length (s), sample peak.",
            "# The check allows loudness +-1.5 LU, length +-2%, peak +-0.02. After a deliberate change to a sound, rewrite it:",
            "#   dotnet build/bin/<name>/Ygo.Harness.dll --audio-check --write-baseline",
        };
        foreach (var (name, s) in effects)
            lines.Add($"{name} {F(momentary[name], "F2")} {F(s.Length / (double)Sfx.Rate, "F4")} {F(s.Max(x => Math.Abs(x)), "F3")}");
        lines.Add("# The themes (row 4): one missing from Sfx.Themes fails; a new one warns until this file is rewritten.");
        foreach (var name in Sfx.Themes.Keys) lines.Add($"theme {name}");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    private static void CheckBaseline(string path, Dictionary<string, float[]> effects, Dictionary<string, double> momentary)
    {
        if (!File.Exists(path)) { Fail($"2 baseline: {path} is missing (write it with --write-baseline)"); return; }
        var baseline = new Dictionary<string, (double lufs, double length, double peak)>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p[0] == "theme") continue; // row 4's list
            baseline[p[0]] = (double.Parse(p[1], Inv), double.Parse(p[2], Inv), double.Parse(p[3], Inv));
        }
        int problems = 0;
        foreach (var (name, s) in effects)
        {
            if (!baseline.TryGetValue(name, out var b)) { problems++; Fail($"2 baseline: {name} isn't in the baseline"); continue; }
            double lufs = momentary[name], length = s.Length / (double)Sfx.Rate, peak = s.Max(x => Math.Abs(x));
            var why = new List<string>();
            if (!(Math.Abs(lufs - b.lufs) <= 1.5)) why.Add($"loudness {F(lufs, "F2")} vs {F(b.lufs, "F2")} LUFS");
            if (!(Math.Abs(length - b.length) <= 0.02 * b.length)) why.Add($"length {F(length, "F4")} vs {F(b.length, "F4")} s");
            if (!(Math.Abs(peak - b.peak) <= 0.02)) why.Add($"peak {F(peak, "F3")} vs {F(b.peak, "F3")}");
            if (why.Count > 0) { problems++; Fail($"2 baseline: {name}: {string.Join("; ", why)}"); }
        }
        var gone = baseline.Keys.Where(n => !effects.ContainsKey(n)).ToList();
        if (gone.Count > 0) Warn($"2 baseline: in the baseline but not in Effects: {string.Join(", ", gone)}");
        if (problems == 0) Ok($"2 baseline: {effects.Count} effects within 1.5 LU, 2% length and 0.02 peak of {Path.GetFileName(path)}");
    }

    // ---- Row 3 -------------------------------------------------------------------------------------------------------

    private static void CheckFamilies(Dictionary<string, double> m)
    {
        var rules = new (string what, string[] names, Func<bool> ok)[]
        {
            ("click, hover, error <= -14", new[] { "click", "hover", "error" }, () => m["click"] <= -14 && m["hover"] <= -14 && m["error"] <= -14),
            ("hover <= click - 6", new[] { "hover", "click" }, () => m["hover"] <= m["click"] - 6),
            ("summon_big >= summon + 3", new[] { "summon_big", "summon" }, () => m["summon_big"] >= m["summon"] + 3),
            ("damage_big >= damage + 1.5", new[] { "damage_big", "damage" }, () => m["damage_big"] >= m["damage"] + 1.5),
            ("phase <= -22", new[] { "phase" }, () => m["phase"] <= -22),
            ("lp_tick <= -30", new[] { "lp_tick" }, () => m["lp_tick"] <= -30),
            ("set <= place + 1", new[] { "set", "place" }, () => m["set"] <= m["place"] + 1),
            ("chain_resolve <= activate - 6", new[] { "chain_resolve", "activate" }, () => m["chain_resolve"] <= m["activate"] - 6),
            ("coin_spin <= -18", new[] { "coin_spin" }, () => m["coin_spin"] <= -18),
            ("banish within destroy +-3", new[] { "banish", "destroy" }, () => Math.Abs(m["banish"] - m["destroy"]) <= 3),
            ("draw_game -18 .. -12", new[] { "draw_game" }, () => m["draw_game"] >= -18 && m["draw_game"] <= -12),
        };
        foreach (var (what, names, ok) in rules)
        {
            var missing = names.Where(n => !m.ContainsKey(n)).ToList();
            if (missing.Count > 0) { Fail($"3 family {what}: no effect {string.Join(", ", missing)}"); continue; }
            string values = string.Join(", ", names.Distinct().Select(n => $"{n} {F(m[n])}"));
            Check(ok(), $"3 family {what} ({values})");
        }
    }

    // ---- Row 4 -------------------------------------------------------------------------------------------------------

    private static float[] SumLayers(ThemeMusic music, params string[] names)
    {
        var s = new float[music.Layers["bed"].Length];
        foreach (var n in names)
        {
            var l = music.Layers[n];
            for (int i = 0; i < s.Length && i < l.Length; i++) s[i] += l[i];
        }
        return s;
    }

    /// <summary>The layer sets the game can play at full (Sounds.LayerTargets): bed and drums, the tune or not, then
    /// nothing, drive and dread (you're low) or drive and hope (they are); or the lethal heartbeat alone.</summary>
    private static readonly string[][] Allowed =
    {
        new[] { "bed", "drums" }, new[] { "bed", "drums", "drive", "dread" }, new[] { "bed", "drums", "drive", "hope" },
        new[] { "bed", "drums", "lead" }, new[] { "bed", "drums", "lead", "drive", "dread" }, new[] { "bed", "drums", "lead", "drive", "hope" },
        new[] { "lethal" },
    };

    private static Dictionary<string, ThemeMusic> CheckThemes()
    {
        var rendered = new Dictionary<string, ThemeMusic>();
        var withLead = new List<double>();
        var withoutLead = new List<double>();
        double worstPeak = 0;
        int problems = 0;
        foreach (var theme in Sfx.Themes.Values)
        {
            ThemeMusic music;
            try { music = Sfx.Render(theme); }
            catch (Exception e) { problems++; Fail($"4 theme {theme.Name} doesn't render: {e.Message}"); continue; }
            rendered[theme.Name] = music;
            var why = new List<string>();
            var names = Sfx.LayerNames.Where(n => !music.Layers.ContainsKey(n)).ToList();
            if (names.Count > 0) { problems++; Fail($"4 theme {theme.Name}: no layer {string.Join(", ", names)}"); continue; }
            int length = music.Layers["bed"].Length;
            if (music.Layers.Values.Any(s => s.Length != length)) why.Add("layers of different lengths");
            foreach (var (layer, s) in music.Layers)
            {
                if (s.Any(x => float.IsNaN(x) || float.IsInfinity(x))) why.Add($"{layer} has NaN");
                // SoundExport's seam rule: the step where the loop wraps is no bigger than the largest step inside it.
                float seam = Math.Abs(s[0] - s[^1]), step = 0;
                for (int i = 1; i < s.Length; i++) step = Math.Max(step, Math.Abs(s[i] - s[i - 1]));
                if (seam > step + 1e-6) why.Add($"{layer} seam {F(seam, "F4")} > {F(step, "F4")}");
            }
            double seconds = length / (double)Sfx.Rate;
            if (seconds < 20 || seconds > 60) why.Add($"loop {F(seconds)} s outside 20-60");
            double lead = Loudness.Integrated(SumLayers(music, "bed", "drums", "lead"), Sfx.Rate);
            double bedDrums = Loudness.Integrated(SumLayers(music, "bed", "drums"), Sfx.Rate);
            withLead.Add(lead);
            withoutLead.Add(bedDrums);
            if (!(lead >= -24.0 && lead <= -19.5)) why.Add($"bed+drums+lead {F(lead)} LUFS outside -24.0 .. -19.5");
            if (!(bedDrums >= -27.0 && bedDrums <= -22.5)) why.Add($"bed+drums {F(bedDrums)} LUFS outside -27.0 .. -22.5");
            foreach (var set in Allowed)
            {
                float peak = SumLayers(music, set).Max(x => Math.Abs(x));
                worstPeak = Math.Max(worstPeak, peak);
                if (!(peak < 0.95f)) why.Add($"{string.Join("+", set)} peak {F(peak, "F3")} >= 0.95");
            }
            if (why.Count > 0) { problems++; Fail($"4 theme {theme.Name}: {string.Join("; ", why)}"); }
        }
        if (problems == 0)
            Ok($"4 themes: {rendered.Count} render, seven layers each the same length, no seam jumps, loops "
               + $"{F(rendered.Values.Min(t => t.Layers["bed"].Length) / (double)Sfx.Rate)}-{F(rendered.Values.Max(t => t.Layers["bed"].Length) / (double)Sfx.Rate)} s, "
               + $"bed+drums+lead {F(withLead.Min())} .. {F(withLead.Max())} LUFS, bed+drums {F(withoutLead.Min())} .. {F(withoutLead.Max())}, "
               + $"loudest allowed mix {F(worstPeak, "F3")}");

        // The guests' borrowed themes are their sources moved, sample for sample (section G).
        foreach (var (copy, source, shift) in new[] { ("ilse", "nocturne", 3), ("vera", "rhea", -2) })
        {
            if (!rendered.TryGetValue(copy, out var c) || !rendered.ContainsKey(source)) { Fail($"4 theme {copy}: missing (or {source} is)"); continue; }
            var moved = Sfx.Render(Sfx.Themes[source], shift);
            bool same = Sfx.LayerNames.All(n => c.Layers[n].AsSpan().SequenceEqual(moved.Layers[n]))
                        && c.Sting.AsSpan().SequenceEqual(moved.Sting) && c.Victory.AsSpan().SequenceEqual(moved.Victory) && c.Defeat.AsSpan().SequenceEqual(moved.Defeat);
            Check(same && c.Theme.Key == Mod(Sfx.Themes[source].Key + shift, 12),
                $"4 theme {copy} is {source} {shift:+0;-0} (key {c.Theme.Key}), identical to rendering {source} moved {shift:+0;-0}");
        }

        var menu = Sfx.Music["menu"]();
        double menuLufs = Loudness.Integrated(menu, Sfx.Rate);
        Check(menuLufs >= -22.5 && menuLufs <= -20.0, $"4 menu loop integrated {F(menuLufs)} LUFS (-22.5 .. -20.0)");
        return rendered;
    }

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    /// <summary>Row 4's theme list: every theme named in the baseline must still exist; a new one warns until the
    /// baseline is rewritten.</summary>
    private static void CheckThemeList(string path)
    {
        if (!File.Exists(path)) { Fail($"4 theme list: {path} is missing (write it with --write-baseline)"); return; }
        var listed = File.ReadAllLines(path).Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[0] == "theme").Select(p => p[1]).ToList();
        if (listed.Count == 0) { Fail("4 theme list: the baseline lists no themes (rewrite it with --write-baseline)"); return; }
        var missing = listed.Where(n => !Sfx.Themes.ContainsKey(n)).ToList();
        var added = Sfx.Themes.Keys.Where(n => !listed.Contains(n)).ToList();
        if (missing.Count > 0) Fail($"4 theme list: in the baseline but not in Sfx.Themes: {string.Join(", ", missing)}");
        if (added.Count > 0) Warn($"4 theme list: new since the baseline (rewrite it with --write-baseline): {string.Join(", ", added)}");
        if (missing.Count == 0 && added.Count == 0) Ok($"4 theme list: the baseline's {listed.Count} themes, no more, no fewer");
    }

    // ---- Row 6 -------------------------------------------------------------------------------------------------------

    private static void CheckTuning()
    {
        var missing = Sfx.TunedRoots.Keys.Where(n => !Sfx.Effects.ContainsKey(n)).ToList();
        var badRoots = Sfx.TunedRoots.Where(p => p.Value < 0 || p.Value > 11).Select(p => p.Key).ToList();
        Check(missing.Count == 0 && badRoots.Count == 0,
            $"6 tuning: {Sfx.TunedRoots.Count} tuned roots, " + (missing.Count == 0 ? "all in Effects" : $"not in Effects: {string.Join(", ", missing)}")
            + (badRoots.Count == 0 ? ", roots 0-11" : $", roots out of 0-11: {string.Join(", ", badRoots)}"));
        var ratios = new (int from, int to, double want)[]
        {
            (4, 4, 1), (0, 4, Math.Pow(2, 4 / 12.0)), (0, 9, Math.Pow(2, -3 / 12.0)), (0, 6, Math.Pow(2, -6 / 12.0)),
        };
        var wrong = ratios.Where(r => Math.Abs(Sfx.KeyRatio(r.from, r.to) - r.want) > 1e-12).ToList();
        Check(wrong.Count == 0, "6 tuning: KeyRatio(4,4) = 1, (0,4) = 2^(4/12), (0,9) = 2^(-3/12), (0,6) = 2^(-6/12)"
            + (wrong.Count == 0 ? "" : ": got " + string.Join(", ", wrong.Select(r => $"({r.from},{r.to}) {F(Sfx.KeyRatio(r.from, r.to), "F4")}"))));
        Check(Sfx.ChainSteps.SequenceEqual(new[] { 0, 2, 7, 12 }), $"6 tuning: ChainSteps {string.Join(", ", Sfx.ChainSteps)} (0, 2, 7, 12)");
        Check(Sfx.MenuKey == 9, $"6 tuning: MenuKey {Sfx.MenuKey} (9, A minor)");
    }

    // ---- Row 7 -------------------------------------------------------------------------------------------------------

    /// <summary>The review's pile-ups (PRODUCTION-REVIEW-AUDIO section 7), effects at 1.0: (name, start s, volume).</summary>
    private static readonly (string what, (string name, double at, double gain)[] parts)[] PileUps =
    {
        ("final blow", new[] { ("damage_big", 0.0, 1.0), ("impact", 0.0, 1.0), ("win", 0.2, 1.0) }),
        ("ace lands", new[] { ("summon_big", 0.0, 1.0), ("activate", 0.1, 1.0) }),
        ("battle", new[] { ("impact", 0.0, 1.0), ("destroy", 0.05, 0.9), ("damage_big", 0.1, 1.0) }),
        ("five destroys", Enumerable.Range(0, 5).Select(i => ("destroy", i * 0.06, 0.9 * Math.Max(0.4, Math.Pow(0.8, i)))).ToArray()),
    };

    /// <summary>A pile-up over the everyday theme's bed, drums and lead at 1.0 from bar 3, mono at 44.1 kHz: 0.25 s of music,
    /// the pile-up, then 0.6 s more.</summary>
    private static float[] PileUp((string name, double at, double gain)[] parts, Dictionary<string, float[]> effects, ThemeMusic duel)
    {
        const double lead = 0.25;
        int rate = Sfx.Rate;
        int n = parts.Max(p => (int)((lead + p.at) * rate) + effects[p.name].Length) + (int)(0.6 * rate);
        var s = new float[n];
        var music = SumLayers(duel, "bed", "drums", "lead");
        int from = (int)(2 * duel.BarSeconds * rate);
        for (int i = 0; i < n; i++) s[i] = music[(from + i) % music.Length];
        foreach (var (name, at, gain) in parts)
        {
            var e = effects[name];
            int start = (int)((lead + at) * rate);
            for (int i = 0; i < e.Length; i++) s[start + i] += (float)(e[i] * gain);
        }
        return s;
    }

    /// <summary>Linear resampling from 44.1 kHz (as Unity plays a 44.1 kHz clip on a 48 kHz output).</summary>
    private static float[] Resample(float[] s, int to)
    {
        if (to == Sfx.Rate) return (float[])s.Clone();
        int n = (int)((long)s.Length * to / Sfx.Rate);
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            double x = (double)i * Sfx.Rate / to;
            int k = (int)x;
            double f = x - k;
            o[i] = (float)(s[k] * (1 - f) + (k + 1 < s.Length ? s[k + 1] : 0) * f);
        }
        return o;
    }

    private static float[] Stereo(float[] mono)
    {
        var o = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) o[2 * i] = o[2 * i + 1] = mono[i];
        return o;
    }

    /// <summary>Runs interleaved stereo through a limiter in the game's 1,024-frame buffers.</summary>
    private static void Buffered(Limiter limiter, float[] data)
    {
        const int frames = 1024;
        for (int at = 0; at < data.Length; at += frames * 2)
            limiter.Process(data, at, Math.Min(frames * 2, data.Length - at), 2);
    }

    private static void CheckLimiter(Dictionary<string, float[]> effects, ThemeMusic duel)
    {
        const float threshold = 0.89f;
        foreach (var rate in new[] { 44100, 48000 })
        {
            var probe = new Limiter(rate, 2);
            int latency = probe.LatencyFrames;
            Check(latency == (int)Math.Round(0.002 * rate), $"7 limiter {rate} Hz: lookahead {latency} frames (round(0.002 x rate))");
            foreach (var (what, parts) in PileUps)
            {
                var mono = Resample(PileUp(parts, effects, duel), rate);
                var input = Stereo(mono);
                float inPeak = input.Max(x => Math.Abs(x));

                // The game's path: whole buffers.
                var limiter = new Limiter(rate, 2);
                var output = (float[])input.Clone();
                Buffered(limiter, output);

                // The same, a frame at a time, reading the gain each frame applies (MinGain over one frame).
                var stepper = new Limiter(rate, 2);
                var stepped = (float[])input.Clone();
                var gains = new float[mono.Length];
                for (int f = 0; f < mono.Length; f++)
                {
                    stepper.ResetMeters();
                    stepper.Process(stepped, 2 * f, 2, 2);
                    gains[f] = stepper.MinGain;
                }
                int last = Array.FindLastIndex(mono, x => Math.Abs(x) > threshold);
                // 350 ms, not the spec's 300: with the 120 ms release, the final blow's gain climbs 0.51 -> 0.98 in 314-317 ms (owner's coordinator, 2026-11-20).
                int window = (int)Math.Round(0.35 * rate);
                string recovery;
                bool recovered;
                if (last < 0) { recovered = true; recovery = "never over 0.89"; }
                else
                {
                    int back = -1;
                    for (int f = last; f < gains.Length; f++) if (gains[f] >= 0.98f) { back = f; break; }
                    recovered = back >= 0 && back - last <= window;
                    recovery = back < 0 ? "gain never back to 0.98" : $"gain back to 0.98 {F((back - last) * 1000.0 / rate, "F0")} ms after the last input over 0.89";
                }
                bool sameEitherWay = output.AsSpan().SequenceEqual(stepped);
                // The output measured here, not only by the limiter's own meters (which must agree).
                float outPeak = output.Max(x => Math.Abs(x));
                var why = new List<string>();
                if (what == "final blow" && !(inPeak >= 1.0f)) why.Add($"input peak {F(inPeak, "F2")} < 1.0 (the pile-up should overshoot)");
                if (!(outPeak <= 0.90f)) why.Add($"output peak {F(outPeak, "F3")} > 0.90");
                if (limiter.OutputPeak != outPeak || limiter.InputPeak != inPeak) why.Add($"meters (in {F(limiter.InputPeak, "F3")}, out {F(limiter.OutputPeak, "F3")}) disagree with the samples");
                if (limiter.Frames != mono.Length) why.Add($"{limiter.Frames} frames counted, not {mono.Length}");
                if (limiter.Clamped != 0) why.Add($"{limiter.Clamped} samples clamped");
                if (!recovered) why.Add("gain back to 0.98 within 350 ms");
                if (!sameEitherWay) why.Add("buffered and frame-at-a-time outputs differ");
                Check(why.Count == 0, $"7 limiter {rate} Hz {what}: in {F(inPeak, "F2")} out {F(outPeak, "F3")}, min gain {F(limiter.MinGain, "F3")}, "
                                      + $"clamped {limiter.Clamped}, {recovery}" + (why.Count > 0 ? $" (wanted: {string.Join("; ", why)})" : ""));
            }

            // Normal levels pass untouched: a -20 dBFS 1 kHz sine comes out as itself, L frames late.
            {
                int n = rate;
                var sine = new float[n * 2];
                for (int i = 0; i < n; i++) sine[2 * i] = sine[2 * i + 1] = (float)(0.1 * Math.Sin(2 * Math.PI * 1000 * i / rate));
                var limiter = new Limiter(rate, 2);
                var output = (float[])sine.Clone();
                Buffered(limiter, output);
                double worst = 0;
                for (int i = 0; i < output.Length; i++)
                {
                    int src = i - 2 * latency;
                    worst = Math.Max(worst, Math.Abs(output[i] - (src >= 0 ? sine[src] : 0)));
                }
                Check(worst <= 1e-5 && limiter.MinGain >= 1f, $"7 limiter {rate} Hz: a -20 dBFS 1 kHz sine comes out {latency} frames late, off by at most {worst:E1} (1e-5), min gain {F(limiter.MinGain, "F4")}");
            }

            // Far worse than the pile-ups (16 voices near full and the music at 100%): a burst out of the music that starts
            // at once at peak 5, 8 and 16 still comes out at 0.90 or under, and the safety clamp never acts.
            foreach (var target in new[] { 5.0, 8.0, 16.0 })
            {
                int start = (int)(0.25 * Sfx.Rate), length = (int)(0.05 * Sfx.Rate);
                var music = SumLayers(duel, "bed", "drums", "lead");
                var burst = new float[start + length + Sfx.Rate / 2];
                int from = (int)(2 * duel.BarSeconds * Sfx.Rate);
                for (int i = 0; i < burst.Length; i++) burst[i] = music[(from + i) % music.Length];
                for (int i = 0; i < length; i++) burst[start + i] += (float)((target - 1) * Math.Sign(Math.Sin(2 * Math.PI * 1000 * (i + 0.5) / Sfx.Rate)));
                var mono = Resample(burst, rate);
                float scale = (float)(target / mono.Max(x => Math.Abs(x)));
                for (int i = 0; i < mono.Length; i++) mono[i] *= scale;
                var data = Stereo(mono);
                float inPeak = data.Max(x => Math.Abs(x));
                var limiter = new Limiter(rate, 2);
                Buffered(limiter, data);
                float outPeak = data.Max(x => Math.Abs(x));
                Check(outPeak <= 0.90f && limiter.Clamped == 0,
                    $"7 limiter {rate} Hz burst at peak {F(inPeak, "F1")}: out {F(outPeak, "F3")} (<= 0.90), clamped {limiter.Clamped} (0), min gain {F(limiter.MinGain, "F3")}");
            }

            // The menu loop at 1.0 isn't touched.
            {
                var menu = Stereo(Resample(Sfx.Music["menu"](), rate));
                var limiter = new Limiter(rate, 2);
                Buffered(limiter, menu);
                Check(limiter.MinGain >= 0.999f, $"7 limiter {rate} Hz: the menu loop at 1.0, min gain {F(limiter.MinGain, "F4")} (>= 0.999), peak in {F(limiter.InputPeak, "F2")}");
            }
        }

        // No allocation on the audio thread's path: 1,000 calls on 1,024-frame buffers.
        {
            var limiter = new Limiter(48000, 2);
            var buffer = new float[2048];
            var rng = new Random(7);
            for (int i = 0; i < buffer.Length; i++) buffer[i] = (float)(rng.NextDouble() * 2.4 - 1.2);
            for (int i = 0; i < 20; i++) limiter.Process(buffer, 2); // warm up (JIT)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) limiter.Process(buffer, 2);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0, $"7 limiter: 1,000 calls on 1,024-frame buffers allocate {bytes} bytes (0)");
        }
    }

    // ---- Row 8 -------------------------------------------------------------------------------------------------------

    private static void CheckWarnings(Dictionary<string, float[]> effects)
    {
        int rate = Sfx.Rate;
        var heavy = new[] { "summon_big", "damage_big", "impact", "damage", "destroy", "place" };
        var low = heavy.Where(effects.ContainsKey).ToDictionary(n => n, n => (full: Loudness.MomentaryMax(effects[n], rate),
            above: Loudness.MomentaryMax(Loudness.HighPass(effects[n], 150, rate), rate)));
        string table = string.Join(", ", low.Select(p => $"{p.Key} {F(p.Value.full)} -> {F(p.Value.above)}"));
        bool bigAbove = low["damage_big"].above > low["damage"].above;
        if (bigAbove) Ok($"8 small speakers (150 Hz high-pass): {table}");
        else Warn($"8 small speakers (150 Hz high-pass): damage_big {F(low["damage_big"].above)} isn't above damage {F(low["damage"].above)} ({table})");

        // The strongest alias of a naive square: its odd harmonics above Nyquist, folded back, against the note.
        foreach (var (name, start, length, f0) in new[] { ("dp", 0.12, 0.12, 2093.0), ("lp_tick", 0.0, 0.02, 1500.0), ("click", 0.0, 0.01, 1200.0) })
        {
            if (!effects.TryGetValue(name, out var s)) continue;
            int a = (int)(start * rate), n = Math.Min((int)(length * rate), s.Length - a);
            double reference = Loudness.Goertzel(s, a, n, f0, rate);
            var aliases = new List<(double freq, int harmonic, double db)>();
            for (int h = 1; h < 30; h += 2)
            {
                double f = f0 * h;
                if (f < rate / 2.0) continue;
                double folded = f % rate;
                if (folded > rate / 2.0) folded = rate - folded;
                aliases.Add((folded, h, 10 * Math.Log10(Math.Max(1e-20, Loudness.Goertzel(s, a, n, folded, rate)) / Math.Max(1e-20, reference))));
            }
            var worst = aliases.OrderByDescending(x => x.db).First();
            string what = $"8 alias {name}: strongest {F(worst.freq, "F0")} Hz (harmonic {worst.harmonic}) at {F(worst.db, "F0")} dB against the note";
            if (worst.db < -50) Ok(what + " (-50)"); else Warn(what + " (pass 2's target: under -50 dB)");
        }
    }

    // ---- Paths -------------------------------------------------------------------------------------------------------

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "vendor"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the project root (folder containing vendor/)");
    }
}
