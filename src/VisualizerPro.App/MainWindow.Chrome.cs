using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Runtime.InteropServices;

namespace VisualizerPro.App;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    private void ConstrainMaximizedWindow(IntPtr hwnd, IntPtr pointer)
    {
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var work = screen.WorkingArea; var monitor = screen.Bounds;
        var limits = Marshal.PtrToStructure<MinMaxInfo>(pointer);
        limits.MaxPosition = new() { X = work.Left - monitor.Left, Y = work.Top - monitor.Top };
        limits.MaxSize = new() { X = work.Width, Y = work.Height };
        double scale = Math.Max(1, GetDpiForWindow(hwnd)) / 96.0;
        limits.MinTrackSize = new() { X = (int)Math.Ceiling(MinWidth * scale), Y = (int)Math.Ceiling(MinHeight * scale) };
        Marshal.StructureToPtr(limits, pointer, false);
    }

    private static WindowChrome CreateWindowChrome() => new()
    {
        CaptionHeight = 0, ResizeBorderThickness = new Thickness(6),
        CornerRadius = new CornerRadius(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
    };

    private void OnTitleBarDown(object sender, MouseButtonEventArgs e)
    {
        // Inputs keep their own gestures; only blank header space and the logo drag.
        for (var node = e.OriginalSource as DependencyObject; node != null && node != TitleBar; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ButtonBase or ComboBox or TextBoxBase) return;
        if (e.ClickCount == 2) OnMaximize(sender, e);
        else if (!fullScreen && e.LeftButton == MouseButtonState.Pressed) DragMove();
        e.Handled = true;
    }

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (fullScreen) SetFullScreen(false);
        else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateMaximizeIcon();

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIcon == null) return;
        MaximizeIcon.Data = Geometry.Parse(fullScreen || WindowState == WindowState.Maximized
            ? "M 4,1 L 12,1 12,9 M 1,4 L 9,4 9,12 1,12 Z"
            : "M 1,1 L 11,1 11,11 1,11 Z");
    }
}
