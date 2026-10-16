using System.Threading.Channels;

namespace VisualizerPro.Core;

public sealed class AnalysisEngine : IDisposable
{
    private sealed record Block(float[] Samples, int SampleRate, long Generation);
    private readonly Channel<Block> queue;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private AnalysisSettings settings = new();
    private DisplaySettings display = new();
    private AnalysisFrame? latest;
    private long generation, dropped, clearPeaks;
    private readonly object frameLock = new();
    private readonly Queue<AnalysisFrame> history = new();
    public AnalysisFrame? Latest => Volatile.Read(ref latest);
    public long Dropped => Interlocked.Read(ref dropped);
    public AnalysisSettings Settings => Volatile.Read(ref settings);
    public DisplaySettings Display => Volatile.Read(ref display);
    public void ConfigureDisplay(DisplaySettings next) => Volatile.Write(ref display, next.Sanitize());
    public AnalysisEngine()
    {
        queue = Channel.CreateBounded<Block>(new BoundedChannelOptions(32) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref dropped));
        worker = Task.Run(Run);
    }
    public void Configure(AnalysisSettings next) => Volatile.Write(ref settings, next.Sanitize());
    public void Reset() { lock (frameLock) { Interlocked.Increment(ref generation); history.Clear(); Volatile.Write(ref latest, null); } }
    public AnalysisFrame? FrameAt(double time)
    {
        lock (frameLock)
        {
            AnalysisFrame? result = null;
            foreach (var frame in history) { if (frame.Time > time + .005) break; result = frame; }
            return result;
        }
    }
    public AnalysisFrame[] FramesBetween(double afterTime, double throughTime)
    {
        lock (frameLock)
            return history.Where(frame => frame.Time > afterTime + 1e-6 && frame.Time <= throughTime + 1e-6).ToArray();
    }
    public void ResetPeaks() => Interlocked.Increment(ref clearPeaks);
    public void Submit(float[] stereo, int sampleRate) => queue.Writer.TryWrite(new(stereo, sampleRate, Interlocked.Read(ref generation)));
    private async Task Run()
    {
        Analyzer? analyzer = null; long activeGeneration = -1, peakVersion = 0; int rate = 0; AnalysisSettings? activeSettings = null;
        try
        {
            await foreach (var block in queue.Reader.ReadAllAsync(stop.Token))
            {
                if (block.Generation != Interlocked.Read(ref generation)) continue;
                if (activeGeneration != block.Generation || rate != block.SampleRate)
                {
                    activeGeneration = block.Generation; rate = block.SampleRate; analyzer = new(rate, Settings); activeSettings = Settings;
                }
                var next = Settings;
                if (next != activeSettings) { analyzer!.Configure(next); activeSettings = next; }
                long requestedPeaks = Interlocked.Read(ref clearPeaks);
                if (peakVersion != requestedPeaks) { analyzer!.ResetPeaks(); peakVersion = requestedPeaks; }
                analyzer!.Display = Display;
                var frame = analyzer.Add(block.Samples);
                if (frame != null)
                {
                    lock (frameLock)
                    {
                        if (activeGeneration != Interlocked.Read(ref generation)) continue;
                        history.Enqueue(frame); while (history.Count > 64) history.Dequeue(); Volatile.Write(ref latest, frame);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { stop.Cancel(); queue.Writer.TryComplete(); worker.GetAwaiter().GetResult(); stop.Dispose(); }
}
