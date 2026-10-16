namespace VisualizerPro.Core;

public sealed class Analyzer
{
    public const int Bins = 768;
    private readonly int rate;
    private AnalysisSettings settings;
    private readonly float[] ringL = new float[65536], ringR = new float[65536];
    private readonly float[] meterL, meterR;
    private int head, meterHead, meterCount, sinceFrame;
    private long sampleCount, sequence, lastFrameSample;
    private double energyL, energyR, cross, peakL, peakR;
    private readonly LoudnessMeter loudness;
    private readonly TruePeakMeter trueL = new(), trueR = new();
    private double[] window = [], realL = [], imagL = [], realR = [], imagR = [];
    private readonly double[] frequencies = new double[Bins];
    private float[] average = new float[Bins], peaks = new float[Bins], envelope = new float[Bins];
    private readonly float[] envelopeL = new float[Bins], envelopeR = new float[Bins];
    private double windowSum;
    private double windowEnergy;
    private ThirdOctaveIntegrator octaves = null!;
    private double[] octaveAverage = [], octaveEnvelope = [], octavePeaks = [];
    private bool firstFrame = true;
    public DisplaySettings Display { get; set; } = new();
    private readonly double[] bandL = new double[5], bandR = new double[5], bandCross = new double[5];

    public Analyzer(int sampleRate, AnalysisSettings? settings = null)
    {
        if (sampleRate < 8000 || sampleRate > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        rate = sampleRate; this.settings = (settings ?? new()).Sanitize();
        meterL = new float[(int)(rate * .4)]; meterR = new float[meterL.Length];
        loudness = new(rate);
        for (int i = 0; i < Bins; i++) frequencies[i] = 10 * Math.Pow((rate / 2.0) / 10, i / (double)(Bins - 1));
        ConfigureFft();
    }
    public void Configure(AnalysisSettings next)
    {
        next = next.Sanitize();
        bool reset = next.FftSize != settings.FftSize || next.Window != settings.Window || next.Channel != settings.Channel || next.SmoothingOctaves != settings.SmoothingOctaves;
        settings = next;
        if (reset) { ConfigureFft(); firstFrame = true; ResetPeaks(); }
    }
    public void ResetPeaks() { Array.Fill(peaks, -140f); Array.Fill(octavePeaks, -140); trueL.ResetMaximum(); trueR.ResetMaximum(); }
    private void ConfigureFft()
    {
        int n = settings.FftSize;
        window = Fourier.Window(n, settings.Window); windowSum = window.Sum();
        windowEnergy = window.Sum(x => x * x);
        octaves = new(n, rate); octaveAverage = new double[octaves.Bands.Length]; octaveEnvelope = new double[octaves.Bands.Length]; octavePeaks = new double[octaves.Bands.Length];
        realL = new double[n]; imagL = new double[n]; realR = new double[n]; imagR = new double[n];
    }
    public AnalysisFrame? Add(ReadOnlySpan<float> stereo)
    {
        if (stereo.Length % 2 != 0) throw new ArgumentException("Expected interleaved stereo.");
        double decay = Math.Exp(-1.0 / (rate * .35));
        for (int i = 0; i < stereo.Length; i += 2)
        {
            float l = float.IsFinite(stereo[i]) ? stereo[i] : 0, r = float.IsFinite(stereo[i + 1]) ? stereo[i + 1] : 0;
            ringL[head] = l; ringR[head] = r; head = (head + 1) & 65535;
            double oldL = meterL[meterHead], oldR = meterR[meterHead];
            energyL += (double)l * l - oldL * oldL; energyR += (double)r * r - oldR * oldR; cross += (double)l * r - oldL * oldR;
            meterL[meterHead] = l; meterR[meterHead] = r; meterHead = (meterHead + 1) % meterL.Length;
            meterCount = Math.Min(meterCount + 1, meterL.Length);
            peakL = Math.Max(Math.Abs(l), peakL * decay); peakR = Math.Max(Math.Abs(r), peakR * decay);
            loudness.Add(l, r); trueL.Add(l); trueR.Add(r);
            sampleCount++; sinceFrame++;
        }
        if (sinceFrame < rate / 60) return null;
        double dt = (sampleCount - lastFrameSample) / (double)rate; lastFrameSample = sampleCount;
        sinceFrame %= Math.Max(1, rate / 60);
        return Build(dt);
    }
    public AnalysisFrame Finish()
    {
        trueL.Finish(); trueR.Finish();
        double dt = (sampleCount - lastFrameSample) / (double)rate;
        return Build(dt);
    }
    private AnalysisFrame Build(double dt)
    {
        int n = settings.FftSize;
        for (int i = 0; i < n; i++)
        {
            int at = (head - n + i + 65536) & 65535;
            realL[i] = ringL[at] * window[i]; realR[i] = ringR[at] * window[i]; imagL[i] = imagR[i] = 0;
        }
        Fourier.Transform(realL, imagL); Fourier.Transform(realR, imagR);
        var left = new float[Bins]; var right = new float[Bins]; var current = new float[Bins];
        double norm = 2 / windowSum;
        for (int b = 0; b < Bins; b++)
        {
            double low = b == 0 ? 0 : Math.Sqrt(frequencies[b - 1] * frequencies[b]);
            double high = b == Bins - 1 ? rate / 2.0 : Math.Sqrt(frequencies[b] * frequencies[b + 1]);
            int from = Math.Clamp((int)Math.Ceiling(low * n / rate), 1, n / 2), to = Math.Clamp((int)Math.Floor(high * n / rate), from, n / 2);
            if (low * n / rate > to || high * n / rate < from) from = to = Math.Clamp((int)Math.Round(frequencies[b] * n / rate), 1, n / 2);
            double lp = 0, rp = 0, cp = 0;
            for (int k = from; k <= to; k++)
            {
                double le = realL[k] * realL[k] + imagL[k] * imagL[k], re = realR[k] * realR[k] + imagR[k] * imagR[k];
                double ce = settings.Channel switch
                {
                    ChannelView.Left => le, ChannelView.Right => re,
                    ChannelView.Mid => .25 * (Math.Pow(realL[k] + realR[k], 2) + Math.Pow(imagL[k] + imagR[k], 2)),
                    ChannelView.Side => .25 * (Math.Pow(realL[k] - realR[k], 2) + Math.Pow(imagL[k] - imagR[k], 2)),
                    _ => (le + re) / 2
                };
                double scale = k == n / 2 ? norm * .5 : norm;
                lp = Math.Max(lp, le * scale * scale); rp = Math.Max(rp, re * scale * scale); cp = Math.Max(cp, ce * scale * scale);
            }
            left[b] = (float)AudioMath.PowerDb(lp); right[b] = (float)AudioMath.PowerDb(rp); current[b] = (float)AudioMath.PowerDb(cp);
        }
        if (settings.SmoothingOctaves > 0)
        {
            int radius = Math.Max(1, (int)(settings.SmoothingOctaves / 2 / Math.Log2(frequencies[1] / frequencies[0])));
            float[] Smooth(float[] source)
            {
                var result = new float[Bins];
                for (int b = 0; b < Bins; b++)
                {
                    int from = Math.Max(0, b - radius), to = Math.Min(Bins - 1, b + radius); double sum = 0;
                    for (int k = from; k <= to; k++) sum += Math.Pow(10, source[k] / 10);
                    result[b] = (float)AudioMath.PowerDb(sum / (to - from + 1));
                }
                return result;
            }
            current = Smooth(current); left = Smooth(left); right = Smooth(right);
        }
        double avgA = settings.AverageMs <= 0 ? 1 : 1 - Math.Exp(-dt * 1000 / settings.AverageMs);
        double releaseA = settings.ReleaseMs <= 0 ? 1 : 1 - Math.Exp(-dt * 1000 / settings.ReleaseMs);
        // A short attack removes frame-to-frame flutter without using the much
        // slower average trace. Release=0 still provides an instantaneous mode.
        double attackA = settings.ReleaseMs <= 0 || Display.SpectrumAttackMs <= 0 ? 1 : 1 - Math.Exp(-dt * 1000 / Display.SpectrumAttackMs);
        float Follow(float previous, float target) => previous + (float)((target - previous) * (target > previous ? attackA : releaseA));
        for (int b = 0; b < Bins; b++)
        {
            if (firstFrame) { average[b] = envelope[b] = current[b]; peaks[b] = current[b]; envelopeL[b] = left[b]; envelopeR[b] = right[b]; }
            else
            {
                double power = (1 - avgA) * Math.Pow(10, average[b] / 10) + avgA * Math.Pow(10, current[b] / 10);
                average[b] = (float)AudioMath.PowerDb(power);
                envelope[b] = Follow(envelope[b], current[b]);
                envelopeL[b] = Follow(envelopeL[b], left[b]); envelopeR[b] = Follow(envelopeR[b], right[b]);
                peaks[b] = settings.PeakHold ? Math.Max(peaks[b], current[b]) : current[b];
            }
        }
        var octaveReadings = BuildOctaves(avgA, attackA, releaseA);
        firstFrame = false;
        // Consecutive samples keep high-frequency stereo traces from aliasing
        // into a different shape as the capture window advances.
        int span = Math.Min(12800, rate / 30);
        var phaseL = new float[span]; var phaseR = new float[span];
        for (int i = 0; i < span; i++) { int p = (head - span + i + 65536) & 65535; phaseL[i] = ringL[p]; phaseR[i] = ringR[p]; }
        double el = Math.Max(0, energyL), er = Math.Max(0, energyR), count = Math.Max(1, meterCount);
        double rmsLeft = AudioMath.PowerDb(el / count), rmsRight = AudioMath.PowerDb(er / count);
        var meters = new MeterReading(AudioMath.Db(peakL), AudioMath.Db(peakR), rmsLeft, rmsRight,
            AudioMath.Db(trueL.Maximum), AudioMath.Db(trueR.Maximum), loudness.Momentary, loudness.ShortTerm, loudness.Integrated,
            el * er > 1e-18 ? Math.Clamp(cross / Math.Sqrt(el * er), -1, 1) : double.NaN,
            el + er > 1e-18 ? (er - el) / (el + er) : 0,
            AudioMath.PowerDb(Math.Max(0, (el + er + 2 * cross) / (4 * count))),
            AudioMath.PowerDb(Math.Max(0, (el + er - 2 * cross) / (4 * count))),
            el + er > 1e-18 ? Math.Max(AudioMath.Db(peakL), AudioMath.Db(peakR)) - AudioMath.PowerDb((el + er) / (2 * count)) : double.NaN, sampleCount / (double)rate);
        return new(++sequence, sampleCount / (double)rate, rate, settings, frequencies, (float[])envelope.Clone(), (float[])envelopeL.Clone(), (float[])envelopeR.Clone(),
            (float[])average.Clone(), (float[])peaks.Clone(), phaseL, phaseR, meters) { StereoBands = BuildStereoBands(dt, norm), OctaveBands = octaveReadings };
    }

    private OctaveBandReading[] BuildOctaves(double averageA, double attackA, double releaseA)
    {
        var powers = octaves.Integrate(realL, imagL, realR, imagR, settings.Channel, windowEnergy);
        var result = new OctaveBandReading[powers.Length];
        for (int i = 0; i < result.Length; i++)
        {
            double db = AudioMath.PowerDb(powers[i]);
            if (firstFrame) { octaveAverage[i] = powers[i]; octaveEnvelope[i] = octavePeaks[i] = db; }
            else
            {
                octaveAverage[i] += (powers[i] - octaveAverage[i]) * averageA;
                octaveEnvelope[i] += (db - octaveEnvelope[i]) * (db > octaveEnvelope[i] ? attackA : releaseA);
                octavePeaks[i] = settings.PeakHold ? Math.Max(octavePeaks[i], db) : db;
            }
            var band = octaves.Bands[i];
            result[i] = new(band.CenterHz, band.LowHz, band.HighHz, octaveEnvelope[i], AudioMath.PowerDb(octaveAverage[i]), octavePeaks[i]);
        }
        return result;
    }

    private StereoBandReading[] BuildStereoBands(double dt, double norm)
    {
        double[] edges = [20, 120, 500, 2000, Math.Min(6000, rate / 2.0), Math.Min(20000, rate / 2.0)];
        var readings = new StereoBandReading[5];
        double a = 1 - Math.Exp(-dt / .2);
        for (int band = 0; band < 5; band++)
        {
            double l = 0, r = 0, crossEnergy = 0;
            int from = Math.Max(1, (int)Math.Ceiling(edges[band] * settings.FftSize / rate));
            int to = Math.Min(settings.FftSize / 2, (int)Math.Ceiling(edges[band + 1] * settings.FftSize / rate) - 1);
            for (int k = from; k <= to; k++)
            {
                l += realL[k] * realL[k] + imagL[k] * imagL[k];
                r += realR[k] * realR[k] + imagR[k] * imagR[k];
                crossEnergy += realL[k] * realR[k] + imagL[k] * imagR[k];
            }
            bandL[band] += (l * norm * norm - bandL[band]) * a;
            bandR[band] += (r * norm * norm - bandR[band]) * a;
            bandCross[band] += (crossEnergy * norm * norm - bandCross[band]) * a;
            double energy = bandL[band] + bandR[band];
            bool audible = energy > 1e-8 && edges[band + 1] > edges[band];
            double correlation = audible && bandL[band] * bandR[band] > 1e-20 ? Math.Clamp(bandCross[band] / Math.Sqrt(bandL[band] * bandR[band]), -1, 1) : double.NaN;
            double side = audible ? Math.Clamp((energy - 2 * bandCross[band]) / (2 * energy), 0, 1) : double.NaN;
            readings[band] = new(edges[band], Math.Max(edges[band], edges[band + 1]), correlation, side);
        }
        return readings;
    }
}
