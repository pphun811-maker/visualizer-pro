using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using VisualizerPro.App.Services;

namespace VisualizerPro.App;

public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    public async Task RunFileDialogChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        var observations = new List<object>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        try
        {
            await Task.Delay(1500);
            var owner = new WindowInteropHelper(this).Handle;
            foreach (bool freeze in new[] { false, true })
            {
                if (frozen != freeze) ToggleFreeze();
                await Task.Delay(500);
                var watch = Stopwatch.StartNew();
                long[]? start = null, end = null;
                double firstObservedMs = 0;
                bool paused = false;
                var probe = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
                probe.Tick += (_, _) =>
                {
                    var popup = GetLastActivePopup(owner);
                    if (popup == owner || popup == IntPtr.Zero) return;
                    if (firstObservedMs == 0) firstObservedMs = watch.Elapsed.TotalMilliseconds;
                    // Exclude the initial exposure paint.
                    if (watch.Elapsed.TotalMilliseconds - firstObservedMs < 700) return;
                    if (start == null)
                    {
                        start = panels.Select(p => p.RenderCount).ToArray();
                        paused = fileDialogDepth == 1 && panels.All(p => !p.RenderActive);
                    }
                    if (watch.Elapsed.TotalMilliseconds - firstObservedMs < 2000) return;
                    end = panels.Select(p => p.RenderCount).ToArray();
                    probe.Stop();
                    PostMessageW(popup, 0x0010, IntPtr.Zero, IntPtr.Zero);
                };
                bool? result;
                probe.Start();
                try { result = ShowFileDialog(new OpenFileDialog { Title = "打开音频文件", Filter = "音频文件|*.wav;*.flac;*.mp3;*.aac;*.m4a;*.ogg;*.opus;*.aif;*.aiff|所有文件|*.*" }); }
                finally { probe.Stop(); }
                Check(result != true && start != null && end != null, $"Native dialog cancellation (frozen={freeze})");
                Check(paused && start!.SequenceEqual(end!), $"No instrument rendering inside native dialog (frozen={freeze})");
                Check(fileDialogDepth == 0 && frozen == freeze, $"Modal and freeze state restored (frozen={freeze})");
                long before = Spectrum.RenderCount;
                await Task.Delay(500);
                Check(freeze ? panels.All(p => !p.RenderActive) : Spectrum.RenderCount > before, $"Rendering recovery respects freeze (frozen={freeze})");
                observations.Add(new { frozen = freeze, firstObservedMs, durationMs = watch.Elapsed.TotalMilliseconds, start, end });
            }
            ToggleFreeze();
            try { RunFileDialog(() => throw new IOException("Test dialog failure")); }
            catch (IOException) { }
            Check(fileDialogDepth == 0, "Exception unwinds dialog suspension");
            long recovery = Spectrum.RenderCount;
            await Task.Delay(500);
            Check(Spectrum.RenderCount > recovery, "Rendering recovers after dialog exception");
            await File.WriteAllTextAsync(Path.Combine(directory, "file-dialog.json"), JsonSerializer.Serialize(new { checks, observations, audioDroppedBlocks = engine.Dropped }, SettingsStore.Json));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), error.ToString()); }
        finally { Close(); }
    }
}
