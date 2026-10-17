using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Core.Terminal;

/// <summary>
/// How long a single-VFO radio must stay on the uplink for frames handed to the modem.
/// The modem sends back-to-back frames in one transmission, so a frame queued while
/// the previous one is still on air extends the window by its airtime only.
/// </summary>
public sealed class TerminalTransmitWindow
{
    private readonly object _gate = new();
    private DateTime _airEndsUtc = DateTime.MinValue;

    /// <summary>
    /// Records one frame and returns how long the uplink is needed from <paramref name="nowUtc"/>.
    /// </summary>
    public TimeSpan Add(int frameBytes, int baud, int txDelayMs, int txTailMs, DateTime nowUtc)
    {
        var airtime = FrameAirtime(frameBytes, baud);
        lock (_gate)
        {
            var start = _airEndsUtc > nowUtc ? _airEndsUtc : nowUtc + TimeSpan.FromMilliseconds(txDelayMs);
            _airEndsUtc = start + airtime;
            return _airEndsUtc - nowUtc + TimeSpan.FromMilliseconds(txTailMs);
        }
    }

    /// <summary>
    /// HDLC airtime: payload plus FCS, worst-case bit stuffing (one bit in five) and the closing flag.
    /// </summary>
    public static TimeSpan FrameAirtime(int frameBytes, int baud)
    {
        if (frameBytes <= 0 || baud <= 0)
            return TimeSpan.Zero;

        var bits = (frameBytes + 2) * 8 * 6 / 5 + 8;
        return TimeSpan.FromSeconds((double)bits / baud);
    }

    /// <summary>Bit rate from the profile's modem hint: 9600 for G3RUH, otherwise 1200.</summary>
    public static int BaudForModemHint(string? modemHint) =>
        TerminalSatelliteProfiles.ModemHintUses9600Baud(modemHint) ? HdlcEncoder.G3ruhBaud : HdlcEncoder.Baud;
}
