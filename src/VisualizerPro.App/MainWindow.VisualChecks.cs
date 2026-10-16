using System.Text.Json;
using SkiaSharp;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    public async Task RunVisualChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        var rasterResults = new List<object>();
        try
        {
            await Task.Delay(1200);
            await WaitFor(() => displayFrame != null, "Visual checks need a frame");
            ToggleFreeze();
            var fixture = displayFrame!;
            foreach (int height in new[] { 173, 317, 901 })
            {
                using var raster = new SpectrogramRaster(1, 1200);
                raster.Append([0xFF111111], 1200);
                raster.Append([0xFF7DA7E7], 1);
                var brightness = new List<double>();
                var oldBrightness = new List<double>();
                for (int age = 0; age < 60; age++)
                {
                    using var bitmap = SKBitmap.FromImage(raster.Image(height));
                    double total = 0, oldTotal = 0;
                    for (int y = 0; y < height; y++)
                    {
                        total += bitmap.GetPixel(0, y).Blue - 17;
                        double position = (y + .5) * 1200 / height - .5;
                        oldTotal += 214 * Math.Max(0, 1 - Math.Abs(position - age));
                    }
                    brightness.Add(total * 1200 / height); oldBrightness.Add(oldTotal * 1200 / height);
                    raster.Append([0xFF111111], 1);
                }
                double min = brightness.Min(), max = brightness.Max();
                Check(min > 214 * .9 && max < 214 * 1.1, $"Thin-line brightness at {height}px: {min:F2}..{max:F2}");
                rasterResults.Add(new { physicalHeight = height, areaMin = min, areaMax = max, linearMin = oldBrightness.Min(), linearMax = oldBrightness.Max() });
            }
            checks.Add("PASS moving single-row lines preserve integrated brightness at 173/317/901 physical pixels, 60-second history");

            using (var a = new SpectrogramRaster(1, 200))
            using (var b = new SpectrogramRaster(1, 200))
            {
                a.Append([0xFF111111], 200); b.Append([0xFF111111], 313);
                // Append advances up to capacity by design. Different physical
                // heads with identical logical content exercise the wrap seam.
                b.Append([0xFF111111], 113);
                a.Append([0xFF7DA7E7], 1); b.Append([0xFF7DA7E7], 1);
                for (int i = 0; i < 220; i++)
                {
                    using var ia = SKBitmap.FromImage(a.Image(137)); using var ib = SKBitmap.FromImage(b.Image(137));
                    Check(ia.Bytes.SequenceEqual(ib.Bytes), "Physical ring head must not change raster output");
                    a.Append([0xFF111111], 1); b.Append([0xFF111111], 1);
                }
            }
            checks.Add("PASS ring wrapping, expiration and repeated cached paints have identical chronological output");

            var state = new StereoDisplay();
            AnalysisFrame Phase(double time, float level, double rms = -30) => fixture with
            {
                Time = time, PhaseLeft = [level, -level], PhaseRight = [level, -level],
                Meters = fixture.Meters with { RmsLeft = rms, RmsRight = rms }
            };
            var quiet = Phase(1, .01f); state.Accept(quiet);
            for (int i = 1; i <= 60; i++) state.Accept(Phase(1 + i / 60.0, .01f));
            double oldGain = state.Gain;
            state.Accept(Phase(2 + 1.0 / 60, 1));
            double limitedGain = state.Gain;
            Check(limitedGain * Math.Sqrt(2) <= .820001, "Loud transient must fit the circle");
            state.Accept(Phase(2 + 2.0 / 60, .01f));
            Check(state.Gain - limitedGain < .06, "Gain must recover slowly");
            var repeated = state.Traces[^1].Frame; double gain = state.Gain; int traceCount = state.Traces.Count;
            for (int i = 0; i < 120; i++) state.Accept(repeated);
            Check(state.Gain == gain && state.Traces.Count == traceCount, "Repainting/freeze must not advance gain or trails");
            state.Accept(Phase(.1, .01f)); Check(state.Traces.Count == 1, "Seek resets trails");
            state.Reset(); Check(state.Traces.Count == 0 && state.Gain == 1, "Source clear resets gain");
            checks.Add("PASS stereo transient containment, slow gain recovery, freeze and seek reset");

            // Render dense thin history after more than a full ring rotation.
            ApplyControls(engine.Settings with { HistorySeconds = 60 }); Spectrogram.ResetHistory(); Spectrogram.Frozen = false;
            for (int i = 0; i < 1300; i++)
            {
                var values = new float[Analyzer.Bins]; Array.Fill(values, -110);
                if (i % 19 == 0) Array.Fill(values, -20);
                for (int bin = 0; bin < values.Length; bin++) if (bin % 61 == 0) values[bin] = -12;
                var row = fixture with { Time = i * .05, Sequence = i + 1, Spectrum = values };
                Spectrogram.Frame = row; Spectrogram.AcceptHistory(row);
            }
            SaveScreenshot(Path.Combine(directory, "thin-lines.png"));
            checks.Add("PASS dense thin-line history exported after ring wrap");
            ToggleFreeze();
            await Task.Delay(500);
            long firstPaint = Spectrum.RenderCount;
            var watch = Stopwatch.StartNew();
            await Task.Delay(5000);
            double fps = (Spectrum.RenderCount - firstPaint) / watch.Elapsed.TotalSeconds;
            Check(fps >= 30, $"60-second history should keep live refresh: {fps:F1} FPS");
            Check(engine.Dropped == 0, "No dropped audio blocks during visual checks");
            checks.Add("PASS live 60-second history refresh above 30 FPS with no dropped audio blocks (5-second sample)");
            SaveScreenshot(Path.Combine(directory, "live.png"));
            await File.WriteAllTextAsync(Path.Combine(directory, "visual.json"), JsonSerializer.Serialize(new { success = true, checks, rasterResults, stereoQuietGain = oldGain, stereoTransientGain = limitedGain, liveFramesPerSecond = fps, audioDroppedBlocks = engine.Dropped }, SettingsStore.Json));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "visual.json"), JsonSerializer.Serialize(new { success = false, checks, error = error.ToString() }, SettingsStore.Json));
        }
        finally { Close(); }
    }
}
