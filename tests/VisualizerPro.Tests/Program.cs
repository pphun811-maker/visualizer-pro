using VisualizerPro.Core;

if (args.Length >= 2 && args[0] == "--stress") return Stress.Run(double.Parse(args[1]), args.Length >= 3 ? args[2] : null);

if (args.Length >= 3 && args[0] == "--measure")
{
    byte[] bytes = File.ReadAllBytes(args[1]); var samples = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
    int sampleRate = int.Parse(args[2]); var analyzer = new Analyzer(sampleRate); AnalysisFrame? last = null;
    for (int offset = 0; offset < samples.Length; offset += 8192) last = analyzer.Add(samples.AsSpan(offset, Math.Min(8192, samples.Length - offset))) ?? last;
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { integrated = last!.Meters.Integrated, truePeak = Math.Max(last.Meters.TruePeakLeft, last.Meters.TruePeakRight) }));
    return 0;
}

int passed = 0, failed = 0;
void Test(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS  {name}"); passed++; }
    catch (Exception e) { Console.WriteLine($"FAIL  {name}: {e.Message}"); failed++; }
}
void Near(double value, double expected, double tolerance, string reason)
{
    if (!double.IsFinite(value) || Math.Abs(value - expected) > tolerance) throw new Exception($"{reason}: {value:F5}, expected {expected:F5} ± {tolerance}");
}
float[] Signal(int rate, double seconds, double hz, double amplitude, double rightSign = 1)
{
    var result = new float[(int)(rate * seconds) * 2];
    for (int i = 0; i < result.Length / 2; i++) { result[2 * i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate)); result[2 * i + 1] = (float)(result[2 * i] * rightSign); }
    return result;
}
AnalysisFrame Run(float[] samples, int rate = 48000, AnalysisSettings? settings = null)
{
    var analyzer = new Analyzer(rate, settings ?? new() { SmoothingOctaves = 0 }); AnalysisFrame? frame = null;
    for (int offset = 0; offset < samples.Length; offset += 2048) frame = analyzer.Add(samples.AsSpan(offset, Math.Min(2048, samples.Length - offset))) ?? frame;
    return frame ?? throw new Exception("No analysis frame");
}

Test("FFT impulse and inverse symmetry", () =>
{
    double[] real = new double[1024], imaginary = new double[1024]; real[0] = 1; Fourier.Transform(real, imaginary);
    for (int i = 0; i < real.Length; i++) { Near(real[i], 1, 1e-10, "Real"); Near(imaginary[i], 0, 1e-10, "Imaginary"); }
});
foreach (var window in Enum.GetValues<WindowKind>())
Test($"Calibrated spectrum, {window}", () =>
{
    const double hz = 48000.0 * 171 / 8192;
    var f = Run(Signal(48000, 1, hz, .5), settings: new() { Window = window, SmoothingOctaves = 0 });
    int index = Array.IndexOf(f.Spectrum, f.Spectrum.Max());
    Near(f.Frequencies[index], hz, 15, "Frequency"); Near(f.Spectrum[index], -6.0206, .08, "Amplitude dBFS");
});
Test("Sine RMS and stereo correlation", () =>
{
    var f = Run(Signal(48000, 1, 1000, .5));
    Near(f.Meters.RmsLeft, -9.0309, .01, "RMS"); Near(f.Meters.Correlation, 1, 1e-6, "Correlation");
    Near(f.Meters.Balance, 0, 1e-6, "Balance");
});
Test("Antiphase remains visible in stereo, cancels in Mid", () =>
{
    var samples = Signal(48000, 1, 1000, .5, -1);
    var stereo = Run(samples); var mid = Run(samples, settings: new() { Channel = ChannelView.Mid, SmoothingOctaves = 0 });
    Near(stereo.Meters.Correlation, -1, 1e-6, "Correlation");
    if (stereo.Spectrum.Max() < -8 || mid.Spectrum.Max() > -130) throw new Exception("Incorrect stereo/Mid handling");
});
Test("BS.1770 1 kHz stereo -20 LUFS reference (0.1 amplitude)", () =>
{
    // Two channels of -20 dBFS-peak sine: -23.01 unweighted/channel +3.01 stereo,
    // approximately +0 dB net K-weighting at 1 kHz after the -0.691 offset.
    var f = Run(Signal(48000, 4, 1000, .1)); Near(f.Meters.Integrated, -20, .07, "Stereo integrated");
    Near(f.Meters.Momentary, -20, .07, "Momentary"); Near(f.Meters.ShortTerm, -20, .07, "Short term");
});
Test("BS.1770 absolute gate excludes silence", () =>
{
    var f = Run(new float[48000 * 4 * 2]); if (!double.IsNegativeInfinity(f.Meters.Integrated)) throw new Exception("Silence must not integrate");
    if (!double.IsNaN(f.Meters.Correlation)) throw new Exception("Silence correlation should be undefined");
    if (!double.IsNaN(f.Meters.CrestDb)) throw new Exception("Silence crest factor should be undefined");
});
Test("BS.1770 44.1 kHz weighting", () =>
{
    var f = Run(Signal(44100, 4, 1000, .1), 44100); Near(f.Meters.Integrated, -20, .08, "44.1 kHz integrated");
});
Test("True Peak recovers intersample sine overshoot", () =>
{
    var meter = new TruePeakMeter(); double samplePeak = 0;
    for (int i = 0; i < 48000; i++) { double x = .99 * Math.Sin(2 * Math.PI * .25 * i + Math.PI / 4); samplePeak = Math.Max(samplePeak, Math.Abs(x)); meter.Add(x); }
    Near(AudioMath.Db(meter.Maximum), AudioMath.Db(.99), .12, "Reconstructed peak");
    if (meter.Maximum < samplePeak * 1.3) throw new Exception("No intersample overshoot detected");
});
Test("Display controls cannot change metering", () =>
{
    var samples = Signal(48000, 1, 1000, .25);
    var a = Run(samples); var b = Run(samples, settings: new() { TiltDbPerOctave = 6, ReleaseMs = 2000, AverageMs = 3000, SmoothingOctaves = .5 });
    Near(a.Meters.RmsLeft, b.Meters.RmsLeft, 1e-9, "RMS invariant"); Near(a.Meters.Integrated, b.Meters.Integrated, 1e-9, "Loudness invariant");
});
Test("Nonfinite input is sanitized", () =>
{
    var samples = Signal(48000, 1, 1000, .25); samples[100] = float.NaN; samples[250] = float.PositiveInfinity;
    var f = Run(samples); if (f.Spectrum.Any(x => !float.IsFinite(x))) throw new Exception("NaN in spectrum");
});
Test("Engine source reset discards queued old frames", () =>
{
    using var engine = new AnalysisEngine(); engine.Submit(Signal(48000, .1, 1000, .5), 48000); engine.Reset();
    engine.Submit(new float[2048], 44100);
    if (!SpinWait.SpinUntil(() => engine.Latest?.SampleRate == 44100, 5000)) throw new Exception("Reset lost new source");
});
Test("Musical cursor", () => { if (!AudioMath.Note(440).StartsWith("A4")) throw new Exception("A4 mismatch"); });
Test("Capture cadence preserves 60 Hz across 10 ms audio blocks", () =>
{
    var analyzer = new Analyzer(48000); int frames = 0;
    for (int i = 0; i < 100; i++) if (analyzer.Add(new float[960]) != null) frames++;
    Near(frames, 60, 1, "Frame rate");
});
Test("Corrupt persisted analysis configuration recovers", () =>
{
    var settings = new AnalysisSettings { Channel = (ChannelView)999, Window = (WindowKind)999, MinFrequency = 19000, MaxFrequency = 30, SpectrogramRange = double.NaN }.Sanitize();
    if (settings.MaxFrequency <= settings.MinFrequency || !double.IsFinite(settings.SpectrogramRange) || !Enum.IsDefined(settings.Channel) || !Enum.IsDefined(settings.Window)) throw new Exception("Invalid settings survived validation");
});
Test("Whole-file finalization includes a trailing partial block", () =>
{
    var analyzer = new Analyzer(48000); analyzer.Add(Signal(48000, .2, 1000, .25));
    analyzer.Add(new float[40]); var frame = analyzer.Finish();
    Near(frame.Time, .2 + 20.0 / 48000, 1e-9, "Final sample time");
    if (!double.IsFinite(frame.Meters.TruePeakLeft)) throw new Exception("Final true peak missing");
});

Test("Spectrum damps rapid flutter while preserving transients and channel alignment", () =>
{
    var smooth = new Analyzer(48000, new() { FftSize = 2048, SmoothingOctaves = 0 });
    var raw = new Analyzer(48000, new() { FftSize = 2048, SmoothingOctaves = 0, ReleaseMs = 0 });
    var smoothLevels = new List<double>(); var rawLevels = new List<double>();
    AnalysisFrame? lastSmooth = null, lastRaw = null;
    for (int frame = 0; frame < 240; frame++)
    {
        double amplitude = frame < 60 ? .1 : frame < 180 ? (frame % 2 == 0 ? .1 : .4) : .4;
        var block = new float[1600];
        for (int i = 0; i < 800; i++) block[2 * i] = block[2 * i + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * 1125 * (frame * 800 + i) / 48000));
        lastSmooth = smooth.Add(block)!; lastRaw = raw.Add(block)!;
        if (frame is > 90 and < 180) { smoothLevels.Add(lastSmooth.Spectrum.Max()); rawLevels.Add(lastRaw.Spectrum.Max()); }
        if (frame == 189) Near(lastSmooth.Spectrum.Max(), lastRaw.Spectrum.Max(), .6, "Step settles within 167 ms including FFT window");
        int bin = Array.IndexOf(lastSmooth.Spectrum, lastSmooth.Spectrum.Max());
        Near(lastSmooth.Left[bin], lastSmooth.Spectrum[bin], .0001, "Mono left trace follows main envelope");
        Near(lastSmooth.Right[bin], lastSmooth.Spectrum[bin], .0001, "Mono right trace follows main envelope");
    }
    double Variation(List<double> values) => values.Zip(values.Skip(1), (a, b) => Math.Abs(b - a)).Average();
    double ratio = Variation(smoothLevels) / Variation(rawLevels);
    Console.WriteLine($"      flutter variation ratio {ratio:F3}");
    if (ratio >= .5) throw new Exception($"Insufficient flutter reduction: {ratio:F3}");
    Near(lastSmooth!.Peaks.Max(), lastRaw!.Peaks.Max(), .0001, "Peak hold unaffected by attack");
    Near(lastSmooth.Meters.RmsLeft, lastRaw.Meters.RmsLeft, 1e-9, "Meter unaffected by attack");
});

Test("Stereo phase history retains consecutive high-frequency samples", () =>
{
    foreach (int rate in new[] { 44100, 48000, 192000 })
    {
        var signal = Signal(rate, .25, 12000, .4, -1);
        var frame = Run(signal, rate);
        int count = rate / 30;
        if (frame.PhaseLeft.Length != count) throw new Exception("Unexpected phase window length");
        int start = signal.Length / 2 - count;
        for (int i = 0; i < count; i++)
        {
            Near(frame.PhaseLeft[i], signal[2 * (start + i)], 1e-7, "Consecutive phase sample");
            Near(frame.PhaseRight[i], -frame.PhaseLeft[i], 1e-7, "Stereo polarity retained");
        }
    }
});

Test("Display response settings cannot change calibrated meters or band readings", () =>
{
    var samples = Signal(48000, 1, 1125, .25);
    var a = new Analyzer(48000); var b = new Analyzer(48000) { Display = new() { SpectrumAttackMs = 120, StereoSensitivity = 2, StereoTrailMs = 250 } };
    AnalysisFrame? fa = null, fb = null;
    for (int at = 0; at < samples.Length; at += 1600)
    {
        fa = a.Add(samples.AsSpan(at, Math.Min(1600, samples.Length - at))) ?? fa;
        fb = b.Add(samples.AsSpan(at, Math.Min(1600, samples.Length - at))) ?? fb;
    }
    Near(fa!.Meters.RmsLeft, fb!.Meters.RmsLeft, 1e-12, "RMS");
    Near(fa.Meters.Integrated, fb.Meters.Integrated, 1e-12, "Integrated loudness");
    Near(fa.Meters.TruePeakLeft, fb.Meters.TruePeakLeft, 1e-12, "True peak");
    Near(fa.StereoBands[2].Correlation, fb.StereoBands[2].Correlation, 1e-12, "Band correlation");
});

Test("Band stereo separates centered bass and antiphase midrange", () =>
{
    foreach (int rate in new[] { 48000, 192000 })
    {
        var samples = new float[rate * 2];
        for (int i = 0; i < rate; i++)
        {
            double low = .2 * Math.Sin(2 * Math.PI * 93.75 * i / rate), high = .2 * Math.Sin(2 * Math.PI * 1125 * i / rate);
            samples[2 * i] = (float)(low + high); samples[2 * i + 1] = (float)(low - high);
        }
        var f = Run(samples, rate);
        Near(f.StereoBands[0].Correlation, 1, .005, "Bass correlation");
        Near(f.StereoBands[0].SideFraction, 0, .005, "Bass side energy");
        Near(f.StereoBands[2].Correlation, -1, .005, "Mid correlation");
        Near(f.StereoBands[2].SideFraction, 1, .005, "Mid side energy");
    }
});

Test("Band stereo handles quadrature and silence", () =>
{
    var samples = Signal(48000, 1, 1125, .2);
    for (int i = 0; i < samples.Length / 2; i++) samples[2 * i + 1] = (float)(.2 * Math.Cos(2 * Math.PI * 1125 * i / 48000));
    var f = Run(samples);
    Near(f.StereoBands[2].Correlation, 0, .002, "Quadrature correlation");
    Near(f.StereoBands[2].SideFraction, .5, .002, "Quadrature side energy");
    var silent = Run(new float[96000]);
    if (silent.StereoBands.Any(b => !double.IsNaN(b.Correlation) || !double.IsNaN(b.SideFraction))) throw new Exception("Silent band must be undefined");
    var lowRate = Run(Signal(8000, 1, 1000, .2), 8000);
    if (lowRate.StereoBands.Any(b => b.HighHz > 4000)) throw new Exception("Band exceeds Nyquist");
});

Test("History is bounded, preserves frozen readers and clears on source/FFT changes", () =>
{
    var fixture = Run(Signal(48000, .2, 1125, .2)); var history = new FrameHistory();
    for (int i = 0; i < 1000; i++) history.Add(fixture with { Time = i * .025, Sequence = i }, 10);
    if (history.Count > 200 || history.Count < 190) throw new Exception($"History count {history.Count}");
    var held = history.Frames.ToArray(); double anchor = held[^1].Time;
    for (int i = 1000; i < 2000; i++) history.Add(fixture with { Time = i * .025, Sequence = i }, 10);
    Near(held[^1].Time, anchor, 1e-12, "Frozen anchor");
    var selected = FrameHistory.AtAge(held, 1.25) ?? throw new Exception("Cannot inspect valid frozen history");
    Near(anchor - selected.Time, 1.25, .026, "History time lookup");
    if (FrameHistory.AtAge(held, 20) != null) throw new Exception("Expired time must not alias oldest frame");
    history.Add(fixture with { Time = .1, Sequence = 1 }, 10);
    if (history.Count != 1) throw new Exception("Seek must clear old source history");
    history.Add(fixture with { Time = .2, Settings = fixture.Settings with { FftSize = 4096 } }, 10);
    if (history.Count != 1) throw new Exception("FFT change must clear incompatible history");
    history.Clear(); history.Trim(10);
    if (FrameHistory.AtAge(history.Frames, 0) != null) throw new Exception("Empty history lookup");
});

Test("History time gaps are not reported as measured frames", () =>
{
    var fixture = Run(Signal(48000, .2, 1125, .2)); var history = new FrameHistory();
    history.Add(fixture with { Time = 1 }, 30); history.Add(fixture with { Time = 2 }, 30);
    if (FrameHistory.AtAge(history.Frames, .5) != null) throw new Exception("Missing time must not return a neighboring frame");
    history.Trim(1);
    if (history.Count != 1) throw new Exception("Duration trim");
});

Test("History tolerates floating-point grid drift and repeated paint callbacks", () =>
{
    var fixture = Run(Signal(48000, .2, 1125, .2)); var history = new FrameHistory();
    for (int i = 0; i < 3000; i++)
    {
        var frame = fixture with { Time = i * .05 + .0213333333333333, Sequence = i };
        history.Add(frame, 60); history.Add(frame, 60); history.Add(frame, 60);
    }
    if (history.Count is < 1199 or > 1200) throw new Exception($"Grid drift lost history: {history.Count}");
});

Test("Corrupt display configuration recovers", () =>
{
    var value = new DisplaySettings { SpectrumAttackMs = double.NaN, StereoSensitivity = double.PositiveInfinity, StereoTrailMs = -20, WaveformMs = 2000 }.Sanitize();
    Near(value.SpectrumAttackMs, 35, 0, "Default attack"); Near(value.StereoSensitivity, 1, 0, "Default sensitivity");
    Near(value.StereoTrailMs, 0, 0, "Trail minimum"); Near(value.WaveformMs, 30, 0, "Waveform maximum");
});

Test("Buffered history recovery respects playback time and reset", () =>
{
    using var engine = new AnalysisEngine();
    for (int i = 0; i < 16; i++) engine.Submit(new float[1600], 48000);
    if (!SpinWait.SpinUntil(() => engine.Latest?.Time > .26, 5000)) throw new Exception("Buffered fixture timed out");
    var recovered = engine.FramesBetween(.05, .15);
    if (recovered.Length < 5 || recovered.Any(f => f.Time <= .05 || f.Time > .150001)) throw new Exception("Read-ahead leaked into played history");
    Near(recovered[^1].Time, .15, .00001, "Played recovery boundary");
    engine.Reset();
    if (engine.FramesBetween(-1, 100).Length != 0) throw new Exception("Source reset retained buffered frames");
});

foreach (var window in Enum.GetValues<WindowKind>())
Test($"Third-octave RMS calibration, {window}", () =>
{
    var f = Run(Signal(48000, 1.5, 1000, .5), settings: new() { Window = window, ReleaseMs = 0 });
    var band = f.OctaveBands.Single(b => b.CenterHz == 1000);
    Near(band.LevelDbfs, -9.0309, .025, "Band RMS, not FFT peak magnitude");
    Near(band.AverageDbfs, -9.0309, .1, "Energy average");
    Near(band.LowHz, 1000 / Math.Pow(10, .05), 1e-8, "Exact lower edge");
    Near(band.HighHz, 1000 * Math.Pow(10, .05), 1e-8, "Exact upper edge");
});

Test("Third-octave integrates two tones instead of selecting the maximum", () =>
{
    var pair = Signal(48000, 1, 950, .25); var other = Signal(48000, 1, 1050, .25);
    for (int i = 0; i < pair.Length; i++) pair[i] += other[i];
    var one = Run(Signal(48000, 1, 950, .25), settings: new() { ReleaseMs = 0 });
    var two = Run(pair, settings: new() { ReleaseMs = 0 });
    double a = one.OctaveBands.Single(b => b.CenterHz == 1000).LevelDbfs, b = two.OctaveBands.Single(b => b.CenterHz == 1000).LevelDbfs;
    Near(b - a, 3.0103, .04, "Summed band energy");
});

Test("Third-octave preserves stereo polarity and ignores frequency smoothing", () =>
{
    var signal = Signal(48000, 1, 1000, .4, -1);
    var stereo = Run(signal, settings: new() { ReleaseMs = 0, SmoothingOctaves = 0 });
    var smooth = Run(signal, settings: new() { ReleaseMs = 0, SmoothingOctaves = .5 });
    var mid = Run(signal, settings: new() { ReleaseMs = 0, Channel = ChannelView.Mid });
    var side = Run(signal, settings: new() { ReleaseMs = 0, Channel = ChannelView.Side });
    Near(stereo.OctaveBands[17].LevelDbfs, smooth.OctaveBands[17].LevelDbfs, 1e-9, "Frequency smoothing independent");
    Near(stereo.OctaveBands[17].LevelDbfs, side.OctaveBands[17].LevelDbfs, 1e-9, "Antiphase retained");
    if (mid.OctaveBands.Any(b => b.LevelDbfs > -130)) throw new Exception("Mid must cancel antiphase");
});

Test("Third-octave broadband energy has Parseval normalization", () =>
{
    var random = new Random(173); var samples = new float[96000];
    for (int i = 0; i < samples.Length / 2; i++) samples[2 * i] = samples[2 * i + 1] = random.Next(2) == 0 ? -.1f : .1f;
    var f = Run(samples, settings: new() { ReleaseMs = 0 });
    double power = f.OctaveBands.Sum(b => Math.Pow(10, b.LevelDbfs / 10));
    double expected = .01 * (f.OctaveBands[^1].HighHz - f.OctaveBands[0].LowHz) / 24000;
    Near(power, expected, .00035, "Covered white-noise energy");
    Near(f.Meters.RmsLeft, -20, .00001, "Meter calibration unchanged");
});

Test("Third-octave Nyquist and resolved low-frequency calibration", () =>
{
    var samples = new float[80000];
    for (int i = 0; i < samples.Length / 2; i++) samples[2 * i] = samples[2 * i + 1] = i % 2 == 0 ? .5f : -.5f;
    var f = Run(samples, 40000, new() { ReleaseMs = 0 });
    Near(f.OctaveBands[^1].LevelDbfs, -6.0206, .03, "Nyquist RMS includes the full one-sided edge bin");
    var low = Run(Signal(48000, 2, 20, .5), settings: new() { FftSize = 65536, ReleaseMs = 0 });
    Near(low.OctaveBands[0].LevelDbfs, -9.0309, .1, "Resolved 20 Hz energy");
    var limited = Run(Signal(8000, .3, 1000, .1), 8000);
    if (limited.OctaveBands.Any(b => b.HighHz > 4000 || b.HighHz <= b.LowHz)) throw new Exception("Invalid partial band at low sample rate");
});

Test("Third-octave silence and contiguous preferred band definitions", () =>
{
    var f = Run(new float[96000]);
    if (f.OctaveBands.Length != 31 || f.OctaveBands.Any(b => b.LevelDbfs != -140)) throw new Exception("Silent preferred bands");
    for (int i = 1; i < f.OctaveBands.Length; i++) Near(f.OctaveBands[i].LowHz, f.OctaveBands[i - 1].HighHz, 1e-8, "Adjacent exact boundaries");
    var display = new DisplaySettings { WaterfallAzimuth = double.NaN, WaterfallElevation = 999, WaterfallSeconds = -1 }.Sanitize();
    Near(display.WaterfallAzimuth, 32, 0, "Camera default"); Near(display.WaterfallElevation, 65, 0, "Camera bound");
    if (display.WaterfallSeconds != 8) throw new Exception("Waterfall duration default");
});

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
