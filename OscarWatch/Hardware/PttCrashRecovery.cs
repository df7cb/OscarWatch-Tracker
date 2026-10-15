using OscarWatch.Core.Hardware;
using OscarWatch.Core.Services;
using Serilog;

namespace OscarWatch.Hardware;

/// <summary>
/// At startup, finds the Terminal PTT marker left by a crash while transmitting. A radio keyed over
/// CAT stays on the air after OscarWatch stops, so this sends a CAT unkey as soon as the rig
/// connects. Handshake and separate COM port lines drop by themselves when the port closes.
/// </summary>
public sealed class PttCrashRecovery : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PttCrashRecovery>();
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);

    private readonly IRigController _rig;
    private readonly PttSessionMarker _marker;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _hasNotice;

    public PttCrashRecovery(IRigController rig, PttSessionMarker marker)
    {
        _rig = rig;
        _marker = marker;
    }

    public PttSessionMarker Marker => _marker;

    /// <summary>Raised once, after a recovery unkey was sent.</summary>
    public event EventHandler? Recovered;

    /// <summary>True after a stale CAT PTT was found and unkeyed this session, until <see cref="AcknowledgeNotice"/>.</summary>
    public bool HasNotice => _hasNotice;

    public void AcknowledgeNotice() => _hasNotice = false;

    public void Start()
    {
        if (!_marker.Exists)
            return;

        var method = _marker.RecordedMethod;
        Log.Warning("OscarWatch stopped while the Terminal modem was transmitting (PTT {Method})", method);
        if (method != PttMethod.Cat)
        {
            _marker.Clear();
            return;
        }

        _ = Task.Run(() => UnkeyWhenConnectedAsync(_cts.Token));
    }

    private async Task UnkeyWhenConnectedAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + GiveUpAfter;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                if (_rig.GetStatus().IsConnected)
                {
                    _rig.SetPtt(false);
                    _marker.Clear();
                    _hasNotice = true;
                    Log.Warning("Sent a CAT unkey to the radio left transmitting by the last session");
                    Recovered?.Invoke(this, EventArgs.Empty);
                    return;
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }

            Log.Warning("Radio did not connect within {Minutes} minutes; stale PTT marker kept", GiveUpAfter.TotalMinutes);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Stale PTT recovery failed");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
