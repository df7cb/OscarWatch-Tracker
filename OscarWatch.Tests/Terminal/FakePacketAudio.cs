using System.Diagnostics;
using OscarWatch.Terminal;

namespace OscarWatch.Tests.Terminal;

/// <summary>Soundcard stand-in: playback drains in real time; capture is whatever the test feeds in.</summary>
internal sealed class FakePacketAudio : IPacketAudio
{
    private readonly object _gate = new();
    private readonly Queue<float> _capture = new();
    private readonly SemaphoreSlim _captureSignal = new(0, int.MaxValue);
    private readonly List<float> _played = [];
    private long _playEndTimestamp;

    public FakePacketAudio(int sampleRate = 12000)
    {
        CaptureSampleRate = sampleRate;
        PlaybackSampleRate = sampleRate;
    }

    public bool IsAvailable => true;
    public int CaptureSampleRate { get; }
    public int PlaybackSampleRate { get; }
    public double OutputLatencySeconds => 0.01;
    public double InputPeak => 0;
    public bool Started { get; private set; }
    public int ClearCount { get; private set; }

    public float[] Played
    {
        get
        {
            lock (_gate)
                return _played.ToArray();
        }
    }

    public int StartCount { get; private set; }

    public void Start(string? inputId, string? inputName, string? outputId, string? outputName)
    {
        Started = true;
        StartCount++;
    }

    public void Stop() => Started = false;

    public void Feed(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            foreach (var s in samples)
                _capture.Enqueue(s);
        }

        _captureSignal.Release();
    }

    public int ReadCapture(Span<float> destination)
    {
        lock (_gate)
        {
            var n = 0;
            while (n < destination.Length && _capture.TryDequeue(out var s))
                destination[n++] = s;
            return n;
        }
    }

    public bool WaitForCapture(TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_capture.Count > 0)
                return true;
        }

        return _captureSignal.Wait(timeout, cancellationToken);
    }

    public void Enqueue(float[] samples)
    {
        lock (_gate)
        {
            _played.AddRange(samples);
            var now = Stopwatch.GetTimestamp();
            var start = Math.Max(now, _playEndTimestamp);
            _playEndTimestamp = start + (long)(samples.Length / (double)PlaybackSampleRate * Stopwatch.Frequency);
        }
    }

    public long QueuedPlaybackSamples
    {
        get
        {
            lock (_gate)
            {
                var left = _playEndTimestamp - Stopwatch.GetTimestamp();
                return left <= 0 ? 0 : (long)(left / (double)Stopwatch.Frequency * PlaybackSampleRate);
            }
        }
    }

    public void ClearPlayback()
    {
        lock (_gate)
        {
            _playEndTimestamp = 0;
            ClearCount++;
        }
    }

    public async Task WaitForDrainAsync(CancellationToken cancellationToken)
    {
        while (QueuedPlaybackSamples > 0)
            await Task.Delay(5, cancellationToken);
    }

    public void Dispose() => _captureSignal.Dispose();
}
