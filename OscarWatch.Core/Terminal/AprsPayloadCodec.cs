using System.Globalization;
using OscarWatch.Core.Geo;

namespace OscarWatch.Core.Terminal;

public enum AprsPacketType
{
    Unknown,
    Status,
    Message,
    Ack,
    Reject,
    Position,
    MicE,
    MaidenheadBeacon,
    Telemetry,
    Other
}

public sealed record AprsPacket(
    AprsPacketType Type,
    string Text,
    string? Addressee = null,
    string? MessageNumber = null,
    string? Grid = null);

/// <summary>Builds and parses the APRS payloads used on satellite digipeaters.</summary>
public static class AprsPayloadCodec
{
    public const int AddresseeLength = 9;

    /// <summary>
    /// Destination tocall for frames sent by OscarWatch. <c>APZ</c> is the experimental range;
    /// replace with an allocated <c>APOWxx</c> once the APRS device ID registry assigns one.
    /// </summary>
    public const string Tocall = "APZOWT";

    public static string BuildStatus(string text) => ">" + text.Trim();

    public static string BuildMessage(string addressee, string text, string? messageNumber = null)
    {
        var body = text.Trim().Replace("{", "", StringComparison.Ordinal).Replace("|", "", StringComparison.Ordinal).Replace("~", "", StringComparison.Ordinal);
        if (body.Length > 67)
            body = body[..67];
        var line = ":" + PadAddressee(addressee) + ":" + body;
        return string.IsNullOrEmpty(messageNumber) ? line : line + "{" + messageNumber;
    }

    public static string BuildAck(string addressee, string messageNumber) =>
        ":" + PadAddressee(addressee) + ":ack" + messageNumber;

    public static AprsPacket Parse(string? info, string? destination = null)
    {
        if (string.IsNullOrEmpty(info))
            return new AprsPacket(AprsPacketType.Unknown, "");

        switch (info[0])
        {
            case '>':
            {
                var text = info[1..].Trim();
                return new AprsPacket(AprsPacketType.Status, text, Grid: TerminalText.FindGrid(text.Replace('/', ' ')));
            }
            case ':':
                return ParseMessage(info);
            case '!' or '=':
                return ParsePosition(info, 1);
            case '/' or '@':
                return ParsePosition(info, 8);
            case '`' or '\'':
                return ParseMicE(info, destination);
            case '[':
            {
                var close = info.IndexOf(']');
                var gridText = close > 1 ? info[1..close] : info[1..];
                var rest = close > 0 ? info[(close + 1)..].Trim() : "";
                return new AprsPacket(AprsPacketType.MaidenheadBeacon, rest, Grid: TerminalText.FindGrid(gridText));
            }
            case 'T' when info.Length > 1 && info[1] == '#':
                return new AprsPacket(AprsPacketType.Telemetry, info);
            default:
                return new AprsPacket(AprsPacketType.Other, info.Trim(), Grid: TerminalText.FindGrid(info));
        }
    }

    private static AprsPacket ParseMessage(string info)
    {
        if (info.Length < AddresseeLength + 2 || info[AddresseeLength + 1] != ':')
            return new AprsPacket(AprsPacketType.Other, info.Trim(), Grid: TerminalText.FindGrid(info));

        var addressee = info[1..(AddresseeLength + 1)].Trim().ToUpperInvariant();
        var body = info[(AddresseeLength + 2)..];

        if (body.StartsWith("ack", StringComparison.Ordinal))
            return new AprsPacket(AprsPacketType.Ack, body, addressee, body[3..].Trim());
        if (body.StartsWith("rej", StringComparison.Ordinal))
            return new AprsPacket(AprsPacketType.Reject, body, addressee, body[3..].Trim());

        string? number = null;
        var brace = body.LastIndexOf('{');
        if (brace >= 0)
        {
            number = body[(brace + 1)..].Trim();
            var close = number.IndexOf('}');
            if (close >= 0)
                number = number[..close];
            body = body[..brace];
        }

        var text = body.Trim();
        return new AprsPacket(
            AprsPacketType.Message,
            text,
            addressee,
            string.IsNullOrEmpty(number) ? null : number,
            TerminalText.FindGrid(text));
    }

    private static AprsPacket ParsePosition(string info, int start)
    {
        if (info.Length <= start)
            return new AprsPacket(AprsPacketType.Position, "");

        var body = info[start..];
        if (body.Length >= 19 && (char.IsDigit(body[0]) || body[0] == ' '))
        {
            if (TryParseUncompressed(body, out var lat, out var lon))
            {
                var comment = body.Length > 19 ? body[19..].Trim() : "";
                return new AprsPacket(AprsPacketType.Position, comment, Grid: SafeGrid(lat, lon));
            }
        }
        else if (body.Length >= 13 && TryParseCompressed(body, out var clat, out var clon))
        {
            var comment = body.Length > 13 ? body[13..].Trim() : "";
            return new AprsPacket(AprsPacketType.Position, comment, Grid: SafeGrid(clat, clon));
        }

        return new AprsPacket(AprsPacketType.Position, body.Trim(), Grid: TerminalText.FindGrid(body));
    }

    internal static bool TryParseUncompressed(string body, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        var latText = body[..8].Replace(' ', '0');
        var lonText = body.Substring(9, 9).Replace(' ', '0');
        if (!double.TryParse(latText[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var latDeg)
            || !double.TryParse(latText[2..7], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var latMin)
            || !double.TryParse(lonText[..3], NumberStyles.None, CultureInfo.InvariantCulture, out var lonDeg)
            || !double.TryParse(lonText[3..8], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var lonMin))
            return false;

        var ns = char.ToUpperInvariant(latText[7]);
        var ew = char.ToUpperInvariant(lonText[8]);
        if (ns is not ('N' or 'S') || ew is not ('E' or 'W'))
            return false;

        lat = (latDeg + latMin / 60.0) * (ns == 'S' ? -1 : 1);
        lon = (lonDeg + lonMin / 60.0) * (ew == 'W' ? -1 : 1);
        return Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
    }

    internal static bool TryParseCompressed(string body, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        for (var i = 1; i <= 8; i++)
        {
            if (body[i] is < '!' or > '{')
                return false;
        }

        var y = Base91(body.AsSpan(1, 4));
        var x = Base91(body.AsSpan(5, 4));
        lat = 90.0 - y / 380926.0;
        lon = -180.0 + x / 190463.0;
        return Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
    }

    /// <summary>Mic-E: latitude digits from the destination callsign, longitude from info bytes 1 to 3.</summary>
    internal static AprsPacket ParseMicE(string info, string? destination)
    {
        var comment = info.Length > 9 ? info[9..].Trim() : "";
        var dest = TerminalText.BaseCallsign(destination);
        if (info.Length < 9 || dest.Length < 6)
            return new AprsPacket(AprsPacketType.MicE, comment);

        Span<int> digits = stackalloc int[6];
        for (var i = 0; i < 6; i++)
        {
            var c = dest[i];
            digits[i] = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'J' => c - 'A',
                >= 'P' and <= 'Y' => c - 'P',
                'K' or 'L' or 'Z' => 0,
                _ => -1
            };
            if (digits[i] < 0)
                return new AprsPacket(AprsPacketType.MicE, comment);
        }

        var lat = digits[0] * 10 + digits[1] + (digits[2] * 10 + digits[3] + (digits[4] * 10 + digits[5]) / 100.0) / 60.0;
        var north = dest[3] is >= 'P' and <= 'Z';
        var lonOffset = dest[4] is >= 'P' and <= 'Z' ? 100 : 0;
        var west = dest[5] is >= 'P' and <= 'Z';

        var d = info[1] - 28 + lonOffset;
        if (d is >= 180 and <= 189)
            d -= 80;
        else if (d is >= 190 and <= 199)
            d -= 190;
        var m = info[2] - 28;
        if (m >= 60)
            m -= 60;
        var h = info[3] - 28;
        var lon = d + (m + h / 100.0) / 60.0;

        lat = north ? lat : -lat;
        lon = west ? -lon : lon;
        var grid = Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180 ? SafeGrid(lat, lon) : null;
        return new AprsPacket(AprsPacketType.MicE, comment, Grid: grid);
    }

    private static string? SafeGrid(double lat, double lon)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
            return null;
        lat = Math.Clamp(lat, -89.9999, 89.9999);
        lon = Math.Clamp(lon, -179.9999, 179.9999);
        var grid = MaidenheadGrid.FromLatLon(lat, lon, 6);
        return grid[..4] + grid[4..].ToLowerInvariant();
    }

    private static double Base91(ReadOnlySpan<char> chars)
    {
        double value = 0;
        foreach (var c in chars)
            value = value * 91 + (c - 33);
        return value;
    }

    private static string PadAddressee(string addressee)
    {
        var a = (addressee ?? "").Trim().ToUpperInvariant();
        if (a.Length > AddresseeLength)
            a = a[..AddresseeLength];
        return a.PadRight(AddresseeLength);
    }
}
