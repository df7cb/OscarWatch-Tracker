using System.Runtime.InteropServices;
using OscarWatch.Core.Models;
using OscarWatch.Core.Recording;
using OscarWatch.Core.Services;
using PortAudioSharp;
using Serilog;

namespace OscarWatch.Recording;

public sealed class PortAudioRecordingService : IAudioRecordingService, IDisposable
{
    /// <summary>Let PortAudio pick buffer size (required on many WASAPI devices).</summary>
    private const uint UnspecifiedFramesPerBuffer = 0;
    private const uint FallbackFramesPerBuffer = 1024;
    /// <summary>Scratch size when frame count is host-selected (variable per callback).</summary>
    private const int MaxCallbackFrameCount = 8192;
    private const int RingBufferBytes = 4 * 1024 * 1024;
    /// <summary>Log only when backlog or peak exceeds this (25% of ring).</summary>
    private const int BacklogPressureBytes = RingBufferBytes / 4;
    private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WriterJoinTimeout = TimeSpan.FromSeconds(5);

    private static readonly ILogger Log = Serilog.Log.ForContext<PortAudioRecordingService>();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _initializationGate = new();
    private readonly object _streamGate = new();
    private readonly FfmpegLocator _ffmpegLocator;
    private readonly FfmpegMp3Converter _mp3Converter;

    private bool _initializationAttempted;
    private bool _disposed;
    private string? _initError;
    private PortAudioSharp.Stream? _stream;
    private WavWriter? _wavWriter;
    private PcmRingBuffer? _ringBuffer;
    private byte[]? _callbackScratch;
    private Thread? _writerThread;
    private volatile bool _writerRunning;
    private volatile bool _captureActive;
    private readonly AutoResetEvent _dataAvailable = new(false);
    private DateTime _lastStatsUtc = DateTime.MinValue;
    private long _lastWarnedDroppedBytes;
    private int _activeChannels = 1;
    private int _maxCallbackBytes;
    private RecordingContainerFormat _activeContainer = RecordingContainerFormat.Wav;

    public PortAudioRecordingService(
        FfmpegLocator? ffmpegLocator = null,
        FfmpegMp3Converter? mp3Converter = null)
    {
        _ffmpegLocator = ffmpegLocator ?? new FfmpegLocator();
        _mp3Converter = mp3Converter ?? new FfmpegMp3Converter(locator: _ffmpegLocator);
    }

    public bool IsAvailable => !_initializationAttempted || PortAudioRuntime.IsInitialized;

    public string? UnavailableReason =>
        _initializationAttempted && !PortAudioRuntime.IsInitialized
            ? _initError ?? "PortAudio is not available."
            : null;

    public bool TryInitialize() => EnsureInitialized();
    public bool IsRecording { get; private set; }
    public string? ActiveNoradId { get; private set; }
    public string? ActiveOutputPath { get; private set; }
    public string? LastCompletedOutputPath { get; private set; }

    /// <summary>
    /// Initialises PortAudio on recording use. Each call asks the shared runtime again, because a
    /// host refresh can terminate PortAudio after an earlier success. Do not call this while the
    /// main window is being constructed: some SmartSDR/DAX installations can block inside the
    /// native PortAudio initialiser.
    /// </summary>
    private bool EnsureInitialized()
    {
        lock (_initializationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _initializationAttempted = true;
            var ready = PortAudioRuntime.EnsureInitialized();
            _initError = ready ? null : "PortAudio is not available.";
            return ready;
        }
    }

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        _operationLock.Wait();
        try
        {
            if (!EnsureInitialized())
                return [];

            // PortAudio on Windows enumerates devices from multiple audio APIs. A single physical
            // device can appear through several APIs, so prefer the lower-latency candidate.
            var candidates = new List<RecordingDeviceCandidate>();
            Log.Debug("PortAudio: Scanning {DeviceCount} devices", PortAudio.DeviceCount);
            
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                
                Log.Debug(
                    "PortAudio Device {Index}: '{Name}' (in={Channels}, latency={Latency}ms)",
                    i,
                    info.name,
                    info.maxInputChannels,
                    info.defaultLowInputLatency * 1000);
                
                if (info.maxInputChannels <= 0)
                    continue;

                candidates.Add(new RecordingDeviceCandidate(i, info.name, info.defaultLowInputLatency));
            }

            Log.Debug("PortAudio: Found {InputDevices} input devices", candidates.Count);
            var result = RecordingDeviceListBuilder.Build(candidates);
            Log.Debug("After deduplication: {FinalDevices} unique devices", result.Count);
            
            foreach (var device in result)
                Log.Debug("  - Device: '{DisplayName}' (id={Id})", device.DisplayName, device.Id);

            return result;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StartAsync(
        string noradId,
        string satelliteName,
        string deviceId,
        RecordingFormatPreset format,
        string outputPath,
        string? deviceName = null,
        RecordingContainerFormat container = RecordingContainerFormat.Wav,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!EnsureInitialized())
                throw new InvalidOperationException(_initError ?? "PortAudio is not available.");

            if (IsRecording)
                await StopCoreAsync().ConfigureAwait(false);

            LastCompletedOutputPath = null;
            _activeContainer = container;
            var (preferredSampleRate, channels) = format.GetFormat();

            _maxCallbackBytes = MaxCallbackFrameCount * channels * 2;
            _ringBuffer = new PcmRingBuffer(RingBufferBytes);
            _ringBuffer.ResetStats();
            _callbackScratch = new byte[_maxCallbackBytes];
            _activeChannels = channels;
            _lastStatsUtc = DateTime.UtcNow;
            _lastWarnedDroppedBytes = 0;

            PortAudioSharp.Stream.Callback callback = OnAudioCallback;
            int actualSampleRate;
            uint framesPerBuffer;
            int deviceIndex;
            DeviceInfo deviceInfo;

            lock (_streamGate)
            {
                (deviceIndex, deviceInfo) = ResolveInputDevice(deviceId, deviceName, channels);
                try
                {
                    _stream = OpenCaptureStream(
                        deviceIndex,
                        deviceInfo,
                        channels,
                        preferredSampleRate,
                        callback,
                        out actualSampleRate,
                        out framesPerBuffer);
                }
                catch (InvalidOperationException) when (PortAudioRuntime.TryRefreshHost())
                {
                    Log.Information("Retrying recording capture after refreshing the PortAudio host");
                    (deviceIndex, deviceInfo) = ResolveInputDevice(deviceId, deviceName, channels);
                    _stream = OpenCaptureStream(
                        deviceIndex,
                        deviceInfo,
                        channels,
                        preferredSampleRate,
                        callback,
                        out actualSampleRate,
                        out framesPerBuffer);
                }

                // Count the handle before writer setup and Start. A later failure still runs
                // cleanup, which decrements, so another service's stream is not erased.
                PortAudioRuntime.NotifyStreamOpened();
                _wavWriter = new WavWriter(outputPath, actualSampleRate, (short)channels);
                StartWriterThread();

                _captureActive = true;
                _stream.Start();
            }

            IsRecording = true;
            ActiveNoradId = noradId;
            ActiveOutputPath = outputPath;
            Log.Information(
                "Recording started for {Satellite} ({NoradId}) on device '{Device}' (index={DeviceIndex}) -> {Path} (container={Container}, rate={SampleRate} Hz, frames={Frames}, channels={Channels}, ring={RingKb} KB)",
                satelliteName,
                noradId,
                deviceInfo.name,
                deviceIndex,
                outputPath,
                container,
                actualSampleRate,
                framesPerBuffer,
                _activeChannels,
                RingBufferBytes / 1024);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recording start failed for {NoradId} -> {Path}", noradId, outputPath);
            CleanupRecordingState();
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        if (!IsRecording)
            return;

        var path = ActiveOutputPath;
        var container = _activeContainer;

        // Drop the live-recording flags before MP3 finalisation so the sidebar can
        // clear REC / prune the pass while ffmpeg runs (can take minutes).
        IsRecording = false;
        ActiveNoradId = null;

        try
        {
            StopCaptureAndDrain();
            LogRecordingStats(path, final: true);
            LastCompletedOutputPath = await FinalizeOutputAsync(path, container).ConfigureAwait(false);
            Log.Information("Recording stopped -> {Path}", LastCompletedOutputPath ?? path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while stopping recording -> {Path}", path);
            CleanupRecordingState();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                LastCompletedOutputPath = path;
        }
        finally
        {
            IsRecording = false;
            ActiveNoradId = null;
            ActiveOutputPath = null;
            _activeContainer = RecordingContainerFormat.Wav;
        }
    }

    private async Task<string?> FinalizeOutputAsync(string? wavPath, RecordingContainerFormat container)
    {
        if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
            return wavPath;

        if (container != RecordingContainerFormat.Mp3)
            return wavPath;

        var probe = await _ffmpegLocator.ProbeAsync().ConfigureAwait(false);
        if (!probe.IsAvailable)
        {
            Log.Information(
                "MP3 preferred but ffmpeg is unavailable; keeping WAV {Path}. {Detail}",
                wavPath,
                probe.Detail ?? "ffmpeg was not found on PATH.");
            return wavPath;
        }

        try
        {
            var result = await _mp3Converter.ConvertWavToMp3Async(wavPath).ConfigureAwait(false);
            if (result.Success && !string.IsNullOrEmpty(result.OutputPath))
            {
                if (!string.IsNullOrEmpty(result.Error))
                    Log.Warning("MP3 conversion note for {Path}: {Detail}", result.OutputPath, result.Error);
                else
                    Log.Information("Converted pass recording to MP3 -> {Path}", result.OutputPath);
                return result.OutputPath;
            }

            Log.Warning(
                "MP3 conversion failed for {Path}; keeping WAV. {Detail}",
                wavPath,
                result.Error ?? "Unknown error.");
            return wavPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MP3 conversion threw for {Path}; keeping WAV", wavPath);
            return wavPath;
        }
    }

    private void StopCaptureAndDrain()
    {
        lock (_streamGate)
        {
            _captureActive = false;
            if (_stream is not null)
            {
                try { _stream.Stop(); } catch (Exception ex) { Log.Debug(ex, "PortAudio stream stop"); }
                try { _stream.Dispose(); } catch (Exception ex) { Log.Debug(ex, "PortAudio stream dispose"); }
                _stream = null;
                PortAudioRuntime.NotifyStreamClosed();
            }
        }

        _dataAvailable.Set();
        JoinWriterThread();

        try { _wavWriter?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "WAV writer dispose"); }
        _wavWriter = null;
        _ringBuffer = null;
        _callbackScratch = null;
    }

    private static PortAudioSharp.Stream OpenCaptureStream(
        int deviceIndex,
        DeviceInfo deviceInfo,
        int channels,
        int preferredSampleRate,
        PortAudioSharp.Stream.Callback callback,
        out int actualSampleRate,
        out uint framesPerBuffer)
    {
        var input = new StreamParameters
        {
            device = deviceIndex,
            channelCount = channels,
            sampleFormat = SampleFormat.Int16,
            suggestedLatency = deviceInfo.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        var sampleRates = BuildSampleRatesToTry(preferredSampleRate, deviceInfo);
        uint[] frameBuffers = [UnspecifiedFramesPerBuffer, FallbackFramesPerBuffer];
        var failedAttempts = new List<string>();
        PortAudioException? lastError = null;

        foreach (var rate in sampleRates)
        {
            foreach (var frames in frameBuffers)
            {
                try
                {
                    var stream = new PortAudioSharp.Stream(
                        inParams: input,
                        outParams: null,
                        sampleRate: rate,
                        framesPerBuffer: frames,
                        streamFlags: StreamFlags.ClipOff,
                        callback: callback,
                        userData: IntPtr.Zero);

                    actualSampleRate = rate;
                    framesPerBuffer = frames;
                    LogStreamOpenFallback(deviceInfo.name, preferredSampleRate, rate, frames, failedAttempts);
                    return stream;
                }
                catch (PortAudioException ex)
                {
                    lastError = ex;
                    failedAttempts.Add(DescribeOpenAttempt(rate, frames));
                }
            }
        }

        var tried = string.Join("; ", failedAttempts);
        Log.Warning(
            lastError,
            "Could not open audio input on '{Device}' (tried {Attempts})",
            deviceInfo.name,
            tried);
        throw new InvalidOperationException(
            $"Could not open audio input on '{deviceInfo.name}'. Try another device or recording format.",
            lastError);
    }

    private static string DescribeOpenAttempt(int sampleRate, uint frames) =>
        frames == UnspecifiedFramesPerBuffer
            ? $"{sampleRate} Hz"
            : $"{sampleRate} Hz, {frames}-frame buffer";

    private static void LogStreamOpenFallback(
        string deviceName,
        int preferredSampleRate,
        int actualSampleRate,
        uint framesPerBuffer,
        IReadOnlyList<string> failedAttempts)
    {
        if (actualSampleRate != preferredSampleRate)
        {
            Log.Information(
                "Using {Rate} Hz on '{Device}' ({Requested} Hz not supported on this device)",
                actualSampleRate,
                deviceName,
                preferredSampleRate);
            return;
        }

        if (framesPerBuffer != UnspecifiedFramesPerBuffer)
        {
            Log.Information(
                "Opened '{Device}' at {Rate} Hz with {Frames}-frame buffers (host default buffer size was rejected)",
                deviceName,
                actualSampleRate,
                framesPerBuffer);
            return;
        }

        if (failedAttempts.Count > 0)
        {
            Log.Information(
                "Opened '{Device}' at {Rate} Hz after {AttemptCount} failed open attempt(s): {Attempts}",
                deviceName,
                actualSampleRate,
                failedAttempts.Count,
                string.Join("; ", failedAttempts));
        }
    }

    private static List<int> BuildSampleRatesToTry(int preferredSampleRate, DeviceInfo deviceInfo)
    {
        var rates = new List<int>();
        if (preferredSampleRate > 0)
            rates.Add(preferredSampleRate);

        var deviceRate = (int)Math.Round(deviceInfo.defaultSampleRate);
        if (deviceRate > 0 && !rates.Contains(deviceRate))
            rates.Add(deviceRate);

        if (rates.Count == 0)
            rates.Add(44100);

        return rates;
    }

    private (int Index, DeviceInfo Info) ResolveInputDevice(string deviceId, string? deviceName, int channels)
    {
        var inputs = SnapshotInputDevices();
        var deviceIndex = RecordingDeviceResolver.ResolveIndex(deviceId, deviceName, inputs);
        if (deviceIndex < 0)
        {
            Log.Warning("Could not find device: id='{DeviceId}' name='{DeviceName}'", deviceId, deviceName ?? "(not provided)");
            throw new InvalidOperationException(
                "Audio device is no longer available. " +
                "Please go to Settings → Recording, click Refresh, and re-select your radio input device.");
        }

        var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);
        Log.Information(
            "Resolved recording device '{RawName}' to index {Index} (saved id='{DeviceId}', display='{DisplayName}')",
            deviceInfo.name,
            deviceIndex,
            deviceId,
            deviceName ?? "");

        if (deviceInfo.maxInputChannels <= 0)
        {
            Log.Warning("Device at index {DeviceIndex} ('{Device}') has no input channels", deviceIndex, deviceInfo.name);
            throw new InvalidOperationException(
                $"Device '{deviceInfo.name}' does not support audio input. " +
                $"Please go to Settings → Recording, click Refresh, and re-select your radio input device.");
        }

        if (deviceInfo.maxInputChannels < channels)
            throw new InvalidOperationException(
                $"Device '{deviceInfo.name}' does not support {channels} input channel(s).");

        return (deviceIndex, deviceInfo);
    }

    private List<RecordingDeviceResolver.InputDeviceSnapshot> SnapshotInputDevices()
    {
        var inputs = new List<RecordingDeviceResolver.InputDeviceSnapshot>();
        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxInputChannels <= 0)
                continue;

            inputs.Add(new RecordingDeviceResolver.InputDeviceSnapshot(
                i,
                info.name ?? "",
                info.defaultLowInputLatency,
                info.maxInputChannels));
        }

        return inputs;
    }

    private StreamCallbackResult OnAudioCallback(
        IntPtr inputPtr,
        IntPtr outputPtr,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        if (!_captureActive)
            return StreamCallbackResult.Complete;

        if ((statusFlags & StreamCallbackFlags.InputOverflow) != 0)
            Log.Warning("PortAudio input overflow reported in recording callback");

        var ring = _ringBuffer;
        var scratch = _callbackScratch;
        if (ring is null || scratch is null)
            return StreamCallbackResult.Complete;

        var sampleCount = (int)frameCount * _activeChannels;
        var byteCount = sampleCount * 2;
        if (byteCount > scratch.Length)
        {
            Log.Warning(
                "Recording callback frame larger than scratch buffer ({ByteCount} > {Capacity}); dropping",
                byteCount,
                scratch.Length);
            ring.RecordDropped(byteCount);
            return StreamCallbackResult.Continue;
        }

        Marshal.Copy(inputPtr, scratch, 0, byteCount);
        ring.TryWrite(scratch.AsSpan(0, byteCount));
        _dataAvailable.Set();
        return StreamCallbackResult.Continue;
    }

    private void StartWriterThread()
    {
        _writerRunning = true;
        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "OscarWatch.RecordingWriter"
        };
        _writerThread.Start();
    }

    private void JoinWriterThread()
    {
        _writerRunning = false;
        _dataAvailable.Set();

        var thread = _writerThread;
        _writerThread = null;
        if (thread is null)
            return;

        if (!thread.Join(WriterJoinTimeout))
            Log.Warning("Recording writer thread did not exit within {TimeoutSeconds}s", WriterJoinTimeout.TotalSeconds);
    }

    private void WriterLoop()
    {
        var drainBuffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                _dataAvailable.WaitOne(250);

                var writer = _wavWriter;
                var ring = _ringBuffer;
                if (writer is not null && ring is not null)
                {
                    int read;
                    while ((read = ring.Read(drainBuffer)) > 0)
                        writer.WritePcm16(drainBuffer.AsSpan(0, read));

                    MaybeLogPeriodicStats();
                }

                if (!_writerRunning && !_captureActive && !HasBufferedAudio())
                    break;
            }

            FlushRemainingAudio();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recording writer thread failed");
        }
    }

    private void FlushRemainingAudio()
    {
        var writer = _wavWriter;
        var ring = _ringBuffer;
        if (writer is null || ring is null)
            return;

        var drainBuffer = new byte[64 * 1024];
        int read;
        while ((read = ring.Read(drainBuffer)) > 0)
            writer.WritePcm16(drainBuffer.AsSpan(0, read));
    }

    private bool HasBufferedAudio() => _ringBuffer?.Count > 0;

    private void MaybeLogPeriodicStats()
    {
        if (!IsRecording)
            return;

        var now = DateTime.UtcNow;
        if (now - _lastStatsUtc < StatsInterval)
            return;

        var ring = _ringBuffer;
        if (ring is null)
            return;

        if (!ShouldLogRecordingPressure(ring, periodic: true, _lastWarnedDroppedBytes))
            return;

        _lastStatsUtc = now;
        LogRecordingPressure(ActiveOutputPath, ring, final: false);
    }

    private static bool ShouldLogRecordingPressure(PcmRingBuffer ring, bool periodic, long lastWarnedDroppedBytes)
    {
        if (periodic)
        {
            if (ring.DroppedBytes > lastWarnedDroppedBytes)
                return true;

            return ring.Count >= BacklogPressureBytes;
        }

        return ring.DroppedBytes > 0 || ring.PeakCount >= BacklogPressureBytes;
    }

    private void LogRecordingStats(string? path, bool final)
    {
        var ring = _ringBuffer;
        if (ring is null)
            return;

        if (!ShouldLogRecordingPressure(ring, periodic: false, _lastWarnedDroppedBytes))
            return;

        LogRecordingPressure(path, ring, final);
    }

    private void LogRecordingPressure(string? path, PcmRingBuffer ring, bool final)
    {
        var droppedKb = ring.DroppedBytes / 1024.0;
        var backlogKb = ring.Count / 1024.0;
        var peakKb = ring.PeakCount / 1024.0;
        _lastWarnedDroppedBytes = ring.DroppedBytes;

        if (final)
        {
            Log.Warning(
                "Recording buffer pressure (final) {Path}: peak backlog {PeakKb:F1} KB, dropped {DroppedKb:F1} KB",
                path,
                peakKb,
                droppedKb);
            return;
        }

        Log.Warning(
            "Recording buffer pressure {Path}: backlog {BacklogKb:F1} KB, peak {PeakKb:F1} KB, dropped {DroppedKb:F1} KB",
            path,
            backlogKb,
            peakKb,
            droppedKb);
    }

    private void CleanupRecordingState()
    {
        try
        {
            StopCaptureAndDrain();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Recording cleanup failed");
        }

        IsRecording = false;
        ActiveNoradId = null;
        ActiveOutputPath = null;
        _activeContainer = RecordingContainerFormat.Wav;
    }

    public void Dispose()
    {
        _operationLock.Wait();
        try
        {
            lock (_initializationGate)
            {
                if (_disposed)
                    return;

                _disposed = true;
            }

            try
            {
                StopCoreAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Recording dispose stop failed");
            }

        }
        finally
        {
            _operationLock.Release();
        }

        _dataAvailable.Dispose();
    }
}
