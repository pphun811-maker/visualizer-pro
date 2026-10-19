namespace VisualizerPro.Core;

public enum ChannelView { Stereo, Left, Right, Mid, Side }
public enum WindowKind { Hann, BlackmanHarris, FlatTop }
public enum FrequencyPaletteKind { Soft, Standard }

public sealed record DisplaySettings
{
    public bool FrequencyColors { get; init; }
    public FrequencyPaletteKind FrequencyPalette { get; init; }
    public bool SpectrogramIntensityColors { get; init; }
    public double SpectrumAttackMs { get; init; } = 35;
    public double StereoSensitivity { get; init; } = 1;
    public double StereoTrailMs { get; init; } = 100;
    public double WaveformMs { get; init; } = 20;
    public int WaterfallSeconds { get; init; } = 8;
    public double WaterfallAzimuth { get; init; } = 32;
    public double WaterfallElevation { get; init; } = 35;

    public DisplaySettings Sanitize() => this with
    {
        FrequencyPalette = Enum.IsDefined(FrequencyPalette) ? FrequencyPalette : FrequencyPaletteKind.Soft,
        SpectrumAttackMs = Math.Clamp(double.IsFinite(SpectrumAttackMs) ? SpectrumAttackMs : 35, 0, 150),
        StereoSensitivity = Math.Clamp(double.IsFinite(StereoSensitivity) ? StereoSensitivity : 1, .25, 2),
        StereoTrailMs = Math.Clamp(double.IsFinite(StereoTrailMs) ? StereoTrailMs : 100, 0, 250),
        WaveformMs = Math.Clamp(double.IsFinite(WaveformMs) ? WaveformMs : 20, 2, 30),
        WaterfallSeconds = WaterfallSeconds is 4 or 8 or 16 ? WaterfallSeconds : 8,
        WaterfallAzimuth = Math.Clamp(double.IsFinite(WaterfallAzimuth) ? WaterfallAzimuth : 32, -50, 50),
        WaterfallElevation = Math.Clamp(double.IsFinite(WaterfallElevation) ? WaterfallElevation : 35, 15, 65)
    };
}

public sealed record StereoBandReading(double LowHz, double HighHz, double Correlation, double SideFraction);
public readonly record struct OctaveBandReading(double CenterHz, double LowHz, double HighHz, double LevelDbfs, double AverageDbfs, double PeakDbfs);

public sealed record AnalysisSettings
{
    public int FftSize { get; init; } = 8192;
    public WindowKind Window { get; init; } = WindowKind.Hann;
    public ChannelView Channel { get; init; } = ChannelView.Stereo;
    public double AverageMs { get; init; } = 250;
    public double ReleaseMs { get; init; } = 180;
    public double SmoothingOctaves { get; init; } = 1.0 / 12;
    public double MinFrequency { get; init; } = 20;
    public double MaxFrequency { get; init; } = 20000;
    public double FloorDb { get; init; } = -100;
    public double CeilingDb { get; init; } = 0;
    public double TiltDbPerOctave { get; init; }
    public int HistorySeconds { get; init; } = 30;
    public double SpectrogramRange { get; init; } = 80;
    public bool PeakHold { get; init; } = true;

    public AnalysisSettings Sanitize()
    {
      var result = this with
      {
        FftSize = new[] { 2048, 4096, 8192, 16384, 32768, 65536 }.Contains(FftSize) ? FftSize : 8192,
        Window = Enum.IsDefined(Window) ? Window : WindowKind.Hann,
        Channel = Enum.IsDefined(Channel) ? Channel : ChannelView.Stereo,
        AverageMs = Math.Clamp(double.IsFinite(AverageMs) ? AverageMs : 250, 0, 4000),
        ReleaseMs = Math.Clamp(double.IsFinite(ReleaseMs) ? ReleaseMs : 180, 0, 3000),
        MinFrequency = Math.Clamp(double.IsFinite(MinFrequency) ? MinFrequency : 20, 10, 19000),
        MaxFrequency = Math.Clamp(double.IsFinite(MaxFrequency) ? MaxFrequency : 20000, 30, 96000),
        FloorDb = Math.Clamp(double.IsFinite(FloorDb) ? FloorDb : -100, -140, -20),
        CeilingDb = Math.Clamp(double.IsFinite(CeilingDb) ? CeilingDb : 0, -10, 20),
        SmoothingOctaves = Math.Clamp(double.IsFinite(SmoothingOctaves) ? SmoothingOctaves : 0, 0, 1),
        TiltDbPerOctave = Math.Clamp(double.IsFinite(TiltDbPerOctave) ? TiltDbPerOctave : 0, -6, 6),
        HistorySeconds = new[] { 10, 30, 60 }.Contains(HistorySeconds) ? HistorySeconds : 30,
        SpectrogramRange = Math.Clamp(double.IsFinite(SpectrogramRange) ? SpectrogramRange : 80, 30, 120)
      };
      if (result.MaxFrequency <= result.MinFrequency) result = result with { MinFrequency = 20, MaxFrequency = 20000 };
      return result;
    }
}

public sealed record MeterReading(double PeakLeft, double PeakRight, double RmsLeft, double RmsRight,
    double TruePeakLeft, double TruePeakRight, double Momentary, double ShortTerm, double Integrated,
    double Correlation, double Balance, double MidDb, double SideDb, double CrestDb, double Seconds);

public sealed record AnalysisFrame(long Sequence, double Time, int SampleRate, AnalysisSettings Settings,
    double[] Frequencies, float[] Spectrum, float[] Left, float[] Right, float[] Average, float[] Peaks,
    float[] PhaseLeft, float[] PhaseRight, MeterReading Meters)
{
    public SpectrogramSlice? Spectrogram { get; init; }
    public StereoBandReading[] StereoBands { get; init; } = [];
    public OctaveBandReading[] OctaveBands { get; init; } = [];
}

public sealed record SpectrumSnapshot(string Name, string Source, DateTime Created, int SampleRate,
    AnalysisSettings Settings, double[] Frequencies, float[] Spectrum)
{
    public OctaveBandReading[] OctaveBands { get; init; } = [];
}

public static class AudioMath
{
    public static double Db(double amplitude) => amplitude > 1e-7 ? 20 * Math.Log10(amplitude) : -140;
    public static double PowerDb(double energy) => energy > 1e-14 ? 10 * Math.Log10(energy) : -140;
    public static string Note(double frequency)
    {
        if (frequency <= 0) return "—";
        double note = 69 + 12 * Math.Log2(frequency / 440);
        int nearest = (int)Math.Round(note);
        string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
        return $"{names[((nearest % 12) + 12) % 12]}{nearest / 12 - 1} {100 * (note - nearest):+0;-0;0}¢";
    }
}
