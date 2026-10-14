using System.Globalization;
using System.Text;
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

/// <summary>Mic-E message code from the three message bits in the destination callsign.</summary>
public enum AprsMicEStatus
{
    Emergency = 0,
    Priority = 1,
    Special = 2,
    Committed = 3,
    Returning = 4,
    InService = 5,
    EnRoute = 6,
    OffDuty = 7,
    Custom0 = 10,
    Custom1 = 11,
    Custom2 = 12,
    Custom3 = 13,
    Custom4 = 14,
    Custom5 = 15,
    Custom6 = 16
}

/// <summary>
/// A decoded APRS position. Ambiguous positions are reported at the centre of the ambiguous area;
/// <see cref="AmbiguityDigits"/> says how many minute digits the sender blanked.
/// </summary>
public sealed record AprsPosition(
    double Latitude,
    double Longitude,
    char SymbolTable,
    char SymbolCode,
    int? CourseDeg,
    int? SpeedKnots,
    int? AmbiguityDigits,
    int? AltitudeMetres = null,
    AprsMicEStatus? MicEStatus = null);

public sealed record AprsPacket(
    AprsPacketType Type,
    string Text,
    string? Addressee = null,
    string? MessageNumber = null,
    string? Grid = null,
    AprsPosition? Position = null);

/// <summary>Builds and parses the APRS payloads used on satellite digipeaters.</summary>
public static class AprsPayloadCodec
{
    public const int AddresseeLength = 9;
    public const int MaxPositionCommentLength = 43;

    /// <summary>
    /// Destination tocall for frames sent by OscarWatch. <c>APZ</c> is the experimental range;
    /// replace with an allocated <c>APOWxx</c> once the APRS device ID registry assigns one.
    /// </summary>
    public const string Tocall = "APZOWT";

    private const int MaxPositionPrefix = 40;

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

    /// <summary>
    /// Uncompressed position without timestamp, messaging capable (<c>=</c>), for example
    /// <c>=5552.00N/00415.50W[IO85</c>. <paramref name="ambiguityDigits"/> (0 to 4) blanks minute digits from the right.
    /// </summary>
    public static string BuildPosition(double lat, double lon, char symbolTable, char symbolCode, string? comment, int ambiguityDigits = 0)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(lat), "Position is outside the valid range.");

        var ambiguity = Math.Clamp(ambiguityDigits, 0, 4);
        var latText = FormatCoordinate(Math.Abs(lat), 2, lat < 0 ? 'S' : 'N');
        var lonText = FormatCoordinate(Math.Abs(lon), 3, lon < 0 ? 'W' : 'E');
        latText = ApplyAmbiguity(latText, 2, ambiguity);
        lonText = ApplyAmbiguity(lonText, 3, ambiguity);

        var text = (comment ?? "").Replace("|", "", StringComparison.Ordinal).Replace("~", "", StringComparison.Ordinal).Trim();
        if (text.Length > MaxPositionCommentLength)
            text = text[..MaxPositionCommentLength].TrimEnd();
        return "=" + latText + symbolTable + lonText + symbolCode + text;
    }

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
            {
                var bang = info.IndexOf('!');
                if (bang is > 0 and <= MaxPositionPrefix)
                {
                    var prefixed = ParsePosition(info, bang + 1);
                    if (prefixed.Position is not null)
                        return prefixed;
                }

                return new AprsPacket(AprsPacketType.Other, info.Trim(), Grid: TerminalText.FindGrid(info));
            }
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
            if (TryParseUncompressed(body, out var lat, out var lon, out var ambiguity))
            {
                var comment = body.Length > 19 ? body[19..] : "";
                int? course = null, speed = null;
                if (TryParseCourseSpeed(comment, out var c, out var s))
                {
                    course = c;
                    speed = s;
                    comment = comment[7..];
                }

                var position = new AprsPosition(lat, lon, body[8], body[18], course, speed,
                    ambiguity == 0 ? null : ambiguity, ParseAltitudeFeetTag(comment));
                return new AprsPacket(AprsPacketType.Position, comment.Trim(), Grid: SafeGrid(lat, lon), Position: position);
            }
        }
        else if (body.Length >= 13 && TryParseCompressed(body, out var clat, out var clon))
        {
            var comment = body.Length > 13 ? body[13..] : "";
            DecodeCompressedExtension(body, out var course, out var speed, out var altitude);
            var position = new AprsPosition(clat, clon, body[0], body[9], course, speed, null,
                altitude ?? ParseAltitudeFeetTag(comment));
            return new AprsPacket(AprsPacketType.Position, comment.Trim(), Grid: SafeGrid(clat, clon), Position: position);
        }

        return new AprsPacket(AprsPacketType.Position, body.Trim(), Grid: TerminalText.FindGrid(body));
    }

    internal static bool TryParseUncompressed(string body, out double lat, out double lon) =>
        TryParseUncompressed(body, out lat, out lon, out _);

    internal static bool TryParseUncompressed(string body, out double lat, out double lon, out int ambiguity)
    {
        lat = 0;
        lon = 0;
        ambiguity = 0;
        foreach (var i in (ReadOnlySpan<int>)[2, 3, 5, 6])
        {
            if (body[i] == ' ')
                ambiguity++;
        }

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

        if (ambiguity > 0)
        {
            var unit = ambiguity switch { 1 => 0.1, 2 => 1.0, 3 => 10.0, _ => 60.0 };
            latMin = Math.Floor(latMin / unit) * unit + unit / 2;
            lonMin = Math.Floor(lonMin / unit) * unit + unit / 2;
        }

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

    /// <summary>Course and speed, or altitude, from the compressed <c>cs</c> bytes and compression type byte.</summary>
    private static void DecodeCompressedExtension(string body, out int? course, out int? speed, out int? altitudeMetres)
    {
        course = null;
        speed = null;
        altitudeMetres = null;
        if (body.Length < 13 || body[10] == ' ')
            return;

        var c = body[10] - 33;
        var s = body[11] - 33;
        var type = body[12] - 33;
        if (c is < 0 or > 90 || s is < 0 or > 90 || type is < 0 or > 63)
            return;

        if ((type & 0x18) == 0x10)
        {
            altitudeMetres = (int)Math.Round(Math.Pow(1.002, c * 91 + s) * 0.3048);
            return;
        }

        if (c <= 89)
        {
            course = c * 4;
            speed = (int)Math.Round(Math.Pow(1.08, s) - 1);
        }
    }

    private static bool TryParseCourseSpeed(string comment, out int course, out int speed)
    {
        course = 0;
        speed = 0;
        if (comment.Length < 7 || comment[3] != '/')
            return false;
        return int.TryParse(comment.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out course)
               && int.TryParse(comment.AsSpan(4, 3), NumberStyles.None, CultureInfo.InvariantCulture, out speed)
               && course <= 360;
    }

    /// <summary>Altitude from a <c>/A=001234</c> comment tag (feet), in metres.</summary>
    private static int? ParseAltitudeFeetTag(string comment)
    {
        var at = comment.IndexOf("/A=", StringComparison.Ordinal);
        if (at < 0 || comment.Length < at + 9)
            return null;
        return int.TryParse(comment.AsSpan(at + 3, 6), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var feet)
            ? (int)Math.Round(feet * 0.3048)
            : null;
    }

    /// <summary>
    /// Mic-E: latitude digits and message bits from the destination callsign, longitude, speed, course
    /// and symbol from info bytes 1 to 8, then an optional radio type byte, altitude and comment.
    /// </summary>
    internal static AprsPacket ParseMicE(string info, string? destination)
    {
        var rawComment = info.Length > 9 ? info[9..] : "";
        var dest = TerminalText.BaseCallsign(destination);
        if (info.Length < 9 || dest.Length < 6)
            return new AprsPacket(AprsPacketType.MicE, rawComment.Trim());

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
                return new AprsPacket(AprsPacketType.MicE, rawComment.Trim());
        }

        var ambiguity = 0;
        for (var i = 5; i >= 2; i--)
        {
            if (dest[i] is 'K' or 'L' or 'Z')
                ambiguity++;
        }

        var latMin = digits[2] * 10 + digits[3] + (digits[4] * 10 + digits[5]) / 100.0;
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
        double lonMin = m + h / 100.0;

        if (ambiguity > 0)
        {
            var unit = ambiguity switch { 1 => 0.1, 2 => 1.0, 3 => 10.0, _ => 60.0 };
            latMin = Math.Floor(latMin / unit) * unit + unit / 2;
            lonMin = Math.Floor(lonMin / unit) * unit + unit / 2;
        }

        var lat = digits[0] * 10 + digits[1] + latMin / 60.0;
        var lon = d + lonMin / 60.0;
        lat = north ? lat : -lat;
        lon = west ? -lon : lon;

        var sp = info[4] - 28;
        var dc = info[5] - 28;
        var se = info[6] - 28;
        int? speed = null, course = null;
        if (sp >= 0 && dc >= 0 && se >= 0)
        {
            var knots = sp * 10 + dc / 10;
            if (knots >= 800)
                knots -= 800;
            var cse = dc % 10 * 100 + se;
            if (cse >= 400)
                cse -= 400;
            speed = knots;
            course = cse is > 0 and <= 360 ? cse : null;
        }

        var comment = CleanMicEComment(rawComment, out var altitude);
        if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
            return new AprsPacket(AprsPacketType.MicE, comment);

        var position = new AprsPosition(lat, lon, info[8], info[7], course, speed,
            ambiguity == 0 ? null : ambiguity, altitude, MicEStatusFromDestination(dest));
        return new AprsPacket(AprsPacketType.MicE, comment, Grid: SafeGrid(lat, lon), Position: position);
    }

    /// <summary>Removes the Mic-E radio type prefix and suffix and an optional <c>xxx}</c> altitude.</summary>
    internal static string CleanMicEComment(string raw, out int? altitudeMetres)
    {
        altitudeMetres = null;
        var text = raw;
        if (text.Length > 0)
        {
            switch (text[0])
            {
                case '>':
                    text = text[1..];
                    if (text.EndsWith('=') || text.EndsWith('^'))
                        text = text[..^1];
                    break;
                case ']':
                    text = text[1..];
                    if (text.EndsWith('='))
                        text = text[..^1];
                    break;
                case '`' or '\'':
                    text = text[1..];
                    if (text.Length >= 2)
                        text = text[..^2];
                    break;
            }
        }

        if (text.Length >= 4 && text[3] == '}' && text[0] is >= '!' and <= '{' && text[1] is >= '!' and <= '{'
            && text[2] is >= '!' and <= '{')
        {
            altitudeMetres = (int)Base91(text.AsSpan(0, 3)) - 10000;
            text = text[4..];
        }

        return text.Trim();
    }

    private static AprsMicEStatus? MicEStatusFromDestination(string dest)
    {
        var value = 0;
        var custom = false;
        var standard = false;
        for (var i = 0; i < 3; i++)
        {
            var c = dest[i];
            value <<= 1;
            if (c is >= 'A' and <= 'K')
            {
                custom = true;
                value |= 1;
            }
            else if (c is >= 'P' and <= 'Z')
            {
                standard = true;
                value |= 1;
            }
        }

        if (value == 0)
            return AprsMicEStatus.Emergency;
        if (custom && standard)
            return null;
        return custom ? (AprsMicEStatus)(10 + 7 - value) : (AprsMicEStatus)value;
    }

    private static string FormatCoordinate(double value, int degreeDigits, char hemisphere)
    {
        var degrees = (int)Math.Floor(value);
        var minutes = Math.Round((value - degrees) * 60.0, 2, MidpointRounding.AwayFromZero);
        if (minutes >= 60.0)
        {
            degrees++;
            minutes = 0;
        }

        var sb = new StringBuilder();
        sb.Append(degrees.ToString(new string('0', degreeDigits), CultureInfo.InvariantCulture));
        sb.Append(minutes.ToString("00.00", CultureInfo.InvariantCulture));
        sb.Append(hemisphere);
        return sb.ToString();
    }

    /// <summary>Blanks minute digits from the right: hundredths, tenths, units, tens.</summary>
    private static string ApplyAmbiguity(string coordinate, int degreeDigits, int ambiguity)
    {
        if (ambiguity == 0)
            return coordinate;
        var chars = coordinate.ToCharArray();
        int[] order = [degreeDigits + 4, degreeDigits + 3, degreeDigits + 1, degreeDigits];
        for (var i = 0; i < ambiguity; i++)
            chars[order[i]] = ' ';
        return new string(chars);
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
