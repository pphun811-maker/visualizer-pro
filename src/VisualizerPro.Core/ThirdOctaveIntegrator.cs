namespace VisualizerPro.Core;

// FFT energy integration, not an IEC 61260 filter-bank conformance claim.
// Base-ten third-octave edges partition bin energy; edge bins are apportioned.
public sealed class ThirdOctaveIntegrator
{
    public readonly record struct Band(double CenterHz, double LowHz, double HighHz);
    private readonly record struct Weight(int Bin, double Fraction);
    private static readonly double[] Nominal = [20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000];
    private readonly int size;
    private readonly Weight[][] weights;
    public Band[] Bands { get; }

    public ThirdOctaveIntegrator(int size, int rate)
    {
        this.size = size;
        var bands = new List<Band>(); var maps = new List<Weight[]>();
        double df = rate / (double)size, edge = Math.Pow(10, .05);
        for (int i = 0; i < Nominal.Length; i++)
        {
            double exact = 1000 * Math.Pow(10, (i - 17) / 10.0), low = exact / edge, high = Math.Min(exact * edge, rate / 2.0);
            if (low >= high) break;
            var list = new List<Weight>();
            int first = Math.Max(1, (int)Math.Ceiling(low / df - .5)), last = Math.Min(size / 2, (int)Math.Floor(high / df + .5));
            for (int k = first; k <= last; k++)
            {
                double binLow = Math.Max(0, (k - .5) * df), binHigh = Math.Min(rate / 2.0, (k + .5) * df);
                double fraction = Math.Max(0, Math.Min(high, binHigh) - Math.Max(low, binLow)) / (binHigh - binLow);
                if (fraction > 0) list.Add(new(k, fraction));
            }
            bands.Add(new(Nominal[i], low, high)); maps.Add(list.ToArray());
        }
        Bands = bands.ToArray(); weights = maps.ToArray();
    }

    public double[] Integrate(double[] realL, double[] imagL, double[] realR, double[] imagR, ChannelView channel, double windowEnergy)
    {
        var result = new double[Bands.Length];
        double normalization = 1 / (size * windowEnergy);
        for (int b = 0; b < result.Length; b++)
        {
            double power = 0;
            foreach (var weight in weights[b])
            {
                int k = weight.Bin;
                double l = realL[k] * realL[k] + imagL[k] * imagL[k], r = realR[k] * realR[k] + imagR[k] * imagR[k];
                double a = realL[k] + (channel == ChannelView.Side ? -realR[k] : realR[k]);
                double c = imagL[k] + (channel == ChannelView.Side ? -imagR[k] : imagR[k]);
                double energy = channel switch { ChannelView.Left => l, ChannelView.Right => r, ChannelView.Mid or ChannelView.Side => .25 * (a * a + c * c), _ => (l + r) / 2 };
                power += energy * weight.Fraction * (k == size / 2 ? 1 : 2);
            }
            result[b] = power * normalization;
        }
        return result;
    }
}
