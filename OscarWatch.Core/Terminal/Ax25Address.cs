using System.Globalization;

namespace OscarWatch.Core.Terminal;

/// <summary>One AX.25 address field: callsign, SSID and the H (has-been-repeated) or C bit.</summary>
public readonly record struct Ax25Address(string Callsign, int Ssid, bool HasBeenRepeated = false)
{
    public const int EncodedLength = 7;

    /// <summary>Callsign with SSID suffix when non-zero, for example <c>G0ABC-7</c>.</summary>
    public string Text => Ssid == 0 ? Callsign : $"{Callsign}-{Ssid.ToString(CultureInfo.InvariantCulture)}";

    public override string ToString() => Text;

    /// <summary>Parses <c>CALL</c>, <c>CALL-SSID</c> or <c>CALL*</c> (repeated marker).</summary>
    public static bool TryParse(string? text, out Ax25Address address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim().ToUpperInvariant();
        var repeated = false;
        if (t.EndsWith('*'))
        {
            repeated = true;
            t = t[..^1];
        }

        var ssid = 0;
        var dash = t.IndexOf('-');
        if (dash >= 0)
        {
            if (!int.TryParse(t[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out ssid)
                || ssid is < 0 or > 15)
                return false;
            t = t[..dash];
        }

        if (t.Length is < 1 or > 6)
            return false;

        foreach (var c in t)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
                return false;
        }

        address = new Ax25Address(t, ssid, repeated);
        return true;
    }

    public static Ax25Address Parse(string text) =>
        TryParse(text, out var address)
            ? address
            : throw new FormatException($"Invalid AX.25 address '{text}'.");

    /// <summary>Writes the 7-byte shifted form. <paramref name="highBit"/> is C (destination/source) or H (digipeater).</summary>
    internal void Encode(Span<byte> destination, bool highBit, bool last)
    {
        for (var i = 0; i < 6; i++)
        {
            var c = i < Callsign.Length ? Callsign[i] : ' ';
            destination[i] = (byte)(c << 1);
        }

        var ssidByte = 0x60 | ((Ssid & 0x0F) << 1);
        if (highBit)
            ssidByte |= 0x80;
        if (last)
            ssidByte |= 0x01;
        destination[6] = (byte)ssidByte;
    }

    internal static Ax25Address Decode(ReadOnlySpan<byte> source, out bool highBit, out bool last)
    {
        Span<char> chars = stackalloc char[6];
        var len = 0;
        for (var i = 0; i < 6; i++)
        {
            var c = (char)(source[i] >> 1);
            if (c != ' ')
                chars[len++] = c;
        }

        var ssidByte = source[6];
        highBit = (ssidByte & 0x80) != 0;
        last = (ssidByte & 0x01) != 0;
        var ssid = (ssidByte >> 1) & 0x0F;
        return new Ax25Address(new string(chars[..len]), ssid);
    }
}
