namespace OscarWatch.Core.Logbook;

/// <summary>
/// History-column mode text. One token when uplink and downlink match, otherwise <c>USB/LSB</c>.
/// </summary>
public static class QsoModeText
{
    public static string Format(string? uplink, string? downlink)
    {
        var tx = (uplink ?? "").Trim();
        var rx = (downlink ?? "").Trim();
        if (tx.Equals(rx, StringComparison.OrdinalIgnoreCase))
            return tx;

        return $"{tx}/{rx}";
    }

    /// <summary>
    /// Splits an edited mode. A single token is stored on both uplink and downlink.
    /// A blank side copies the side that was filled in.
    /// </summary>
    public static (string Uplink, string Downlink) Split(string? text)
    {
        var value = (text ?? "").Trim().ToUpperInvariant();
        if (value.Length == 0)
            return ("", "");

        var slash = value.IndexOf('/');
        if (slash < 0)
            return (value, value);

        var uplink = value[..slash].Trim();
        var downlink = value[(slash + 1)..].Trim();
        if (uplink.Length == 0)
            uplink = downlink;
        if (downlink.Length == 0)
            downlink = uplink;

        return (uplink, downlink);
    }
}
