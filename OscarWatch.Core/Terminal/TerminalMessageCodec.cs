namespace OscarWatch.Core.Terminal;

/// <summary>A received or sent Terminal frame reduced to the From, To and Message columns.</summary>
public sealed record TerminalDecodedMessage(
    DateTime Utc,
    string From,
    string To,
    string Text,
    string? Grid,
    string? Report,
    bool IsCq,
    bool IsRoger,
    bool IsSignOff,
    bool IsTelemetry,
    bool IsDigipeated,
    string Path,
    string Monitor,
    AprsPacket? Aprs = null)
{
    /// <summary>True for APRS position-only or beacon frames that carry no operator text.</summary>
    public bool IsBeacon => (Aprs?.Type is AprsPacketType.Position or AprsPacketType.MicE or AprsPacketType.MaidenheadBeacon)
                            && !IsCq && Report is null;
}

/// <summary>Outgoing frame plus the text shown in the message log.</summary>
public sealed record TerminalOutgoingFrame(Ax25Frame Frame, string To, string Text);

/// <summary>Turns AX.25 frames into Terminal messages and builds outgoing frames for either format.</summary>
public static class TerminalMessageCodec
{
    public const string CqTo = "CQ";
    public const string AllTo = "ALL";

    private static readonly HashSet<string> NonStationDestinations = new(StringComparer.Ordinal)
    {
        "APRS", "BEACON", "ID", "GPS", "SPC", "SYM", "ALL", "QST", "MAIL", "TEST", "TLM", "TELEM"
    };

    public static TerminalDecodedMessage Decode(Ax25Frame frame, TerminalMessageFormat format, DateTime utc)
    {
        var info = frame.InfoText;
        var from = frame.Source.Text;
        var dest = frame.Destination.Text;
        AprsPacket? aprs = null;
        string to;
        string text;
        string? grid;

        if (format == TerminalMessageFormat.Aprs)
        {
            aprs = AprsPayloadCodec.Parse(info, dest);
            text = aprs.Text;
            grid = aprs.Grid;
            if (aprs.Addressee is { Length: > 0 })
                to = aprs.Addressee;
            else if (TerminalText.StartsWithCq(text))
                to = CqTo;
            else if (aprs.Type == AprsPacketType.MicE || IsNonStationDestination(dest))
                to = AllTo;
            else
                to = dest;
        }
        else
        {
            text = info;
            grid = TerminalText.FindGrid(text);
            to = dest;
        }

        var isCq = string.Equals(TerminalText.BaseCallsign(to), CqTo, StringComparison.Ordinal)
                   || TerminalText.StartsWithCq(text);
        var isTelemetry = aprs?.Type == AprsPacketType.Telemetry
                          || text.StartsWith("[TLM", StringComparison.OrdinalIgnoreCase)
                          || !TerminalText.LooksLikeCallsign(from);

        return new TerminalDecodedMessage(
            utc,
            from,
            to,
            text,
            grid,
            TerminalText.FindReport(text),
            isCq,
            TerminalText.ContainsRoger(text),
            TerminalText.ContainsSignOff(text),
            isTelemetry,
            frame.HasBeenDigipeated,
            frame.PathText(),
            frame.ToMonitorString(),
            aprs);
    }

    /// <summary>
    /// Builds an outgoing UI frame. APRS: CQ or empty To sends a status (prefixed with CQ when needed),
    /// a callsign sends an APRS message. Plain text: To becomes the AX.25 destination.
    /// </summary>
    public static TerminalOutgoingFrame BuildOutgoing(
        TerminalMessageFormat format,
        Ax25Address source,
        string? to,
        string text,
        IReadOnlyList<Ax25Address> path,
        string? aprsMessageNumber = null)
    {
        var target = (to ?? "").Trim().ToUpperInvariant();
        var body = (text ?? "").Trim();
        var broadcast = target.Length == 0 || target == CqTo || target == AllTo;

        if (format == TerminalMessageFormat.Aprs)
        {
            var dest = Ax25Address.Parse(AprsPayloadCodec.Tocall);
            if (broadcast)
            {
                if (target == CqTo && !TerminalText.StartsWithCq(body))
                    body = "CQ " + body;
                var frame = Ax25Frame.CreateUi(dest, source, path, AprsPayloadCodec.BuildStatus(body));
                return new TerminalOutgoingFrame(frame, TerminalText.StartsWithCq(body) ? CqTo : AllTo, body);
            }

            var msg = Ax25Frame.CreateUi(dest, source, path, AprsPayloadCodec.BuildMessage(target, body, aprsMessageNumber));
            return new TerminalOutgoingFrame(msg, target, body);
        }

        var destText = broadcast ? (target.Length == 0 ? CqTo : target) : target;
        if (!Ax25Address.TryParse(destText, out var destination))
            throw new FormatException($"'{destText}' is not a valid AX.25 destination.");
        var plain = Ax25Frame.CreateUi(destination with { HasBeenRepeated = false }, source, path, body);
        return new TerminalOutgoingFrame(plain, destination.Text, body);
    }

    /// <summary>Expands macro placeholders: <c>{CALL}</c>, <c>{MYCALL}</c>, <c>{MYGRID}</c>, <c>{MYGRID4}</c>.</summary>
    public static string ExpandMacro(string template, string myCall, string myGrid, string? theirCall)
    {
        var grid = (myGrid ?? "").Trim();
        var grid4 = grid.Length >= 4 ? grid[..4].ToUpperInvariant() : grid.ToUpperInvariant();
        var grid6 = TerminalText.TryNormaliseGrid(grid.Length >= 6 ? grid[..6] : grid, out var g) ? g : grid;
        return (template ?? "")
            .Replace("{CALL}", (theirCall ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{MYCALL}", (myCall ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{MYGRID4}", grid4, StringComparison.OrdinalIgnoreCase)
            .Replace("{MYGRID}", grid6, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool IsNonStationDestination(string dest)
    {
        var d = TerminalText.BaseCallsign(dest);
        return d.StartsWith("AP", StringComparison.Ordinal) || NonStationDestinations.Contains(d);
    }
}
