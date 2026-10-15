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

    private DateTime _lastAudioUtc;

    public CaptureStallWatch(DateTime utcNow, TimeSpan? timeout = null)
    {
        Timeout = timeout ?? DefaultTimeout;
        _lastAudioUtc = utcNow;
    }

    public TimeSpan Timeout { get; set; }

    /// <summary>Reopens requested so far.</summary>
    public int Reopens { get; private set; }

    public void Reset(DateTime utcNow) => _lastAudioUtc = utcNow;

    /// <summary>
    /// Call on every capture read. Returns true when the caller should reopen the device now.
    /// Pass <paramref name="paused"/> while audio is legitimately not expected.
    /// </summary>
    public bool OnRead(DateTime utcNow, bool gotSamples, bool paused = false)
    {
        if (gotSamples || paused)
        {
            _lastAudioUtc = utcNow;
            return false;
        }

        if (utcNow - _lastAudioUtc < Timeout)
            return false;

        _lastAudioUtc = utcNow;
        Reopens++;
        return true;
    }
}
