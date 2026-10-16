using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using VisualizerPro.App.Services;

namespace VisualizerPro.App.Views;

public sealed class OverviewView : SKElement, IDisposable
{
    private SKBitmap? bitmap;
    private FileOverview? overview;
    private readonly SKTypeface face = SKTypeface.FromFamilyName("Cascadia Mono") ?? SKTypeface.Default;
    public double Position { get; set; }
    public double Duration { get; set; }
    public double? LoopA { get; set; }
    public double? LoopB { get; set; }
    public bool LoopEnabled { get; set; }
    public double Progress { get; set; }
    public string Status { get; set; } = "正在生成整曲概览";
    public event Action<double>? SeekRequested;
    public OverviewView()
    {
        IgnorePixelScaling = false; PaintSurface += Paint;
        MouseLeftButtonDown += (_, e) => { if (Duration > 0) SeekRequested?.Invoke(Math.Clamp(e.GetPosition(this).X / Math.Max(1, ActualWidth), 0, 1) * Duration); e.Handled = true; };
    }
    public void SetOverview(FileOverview? value)
    {
        overview = value; bitmap?.Dispose(); bitmap = null;
        if (value == null || value.Columns.Count == 0) return;
        bitmap = new SKBitmap(value.Columns.Count, 128);
        for (int x = 0; x < value.Columns.Count; x++)
            for (int y = 0; y < 128; y++) bitmap.SetPixel(x, 127 - y, InstrumentView.Heat((value.Columns[x].Spectrum[y] + 90) / 90));
    }
    private void Text(SKCanvas c, string text, float x, float y, SKColor color)
    {
        using var font = new SKFont(face, 10); using var paint = new SKPaint { Color = color, IsAntialias = true }; c.DrawText(text, x, y, font, paint);
    }
    private void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var c = e.Surface.Canvas; c.Scale(e.Info.Width / Math.Max(1f, (float)ActualWidth)); float w = (float)ActualWidth, h = (float)ActualHeight; c.Clear(SKColor.Parse("#111111"));
        if (bitmap != null && overview != null)
        {
            using var image = SKImage.FromBitmap(bitmap); c.DrawImage(image, new SKRect(0, 0, w, h * .65f), new SKSamplingOptions(SKFilterMode.Linear));
            using var path = new SKPath();
            for (int i = 0; i < overview.Columns.Count; i++)
            {
                var col = overview.Columns[i]; float x = (float)(col.Time / Duration) * w;
                float y = h - 3 - (float)Math.Clamp((col.Loudness + 60) / 60, 0, 1) * h * .28f;
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            using var line = new SKPaint { Color = SKColor.Parse("#7DA7E7"), StrokeWidth = 1, IsAntialias = true, Style = SKPaintStyle.Stroke }; c.DrawPath(path, line);
        }
        else
        {
            using var progress = new SKPaint { Color = SKColor.Parse("#303030") }; c.DrawRect(0, 0, (float)Progress * w, h, progress);
            Text(c, $"{Status}  {Progress:P0}", 12, h / 2 + 3, SKColor.Parse("#AAAAAA"));
        }
        if (LoopA is { } a)
        {
            float x = (float)(a / Math.Max(1, Duration)) * w;
            using var p = new SKPaint { Color = SKColor.Parse("#7DA7E7"), StrokeWidth = 1 }; c.DrawLine(x, 0, x, h, p); Text(c, "A", x + 4, 14, p.Color);
            if (LoopB is { } b)
            {
                float bx = (float)(b / Math.Max(1, Duration)) * w;
                if (LoopEnabled) { using var fill = new SKPaint { Color = p.Color.WithAlpha(22) }; c.DrawRect(x, 0, bx - x, h, fill); }
                c.DrawLine(bx, 0, bx, h, p); Text(c, "B", bx + 4, 14, p.Color);
            }
        }
        if (Duration > 0)
        {
            float x = (float)(Position / Duration) * w;
            using var p = new SKPaint { Color = SKColor.Parse("#E6E6E6"), StrokeWidth = 1.5f }; c.DrawLine(x, 0, x, h, p);
        }
    }
    public void Dispose() { bitmap?.Dispose(); face.Dispose(); }
}
