using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisualizerPro.App.Views;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private Window? shortcutsWindow;
    private bool ModeSelectorOpen => new[] { DeviceCombo, PresetCombo, SpectrumModeCombo, ChannelCombo, HistoryCombo, HistoryModeCombo, SecondaryInstrumentCombo, FftCombo, WindowCombo, SmoothCombo, ResponsePresetCombo, WaveformWindowCombo, WaterfallWindowCombo, PaletteCombo }.Any(c => c.IsDropDownOpen);
    private const string ShortcutGuide = "面板切换\n\n1：FFT 频谱 ↔ 1/3 倍频程\n2：二维声谱 ↔ 三维瀑布\n3 或 V：立体声场 → 分频段声场 → 波形示波器\n数字键支持主键盘和数字小键盘，也可使用顶部模式下拉框。\n\n显示与操作\n\nF：冻结 / 恢复显示，音频与分析继续\nS：保存当前频谱参考快照\nTab：隐藏 / 显示顶部控制区，面板按钮仍可用\nF11：全屏 / 窗口\nEsc：还原放大面板 / 退出全屏；在本说明中关闭说明\nF1：打开本说明\n双击仪表或点击“放大 / 还原”：切换单面板大小\n\n查看数据\n\n鼠标移入曲线或曲面：图内显示频率和电平，冻结时仍可读取\n频谱拖动：选择频段；Shift + 拖动：缩放频率\n滚轮：缩放频率；图内右键：恢复频率范围\n点击 / 拖动声谱或下方滑块：同步回看各仪表，音频位置不变\n“回到实时”或 F：结束回看\n三维瀑布 Alt + 拖动：调整视角\n“显示设置”：设置三维 4 / 8 / 16 秒时间窗、复位视角及显示响应\n\n文件播放\n\nCtrl+O：打开文件，也可拖入音频文件\n空格：文件播放 / 暂停\n点击整曲概览：定位；A / B 按钮：设置循环边界\n\n读数\n\nFFT：dBFS 峰值幅度，频率平滑影响窄带读数。\n倍频程：频带能量 dBFS RMS，与 FFT 峰值不同；淡色柱表示 FFT 分辨率不足。\n电平：400 ms RMS、衰减 Peak；TP 为本次测量最大真峰值。\n响度：400 ms M / 3 s S / 双门限 I（BS.1770）。\n声场：M 纵轴 / S 横轴，显示自动增益不改变测量值。\n多声道输入降混为立体声，不计 LFE。";

    private void CyclePanelMode(InstrumentKind kind)
    {
        switch (kind)
        {
            case InstrumentKind.Spectrum: SpectrumModeCombo.SelectedIndex = (SpectrumModeCombo.SelectedIndex + 1) % 2; break;
            case InstrumentKind.Spectrogram: HistoryModeCombo.SelectedIndex = (HistoryModeCombo.SelectedIndex + 1) % 2; break;
            case InstrumentKind.Stereo: SecondaryInstrumentCombo.SelectedIndex = (SecondaryInstrumentCombo.SelectedIndex + 1) % 3; break;
        }
    }

    private void OnHelp(object sender, RoutedEventArgs e) => ShowShortcutGuide();

    private void ShowShortcutGuide()
    {
        if (shortcutsWindow != null) { shortcutsWindow.Activate(); return; }
        shortcutsWindow = new Window
        {
            Owner = this, Title = "快捷键与读数说明", Width = 620, Height = 700, MinWidth = 450, MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = ShortcutGuide, TextWrapping = TextWrapping.Wrap, Margin = new(24), FontSize = 13, LineHeight = 23 } }
        };
        shortcutsWindow.PreviewKeyDown += (_, e) => { if (e.Key is Key.Escape or Key.F1) { shortcutsWindow?.Close(); e.Handled = true; } };
        shortcutsWindow.Closed += (_, _) => shortcutsWindow = null;
        shortcutsWindow.Show();
    }
}
