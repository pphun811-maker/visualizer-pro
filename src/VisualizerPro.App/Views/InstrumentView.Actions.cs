using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisualizerPro.App.Views;

public sealed partial class InstrumentView
{
    private readonly StackPanel headerActions = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 20, 20, 0), IsHitTestVisible = false };
    private readonly TextBlock frozenState = new() { Text = "已冻结", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), Foreground = Brushes.DarkGray, Visibility = Visibility.Collapsed };
    public bool ExpandedView { get; set; }

    private void InitializeHeaderActions()
    {
        headerActions.Children.Add(frozenState);
    }

    private void CaptureHeader(DrawingContext drawing)
    {
        var origin = headerActions.TranslatePoint(new Point(), this);
        drawing.DrawRectangle(new VisualBrush(headerActions) { Stretch = Stretch.Fill }, null, new Rect(origin, new Size(headerActions.ActualWidth, headerActions.ActualHeight)));
    }
}
