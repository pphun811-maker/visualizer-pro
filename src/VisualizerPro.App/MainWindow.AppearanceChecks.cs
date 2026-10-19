using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private static SKColor ReadPixel(BitmapSource image, int x, int y)
    {
        var bgra = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4]; bgra.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return new(pixel[2], pixel[1], pixel[0], pixel[3]);
    }

    private async Task CheckFrequencyAppearance(string directory, int paletteIndex)
    {
        directory = Path.Combine(directory, paletteIndex == 2 ? "standard" : "soft");
        Directory.CreateDirectory(directory);
        var style = paletteIndex == 2 ? FrequencyPaletteKind.Standard : FrequencyPaletteKind.Soft;
        int previousPalette = PaletteCombo.SelectedIndex;
        SpectrumModeCombo.SelectedIndex = HistoryModeCombo.SelectedIndex = SecondaryInstrumentCombo.SelectedIndex = 0;
        if (!frozen) ToggleFreeze();
        HistoryPositionSlider.Value = -1;
        var held = displayFrame!; var history = frozenHistory; var settings = engine.Settings;
        double? age = reviewAge; int rows = Spectrogram.HistoryRasterRows;
        PaletteCombo.SelectedIndex = 0;
        await Task.Delay(70); SaveScreenshot(Path.Combine(directory, "appearance-blue.png"));
        PaletteCombo.SelectedIndex = paletteIndex;
        Check(panels.All(p => p.Display.FrequencyColors && p.Display.FrequencyPalette == style) && engine.Display.FrequencyColors, "Palette must reach every instrument");
        Check(frozen && ReferenceEquals(held, displayFrame) && ReferenceEquals(history, frozenHistory) && reviewAge == age && engine.Settings == settings, "Palette must preserve review frame, anchor and analysis settings");
        Check(Spectrogram.HistoryRasterRows == rows && rows > 0, "Palette rebuild must retain existing heatmap history");
        UpdateLayout();
        int px = (int)Spectrogram.FrequencyX(65), py = (int)Spectrogram.PlotBounds.Top + 10;
        var beforeStyle = ReadPixel(Spectrogram.CaptureBitmap(1), px, py);
        PaletteCombo.SelectedIndex = paletteIndex == 2 ? 1 : 2;
        var afterStyle = ReadPixel(Spectrogram.CaptureBitmap(1), px, py);
        Check(Math.Abs(beforeStyle.Red - afterStyle.Red) + Math.Abs(beforeStyle.Green - afterStyle.Green) + Math.Abs(beforeStyle.Blue - afterStyle.Blue) > 12, "Soft/standard switch must recolor existing frozen heatmap pixels");
        Check(ReferenceEquals(held, displayFrame) && ReferenceEquals(history, frozenHistory) && reviewAge == age, "Direct palette style change must preserve review data");
        PaletteCombo.SelectedIndex = paletteIndex;
        var swatch = (SolidColorBrush)((TextBlock)PaletteLegend.Children[1]).Foreground;
        var expectedSwatch = FrequencyPalette.GetColors(style)[0];
        Check(swatch.Color.R == expectedSwatch.Red && swatch.Color.G == expectedSwatch.Green && swatch.Color.B == expectedSwatch.Blue, "Legend must use the current palette style");
        Spectrogram.TrackPointer(new Point(Spectrogram.FrequencyX(1100), Spectrogram.PlotBounds.Top + 1 / settings.HistorySeconds * Spectrogram.PlotBounds.Height));
        Check(Spectrogram.CursorLevelDbfs.HasValue, "Frozen multicolor heatmap must retain an inline probe");
        await Task.Delay(70); SaveScreenshot(Path.Combine(directory, "appearance-multicolor-fft.png"));
        SpectrumModeCombo.SelectedIndex = HistoryModeCombo.SelectedIndex = SecondaryInstrumentCombo.SelectedIndex = 1;
        var mesh = Spectrogram.EnsureWaterfall(); var vertices = mesh.Data.ToArray();
        int triangles = mesh.TriangleCount;
        PaletteCombo.SelectedIndex = 0; var blueMesh = Spectrogram.EnsureWaterfall();
        Check(triangles == blueMesh.TriangleCount && vertices.SequenceEqual(blueMesh.Data), "Palette must not change waterfall geometry or sampling");
        PaletteCombo.SelectedIndex = paletteIndex;
        await Task.Delay(70); SaveScreenshot(Path.Combine(directory, "appearance-multicolor-3d.png"));
        SettingsStore.Save(saved);
        var loadedDisplay = SettingsStore.Load().Display;
        Check(loadedDisplay.FrequencyColors && loadedDisplay.FrequencyPalette == style, "Palette and style must survive settings persistence");
        ApplyDisplayControls(loadedDisplay);
        Check(PaletteCombo.SelectedIndex == paletteIndex, "Restored palette must select the matching option");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<DisplaySettings>("{\"FrequencyColors\":true}", SettingsStore.Json)!;
        Check(legacy.FrequencyColors && legacy.FrequencyPalette == FrequencyPaletteKind.Soft, "Old multicolor settings must retain the soft palette");
        Check((legacy with { FrequencyPalette = (FrequencyPaletteKind)99 }).Sanitize().FrequencyPalette == FrequencyPaletteKind.Soft, "Invalid palette style must recover to soft");

        // Verify actual rendered RGB values using a flat, equal-energy fixture.
        // This detects level-driven rainbows and palette switches that do not repaint history.
        var fixture = held with { Spectrum = Enumerable.Repeat(-25f, Analyzer.Bins).ToArray() };
        using var view = new InstrumentView { Kind = InstrumentKind.Spectrogram, Settings = settings with { HistorySeconds = 10, TiltDbPerOctave = 0 }, Display = new() { FrequencyColors = true, FrequencyPalette = style } };
        view.EnableSoftware(); view.Measure(new Size(960, 400)); view.Arrange(new Rect(0, 0, 960, 400)); view.UpdateLayout();
        for (int i = 0; i < 200; i++) { var frame = fixture with { Time = i * .05, Sequence = i + 1 }; view.Frame = frame; view.AcceptHistory(frame); }
        var bitmap = view.CaptureBitmap(1);
        double[] probes = [65, 250, 1100, 3500, 9000];
        foreach (double hz in probes)
        {
            var actual = ReadPixel(bitmap, (int)view.FrequencyX(hz), 175);
            var expected = FrequencyPalette.Heat((settings.SpectrogramRange - 25 - settings.CeilingDb) / settings.SpectrogramRange, hz, true, style);
            Check(Math.Abs(actual.Red - expected.Red) <= 3 && Math.Abs(actual.Green - expected.Green) <= 3 && Math.Abs(actual.Blue - expected.Blue) <= 3, $"Equal-energy heatmap must use the frequency hue at {hz} Hz: {actual} vs {expected}");
        }
        view.Kind = InstrumentKind.Spectrum;
        var fft = view.CaptureBitmap(1);
        int y = (int)Math.Round(400 - 61 - (-25 - settings.FloorDb) / (settings.CeilingDb - settings.FloorDb) * (400 - 143));
        var warm = ReadPixel(fft, (int)view.FrequencyX(3500), y);
        var purple = ReadPixel(fft, (int)view.FrequencyX(9000), y);
        Check(warm.Red > warm.Blue + 15 && purple.Blue > purple.Green + 8 && purple.Red > purple.Green + 8, "Rendered FFT must follow the shared frequency palette");
        view.Settings = view.Settings with { MinFrequency = 200, MaxFrequency = 10000 };
        var zoomed = view.CaptureBitmap(1);
        var warmZoomed = ReadPixel(zoomed, (int)view.FrequencyX(3500), y);
        Check(warmZoomed.Red > warmZoomed.Blue + 15, "Frequency colors must stay attached to the same Hz after zoom");
        PaletteCombo.SelectedIndex = previousPalette;
        SpectrumModeCombo.SelectedIndex = HistoryModeCombo.SelectedIndex = SecondaryInstrumentCombo.SelectedIndex = 0;
        if (frozen) ToggleFreeze();
    }

    private async Task CheckCompactAppearance(string directory)
    {
        bool wasFullScreen = fullScreen;
        double previousWidth = Width, previousHeight = Height;
        bool controlsVisible = ControlsPanel.Visibility == Visibility.Visible;
        SetFullScreen(false); Width = 1120; Height = 740;
        ControlsPanel.Visibility = Visibility.Visible;
        PaletteCombo.SelectedIndex = 2;
        if (!frozen) ToggleFreeze();
        await Task.Delay(100); UpdateLayout();
        void Fits(FrameworkElement element)
        {
            var root = (FrameworkElement)Content;
            var at = element.TranslatePoint(new Point(), root);
            Check(element.IsVisible && element.ActualWidth > 0 && at.X >= -.5 && at.Y >= -.5 && at.X + element.ActualWidth <= root.ActualWidth + .5 && at.Y + element.ActualHeight <= root.ActualHeight + .5, $"Compact layout clips {element.Name}");
        }
        foreach (var control in new FrameworkElement[] { DeviceCombo, MoreButton, FreezeButton, PaletteCombo, AnalysisToggle, DisplayToggle, Spectrum, Spectrogram, Stereo, Levels }) Fits(control);
        SaveScreenshot(Path.Combine(directory, "appearance-1120.png"));
        AnalysisToggle.IsChecked = true; UpdateLayout();
        Check(AdvancedExpander.IsExpanded && !DisplayExpander.IsExpanded, "Analysis toggle must open its controls");
        foreach (var control in new FrameworkElement[] { ChannelCombo, AverageSlider, ReleaseSlider, HistoryCombo, FftCombo, SmoothCombo, TiltSlider, RangeSlider, MinFrequencyBox, CeilingBox }) Fits(control);
        foreach (var panel in panels) Fits(panel);
        SaveScreenshot(Path.Combine(directory, "appearance-1120-analysis.png"));
        DisplayToggle.IsChecked = true; UpdateLayout();
        Check(DisplayExpander.IsExpanded && !AdvancedExpander.IsExpanded, "Settings sections must share space without stacking");
        foreach (var control in new FrameworkElement[] { ResponsePresetCombo, AttackSlider, TrailSlider, WaveformWindowCombo, WaterfallWindowCombo, ResetWaterfallViewButton }) Fits(control);
        foreach (var panel in panels) Fits(panel);
        SaveScreenshot(Path.Combine(directory, "appearance-1120-display.png"));
        DisplayToggle.IsChecked = false;
        OnMenu(MoreButton, new RoutedEventArgs());
        var menu = (ContextMenu?)MoreButton.ContextMenu;
        Check(menu?.IsOpen == true && menu.Items.OfType<MenuItem>().Any(item => item.Header.ToString()!.Contains("PNG")) && menu.Items.OfType<MenuItem>().Any(item => item.Header.ToString()!.Contains("CSV")), "More menu must retain screenshot and CSV export");
        menu!.IsOpen = false;
        ControlsPanel.Visibility = controlsVisible ? Visibility.Visible : Visibility.Collapsed;
        Width = previousWidth; Height = previousHeight;
        if (wasFullScreen) SetFullScreen(true);
        PaletteCombo.SelectedIndex = 0;
        if (frozen) ToggleFreeze();
        await Task.Delay(80);
    }
}
