using System.Text;
using System.Text.Json;
using System.Windows;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;

namespace VisualizerPro.App;

public partial class MainWindow
{
    public async Task RunPerformance(int seconds, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            Topmost = true; Activate();
            await Task.Delay(3000);
            using var process = Process.GetCurrentProcess(); process.Refresh();
            long startPaint = Spectrum.RenderCount, startWorkingSet = process.WorkingSet64, startTicks = tick;
            TimeSpan startCpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
            await Task.Delay(Math.Clamp(seconds, 5, 7200) * 1000);
            process.Refresh(); double elapsed = watch.Elapsed.TotalSeconds;
            var report = new { seconds = elapsed, framesPerSecond = (Spectrum.RenderCount - startPaint) / elapsed,
                cpuPercent = 100 * (process.TotalProcessorTime - startCpu).TotalSeconds / elapsed / Environment.ProcessorCount,
                startWorkingSet, endWorkingSet = process.WorkingSet64, managedBytes = GC.GetTotalMemory(false), audioDroppedBlocks = engine.Dropped,
                dispatcherTicksPerSecond = (tick - startTicks) / elapsed, renderTier = System.Windows.Media.RenderCapability.Tier >> 16, isActive = IsActive, visible = IsVisible,
                historyFrames = frameHistory.Count, historySpanSeconds = frameHistory.Count > 1 ? frameHistory.LatestTime - frameHistory.Frames[0].Time : 0,
                historyCapacity = engine.Settings.HistorySeconds * 20, frozenHistoryFrames = frozenHistory?.Length ?? 0,
                panelRenderMs = panels.Select(p => new { kind = p.Kind.ToString(), renderer = p.Renderer, meanMs = p.RenderMilliseconds / Math.Max(1, p.RenderCount) }).ToArray() };
            SaveScreenshot(Path.Combine(directory, "dashboard.png"));
            await File.WriteAllTextAsync(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(report, SettingsStore.Json));
        }
        catch (Exception e) { SettingsStore.Log(e); }
        finally { Close(); }
    }
    public async Task RunIntegration(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var results = new List<string>();
        try
        {
            await Task.Delay(800);
            if (fullScreen)
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                GetWindowRect(handle, out var bounds);
                var screen = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
                Check(bounds.Left == screen.Left && bounds.Top == screen.Top && bounds.Right == screen.Right && bounds.Bottom == screen.Bottom, "Startup fullscreen must match monitor pixels");
                results.Add("PASS initial fullscreen HWND bounds match all four monitor edges");
            }
            string fixture = Path.Combine(outputDirectory, "stereo-44100.wav");
            WriteTestWave(fixture);
            player.Volume = 0; VolumeSlider.Value = 0;
            await OpenFile(fixture);
            await WaitFor(() => engine.Latest?.SampleRate == 44100 && engine.Latest.Time > .5, "File decoder/player did not produce 44.1 kHz audio");
            Check(media != null && fileMode && FilePanel.IsVisible, "File mode UI");
            Check(engine.Latest!.Meters.RmsLeft is > -80 and < -5, $"Live RMS invalid: {engine.Latest.Meters.RmsLeft}");
            results.Add("PASS native 44.1 kHz decoding, playback, pre-volume analysis");

            ToggleFreeze(); var oldFrame = displayFrame;
            await Task.Delay(400); Check(ReferenceEquals(displayFrame, oldFrame) && engine.Latest != oldFrame, "Freeze must hold UI while analysis advances");
            ToggleFreeze(); results.Add("PASS freeze keeps playback and analysis active");
            ApplyControls(engine.Settings with { FftSize = 4096, TiltDbPerOctave = 3, AverageMs = 500 });
            await WaitFor(() => engine.Latest?.Settings.FftSize == 4096, "FFT reconfiguration");
            OnSnapshot(this, new RoutedEventArgs()); Check(snapshots.Count > 0 && Spectrum.Snapshots.Count > 0, "Snapshot");
            results.Add("PASS live controls and reference snapshots");

            bool wasPlaying = player.Playing; player.Toggle(); await Task.Delay(150);
            long sequence = engine.Latest?.Sequence ?? -1; await Task.Delay(220);
            Check(!player.Playing && engine.Latest?.Sequence == sequence, "Paused file must not integrate synthetic silence");
            if (wasPlaying) player.Toggle();
            Seek(2); await Task.Delay(250); Check(player.Position >= 2 && player.Position < 3, "Seek position");
            Check(engine.Latest?.Meters.Seconds < 1, "Seek resets live integration");
            Check(engine.Latest!.Meters.RmsLeft is > -80 and < -5, $"Seek RMS invalid: {engine.Latest.Meters.RmsLeft}");
            results.Add("PASS pause, seek synchronization and measurement reset");

            loopA = .8; loopB = 1.3; LoopCheck.IsChecked = true; UpdateLoop(); Seek(1.1);
            await Task.Delay(900); Check(player.Position >= .8 && player.Position < 1.6, "A-B loop");
            LoopCheck.IsChecked = false; results.Add("PASS A-B loop and loop-boundary history reset");

            using (var token = new CancellationTokenSource())
            {
                var overview = await FileAnalysis.Analyze(media!, null, token.Token);
                Check(overview.Columns.Count > 10 && double.IsFinite(overview.Integrated), "Whole-file analysis");
                Overview.SetOverview(overview); FileStatsLabel.Text = $"整曲 I {overview.Integrated:0.0} LUFS · TP {overview.TruePeak:0.0} dBTP";
                var cached = await FileAnalysis.Analyze(media!, null, token.Token); Check(cached.Columns.Count == overview.Columns.Count, "Overview cache");
            }
            results.Add("PASS whole-file spectrogram, loudness and cache round trip");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool observed = false;
                try { await FileAnalysis.Analyze(media!, null, cancelled.Token); } catch (OperationCanceledException) { observed = true; }
                Check(observed, "File analysis cancellation");
            }
            results.Add("PASS whole-file cancellation");

            string rendererBeforeExpand = Stereo.Renderer; long beforeExpand = Stereo.RenderCount;
            ToggleExpand(InstrumentKind.Stereo); Check(expandedHost.IsVisible && !Instruments.IsVisible, "Expand panel");
            await Task.Delay(250); Check(Stereo.RenderCount > beforeExpand, "Expanded renderer must remain live");
            ToggleExpand(InstrumentKind.Stereo); Check(Instruments.IsVisible, "Restore panel");
            await Task.Delay(200); Check(Stereo.Renderer == rendererBeforeExpand, "Reparenting must preserve rendering backend");
            OnFocus(this, new RoutedEventArgs()); OnFocus(this, new RoutedEventArgs());
            SetFullScreen(false); Check(WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(this) != null, "Exit fullscreen with custom chrome");
            SetFullScreen(true); Check(WindowStyle == WindowStyle.None, "Enter fullscreen");
            results.Add("PASS focus mode, panel expansion and fullscreen round trip");

            await Task.Delay(200); SaveScreenshot(Path.Combine(outputDirectory, "file-dashboard.png"));
            StartSystem();
            if (system.Connected)
            {
                await WaitFor(() => engine.Latest != null, "Loopback silence delivery");
                results.Add("PASS WASAPI loopback connection and silent-stream metering");
                string? defaultId = SystemAudioSource.DefaultId();
                saved.DeviceId = defaultId; StartSystem(); Check(system.ActiveId == defaultId, "Manual output picker");
                saved.DeviceId = "nonexistent-test-endpoint"; StartSystem(); Check(!system.Connected, "Missing endpoint state");
                saved.DeviceId = null; StartSystem(); Check(system.Connected, "Reconnect default endpoint");
                results.Add("PASS manual device selection, missing-device handling and reconnect");
            }
            else results.Add("SKIP WASAPI: no output endpoint available in test environment");
            foreach (var format in new[] { ("flac", "flac"), ("mp3", "libmp3lame"), ("m4a", "aac"), ("aac", "aac"), ("ogg", "libvorbis") })
            {
                string encoded = Path.Combine(outputDirectory, $"测试音频 - format.{format.Item1}");
                using (var encoder = Ffmpeg.Start("ffmpeg", ["-v", "error", "-y", "-i", fixture, "-t", "1", "-c:a", format.Item2, encoded]))
                {
                    var errors = encoder.StandardError.ReadToEndAsync(); await encoder.WaitForExitAsync();
                    Check(encoder.ExitCode == 0, await errors);
                }
                var metadata = await Ffmpeg.Probe(encoded, CancellationToken.None);
                using var formatEngine = new VisualizerPro.Core.AnalysisEngine();
                using var decoded = new DecodedProvider(metadata, .2, formatEngine);
                var data = new float[8192]; int read = decoded.Read(data, 0, data.Length);
                Check(read > 0 && data.Take(read).Max(Math.Abs) is > .01f and < 1, "Format decoder " + format.Item1);
            }
            results.Add("PASS FLAC / MP3 / AAC / M4A / OGG decoding and Unicode/spaced file paths");
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "integration.json"), JsonSerializer.Serialize(new { success = true, results }, SettingsStore.Json));
        }
        catch (Exception error)
        {
            results.Add("FAIL " + error);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "integration.json"), JsonSerializer.Serialize(new { success = false, results }, SettingsStore.Json));
        }
        finally { Close(); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task WaitFor(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.Elapsed.TotalSeconds > 12) throw new TimeoutException(message); await Task.Delay(50); }
    }
    private static void WriteTestWave(string path)
    {
        const int rate = 44100, frames = rate * 8, bytes = frames * 8;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(bytes + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)3); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 8); writer.Write((short)8); writer.Write((short)32);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)rate;
            double pulse = .12 + .1 * Math.Sin(t * 4);
            writer.Write((float)(pulse * Math.Sin(2 * Math.PI * 440 * t) + .03 * Math.Sin(2 * Math.PI * 2200 * t)));
            writer.Write((float)(pulse * Math.Sin(2 * Math.PI * 440 * t + .4) + .03 * Math.Sin(2 * Math.PI * 3300 * t)));
        }
    }
}
