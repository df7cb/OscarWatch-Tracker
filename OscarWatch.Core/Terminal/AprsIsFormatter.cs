namespace OscarWatch.Core.Terminal;

/// <summary>Formats heard RF frames for a receive-only APRS-IS gateway (RF to internet only).</summary>
public static class AprsIsFormatter
{
    public static readonly TimeSpan DupeWindow = TimeSpan.FromSeconds(30);

    private static readonly string[] NoGatePathElements = ["TCPIP", "TCPXX", "NOGATE", "RFONLY"];

    public static string LoginLine(string callsign, string passcode, string software, string version) =>
        $"user {TerminalText.BaseCallsign(callsign)} pass {(string.IsNullOrWhiteSpace(passcode) ? "-1" : passcode.Trim())} vers {software} {version}";

    /// <summary>
    /// TNC2 line <c>SRC&gt;DEST,PATH,qAR,IGATE:payload</c>, or null when the frame must not be gated
    /// (not a UI frame, NOGATE/RFONLY/TCPIP in the path, third-party internet traffic, or empty payload).
    /// </summary>
    public static string? FormatGateLine(Ax25Frame frame, string igateCallsign)
    {
        if (!frame.IsUi || frame.Pid != Ax25Frame.NoLayer3Pid)
            return null;

        var info = frame.InfoText;
        if (info.Length == 0 || info.StartsWith('?'))
            return null;
        if (info.StartsWith('}') && (info.Contains(",TCPIP", StringComparison.OrdinalIgnoreCase)
                                     || info.Contains(",TCPXX", StringComparison.OrdinalIgnoreCase)))
            return null;
        if (frame.Digipeaters.Any(d => NoGatePathElements.Contains(d.Callsign, StringComparer.OrdinalIgnoreCase)))
            return null;

        var igate = TerminalText.BaseCallsign(igateCallsign);
        if (igate.Length == 0)
            return null;

        var path = frame.PathText();
        var header = frame.Source.Text + ">" + frame.Destination.Text + (path.Length > 0 ? "," + path : "");
        return $"{header},qAR,{igate}:{info}";
    }

    /// <summary>Dupe key: source, destination and payload (the path differs between digipeated copies).</summary>
    public static string DupeKey(Ax25Frame frame) =>
        frame.Source.Text + ">" + frame.Destination.Text + ":" + frame.InfoText;
}

/// <summary>Remembers gated frames for <see cref="AprsIsFormatter.DupeWindow"/>.</summary>
public sealed class AprsIsDupeFilter
{
    private readonly Dictionary<string, DateTime> _seen = new(StringComparer.Ordinal);

    /// <summary>True the first time <paramref name="key"/> is seen inside the window.</summary>
    public bool TryAccept(string key, DateTime utc)
    {
        if (_seen.Count > 256)
        {
            foreach (var old in _seen.Where(kv => utc - kv.Value >= AprsIsFormatter.DupeWindow).Select(kv => kv.Key).ToList())
                _seen.Remove(old);
        }

        if (_seen.TryGetValue(key, out var last) && utc - last < AprsIsFormatter.DupeWindow)
            return false;
        _seen[key] = utc;
        return true;
    }
}
