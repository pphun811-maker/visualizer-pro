namespace VisualizerPro.Core;

// At most twenty immutable analysis snapshots per second, bounded to 60 seconds.
// Frozen readers own a snapshot of references while the live buffer advances.
public sealed class FrameHistory
{
    private readonly List<AnalysisFrame> frames = [];
    private double lastSlot = -1;
    public IReadOnlyList<AnalysisFrame> Frames => frames;
    public double LatestTime => frames.Count == 0 ? 0 : frames[^1].Time;
    public int Count => frames.Count;
    public void Clear() { frames.Clear(); lastSlot = -1; }

    public AnalysisFrame? Add(AnalysisFrame frame, int seconds)
    {
        var previous = frames.LastOrDefault();
        if (previous != null && (frame.Time < previous.Time - 1e-6 || frame.SampleRate != previous.SampleRate || frame.Settings.FftSize != previous.Settings.FftSize || frame.Settings.Window != previous.Settings.Window || frame.Settings.Channel != previous.Settings.Channel || frame.Settings.SmoothingOctaves != previous.Settings.SmoothingOctaves)) Clear();
        if (lastSlot < 0) lastSlot = frame.Time - .05;
        int advance = (int)((frame.Time - lastSlot) * 20 + 1e-6);
        if (advance < 1) return null;
        lastSlot += advance * .05;
        var entry = frame with { Time = lastSlot };
        frames.Add(entry);
        Trim(seconds);
        return entry;
    }

    public void Trim(int seconds)
    {
        seconds = Math.Clamp(seconds, 1, 60);
        int capacity = seconds * 20;
        int expired = 0;
        while (expired < frames.Count && (frames.Count - expired > capacity || frames[^1].Time - frames[expired].Time >= seconds)) expired++;
        if (expired > 0) frames.RemoveRange(0, expired);
    }

    public static AnalysisFrame? AtAge(IReadOnlyList<AnalysisFrame> source, double age)
    {
        if (source.Count == 0 || !double.IsFinite(age) || age < 0) return null;
        double time = source[^1].Time - age;
        int lo = 0, hi = source.Count - 1;
        while (lo < hi) { int mid = (lo + hi) / 2; if (source[mid].Time < time) lo = mid + 1; else hi = mid; }
        if (lo > 0 && Math.Abs(source[lo - 1].Time - time) < Math.Abs(source[lo].Time - time)) lo--;
        return Math.Abs(source[lo].Time - time) <= .051 ? source[lo] : null;
    }
}
