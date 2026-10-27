namespace OscarWatch.Core.Recording;

/// <summary>
/// Decides when a soundcard capture that has stopped delivering audio should be reopened.
/// Windows can invalidate a WASAPI stream (for example when the device format or the
/// recording level dialogue changes) and PortAudio then stops calling back without an error.
/// After each reopen the timer starts again, so a device that stays dead is retried at the
/// same interval rather than in a tight loop.
/// </summary>
public sealed class CaptureStallWatch
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    private DateTime _lastAudioUtc;
    private TimeSpan _retryDelay;

    public CaptureStallWatch(DateTime utcNow, TimeSpan? timeout = null)
    {
        Timeout = timeout ?? DefaultTimeout;
        _retryDelay = Timeout;
        _lastAudioUtc = utcNow;
    }

    public TimeSpan Timeout { get; set; }

    /// <summary>Delay before the next reopen attempt after a stall (grows on failed reopens).</summary>
    public TimeSpan RetryDelay => _retryDelay;

    /// <summary>Reopens requested so far.</summary>
    public int Reopens { get; private set; }

    public void Reset(DateTime utcNow)
    {
        _lastAudioUtc = utcNow;
        _retryDelay = Timeout;
    }

    /// <summary>Call after a reopen attempt. Failed attempts increase <see cref="RetryDelay"/> up to <see cref="MaxRetryDelay"/>.</summary>
    public void NoteReopenResult(bool succeeded)
    {
        if (succeeded)
        {
            _retryDelay = Timeout;
            return;
        }

        var nextMs = Math.Min(MaxRetryDelay.TotalMilliseconds, _retryDelay.TotalMilliseconds * 2);
        _retryDelay = TimeSpan.FromMilliseconds(Math.Max(Timeout.TotalMilliseconds, nextMs));
    }

    /// <summary>
    /// Call on every capture read. Returns true when the caller should reopen the device now.
    /// Pass <paramref name="paused"/> while audio is legitimately not expected.
    /// </summary>
    public bool OnRead(DateTime utcNow, bool gotSamples, bool paused = false)
    {
        if (gotSamples || paused)
        {
            _lastAudioUtc = utcNow;
            _retryDelay = Timeout;
            return false;
        }

        if (utcNow - _lastAudioUtc < _retryDelay)
            return false;

        _lastAudioUtc = utcNow;
        Reopens++;
        return true;
    }
}
