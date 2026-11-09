using System.Diagnostics;
using Ygo.Engine.Audio;

namespace Ygo.Harness;

public static class SoundExport
{
    // `--export-sounds <folder>` (or `--sound-export`): every synthesized sound effect and music loop as a WAV, plus tour.wav (all effects in a
    // row), with each one's length and loudness. Then each layered theme (themes/<name>/<layer>.wav and its one-shots)
    // and themes/demo-<name>.wav, a whole boss duel in a couple of minutes (themes/demo-guide.txt has the timings).
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var tour = new List<float>();
        var gap = new float[(int)(0.45 * Sfx.Rate)];
        foreach (var (name, make) in Sfx.Effects.Concat(Sfx.Music))
        {
            var timer = Stopwatch.StartNew();
            var samples = make();
            Sfx.WriteWav(Path.Combine(folder, name + ".wav"), samples);
            double peak = samples.Max(x => Math.Abs(x)), rms = Math.Sqrt(samples.Average(x => (double)x * x));
            Console.WriteLine($"  {name,-12} {samples.Length / (double)Sfx.Rate,5:F2} s  peak {peak:F2}  rms {rms:F3}  ({timer.ElapsedMilliseconds} ms)");
            if (Sfx.Effects.ContainsKey(name)) { tour.AddRange(samples); tour.AddRange(gap); }
        }
        Sfx.WriteWav(Path.Combine(folder, "tour.wav"), tour.ToArray());
        Console.WriteLine($"Wrote {Sfx.Effects.Count + Sfx.Music.Count} sounds and tour.wav to {folder}");
        int themes = ExportThemes(Path.Combine(folder, "themes"));
        return Math.Max(themes, ExportAlley(Path.Combine(folder, "alley")));
    }

    // The Back Alley's sound (ALLEY-SPEC section 13): the rain bed (Sfx.RainBed) on its own and looped three times (to hear
    // the seam), and alley-night.wav, the alley's everyday theme with the bed under it at 0.35 of the music, as the Back Alley
    // tab plays them. Its themes (alley, orvel, nyx) are in themes/ with the rest. Checks the bed: its length, a peak of 0.6,
    // and no jump where it loops.
    private static int ExportAlley(string folder)
    {
        Directory.CreateDirectory(folder);
        var timer = Stopwatch.StartNew();
        var bed = Sfx.RainBed();
        long ms = timer.ElapsedMilliseconds;
        Sfx.WriteWav(Path.Combine(folder, "rain-bed.wav"), bed);
        Sfx.WriteWav(Path.Combine(folder, "rain-bed-x3.wav"), bed.Concat(bed).Concat(bed).ToArray());
        float peak = bed.Max(x => Math.Abs(x)), seam = Math.Abs(bed[0] - bed[^1]);
        // The seam's step against the bed's own steps: no bigger than 99.9% of them (a click would stand out above the rain).
        var steps = Enumerable.Range(1, bed.Length - 1).Select(i => Math.Abs(bed[i] - bed[i - 1])).OrderBy(x => x).ToArray();
        float typical = steps[(int)(0.999 * (steps.Length - 1))];
        double rms = Math.Sqrt(bed.Average(x => (double)x * x));
        bool lengthOk = bed.Length == (int)Math.Round(Sfx.RainBedSeconds * Sfx.Rate), peakOk = Math.Abs(peak - 0.6) < 0.01, seamOk = seam <= typical + 1e-6;
        int problems = (lengthOk ? 0 : 1) + (peakOk ? 0 : 1) + (seamOk ? 0 : 1);
        Console.WriteLine($"  rain bed     {bed.Length / (double)Sfx.Rate,5:F1} s  peak {peak:F2}  rms {rms:F3}  seam {seam:F4} (99.9% of steps under {typical:F4}, largest {steps[^1]:F4})  ({ms} ms)"
                          + (problems > 0 ? $"  (!) {(lengthOk ? "" : "length ")}{(peakOk ? "" : "peak ")}{(seamOk ? "" : "seam")}" : ""));

        // The alley at night: the alley theme's bed, drums and lead for 32 bars, the rain under it at 0.35.
        var music = Sfx.Render(Sfx.Themes[Ygo.Engine.Campaign.BossMusic.Alley]);
        var layers = new[] { "bed", "drums", "lead" }.Where(music.Layers.ContainsKey).Select(name => music.Layers[name]).ToArray();
        int loop = music.Layers["bed"].Length, n = (int)(32 * music.BarSeconds * Sfx.Rate);
        var night = new float[n];
        for (int i = 0; i < n; i++)
        {
            double x = 0.35 * bed[i % bed.Length];
            foreach (var l in layers) x += l[i % loop];
            night[i] = (float)Math.Max(-1, Math.Min(1, x));
        }
        Sfx.WriteWav(Path.Combine(folder, "alley-night.wav"), night);
        Console.WriteLine($"Wrote the rain bed and alley-night.wav ({n / (double)Sfx.Rate:F0} s) to {folder}" + (problems > 0 ? $"; {problems} problems with the bed (!)" : ""));
        return problems > 0 ? 1 : 0;
    }

    // `--boss-music`: every boss duel and the theme (and key) it plays, each rendered once to check it builds cleanly.
    public static int Bosses(string root)
    {
        var roster = Ygo.Engine.AI.Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Ygo.Engine.Campaign.Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var guests = Ygo.Engine.Campaign.GuestList.Load(Path.Combine(root, "campaign", "guests.txt"), roster);
        var bosses = Ygo.Engine.Campaign.BossMusic.All(circuits, guests);
        int problems = 0;
        foreach (var (where, who, theme, key) in bosses)
            Console.WriteLine($"  {where,-44} {who,-38} {theme} {(key >= 0 ? "+" : "")}{key}");
        // Every circuit's last match, every Gauntlet match, every Tag Cup final and every guest has a boss theme.
        int expected = circuits.All.Count(c => c.Band != "Gauntlet") + circuits.All.Where(c => c.Band == "Gauntlet").Sum(c => c.Matches.Count)
                       + circuits.TagCups.Count + guests.All.Count;
        if (bosses.Count != expected || bosses.Any(b => b.theme == Ygo.Engine.Campaign.BossMusic.Everyday || !Sfx.Themes.ContainsKey(b.theme)))
        {
            Console.WriteLine($"  (!) {bosses.Count} boss duels with themes, expected {expected}");
            problems++;
        }
        foreach (var (theme, key) in bosses.Select(b => (b.theme, b.transpose)).Distinct())
        {
            var timer = Stopwatch.StartNew();
            var music = Sfx.Render(Sfx.Themes[theme], key);
            float peak = music.Layers.Values.Max(s => s.Max(x => Math.Abs(x)));
            bool ok = peak < 1 && music.Layers.Values.All(s => s.Length == music.Layers["bed"].Length);
            if (!ok) problems++;
            Console.WriteLine($"  {theme} {key:+0;-0;0}: peak {peak:F2} ({timer.ElapsedMilliseconds} ms){(ok ? "" : " (!)")}");
        }
        Console.WriteLine($"{bosses.Count} boss duels, {bosses.Select(b => (b.theme, b.transpose)).Distinct().Count()} theme keys" + (problems > 0 ? $"; {problems} problems" : ", all fine"));
        return problems > 0 ? 1 : 0;
    }

    private static int ExportThemes(string folder)
    {
        Directory.CreateDirectory(folder);
        var guide = new List<string> { "Each demo plays a boss duel's music in order (times are minutes:seconds):" };
        int problems = 0;
        foreach (var theme in Sfx.Themes.Values)
        {
            var timer = Stopwatch.StartNew();
            var music = Sfx.Render(theme);
            long ms = timer.ElapsedMilliseconds;
            var dir = Path.Combine(folder, theme.Name);
            Directory.CreateDirectory(dir);
            foreach (var (layer, samples) in music.Layers) Sfx.WriteWav(Path.Combine(dir, layer + ".wav"), samples);
            Sfx.WriteWav(Path.Combine(dir, "sting.wav"), music.Sting);
            Sfx.WriteWav(Path.Combine(dir, "victory.wav"), music.Victory);
            Sfx.WriteWav(Path.Combine(dir, "defeat.wav"), music.Defeat);

            // Every layer the same length (so they stay in sync), and no jump where the loop wraps.
            int length = music.Layers["bed"].Length;
            var stats = music.Layers.Select(p =>
            {
                var s = p.Value;
                double rms = Math.Sqrt(s.Average(x => (double)x * x));
                float seam = Math.Abs(s[0] - s[^1]), step = 0;
                for (int i = 1; i < s.Length; i++) step = Math.Max(step, Math.Abs(s[i] - s[i - 1]));
                bool ok = s.Length == length && seam <= step + 1e-6 && s.Max(x => Math.Abs(x)) < 1;
                if (!ok) problems++;
                return $"{p.Key} {rms:F3}{(ok ? "" : " (!)")}";
            });
            Console.WriteLine($"  {theme.Name,-12} {theme.Bpm,3} bpm  {music.Bars} bars  {length / (double)Sfx.Rate,5:F1} s  rms: {string.Join(", ", stats)}  ({ms} ms)");

            var (demo, marks) = Demo(music);
            Sfx.WriteWav(Path.Combine(folder, $"demo-{theme.Name}.wav"), demo);
            guide.Add($"\n{theme.Name} ({theme.Bpm} bpm, {demo.Length / (double)Sfx.Rate:F0} s)");
            guide.AddRange(marks.Select(m => $"  {(int)m.at / 60}:{(int)m.at % 60:D2}  {m.what}"));
        }
        File.WriteAllLines(Path.Combine(folder, "demo-guide.txt"), guide);
        Console.WriteLine($"Wrote {Sfx.Themes.Count} themes and their demos to {folder}" + (problems > 0 ? $"; {problems} layers have problems (!)" : ""));
        return problems > 0 ? 1 : 0;
    }

    /// <summary>
    /// A boss duel's music in order, mixed the way the game will play it: the sting, then the layers fading in and out
    /// over a second at each change, then the victory and (after a pause) the defeat ending.
    /// </summary>
    private static (float[] demo, List<(double at, string what)> marks) Demo(ThemeMusic music)
    {
        var names = Sfx.LayerNames;
        // Gains per layer: bed, drums, lead, drive, dread, hope, lethal.
        var sections = new (int bars, double[] gains, string what)[]
        {
            (8, new double[] { 1, 1, 0, 0, 0, 0, 0 }, "duel starts: bed and drums"),
            (8, new double[] { 1, 1, 1, 0, 0, 0, 0 }, "the boss's ace is out: the tune comes in"),
            (8, new double[] { 1, 1, 1, 1, 1, 0, 0 }, "you're at a quarter of your LP or less: drive and dread"),
            (8, new double[] { 1, 1, 1, 1, 0, 1, 0 }, "the boss is low instead: drive and hope"),
            (4, new double[] { 0, 0, 0, 0, 0, 0, 1 }, "an attack that would end the duel: the heartbeat alone"),
        };
        var marks = new List<(double, string)> { (0, "versus screen: the sting") };
        var output = new List<float>(music.Sting);
        int rate = Sfx.Rate, fade = rate, length = music.Layers["bed"].Length;
        int start = Math.Min(music.Sting.Length, 2 * rate), position = 0;
        output.RemoveRange(start, output.Count - start);
        var layers = names.Select(n => music.Layers[n]).ToArray();
        var gains = new double[names.Length];
        var tail = music.Sting.Skip(start).ToArray();
        foreach (var (bars, target, what) in sections)
        {
            marks.Add((output.Count / (double)rate, what));
            var from = (double[])gains.Clone();
            int n = (int)(bars * music.BarSeconds * rate);
            for (int i = 0; i < n; i++, position++)
            {
                double k = Math.Min(1, i / (double)fade), x = i < tail.Length ? tail[i] : 0;
                for (int l = 0; l < layers.Length; l++)
                {
                    gains[l] = from[l] + (target[l] - from[l]) * k;
                    x += layers[l][position % length] * gains[l];
                }
                output.Add((float)x);
            }
            tail = Array.Empty<float>();
        }
        // The winning blow: the layers out over 0.3 s under the victory, then a pause and the defeat for comparison.
        marks.Add((output.Count / (double)rate, "you win: the victory ending"));
        int win = output.Count;
        output.AddRange(new float[music.Victory.Length]);
        for (int i = 0; i < (int)(0.3 * rate); i++, position++)
        {
            double x = 0;
            for (int l = 0; l < layers.Length; l++) x += layers[l][position % length] * gains[l] * (1 - i / (0.3 * rate));
            output[win + i] += (float)x;
        }
        for (int i = 0; i < music.Victory.Length; i++) output[win + i] += music.Victory[i];
        output.AddRange(new float[rate * 3 / 2]);
        marks.Add((output.Count / (double)rate, "(or) you lose: the defeat ending"));
        output.AddRange(music.Defeat);
        return (output.Select(x => Math.Max(-1f, Math.Min(1f, x))).ToArray(), marks);
    }
}
