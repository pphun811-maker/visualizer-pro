namespace VisualizerPro.Core;

// Immutable instantaneous spectrum for history views. It never uses the FFT
// trace's octave smoothing, attack, release, average or peak hold.
public sealed record SpectrogramSlice(double[] Frequencies, float[] Levels)
{
    public double At(double frequency)
    {
        int hi = Array.BinarySearch(Frequencies, frequency);
        if (hi >= 0) return Levels[hi];
        hi = ~hi;
        if (hi == 0) return Levels[0];
        if (hi >= Levels.Length) return Levels[^1];
        double t = Math.Log(frequency / Frequencies[hi - 1]) / Math.Log(Frequencies[hi] / Frequencies[hi - 1]);
        double a = Math.Pow(10, Levels[hi - 1] / 10), b = Math.Pow(10, Levels[hi] / 10);
        return AudioMath.PowerDb(a + (b - a) * t);
    }
}

internal sealed class SpectrogramAnalyzer
{
    internal const int Bins = 2048;
    private readonly int longSize, shortSize;
    private readonly int factor;
    private readonly int sourceRate;
    private readonly double effectiveRate;
    private readonly float[] ringL, ringR;
    private readonly float[] inputL, inputR;
    private readonly double[] filter;
    private readonly double[] longWindow, shortWindow;
    private readonly double[] realL, imagL, realR, imagR;
    private readonly double[] shortRealL, shortImagL, shortRealR, shortImagR;
    private readonly double[] frequencies = new double[Bins];
    private readonly double longNorm, shortNorm;
    private int inputHead, phase, head;
    private long samples, lastBuild;
    private ChannelView? previousChannel;
    private SpectrogramSlice? latest;

    internal SpectrogramAnalyzer(int sampleRate)
    {
        sourceRate = sampleRate;
        factor = Math.Max(1, sampleRate / 48000); effectiveRate = sampleRate / (double)factor;
        longSize = effectiveRate <= 16000 ? 8192 : effectiveRate <= 32000 ? 16384 : 32768;
        shortSize = longSize / 4;
        ringL = new float[longSize]; ringR = new float[longSize];
        longWindow = Fourier.Window(longSize, WindowKind.Hann); shortWindow = Fourier.Window(shortSize, WindowKind.Hann);
        realL = new double[longSize]; imagL = new double[longSize]; realR = new double[longSize]; imagR = new double[longSize];
        shortRealL = new double[shortSize]; shortImagL = new double[shortSize]; shortRealR = new double[shortSize]; shortImagR = new double[shortSize];
        // A streaming low-pass precedes decimation; simply skipping 192 kHz
        // samples would fold ultrasonic content into the displayed audio band.
        int taps = factor == 1 ? 1 : factor * 32 + 1;
        inputL = new float[taps]; inputR = new float[taps]; filter = new double[taps];
        if (factor == 1) filter[0] = 1;
        else
        {
            double cutoff = .46 / factor, sum = 0;
            for (int i = 0; i < taps; i++)
            {
                double x = i - (taps - 1) / 2.0;
                double sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
                filter[i] = sinc * (.42 - .5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + .08 * Math.Cos(4 * Math.PI * i / (taps - 1)));
                sum += filter[i];
            }
            for (int i = 0; i < taps; i++) filter[i] /= sum;
        }
        longNorm = 2 / longWindow.Sum(); shortNorm = 2 / shortWindow.Sum();
        for (int i = 0; i < Bins; i++) frequencies[i] = 10 * Math.Pow(sampleRate / 20.0, i / (double)(Bins - 1));
    }

    internal void Add(float left, float right)
    {
        if (factor > 1)
        {
            inputL[inputHead] = left; inputR[inputHead] = right;
            inputHead = (inputHead + 1) % filter.Length;
            if (++phase < factor) return;
            phase = 0;
            double l = 0, r = 0; int at = inputHead;
            for (int i = 0; i < filter.Length; i++)
            {
                if (--at < 0) at = filter.Length - 1;
                l += filter[i] * inputL[at]; r += filter[i] * inputR[at];
            }
            left = (float)l; right = (float)r;
        }
        ringL[head] = left; ringR[head] = right; head = (head + 1) & (longSize - 1); samples++;
    }

    internal SpectrogramSlice Read(ChannelView channel, double[] sourceRealL, double[] sourceImagL, double[] sourceRealR, double[] sourceImagR, double sourceNorm)
    {
        if (latest != null && previousChannel == channel && samples - lastBuild < effectiveRate / 20) return latest;
        Transform(longSize, longWindow, realL, imagL, realR, imagR);
        Transform(shortSize, shortWindow, shortRealL, shortImagL, shortRealR, shortImagR);
        var levels = new float[Bins];
        double OriginalPower(int i)
        {
            int size = sourceRealL.Length;
            double low = i == 0 ? 10 : Math.Sqrt(frequencies[i - 1] * frequencies[i]);
            double high = i == Bins - 1 ? sourceRate / 2.0 : Math.Sqrt(frequencies[i] * frequencies[i + 1]);
            int first = Math.Max(1, (int)Math.Ceiling(low * size / sourceRate));
            int last = Math.Min(size / 2, (int)Math.Floor(high * size / sourceRate));
            double peak = Power(frequencies[i], size, sourceNorm, sourceRealL, sourceImagL, sourceRealR, sourceImagR, channel, sourceRate);
            for (int k = first; k <= last; k++)
                peak = Math.Max(peak, Power(k * sourceRate / (double)size, size, sourceNorm, sourceRealL, sourceImagL, sourceRealR, sourceImagR, channel, sourceRate));
            return peak;
        }
        for (int i = 0; i < levels.Length; i++)
        {
            double hz = frequencies[i];
            // Preserve the original input's ultrasonic range as well. The
            // decimated path supplies detail in the human audio band; original
            // FFT coefficients supply frequencies above that band's crossover.
            if (factor > 1 && hz >= 20000)
            {
                levels[i] = (float)AudioMath.PowerDb(OriginalPower(i));
                continue;
            }
            // Long windows resolve bass; short windows retain transient texture
            // above 400 Hz. Blend in power across the fixed 200–400 Hz crossover.
            double low = samples < longSize ? 0 : Power(hz, longSize, longNorm, realL, imagL, realR, imagR, channel);
            double high = samples < shortSize ? 0 : Power(hz, shortSize, shortNorm, shortRealL, shortImagL, shortRealR, shortImagR, channel);
            double mix = Math.Clamp(Math.Log2(hz / 200), 0, 1);
            double power = low + (high - low) * mix;
            if (factor > 1 && hz > 18000)
            {
                double original = OriginalPower(i);
                power += (original - power) * ((hz - 18000) / 2000);
            }
            levels[i] = (float)AudioMath.PowerDb(power);
        }
        latest = new(frequencies, levels); lastBuild = samples; previousChannel = channel;
        return latest;
    }

    private void Transform(int size, double[] window, double[] lr, double[] li, double[] rr, double[] ri)
    {
        for (int i = 0; i < size; i++)
        {
            int at = (head - size + i + longSize) & (longSize - 1);
            lr[i] = ringL[at] * window[i]; rr[i] = ringR[at] * window[i]; li[i] = ri[i] = 0;
        }
        Fourier.Transform(lr, li); Fourier.Transform(rr, ri);
    }

    private double Power(double hz, int size, double norm, double[] lr, double[] li, double[] rr, double[] ri, ChannelView channel, double sampleRate = 0)
    {
        double at = hz * size / (sampleRate > 0 ? sampleRate : effectiveRate);
        int lo = Math.Clamp((int)at, 0, size / 2 - 1); double t = at - lo;
        double Bin(int k)
        {
            double l = lr[k] * lr[k] + li[k] * li[k], r = rr[k] * rr[k] + ri[k] * ri[k];
            double gain = k == size / 2 ? norm * .5 : norm;
            return channel switch
            {
                ChannelView.Left => l, ChannelView.Right => r,
                ChannelView.Mid => .25 * ((lr[k] + rr[k]) * (lr[k] + rr[k]) + (li[k] + ri[k]) * (li[k] + ri[k])),
                ChannelView.Side => .25 * ((lr[k] - rr[k]) * (lr[k] - rr[k]) + (li[k] - ri[k]) * (li[k] - ri[k])),
                _ => (l + r) * .5
            } * gain * gain;
        }
        double a = Bin(lo), b = Bin(lo + 1);
        return a + (b - a) * t;
    }
}
