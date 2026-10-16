using System.Diagnostics;
using System.Text.Json;
using VisualizerPro.Core;

internal static class Stress
{
    public static int Run(double seconds, string? output)
    {
        const int rate = 48000, chunk = 2048;
        var samples = new float[chunk * 2];
        for (int i = 0; i < chunk; i++) { samples[2 * i] = (float)(.25 * Math.Sin(2 * Math.PI * 375 * i / rate)); samples[2 * i + 1] = (float)(.25 * Math.Sin(2 * Math.PI * 375 * i / rate + .3)); }
        var analyzer = new Analyzer(rate); var watch = Stopwatch.StartNew();
        long processed = 0, baseline = 0, maximum = 0; double nextSample = 30, nextProgress = 600;
        AnalysisFrame? last = null;
        while (processed < seconds * rate)
        {
            last = analyzer.Add(samples) ?? last; processed += chunk;
            double time = processed / (double)rate;
            if (time >= nextSample)
            {
                long memory = GC.GetTotalMemory(true); if (baseline == 0) baseline = memory; maximum = Math.Max(maximum, memory);
                nextSample += 30;
            }
            if (time >= nextProgress)
            {
                Console.WriteLine($"Processed {time / 60:0} min of audio in {watch.Elapsed.TotalSeconds:0.0}s; retained {maximum / 1048576.0:0.0} MiB"); nextProgress += 600;
            }
        }
        long growth = GC.GetTotalMemory(true) - baseline;
        bool pass = growth < 4 * 1024 * 1024 && last != null && double.IsFinite(last.Meters.Integrated) && last.Spectrum.All(float.IsFinite);
        var result = new { pass, virtualAudioSeconds = processed / (double)rate, wallSeconds = watch.Elapsed.TotalSeconds, baselineBytes = baseline, maximumRetainedBytes = maximum, retainedGrowthBytes = growth, integrated = last?.Meters.Integrated };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }); Console.WriteLine(json);
        if (output != null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output, json); }
        return pass ? 0 : 1;
    }
}
