namespace VisualizerPro.Core;

internal sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
{
    private double z1, z2;
    public double Process(double x)
    {
        double y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y;
        return y;
    }
    public static Biquad Shelf(int rate)
    {
        double k = Math.Tan(Math.PI * 1681.974450955533 / rate), vh = Math.Pow(10, 3.999843853973347 / 20), vb = Math.Pow(vh, .4996667741545416);
        double q = .7071752369554196, a = 1 + k / q + k * k;
        return new((vh + vb * k / q + k * k) / a, 2 * (k * k - vh) / a, (vh - vb * k / q + k * k) / a,
            2 * (k * k - 1) / a, (1 - k / q + k * k) / a);
    }
    public static Biquad HighPass(int rate)
    {
        double k = Math.Tan(Math.PI * 38.13547087602444 / rate), q = .5003270373238773, a = 1 + k / q + k * k;
        // BS.1770 RLB stage deliberately has unity numerator (not RBJ-normalized).
        return new(1, -2, 1, 2 * (k * k - 1) / a, (1 - k / q + k * k) / a);
    }
}

/// <summary>Stereo BS.1770 K-weighting, 400 ms / 100 ms overlapping blocks, dual gating.
/// Fixed 0.01 LU histogram keeps integration memory bounded for arbitrarily long sessions.</summary>
public sealed class LoudnessMeter
{
    private readonly Biquad shelfL, shelfR, highL, highR;
    private readonly int hop;
    private readonly double[] energy = new double[30], sums = new double[16001];
    private readonly long[] counts = new long[16001];
    private int samples, blocks, write;
    private double current, totalEnergy;
    private long gatedCount;
    public double Momentary { get; private set; } = double.NegativeInfinity;
    public double ShortTerm { get; private set; } = double.NegativeInfinity;
    public double Integrated
    {
        get
        {
            if (gatedCount == 0) return double.NegativeInfinity;
            double relativeGate = Math.Max(-70, ToLufs(totalEnergy / gatedCount) - 10), sum = 0;
            long count = 0;
            int first = Math.Clamp((int)Math.Ceiling((relativeGate + 140) * 100), 0, counts.Length - 1);
            for (int i = first; i < counts.Length; i++) { sum += sums[i]; count += counts[i]; }
            return count == 0 ? double.NegativeInfinity : ToLufs(sum / count);
        }
    }
    public LoudnessMeter(int sampleRate)
    {
        hop = sampleRate / 10;
        shelfL = Biquad.Shelf(sampleRate); shelfR = Biquad.Shelf(sampleRate);
        highL = Biquad.HighPass(sampleRate); highR = Biquad.HighPass(sampleRate);
    }
    public void Add(double left, double right)
    {
        double l = highL.Process(shelfL.Process(left)), r = highR.Process(shelfR.Process(right));
        current += l * l + r * r;
        if (++samples < hop) return;
        energy[write] = current / samples; write = (write + 1) % 30; blocks++;
        samples = 0; current = 0;
        if (blocks >= 4)
        {
            double e = 0;
            for (int i = 1; i <= 4; i++) e += energy[(write - i + 30) % 30];
            e /= 4; Momentary = ToLufs(e);
            if (Momentary >= -70)
            {
                int bin = Math.Clamp((int)Math.Round((Momentary + 140) * 100), 0, counts.Length - 1);
                counts[bin]++; sums[bin] += e; gatedCount++; totalEnergy += e;
            }
        }
        if (blocks >= 30) ShortTerm = ToLufs(energy.Sum() / 30);
    }
    public static double ToLufs(double weightedEnergy) => weightedEnergy > 1e-20 ? -.691 + 10 * Math.Log10(weightedEnergy) : double.NegativeInfinity;
}

/// <summary>Four-phase, 48 taps per phase windowed-sinc reconstruction; persists across audio blocks.</summary>
public sealed class TruePeakMeter
{
    private const int Taps = 48;
    private readonly double[][] kernels = new double[3][];
    private readonly double[] history = new double[Taps];
    private int position;
    public double Maximum { get; private set; }
    public void ResetMaximum() => Maximum = 0;
    public void Finish() { for (int i = 0; i < 24; i++) Add(0); }
    public TruePeakMeter()
    {
        for (int phase = 1; phase <= 3; phase++)
        {
            var kernel = new double[Taps];
            for (int k = 0; k < Taps; k++)
            {
                double x = k - 23 + phase / 4.0;
                double window = .42 - .5 * Math.Cos(2 * Math.PI * k / (Taps - 1)) + .08 * Math.Cos(4 * Math.PI * k / (Taps - 1));
                kernel[k] = Math.Sin(Math.PI * x) / (Math.PI * x) * window;
            }
            double sum = kernel.Sum();
            for (int k = 0; k < Taps; k++) kernel[k] /= sum;
            kernels[phase - 1] = kernel;
        }
    }
    public void Add(double sample)
    {
        history[position] = sample; Maximum = Math.Max(Maximum, Math.Abs(sample));
        foreach (var kernel in kernels)
        {
            double output = 0; int p = position;
            for (int k = 0; k < Taps; k++) { output += history[p] * kernel[k]; if (--p < 0) p = Taps - 1; }
            Maximum = Math.Max(Maximum, Math.Abs(output));
        }
        position = (position + 1) % Taps;
    }
}
