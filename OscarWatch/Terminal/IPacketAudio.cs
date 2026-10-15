namespace OscarWatch.Terminal;

/// <summary>Soundcard capture and playback for the built-in packet modem.</summary>
public interface IPacketAudio : IDisposable
{
    bool IsAvailable { get; }

    int CaptureSampleRate { get; }

    int PlaybackSampleRate { get; }

    /// <summary>Time from handing a sample to the soundcard until it reaches the DAC, in seconds.</summary>
    double OutputLatencySeconds { get; }

    /// <summary>Recent input peak, 0 to 1.</summary>
    double InputPeak { get; }

    /// <summary>Opens capture and playback. Empty device names use the system default.</summary>
    void Start(string? inputId, string? inputName, string? outputId, string? outputName);

    void Stop();

    /// <summary>Copies captured samples without blocking. Returns the number copied.</summary>
    int ReadCapture(Span<float> destination);

    /// <summary>Blocks until captured audio is waiting, the timeout passes or the token is cancelled.</summary>
    bool WaitForCapture(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Appends samples at <see cref="PlaybackSampleRate"/> to the playback queue, even while it is playing.</summary>
    void Enqueue(float[] samples);

    /// <summary>Samples queued and not yet handed to the soundcard.</summary>
    long QueuedPlaybackSamples { get; }

    /// <summary>Drops everything queued for playback.</summary>
    void ClearPlayback();

    /// <summary>Completes once the playback queue is empty.</summary>
    Task WaitForDrainAsync(CancellationToken cancellationToken);
}
