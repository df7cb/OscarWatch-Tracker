using PortAudioSharp;
using Serilog;

namespace OscarWatch.Recording;

/// <summary>
/// Process-wide PortAudio initialisation and open-stream accounting. A USB soundcard reset can
/// leave PortAudio unable to open new streams until Terminate/Initialize, which is only safe when
/// no other capture or playback stream is active.
/// </summary>
internal static class PortAudioRuntime
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(PortAudioRuntime));
    private static readonly object Gate = new();
    private static bool _initialized;
    private static bool _probeAttempted;
    private static bool _probePassed;
    private static int _activeStreams;

    public static bool IsInitialized
    {
        get
        {
            lock (Gate)
                return _initialized;
        }
    }

    public static int ActiveStreamCount => Volatile.Read(ref _activeStreams);

    public static bool EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
                return true;

            if (_probeAttempted && !_probePassed)
                return false;

            if (!_probeAttempted)
            {
                _probeAttempted = true;
                if (!PortAudioOutOfProcessProbe.TryRun(out var probeError))
                {
                    Log.Warning("PortAudio out-of-process probe failed: {Reason}", probeError);
                    _probePassed = false;
                    return false;
                }

                _probePassed = true;
            }

            try
            {
                PortAudio.Initialize();
                _initialized = true;
                Log.Information("PortAudio initialized ({Version})", PortAudio.VersionInfo.versionText);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PortAudio initialization failed");
                _initialized = false;
                return false;
            }
        }
    }

    public static void NotifyStreamOpened() => Interlocked.Increment(ref _activeStreams);

    public static void NotifyStreamClosed()
    {
        var remaining = Interlocked.Decrement(ref _activeStreams);
        if (remaining < 0)
            Interlocked.Exchange(ref _activeStreams, 0);
    }

    /// <summary>
    /// Terminate and re-initialise PortAudio when no stream is open anywhere in the process.
    /// </summary>
    public static bool TryRefreshHost()
    {
        lock (Gate)
        {
            if (!_initialized && !EnsureInitialized())
                return false;

            if (Volatile.Read(ref _activeStreams) > 0)
            {
                Log.Debug(
                    "PortAudio host refresh skipped because {StreamCount} stream(s) are still open",
                    _activeStreams);
                return false;
            }

            try
            {
                PortAudio.Terminate();
                _initialized = false;
                PortAudio.Initialize();
                _initialized = true;
                Log.Information("PortAudio host refreshed after Terminate/Initialize");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PortAudio host refresh failed");
                _initialized = false;
                return false;
            }
        }
    }
}
