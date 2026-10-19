using SkiaSharp;
using VisualizerPro.Core;

namespace VisualizerPro.App.Views;

// Fixed frequency bands, shared by every frequency-based instrument.
// In frequency-band styles, level changes brightness while hue stays tied to Hz.
internal static class FrequencyPalette
{
    internal static readonly double[] Edges = [20, 120, 500, 2000, 6000, 20000];
    internal static readonly string[] Labels = ["20–120", "120–500", "500–2k", "2k–6k", "6k–20k Hz"];
    private static readonly SKColor[] SoftColors = [SKColor.Parse("#829EC6"), SKColor.Parse("#7EADA7"), SKColor.Parse("#A6B58C"), SKColor.Parse("#C2AE87"), SKColor.Parse("#B39CAF")];
    private static readonly SKColor[] StandardColors = [SKColor.Parse("#4C91D9"), SKColor.Parse("#36B1B5"), SKColor.Parse("#75B65B"), SKColor.Parse("#DEA04C"), SKColor.Parse("#AC80CC")];
    private static readonly SKColor Background = new(17, 17, 17);
    // Sampled from the reference JPEG scale at 10 dB intervals, -130 through 0.
    // Median of the brightest 15% of text interiors avoids black antialiasing.
    private static readonly SKColor[] IntensityStops = [SKColor.Parse("#050F09"), SKColor.Parse("#0D2921"), SKColor.Parse("#1B3A47"), SKColor.Parse("#34476E"), SKColor.Parse("#594E8D"), SKColor.Parse("#7C569D"), SKColor.Parse("#A4619E"), SKColor.Parse("#C3769A"), SKColor.Parse("#D29097"), SKColor.Parse("#D2AD9C"), SKColor.Parse("#D6CBAF"), SKColor.Parse("#DDE3C4"), SKColor.Parse("#EDF2E8"), SKColor.Parse("#FFFFFF")];

    internal static SKColor[] GetColors(FrequencyPaletteKind style) => style == FrequencyPaletteKind.Standard ? StandardColors : SoftColors;
    internal static SKColor Intensity(double db) => Heat((db + 130) / 130, 1000, false, intensity: true);

    internal static SKColor At(double frequency, FrequencyPaletteKind style = FrequencyPaletteKind.Soft)
    {
        int band = frequency < 120 ? 0 : frequency < 500 ? 1 : frequency < 2000 ? 2 : frequency < 6000 ? 3 : 4;
        return GetColors(style)[band];
    }

    internal static SKColor Heat(double amount, double frequency, bool multicolor, FrequencyPaletteKind style = FrequencyPaletteKind.Soft, bool intensity = false)
    {
        if (intensity)
        {
            double at = Math.Clamp(amount, 0, 1) * (IntensityStops.Length - 1);
            int low = Math.Min((int)at, IntensityStops.Length - 2);
            var a = IntensityStops[low]; var b = IntensityStops[low + 1]; double t = at - low;
            return new((byte)Math.Round(a.Red + (b.Red - a.Red) * t), (byte)Math.Round(a.Green + (b.Green - a.Green) * t), (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * t));
        }
        if (!multicolor) return InstrumentView.Heat(amount);
        double level = Math.Clamp(amount, 0, 1);
        var hue = At(frequency, style);
        return new((byte)Math.Round(Background.Red + (hue.Red - Background.Red) * level),
            (byte)Math.Round(Background.Green + (hue.Green - Background.Green) * level),
            (byte)Math.Round(Background.Blue + (hue.Blue - Background.Blue) * level));
    }

    internal static SKShader Shader(float left, float right, double minimum, double maximum, byte alpha = 255, FrequencyPaletteKind style = FrequencyPaletteKind.Soft)
    {
        var palette = GetColors(style);
        var colors = new List<SKColor> { At(minimum, style).WithAlpha(alpha) };
        var positions = new List<float> { 0 };
        double span = Math.Log(maximum / minimum);
        // Narrow, fixed transitions around boundaries avoid hard seams in FFT curves.
        for (int i = 1; i < Edges.Length - 1; i++)
        {
            double low = Edges[i] / 1.06, high = Edges[i] * 1.06;
            if (high <= minimum || low >= maximum) continue;
            float a = (float)Math.Clamp(Math.Log(low / minimum) / span, 0, 1);
            float b = (float)Math.Clamp(Math.Log(high / minimum) / span, 0, 1);
            colors.Add(palette[i - 1].WithAlpha(alpha)); positions.Add(a);
            colors.Add(palette[i].WithAlpha(alpha)); positions.Add(b);
        }
        colors.Add(At(maximum, style).WithAlpha(alpha)); positions.Add(1);
        return SKShader.CreateLinearGradient(new(left, 0), new(right, 0), colors.ToArray(), positions.ToArray(), SKShaderTileMode.Clamp);
    }
}
