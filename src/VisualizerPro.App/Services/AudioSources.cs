using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VisualizerPro.Core;

namespace VisualizerPro.App.Services;

public sealed record OutputDevice(string? Id, string Name) { public override string ToString() => Name; }

internal sealed class LowLatencyLoopbackCapture(MMDevice device) : WasapiCapture(device, false, 20)
{
    protected override AudioClientStreamFlags GetAudioClientStreamFlags() => base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
}

public sealed class SystemAudioSource(AnalysisEngine engine) : IDisposable
{
    private WasapiCapture? capture;
    private MMDevice? device;
    private long lastData, lastSilencePump;
    private int rate = 48000;
    private readonly object captureLock = new();
    public string? ActiveId { get; private set; }
    public string Description { get; private set; } = "未连接";
    public bool Connected => capture != null;
    public event Action<string>? Error;
    public static List<OutputDevice> Devices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<OutputDevice> { new(null, "跟随默认播放设备") };
        foreach (var item in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        { using (item) devices.Add(new(item.ID, item.FriendlyName)); }
        return devices;
    }
    public static string? DefaultId()
    {
        try { using var enumerator = new MMDeviceEnumerator(); using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); return endpoint.ID; }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }
    public void Start(string? id)
    {
        DisposeCapture();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = id == null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id);
            capture = new LowLatencyLoopbackCapture(device); ActiveId = device.ID; rate = capture.WaveFormat.SampleRate;
            Description = $"{device.FriendlyName} · {rate / 1000.0:0.#} kHz · {capture.WaveFormat.Channels} ch";
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (sender, e) => { if (ReferenceEquals(sender, capture) && e.Exception != null) Error?.Invoke(e.Exception.Message); };
            engine.Reset(); lastData = lastSilencePump = Stopwatch.GetTimestamp(); capture.StartRecording();
        }
        catch { DisposeCapture(); throw; }
    }
    private void OnData(object? sender, WaveInEventArgs e)
    {
      lock (captureLock)
      {
        var current = capture; if (current == null || !ReferenceEquals(sender, current)) return;
        lastData = lastSilencePump = Stopwatch.GetTimestamp();
        WaveFormat format = current.WaveFormat; int bytes = format.BitsPerSample / 8, channels = format.Channels;
        int frames = e.BytesRecorded / format.BlockAlign; var stereo = new float[frames * 2];
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        float Sample(int offset) => isFloat ? BitConverter.ToSingle(e.Buffer, offset) : bytes switch
        {
            2 => BitConverter.ToInt16(e.Buffer, offset) / 32768f,
            3 => ((e.Buffer[offset] | e.Buffer[offset + 1] << 8 | e.Buffer[offset + 2] << 16) << 8 >> 8) / 8388608f,
            4 => BitConverter.ToInt32(e.Buffer, offset) / 2147483648f,
            _ => (e.Buffer[offset] - 128) / 128f
        };
        for (int f = 0; f < frames; f++)
        {
            int at = f * format.BlockAlign;
            float l = Sample(at), r = channels == 1 ? l : Sample(at + bytes);
            // Windows standard speaker order: FL FR FC LFE BL BR SL SR; LFE excluded.
            if (channels >= 3) { float c = Sample(at + 2 * bytes) * .70710678f; l += c; r += c; }
            if (channels >= 6) { l += Sample(at + 4 * bytes) * .70710678f; r += Sample(at + 5 * bytes) * .70710678f; }
            if (channels >= 8) { l += Sample(at + 6 * bytes) * .70710678f; r += Sample(at + 7 * bytes) * .70710678f; }
            stereo[2 * f] = l; stereo[2 * f + 1] = r;
        }
        engine.Submit(stereo, rate);
      }
    }
    public void PumpSilence()
    {
        if (capture == null) return;
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastData), now).TotalMilliseconds < 100) return;
        int frames = (int)Math.Clamp(Stopwatch.GetElapsedTime(Interlocked.Read(ref lastSilencePump), now).TotalSeconds * rate, 0, rate / 4.0);
        if (frames == 0) return;
        lastSilencePump = now; engine.Submit(new float[frames * 2], rate);
    }
    private void DisposeCapture()
    {
        WasapiCapture? old;
        lock (captureLock) { old = capture; capture = null; ActiveId = null; }
        if (old != null) { old.DataAvailable -= OnData; try { old.StopRecording(); } catch (System.Runtime.InteropServices.COMException) { } old.Dispose(); }
        device?.Dispose(); device = null;
    }
    public void Dispose() => DisposeCapture();
}

internal sealed class DecodedProvider : ISampleProvider, IDisposable
{
    private readonly Process decoder;
    private readonly AnalysisEngine engine;
    private readonly Stream stdout;
    private readonly Task<string> stderr;
    private byte[] bytes = [];
    private long frames;
    public WaveFormat WaveFormat { get; }
    public bool Eof { get; private set; }
    public string? Failure { get; private set; }
    public long Frames => Interlocked.Read(ref frames);
    public DecodedProvider(MediaInfo media, double start, AnalysisEngine engine)
    {
        this.engine = engine; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(media.SampleRate, 2);
        decoder = Ffmpeg.Decode(media, start);
        stdout = decoder.StandardOutput.BaseStream;
        stderr = decoder.StandardError.ReadToEndAsync();
    }
    public int Read(float[] buffer, int offset, int count)
    {
        count -= count % 2;
        int wanted = count * 4; if (bytes.Length < wanted) bytes = new byte[wanted];
        int read = 0;
        try
        {
            while (read < wanted) { int n = stdout.Read(bytes, read, wanted - read); if (n == 0) break; read += n; }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { return 0; }
        read -= read % 8;
        if (read == 0)
        {
            Eof = true;
            try { decoder.WaitForExit(); if (decoder.ExitCode != 0) Failure = stderr.GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { }
            return 0;
        }
        Buffer.BlockCopy(bytes, 0, buffer, offset * 4, read);
        // NAudio WaveBuffer can alias a byte[] as float[]. Array.Copy would inspect
        // its runtime element type and convert individual bytes into float values.
        var analysis = new float[read / 4]; Buffer.BlockCopy(buffer, offset * 4, analysis, 0, read);
        engine.Submit(analysis, WaveFormat.SampleRate); Interlocked.Add(ref frames, analysis.Length / 2);
        return analysis.Length;
    }
    public void Dispose() { Ffmpeg.Kill(decoder); decoder.Dispose(); }
}

public sealed class FilePlayer(AnalysisEngine engine) : IDisposable
{
    private WasapiOut? output;
    private DecodedProvider? provider;
    private VolumeSampleProvider? volumeProvider;
    private MediaInfo? media;
    private double start;
    private float volume = .7f;
    public bool Playing => output?.PlaybackState == PlaybackState.Playing;
    public bool Ended => provider?.Eof == true && output?.PlaybackState == PlaybackState.Stopped;
    public string? Failure => provider?.Failure;
    public event Action<string>? Error;
    public double SessionTime
    {
        get
        {
            try { return output == null ? 0 : output.GetPosition() / (double)output.OutputWaveFormat.AverageBytesPerSecond; }
            catch (COMException) { return 0; }
        }
    }
    public double Position => media == null ? 0 : Math.Min(media.Duration, start + SessionTime);
    public float Volume { get => volume; set { volume = Math.Clamp(value, 0, 1); if (volumeProvider != null) volumeProvider.Volume = volume; } }
    public void Open(MediaInfo file) { Stop(); media = file; start = 0; Seek(0, false); }
    public void Seek(double seconds, bool play)
    {
        if (media == null) return;
        StopOutput(); start = Math.Clamp(seconds, 0, Math.Max(0, media.Duration - .01)); engine.Reset();
        provider = new(media, start, engine); volumeProvider = new(provider) { Volume = volume };
        output = new WasapiOut(AudioClientShareMode.Shared, 60);
        output.PlaybackStopped += (sender, e) => { if (ReferenceEquals(sender, output) && e.Exception != null) Error?.Invoke(e.Exception.Message); };
        output.Init(new SampleToWaveProvider(volumeProvider));
        if (play) output.Play();
    }
    public void Toggle() { if (output == null) return; if (Playing) output.Pause(); else output.Play(); }
    private void StopOutput()
    {
        provider?.Dispose(); provider = null;
        if (output != null) { output.Stop(); output.Dispose(); output = null; }
        volumeProvider = null;
    }
    public void Stop() { StopOutput(); media = null; }
    public void Dispose() => Stop();
}
