namespace OscarWatch.Core.Terminal.Uhm;

/// <summary>
/// Decides which decoded frames are posted to UHM <c>/heard</c>. Only frames another station addressed to
/// us are reported: hearing our own digipeated frame does not show that the other station heard us.
/// </summary>
public sealed class UhmReportPolicy
{
    public static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.Ordinal);

    /// <summary>Builds the report when the frame should be posted; records it for the 30-second dedup.</summary>
    public bool TryCreateReport(
        TerminalDecodedMessage message,
        string myCall,
        string satelliteName,
        string? client,
        out UhmHeardReport? report)
    {
        report = null;
        var me = TerminalText.BaseCallsign(myCall);
        var tx = TerminalText.BaseCallsign(message.From);
        var rx = TerminalText.BaseCallsign(message.To);
        var satellite = (satelliteName ?? "").Trim();

        if (me.Length == 0 || satellite.Length == 0)
            return false;
        if (!string.Equals(rx, me, StringComparison.Ordinal) || string.Equals(tx, rx, StringComparison.Ordinal))
            return false;
        if (!TerminalText.LooksLikeCallsign(tx))
            return false;
        if (message.IsCq || message.IsBeacon || message.IsTelemetry)
            return false;

        Prune(message.Utc);
        var key = $"{satellite.ToUpperInvariant()}|{tx}|{rx}";
        if (_recent.TryGetValue(key, out var last) && message.Utc - last < DedupWindow)
            return false;

        _recent[key] = message.Utc;
        report = new UhmHeardReport(satellite, tx, rx, message.Utc, message.Text, client);
        return true;
    }

    private void Prune(DateTime nowUtc)
    {
        if (_recent.Count < 64)
            return;
        foreach (var key in _recent.Where(kv => nowUtc - kv.Value >= DedupWindow).Select(kv => kv.Key).ToList())
            _recent.Remove(key);
    }
}
