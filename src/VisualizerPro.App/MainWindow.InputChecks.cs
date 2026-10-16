using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;

namespace VisualizerPro.App;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct InputPoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out InputPoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);

    // Default: deterministic WPF routed input. --native-input also exercises OS
    // delivery when the session provides an interactive input desktop.
    public async Task RunInputChecks(string directory, bool nativeInput = false)
    {
        Directory.CreateDirectory(directory);
        bool haveOriginalPointer = GetCursorPos(out var originalPointer);
        var checks = new List<string>(); var latency = new List<double>();
        int sliderChanges = 0; bool sawDrag = false;
        Thumb? simulatedThumb = null; Point previousSimulatedPoint = default;
        void Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { sliderChanges++; sawDrag |= draggingParameter; }
        async Task Sweep(Point start, Point end, int steps)
        {
            await Task.Run(async () =>
            {
                for (int i = 1; i <= steps; i++)
                {
                    long sent = Stopwatch.GetTimestamp();
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => latency.Add(Stopwatch.GetElapsedTime(sent).TotalMilliseconds));
                    var point = new Point(Math.Round(start.X + (end.X - start.X) * i / steps), Math.Round(start.Y + (end.Y - start.Y) * i / steps));
                    if (nativeInput)
                    {
                        if (!SetCursorPos((int)point.X, (int)point.Y)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetCursorPos failed in desktop input test");
                        mouse_event(0x0001, 0, 0, 0, UIntPtr.Zero);
                    }
                    else await Dispatcher.InvokeAsync(() =>
                    {
                        if (simulatedThumb == null) Spectrum.TrackPointer(Spectrum.PointFromScreen(point));
                        else
                        {
                            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                            simulatedThumb.RaiseEvent(new DragDeltaEventArgs((point.X - previousSimulatedPoint.X) / scale.DpiScaleX, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                            previousSimulatedPoint = point;
                        }
                    }, DispatcherPriority.Input);
                    await Task.Delay(16);
                }
            });
        }
        try
        {
            // Start-Process uses a hidden startup window for helpers; override that
            // only for this explicit interactive check so real input can reach it.
            if (nativeInput) ShowWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle, 5);
            Topmost = true; Activate(); await Task.Delay(1200);
            Check(Spectrum.ToolTip == null && !ToolTipService.GetIsEnabled(Spectrum) && Stereo.ToolTip == null, "Panel help popups must be disabled");
            checks.Add("PASS panel help ToolTips removed");
            await WaitFor(() => Spectrum.Frame != null, "Input fixture needs analysis data");
            var sample = Spectrum.Frame! with { Frequencies = [100, 1000], Spectrum = [-20, -40] };
            Check(Math.Abs(InstrumentView.SampleSpectrum(sample, Math.Sqrt(100 * 1000)) + 30) < .00001, "Log-frequency level interpolation");
            Check(InstrumentView.Nearest([100, 1000], 101) == 0, "Nearest bin must not always round upward");
            checks.Add("PASS interpolated dBFS matches the plotted line between bins");

            var plot = Spectrum.PlotBounds;
            var startLocal = new Point(plot.Left + plot.Width * .2, plot.Top + plot.Height * .5);
            var endLocal = new Point(plot.Left + plot.Width * .7, plot.Top + plot.Height * .5);
            await Sweep(Spectrum.PointToScreen(startLocal), Spectrum.PointToScreen(endLocal), 60);
            await Task.Delay(100);
            var actual = Spectrum.PointerPosition;
            double expectedX = Spectrum.PointFromScreen(new Point(Math.Round(Spectrum.PointToScreen(endLocal).X), Math.Round(Spectrum.PointToScreen(endLocal).Y))).X;
            GetCursorPos(out var nativePoint);
            Check(actual.HasValue && Math.Abs(actual.Value.X - expectedX) < 1, $"Pointer position in panel DIP coordinates: probe={actual}, expectedX={expectedX}, native=({nativePoint.X},{nativePoint.Y}), WPF={System.Windows.Input.Mouse.GetPosition(Spectrum)}, over={Spectrum.IsMouseOver}, active={IsActive}");
            Check(Math.Abs(Spectrum.FrequencyX(Spectrum.CursorFrequency) - expectedX) < 1, "Crosshair must stay under the physical pointer at current DPI");
            Check(Spectrum.CursorLevelDbfs.HasValue && double.IsFinite(Spectrum.CursorLevelDbfs.Value), "Frequency probe needs a numeric dBFS reading");
            checks.Add("PASS pointer sweep coordinates, crosshair alignment and live Hz/dBFS readout");
            SaveScreenshot(Path.Combine(directory, "live-probe.png"));

            ToggleFreeze(); var held = displayFrame;
            var frozenLocal = new Point(plot.Left + plot.Width * .4, plot.Top + plot.Height * .4);
            await Sweep(Spectrum.PointToScreen(endLocal), Spectrum.PointToScreen(frozenLocal), 20);
            await Task.Delay(80);
            Check(ReferenceEquals(held, displayFrame) && Math.Abs(Spectrum.FrequencyX(Spectrum.CursorFrequency) - frozenLocal.X) < 1, "Frozen data must keep the pointer overlay responsive");
            SaveScreenshot(Path.Combine(directory, "frozen-probe.png")); ToggleFreeze();
            checks.Add("PASS probe movement while measurement display is frozen");

            AverageSlider.ApplyTemplate(); var track = (Track)AverageSlider.Template.FindName("PART_Track", AverageSlider);
            var thumb = track.Thumb;
            Point thumbCenter = thumb.PointToScreen(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
            Point destination = track.PointToScreen(new Point(track.ActualWidth * .86, track.ActualHeight / 2));
            AverageSlider.ValueChanged += Changed; long appliesBefore = settingsApplied;
            if (nativeInput) { SetCursorPos((int)thumbCenter.X, (int)thumbCenter.Y); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); }
            else
            {
                simulatedThumb = thumb; previousSimulatedPoint = thumbCenter;
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            }
            await Task.Delay(60);
            await Sweep(thumbCenter, destination, 60);
            if (nativeInput) mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            else { thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent }); simulatedThumb = null; }
            await Task.Delay(160);
            long applied = settingsApplied - appliesBefore;
            Check(sawDrag && sliderChanges > 10 && !draggingParameter, "Thumb drag routing and release");
            Check(applied < sliderChanges && pendingSettings == null, "Slider settings should coalesce and commit on release");
            Check(Math.Abs(engine.Settings.AverageMs - AverageSlider.Value) < .001, "Released slider final value must reach analyzer");
            checks.Add("PASS routed slider drag, coalesced settings and exact final commit");
            AverageSlider.ValueChanged -= Changed;

            await Task.Delay(300);
            Check(!interacting && panels.Where(p => p.Renderer == "OPENGL").All(p => p.RenderActive), "Continuous rendering must recover after input");
            checks.Add("PASS continuous rendering resumes after interaction");
            var sorted = latency.Order().ToArray();
            await File.WriteAllTextAsync(Path.Combine(directory, "input.json"), JsonSerializer.Serialize(new {
                success = true, inputMode = nativeInput ? "Windows native input" : "WPF coordinates and routed thumb events (no hardware input)", checks, sliderValueChanges = sliderChanges, analysisSettingsApplied = applied,
                inputQueueP95Ms = sorted[(int)((sorted.Length - 1) * .95)], inputQueueMaxMs = sorted[^1], audioDroppedBlocks = engine.Dropped
            }, SettingsStore.Json));
        }
        catch (Exception error)
        { await File.WriteAllTextAsync(Path.Combine(directory, "input.json"), JsonSerializer.Serialize(new { success = false, checks, error = error.ToString() }, SettingsStore.Json)); }
        finally
        {
            AverageSlider.ValueChanged -= Changed;
            if (nativeInput) { mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); if (haveOriginalPointer) SetCursorPos(originalPointer.X, originalPointer.Y); }
            Close();
        }
    }
}
