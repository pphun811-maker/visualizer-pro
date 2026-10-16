using System.Text;
using System.Windows;
using System.Windows.Controls;
using VisualizerPro.App.Views;
using VisualizerPro.Core;

namespace VisualizerPro.App;

public partial class MainWindow
{
    private static bool ValidOctaveBands(OctaveBandReading[]? bands) => bands is { Length: <= 31 } && bands.All(b => double.IsFinite(b.CenterHz) && double.IsFinite(b.LowHz) && double.IsFinite(b.HighHz) && b.CenterHz > 0 && b.LowHz > 0 && b.HighHz > b.LowHz && double.IsFinite(b.LevelDbfs) && double.IsFinite(b.AverageDbfs) && double.IsFinite(b.PeakDbfs)) && bands.Zip(bands.Skip(1)).All(pair => pair.First.CenterHz < pair.Second.CenterHz);
    private void OnSpectrumMode(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        saved.SpectrumMode = Math.Clamp(SpectrumModeCombo.SelectedIndex, 0, 1);
        Spectrum.SpectrumMode = (SpectrumDisplayMode)saved.SpectrumMode;
        Redraw();
    }

    private void OnHistoryMode(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        saved.HistoryMode = Math.Clamp(HistoryModeCombo.SelectedIndex, 0, 1);
        Spectrogram.HistoryMode = (HistoryDisplayMode)saved.HistoryMode;
        Redraw();
    }

    private void OnResetWaterfallView(object sender, RoutedEventArgs e) => ApplyDisplayControls(saved.Display with { WaterfallAzimuth = 32, WaterfallElevation = 35 });

    internal static string OctaveCsv(AnalysisFrame frame)
    {
        var csv = new StringBuilder("CenterHz,LowerHz,UpperHz,RmsDbfs,AverageRmsDbfs,PeakHoldRmsDbfs,Channel,FftSize,Window\r\n");
        foreach (var band in frame.OctaveBands)
            csv.AppendLine(FormattableString.Invariant($"{band.CenterHz:F5},{band.LowHz:F5},{band.HighHz:F5},{band.LevelDbfs:F5},{band.AverageDbfs:F5},{band.PeakDbfs:F5},{frame.Settings.Channel},{frame.Settings.FftSize},{frame.Settings.Window}"));
        return csv.ToString();
    }
}
