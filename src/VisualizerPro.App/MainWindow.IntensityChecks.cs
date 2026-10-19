using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaSharp;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private async Task CheckIntensityAppearance(string directory)
    {
        var previous = saved.Display;
        SpectrumModeCombo.SelectedIndex = HistoryModeCombo.SelectedIndex = SecondaryInstrumentCombo.SelectedIndex = 0;
        PaletteCombo.SelectedIndex = 2;
        if (!frozen) ToggleFreeze();
        HistoryPositionSlider.Value = -1;
        var held = displayFrame!; var anchor = frozenHistory; var age = reviewAge;
        int count = Spectrogram.HistoryRasterRows;
        var settings = engine.Settings;
        var before = Spectrum.CaptureBitmap(1);
        var beforeHeat = ReadPixel(Spectrogram.CaptureBitmap(1), (int)Spectrogram.FrequencyX(65), (int)Spectrogram.PlotBounds.Top + 10);
        SpectrogramPaletteCombo.SelectedIndex = 1;
        var after = Spectrum.CaptureBitmap(1);
        var beforeBytes = new byte[before.PixelWidth * before.PixelHeight * 4]; var afterBytes = new byte[beforeBytes.Length];
        before.CopyPixels(beforeBytes, before.PixelWidth * 4, 0); after.CopyPixels(afterBytes, after.PixelWidth * 4, 0);
        Check(beforeBytes.SequenceEqual(afterBytes), "Intensity mode must leave rendered FFT pixels unchanged");
        var afterHeat = ReadPixel(Spectrogram.CaptureBitmap(1), (int)Spectrogram.FrequencyX(65), (int)Spectrogram.PlotBounds.Top + 10);
        Check(beforeHeat != afterHeat, "Switching intensity must repaint existing frozen history");
        Check(frozen && ReferenceEquals(held, displayFrame) && ReferenceEquals(anchor, frozenHistory) && age == reviewAge && count == Spectrogram.HistoryRasterRows && settings == engine.Settings, "Intensity must retain frozen frame, history, position and analysis");
        SettingsStore.Save(saved); var restored = SettingsStore.Load().Display;
        Check(restored.SpectrogramIntensityColors && restored.FrequencyColors && restored.FrequencyPalette == FrequencyPaletteKind.Standard, "Both independent color settings must persist");
        ApplyDisplayControls(restored); Check(SpectrogramPaletteCombo.SelectedIndex == 1 && PaletteCombo.SelectedIndex == 2, "Restore both selectors");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<DisplaySettings>("{}", SettingsStore.Json)!;
        Check(!legacy.SpectrogramIntensityColors, "Old settings must keep their original history colors");
        await Task.Delay(80); SaveScreenshot(Path.Combine(directory, "intensity-2d.png"));
        HistoryModeCombo.SelectedIndex = 1;
        var colored = Spectrogram.EnsureWaterfall();
        SpectrogramPaletteCombo.SelectedIndex = 0; var normal = Spectrogram.EnsureWaterfall();
        Check(!ReferenceEquals(colored, normal) && normal.TriangleCount > 0 && ReferenceEquals(anchor, frozenHistory), "3D raw/display switching must invalidate cache while retaining history");
        SpectrogramPaletteCombo.SelectedIndex = 1;
        var mesh = Spectrogram.EnsureWaterfall();
        var point = mesh.Data[mesh.Columns / 2].Position;
        Spectrogram.TrackPointer(new Point(point.X, point.Y));
        Check(Spectrogram.CursorLevelDbfs.HasValue, "Intensity mode retains the frozen inline 3D probe");
        await Task.Delay(80); SaveScreenshot(Path.Combine(directory, "intensity-3d.png"));

        // Source-image colors, checked against actual 2D and 3D raster output.
        // Equal-energy frequencies must retain the same hue even with display tilt.
        foreach (var sample in new[] { (Node: 3, Hex: "#34476E"), (Node: 7, Hex: "#C3769A"), (Node: 11, Hex: "#DDE3C4") })
        {
            float db = -130 + 10 * sample.Node;
            var fixture = held with { Spectrum = Enumerable.Repeat(db, Analyzer.Bins).ToArray(), Spectrogram = null };
            var frames = Enumerable.Range(0, 200).Select(i => fixture with { Time = i * .05, Sequence = i + 1 }).ToArray();
            var fixtureSettings = settings with { HistorySeconds = 10, SpectrogramRange = 80, CeilingDb = 0, TiltDbPerOctave = 3 };
            var display = new DisplaySettings { FrequencyColors = true, SpectrogramIntensityColors = true };
            using var view = new InstrumentView { Kind = InstrumentKind.Spectrogram, Settings = fixtureSettings, Display = display };
            view.EnableSoftware(); view.Measure(new Size(960, 400)); view.Arrange(new Rect(0, 0, 960, 400)); view.UpdateLayout();
            foreach (var frame in frames) { view.Frame = frame; view.AcceptHistory(frame); }
            var image = view.CaptureBitmap(1); var expected = SKColor.Parse(sample.Hex);
            bool Near(SKColor color) => Math.Abs(color.Red - expected.Red) <= 3 && Math.Abs(color.Green - expected.Green) <= 3 && Math.Abs(color.Blue - expected.Blue) <= 3;
            foreach (double hz in new[] { 65.0, 250, 1100, 3500, 9000 })
                Check(Near(ReadPixel(image, (int)view.FrequencyX(hz), 175)), $"2D must match reference {sample.Hex} at {hz} Hz");
            using var flatMesh = new WaterfallMesh(frames, new(49, 78, 900, 329), 20, 20000, 0, 8, fixtureSettings, display);
            using var bitmap = new SKBitmap(960, 400); using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Black); flatMesh.Draw(canvas);
            foreach (double u in new[] { .31, .7 })
            {
                double hz = 20 * Math.Pow(1000, u);
                var pixel = flatMesh.Project(u, 3.17, db + 3 * Math.Log2(hz / 1000));
                var actual = bitmap.GetPixel((int)pixel.X, (int)pixel.Y);
                Check(Near(actual), $"3D must match reference {sample.Hex} across frequencies: {actual} at {pixel}");
            }
        }
        ApplyDisplayControls(previous);
        HistoryModeCombo.SelectedIndex = 0;
        if (frozen) ToggleFreeze();
    }

    private async Task CheckWindowChrome(string directory)
    {
        bool wasFullScreen = fullScreen;
        SetFullScreen(false);
        Check(Title == "Visualizer Pro" && WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(this) != null, "Window must use custom chrome without the old caption");
        foreach (var button in new[] { MinimizeButton, MaximizeButton, CloseButton })
        {
            var center = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), this);
            var hit = InputHitTest(center) as DependencyObject;
            while (hit != null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
            Check(ReferenceEquals(hit, button), "Custom window controls must receive hit tests");
        }
        MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
        Check(WindowState == WindowState.Maximized, "Custom maximize button");
        var hwnd = new WindowInteropHelper(this).Handle;
        var work = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        // WindowChrome can keep an invisible resize border beyond the working area;
        // the content and the three controls must still fit inside it.
        var bottom = PointToScreen(new Point(ActualWidth / 2, ActualHeight));
        Check(bottom.Y <= work.Bottom + 1, $"Maximized content must not cover the taskbar: bottom {bottom.Y}, working edge {work.Bottom}");
        SaveScreenshot(Path.Combine(directory, "chrome-maximized.png"));
        MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
        Check(WindowState == WindowState.Normal, "Custom restore button");
        MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(WindowState == WindowState.Minimized, "Custom minimize button");
        WindowState = WindowState.Normal; Activate(); await Task.Delay(80);
        SetFullScreen(true); await Task.Delay(80);
        GetWindowRect(hwnd, out var bounds); var screen = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
        Check(bounds.Left == screen.Left && bounds.Top == screen.Top && bounds.Right == screen.Right && bounds.Bottom == screen.Bottom, "Custom chrome fullscreen must match all monitor edges");
        if (!wasFullScreen) SetFullScreen(false);
    }
}
