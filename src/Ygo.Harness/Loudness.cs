namespace Ygo.Harness;

/// <summary>
/// A loudness meter after ITU-R BS.1770 (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md section M), for mono sounds: K-weighting
/// (the pre-filter high shelf and the RLB high-pass, made for any sample rate by the bilinear transform as libebur128
/// does), then the mean square over 400 ms blocks, LUFS = -0.691 + 10 log10(mean square). Checked once against ffmpeg's
/// ebur128 filter (within 0.3 LU).
/// </summary>
public static class Loudness
{
    /// <summary>A second-order IIR section (direct form I, a0 = 1).</summary>
    private readonly struct Biquad
    {
        public readonly double B0, B1, B2, A1, A2;
        public Biquad(double b0, double b1, double b2, double a1, double a2) { B0 = b0; B1 = b1; B2 = b2; A1 = a1; A2 = a2; }

        public double[] Run(double[] x)
        {
            var y = new double[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = B0 * x[i] + B1 * x1 + B2 * x2 - A1 * y1 - A2 * y2;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = v;
                y[i] = v;
            }
            return y;
        }
    }

    /// <summary>Stage 1: the head's high shelf (+4 dB above about 1.7 kHz).</summary>
    private static Biquad Shelf(int rate)
    {
        const double f0 = 1681.974450955533, gain = 3.999843853973347, q = 0.7071752369554196;
        double k = Math.Tan(Math.PI * f0 / rate);
        double vh = Math.Pow(10, gain / 20), vb = Math.Pow(vh, 0.4996667741545416);
        double a0 = 1 + k / q + k * k;
        return new Biquad((vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0,
            2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    /// <summary>Stage 2: the RLB high-pass at about 38 Hz.</summary>
    private static Biquad Rlb(int rate)
    {
        const double f0 = 38.13547087602444, q = 0.5003270373238773;
        double k = Math.Tan(Math.PI * f0 / rate);
        double a0 = 1 + k / q + k * k;
        return new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    /// <summary>A second-order Butterworth high-pass (an imitation of a small speaker's low end, row 8).</summary>
    public static float[] HighPass(float[] s, double cutoff, int rate)
    {
        double k = Math.Tan(Math.PI * cutoff / rate), q = Math.Sqrt(0.5);
        double a0 = 1 + k / q + k * k;
        var hp = new Biquad(1 / a0, -2 / a0, 1 / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        return hp.Run(s.Select(x => (double)x).ToArray()).Select(x => (float)x).ToArray();
    }

    /// <summary>The K-weighted signal, squared.</summary>
    private static double[] WeightedSquares(float[] s, int rate)
    {
        var y = Rlb(rate).Run(Shelf(rate).Run(s.Select(x => (double)x).ToArray()));
        for (int i = 0; i < y.Length; i++) y[i] *= y[i];
        return y;
    }

    private static double Lufs(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(meanSquare);

    /// <summary>
    /// The loudest momentary loudness: 400 ms windows ending every 100 ms over the sound with 0.5 s of silence after it
    /// (before the start counts as silence), as ffmpeg's ebur128 reports them.
    /// </summary>
    public static double MomentaryMax(float[] s, int rate)
    {
        var padded = new float[s.Length + rate / 2];
        Array.Copy(s, padded, s.Length);
        var sq = WeightedSquares(padded, rate);
        var prefix = new double[sq.Length + 1];
        for (int i = 0; i < sq.Length; i++) prefix[i + 1] = prefix[i] + sq[i];
        int window = (int)Math.Round(0.4 * rate), step = (int)Math.Round(0.1 * rate);
        double best = double.NegativeInfinity;
        for (int end = step; end <= sq.Length; end += step)
        {
            int start = Math.Max(0, end - window);
            best = Math.Max(best, Lufs((prefix[end] - prefix[start]) / window));
        }
        return best;
    }

    /// <summary>Integrated loudness: 400 ms blocks with 75% overlap, gated at -70 LUFS and then 10 LU under the gated mean.</summary>
    public static double Integrated(float[] s, int rate)
    {
        var sq = WeightedSquares(s, rate);
        var prefix = new double[sq.Length + 1];
        for (int i = 0; i < sq.Length; i++) prefix[i + 1] = prefix[i] + sq[i];
        int window = (int)Math.Round(0.4 * rate), step = (int)Math.Round(0.1 * rate);
        var blocks = new List<double>();
        for (int start = 0; start + window <= sq.Length; start += step) blocks.Add((prefix[start + window] - prefix[start]) / window);
        var loud = blocks.Where(b => Lufs(b) > -70).ToList();
        if (loud.Count == 0) return double.NegativeInfinity;
        double gate = Lufs(loud.Average()) - 10;
        var gated = loud.Where(b => Lufs(b) > gate).ToList();
        return gated.Count == 0 ? double.NegativeInfinity : Lufs(gated.Average());
    }

    /// <summary>The power of one frequency in a Hann-windowed stretch (Goertzel), for the alias scan (row 8).</summary>
    public static double Goertzel(float[] s, int start, int length, double freq, int rate)
    {
        double k = 2 * Math.Cos(2 * Math.PI * freq / rate), a = 0, b = 0;
        for (int i = 0; i < length && start + i < s.Length; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Math.Max(1, length - 1));
            double x = s[start + i] * w;
            (a, b) = (x + k * a - b, a);
        }
        return a * a + b * b - k * a * b;
    }
}
