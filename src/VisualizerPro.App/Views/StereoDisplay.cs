using VisualizerPro.Core;

namespace VisualizerPro.App.Views;

// Presentation state advances with audio time, never with repaint frequency.
internal sealed class StereoDisplay
{
    internal sealed record Trace(AnalysisFrame Frame, double Gain, double Sensitivity, double Ceiling)
    {
        public double DisplayGain(double sensitivity) => Math.Min(Ceiling, Gain * sensitivity / Sensitivity);
    }
    private readonly List<Trace> traces = [];
    public IReadOnlyList<Trace> Traces => traces;
    public double Gain { get; private set; } = 1;
    public DisplaySettings Settings { get; set; } = new();

    public void Reset() { traces.Clear(); Gain = 1; }

    public void Accept(AnalysisFrame frame)
    {
        var previous = traces.LastOrDefault()?.Frame;
        if (ReferenceEquals(previous, frame)) return;
        if (previous != null && (frame.SampleRate != previous.SampleRate || frame.Time <= previous.Time)) { Reset(); previous = null; }
        double dt = previous == null ? 0 : frame.Time - previous.Time;
        double rms = Math.Pow(10, Math.Max(frame.Meters.RmsLeft, frame.Meters.RmsRight) / 20);
        double target = Math.Min(4 * Settings.StereoSensitivity, .16 * Settings.StereoSensitivity / Math.Max(.0001, rms));
        // Keep transients inside the circular field instead of clipping X/Y into
        // a square. Recovery is slow so quiet passages do not suddenly expand.
        double peak = 0;
        for (int i = 0; i < frame.PhaseLeft.Length; i++)
            peak = Math.Max(peak, Math.Sqrt((double)frame.PhaseLeft[i] * frame.PhaseLeft[i] + (double)frame.PhaseRight[i] * frame.PhaseRight[i]));
        double ceiling = peak > 0 ? .82 / peak : 4;
        target = Math.Min(target, ceiling);
        Gain = previous == null ? Math.Min(1, target) : Gain + (target - Gain) * (1 - Math.Exp(-dt / (target < Gain ? .025 : 1.2)));
        Gain = Math.Min(Gain, ceiling);
        traces.RemoveAll(trace => frame.Time - trace.Frame.Time > Settings.StereoTrailMs / 1000);
        traces.Add(new(frame, Gain, Settings.StereoSensitivity, ceiling));
        while (traces.Count > 20) traces.RemoveAt(0);
    }
}
