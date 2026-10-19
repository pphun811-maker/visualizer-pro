using System.Windows;
using SkiaSharp;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private async Task CheckDetailedSpectrogram(string directory)
    {
        var frames = await Task.Run(() =>
        {
            const int rate = 192000, count = rate / 20;
            var analyzer = new Analyzer(rate, includeSpectrogram: true);
            var result = new List<AnalysisFrame>(); var random = new Random(1984);
            for (int block = 0; block < 200; block++)
            {
                var samples = new float[count * 2];
                for (int i = 0; i < count; i++)
                {
                    double t = (block * count + i) / (double)rate;
                    double beat = Math.Pow(Math.Max(0, Math.Sin(t * 2 * Math.PI * 1.7)), 4);
                    double bass = (.07 + .13 * beat) * (Math.Sin(2 * Math.PI * 56 * t) + .6 * Math.Sin(2 * Math.PI * 64 * t));
                    double body = (.02 + .06 * beat) * Math.Sin(2 * Math.PI * 220 * t + 12 * Math.Sin(t * 1.7));
                    double transient = beat * (.025 * Math.Sin(2 * Math.PI * 1700 * t) + .015 * Math.Sin(2 * Math.PI * 6200 * t));
                    samples[2 * i] = (float)(bass + body + transient + .035 * (random.NextDouble() * 2 - 1));
                    samples[2 * i + 1] = (float)(bass + body + transient + .035 * (random.NextDouble() * 2 - 1));
                }
                var frame = analyzer.Add(samples);
                if (frame != null) result.Add(frame);
            }
            return result;
        });
        Check(frames.Count == 200 && frames.All(f => f.Spectrogram?.Levels.Length == 2048), "Detailed history must retain 20 Hz snapshots at 192 kHz");
        const int width = 1724, height = 567;
        var plot = new SKRect(16, 28, 1700, 513);
        using var legacy = new SpectrogramRaster(Analyzer.Bins, 200);
        using var detailed = new IntensitySpectrogramRaster(2048, 200);
        foreach (var frame in frames)
        {
            legacy.Append(frame.Spectrum.Select(db => (uint)FrequencyPalette.Heat((db + 80) / 80, 1000, false, intensity: true)).ToArray(), 1);
            detailed.Append(frame.Spectrogram!.Levels, 1);
        }
        double left = Math.Log(20.0 / 10) / Math.Log(96000.0 / 10), right = Math.Log(20000.0 / 10) / Math.Log(96000.0 / 10);
        foreach (bool useDetail in new[] { false, true })
        {
            using var bitmap = new SKBitmap(width, height); using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(23, 23, 23));
            if (useDetail) canvas.DrawImage(detailed.Image((int)plot.Width, (int)plot.Height, left * 2047, right * 2047), plot, new SKSamplingOptions(SKFilterMode.Nearest));
            else
            {
                var image = legacy.Image((int)plot.Height);
                canvas.DrawImage(image, new SKRect((float)(left * 767), 0, (float)(right * 767), image.Height), plot, new SKSamplingOptions(SKFilterMode.Linear));
            }
            using var divider = new SKPaint { Color = new SKColor(0, 0, 0, 165), StrokeWidth = 1 };
            using var font = new SKFont(SKTypeface.FromFamilyName("Cascadia Mono"), 12);
            using var ink = new SKPaint { Color = new SKColor(150, 150, 150), IsAntialias = true };
            foreach (double hz in new[] { 20.0, 30, 50, 100, 200, 300, 500, 1000, 2000, 3000, 5000, 10000, 20000 })
            {
                float x = plot.Left + (float)(Math.Log(hz / 20) / Math.Log(1000)) * plot.Width;
                canvas.DrawLine((float)Math.Floor(x) + .5f, plot.Top, (float)Math.Floor(x) + .5f, plot.Bottom, divider);
                string label = hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
                canvas.DrawText(label, x - font.MeasureText(label) / 2, 545, font, ink);
            }
            using var imageOut = SKImage.FromBitmap(bitmap); using var data = imageOut.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(directory, useDetail ? "sharpness-after.png" : "sharpness-before.png")); data.SaveTo(file);
        }

        // A one-row event remains a one-row edge at 1:1; no RGB bilinear halo.
        using (var raster = new IntensitySpectrogramRaster(8, 200))
        {
            raster.Append(Enumerable.Repeat(-140f, 8).ToArray(), 199);
            raster.Append(Enumerable.Repeat(-60f, 8).ToArray(), 1);
            using var bitmap = SKBitmap.FromImage(raster.Image(8, 200, 0, 7));
            Check(bitmap.GetPixel(3, 0) == FrequencyPalette.Intensity(-60) && bitmap.GetPixel(3, 1) == FrequencyPalette.Intensity(-140), "Temporal edges must not blend into the next physical pixel");
            foreach (int physicalHeight in new[] { 37, 113, 317 })
            {
                using var reduced = SKBitmap.FromImage(raster.Image(8, physicalHeight, 0, 7));
                Check(reduced.GetPixel(3, 0) != FrequencyPalette.Intensity(-140), "Downsampling must retain a brief event");
            }
        }
        // Actual view/probe path, with a raw value different from the FFT curve.
        using var view = new InstrumentView { Kind = InstrumentKind.Spectrogram, Settings = frames[^1].Settings with { HistorySeconds = 10 }, Display = new() { SpectrogramIntensityColors = true } };
        view.EnableSoftware(); view.Measure(new Size(1120, 600)); view.Arrange(new Rect(0, 0, 1120, 600)); view.UpdateLayout();
        foreach (var frame in frames) { view.Frame = frame; view.AcceptHistory(frame); }
        view.HistoryFrameAtAge = _ => frames[^1];
        view.CursorChanged += hz => view.CursorFrequency = hz;
        view.TrackPointer(new Point(view.FrequencyX(60), view.PlotBounds.Top + 1));
        Check(Math.Abs(view.CursorLevelDbfs!.Value - frames[^1].Spectrogram!.At(60)) < .001, "History probe must read the displayed raw spectrum");

        int previousPalette = SpectrogramPaletteCombo.SelectedIndex;
        SpectrogramPaletteCombo.SelectedIndex = 1;
        if (frozen) ToggleFreeze();
        await Task.Delay(500);
        long beforeFrames = Spectrogram.RenderCount; double beforeMs = Spectrogram.RenderMilliseconds;
        var watch = Stopwatch.StartNew(); await Task.Delay(3000);
        long painted = Spectrogram.RenderCount - beforeFrames;
        double fps = painted / watch.Elapsed.TotalSeconds;
        double meanPaintMs = (Spectrogram.RenderMilliseconds - beforeMs) / Math.Max(1, painted);
        Check(fps >= (Spectrogram.Renderer == "OPENGL" ? 30 : 10), $"Detailed live 2D renderer too slow: {fps:F1} FPS");
        Check(engine.Dropped == 0, "Detailed history must not drop audio blocks");
        await File.WriteAllTextAsync(Path.Combine(directory, "sharpness-metrics.json"), System.Text.Json.JsonSerializer.Serialize(new { renderer = Spectrogram.Renderer, framesPerSecond = fps, meanPaintMilliseconds = meanPaintMs, audioDroppedBlocks = engine.Dropped, fixtureSampleRate = 192000, sameSignalComparison = true }, Services.SettingsStore.Json));
        SaveScreenshot(Path.Combine(directory, "detail-live.png"));
        SpectrogramPaletteCombo.SelectedIndex = previousPalette;
    }
}
