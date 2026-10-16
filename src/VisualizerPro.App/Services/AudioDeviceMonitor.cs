using System.Runtime.InteropServices;

namespace VisualizerPro.App.Services;

public sealed record AudioDeviceSnapshot(IReadOnlyList<OutputDevice> Devices, string? DefaultId, string? Error);

// Endpoint enumeration can block for hundreds of milliseconds in Windows drivers.
// Keep all polling on one worker and publish only the latest immutable result.
public sealed class AudioDeviceMonitor : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private AudioDeviceSnapshot? latest;
    public AudioDeviceSnapshot? Latest => Volatile.Read(ref latest);

    public AudioDeviceMonitor() => _ = Task.Run(Run);

    private async Task Run()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                AudioDeviceSnapshot next;
                try { next = new(SystemAudioSource.Devices().ToArray(), SystemAudioSource.DefaultId(), null); }
                catch (Exception error) when (error is COMException or InvalidOperationException)
                { next = new([], null, error.Message); }
                Volatile.Write(ref latest, next);
                await Task.Delay(2000, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally { stop.Dispose(); }
    }

    // Do not wait for a potentially blocked driver call on the window thread.
    public void Dispose() { try { stop.Cancel(); } catch (ObjectDisposedException) { } }
}
