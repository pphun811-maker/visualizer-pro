using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisualizerPro.Core;

namespace VisualizerPro.App.Services;

public sealed record OverviewColumn(double Time, float[] Spectrum, double Loudness);
public sealed record FileOverview(double Duration, int SampleRate, double Integrated, double TruePeak,
    List<OverviewColumn> Columns);

public static class FileAnalysis
{
    public static async Task<FileOverview> Analyze(MediaInfo media, IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(SettingsStore.Cache);
        var info = new FileInfo(media.Path);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v3|{media.Path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")));
        string cache = Path.Combine(SettingsStore.Cache, key + ".json");
        if (File.Exists(cache))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<FileOverview>(await File.ReadAllTextAsync(cache, token), SettingsStore.Json);
                if (saved != null) { File.SetLastWriteTimeUtc(cache, DateTime.UtcNow); progress?.Report(1); return saved; }
            }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        var analyzer = new Analyzer(media.SampleRate, new AnalysisSettings { FftSize = 4096, SmoothingOctaves = 0, AverageMs = 0, ReleaseMs = 0 });
        var columns = new List<OverviewColumn>();
        double interval = Math.Max(.1, media.Duration / 2400), nextColumn = 0;
        AnalysisFrame? frame = null;
        using var decoder = Ffmpeg.Decode(media);
        using var registration = token.Register(() => Ffmpeg.Kill(decoder));
        Task<string> stderr = decoder.StandardError.ReadToEndAsync();
        var bytes = new byte[8192 * 8];
        long frames = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = 0;
            while (read < bytes.Length)
            {
                int count = await decoder.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(read, bytes.Length - read), token);
                if (count == 0) break; read += count;
            }
            read -= read % 8; if (read == 0) break;
            var samples = new float[read / 4]; Buffer.BlockCopy(bytes, 0, samples, 0, read);
            frame = analyzer.Add(samples) ?? frame; frames += read / 8;
            double time = frames / (double)media.SampleRate;
            if (frame != null && time >= nextColumn)
            {
                var bins = new float[128];
                for (int b = 0; b < bins.Length; b++) bins[b] = frame.Spectrum[b * (Analyzer.Bins - 1) / (bins.Length - 1)];
                columns.Add(new(time, bins, frame.Meters.Momentary)); nextColumn = time + interval;
                progress?.Report(Math.Min(.999, time / media.Duration));
            }
        }
        await decoder.WaitForExitAsync(token);
        if (decoder.ExitCode != 0) throw new InvalidDataException(await stderr);
        if (frames == 0) throw new InvalidDataException("音频中没有可分析的数据。");
        frame = analyzer.Finish();
        var result = new FileOverview(media.Duration, media.SampleRate, frame.Meters.Integrated,
            Math.Max(frame.Meters.TruePeakLeft, frame.Meters.TruePeakRight), columns);
        string temp = cache + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(result, SettingsStore.Json), token); token.ThrowIfCancellationRequested(); File.Move(temp, cache, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        TrimCache(); progress?.Report(1); return result;
    }
    public static void TrimCache()
    {
        if (!Directory.Exists(SettingsStore.Cache)) return;
        var files = new DirectoryInfo(SettingsStore.Cache).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long bytes = 0;
        foreach (var file in files) { bytes += file.Length; if (bytes > 512L * 1024 * 1024) file.Delete(); }
    }
}
