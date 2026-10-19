using System.Windows;
using System.Windows.Controls;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private readonly FrameHistory frameHistory = new();
    private AnalysisFrame[]? frozenHistory;
    private double? reviewAge;
    private bool updatingHistoryControls, updatingDisplayControls;
    private long canvasReviewStarted;
    private AnalysisFrame? lastHistoryInput;
    private IReadOnlyList<AnalysisFrame> VisibleHistory => frozenHistory ?? frameHistory.Frames;

    private void InitializeExploration()
    {
        Spectrogram.HistoryFrameAtAge = age => FrameHistory.AtAge(VisibleHistory, age);
        Spectrogram.HistoryFramesProvider = () => VisibleHistory;
        Spectrogram.WaterfallCameraChanged += ApplyDisplaySettings;
        Spectrogram.ReviewRequested += (age, frequency) =>
        {
            bool wasLive = !frozen;
            ReviewHistory(age, frequency);
            if (wasLive && frozen) canvasReviewStarted = Stopwatch.GetTimestamp();
        };
        ApplyDisplayControls(saved.Display);
        bool previous = ready; ready = false;
        SecondaryInstrumentCombo.SelectedIndex = saved.SecondaryInstrument;
        Stereo.SecondaryMode = (StereoDisplayMode)saved.SecondaryInstrument;
        SpectrumModeCombo.SelectedIndex = saved.SpectrumMode; Spectrum.SpectrumMode = (SpectrumDisplayMode)saved.SpectrumMode;
        HistoryModeCombo.SelectedIndex = saved.HistoryMode; Spectrogram.HistoryMode = (HistoryDisplayMode)saved.HistoryMode;
        ready = previous;
        UpdateHistoryControls();
    }

    private void CollectHistory(AnalysisFrame next)
    {
        if (ReferenceEquals(lastHistoryInput, next)) return;
        lastHistoryInput = next;
        if (frameHistory.Count > 0 && (next.Time < frameHistory.LatestTime - 1e-6 || next.SampleRate != frameHistory.Frames[^1].SampleRate))
        {
            frameHistory.Clear();
            if (!frozen) Spectrogram.ResetHistory();
        }
        // Recover frames produced between UI paints, including a brief UI stall.
        // In file mode throughTime is the played frame, never decoder pre-read.
        foreach (var captured in engine.FramesBetween(frameHistory.Count == 0 ? -1 : frameHistory.LatestTime, next.Time))
        {
            if (captured.Settings.FftSize != next.Settings.FftSize || captured.Settings.Window != next.Settings.Window || captured.Settings.Channel != next.Settings.Channel || captured.Settings.SmoothingOctaves != next.Settings.SmoothingOctaves || captured.SampleRate != next.SampleRate) continue;
            var entry = frameHistory.Add(captured, engine.Settings.HistorySeconds);
            if (entry != null && !frozen) Spectrogram.AcceptHistory(entry);
        }
    }

    private void RebuildSpectrogram()
    {
        Spectrogram.ResetHistory();
        bool held = Spectrogram.Frozen; Spectrogram.Frozen = false;
        foreach (var entry in VisibleHistory) Spectrogram.AcceptHistory(entry);
        Spectrogram.Frozen = held;
    }

    private void UpdateHistoryControls()
    {
        if (HistoryPositionSlider == null) return;
        updatingHistoryControls = true;
        try
        {
            var history = VisibleHistory;
            HistoryPositionSlider.Minimum = history.Count > 1 ? -Math.Min(engine.Settings.HistorySeconds, history[^1].Time - history[0].Time) : 0;
            HistoryPositionSlider.IsEnabled = history.Count > 1;
            HistoryPositionSlider.Value = reviewAge is { } age ? -age : 0;
            HistoryPositionLabel.Text = reviewAge is { } selected ? $"−{selected:0.00}s" : frozen ? "已冻结" : "实时";
            Spectrogram.SelectedHistoryAge = reviewAge;
        }
        finally { updatingHistoryControls = false; }
    }

    private void ReviewHistory(double age, double frequency = 0)
    {
        var candidate = FrameHistory.AtAge(VisibleHistory, age);
        if (candidate == null) { stateText = "该时刻没有历史数据"; return; }
        if (!frozen) ToggleFreeze();
        var selected = FrameHistory.AtAge(VisibleHistory, age);
        if (selected == null) return;
        reviewAge = VisibleHistory[^1].Time - selected.Time;
        displayFrame = selected;
        foreach (var panel in panels) if (panel != Spectrogram) panel.Frame = selected;
        if (frequency > 0) Spectrum.CursorFrequency = Spectrogram.CursorFrequency = frequency;
        UpdateHistoryControls(); Redraw();
    }

    private void OnHistoryPositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready || updatingHistoryControls) return;
        ReviewHistory(Math.Max(0, -e.NewValue));
    }

    private void OnHistoryLive(object sender, RoutedEventArgs e)
    {
        if (frozen) ToggleFreeze();
    }

    private void ExpandInstrument(InstrumentKind kind)
    {
        // The first click of a double-click may have entered history review.
        // Expanding from live should stay live; an existing freeze stays frozen.
        if (kind == InstrumentKind.Spectrogram && frozen && canvasReviewStarted != 0 && Stopwatch.GetElapsedTime(canvasReviewStarted).TotalMilliseconds <= System.Windows.Forms.SystemInformation.DoubleClickTime) ToggleFreeze();
        canvasReviewStarted = 0;
        ToggleExpand(kind);
    }

    private void ApplyDisplayControls(DisplaySettings value)
    {
        value = value.Sanitize(); updatingDisplayControls = true;
        try
        {
            AttackSlider.Value = value.SpectrumAttackMs; SensitivitySlider.Value = value.StereoSensitivity; TrailSlider.Value = value.StereoTrailMs;
            WaveformWindowCombo.SelectedIndex = value.WaveformMs <= 5 ? 0 : value.WaveformMs <= 10 ? 1 : value.WaveformMs <= 20 ? 2 : 3;
            WaterfallWindowCombo.SelectedIndex = Array.IndexOf(new[] { 4, 8, 16 }, value.WaterfallSeconds);
            PaletteCombo.SelectedIndex = value.FrequencyColors ? value.FrequencyPalette == FrequencyPaletteKind.Standard ? 2 : 1 : 0;
        }
        finally { updatingDisplayControls = false; }
        ApplyDisplaySettings(value);
    }

    private void ApplyDisplaySettings(DisplaySettings value)
    {
        value = value.Sanitize();
        bool paletteChanged = saved.Display.FrequencyColors != value.FrequencyColors || saved.Display.FrequencyPalette != value.FrequencyPalette;
        saved.Display = value; engine.ConfigureDisplay(value);
        foreach (var panel in panels) panel.Display = value;
        PaletteLegend.Visibility = value.FrequencyColors ? Visibility.Visible : Visibility.Collapsed;
        if (paletteChanged || PaletteLegend.Children.Count == 0) UpdatePaletteLegend(value);
        if (paletteChanged) RebuildSpectrogram();
        AttackLabel.Text = $"{value.SpectrumAttackMs:0}ms";
        SensitivityLabel.Text = $"{value.StereoSensitivity:0.00}×";
        TrailLabel.Text = $"{value.StereoTrailMs:0}ms";
        updatingDisplayControls = true;
        ResponsePresetCombo.SelectedIndex = value.SpectrumAttackMs == 35 && value.StereoSensitivity == 1 && value.StereoTrailMs == 100 ? 0
            : value.SpectrumAttackMs == 70 && value.StereoSensitivity == .75 && value.StereoTrailMs == 160 ? 1
            : value.SpectrumAttackMs == 12 && value.StereoSensitivity == 1.15 && value.StereoTrailMs == 50 ? 2 : 3;
        updatingDisplayControls = false;
        if (ready) { BeginInteraction(); Redraw(); }
    }

    private void OnDisplayResponseChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updatingDisplayControls) return;
        ApplyDisplaySettings(saved.Display with { SpectrumAttackMs = AttackSlider.Value, StereoSensitivity = SensitivitySlider.Value, StereoTrailMs = TrailSlider.Value, WaveformMs = new[] { 5.0, 10, 20, 30 }[Math.Max(0, WaveformWindowCombo.SelectedIndex)], WaterfallSeconds = new[] { 4, 8, 16 }[Math.Max(0, WaterfallWindowCombo.SelectedIndex)] });
    }

    private void OnPaletteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updatingDisplayControls) return;
        ApplyDisplaySettings(saved.Display with { FrequencyColors = PaletteCombo.SelectedIndex > 0, FrequencyPalette = PaletteCombo.SelectedIndex == 2 ? FrequencyPaletteKind.Standard : FrequencyPaletteKind.Soft });
    }

    private void UpdatePaletteLegend(DisplaySettings value)
    {
        PaletteLegend.Children.Clear();
        PaletteLegend.Children.Add(new TextBlock { Text = "频段 Hz", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)), FontSize = 10, Margin = new(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
        var colors = FrequencyPalette.GetColors(value.FrequencyPalette);
        for (int i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            PaletteLegend.Children.Add(new TextBlock { Text = "●  " + FrequencyPalette.Labels[i].Replace(" Hz", ""), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue)), FontSize = 10, Margin = new(0, 0, 18, 0) });
        }
    }

    private void OnSettingsExpanded(object sender, RoutedEventArgs e)
    {
        if (AdvancedExpander == null || DisplayExpander == null) return;
        if (ReferenceEquals(sender, AdvancedExpander)) DisplayExpander.IsExpanded = false;
        else AdvancedExpander.IsExpanded = false;
    }

    private void OnResponsePreset(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updatingDisplayControls) return;
        switch (ResponsePresetCombo.SelectedIndex)
        {
            case 0: ApplyDisplayControls(saved.Display with { SpectrumAttackMs = 35, StereoSensitivity = 1, StereoTrailMs = 100 }); break;
            case 1: ApplyDisplayControls(saved.Display with { SpectrumAttackMs = 70, StereoSensitivity = .75, StereoTrailMs = 160 }); break;
            case 2: ApplyDisplayControls(saved.Display with { SpectrumAttackMs = 12, StereoSensitivity = 1.15, StereoTrailMs = 50 }); break;
        }
    }

    private void OnSecondaryInstrument(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        saved.SecondaryInstrument = Math.Clamp(SecondaryInstrumentCombo.SelectedIndex, 0, 2);
        Stereo.SecondaryMode = (StereoDisplayMode)saved.SecondaryInstrument;
        Redraw();
    }

    private void UpdateHistorySettings(AnalysisSettings previous, AnalysisSettings next)
    {
        bool changedAnalysis = previous.FftSize != next.FftSize || previous.Window != next.Window || previous.Channel != next.Channel || previous.SmoothingOctaves != next.SmoothingOctaves;
        if (changedAnalysis)
        {
            frameHistory.Clear(); lastHistoryInput = null; frozenHistory = null; reviewAge = null; Spectrogram.ResetHistory();
            if (frozen) ToggleFreeze();
        }
        else
        {
            frameHistory.Trim(next.HistorySeconds);
            if (frozenHistory != null) frozenHistory = frozenHistory.Where(f => frozenHistory[^1].Time - f.Time < next.HistorySeconds).ToArray();
            if (reviewAge >= next.HistorySeconds && VisibleHistory.Count > 0) ReviewHistory(VisibleHistory[^1].Time - VisibleHistory[0].Time);
            if (previous.HistorySeconds != next.HistorySeconds || previous.TiltDbPerOctave != next.TiltDbPerOctave || previous.SpectrogramRange != next.SpectrogramRange || previous.CeilingDb != next.CeilingDb) RebuildSpectrogram();
        }
        UpdateHistoryControls();
    }
}
