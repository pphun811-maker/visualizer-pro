using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private bool interacting, draggingParameter;
    private long lastInputActivity, lastInteractionPaint, lastSettingsApply;
    private AnalysisSettings? pendingSettings;
    private long settingsApplied;

    private void InitializeInteraction()
    {
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, e) =>
        {
            if (FindSlider(e.OriginalSource as DependencyObject) == null) return;
            draggingParameter = true; BeginInteraction();
        }), true);
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, e) =>
        {
            if (FindSlider(e.OriginalSource as DependencyObject) == null) return;
            draggingParameter = false; FlushSettings(); lastInputActivity = Stopwatch.GetTimestamp();
        }), true);
    }
    private static Slider? FindSlider(DependencyObject? child)
    {
        while (child != null)
        {
            if (child is Slider slider) return slider;
            child = child is Visual ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);
        }
        return null;
    }
    private void BeginInteraction()
    {
        lastInputActivity = Stopwatch.GetTimestamp();
        if (interacting) return;
        interacting = true; UpdateRenderActivity();
    }
    private void UpdateInteraction()
    {
        if (pendingSettings != null && Stopwatch.GetElapsedTime(lastSettingsApply).TotalMilliseconds >= 50) FlushSettings();
        if (interacting && !draggingParameter && Stopwatch.GetElapsedTime(lastInputActivity).TotalMilliseconds > 180)
        { interacting = false; UpdateRenderActivity(); }
    }
    private void FlushSettings()
    {
        if (pendingSettings is not { } value) return;
        pendingSettings = null; lastSettingsApply = Stopwatch.GetTimestamp(); PushSettings(value);
    }
    private void UpdateParameterLabels(AnalysisSettings settings)
    {
        AverageLabel.Text = $"{settings.AverageMs:0}ms"; ReleaseLabel.Text = $"{settings.ReleaseMs:0}ms";
        TiltLabel.Text = $"{settings.TiltDbPerOctave:+0.#;-0.#;0} dB/oct"; RangeLabel.Text = $"{settings.SpectrogramRange:0} dB";
    }
}
