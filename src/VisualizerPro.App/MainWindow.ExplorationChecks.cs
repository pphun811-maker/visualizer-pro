using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    public async Task RunExplorationChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>(); var rates = new List<object>();
        try
        {
            await Task.Delay(3000);
            await WaitFor(() => frameHistory.Count >= 40, "Need two seconds of history");
            Check(HistoryPositionSlider.IsEnabled, "History controls must refresh independently of capture/render cadence");
            Thread.Sleep(180); // Deliberately pause UI consumption, not DSP.
            await Task.Delay(150);
            double widestGap = frameHistory.Frames.Zip(frameHistory.Frames.Skip(1), (a, b) => b.Time - a.Time).Max();
            Check(widestGap <= .050001, $"Brief UI stall must recover buffered analysis frames: {widestGap:F4}s gap");
            double oldest = frameHistory.Frames[0].Time;
            int rowsBefore = Spectrogram.HistoryRasterRows;
            ToggleFreeze(); var held = displayFrame; var anchor = VisibleHistory[^1].Time;
            int heldCount = VisibleHistory.Count;
            await Task.Delay(700);
            Check(ReferenceEquals(held, displayFrame) && VisibleHistory.Count == heldCount && VisibleHistory[^1].Time == anchor, "Frozen display and history anchor must hold");
            Check(frameHistory.LatestTime > anchor + .4, "History capture must continue during freeze");
            checks.Add("PASS frozen readers stay stable while bounded live history continues");

            var bounds = Spectrogram.PlotBounds;
            var point = new Point(bounds.Left + bounds.Width * .45, bounds.Top + bounds.Height * 1.0 / engine.Settings.HistorySeconds);
            Spectrogram.TrackPointer(point); Spectrogram.RequestHistory(point);
            Check(frozen && reviewAge is > .9 and < 1.1, "Select the requested history time from panel coordinates");
            var selected = FrameHistory.AtAge(VisibleHistory, 1)!;
            Check(ReferenceEquals(Spectrum.Frame, selected) && ReferenceEquals(Stereo.Frame, selected) && ReferenceEquals(Levels.Frame, selected), "All instruments must inspect the same historical frame");
            Check(Math.Abs(Spectrogram.CursorLevelDbfs!.Value - InstrumentView.SampleSpectrum(selected, Spectrogram.CursorFrequency)) < .00001, "History probe reads the selected time, without display tilt");
            Check(HistoryPositionLabel.Text == "−1.00s", "History timestamp label");
            await Task.Delay(80);
            SaveScreenshot(Path.Combine(directory, "history-review.png"));
            checks.Add("PASS panel time/frequency/level probe and synchronized spectrum, stereo and meters");

            HistoryPositionSlider.Value = -.5;
            Check(reviewAge is > .4 and < .6 && Spectrum.Frame == FrameHistory.AtAge(VisibleHistory, .5), "History slider ValueChanged selects time");
            var frozenPointer = new Point(bounds.Left + bounds.Width * .65, bounds.Top + bounds.Height * .6 / engine.Settings.HistorySeconds);
            Spectrogram.TrackPointer(frozenPointer);
            Check(Spectrogram.CursorLevelDbfs.HasValue && Math.Abs(Spectrogram.CursorHistoryAge!.Value - .6) < .001, "Frozen time probe stays responsive");
            HistoryLiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(150);
            Check(!frozen && frozenHistory == null && reviewAge == null, "Return-to-live button");
            Check(frameHistory.Frames[0].Time <= oldest + .051 && Spectrogram.HistoryRasterRows >= rowsBefore, $"Resume must retain pre-freeze history: oldest {oldest:F3}->{frameHistory.Frames[0].Time:F3}, rows {rowsBefore}->{Spectrogram.HistoryRasterRows}, numeric={frameHistory.Count}");
            checks.Add("PASS history slider, responsive frozen probe and live resume without history clearing");

            var analysisBefore = engine.Settings;
            int historyBefore = frameHistory.Count;
            DisplayExpander.IsExpanded = true;
            ResponsePresetCombo.SelectedIndex = 1;
            Check(engine.Display.SpectrumAttackMs == 70 && Stereo.Display.StereoSensitivity == .75 && Stereo.Display.StereoTrailMs == 160, "Smooth display preset");
            AttackSlider.Value = 43; SensitivitySlider.Value = .8; TrailSlider.Value = 130; WaveformWindowCombo.SelectedIndex = 1;
            Check(ResponsePresetCombo.SelectedIndex == 3 && engine.Display.SpectrumAttackMs == 43 && engine.Display.WaveformMs == 10, "Custom display controls");
            Check(engine.Settings == analysisBefore && frameHistory.Count >= historyBefore, "Display preferences must not change analysis settings or clear history");
            var expectedDisplay = saved.Display;
            SettingsStore.Save(saved); var reloaded = SettingsStore.Load();
            Check(reloaded.Display == expectedDisplay, "Display preferences survive persistence");
            ResponsePresetCombo.SelectedIndex = 0;
            Check(engine.Display.SpectrumAttackMs == 35 && engine.Display.StereoSensitivity == 1 && engine.Display.StereoTrailMs == 100, "Restore approved default response");
            checks.Add("PASS response presets, individual sliders, isolated settings persistence and unchanged analysis configuration");

            using (var cadenceView = new InstrumentView { Kind = InstrumentKind.Spectrogram, Settings = new() { HistorySeconds = 10 } })
            {
                cadenceView.EnableSoftware(); cadenceView.Measure(new Size(400, 300)); cadenceView.Arrange(new Rect(0, 0, 400, 300)); cadenceView.UpdateLayout();
                var uniform = Enumerable.Repeat(-10f, Analyzer.Bins).ToArray(); var fixture = displayFrame!;
                for (int i = 0; i < 200; i++)
                {
                    var row = fixture with { Time = i * .06, Sequence = i + 1, Spectrum = uniform };
                    cadenceView.Frame = row; cadenceView.AcceptHistory(row);
                }
                var rendered = cadenceView.CaptureBitmap(1); var pixels = new byte[400 * 300 * 4]; rendered.CopyPixels(pixels, 400 * 4, 0);
                byte expectedBlue = InstrumentView.Heat(.875).Blue;
                for (int y = 81; y < 226; y++) Check(pixels[(y * 400 + 170) * 4] >= expectedBlue - 2, "60 ms capture cadence must not create periodic dark stripes");
            }

            var wave = Enumerable.Range(0, 256).Select(i => (float)Math.Sin(i * .2 + 1)).ToArray();
            int trigger = InstrumentView.WaveformStart(wave, 100);
            Check(trigger > 0 && wave[trigger - 1] <= 0 && wave[trigger] > 0, "Waveform rising-edge trigger");
            Check(InstrumentView.WaveformStart(new float[256], 100) == 156, "Silent waveform trigger fallback");
            foreach (int mode in new[] { 1, 2 })
            {
                SecondaryInstrumentCombo.SelectedIndex = mode;
                await Task.Delay(250);
                long start = Stereo.RenderCount; var watch = Stopwatch.StartNew();
                await Task.Delay(2000);
                double fps = (Stereo.RenderCount - start) / watch.Elapsed.TotalSeconds;
                Check(Stereo.SecondaryMode == (StereoDisplayMode)mode && fps >= 30, $"Instrument mode {mode} must remain live: {fps:F1} FPS");
                Check(Stereo.Frame?.StereoBands.Length == 5 && Stereo.Frame.PhaseLeft.Length > 512, "Band and waveform analysis data");
                rates.Add(new { mode = Stereo.SecondaryMode.ToString(), framesPerSecond = fps });
                SaveScreenshot(Path.Combine(directory, mode == 1 ? "stereo-bands.png" : "waveform.png"));
            }
            SettingsStore.Save(saved);
            Check(SettingsStore.Load().SecondaryInstrument == 2, "Secondary instrument mode survives persistence");
            ToggleExpand(InstrumentKind.Stereo); await Task.Delay(200);
            SaveScreenshot(Path.Combine(directory, "waveform-expanded.png")); ToggleExpand(InstrumentKind.Stereo);
            ToggleExpand(InstrumentKind.Spectrogram); await Task.Delay(100);
            Check(HistoryToolbar.IsVisible && HistoryToolbar.IsDescendantOf(expandedHost), "History controls travel with expanded spectrogram");
            SaveScreenshot(Path.Combine(directory, "history-expanded.png")); ToggleExpand(InstrumentKind.Spectrogram);
            Spectrogram.RequestHistory(point); ExpandInstrument(InstrumentKind.Spectrogram);
            Check(!frozen && expanded == InstrumentKind.Spectrogram, "Double-click expansion must undo only the first-click automatic review");
            ToggleExpand(InstrumentKind.Spectrogram);
            checks.Add("PASS live band/waveform modes, shared rising-edge trigger and expanded instrument layouts");

            ToggleFreeze(); ReviewHistory(.5);
            var oldFrame = displayFrame;
            ApplyControls(engine.Settings with { FftSize = 4096 });
            await WaitFor(() => displayFrame?.Settings.FftSize == 4096 && !frozen, "Analysis reconfiguration must leave incompatible review");
            Check(!frameHistory.Frames.Contains(oldFrame!) && VisibleHistory.All(f => f.Settings.FftSize == 4096), "Old FFT history must be discarded");
            ClearDisplay();
            Check(frameHistory.Count == 0 && VisibleHistory.Count == 0 && Spectrum.Frame == null && Spectrogram.HistoryRasterRows == 0, "Source reset clears history, probes and instruments");
            Check(engine.Dropped == 0, "No audio drops during new feature checks");
            checks.Add("PASS FFT-change review invalidation, source clear and zero dropped audio blocks");

            await File.WriteAllTextAsync(Path.Combine(directory, "exploration.json"), JsonSerializer.Serialize(new { success = true, inputMode = "WPF slider/button events and panel coordinates; no hardware input", checks, rates, audioDroppedBlocks = engine.Dropped }, SettingsStore.Json));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "exploration.json"), JsonSerializer.Serialize(new { success = false, checks, error = error.ToString() }, SettingsStore.Json));
        }
        finally { Close(); }
    }
}
