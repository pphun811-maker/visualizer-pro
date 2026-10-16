using System.Text.Json;

namespace VisualizerPro.App.Services;

public sealed record MediaInfo(string Path, double Duration, int SampleRate, int Channels, string Codec);

public static class Ffmpeg
{
    public static string Find(string name)
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", name + ".exe");
        if (File.Exists(bundled)) return bundled;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 7 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tools", "ffmpeg", name + ".exe");
            if (File.Exists(candidate)) return candidate;
        }
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(entry, name + ".exe"); if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("找不到 FFmpeg 解码组件。请使用完整便携包，或运行 scripts/Get-FFmpeg.ps1。");
    }
    public static Process Start(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Find(executable)) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info };
        process.Start(); return process;
    }
    public static async Task<MediaInfo> Probe(string path, CancellationToken token)
    {
        using var process = Start("ffprobe", ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=sample_rate,channels,codec_name,duration:format=duration", "-of", "json", path]);
        using var registration = token.Register(() => Kill(process));
        Task<string> errors = process.StandardError.ReadToEndAsync(token);
        string json = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidDataException(await errors);
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new InvalidDataException("文件中没有可播放的音频。");
        var stream = streams[0];
        int rate = int.Parse(stream.GetProperty("sample_rate").GetString()!, CultureInfo.InvariantCulture);
        double duration = 0;
        if (document.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var value)) double.TryParse(value.GetString(), CultureInfo.InvariantCulture, out duration);
        if (duration <= 0 && stream.TryGetProperty("duration", out var streamDuration)) double.TryParse(streamDuration.GetString(), CultureInfo.InvariantCulture, out duration);
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("无法读取文件时长。");
        return new(path, duration, Math.Clamp(rate, 8000, 192000), stream.GetProperty("channels").GetInt32(), stream.GetProperty("codec_name").GetString() ?? "audio");
    }
    public static Process Decode(MediaInfo media, double position = 0)
    {
        var process = Start("ffmpeg", ["-v", "error", "-nostdin", "-ss", position.ToString("0.000000", CultureInfo.InvariantCulture), "-i", media.Path, "-map", "0:a:0", "-vn", "-f", "f32le", "-acodec", "pcm_f32le", "-ac", "2", "-ar", media.SampleRate.ToString(CultureInfo.InvariantCulture), "pipe:1"]);
        return process;
    }
    public static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
    }
}
