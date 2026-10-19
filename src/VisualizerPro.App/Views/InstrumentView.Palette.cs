using SkiaSharp;

namespace VisualizerPro.App.Views;

public sealed partial class InstrumentView
{
    private void DrawFrequencyLegend(SKCanvas c, float w, float h)
    {
        if (!Display.FrequencyColors || w < 920)
        {
            Text(c, "Hz", w - 23, h - 15, 10, Muted, true, true);
            return;
        }
        float x = w - 438;
        var colors = FrequencyPalette.GetColors(Display.FrequencyPalette);
        for (int i = 0; i < colors.Length; i++)
        {
            using var paint = new SKPaint { Color = colors[i], IsAntialias = true };
            c.DrawCircle(x, h - 19, 2.5f, paint);
            Text(c, FrequencyPalette.Labels[i], x + 8, h - 15, 9, Muted);
            x += i == 3 ? 68 : 86;
        }
    }
}
