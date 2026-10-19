using System.Windows;
using System.Windows.Media;
using SkiaSharp;
using VisualizerPro.Core;

namespace VisualizerPro.App.Views;

public sealed partial class InstrumentView
{
    public Func<IReadOnlyList<AnalysisFrame>>? HistoryFramesProvider { get; set; }
    public event Action<DisplaySettings>? WaterfallCameraChanged;
    internal bool IsWaterfall => Kind == InstrumentKind.Spectrogram && HistoryMode == HistoryDisplayMode.Waterfall;
    internal WaterfallHit? WaterfallCursor { get; private set; }
    private WaterfallMesh? waterfall;
    private object? waterfallKey;
    private double waterfallStart;
    private bool orbiting;
    private Point orbitPointer;

    private void ClearWaterfall()
    {
        waterfall?.Dispose(); waterfall = null; waterfallKey = null; waterfallStart = 0; WaterfallCursor = null;
    }

    internal WaterfallMesh EnsureWaterfall()
    {
        var source = HistoryFramesProvider?.Invoke() ?? Array.Empty<AnalysisFrame>();
        double span = Math.Min(Settings.HistorySeconds, Display.WaterfallSeconds);
        double maxStart = Math.Max(0, Settings.HistorySeconds - span);
        if (SelectedHistoryAge is { } selected)
        {
            if (selected < waterfallStart) waterfallStart = Math.Max(0, selected - span * .1);
            if (selected > waterfallStart + span) waterfallStart = Math.Min(maxStart, selected - span * .9);
        }
        else waterfallStart = 0;
        waterfallStart = Math.Clamp(waterfallStart, 0, maxStart);
        var b = PlotBounds;
        var bounds = new SKRect((float)b.Left, (float)b.Top, (float)b.Right, (float)b.Bottom);
        object key = (source.Count, source.Count == 0 ? -1 : source[^1].Time, bounds, MinFrequency, MaxFrequency, waterfallStart, span, Settings.CeilingDb, Settings.SpectrogramRange, Settings.TiltDbPerOctave, Display.WaterfallAzimuth, Display.WaterfallElevation, Display.FrequencyColors, Display.FrequencyPalette, Display.SpectrogramIntensityColors);
        if (waterfall != null && key.Equals(waterfallKey)) return waterfall;
        var previous = waterfall; previous?.Dispose();
        waterfall = new(source, bounds, MinFrequency, MaxFrequency, waterfallStart, span, Settings, Display, previous);
        waterfallKey = key;
        return waterfall;
    }

    private void DrawWaterfall(SKCanvas c, float w, float h)
    {
        plot = new(49, 78, w - 23, h - 71);
        var mesh = EnsureWaterfall();
        var backLeft = mesh.Project(0, mesh.StartAge + mesh.Span, mesh.Floor);
        for (int i = 0; i <= 4; i++)
        {
            double age = mesh.StartAge + mesh.Span * i / 4;
            var a = mesh.Project(0, age, mesh.Floor); var b = mesh.Project(1, age, mesh.Floor);
            Line(c, a.X, a.Y, b.X, b.Y, Grid);
            if (i > 0 || mesh.StartAge > 0) Text(c, $"−{age:0.#}s", b.X - 3, b.Y + (i == 0 ? -4 : 13), 9, Muted, true, true);
            double db = mesh.Floor + (mesh.Ceiling - mesh.Floor) * i / 4;
            var z = mesh.Project(0, mesh.StartAge + mesh.Span, db); var right = mesh.Project(1, mesh.StartAge + mesh.Span, db);
            Line(c, z.X, z.Y, right.X, right.Y, Grid.WithAlpha(120));
            Text(c, $"{db:0}", z.X - 7, z.Y + 3, 9, Muted, true, true);
        }
        Line(c, backLeft.X, backLeft.Y, backLeft.X, mesh.Project(0, mesh.StartAge + mesh.Span, mesh.Ceiling).Y, Muted.WithAlpha(130));
        Text(c, "dBFS", backLeft.X - 7, mesh.Project(0, mesh.StartAge + mesh.Span, mesh.Ceiling).Y - 8, 9, Muted, true, true);
        double[] ticks = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000]; float previousLabel = -100;
        foreach (double f in ticks)
        {
            if (f < MinFrequency || f > MaxFrequency) continue;
            double u = Math.Log(f / MinFrequency) / Math.Log(MaxFrequency / MinFrequency);
            var a = mesh.Project(u, mesh.StartAge, mesh.Floor); var b = mesh.Project(u, mesh.StartAge + mesh.Span, mesh.Floor);
            Line(c, a.X, a.Y, b.X, b.Y, Grid);
            if (a.X - previousLabel > 33)
            {
                Text(c, f >= 1000 ? $"{f / 1000:0.#}k" : $"{f:0}", a.X - 5, a.Y + 20, 9, Muted); previousLabel = a.X;
            }
        }
        c.Save(); c.ClipRect(new SKRect(18, 63, w - 15, h - 47));
        mesh.Draw(c);
        if (SelectedHistoryAge is { } ageSelected && HistoryFrameAtAge?.Invoke(ageSelected) is { } selected)
        {
            using var path = new SKPath(); var points = mesh.Slice(selected, ageSelected);
            path.AddPoly(points, false);
            using var paint = new SKPaint { Color = Accent, StrokeWidth = 1.6f, IsAntialias = true, Style = SKPaintStyle.Stroke }; c.DrawPath(path, paint);
        }
        c.Restore();
        Text(c, "Alt+拖动：视角 · 拖动：回看", 49, h - 39, 9, Muted, false);
        if ((HistoryFramesProvider?.Invoke().Count ?? 0) == 0) Empty(c, "等待音频信号", plot);
    }

    internal void OrbitWaterfall(double dx, double dy)
    {
        WaterfallCursor = null;
        var next = Display with { WaterfallAzimuth = Math.Clamp(Display.WaterfallAzimuth + dx * .15, -50, 50), WaterfallElevation = Math.Clamp(Display.WaterfallElevation - dy * .15, 15, 65) };
        if (WaterfallCameraChanged == null) { Display = next; InvalidateVisual(); } else WaterfallCameraChanged(next);
    }

    internal void DrawWaterfallProbe(DrawingContext context, Brush blue, Pen pen, Typeface font)
    {
        var mesh = EnsureWaterfall();
        if (WaterfallCursor is { } hit)
        {
            double u = Math.Log(hit.Frequency / MinFrequency) / Math.Log(MaxFrequency / MinFrequency);
            var floor = mesh.Project(u, hit.Age, mesh.Floor);
            context.DrawLine(pen, new Point(floor.X, floor.Y), new Point(hit.Position.X, hit.Position.Y));
            context.DrawEllipse(blue, null, new Point(hit.Position.X, hit.Position.Y), 3, 3);
            string reading = $"−{hit.Age:0.00}s   {hit.Frequency:0.0} Hz   {hit.LevelDbfs:0.0} dBFS";
            var text = new FormattedText(reading, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 11, blue, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double x = Math.Clamp(hit.Position.X + 12, PlotBounds.Left, Math.Max(PlotBounds.Left, PlotBounds.Right - text.Width));
            context.DrawText(text, new Point(x, PlotBounds.Top - 19));
        }
        else if (CursorFrequency >= MinFrequency && CursorFrequency <= MaxFrequency)
        {
            double u = Math.Log(CursorFrequency / MinFrequency) / Math.Log(MaxFrequency / MinFrequency);
            var a = mesh.Project(u, mesh.StartAge, mesh.Floor); var b = mesh.Project(u, mesh.StartAge + mesh.Span, mesh.Floor);
            context.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
        }
    }
}
