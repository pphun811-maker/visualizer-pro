using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using VisualizerPro.App.Services;
using VisualizerPro.App.Views;
using VisualizerPro.Core;
using Forms = System.Windows.Forms;

namespace VisualizerPro.App;

public partial class MainWindow : Window
{
    private readonly UserSettings saved;
    private readonly AnalysisEngine engine = new();
    private readonly SystemAudioSource system;
    private readonly FilePlayer player;
    private readonly AudioDeviceMonitor deviceMonitor = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly List<SpectrumSnapshot> snapshots = [];
    private readonly HashSet<SpectrumSnapshot> hiddenSnapshots = [];
    private readonly Border expandedHost = new() { BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(16), Visibility = Visibility.Collapsed };
    private InstrumentView[] panels = [];
    private InstrumentKind? expanded;
    private Forms.NotifyIcon? tray;
    private CancellationTokenSource? analysisCancellation, loadCancellation;
    private MediaInfo? media;
    private AnalysisFrame? displayFrame;
    private bool ready, fileMode, frozen, fullScreen, closing, switching;
    private readonly bool demo;
    private double loopA = -1, loopB = -1;
    private int tick, fileVersion, snapshotNumber;
    private long lastFrameUpdate;
    private AudioDeviceSnapshot? lastDeviceSnapshot;
    private bool movingWindow;
    private int fileDialogDepth;
    private HwndSource? windowSource;
    private Rect windowBounds;
    private string deviceSignature = "";
    private string stateText = "等待系统声音";
    private Task? demoTask;
    private readonly CancellationTokenSource demoCancellation = new();

    public MainWindow(bool demo = false)
    {
        this.demo = demo; saved = SettingsStore.Load(); system = new(engine); player = new(engine);
        InitializeComponent();
        panels = [Spectrum, Spectrogram, Stereo, Levels];
        Grid.SetRow(expandedHost, 2); ((Grid)Content).Children.Add(expandedHost);
        foreach (var panel in panels)
        {
            panel.BeforePaint += SyncAnalysisFrame;
            panel.CursorChanged += f => { Spectrum.CursorFrequency = Spectrogram.CursorFrequency = f; };
            panel.InputActivity += BeginInteraction;
            panel.RangeChanged += SetFrequencyRange;
            panel.BandChanged += (low, high) => { Spectrum.SetBand(low, high); Spectrogram.SetBand(low, high); if (frozen) Redraw(); };
            panel.ExpandRequested += ExpandInstrument;
            panel.HeaderExpandRequested += ToggleExpand;
            panel.HelpRequested += ShowShortcutGuide;
        }
        Overview.SeekRequested += Seek; InitializeInteraction(); InitializeExploration();
        system.Error += message => Dispatcher.BeginInvoke(() => { stateText = "设备连接中断，正在等待恢复：" + message; system.Dispose(); engine.Reset(); ClearDisplay(); });
        player.Error += message => Dispatcher.BeginInvoke(() => { stateText = "播放设备连接中断：" + message; });
        ApplyControls(saved.Analysis);
        AverageCheck.IsChecked = saved.ShowAverage; PeaksCheck.IsChecked = saved.ShowPeaks;
        ControlsPanel.Visibility = saved.ControlsVisible ? Visibility.Visible : Visibility.Collapsed;
        VolumeSlider.Value = saved.Volume; player.Volume = saved.Volume; Topmost = saved.Topmost;
        RestoreLayout(); LoadSnapshots();
        DeviceCombo.ItemsSource = new[] { new OutputDevice(null, "跟随默认播放设备") }; DeviceCombo.SelectedIndex = 0;
        timer.Tick += OnTick;
        StateChanged += (_, _) => UpdateRenderActivity();
        IsVisibleChanged += (_, _) => UpdateRenderActivity();
        Loaded += OnLoaded;
        ContentRendered += (_, _) => { if (fullScreen) PositionOn(CurrentScreen()); };
        SizeChanged += (_, _) => FitPanels();
        ControlsPanel.IsVisibleChanged += (_, _) => FitPanels();
        ControlsPanel.SizeChanged += (_, _) => FitPanels();
        FilePanel.IsVisibleChanged += (_, _) => FitPanels();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        ready = true;
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        windowSource?.AddHook(WindowMessage);
        double width = double.IsFinite(saved.Width) ? Math.Clamp(saved.Width, 1120, 4000) : 1440;
        double height = double.IsFinite(saved.Height) ? Math.Clamp(saved.Height, 740, 2400) : 900;
        Width = width; Height = height;
        Left = double.IsFinite(saved.Left) ? saved.Left : 50; Top = double.IsFinite(saved.Top) ? saved.Top : 50;
        // WPF persists DIP coordinates; Screen rectangles and HWND bounds are pixels.
        var scale = VisualTreeHelper.GetDpi(this);
        bool visible = Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new System.Drawing.Rectangle((int)(Left * scale.DpiScaleX), (int)(Top * scale.DpiScaleY), 200, 100)));
        if (!visible) { Left = 40; Top = 40; }
        windowBounds = new(Left, Top, width, height);
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (saved.FullScreen) SetFullScreen(true);
        CreateTray(); FitPanels(); UpdateRenderActivity(); timer.Start();
        if (demo) { stateText = "测试信号 · 不代表当前系统声音"; SourceLabel.Text = "DEMO / SYNTHETIC SIGNAL · 48 kHz · Stereo"; DeviceCombo.IsEnabled = false; demoTask = Task.Run(DemoLoop); }
        else
        {
            string[] args = Environment.GetCommandLineArgs();
            int open = Array.IndexOf(args, "--open");
            if (open >= 0 && args.Length > open + 1) await OpenFile(args[open + 1]); else StartSystem();
        }
    }
    private void RefreshDevices(AudioDeviceSnapshot snapshot)
    {
            if (snapshot.Error != null) { stateText = "暂时无法枚举播放设备：" + snapshot.Error; return; }
            var devices = snapshot.Devices.ToList(); string signature = string.Join("|", devices.Select(d => d.Id + ":" + d.Name));
            if (signature == deviceSignature && DeviceCombo.ItemsSource != null) return;
            deviceSignature = signature; bool previous = ready; ready = false;
            DeviceCombo.ItemsSource = devices;
            var chosen = devices.FirstOrDefault(d => d.Id == saved.DeviceId);
            if (chosen == null && saved.DeviceId != null)
            { chosen = new(saved.DeviceId, "已指定的设备（等待连接）"); devices.Add(chosen); }
            DeviceCombo.SelectedItem = chosen ?? devices[0]; ready = previous;
    }
    private void StartSystem()
    {
        if (demo || closing || switching) return;
        switching = true;
        try
        {
            loadCancellation?.Cancel(); analysisCancellation?.Cancel(); player.Stop(); media = null; fileMode = false; fileVersion++;
            FilePanel.Visibility = Visibility.Collapsed; DeviceCombo.Visibility = Visibility.Visible; SourceLabel.FontSize = 10;
            loopA = loopB = -1; Overview.SetOverview(null); ClearDisplay();
            system.Start(saved.DeviceId); SourceLabel.Text = system.Description;
            stateText = "系统监听 · 选择频段深入观察，或按 Tab 收起控制区";
            SystemButton.Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224));
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException)
        { engine.Reset(); stateText = "等待播放设备连接 · " + e.Message; SourceLabel.Text = "设备尚未连接，将自动重试"; }
        finally { switching = false; }
    }
    private void OnTick(object? sender, EventArgs e)
    {
        if (closing) return;
        if (fileDialogDepth > 0)
        {
            // The shell dialog pumps this dispatcher too. Leave audio/history
            // running without competing with Explorer for rendering and layout.
            if (!fileMode && !demo) system.PumpSilence();
            SyncAnalysisFrame();
            if (fileMode && LoopCheck.IsChecked == true && loopA >= 0 && loopB > loopA && player.Position >= loopB && player.Playing) Seek(loopA);
            return;
        }
        tick++;
        UpdateInteraction();
        var devices = deviceMonitor.Latest;
        if (devices != null && !ReferenceEquals(devices, lastDeviceSnapshot))
        {
            lastDeviceSnapshot = devices; RefreshDevices(devices);
            if (!fileMode && !demo && devices.Error == null)
            {
                string? wanted = saved.DeviceId ?? devices.DefaultId;
                if (!system.Connected || wanted != system.ActiveId) StartSystem();
            }
        }
        if (!fileMode && !demo)
        {
            system.PumpSilence();
        }
        SyncAnalysisFrame();
        if (tick % 6 == 0) UpdateHistoryControls();
        if (!movingWindow && !frozen && IsVisible && WindowState != WindowState.Minimized && (!interacting || Stopwatch.GetElapsedTime(lastInteractionPaint).TotalMilliseconds >= 33))
        {
            // GLWpfControl's composition callback alone falls to WPF's 10 Hz idle
            // cadence on this system. Keep composition awake while observing.
            lastInteractionPaint = Stopwatch.GetTimestamp(); Redraw();
        }
        if (fileMode && media != null)
        {
            double position = player.Position;
            if (LoopCheck.IsChecked == true && loopA >= 0 && loopB > loopA && position >= loopB && player.Playing) Seek(loopA);
            Overview.Position = player.Position; Overview.InvalidateVisual();
            if (tick % 6 == 0)
            {
                PositionLabel.Text = $"{Time(player.Position)} / {Time(media.Duration)}";
                PlayButton.Content = player.Playing ? "Ⅱ 暂停" : "▶ 播放";
                if (player.Failure is { Length: > 0 } failure) stateText = "解码中断：" + failure.Trim();
            }
        }
        if (tick % 30 == 0)
        {
            StatusLabel.Text = frozen ? reviewAge is { } age ? $"历史回看 −{age:0.00}s · F 恢复实时" : "Ⅱ 画面已冻结 · 拖动声谱或回看滑块选择时刻 · F 恢复实时" : stateText;
            int rate = displayFrame?.SampleRate ?? 48000;
            ResolutionLabel.Text = $"Δf {rate / (double)engine.Settings.FftSize:0.00} Hz   窗长 {1000.0 * engine.Settings.FftSize / rate:0} ms";
            if (engine.Dropped > 0) PerformanceLabel.Text = $"音频队列丢块 {engine.Dropped} · 可降低 FFT 大小   |   F11 全屏 · Esc 返回";
        }
    }
    private static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
    private void FitPanels()
    {
        double height = Math.Max(250, ((Grid)Content).ActualHeight - 100 - (ControlsPanel.IsVisible ? ControlsPanel.ActualHeight : 0) - (FilePanel.IsVisible ? FilePanel.ActualHeight + 10 : 0));
        StereoRow.MinHeight = Math.Min(210, Math.Max(110, (height - 12) * .38));
        RightPanels.RowDefinitions[2].MinHeight = Math.Min(260, Math.Max(190, (height - 12) * .54));
        SpectrumRow.MinHeight = Math.Min(160, Math.Max(110, (height - 9) * .45));
        LeftPanels.RowDefinitions[2].MinHeight = Math.Min(150, Math.Max(100, (height - 9) * .40));
    }
    private void SyncAnalysisFrame()
    {
        if (closing || Stopwatch.GetElapsedTime(lastFrameUpdate).TotalMilliseconds < 1) return;
        lastFrameUpdate = Stopwatch.GetTimestamp();
        var next = fileMode ? engine.FrameAt(player.SessionTime) : engine.Latest;
        if (next == null) return;
        var currentSettings = engine.Settings;
        if (next.Settings.FftSize != currentSettings.FftSize || next.Settings.Window != currentSettings.Window || next.Settings.Channel != currentSettings.Channel || next.Settings.SmoothingOctaves != currentSettings.SmoothingOctaves) return;
        CollectHistory(next);
        if (fileDialogDepth > 0 || frozen || ReferenceEquals(displayFrame, next)) return;
        displayFrame = next; foreach (var panel in panels) panel.Frame = next;
    }
    private void UpdateRenderActivity()
    {
        bool active = fileDialogDepth == 0 && IsVisible && WindowState != WindowState.Minimized && !frozen && !movingWindow && !interacting;
        foreach (var panel in panels) panel.SetRenderActivity(active);
    }
    public bool TrySoftwareRendering(Exception error)
    {
        if (panels.All(p => p.Renderer == "SOFTWARE") || error.StackTrace?.Contains("OpenTK", StringComparison.Ordinal) != true) return false;
        foreach (var panel in panels) panel.EnableSoftware();
        stateText = "已切换到兼容绘制模式"; return true;
    }
    private void Redraw() { if (fileDialogDepth > 0) return; foreach (var panel in panels) if (panel.IsVisible) panel.InvalidateVisual(); }
    private void ClearDisplay()
    {
        displayFrame = null; foreach (var panel in panels) panel.Frame = null; Spectrogram.ResetHistory();
        frameHistory.Clear(); lastHistoryInput = null; frozenHistory = null; reviewAge = null;
        if (frozen)
        {
            frozen = false; foreach (var panel in panels) panel.Frozen = false;
            FreezeButton.Content = "冻结 F"; FreezeButton.Foreground = new SolidColorBrush(Color.FromRgb(221, 221, 221));
        }
        UpdateHistoryControls(); UpdateRenderActivity(); Redraw();
    }
    private void ApplyControls(AnalysisSettings value)
    {
        bool old = ready; ready = false; value = value.Sanitize();
        ChannelCombo.SelectedIndex = (int)value.Channel;
        FftCombo.SelectedIndex = Array.IndexOf(new[] { 2048, 4096, 8192, 16384, 32768, 65536 }, value.FftSize);
        WindowCombo.SelectedIndex = (int)value.Window;
        AverageSlider.Value = value.AverageMs; ReleaseSlider.Value = value.ReleaseMs;
        SmoothCombo.SelectedIndex = value.SmoothingOctaves == 0 ? 0 : value.SmoothingOctaves <= 1.0 / 24 ? 1 : value.SmoothingOctaves <= 1.0 / 12 ? 2 : value.SmoothingOctaves <= 1.0 / 6 ? 3 : 4;
        HistoryCombo.SelectedIndex = Array.IndexOf(new[] { 10, 30, 60 }, value.HistorySeconds);
        TiltSlider.Value = value.TiltDbPerOctave; RangeSlider.Value = value.SpectrogramRange;
        MinFrequencyBox.Text = value.MinFrequency.ToString("0.#", CultureInfo.InvariantCulture); MaxFrequencyBox.Text = value.MaxFrequency.ToString("0.#", CultureInfo.InvariantCulture);
        FloorBox.Text = value.FloorDb.ToString("0", CultureInfo.InvariantCulture); CeilingBox.Text = value.CeilingDb.ToString("0", CultureInfo.InvariantCulture);
        ready = old; PushSettings(value);
    }
    private void PushSettings(AnalysisSettings settings)
    {
        pendingSettings = null; settingsApplied++;
        var previousSettings = engine.Settings;
        engine.Configure(settings); saved.Analysis = engine.Settings;
        foreach (var panel in panels) panel.Settings = engine.Settings;
        UpdateHistorySettings(previousSettings, engine.Settings);
        UpdateParameterLabels(settings);
        if (!interacting || frozen) Redraw();
    }
    private void OnAnalysisChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        var settings = engine.Settings with
        {
            Channel = (ChannelView)Math.Max(0, ChannelCombo.SelectedIndex),
            FftSize = new[] { 2048, 4096, 8192, 16384, 32768, 65536 }[Math.Max(0, FftCombo.SelectedIndex)],
            Window = (WindowKind)Math.Max(0, WindowCombo.SelectedIndex),
            AverageMs = AverageSlider.Value, ReleaseMs = ReleaseSlider.Value,
            SmoothingOctaves = new[] { 0, 1.0 / 24, 1.0 / 12, 1.0 / 6, 1.0 / 3 }[Math.Max(0, SmoothCombo.SelectedIndex)],
            HistorySeconds = new[] { 10, 30, 60 }[Math.Max(0, HistoryCombo.SelectedIndex)],
            TiltDbPerOctave = TiltSlider.Value, SpectrogramRange = RangeSlider.Value
        };
        if (draggingParameter) { pendingSettings = settings; UpdateParameterLabels(settings); }
        else PushSettings(settings);
    }
    private void OnDisplayChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        saved.ShowAverage = AverageCheck.IsChecked == true; saved.ShowPeaks = PeaksCheck.IsChecked == true;
        foreach (var p in panels) { p.ShowAverage = saved.ShowAverage; p.ShowPeaks = saved.ShowPeaks; }
        Redraw();
    }
    private void OnRangeEdited(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (double.TryParse(MinFrequencyBox.Text, CultureInfo.InvariantCulture, out double min) && double.TryParse(MaxFrequencyBox.Text, CultureInfo.InvariantCulture, out double max) &&
            double.TryParse(FloorBox.Text, CultureInfo.InvariantCulture, out double floor) && double.TryParse(CeilingBox.Text, CultureInfo.InvariantCulture, out double ceiling) &&
            double.IsFinite(min) && double.IsFinite(max) && double.IsFinite(floor) && double.IsFinite(ceiling) && max > min && ceiling > floor)
            ApplyControls(engine.Settings with { MinFrequency = min, MaxFrequency = max, FloorDb = floor, CeilingDb = ceiling });
        else { ApplyControls(engine.Settings); stateText = "范围需要有效数字，且上限大于下限"; }
    }
    private void OnRangeKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnRangeEdited(sender, e); e.Handled = true; } }
    private void SetFrequencyRange(double min, double max) { ApplyControls(engine.Settings with { MinFrequency = min, MaxFrequency = Math.Max(min * 1.1, max) }); }
    private void OnResetSettings(object sender, RoutedEventArgs e) => ApplyControls(new());
    private void OnClearPeaks(object sender, RoutedEventArgs e) { engine.ResetPeaks(); stateText = "已重置峰值保持"; }
    private void OnPreset(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (expanded != null) ToggleExpand(expanded.Value);
        int preset = PresetCombo.SelectedIndex;
        LeftColumn.Width = new(preset == 2 ? 1.45 : 2.6, GridUnitType.Star);
        RightColumn.Width = new(1, GridUnitType.Star);
        SpectrumRow.Height = new(preset == 1 ? 1.8 : 1.2, GridUnitType.Star);
        LeftPanels.RowDefinitions[2].Height = new(1, GridUnitType.Star);
        StereoRow.Height = new(preset == 2 ? 1.5 : 1, GridUnitType.Star);
        RightPanels.RowDefinitions[2].Height = new(1.1, GridUnitType.Star);
    }
    private void RestoreLayout()
    {
        PresetCombo.SelectedIndex = 0;
        LeftColumn.Width = new(saved.MainRatio, GridUnitType.Star); RightColumn.Width = new(1, GridUnitType.Star);
        SpectrumRow.Height = new(saved.SpectrumRatio, GridUnitType.Star); LeftPanels.RowDefinitions[2].Height = new(1, GridUnitType.Star);
        StereoRow.Height = new(saved.StereoRatio, GridUnitType.Star); RightPanels.RowDefinitions[2].Height = new(1, GridUnitType.Star);
        foreach (var panel in panels) { panel.ShowAverage = saved.ShowAverage; panel.ShowPeaks = saved.ShowPeaks; }
    }
    private Border BorderFor(InstrumentKind kind) => kind switch { InstrumentKind.Spectrum => SpectrumBorder, InstrumentKind.Spectrogram => SpectrogramBorder, InstrumentKind.Stereo => StereoBorder, _ => LevelsBorder };
    private void ToggleExpand(InstrumentKind kind)
    {
        if (expanded is { } previous)
        {
            panels.First(p => p.Kind == previous).ExpandedView = false;
            var view = expandedHost.Child; expandedHost.Child = null; BorderFor(previous).Child = view;
            expandedHost.Visibility = Visibility.Collapsed; Instruments.Visibility = Visibility.Visible; expanded = null;
            if (kind == previous) return;
        }
        CaptureLayout();
        var border = BorderFor(kind); var child = border.Child; border.Child = null;
        expandedHost.Child = child; Instruments.Visibility = Visibility.Collapsed; expandedHost.Visibility = Visibility.Visible; expanded = kind;
        panels.First(p => p.Kind == kind).ExpandedView = true;
    }
    private void OnFreeze(object sender, RoutedEventArgs e) => ToggleFreeze();
    private void ToggleFreeze()
    {
        canvasReviewStarted = 0;
        frozen = !frozen; foreach (var panel in panels) panel.Frozen = frozen;
        if (frozen) { frozenHistory = frameHistory.Frames.ToArray(); reviewAge = null; }
        else
        {
            frozenHistory = null; reviewAge = null; RebuildSpectrogram();
            displayFrame = null; lastFrameUpdate = 0; SyncAnalysisFrame();
        }
        FreezeButton.Content = frozen ? "Ⅱ 已冻结 F" : "冻结 F";
        FreezeButton.Foreground = new SolidColorBrush(frozen ? Color.FromRgb(255, 255, 255) : Color.FromRgb(221, 221, 221));
        UpdateHistoryControls(); UpdateRenderActivity(); Redraw();
    }
    private void OnFocus(object sender, RoutedEventArgs e)
    {
        saved.ControlsVisible = ControlsPanel.Visibility != Visibility.Visible;
        ControlsPanel.Visibility = saved.ControlsVisible ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnSystem(object sender, RoutedEventArgs e) => StartSystem();
    private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || DeviceCombo.SelectedItem is not OutputDevice device) return;
        saved.DeviceId = device.Id; if (!fileMode) StartSystem();
    }
    private async void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "音频文件|*.wav;*.flac;*.mp3;*.aac;*.m4a;*.ogg;*.opus;*.aif;*.aiff|所有文件|*.*", Title = "打开音频文件" };
        if (ShowFileDialog(dialog) == true) await OpenFile(dialog.FileName);
    }
    private bool? ShowFileDialog(CommonDialog dialog) => RunFileDialog(() => dialog.ShowDialog(this));
    private bool? RunFileDialog(Func<bool?> show)
    {
        fileDialogDepth++;
        try { UpdateRenderActivity(); return show(); }
        finally
        {
            fileDialogDepth--;
            if (fileDialogDepth == 0 && !closing)
            {
                SyncAnalysisFrame(); UpdateHistoryControls(); UpdateRenderActivity(); Redraw();
            }
        }
    }
    private async Task OpenFile(string path)
    {
        if (demo) { demoCancellation.Cancel(); if (demoTask != null) await demoTask; }
        int version = ++fileVersion;
        loadCancellation?.Cancel(); analysisCancellation?.Cancel(); loadCancellation = new(); var token = loadCancellation.Token;
        try
        {
            stateText = "正在打开音频文件…";
            var file = await Ffmpeg.Probe(path, token); token.ThrowIfCancellationRequested();
            if (version != fileVersion) return;
            system.Dispose(); player.Open(file); media = file; fileMode = true;
            engine.Reset(); ClearDisplay(); loopA = loopB = -1; LoopCheck.IsChecked = false;
            FilePanel.Visibility = Visibility.Visible; DeviceCombo.Visibility = Visibility.Collapsed; SourceLabel.FontSize = 13;
            SourceLabel.Text = $"{Path.GetFileName(path)} · {file.SampleRate / 1000.0:0.#} kHz · {file.Codec.ToUpperInvariant()}";
            Overview.SetOverview(null); Overview.Duration = file.Duration; Overview.Progress = 0; Overview.Status = "正在生成整曲概览";
            Overview.LoopA = Overview.LoopB = null; Overview.LoopEnabled = false;
            FileStatsLabel.Text = "整曲分析中…"; CancelAnalysisButton.Visibility = Visibility.Visible;
            player.Toggle(); stateText = "文件分析 · 点击底部概览定位 · 音量不影响分析电平";
            analysisCancellation = new(); var analysisToken = analysisCancellation.Token;
            var progress = new Progress<double>(p => { if (version == fileVersion) { Overview.Progress = p; FileStatsLabel.Text = $"整曲分析 {p:P0}"; } });
            _ = AnalyzeFile(file, version, progress, analysisToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidOperationException or COMException or JsonException or System.ComponentModel.Win32Exception)
        { SettingsStore.Log(e); stateText = "打开失败：" + e.Message; MessageBox.Show(this, e.Message, "无法打开音频", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private async Task AnalyzeFile(MediaInfo file, int version, IProgress<double> progress, CancellationToken token)
    {
        try
        {
            var result = await Task.Run(() => FileAnalysis.Analyze(file, progress, token), token);
            if (version != fileVersion || closing) return;
            Overview.SetOverview(result);
            FileStatsLabel.Text = $"整曲  I {InstrumentView.Format(result.Integrated)} LUFS   TP {InstrumentView.Format(result.TruePeak)} dBTP";
            Overview.Status = "整曲分析完成";
        }
        catch (OperationCanceledException) { if (version == fileVersion && !closing) { FileStatsLabel.Text = "整曲分析已取消"; Overview.Status = "整曲分析已取消 · 点击仍可定位"; } }
        catch (Exception e)
        { SettingsStore.Log(e); if (version == fileVersion && !closing) { FileStatsLabel.Text = "整曲分析失败"; Overview.Status = "整曲分析失败 · 播放不受影响"; stateText = e.Message; } }
        finally { if (version == fileVersion && !closing) CancelAnalysisButton.Visibility = Visibility.Collapsed; }
    }
    private void Seek(double seconds)
    {
        if (media == null) return;
        try { bool playing = player.Playing; player.Seek(seconds, playing); ClearDisplay(); }
        catch (Exception e) when (e is IOException or InvalidOperationException or COMException) { Report(e); }
    }
    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (media == null) return;
        try { if (player.Ended) { player.Seek(loopA >= 0 ? loopA : 0, true); ClearDisplay(); } else player.Toggle(); }
        catch (Exception error) when (error is InvalidOperationException or COMException) { Report(error); }
    }
    private void OnVolume(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!ready) return; saved.Volume = (float)e.NewValue; player.Volume = saved.Volume; }
    private void OnLoopA(object sender, RoutedEventArgs e) { if (media == null) return; loopA = player.Position; if (loopB <= loopA) { loopB = -1; LoopCheck.IsChecked = false; } UpdateLoop(); }
    private void OnLoopB(object sender, RoutedEventArgs e)
    {
        if (media == null) return;
        if (loopA < 0) loopA = 0;
        if (player.Position <= loopA + .1) { stateText = "循环终点需要在起点至少 0.1 秒之后"; return; }
        loopB = player.Position; UpdateLoop();
    }
    private void OnLoopChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (LoopCheck.IsChecked == true && (loopA < 0 || loopB <= loopA)) { LoopCheck.IsChecked = false; stateText = "先用 A、B 设置有效的循环范围"; }
        UpdateLoop();
    }
    private void UpdateLoop() { Overview.LoopA = loopA >= 0 ? loopA : null; Overview.LoopB = loopB >= 0 ? loopB : null; Overview.LoopEnabled = LoopCheck.IsChecked == true; }
    private void OnCancelAnalysis(object sender, RoutedEventArgs e) => analysisCancellation?.Cancel();
    private async void OnDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) await OpenFile(paths[0]); }
    private void OnDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void OnSnapshot(object sender, RoutedEventArgs e)
    {
        if (displayFrame is not { } f) { stateText = "有音频数据后即可保存快照"; return; }
        var shot = new SpectrumSnapshot($"REF {++snapshotNumber:00}", fileMode ? Path.GetFileName(media?.Path ?? "File") : demo ? "测试信号" : system.Description, DateTime.Now, f.SampleRate, f.Settings, (double[])f.Frequencies.Clone(), (float[])f.Spectrum.Clone()) { OctaveBands = (OctaveBandReading[])f.OctaveBands.Clone() };
        if (snapshots.Count == 4) { hiddenSnapshots.Remove(snapshots[0]); snapshots.RemoveAt(0); }
        snapshots.Add(shot); RebuildSnapshots(); SaveSnapshots();
    }
    private void RebuildSnapshots()
    {
        SnapshotBar.Children.Clear(); SnapshotBar.Visibility = snapshots.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var shot in snapshots)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            var check = new CheckBox { Content = $"{shot.Name} · {Shorten(shot.Source, 24)}", IsChecked = !hiddenSnapshots.Contains(shot), ToolTip = $"{shot.Source}\n{shot.Created:g} · {shot.Settings.Channel} · FFT {shot.Settings.FftSize} · {shot.Settings.Window}" };
            check.Checked += (_, _) => { hiddenSnapshots.Remove(shot); UpdateSnapshots(); }; check.Unchecked += (_, _) => { hiddenSnapshots.Add(shot); UpdateSnapshots(); };
            var remove = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), ToolTip = "删除参考快照" };
            remove.Click += (_, _) => { snapshots.Remove(shot); hiddenSnapshots.Remove(shot); RebuildSnapshots(); SaveSnapshots(); };
            row.Children.Add(check); row.Children.Add(remove); SnapshotBar.Children.Add(row);
        }
        UpdateSnapshots();
    }
    private static string Shorten(string text, int count) => text.Length > count ? text[..count] + "…" : text;
    private void UpdateSnapshots() { Spectrum.Snapshots = snapshots.Where(s => !hiddenSnapshots.Contains(s)).ToArray(); Redraw(); }
    private void SaveSnapshots()
    {
        try { Directory.CreateDirectory(SettingsStore.Root); File.WriteAllText(Path.Combine(SettingsStore.Root, "snapshots.json"), JsonSerializer.Serialize(snapshots, SettingsStore.Json)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Report(e); }
    }
    private static bool ValidSnapshot(SpectrumSnapshot s) => s is not null && s.Settings is not null && !string.IsNullOrEmpty(s.Source) && !string.IsNullOrEmpty(s.Name) && s.Source.Length < 2000 && s.Name.Length < 200 && s.SampleRate is >= 8000 and <= 384000 && s.Frequencies is { Length: > 1 and <= 32769 } && s.Spectrum?.Length == s.Frequencies.Length && s.Frequencies.All(double.IsFinite) && s.Frequencies[0] > 0 && s.Spectrum.All(float.IsFinite) && s.Frequencies.Zip(s.Frequencies.Skip(1)).All(pair => pair.First < pair.Second) && ValidOctaveBands(s.OctaveBands);
    private void LoadSnapshots()
    {
        try
        {
            string path = Path.Combine(SettingsStore.Root, "snapshots.json");
            if (File.Exists(path) && new FileInfo(path).Length < 4 * 1024 * 1024) snapshots.AddRange((JsonSerializer.Deserialize<List<SpectrumSnapshot>>(File.ReadAllText(path), SettingsStore.Json) ?? []).Where(ValidSnapshot).TakeLast(4));
            snapshotNumber = snapshots.Count; RebuildSnapshots();
        }
        catch (Exception e) when (e is IOException or JsonException) { SettingsStore.Log(e); }
    }
    private static void AddMenu(ContextMenu menu, string label, Action action, bool? check = null)
    {
        var item = new MenuItem { Header = label, IsCheckable = check.HasValue, IsChecked = check ?? false }; item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    public void SaveScreenshot(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bool wasFrozen = frozen; frozen = true;
        try
        {
            UpdateLayout();
            bitmap.Render(this);
            // D3DImage readback varies by driver. Re-render instruments through the
            // same Skia drawing code onto CPU surfaces, keeping one consistent frame.
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
                foreach (var panel in panels.Where(p => p.IsVisible && p.ActualWidth > 0 && p.ActualHeight > 0))
                {
                    var point = panel.TranslatePoint(new Point(0, 0), this);
                    context.DrawImage(panel.CaptureBitmap(dpi.DpiScaleX), new Rect(point.X, point.Y, panel.ActualWidth, panel.ActualHeight));
                }
                if (HistoryToolbar.IsVisible)
                {
                    var point = HistoryToolbar.TranslatePoint(new Point(0, 0), this);
                    context.DrawRectangle(new VisualBrush(HistoryToolbar), null, new Rect(point.X, point.Y, HistoryToolbar.ActualWidth, HistoryToolbar.ActualHeight));
                }
            }
            var composite = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            composite.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(composite)); using var file = File.Create(path); encoder.Save(file);
        }
        finally { frozen = wasFrozen; }
    }
    private void ExportScreenshot()
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"VisualizerPro-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (ShowFileDialog(dialog) == true) { try { SaveScreenshot(dialog.FileName); stateText = "已导出截图"; } catch (IOException e) { Report(e); } }
    }
    private void ExportCsv()
    {
        if (displayFrame is not { } f) return;
        var dialog = new SaveFileDialog { Filter = "CSV 频谱|*.csv", FileName = $"spectrum-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (ShowFileDialog(dialog) != true) return;
        try
        {
            var csv = new StringBuilder("FrequencyHz,LevelDbfs,AverageDbfs,PeakDbfs,Channel,FftSize,Window\r\n");
            for (int i = 0; i < f.Spectrum.Length; i++) csv.AppendLine(FormattableString.Invariant($"{f.Frequencies[i]:F5},{f.Spectrum[i]:F5},{f.Average[i]:F5},{f.Peaks[i]:F5},{f.Settings.Channel},{f.Settings.FftSize},{f.Settings.Window}"));
            File.WriteAllText(dialog.FileName, Spectrum.SpectrumMode == SpectrumDisplayMode.ThirdOctave ? OctaveCsv(f) : csv.ToString(), new UTF8Encoding(true)); stateText = "已导出频谱，数值未叠加显示斜率";
        }
        catch (IOException e) { Report(e); }
    }
    private void ExportSnapshots()
    {
        var dialog = new SaveFileDialog { Filter = "参考快照|*.json", FileName = "VisualizerPro-references.json" };
        if (ShowFileDialog(dialog) == true) try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(snapshots, SettingsStore.Json)); } catch (IOException e) { Report(e); }
    }
    private void ImportSnapshots()
    {
        var dialog = new OpenFileDialog { Filter = "参考快照|*.json" }; if (ShowFileDialog(dialog) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024) throw new InvalidDataException("参考文件过大。");
            var imported = JsonSerializer.Deserialize<List<SpectrumSnapshot>>(File.ReadAllText(dialog.FileName), SettingsStore.Json) ?? [];
            if (imported.Any(s => !ValidSnapshot(s))) throw new InvalidDataException("参考文件包含无效数据。");
            snapshots.AddRange(imported); while (snapshots.Count > 4) snapshots.RemoveAt(0); RebuildSnapshots(); SaveSnapshots();
        }
        catch (Exception e) when (e is IOException or JsonException) { Report(e); }
    }
    private void OnMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        AddMenu(menu, "保存参考快照  S", () => OnSnapshot(this, new RoutedEventArgs()));
        AddMenu(menu, "导出仪表截图 PNG…", () => ExportScreenshot());
        AddMenu(menu, "导出当前频谱 CSV…", ExportCsv);
        AddMenu(menu, "导出参考快照 JSON…", ExportSnapshots);
        AddMenu(menu, "导入参考快照 JSON…", ImportSnapshots);
        menu.Items.Add(new Separator());
        AddMenu(menu, fullScreen ? "退出全屏  F11" : "进入全屏  F11", () => SetFullScreen(!fullScreen));
        AddMenu(menu, "窗口置顶", () => Topmost = !Topmost, Topmost);
        AddMenu(menu, "收起到托盘", Hide);
        AddMenu(menu, "重置本次测量", () => { engine.Reset(); ClearDisplay(); });
        AddMenu(menu, "清理整曲分析缓存", () =>
        {
            try { if (Directory.Exists(SettingsStore.Cache)) foreach (string path in Directory.EnumerateFiles(SettingsStore.Cache, "*.json")) File.Delete(path); stateText = "已清理整曲分析缓存"; }
            catch (IOException e) { Report(e); }
        });
        AddMenu(menu, "快捷键与读数说明  F1", ShowShortcutGuide);
        if (sender is FrameworkElement owner) owner.ContextMenu = menu;
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { ShowShortcutGuide(); e.Handled = true; return; }
        if (e.Key != Key.F11 && e.Key != Key.Escape && (Keyboard.FocusedElement is TextBox || ModeSelectorOpen)) return;
        if (e.Key == Key.Escape && ModeSelectorOpen) return;
        switch (e.Key)
        {
            case Key.F11: SetFullScreen(!fullScreen); break;
            case Key.Escape: if (expanded is { } kind) ToggleExpand(kind); else if (fullScreen) SetFullScreen(false); break;
            case Key.F: ToggleFreeze(); break;
            case Key.D1 or Key.NumPad1 when Keyboard.Modifiers == ModifierKeys.None: if (!e.IsRepeat) CyclePanelMode(InstrumentKind.Spectrum); break;
            case Key.D2 or Key.NumPad2 when Keyboard.Modifiers == ModifierKeys.None: if (!e.IsRepeat) CyclePanelMode(InstrumentKind.Spectrogram); break;
            case Key.D3 or Key.NumPad3 or Key.V when Keyboard.Modifiers == ModifierKeys.None: if (!e.IsRepeat) CyclePanelMode(InstrumentKind.Stereo); break;
            case Key.S: OnSnapshot(this, e); break;
            case Key.Tab: OnFocus(this, e); break;
            case Key.Space: if (fileMode) OnPlay(this, e); else return; break;
            case Key.O when Keyboard.Modifiers == ModifierKeys.Control: OnOpenFile(this, e); break;
            default: return;
        }
        e.Handled = true;
    }
    private void SetFullScreen(bool enabled)
    {
        if (enabled == fullScreen) return;
        if (enabled)
        {
            windowBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            var screen = CurrentScreen();
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            fullScreen = true; PositionOn(screen);
        }
        else
        {
            fullScreen = false; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            MinWidth = 1120; MinHeight = 740;
            Left = windowBounds.Left; Top = windowBounds.Top; Width = windowBounds.Width; Height = windowBounds.Height;
        }
    }
    private Forms.Screen CurrentScreen() => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == saved.Monitor) ?? Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
    private void PositionOn(Forms.Screen screen)
    {
        var b = screen.Bounds; var handle = new WindowInteropHelper(this).Handle;
        double scale = GetDpiForWindow(handle) / 96.0;
        if (scale <= 0) scale = 1;
        MinWidth = MinHeight = 0;
        // Update WPF's values too; otherwise its pending layout restores the old
        // Left/Top after SetWindowPos, shifting the first fullscreen down/right.
        Left = b.Left / scale; Top = b.Top / scale; Width = b.Width / scale; Height = b.Height / scale;
        SetWindowPos(handle, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, 0x0004 | 0x0010 | 0x0020); saved.Monitor = screen.DeviceName;
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Preserve the last composited frame during the native move/resize loop.
        // D3D/OpenGL interop locks otherwise compete with DWM and stall dragging.
        if (message == 0x0231) { movingWindow = true; UpdateRenderActivity(); }
        else if (message == 0x0232) { movingWindow = false; SyncAnalysisFrame(); UpdateRenderActivity(); Redraw(); }
        else if (message == 0x02E0 && fullScreen)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (fullScreen && !closing) PositionOn(CurrentScreen()); });
        return IntPtr.Zero;
    }
    private void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == saved.Monitor) ?? Forms.Screen.PrimaryScreen;
        if (screen == null) return;
        if (fullScreen) PositionOn(screen);
        else
        {
            var handle = new WindowInteropHelper(this).Handle;
            GetWindowRect(handle, out var bounds);
            int x = Math.Clamp(bounds.Left, screen.WorkingArea.Left, screen.WorkingArea.Right - Math.Min(200, screen.WorkingArea.Width));
            int y = Math.Clamp(bounds.Top, screen.WorkingArea.Top, screen.WorkingArea.Bottom - Math.Min(100, screen.WorkingArea.Height));
            double scale = Math.Max(1, GetDpiForWindow(handle)) / 96.0;
            Left = x / scale; Top = y / scale;
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }
    });
    private void CreateTray()
    {
        using var icon = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(icon), Text = "Visualizer Pro", Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("打开观察台", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); })); menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close)); tray.ContextMenuStrip = menu;
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnExit(object sender, RoutedEventArgs e) => Close();
    private void Report(Exception e) { SettingsStore.Log(e); stateText = e.Message; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return; FlushSettings(); closing = true; timer.Stop();
        windowSource?.RemoveHook(WindowMessage); deviceMonitor.Dispose();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        loadCancellation?.Cancel(); analysisCancellation?.Cancel(); demoCancellation.Cancel();
        saved.FullScreen = fullScreen; saved.Topmost = Topmost;
        if (!fullScreen && WindowState == WindowState.Normal) windowBounds = new(Left, Top, ActualWidth, ActualHeight);
        saved.Left = windowBounds.Left; saved.Top = windowBounds.Top; saved.Width = windowBounds.Width; saved.Height = windowBounds.Height;
        if (expanded == null) CaptureLayout();
        try { if (!demo) SettingsStore.Save(saved); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { SettingsStore.Log(error); }
        system.Dispose(); player.Dispose(); demoTask?.GetAwaiter().GetResult(); engine.Dispose();
        tray?.Dispose(); foreach (var panel in panels) panel.Dispose(); Overview.Dispose();
        loadCancellation?.Dispose(); analysisCancellation?.Dispose(); demoCancellation.Dispose();
    }
    private void CaptureLayout()
    {
        if (LeftColumn.ActualWidth <= 0 || RightColumn.ActualWidth <= 0) return;
        saved.MainRatio = LeftColumn.ActualWidth / RightColumn.ActualWidth;
        saved.SpectrumRatio = SpectrumRow.ActualHeight / Math.Max(1, LeftPanels.RowDefinitions[2].ActualHeight);
        saved.StereoRatio = StereoRow.ActualHeight / Math.Max(1, RightPanels.RowDefinitions[2].ActualHeight);
    }
    private async Task DemoLoop()
    {
        const int rate = 48000, count = 1024; long sample = 0;
        try
        {
            while (!demoCancellation.IsCancellationRequested)
            {
                var values = new float[count * 2];
                for (int i = 0; i < count; i++, sample++)
                {
                    double t = sample / (double)rate, pulse = .45 + .4 * Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.7)), 7);
                    double l = 0, r = 0;
                    for (int harmonic = 1; harmonic <= 24; harmonic++)
                    {
                        double frequency = 55 * harmonic * (1 + .02 * Math.Sin(t * .3)), amplitude = pulse * .22 / Math.Pow(harmonic, .8);
                        l += amplitude * Math.Sin(2 * Math.PI * frequency * t); r += amplitude * Math.Sin(2 * Math.PI * frequency * t + Math.Sin(harmonic + t * .2) * .5);
                    }
                    l += .05 * Math.Sin(2 * Math.PI * (1500 * t + 150 * Math.Sin(t * .5)));
                    r += .05 * Math.Sin(2 * Math.PI * (1600 * t + 130 * Math.Sin(t * .5)));
                    values[2 * i] = (float)l; values[2 * i + 1] = (float)r;
                }
                engine.Submit(values, rate); await Task.Delay(21, demoCancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
}
