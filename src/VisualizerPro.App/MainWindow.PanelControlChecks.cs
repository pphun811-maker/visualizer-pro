using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VisualizerPro.App.Views;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private async Task CheckPanelControls(string directory)
    {
        void Press(Key key) => RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SpectrumModeCombo.SelectedIndex = HistoryModeCombo.SelectedIndex = SecondaryInstrumentCombo.SelectedIndex = 0;
        if (!frozen) ToggleFreeze();
        var held = displayFrame; var analysis = engine.Settings;
        Focus();
        Press(Key.D1); Press(Key.D2); Press(Key.D3);
        Check(SpectrumModeCombo.SelectedIndex == 1 && HistoryModeCombo.SelectedIndex == 1 && SecondaryInstrumentCombo.SelectedIndex == 1, "Number keys must cycle the matching panel");
        Check(Spectrum.SpectrumMode == SpectrumDisplayMode.ThirdOctave && Spectrogram.HistoryMode == HistoryDisplayMode.Waterfall && Stereo.SecondaryMode == StereoDisplayMode.Bands, "Keyboard modes must update the views");
        Press(Key.NumPad1); Press(Key.NumPad2); Press(Key.NumPad3); Press(Key.V);
        Check(SpectrumModeCombo.SelectedIndex == 0 && HistoryModeCombo.SelectedIndex == 0 && SecondaryInstrumentCombo.SelectedIndex == 0, "Numpad and V aliases must cycle correctly");
        SpectrumModeCombo.SelectedIndex = HistoryModeCombo.SelectedIndex = SecondaryInstrumentCombo.SelectedIndex = 1;
        Check(saved.SpectrumMode == 1 && saved.HistoryMode == 1 && saved.SecondaryInstrument == 1, "Top selectors must share saved mode state with shortcuts");
        Check(ReferenceEquals(held, displayFrame) && analysis == engine.Settings, "Switching views must preserve frozen data and analysis settings");
        await Task.Delay(100); SaveScreenshot(Path.Combine(directory, "panel-controls.png"));

        bool wasAdvanced = AdvancedExpander.IsExpanded;
        AdvancedExpander.IsExpanded = true; UpdateLayout(); MinFrequencyBox.Focus();
        Check(ReferenceEquals(Keyboard.FocusedElement, MinFrequencyBox), "Numeric entry fixture needs focus");
        Press(Key.D1); Press(Key.D2); Press(Key.D3);
        Check(saved.SpectrumMode == 1 && saved.HistoryMode == 1 && saved.SecondaryInstrument == 1, "Typing numbers must not change modes");
        AdvancedExpander.IsExpanded = wasAdvanced; Focus();
        SpectrumModeCombo.IsDropDownOpen = true; Press(Key.D1);
        Check(saved.SpectrumMode == 1, "Open dropdown must retain keyboard navigation");
        SpectrumModeCombo.IsDropDownOpen = false; Focus();

        bool controlsVisible = ControlsPanel.Visibility == Visibility.Visible;
        ControlsPanel.Visibility = Visibility.Collapsed;
        foreach (var panel in panels)
        {
            Click(panel.ExpandButton); await Task.Delay(30); UpdateLayout();
            Check(expanded == panel.Kind && panel.ExpandedView && Equals(panel.ExpandButton.Content, "还原"), "Panel expand button and caption");
            if (panel.Kind != InstrumentKind.Levels)
            {
                Press(panel.Kind switch { InstrumentKind.Spectrum => Key.D1, InstrumentKind.Spectrogram => Key.D2, _ => Key.D3 });
                Check(ReferenceEquals(held, displayFrame), "Expanded panel switch must preserve review data");
            }
            var center = panel.ExpandButton.TranslatePoint(new Point(panel.ExpandButton.ActualWidth / 2, panel.ExpandButton.ActualHeight / 2), panel);
            var hit = panel.InputHitTest(center) as DependencyObject;
            while (hit != null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
            Check(ReferenceEquals(hit, panel.ExpandButton), "Header control must receive hit tests above the GPU surface");
            Click(panel.ExpandButton);
            Check(expanded == null && !panel.ExpandedView && Equals(panel.ExpandButton.Content, "放大"), "Panel restore button");
        }
        ControlsPanel.Visibility = controlsVisible ? Visibility.Visible : Visibility.Collapsed;
        Press(Key.F1); await Task.Delay(80);
        Check(shortcutsWindow?.IsVisible == true && ShortcutGuide.Contains("1：FFT") && ShortcutGuide.Contains("2：二维") && ShortcutGuide.Contains("3 或 V"), "F1 must expose current shortcuts");
        var guide = shortcutsWindow!; Click(Levels.HelpButton);
        Check(ReferenceEquals(guide, shortcutsWindow), "Help button must reuse the existing help window");
        var helpImage = new RenderTargetBitmap((int)guide.ActualWidth, (int)guide.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        helpImage.Render(guide); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(helpImage));
        using (var stream = File.Create(Path.Combine(directory, "shortcuts.png"))) encoder.Save(stream);
        guide.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(guide)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Check(shortcutsWindow == null, "Escape must close shortcut guide");
        Activate(); Focus();
        SpectrumModeCombo.SelectedIndex = HistoryModeCombo.SelectedIndex = SecondaryInstrumentCombo.SelectedIndex = 0;
        if (frozen) ToggleFreeze();
    }
}
