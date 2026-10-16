using System.Windows;
using VisualizerPro.App.Services;

namespace VisualizerPro.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            SettingsStore.Log(args.Exception);
            if (MainWindow is MainWindow current && current.TrySoftwareRendering(args.Exception)) { args.Handled = true; return; }
            MessageBox.Show(args.Exception.Message, "Visualizer Pro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        bool smoke = e.Args.Contains("--smoke-test");
        int fileDialog = Array.IndexOf(e.Args, "--file-dialog-test");
        var window = new MainWindow(smoke || e.Args.Contains("--demo") || e.Args.Contains("--performance-test") || e.Args.Contains("--input-test") || e.Args.Contains("--visual-test") || e.Args.Contains("--exploration-test") || e.Args.Contains("--spectral-test")); MainWindow = window; window.Show();
        if (smoke)
        {
            _ = CaptureSmoke(window, e.Args);
        }
        int integration = Array.IndexOf(e.Args, "--integration-test");
        if (fileDialog >= 0 && e.Args.Length > fileDialog + 1) _ = window.RunFileDialogChecks(Path.GetFullPath(e.Args[fileDialog + 1]));
        if (integration >= 0 && e.Args.Length > integration + 1) _ = window.RunIntegration(Path.GetFullPath(e.Args[integration + 1]));
        int input = Array.IndexOf(e.Args, "--input-test");
        if (input >= 0 && e.Args.Length > input + 1) _ = window.RunInputChecks(Path.GetFullPath(e.Args[input + 1]), e.Args.Contains("--native-input"));
        int visual = Array.IndexOf(e.Args, "--visual-test");
        if (visual >= 0 && e.Args.Length > visual + 1) _ = window.RunVisualChecks(Path.GetFullPath(e.Args[visual + 1]));
        int exploration = Array.IndexOf(e.Args, "--exploration-test");
        if (exploration >= 0 && e.Args.Length > exploration + 1) _ = window.RunExplorationChecks(Path.GetFullPath(e.Args[exploration + 1]));
        int spectral = Array.IndexOf(e.Args, "--spectral-test");
        if (spectral >= 0 && e.Args.Length > spectral + 1) _ = window.RunSpectralChecks(Path.GetFullPath(e.Args[spectral + 1]));
        int responsiveness = Array.IndexOf(e.Args, "--responsiveness-test");
        if (responsiveness >= 0 && e.Args.Length > responsiveness + 1) _ = window.RunResponsiveness(Path.GetFullPath(e.Args[responsiveness + 1]));
        int performance = Array.IndexOf(e.Args, "--performance-test");
        if (performance >= 0 && e.Args.Length > performance + 2) _ = window.RunPerformance(int.Parse(e.Args[performance + 1], CultureInfo.InvariantCulture), Path.GetFullPath(e.Args[performance + 2]));
    }
    private static async Task CaptureSmoke(MainWindow window, string[] args)
    {
        try
        {
            await Task.Delay(5000);
            int index = Array.IndexOf(args, "--smoke-test");
            string path = args.Length > index + 1 ? Path.GetFullPath(args[index + 1]) : Path.Combine(AppContext.BaseDirectory, "smoke-test.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); window.SaveScreenshot(path);
            await File.WriteAllTextAsync(path + ".ok", "UI rendered successfully");
        }
        catch (Exception error) { SettingsStore.Log(error); }
        finally { window.Close(); }
    }
}
