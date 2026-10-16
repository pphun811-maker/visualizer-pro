using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    public async Task RunSpectralChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            await WaitFor(() => frameHistory.Count > 155, "Need eight seconds of spectral history");
            await CheckPanelControls(directory);
            checks.Add("PASS panel buttons, 1/2/3/numpad/V shortcuts, text/dropdown guards, frozen data, expanded/focus layouts and F1 guide");
            SpectrumModeCombo.SelectedIndex = 1; HistoryModeCombo.SelectedIndex = 0;
            await Task.Delay(100);
            Check(Spectrum.Frame!.OctaveBands.Length == 31, "Octave readings must be available");
            if (!frozen) ToggleFreeze();
            var held = Spectrum.Frame!; var settings = engine.Settings;
            Spectrum.TrackPointer(new Point(Spectrum.FrequencyX(1000), Spectrum.PlotBounds.Top + Spectrum.PlotBounds.Height * .5));
            var band = held.OctaveBands.Single(b => b.CenterHz == 1000);
            Check(Math.Abs(Spectrum.CursorLevelDbfs!.Value - band.LevelDbfs) < 1e-8, "Band probe must return RMS energy");
            OnSnapshot(this, new RoutedEventArgs());
            var shot = snapshots[^1];
            Check(shot.OctaveBands.Length == 31 && ValidSnapshot(shot), "Snapshot includes octave values");
            var roundTrip = JsonSerializer.Deserialize<SpectrumSnapshot>(JsonSerializer.Serialize(shot, SettingsStore.Json), SettingsStore.Json)!;
            Check(roundTrip.OctaveBands.SequenceEqual(shot.OctaveBands), "Octave snapshot round trip");
            var legacy = new SpectrumSnapshot("OLD", "fixture", DateTime.Now, held.SampleRate, held.Settings, held.Frequencies, held.Spectrum);
            Check(ValidSnapshot(legacy) && legacy.OctaveBands.Length == 0, "Legacy FFT snapshots remain valid without invented octave data");
            Check(!ValidSnapshot(shot with { OctaveBands = [new(1000, 900, 1100, double.NaN, -20, -10)] }), "Malformed band snapshot rejected");
            var csv = OctaveCsv(held);
            Check(csv.StartsWith("CenterHz,LowerHz,UpperHz,RmsDbfs,") && csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 32, "Band CSV units and rows");
            await Task.Delay(80); SaveScreenshot(Path.Combine(directory, "third-octave.png"));
            checks.Add("PASS octave display, frozen RMS probe, reference/legacy validation and unit-labelled CSV");

            var uniform = held with { Spectrum = Enumerable.Repeat(-20f, Analyzer.Bins).ToArray() };
            var flat = Enumerable.Range(0, 201).Select(i => uniform with { Time = i * .05, Sequence = i + 1 }).ToArray();
            using (var mesh = new WaterfallMesh(flat, new(49, 78, 700, 350), 20, 20000, 0, 8, settings with { TiltDbPerOctave = 0 }, new()))
            {
                var p = mesh.Project(.42, 3.25, -20); var hit = mesh.Pick(p) ?? throw new InvalidOperationException("Surface picking failed");
                Check(Math.Abs(hit.Age - 3.25) < .001 && Math.Abs(Math.Log(hit.Frequency / (20 * Math.Pow(1000, .42)))) < .001 && Math.Abs(hit.LevelDbfs + 20) < .001, "3D picking must invert the displayed projection");
                using var empty = new WaterfallMesh([], new(49, 78, 700, 350), 20, 20000, 0, 8, settings, new());
                Check(empty.TriangleCount == 0 && empty.Pick(p) == null, "No surface or hit for missing history");
            }
            var line = Enumerable.Repeat(-140f, Analyzer.Bins).ToArray(); line[700] = -5;
            var narrow = flat.Select(f => f with { Spectrum = line }).ToArray();
            var oldMesh = new WaterfallMesh(narrow, new(49, 78, 700, 350), 20, 20000, 0, 8, settings, new());
            Check(oldMesh.Data.Any(v => v.Valid && v.LevelDbfs > -6), "Frequency reduction must retain a single-bin ridge");
            oldMesh.Dispose();
            using (var nextMesh = new WaterfallMesh(narrow, new(49, 78, 700, 350), 20, 20000, 0, 8, settings, new(), oldMesh))
                Check(ReferenceEquals(oldMesh.Data, nextMesh.Data), "Managed geometry buffers must be reused");
            checks.Add("PASS 3D projection/picking, missing-data gaps, narrow-ridge preservation and geometry buffer reuse");

            HistoryModeCombo.SelectedIndex = 1; await Task.Delay(80);
            var actualMesh = Spectrogram.EnsureWaterfall();
            Check(actualMesh.TriangleCount > 1000 && ReferenceEquals(actualMesh, Spectrogram.EnsureWaterfall()), "Frozen mesh should be cached");
            var front = actualMesh.Data[actualMesh.Columns / 2].Position;
            var point = new Point(front.X, front.Y);
            Spectrogram.TrackPointer(point);
            Check(Spectrogram.WaterfallCursor != null && Spectrogram.CursorLevelDbfs.HasValue, "Inline 3D probe");
            Spectrogram.RequestHistory(point);
            Check(frozen && reviewAge != null && ReferenceEquals(Spectrum.Frame, Stereo.Frame) && ReferenceEquals(Spectrum.Frame, Levels.Frame), "3D click selects synchronized history");
            WaterfallWindowCombo.SelectedIndex = 0;
            HistoryPositionSlider.Value = -6;
            var scrolled = Spectrogram.EnsureWaterfall();
            Check(scrolled.StartAge > 0 && reviewAge >= scrolled.StartAge && reviewAge <= scrolled.StartAge + scrolled.Span, "Old history selection must enter the visible 3D window");
            var selectedFrame = displayFrame;
            Spectrogram.OrbitWaterfall(1000, -1000);
            Check(engine.Display.WaterfallAzimuth == 50 && engine.Display.WaterfallElevation == 65 && ReferenceEquals(selectedFrame, displayFrame), "Camera bounds and held data");
            ResetWaterfallViewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(engine.Display.WaterfallAzimuth == 32 && engine.Display.WaterfallElevation == 35 && engine.Settings == settings, "Camera reset must not change analysis");
            await Task.Delay(100); SaveScreenshot(Path.Combine(directory, "waterfall-review.png"));
            ToggleExpand(InstrumentKind.Spectrogram); await Task.Delay(100);
            SaveScreenshot(Path.Combine(directory, "waterfall-expanded.png")); ToggleExpand(InstrumentKind.Spectrogram);
            checks.Add("PASS frozen 3D probe, synchronized selection, older-window navigation, camera controls and expanded capture");

            WaterfallWindowCombo.SelectedIndex = 1;
            SettingsStore.Save(saved); var loaded = SettingsStore.Load();
            Check(loaded.SpectrumMode == 1 && loaded.HistoryMode == 1 && loaded.Display == saved.Display, "Spectral mode and camera persistence");
            HistoryLiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(400);
            long first = Spectrogram.RenderCount; var watch = Stopwatch.StartNew(); await Task.Delay(3000);
            double fps = (Spectrogram.RenderCount - first) / watch.Elapsed.TotalSeconds;
            Check(fps >= (Spectrogram.Renderer == "OPENGL" ? 30 : 10), $"Waterfall rendering stopped or too slow: {fps:F1} FPS");
            Check(engine.Dropped == 0, "No dropped audio blocks");
            SaveScreenshot(Path.Combine(directory, "live.png"));
            checks.Add("PASS persisted modes/camera, live 3D refresh and zero dropped audio blocks");
            await File.WriteAllTextAsync(Path.Combine(directory, "spectral.json"), JsonSerializer.Serialize(new { success = true, checks, framesPerSecond = fps, renderer = Spectrogram.Renderer, triangles = Spectrogram.EnsureWaterfall().TriangleCount, audioDroppedBlocks = engine.Dropped }, SettingsStore.Json));
        }
        catch (Exception error)
        { await File.WriteAllTextAsync(Path.Combine(directory, "spectral.json"), JsonSerializer.Serialize(new { success = false, checks, error = error.ToString() }, SettingsStore.Json)); }
        finally { Close(); }
    }
}
