using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisualizerPro.App.Views;

public sealed partial class InstrumentView
{
    private readonly StackPanel headerActions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 32, 16, 0) };
    private readonly TextBlock frozenState = new() { Text = "已冻结", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), Foreground = Brushes.DarkGray, Visibility = Visibility.Collapsed };
    internal Button ModeButton { get; } = new() { Padding = new(9, 3, 9, 3), Margin = new(2, 0, 2, 0), FontSize = 11, Height = 25, Focusable = false };
    internal Button ExpandButton { get; } = new() { Content = "放大", Padding = new(8, 3, 8, 3), Margin = new(2, 0, 0, 0), FontSize = 11, Height = 25, Focusable = false };
    public event Action<InstrumentKind>? ModeCycleRequested;
    public event Action<InstrumentKind>? HeaderExpandRequested;
    public event Action? HelpRequested;
    private bool expandedView;
    public bool ExpandedView { get => expandedView; set { expandedView = value; UpdateHeaderActions(); } }

    private void InitializeHeaderActions()
    {
        headerActions.Children.Add(frozenState); headerActions.Children.Add(ModeButton); headerActions.Children.Add(ExpandButton);
        ModeButton.Click += (_, _) => { if (Kind == InstrumentKind.Levels) HelpRequested?.Invoke(); else ModeCycleRequested?.Invoke(Kind); };
        ExpandButton.Click += (_, _) => HeaderExpandRequested?.Invoke(Kind);
        // Header controls do not forward plot gestures, frequency resets or zoom.
        headerActions.MouseMove += (_, e) => e.Handled = true;
        headerActions.MouseWheel += (_, e) => e.Handled = true;
        headerActions.MouseRightButtonDown += (_, e) => e.Handled = true;
        Loaded += (_, _) => UpdateHeaderActions();
    }

    private void UpdateHeaderActions()
    {
        ModeButton.Content = Kind switch { InstrumentKind.Spectrum => "切换 1", InstrumentKind.Spectrogram => "切换 2", InstrumentKind.Stereo => "切换 3", _ => "读数 F1" };
        string action = Kind switch
        {
            InstrumentKind.Spectrum => SpectrumMode == SpectrumDisplayMode.Fft ? "FFT 频谱，切换为 1/3 倍频程，快捷键 1" : "1/3 倍频程，切换为 FFT 频谱，快捷键 1",
            InstrumentKind.Spectrogram => HistoryMode == HistoryDisplayMode.Heatmap ? "二维声谱，切换为三维瀑布，快捷键 2" : "三维瀑布，切换为二维声谱，快捷键 2",
            InstrumentKind.Stereo => "切换立体声场、分频段声场、波形示波器，快捷键 3 或 V",
            _ => "快捷键与读数说明，F1"
        };
        AutomationProperties.SetName(ModeButton, action);
        ExpandButton.Content = expandedView ? "还原" : "放大";
        AutomationProperties.SetName(ExpandButton, expandedView ? "还原仪表布局" : "放大此仪表，也可双击仪表");
    }

    private void CaptureHeader(DrawingContext drawing)
    {
        var origin = headerActions.TranslatePoint(new Point(), this);
        drawing.DrawRectangle(new VisualBrush(headerActions) { Stretch = Stretch.Fill }, null, new Rect(origin, new Size(headerActions.ActualWidth, headerActions.ActualHeight)));
    }
}
