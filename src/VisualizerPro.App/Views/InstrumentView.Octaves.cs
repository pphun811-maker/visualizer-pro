using SkiaSharp;
using VisualizerPro.Core;

namespace VisualizerPro.App.Views;

public enum SpectrumDisplayMode { Fft, ThirdOctave }
public enum HistoryDisplayMode { Heatmap, Waterfall }

public sealed partial class InstrumentView
{
    private SpectrumDisplayMode spectrumMode;
    public SpectrumDisplayMode SpectrumMode { get => spectrumMode; set { spectrumMode = value; probe.InvalidateVisual(); } }
    private HistoryDisplayMode historyMode;
    public HistoryDisplayMode HistoryMode { get => historyMode; set { historyMode = value; WaterfallCursor = null; probe.InvalidateVisual(); } }
    internal OctaveBandReading? CursorOctaveBand => Frame == null ? null : OctaveAt(Frame.OctaveBands, CursorFrequency);

    internal static OctaveBandReading? OctaveAt(OctaveBandReading[] bands, double frequency)
    {
        foreach (var band in bands) if (frequency >= band.LowHz && frequency <= band.HighHz) return band;
        return null;
    }

    private void DrawOctaves(SKCanvas c, float w, float h)
    {
        plot = new(49, 82, w - 23, h - 61);
        for (double db = Settings.CeilingDb; db >= Settings.FloorDb; db -= 20)
        {
            float y = Y(db); Line(c, plot.Left, y, plot.Right, y, Grid); Text(c, $"{db:0}", 37, y + 3, 10, Muted, true, true);
        }
        if (Frame is not { OctaveBands.Length: > 0 } frame) { Empty(c, "等待音频信号", plot); return; }
        float previous = -100;
        foreach (var band in frame.OctaveBands)
        {
            if (band.CenterHz < MinFrequency || band.CenterHz > MaxFrequency) continue;
            float x = X(band.CenterHz);
            if (x - previous > 38)
            {
                string label = band.CenterHz >= 1000 ? $"{band.CenterHz / 1000:0.##}k" : $"{band.CenterHz:0.#}";
                Text(c, label, x - 7, plot.Bottom + 21, 10, Muted); previous = x;
            }
        }
        c.Save(); c.ClipRect(plot);
        foreach (var band in frame.OctaveBands)
        {
            if (band.HighHz < MinFrequency || band.LowHz > MaxFrequency) continue;
            float left = X(Math.Max(MinFrequency, band.LowHz)), right = X(Math.Min(MaxFrequency, band.HighHz));
            float gap = Math.Min(2, (right - left) * .09f);
            float y = Y(band.LevelDbfs + Settings.TiltDbPerOctave * Math.Log2(band.CenterHz / 1000));
            bool resolved = band.HighHz - band.LowHz >= 2.0 * frame.SampleRate / frame.Settings.FftSize;
            var color = Display.FrequencyColors ? FrequencyPalette.At(band.CenterHz, Display.FrequencyPalette) : Accent;
            using var fill = new SKPaint { Color = color.WithAlpha((byte)(resolved ? 195 : 100)), IsAntialias = true };
            c.DrawRect(new SKRect(left + gap, y, Math.Max(left + gap, right - gap), plot.Bottom), fill);
            if (ShowAverage) Line(c, left + gap, Y(band.AverageDbfs + Settings.TiltDbPerOctave * Math.Log2(band.CenterHz / 1000)), right - gap, Y(band.AverageDbfs + Settings.TiltDbPerOctave * Math.Log2(band.CenterHz / 1000)), Reference, 1.3f);
            if (ShowPeaks) Line(c, left + gap, Y(band.PeakDbfs + Settings.TiltDbPerOctave * Math.Log2(band.CenterHz / 1000)), right - gap, Y(band.PeakDbfs + Settings.TiltDbPerOctave * Math.Log2(band.CenterHz / 1000)), Ink.WithAlpha(150), 1);
        }
        for (int i = 0; i < Snapshots.Count; i++)
        {
            var bands = Snapshots[i].OctaveBands;
            if (bands.Length == 0) continue;
            Trace(c, bands.Select(b => b.CenterHz).ToArray(), bands.Select(b => (float)b.LevelDbfs).ToArray(), SnapshotColors[i % 4], 1.3f, dashes: SnapshotDashes[i % 4]);
        }
        c.Restore();
        Text(c, Snapshots.Any(s => s.OctaveBands.Length == 0) ? "频带 RMS · 旧快照无倍频程数据" : "频带 RMS · 淡色柱：FFT 分辨率不足", 49, h - 15, 10, Muted, false);
        DrawFrequencyLegend(c, w, h);
    }
}
