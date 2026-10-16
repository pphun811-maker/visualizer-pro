using System.Text.Json;
using VisualizerPro.Core;

namespace VisualizerPro.App.Services;

public sealed class UserSettings
{
    public AnalysisSettings Analysis { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public int SecondaryInstrument { get; set; }
    public int SpectrumMode { get; set; }
    public int HistoryMode { get; set; }
    public bool FullScreen { get; set; } = true;
    public bool Topmost { get; set; }
    public bool ControlsVisible { get; set; } = true;
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 60;
    public double Width { get; set; } = 1440;
    public double Height { get; set; } = 900;
    public string Monitor { get; set; } = "";
    public double MainRatio { get; set; } = 2.6;
    public double SpectrumRatio { get; set; } = 1.2;
    public double StereoRatio { get; set; } = 1;
    public string? DeviceId { get; set; }
    public float Volume { get; set; } = .7f;
    public bool ShowAverage { get; set; } = true;
    public bool ShowPeaks { get; set; } = true;
}

public static class SettingsStore
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("VISUALIZERPRO_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualizerPro");
    public static string Cache => Path.Combine(Root, "Cache");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public static UserSettings Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(Root, "settings.json")), Json) ?? new();
            result.Analysis = (result.Analysis ?? new()).Sanitize();
            result.Display = (result.Display ?? new()).Sanitize();
            result.SecondaryInstrument = Math.Clamp(result.SecondaryInstrument, 0, 2);
            result.SpectrumMode = Math.Clamp(result.SpectrumMode, 0, 1);
            result.HistoryMode = Math.Clamp(result.HistoryMode, 0, 1);
            if (!double.IsFinite(result.MainRatio) || result.MainRatio < .5 || result.MainRatio > 8) result.MainRatio = 2.6;
            if (!double.IsFinite(result.SpectrumRatio) || result.SpectrumRatio < .2 || result.SpectrumRatio > 5) result.SpectrumRatio = 1.2;
            if (!double.IsFinite(result.StereoRatio) || result.StereoRatio < .2 || result.StereoRatio > 5) result.StereoRatio = 1;
            result.Volume = float.IsFinite(result.Volume) ? Math.Clamp(result.Volume, 0, 1) : .7f;
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Root);
        string path = Path.Combine(Root, "settings.json"), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json)); File.Move(temp, path, true);
    }
    public static void Log(Exception e)
    {
        try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root, "errors.log"), $"{DateTime.Now:O} {e}\n"); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
