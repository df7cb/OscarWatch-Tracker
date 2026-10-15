using System.Collections.Concurrent;
using OscarWatch.Core.Services;
using OscarWatch.Recording;
using PortAudioSharp;
using Serilog;
using PaStream = PortAudioSharp.Stream;

namespace OscarWatch.Terminal;

/// <summary>
/// PortAudio capture and playback for the Terminal built-in modem. Prefers WASAPI shared so it can
/// run alongside FT4 on the same soundcard. Playback is a queue of chunks that can be appended to
/// while it plays, so frames queued during a transmission go out in the same burst.
/// </summary>
public sealed class PacketAudioService : IPacketAudio
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PacketAudioService>();
    private const int CaptureSeconds = 10;

    private readonly object _gate = new();
    private readonly object _captureGate = new();
    private readonly SemaphoreSlim _captureSignal = new(0, int.MaxValue);
    private readonly ConcurrentQueue<float[]> _playQueue = new();
    private float[] _captureRing = new float[48000 * CaptureSeconds];
    private int _captureWrite;
    private int _captureCount;
    private PaStream? _input;
    private PaStream? _output;
    private int _outputChannels = 1;
    private float[]? _playing;
    private int _playIndex;
    private long _queuedSamples;
    private TaskCompletionSource? _drained;
    private float _inputPeak;
    private bool _portAudioReady;

    private static readonly object PortAudioGate = new();
    private static bool s_portAudioTried;
    private static bool s_portAudioReady;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                EnsurePortAudio();
                return _portAudioReady;
            }
        }
    }

    public int CaptureSampleRate { get; private set; } = 48000;

    public int PlaybackSampleRate { get; private set; } = 48000;

    public double OutputLatencySeconds { get; private set; } = 0.05;

    public double InputPeak
    {
        get
        {
            var peak = Volatile.Read(ref _inputPeak);
            Volatile.Write(ref _inputPeak, peak * 0.85f);
            return Math.Clamp(peak, 0, 1);
        }
    }

    public long QueuedPlaybackSamples => Interlocked.Read(ref _queuedSamples);

    public IReadOnlyList<AudioInputDevice> GetInputDevices() => ListDevices(input: true);

    public IReadOnlyList<AudioInputDevice> GetOutputDevices() => ListDevices(input: false);

    private IReadOnlyList<AudioInputDevice> ListDevices(bool input)
    {
        lock (_gate)
        {
            EnsurePortAudio();
            if (!_portAudioReady)
                return [];

            var candidates = new List<RecordingDeviceCandidate>();
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if ((input ? info.maxInputChannels : info.maxOutputChannels) <= 0)
                    continue;
                candidates.Add(new RecordingDeviceCandidate(
                    i,
                    info.name ?? $"{(input ? "Input" : "Output")} {i}",
                    input ? info.defaultLowInputLatency : info.defaultLowOutputLatency));
            }

            return RecordingDeviceListBuilder.Build(candidates);
        }
    }

    public void Start(string? inputId, string? inputName, string? outputId, string? outputName)
    {
        lock (_gate)
        {
            EnsurePortAudio();
            if (!_portAudioReady)
                throw new InvalidOperationException("PortAudio is not available.");

            StopUnlocked();
            try
            {
                OpenInputUnlocked(inputId, inputName);
                OpenOutputUnlocked(outputId, outputName);
            }
            catch
            {
                StopUnlocked();
                throw;
            }
        }
    }

    /// <summary>Opens capture only, for the settings level meter.</summary>
    public void StartCapture(string? inputId, string? inputName)
    {
        lock (_gate)
        {
            EnsurePortAudio();
            if (!_portAudioReady)
                throw new InvalidOperationException("PortAudio is not available.");

            StopUnlocked();
            OpenInputUnlocked(inputId, inputName);
        }
    }

    /// <summary>Opens playback only, for test tones.</summary>
    public void StartPlayback(string? outputId, string? outputName)
    {
        lock (_gate)
        {
            EnsurePortAudio();
            if (!_portAudioReady)
                throw new InvalidOperationException("PortAudio is not available.");

            CloseOutputUnlocked();
            OpenOutputUnlocked(outputId, outputName);
        }
    }

    public void Stop()
    {
        lock (_gate)
            StopUnlocked();
    }

    public int ReadCapture(Span<float> destination)
    {
        lock (_captureGate)
        {
            var n = Math.Min(destination.Length, _captureCount);
            var ring = _captureRing;
            var start = (_captureWrite - _captureCount + ring.Length) % ring.Length;
            for (var i = 0; i < n; i++)
                destination[i] = ring[(start + i) % ring.Length];
            _captureCount -= n;
            return n;
        }
    }

    public bool WaitForCapture(TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_captureGate)
        {
            if (_captureCount > 0)
                return true;
        }

        try
        {
            return _captureSignal.Wait(timeout, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Enqueue(float[] samples)
    {
        if (samples.Length == 0)
            return;
        Interlocked.Add(ref _queuedSamples, samples.Length);
        _playQueue.Enqueue(samples);
    }

    public void ClearPlayback()
    {
        while (_playQueue.TryDequeue(out _))
        {
        }

        Volatile.Write(ref _playing, null);
        Interlocked.Exchange(ref _queuedSamples, 0);
        SignalDrained();
    }

    public Task WaitForDrainAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource tcs;
        lock (_gate)
        {
            if (Interlocked.Read(ref _queuedSamples) == 0 || _output is null)
                return Task.CompletedTask;
            tcs = _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.Read(ref _queuedSamples) == 0)
            {
                _drained = null;
                tcs.TrySetResult();
            }
        }

        return tcs.Task.WaitAsync(cancellationToken);
    }

    private void SignalDrained()
    {
        TaskCompletionSource? tcs;
        lock (_gate)
        {
            tcs = _drained;
            _drained = null;
        }

        tcs?.TrySetResult();
    }

    private void OpenInputUnlocked(string? deviceId, string? deviceName)
    {
        var index = ResolveDeviceIndex(deviceId, deviceName, input: true);
        if (index < 0)
            throw new InvalidOperationException(
                "Terminal input soundcard is no longer available. Open Terminal Settings, click Refresh, and re-select the input device.");

        var info = PortAudio.GetDeviceInfo(index);
        var rate = (int)Math.Round(info.defaultSampleRate);
        CaptureSampleRate = rate < 8000 ? 48000 : rate;
        lock (_captureGate)
        {
            _captureRing = new float[CaptureSampleRate * CaptureSeconds];
            _captureWrite = 0;
            _captureCount = 0;
        }

        var param = new StreamParameters
        {
            device = index,
            channelCount = 1,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };
        _input = new PaStream(param, null, CaptureSampleRate, 256, StreamFlags.ClipOff, OnInput, IntPtr.Zero);
        _input.Start();
        Log.Information("Terminal modem capture on '{Device}' (index {Index}) at {Rate} Hz", info.name, index, CaptureSampleRate);
    }

    private void OpenOutputUnlocked(string? deviceId, string? deviceName)
    {
        var index = ResolveDeviceIndex(deviceId, deviceName, input: false);
        if (index < 0)
            throw new InvalidOperationException(
                "Terminal output soundcard is no longer available. Open Terminal Settings, click Refresh, and re-select the output device.");

        var info = PortAudio.GetDeviceInfo(index);
        var rate = (int)Math.Round(info.defaultSampleRate);
        PlaybackSampleRate = rate < 8000 ? 48000 : rate;
        try
        {
            OpenOutputStream(index, info, channels: 1);
        }
        catch (Exception ex) when (info.maxOutputChannels >= 2)
        {
            Log.Warning(ex, "Terminal modem mono output open failed; retrying stereo");
            OpenOutputStream(index, info, channels: 2);
        }

        OutputLatencySeconds = info.defaultLowOutputLatency is > 0 and < 2 ? info.defaultLowOutputLatency : 0.05;
        Log.Information(
            "Terminal modem output on '{Device}' (index {Index}) at {Rate} Hz, {Channels} ch, latency {LatencyMs:0} ms",
            info.name, index, PlaybackSampleRate, _outputChannels, OutputLatencySeconds * 1000);
    }

    private void OpenOutputStream(int index, DeviceInfo info, int channels)
    {
        var param = new StreamParameters
        {
            device = index,
            channelCount = channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        PaStream? stream = null;
        try
        {
            stream = new PaStream(null, param, PlaybackSampleRate, PortAudio.FramesPerBufferUnspecified,
                StreamFlags.ClipOff, OnOutput, IntPtr.Zero);
            stream.Start();
            _output = stream;
            _outputChannels = channels;
            stream = null;
        }
        finally
        {
            if (stream is not null)
            {
                try { stream.Dispose(); } catch { /* open failed */ }
            }
        }
    }

    private StreamCallbackResult OnInput(
        IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (input == IntPtr.Zero)
            return StreamCallbackResult.Continue;

        var peak = 0f;
        unsafe
        {
            var ptr = (float*)input.ToPointer();
            lock (_captureGate)
            {
                var ring = _captureRing;
                for (var i = 0; i < frameCount; i++)
                {
                    var s = ptr[i];
                    ring[_captureWrite] = s;
                    _captureWrite = (_captureWrite + 1) % ring.Length;
                    if (_captureCount < ring.Length)
                        _captureCount++;
                    var a = Math.Abs(s);
                    if (a > peak)
                        peak = a;
                }
            }
        }

        if (peak > Volatile.Read(ref _inputPeak))
            Volatile.Write(ref _inputPeak, peak);
        if (_captureSignal.CurrentCount == 0)
            _captureSignal.Release();
        return StreamCallbackResult.Continue;
    }

    private StreamCallbackResult OnOutput(
        IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (output == IntPtr.Zero)
            return StreamCallbackResult.Complete;

        var played = 0;
        var hadAudio = Volatile.Read(ref _playing) is not null || !_playQueue.IsEmpty;
        unsafe
        {
            var ptr = (float*)output.ToPointer();
            var channels = Math.Max(1, _outputChannels);
            for (var frame = 0; frame < frameCount; frame++)
            {
                var sample = 0f;
                var chunk = Volatile.Read(ref _playing);
                if (chunk is null || _playIndex >= chunk.Length)
                {
                    chunk = _playQueue.TryDequeue(out var next) ? next : null;
                    Volatile.Write(ref _playing, chunk);
                    _playIndex = 0;
                }

                if (chunk is not null)
                {
                    sample = chunk[_playIndex++];
                    played++;
                }

                var dest = frame * channels;
                for (var c = 0; c < channels; c++)
                    ptr[dest + c] = sample;
            }
        }

        if (played > 0 && Interlocked.Add(ref _queuedSamples, -played) < 0)
            Interlocked.Exchange(ref _queuedSamples, 0);
        if (hadAudio && Interlocked.Read(ref _queuedSamples) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(_ => SignalDrained(), null);

        // Keep the stream running so the next transmission does not wait for the soundcard to wake.
        return StreamCallbackResult.Continue;
    }

    private void StopUnlocked()
    {
        try { _input?.Stop(); } catch { /* ignore */ }
        try { _input?.Dispose(); } catch { /* ignore */ }
        _input = null;
        CloseOutputUnlocked();
    }

    private void CloseOutputUnlocked()
    {
        try { _output?.Stop(); } catch { /* ignore */ }
        try { _output?.Dispose(); } catch { /* ignore */ }
        _output = null;
        _outputChannels = 1;
        while (_playQueue.TryDequeue(out _))
        {
        }

        Volatile.Write(ref _playing, null);
        Interlocked.Exchange(ref _queuedSamples, 0);
        var tcs = _drained;
        _drained = null;
        tcs?.TrySetResult();
    }

    private void EnsurePortAudio()
    {
        lock (PortAudioGate)
        {
            if (!s_portAudioTried)
            {
                s_portAudioTried = true;
                try
                {
                    if (PortAudioOutOfProcessProbe.TryRun(out _))
                    {
                        PortAudio.Initialize();
                        s_portAudioReady = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Terminal modem PortAudio init failed");
                }
            }

            _portAudioReady = s_portAudioReady;
        }
    }

    private static int ResolveDeviceIndex(string? deviceId, string? deviceName, bool input)
    {
        if (string.IsNullOrWhiteSpace(deviceId) && string.IsNullOrWhiteSpace(deviceName))
            return input ? PortAudio.DefaultInputDevice : PortAudio.DefaultOutputDevice;

        var snapshots = new List<RecordingDeviceResolver.InputDeviceSnapshot>();
        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            var channels = input ? info.maxInputChannels : info.maxOutputChannels;
            if (channels <= 0)
                continue;
            snapshots.Add(new RecordingDeviceResolver.InputDeviceSnapshot(
                i,
                info.name ?? "",
                input ? info.defaultLowInputLatency : info.defaultLowOutputLatency,
                channels,
                PortAudioHostApi.GetTypeId(info.hostApi)));
        }

        return RecordingDeviceResolver.ResolveIndex(deviceId, deviceName, snapshots, preferLowLatencyShared: true);
    }

    public void Dispose()
    {
        lock (_gate)
            StopUnlocked();
        _captureSignal.Dispose();
    }
}
