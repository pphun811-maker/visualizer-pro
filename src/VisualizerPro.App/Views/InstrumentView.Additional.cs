using SkiaSharp;

namespace VisualizerPro.App.Views;

public enum StereoDisplayMode { Field, Bands, Waveform }

public sealed partial class InstrumentView
{
    private void DrawStereoBands(SKCanvas c, float w, float h)
    {
        if (Frame is not { StereoBands.Length: > 0 } f) { Empty(c, "等待音频信号", new(20, 80, w - 20, h - 30)); return; }
        float top = 100, step = Math.Max(18, (h - 135) / 5);
        float barLeft = Math.Max(103, w * .34f), barRight = w - 127, mid = (barLeft + barRight) / 2;
        Text(c, "Hz", 22, 82, 9, Muted);
        Text(c, "相关度", barLeft, 82, 9, Muted, false);
        Text(c, "S 占比", w - 22, 82, 9, Muted, false, true);
        for (int i = 0; i < f.StereoBands.Length; i++)
        {
            var band = f.StereoBands[i]; float y = top + i * step;
            var color = Display.FrequencyColors ? FrequencyPalette.At(Math.Sqrt(band.LowHz * band.HighHz), Display.FrequencyPalette) : Accent;
            string Hz(double value) => value >= 1000 ? $"{value / 1000:0.#}k" : $"{value:0}";
            Text(c, $"{Hz(band.LowHz)}–{Hz(band.HighHz)}", 22, y + 4, 10, Display.FrequencyColors ? color : Ink);
            Line(c, barLeft, y, barRight, y, Grid, 4);
            Line(c, mid, y - 5, mid, y + 5, Muted);
            if (double.IsFinite(band.Correlation))
            {
                float x = barLeft + (float)((band.Correlation + 1) / 2) * (barRight - barLeft);
                using var p = new SKPaint { Color = Display.FrequencyColors ? color : band.Correlation < 0 ? Reference : Accent, IsAntialias = true }; c.DrawCircle(x, y, 3, p);
            }
            Text(c, double.IsFinite(band.Correlation) ? $"{band.Correlation:+0.00;-0.00;0.00}" : "—", w - 69, y + 4, 10, Ink, true, true);
            Text(c, double.IsFinite(band.SideFraction) ? $"{band.SideFraction:P0}" : "—", w - 22, y + 4, 11, color, true, true);
        }
        Text(c, "相关度 −1 … +1   S 占比 = S / (M + S)", 22, h - 14, 9, Muted, false);
    }

    // Pick a real rising zero crossing; use the same index for both channels.
    internal static int WaveformStart(float[] left, int count)
    {
        int limit = left.Length - count;
        if (limit <= 0) return 0;
        float peak = left.Select(Math.Abs).DefaultIfEmpty().Max();
        if (peak < 1e-5) return limit;
        for (int i = 1; i <= limit; i++) if (left[i - 1] <= 0 && left[i] > 0) return i;
        return limit;
    }

    private void DrawWaveform(SKCanvas c, float w, float h)
    {
        var area = new SKRect(49, 82, w - 23, h - 43);
        if (Frame is not { PhaseLeft.Length: > 0 } f) { Empty(c, "等待音频信号", area); return; }
        int count = Math.Clamp((int)(Display.WaveformMs / 1000 * f.SampleRate), 2, f.PhaseLeft.Length);
        int start = WaveformStart(f.PhaseLeft, count);
        float gap = 18, half = Math.Max(10, (area.Height - gap) / 2);
        for (int channel = 0; channel < 2; channel++)
        {
            float center = area.Top + half / 2 + channel * (half + gap), radius = half / 2;
            Text(c, channel == 0 ? "L" : "R", 22, center + 3, 11, Ink);
            Line(c, area.Left, center - radius, area.Right, center - radius, Grid);
            Line(c, area.Left, center, area.Right, center, Grid);
            Line(c, area.Left, center + radius, area.Right, center + radius, Grid);
            using var restore = new SKAutoCanvasRestore(c, true); c.ClipRect(new(area.Left, center - radius, area.Right, center + radius));
            var samples = channel == 0 ? f.PhaseLeft : f.PhaseRight;
            using var path = new SKPath();
            // Min/max buckets preserve narrow impulses when many samples share
            // one column. Sparse data is drawn as the actual sample sequence.
            int columns = Math.Max(1, (int)area.Width);
            if (count <= columns)
            {
                for (int i = 0; i < count; i++)
                {
                    float x = area.Left + area.Width * i / (count - 1), y = center - samples[start + i] * radius;
                    if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
                }
            }
            else
            {
                for (int x = 0; x < columns; x++)
                {
                    int from = start + x * count / columns, to = start + (x + 1) * count / columns;
                    float low = float.MaxValue, high = float.MinValue;
                    for (int i = from; i < to; i++) { low = Math.Min(low, samples[i]); high = Math.Max(high, samples[i]); }
                    float px = area.Left + (x + .5f) * area.Width / columns;
                    if (x == 0) path.MoveTo(px, center - samples[from] * radius); else path.LineTo(px, center - samples[from] * radius);
                    path.LineTo(px, center - low * radius); path.LineTo(px, center - high * radius); path.LineTo(px, center - samples[to - 1] * radius);
                }
            }
            using var paint = new SKPaint { Color = channel == 0 ? Accent : SKColor.Parse("#A7BFE3"), StrokeWidth = 1.15f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            c.DrawPath(path, paint);
        }
        Text(c, "0", area.Left, h - 20, 9, Muted); Text(c, $"{1000.0 * (count - 1) / f.SampleRate:0.#} ms", area.Right, h - 20, 9, Muted, true, true);
        Text(c, "±1 FS · 上升沿触发", 49, h - 4, 9, Muted, false);
    }
}
