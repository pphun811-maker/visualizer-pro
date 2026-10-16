using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using VisualizerPro.Core;

namespace VisualizerPro.App.Views;

public enum InstrumentKind { Spectrum, Spectrogram, Stereo, Levels }

public sealed partial class InstrumentView : Grid, IDisposable
{
    public InstrumentKind Kind { get; set; }
    private AnalysisFrame? frame;
    public AnalysisFrame? Frame { get => frame; set { frame = value; if (Kind == InstrumentKind.Stereo) { if (value == null) stereoDisplay.Reset(); else stereoDisplay.Accept(value); } probe.InvalidateVisual(); } }
    private AnalysisSettings settings = new();
    public AnalysisSettings Settings { get => settings; set { settings = value; probe.InvalidateVisual(); } }
    private DisplaySettings display = new();
    public DisplaySettings Display { get => display; set { display = value.Sanitize(); stereoDisplay.Settings = display; } }
    private StereoDisplayMode secondaryMode;
    public StereoDisplayMode SecondaryMode { get => secondaryMode; set { secondaryMode = value; UpdateHeaderActions(); } }
    public Func<double, AnalysisFrame?>? HistoryFrameAtAge { get; set; }
    public event Action<double, double>? ReviewRequested;
    private double? selectedHistoryAge;
    public double? SelectedHistoryAge { get => selectedHistoryAge; set { selectedHistoryAge = value; probe.InvalidateVisual(); } }
    internal double? CursorHistoryAge { get; private set; }
    public IReadOnlyList<SpectrumSnapshot> Snapshots { get; set; } = [];
    public bool ShowAverage { get; set; } = true;
    public bool ShowPeaks { get; set; } = true;
    private bool frozen;
    public bool Frozen { get => frozen; set { frozen = value; frozenState.Visibility = value ? Visibility.Visible : Visibility.Collapsed; InvalidateVisual(); } }
    private SKGLElement? gpu;
    private SKElement? cpu;
    private bool disposed;
    private readonly SpectrumProbeOverlay probe;
    public string Renderer => gpu == null ? "SOFTWARE" : "OPENGL";
    public bool RenderActive => gpu?.RenderContinuously ?? false;
    public event Action? BeforePaint;
    public void SetRenderActivity(bool active) { if (gpu != null) gpu.RenderContinuously = active; }
    public event Action<long>? FramePainted;
    public long RenderCount { get; private set; }
    public double RenderMilliseconds { get; private set; }
    private double cursorFrequency;
    public double CursorFrequency { get => cursorFrequency; set { cursorFrequency = value; probe.InvalidateVisual(); } }
    public double? CursorLevelDbfs
    {
        get
        {
            if (Kind == InstrumentKind.Spectrum && SpectrumMode == SpectrumDisplayMode.ThirdOctave) return CursorOctaveBand?.LevelDbfs;
            if (IsWaterfall) return WaterfallCursor?.LevelDbfs;
            var reading = Kind == InstrumentKind.Spectrogram && CursorHistoryAge is { } age ? HistoryFrameAtAge?.Invoke(age) : Frame;
            return reading == null || CursorFrequency <= 0 ? null : SampleSpectrum(reading, CursorFrequency);
        }
    }
    internal Point? PointerPosition { get; private set; }
    internal Rect PlotBounds => Kind == InstrumentKind.Spectrum ? new Rect(49, 82, Math.Max(0, ActualWidth - 72), Math.Max(0, ActualHeight - 143)) : new Rect(49, 78, Math.Max(0, ActualWidth - 72), Math.Max(0, ActualHeight - 149));
    internal double AxisMinimum => MinFrequency;
    internal double AxisMaximum => MaxFrequency;
    internal double FrequencyX(double frequency) => PlotBounds.Left + Math.Log(frequency / MinFrequency) / Math.Log(MaxFrequency / MinFrequency) * PlotBounds.Width;
    public event Action? InputActivity;
    public event Action<double>? CursorChanged;
    public event Action<double, double>? RangeChanged;
    public event Action<double, double>? BandChanged;
    public void SetBand(double low, double high) { bandLow = low; bandHigh = high; }
    public event Action<InstrumentKind>? ExpandRequested;
    private readonly SKTypeface mono = SKTypeface.FromFamilyName("Cascadia Mono") ?? SKTypeface.Default;
    private readonly SKTypeface ui = SKTypeface.FromFamilyName("Microsoft YaHei UI") ?? SKTypeface.Default;
    private static readonly SKColor Ink = SKColor.Parse("#DDDDDD"), Muted = SKColor.Parse("#858585"), Grid = SKColor.Parse("#292929"), Accent = SKColor.Parse("#7DA7E7"), Reference = SKColor.Parse("#AAAAAA"), Alert = SKColor.Parse("#FFFFFF");
    private static readonly SKColor[] HeatStops = [new(17, 17, 17), new(20, 27, 38), new(28, 47, 75), new(50, 87, 136), new(91, 143, 212), new(188, 215, 249)];
    private static readonly SKColor[] SnapshotColors = [SKColor.Parse("#D0D0D0"), SKColor.Parse("#A7BFE3"), SKColor.Parse("#999999"), SKColor.Parse("#657DA0")];
    private static readonly float[][] SnapshotDashes = [[7, 3], [2, 3], [10, 3, 2, 3], [14, 4]];
    private SpectrogramRaster? history;
    internal int HistoryRasterRows => history?.Count ?? 0;
    private readonly StereoDisplay stereoDisplay = new();
    private GRContext? limitedContext;
    private uint[] row = new uint[Analyzer.Bins];
    private int historyRows;
    private double lastHistoryTime = -1;
    private long lastSequence = -1;
    private int lastRate;
    private AnalysisSettings? historySettings;
    private SKRect plot;
    private double dragStart, bandLow, bandHigh;
    private bool dragging, reviewing;

    public InstrumentView()
    {
        probe = new SpectrumProbeOverlay(this) { IsHitTestVisible = false };
        InitializeHeaderActions();
        Background = Brushes.Transparent;
        try
        {
            if (Environment.GetEnvironmentVariable("VISUALIZERPRO_SOFTWARE") == "1") EnableSoftware();
            else
            {
                // MainWindow enables composition after loading and pauses it during
                // native window movement. Paint callbacks always draw completely.
                gpu = new SKGLElement { RenderContinuously = false, IsHitTestVisible = false };
                gpu.PaintSurface += (_, e) => Paint(e.Surface.Canvas, e.Info);
                Children.Add(gpu);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Services.SettingsStore.Log(error); EnableSoftware();
        }
        if (!Children.Contains(probe)) Children.Add(probe);
        if (!Children.Contains(headerActions)) Children.Add(headerActions);
        PreviewMouseMove += Move;
        MouseLeave += (_, _) => { if (!dragging && !reviewing && !orbiting) { PointerPosition = null; CursorHistoryAge = null; WaterfallCursor = null; CursorChanged?.Invoke(0); } };
        MouseLeftButtonDown += Down;
        MouseLeftButtonUp += Up;
        MouseRightButtonDown += (_, e) => { bandLow = bandHigh = 0; BandChanged?.Invoke(0, 0); RangeChanged?.Invoke(20, 20000); e.Handled = true; };
        MouseWheel += Wheel;
        LostMouseCapture += (_, _) => { dragging = reviewing = orbiting = false; };
        ToolTipService.SetIsEnabled(this, false);
        Focusable = false;
    }
    public void EnableSoftware()
    {
        if (cpu != null) return;
        try { gpu?.Dispose(); } catch (InvalidOperationException) { }
        gpu = null; Children.Clear(); cpu = new SKElement { IgnorePixelScaling = false, IsHitTestVisible = false };
        cpu.PaintSurface += (_, e) => Paint(e.Surface.Canvas, e.Info); Children.Add(cpu); Children.Add(probe); Children.Add(headerActions);
    }
    public new void InvalidateVisual() { base.InvalidateVisual(); gpu?.InvalidateVisual(); cpu?.InvalidateVisual(); }
    public void ResetHistory()
    {
        history?.Dispose(); history = null; lastHistoryTime = -1; lastSequence = -1;
        ClearWaterfall();
    }
    public void AcceptHistory(AnalysisFrame frame)
    {
        if (Kind != InstrumentKind.Spectrogram || Frozen || frame.Sequence == lastSequence && frame.SampleRate == lastRate) return;
        if (lastHistoryTime > frame.Time + 1e-6 || lastRate != frame.SampleRate || historySettings?.HistorySeconds != Settings.HistorySeconds || historySettings?.SpectrogramRange != Settings.SpectrogramRange || historySettings?.TiltDbPerOctave != Settings.TiltDbPerOctave || historySettings?.Channel != Settings.Channel)
            ResetHistory();
        if (lastHistoryTime < 0) lastHistoryTime = frame.Time - .05;
        int advance = (int)((frame.Time - lastHistoryTime) * 20 + 1e-6);
        if (advance < 1) return;
        historySettings = Settings; lastRate = frame.SampleRate; lastSequence = frame.Sequence;
        historyRows = Settings.HistorySeconds * 20;
        history ??= new SpectrogramRaster(Analyzer.Bins, historyRows);
        for (int i = 0; i < row.Length; i++)
        {
            double db = frame.Spectrum[i] + Settings.TiltDbPerOctave * Math.Log2(frame.Frequencies[i] / 1000);
            row[i] = (uint)Heat((db - Settings.CeilingDb + Settings.SpectrogramRange) / Settings.SpectrogramRange);
        }
        // Audio callbacks can deliver one FFT every 30–60 ms. Hold a nearby
        // sample over one missing 50 ms slot; a longer interruption stays blank.
        if (advance == 2) history.Append(row, 1);
        else if (advance > 2) history.Append(Enumerable.Repeat(0xFF111111u, Analyzer.Bins).ToArray(), advance - 1);
        history.Append(row, 1); lastHistoryTime += advance * .05;
    }
    public static SKColor Heat(double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var stops = HeatStops;
        double scaled = amount * (stops.Length - 1); int i = Math.Min(stops.Length - 2, (int)scaled); float t = (float)(scaled - i);
        return new((byte)(stops[i].Red + (stops[i + 1].Red - stops[i].Red) * t), (byte)(stops[i].Green + (stops[i + 1].Green - stops[i].Green) * t), (byte)(stops[i].Blue + (stops[i + 1].Blue - stops[i].Blue) * t));
    }
    private void Paint(SKCanvas c, SKImageInfo info, bool capture = false)
    {
        if (!capture) BeforePaint?.Invoke();
        if (!capture && gpu?.GRContext is { } context && !ReferenceEquals(context, limitedContext))
        {
            context.SetResourceCacheLimit(32L * 1024 * 1024); limitedContext = context;
        }
        if (!capture) { RenderCount++; FramePainted?.Invoke(Stopwatch.GetTimestamp()); }
        long paintStarted = Stopwatch.GetTimestamp();
        using var restore = new SKAutoCanvasRestore(c, true);
        c.Scale(info.Width / Math.Max(1f, (float)ActualWidth)); float w = (float)ActualWidth, h = (float)ActualHeight;
        c.Clear(SKColor.Parse("#0C0C0C"));
        using var card = new SKRoundRect(new SKRect(0, 0, w, h), 16, 16);
        using var surface = new SKPaint { Color = SKColor.Parse("#171717"), IsAntialias = true };
        c.DrawRoundRect(card, surface); c.ClipRoundRect(card, antialias: true);
        Text(c, Kind switch { InstrumentKind.Spectrum => SpectrumMode == SpectrumDisplayMode.ThirdOctave ? "01 / THIRD OCTAVE" : "01 / SPECTRUM", InstrumentKind.Spectrogram => IsWaterfall ? "02 / WATERFALL" : "02 / SPECTROGRAM", InstrumentKind.Stereo => SecondaryMode switch { StereoDisplayMode.Waveform => "03 / WAVEFORM", StereoDisplayMode.Bands => "03 / STEREO BANDS", _ => "03 / STEREO FIELD" }, _ => "04 / LEVEL & LOUDNESS" }, 20, 24, 10, Muted);
        Text(c, Kind switch { InstrumentKind.Spectrum => SpectrumMode == SpectrumDisplayMode.ThirdOctave ? "1/3 倍频程" : "频谱分析", InstrumentKind.Spectrogram => IsWaterfall ? "三维瀑布" : "时间 · 频率", InstrumentKind.Stereo => SecondaryMode switch { StereoDisplayMode.Waveform => "波形示波器", StereoDisplayMode.Bands => "分频段声场", _ => "立体声场" }, _ => "电平与响度" }, 20, 48, 16, Ink, false);
        string detail = Kind switch
        {
            InstrumentKind.Spectrum => SpectrumMode == SpectrumDisplayMode.ThirdOctave ? $"dBFS RMS / {Settings.Channel.ToString().ToUpperInvariant()}" : $"{Settings.FftSize:N0} FFT  /  {Settings.Window}  /  {Settings.Channel.ToString().ToUpperInvariant()}",
            InstrumentKind.Spectrogram => IsWaterfall ? $"{Math.Min(Settings.HistorySeconds, Display.WaterfallSeconds)}s / {Settings.SpectrogramRange:0} dB / 3D" : $"{Settings.HistorySeconds}s  /  {Settings.SpectrogramRange:0} dB  /  NEWEST ↑",
            InstrumentKind.Stereo => SecondaryMode switch { StereoDisplayMode.Waveform => $"{Display.WaveformMs:0} ms / ±1 FS", StereoDisplayMode.Bands => "CORR / S ENERGY", _ => "M / S  ·  L / R" },
            _ => "dBFS / LUFS"
        };
        Text(c, detail, w - 18, 25, 10, Muted, true, true);
        Line(c, 18, 61, w - 18, 61, Grid);
        if (w < 180 || h < 150) return;
        switch (Kind)
        {
            case InstrumentKind.Spectrum: if (SpectrumMode == SpectrumDisplayMode.ThirdOctave) DrawOctaves(c, w, h); else DrawSpectrum(c, w, h); break;
            case InstrumentKind.Spectrogram: if (IsWaterfall) DrawWaterfall(c, w, h); else DrawSpectrogram(c, w, h); break;
            case InstrumentKind.Stereo:
                if (SecondaryMode == StereoDisplayMode.Waveform) DrawWaveform(c, w, h);
                else if (SecondaryMode == StereoDisplayMode.Bands) DrawStereoBands(c, w, h);
                else DrawStereo(c, w, h);
                break;
            case InstrumentKind.Levels: DrawLevels(c, w, h); break;
        }
        if (!capture) RenderMilliseconds += Stopwatch.GetElapsedTime(paintStarted).TotalMilliseconds;
    }
    public System.Windows.Media.Imaging.BitmapSource CaptureBitmap(double scale)
    {
        var info = new SKImageInfo(Math.Max(1, (int)Math.Ceiling(ActualWidth * scale)), Math.Max(1, (int)Math.Ceiling(ActualHeight * scale)));
        using var surface = SKSurface.Create(info);
        Paint(surface.Canvas, info, true);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var stream = data.AsStream();
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) { drawing.DrawImage(bitmap, new Rect(0, 0, ActualWidth, ActualHeight)); probe.Draw(drawing); CaptureHeader(drawing); }
        var combined = new RenderTargetBitmap(info.Width, info.Height, scale * 96, scale * 96, PixelFormats.Pbgra32); combined.Render(visual); combined.Freeze(); return combined;
    }
    private void Text(SKCanvas c, string value, float x, float y, float size, SKColor color, bool isMono = true, bool right = false)
    {
        using var font = new SKFont(isMono ? mono : ui, size); using var paint = new SKPaint { Color = color, IsAntialias = true };
        if (right) x -= font.MeasureText(value);
        c.DrawText(value, x, y, font, paint);
    }
    private static void Line(SKCanvas c, float x1, float y1, float x2, float y2, SKColor color, float width = 1)
    {
        using var p = new SKPaint { Color = color, StrokeWidth = width, IsAntialias = true }; c.DrawLine(x1, y1, x2, y2, p);
    }
    private double MaxFrequency => Math.Min(Settings.MaxFrequency, (Frame?.SampleRate ?? 48000) / 2.0);
    private double MinFrequency => Math.Min(Settings.MinFrequency, MaxFrequency / 2);
    private float X(double f) => plot.Left + (float)(Math.Log(f / MinFrequency) / Math.Log(MaxFrequency / MinFrequency)) * plot.Width;
    private double Frequency(double x) => MinFrequency * Math.Pow(MaxFrequency / MinFrequency, Math.Clamp((x - plot.Left) / Math.Max(1, plot.Width), 0, 1));
    private float Y(double db) => plot.Bottom - (float)Math.Clamp((db - Settings.FloorDb) / (Settings.CeilingDb - Settings.FloorDb), 0, 1) * plot.Height;
    private void FrequencyGrid(SKCanvas c, bool labels)
    {
        double[] ticks = [20, 30, 50, 100, 200, 300, 500, 1000, 2000, 3000, 5000, 10000, 20000, 40000, 80000];
        float previous = -100;
        foreach (double f in ticks)
        {
            if (f < MinFrequency || f > MaxFrequency) continue;
            float x = X(f); Line(c, x, plot.Top, x, plot.Bottom, Grid.WithAlpha(160));
            if (labels && x - previous > 42)
            { Text(c, f >= 1000 ? $"{f / 1000:0.#}k" : $"{f:0}", x - 7, plot.Bottom + 21, 10, Muted); previous = x; }
        }
    }
    private void DrawSpectrum(SKCanvas c, float w, float h)
    {
        plot = new(49, 82, w - 23, h - 61);
        FrequencyGrid(c, true);
        for (double db = Settings.CeilingDb; db >= Settings.FloorDb; db -= 20)
        {
            float y = Y(db); Line(c, plot.Left, y, plot.Right, y, Grid); Text(c, $"{db:0}", 37, y + 3, 10, Muted, true, true);
        }
        if (Frame is { } f)
        {
            c.Save(); c.ClipRect(plot);
            Trace(c, f.Frequencies, f.Spectrum, Accent, 1.65f, true);
            if (Settings.Channel == ChannelView.Stereo) { Trace(c, f.Frequencies, f.Left, Accent.WithAlpha(85), .7f); Trace(c, f.Frequencies, f.Right, SKColor.Parse("#A7BFE3").WithAlpha(110), .7f); }
            if (ShowAverage) Trace(c, f.Frequencies, f.Average, SKColor.Parse("#C8C8C8").WithAlpha(150), 1.2f);
            if (ShowPeaks) Trace(c, f.Frequencies, f.Peaks, Reference.WithAlpha(155), .9f);
            for (int i = 0; i < Snapshots.Count; i++) Trace(c, Snapshots[i].Frequencies, Snapshots[i].Spectrum, SnapshotColors[i % 4], 1.3f, dashes: SnapshotDashes[i % 4]);
            if (bandHigh > bandLow)
            {
                using var p = new SKPaint { Color = Accent.WithAlpha(22) }; c.DrawRect(new SKRect(X(bandLow), plot.Top, X(bandHigh), plot.Bottom), p);
                Line(c, X(bandLow), plot.Top, X(bandLow), plot.Bottom, Accent.WithAlpha(120)); Line(c, X(bandHigh), plot.Top, X(bandHigh), plot.Bottom, Accent.WithAlpha(120));
            }
            c.Restore();
            string readout = "实时  ─  平均  ─  峰值保持";
            if (bandHigh > bandLow)
            {
                var values = f.Spectrum.Where((_, i) => f.Frequencies[i] >= bandLow && f.Frequencies[i] <= bandHigh).ToArray();
                double max = values.Length > 0 ? values.Max() : -140;
                readout = $"{bandLow:0} – {bandHigh:0} Hz   带内峰值 {max:0.0} dBFS";
            }
            Text(c, readout, 49, h - 15, 11, Muted, false);
        }
        else Empty(c, "等待音频信号", plot);
        Text(c, "Hz", w - 23, h - 15, 10, Muted, true, true);
    }
    public static int Nearest(double[] frequencies, double f)
    {
        int index = Array.BinarySearch(frequencies, f); if (index < 0) index = ~index;
        index = Math.Clamp(index, 0, frequencies.Length - 1);
        return index > 0 && Math.Abs(f - frequencies[index - 1]) < Math.Abs(frequencies[index] - f) ? index - 1 : index;
    }
    internal static double SampleSpectrum(AnalysisFrame frame, double frequency)
    {
        int upper = Array.BinarySearch(frame.Frequencies, frequency);
        if (upper >= 0) return frame.Spectrum[upper];
        upper = ~upper;
        if (upper == 0) return frame.Spectrum[0];
        if (upper >= frame.Frequencies.Length) return frame.Spectrum[^1];
        int lower = upper - 1;
        double fraction = Math.Log(frequency / frame.Frequencies[lower]) / Math.Log(frame.Frequencies[upper] / frame.Frequencies[lower]);
        return frame.Spectrum[lower] + (frame.Spectrum[upper] - frame.Spectrum[lower]) * fraction;
    }
    private void Trace(SKCanvas c, double[] frequencies, float[] values, SKColor color, float width, bool fill = false, float[]? dashes = null)
    {
        using var path = new SKPath(); bool first = true; float start = 0, end = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (frequencies[i] < MinFrequency || frequencies[i] > MaxFrequency) continue;
            float x = X(frequencies[i]), y = Y(values[i] + Settings.TiltDbPerOctave * Math.Log2(frequencies[i] / 1000));
            if (first) { path.MoveTo(x, y); start = x; first = false; } else path.LineTo(x, y); end = x;
        }
        if (fill && !first)
        {
            using var area = new SKPath(path); area.LineTo(end, plot.Bottom); area.LineTo(start, plot.Bottom); area.Close();
            using var p = new SKPaint { Color = color.WithAlpha(19), IsAntialias = true }; c.DrawPath(area, p);
        }
        using var dash = dashes == null ? null : SKPathEffect.CreateDash(dashes, 0);
        using var stroke = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true, PathEffect = dash }; c.DrawPath(path, stroke);
    }
    private void DrawSpectrogram(SKCanvas c, float w, float h)
    {
        plot = new(49, 78, w - 23, h - 71);
        if (history != null && Frame is { } f)
        {
            float left = (float)(Math.Log(MinFrequency / 10) / Math.Log((f.SampleRate / 2.0) / 10) * (Analyzer.Bins - 1));
            float right = (float)(Math.Log(MaxFrequency / 10) / Math.Log((f.SampleRate / 2.0) / 10) * (Analyzer.Bins - 1));
            // Rasterize in physical pixels, then map one output row to one
            // device row; ring-buffer wrapping no longer creates a second seam.
            var device = c.TotalMatrix.MapRect(plot);
            float top = (float)Math.Ceiling(device.Top), bottom = (float)Math.Floor(device.Bottom);
            int pixelHeight = Math.Max(1, (int)(bottom - top));
            var image = history.Image(pixelHeight);
            float scaleY = c.TotalMatrix.ScaleY;
            var destination = new SKRect(plot.Left, (top - c.TotalMatrix.TransY) / scaleY, plot.Right, (bottom - c.TotalMatrix.TransY) / scaleY);
            c.DrawImage(image, new SKRect(left, 0, right, image.Height), destination, new SKSamplingOptions(SKFilterMode.Linear));
        }
        FrequencyGrid(c, true);
        for (int i = 0; i <= 3; i++) Text(c, $"-{Settings.HistorySeconds * i / 3}s", 38, plot.Top + plot.Height * i / 3 + 4, 10, Muted, true, true);
        if (bandHigh > bandLow)
        {
            c.Save(); c.ClipRect(plot);
            using var bandPaint = new SKPaint { Color = Accent.WithAlpha(25) };
            c.DrawRect(new SKRect(X(bandLow), plot.Top, X(bandHigh), plot.Bottom), bandPaint);
            Line(c, X(bandLow), plot.Top, X(bandLow), plot.Bottom, Accent.WithAlpha(140)); Line(c, X(bandHigh), plot.Top, X(bandHigh), plot.Bottom, Accent.WithAlpha(140));
            c.Restore();
        }
        if (history == null) Empty(c, "等待音频信号", plot);
    }
    private void Empty(SKCanvas c, string label, SKRect area)
    {
        using var font = new SKFont(ui, 13); float x = area.MidX - font.MeasureText(label) / 2;
        Text(c, label, x, area.MidY, 13, Muted, false);
    }
    private void DrawStereo(SKCanvas c, float w, float h)
    {
        float cx = w / 2, cy = 74 + Math.Max(20, h - 159) / 2, radius = Math.Max(10, Math.Min(w - 90, h - 159) / 2);
        using var outline = new SKPaint { Color = Grid, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        c.DrawCircle(cx, cy, radius, outline); c.DrawCircle(cx, cy, radius / 2, outline);
        Line(c, cx - radius, cy, cx + radius, cy, Grid); Line(c, cx, cy - radius, cx, cy + radius, Grid);
        float d = radius * .7071f;
        Line(c, cx - d, cy - d, cx + d, cy + d, Grid); Line(c, cx + d, cy - d, cx - d, cy + d, Grid);
        Text(c, "M", cx - 3, cy - radius - 7, 10, Muted); Text(c, "L", cx - d - 12, cy - d, 10, Muted); Text(c, "R", cx + d + 6, cy - d, 10, Muted);
        Text(c, "S", cx + radius + 8, cy + 3, 10, Muted);
        if (Frame is { } f)
        {
            foreach (var trail in stereoDisplay.Traces)
            {
                if (f.Time - trail.Frame.Time > Display.StereoTrailMs / 1000) continue;
                using var path = new SKPath();
                var phase = trail.Frame;
                double gain = trail.DisplayGain(Display.StereoSensitivity);
                for (int i = 0; i < phase.PhaseLeft.Length; i++)
                {
                    float x = cx + (float)((phase.PhaseLeft[i] - phase.PhaseRight[i]) * .7071 * gain) * radius;
                    float y = cy - (float)((phase.PhaseLeft[i] + phase.PhaseRight[i]) * .7071 * gain) * radius;
                    if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
                }
                byte alpha = (byte)(85 * Math.Exp(-(f.Time - phase.Time) / Math.Max(.001, Display.StereoTrailMs / 1000 * .35)));
                using var trace = new SKPaint { Color = Accent.WithAlpha(alpha), IsAntialias = true, StrokeWidth = 1, Style = SKPaintStyle.Stroke }; c.DrawPath(path, trace);
            }
            float barY = h - 63; Line(c, 25, barY, w - 25, barY, Grid, 5);
            Line(c, w / 2, barY - 6, w / 2, barY + 6, Muted);
            if (double.IsFinite(f.Meters.Correlation))
            {
                float x = 25 + (float)((f.Meters.Correlation + 1) / 2) * (w - 50);
                using var p = new SKPaint { Color = f.Meters.Correlation < 0 ? Alert : Accent }; c.DrawCircle(x, barY, 4, p);
            }
            Text(c, "−1", 22, barY + 21, 10, Muted); Text(c, "+1", w - 23, barY + 21, 10, Muted, true, true);
            Text(c, double.IsFinite(f.Meters.Correlation) ? $"相关度 {f.Meters.Correlation:+0.00;-0.00;0.00}" : "相关度 —", cx - 43, barY + 21, 11, Ink, false);
            Text(c, $"M {Format(f.Meters.MidDb)}   S {Format(f.Meters.SideDb)} dBFS", 22, h - 14, 10, Muted);
            Text(c, $"平衡 {f.Meters.Balance * 100:+0;-0;0}%", w - 22, h - 14, 10, Muted, false, true);
        }
    }
    private void DrawLevels(SKCanvas c, float w, float h)
    {
        float top = 84, bottom = h - 54, barHeight = Math.Max(30, bottom - top), barX = 34;
        using var bg = new SKPaint { Color = SKColor.Parse("#2A2A2A") };
        c.DrawRect(barX, top, 16, barHeight, bg); c.DrawRect(barX + 27, top, 16, barHeight, bg);
        for (int db = 0; db >= -60; db -= 12)
        {
            float y = bottom - (db + 60) / 60f * barHeight;
            Line(c, barX - 4, y, barX + 48, y, Grid); Text(c, $"{db}", barX + 55, y + 3, 9, Muted);
        }
        Text(c, "L", barX + 3, h - 32, 11, Muted); Text(c, "R", barX + 30, h - 32, 11, Muted);
        if (Frame is not { } f) { Text(c, "等待测量", 115, top + 10, 13, Muted, false); return; }
        void Bar(float x, double rms, double peak)
        {
            float y = bottom - (float)Math.Clamp((rms + 60) / 60, 0, 1) * barHeight;
            using var p = new SKPaint { Color = peak >= 0 ? Alert : Accent.WithAlpha(195) }; c.DrawRect(x, y, 16, bottom - y, p);
            float py = bottom - (float)Math.Clamp((peak + 60) / 60, 0, 1) * barHeight; Line(c, x - 2, py, x + 18, py, Reference, 2);
        }
        Bar(barX, f.Meters.RmsLeft, f.Meters.PeakLeft); Bar(barX + 27, f.Meters.RmsRight, f.Meters.PeakRight);
        float tx = Math.Max(123, w * .32f), right = w - 23;
        float step = Math.Min(42, Math.Max(22, (barHeight - 8) / 6));
        string[] names = ["M · 瞬时", "S · 短时", "I · 累计", "TP · 最大", "RMS · L/R", "动态峰均比"];
        string[] values = [Format(f.Meters.Momentary), Format(f.Meters.ShortTerm), Format(f.Meters.Integrated), Format(Math.Max(f.Meters.TruePeakLeft, f.Meters.TruePeakRight)), $"{Format(f.Meters.RmsLeft)} / {Format(f.Meters.RmsRight)}", Format(f.Meters.CrestDb)];
        for (int i = 0; i < names.Length; i++)
        {
            if (barHeight < 205)
            {
                float compactY = top + 10 + i * Math.Max(13, (barHeight - 15) / 5);
                Text(c, names[i], tx, compactY, 9, Muted, false);
                Text(c, values[i], right, compactY, 12, i == 2 ? Accent : Ink, true, true);
                continue;
            }
            float y = top + 10 + i * step;
            Text(c, names[i], tx, y, 10, Muted, false);
            Text(c, values[i], right, y + Math.Min(19, step * .48f), i < 3 ? 21 : 13, i == 2 ? Accent : Ink, true, true);
            if (step >= 37) Line(c, tx, y + 24, right, y + 24, Grid.WithAlpha(110));
        }
        Text(c, $"{(Math.Max(f.Meters.PeakLeft, f.Meters.PeakRight) >= 0 ? "过载 · Peak" : "Peak")} {Format(f.Meters.PeakLeft)} / {Format(f.Meters.PeakRight)} dBFS", 23, h - 13, 10, Muted);
    }
    public static string Format(double value) => !double.IsFinite(value) || value <= -130 ? "—" : value.ToString("0.0", CultureInfo.InvariantCulture);
    private void Move(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (orbiting) { InputActivity?.Invoke(); OrbitWaterfall(p.X - orbitPointer.X, p.Y - orbitPointer.Y); orbitPointer = p; return; }
        TrackPointer(p);
    }
    internal void TrackPointer(Point p)
    {
        if (Kind is not (InstrumentKind.Spectrum or InstrumentKind.Spectrogram)) return;
        InputActivity?.Invoke();
        var bounds = PlotBounds;
        if (IsWaterfall)
        {
            WaterfallCursor = EnsureWaterfall().Pick(new((float)p.X, (float)p.Y));
            PointerPosition = WaterfallCursor == null ? null : p; CursorHistoryAge = WaterfallCursor?.Age;
            CursorChanged?.Invoke(WaterfallCursor?.Frequency ?? 0);
            if (reviewing) RequestHistory(p);
            if (dragging && WaterfallCursor is { } hit) { bandLow = Math.Min(dragStart, hit.Frequency); bandHigh = Math.Max(dragStart, hit.Frequency); BandChanged?.Invoke(bandLow, bandHigh); }
            return;
        }
        if (!bounds.Contains(p) && !dragging) { PointerPosition = null; CursorChanged?.Invoke(0); return; }
        PointerPosition = p;
        CursorHistoryAge = Kind == InstrumentKind.Spectrogram ? Math.Clamp((p.Y - bounds.Top) / Math.Max(1, bounds.Height), 0, 1) * Settings.HistorySeconds : null;
        double f = MinFrequency * Math.Pow(MaxFrequency / MinFrequency, Math.Clamp((p.X - bounds.Left) / Math.Max(1, bounds.Width), 0, 1));
        CursorChanged?.Invoke(f);
        if (reviewing) RequestHistory(p);
        if (dragging) { bandLow = Math.Min(dragStart, f); bandHigh = Math.Max(dragStart, f); BandChanged?.Invoke(bandLow, bandHigh); }
    }
    internal void RequestHistory(Point p)
    {
        var bounds = PlotBounds;
        if (Kind != InstrumentKind.Spectrogram || !bounds.Contains(p)) return;
        if (IsWaterfall)
        {
            if (EnsureWaterfall().Pick(new((float)p.X, (float)p.Y)) is { } hit) ReviewRequested?.Invoke(hit.Age, hit.Frequency);
            return;
        }
        double age = (p.Y - bounds.Top) / Math.Max(1, bounds.Height) * Settings.HistorySeconds;
        double frequency = MinFrequency * Math.Pow(MaxFrequency / MinFrequency, (p.X - bounds.Left) / Math.Max(1, bounds.Width));
        ReviewRequested?.Invoke(age, frequency);
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { dragging = reviewing = orbiting = false; ReleaseMouseCapture(); ExpandRequested?.Invoke(Kind); e.Handled = true; return; }
        if (Kind is not (InstrumentKind.Spectrum or InstrumentKind.Spectrogram)) return;
        var p = e.GetPosition(this); if (!plot.Contains((float)p.X, (float)p.Y)) return;
        if (IsWaterfall && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { orbiting = true; orbitPointer = p; CaptureMouse(); e.Handled = true; return; }
        if (Kind == InstrumentKind.Spectrogram && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            reviewing = true; CaptureMouse(); TrackPointer(p); e.Handled = true; return;
        }
        double startFrequency = Frequency(p.X);
        if (IsWaterfall)
        {
            if (EnsureWaterfall().Pick(new((float)p.X, (float)p.Y)) is not { } hit) return;
            startFrequency = hit.Frequency;
        }
        dragging = true; dragStart = startFrequency; bandLow = bandHigh = dragStart; CaptureMouse(); e.Handled = true;
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (orbiting) { orbiting = false; ReleaseMouseCapture(); e.Handled = true; return; }
        if (reviewing) { reviewing = false; ReleaseMouseCapture(); e.Handled = true; return; }
        if (!dragging) return; dragging = false; ReleaseMouseCapture();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && bandHigh / Math.Max(1, bandLow) > 1.03) RangeChanged?.Invoke(bandLow, bandHigh);
        e.Handled = true;
    }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (Kind is not (InstrumentKind.Spectrum or InstrumentKind.Spectrogram)) return;
        double anchor = IsWaterfall ? EnsureWaterfall().Pick(new((float)e.GetPosition(this).X, (float)e.GetPosition(this).Y))?.Frequency ?? Math.Sqrt(MinFrequency * MaxFrequency) : Frequency(e.GetPosition(this).X), factor = e.Delta > 0 ? .8 : 1.25;
        double min = anchor * Math.Pow(MinFrequency / anchor, factor), max = anchor * Math.Pow(MaxFrequency / anchor, factor);
        if (max / min < 1.1) return;
        RangeChanged?.Invoke(Math.Max(10, min), Math.Min((Frame?.SampleRate ?? 48000) / 2.0, max)); e.Handled = true;
    }
    public void Dispose() { if (disposed) return; disposed = true; ClearWaterfall(); gpu?.Dispose(); history?.Dispose(); mono.Dispose(); ui.Dispose(); }
}
