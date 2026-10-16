using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VisualizerPro.App.Services;
using Forms = System.Windows.Forms;

namespace VisualizerPro.App;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    // Exercises the real system-input UI, rather than the demo-only performance path.
    public async Task RunResponsiveness(string directory)
    {
        Directory.CreateDirectory(directory);
        var pulses = new List<(string Phase, double Gap)>();
        var frames = new List<(string Phase, double Gap)>();
        string phase = "stable";
        long previousPulse = 0, previousFrame = 0;
        var pulse = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        pulse.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            if (previousPulse != 0) pulses.Add((phase, Stopwatch.GetElapsedTime(previousPulse, now).TotalMilliseconds));
            previousPulse = now;
        };
        void Painted(long now)
        {
            if (previousFrame != 0) frames.Add((phase, Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds));
            previousFrame = now;
        }
        try
        {
            Topmost = true; Activate();
            await Task.Delay(1200);
            var handle = new WindowInteropHelper(this).Handle;
            GetWindowRect(handle, out var initial);
            var screen = Forms.Screen.FromHandle(handle).Bounds;
            bool startupFits = initial.Left == screen.Left && initial.Top == screen.Top && initial.Right == screen.Right && initial.Bottom == screen.Bottom;
            Spectrum.FramePainted += Painted; pulse.Start();
            await Task.Delay(10000);
            phase = "transition"; SetFullScreen(false); await Task.Delay(500);
            phase = "moving"; previousPulse = previousFrame = 0;
            GetWindowRect(handle, out var normal);
            SendMessageW(handle, 0x0231, IntPtr.Zero, IntPtr.Zero); // WM_ENTERSIZEMOVE
            for (int i = 0; i < 90; i++)
            {
                SetWindowPos(handle, IntPtr.Zero, normal.Left + (int)(30 * Math.Sin(i * .1)), normal.Top + (int)(20 * Math.Cos(i * .1)), 0, 0, 0x0001 | 0x0004 | 0x0010);
                await Task.Delay(16);
            }
            SendMessageW(handle, 0x0232, IntPtr.Zero, IntPtr.Zero); // WM_EXITSIZEMOVE
            phase = "transition"; SetFullScreen(true); await Task.Delay(500);
            GetWindowRect(handle, out var restored);
            bool restoredFits = restored.Left == screen.Left && restored.Top == screen.Top && restored.Right == screen.Right && restored.Bottom == screen.Bottom;
            phase = "recovered"; previousPulse = previousFrame = 0;
            await Task.Delay(2000);
            object Summarize(List<(string Phase, double Gap)> samples, string name)
            {
                var values = samples.Where(s => s.Phase == name).Select(s => s.Gap).Order().ToArray();
                double At(double fraction) => values.Length == 0 ? 0 : values[(int)((values.Length - 1) * fraction)];
                return new { count = values.Length, p50Ms = At(.5), p95Ms = At(.95), p99Ms = At(.99), maxMs = At(1), stallsOver80Ms = values.Count(v => v > 80) };
            }
            var report = new {
                startupFits, restoredFits, initialBounds = new { initial.Left, initial.Top, initial.Right, initial.Bottom },
                screenBounds = new { screen.Left, screen.Top, screen.Right, screen.Bottom },
                audioDroppedBlocks = engine.Dropped, isActive = IsActive, visible = IsVisible,
                panels = panels.Select(p => new { kind = p.Kind.ToString(), p.Renderer, p.RenderActive }).ToArray(),
                phases = new[] { "stable", "moving", "recovered" }.Select(name => new { name, dispatcher = Summarize(pulses, name), spectrum = Summarize(frames, name) }).ToArray(),
                note = "Movement uses real HWND position changes and enter/exit-size-move messages; it does not simulate physical mouse input."
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "responsiveness.json"), JsonSerializer.Serialize(report, SettingsStore.Json));
            SaveScreenshot(Path.Combine(directory, "dashboard.png"));
        }
        catch (Exception error) { SettingsStore.Log(error); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), error.ToString()); }
        finally { pulse.Stop(); Spectrum.FramePainted -= Painted; Close(); }
    }
}
