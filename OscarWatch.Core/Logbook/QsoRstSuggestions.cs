using System.Globalization;
using OscarWatch.Core.Ft4;

namespace OscarWatch.Core.Logbook;

/// <summary>RST choices for the logbook entry row, including FT4 and FT8 signal reports.</summary>
public static class QsoRstSuggestions
{
    public static IReadOnlyList<string> VoiceOptions { get; } =
        ["59", "599", "55", "559", "57", "579", "53", "539"];

    public static IReadOnlyList<string> SnrOptions { get; } = BuildSnrOptions();

    public static bool UsesSnr(string? mode, string? modeType) =>
        IsSnrMode(mode) || IsSnrMode(modeType);

    public static string ModeKey(string? mode, string? modeType) =>
        $"{NormalizeToken(mode)}|{NormalizeToken(modeType)}";

    /// <summary>Report to put in the boxes when the tracked mode changes. Null leaves the current text.</summary>
    public static string? DefaultReport(string? mode, string? modeType)
    {
        if (UsesSnr(mode, modeType))
            return "+00";

        if (NormalizeToken(mode).Length == 0)
            return null;

        return UsesToneReport(mode) || UsesToneReport(modeType) ? "599" : "59";
    }

    private static bool UsesToneReport(string? value)
    {
        var normalized = NormalizeToken(value);
        return normalized.StartsWith("CW", StringComparison.Ordinal)
            || normalized is "RTTY" or "PSK" or "PSK31" or "DATA-USB" or "DATA-LSB" or "DIGU" or "DIGL"
                or "PKTUSB" or "PKTLSB" or "USB-D" or "LSB-D";
    }

    /// <summary>
    /// Store FT4-style reports as +NN or -NN. Voice RST is left unchanged unless the text is already an SNR report.
    /// </summary>
    public static string NormalizeForLog(string? value, bool snrMode)
    {
        var text = Ft4MessageCodec.NormalizeSnrReport(value);
        if (text.Length == 0)
            return "";

        if (!snrMode && (text[0] is not ('+' or '-')))
            return text;

        return PadSnr(text);
    }

    private static string PadSnr(string text)
    {
        var sign = '\0';
        var body = text;
        if (body[0] is '+' or '-')
        {
            sign = body[0];
            body = body[1..];
        }

        if (body.Length is 0 or > 2 || !IsDigits(body))
            return text;

        if (!int.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var magnitude))
            return text;

        if (sign == '\0')
        {
            if (magnitude > 49)
                return text;

            sign = '+';
        }
        else if (sign == '+' && magnitude > 49)
        {
            return text;
        }
        else if (sign == '-' && magnitude > 50)
        {
            return text;
        }

        var signed = sign == '-' ? -magnitude : magnitude;
        return Ft4MessageCodec.FormatSnrReport(signed);
    }

    private static bool IsDigits(string value)
    {
        foreach (var ch in value)
        {
            if (!char.IsDigit(ch))
                return false;
        }

        return true;
    }

    private static bool IsSnrMode(string? value)
    {
        var normalized = NormalizeToken(value);
        return normalized is "FT4" or "FT8";
    }

    private static string NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

    private static string[] BuildSnrOptions()
    {
        var values = new string[100];
        var index = 0;
        for (var snr = 49; snr >= -50; snr--)
            values[index++] = Ft4MessageCodec.FormatSnrReport(snr);

        return values;
    }
}
