using System.Text.RegularExpressions;

namespace OscarWatch.Core.Terminal;

/// <summary>Grid, report and QSO keyword detection in free text (GreenCube-style and APRS message bodies).</summary>
public static partial class TerminalText
{
    /// <summary>First 4- or 6-character Maidenhead token, normalised as <c>IO85</c> or <c>IO85ts</c>; <c>RR73</c> is not a grid.</summary>
    public static string? FindGrid(string? text)
    {
        foreach (var token in Tokens(text))
        {
            if (TryNormaliseGrid(token, out var grid))
                return grid;
        }

        return null;
    }

    public static bool TryNormaliseGrid(string? token, out string grid)
    {
        grid = "";
        if (string.IsNullOrEmpty(token) || !GridToken().IsMatch(token))
            return false;

        var upper = token.ToUpperInvariant();
        if (upper == "RR73")
            return false;

        grid = upper.Length == 6 ? upper[..4] + upper[4..].ToLowerInvariant() : upper;
        return true;
    }

    /// <summary>First RST-style report token (<c>59</c>, <c>599</c>, <c>R599</c> gives <c>599</c>).</summary>
    public static string? FindReport(string? text)
    {
        foreach (var token in Tokens(text))
        {
            var t = token.ToUpperInvariant();
            if (t.StartsWith('R') && t.Length > 1 && char.IsDigit(t[1]))
                t = t[1..];
            if (ReportToken().IsMatch(t))
                return t;
        }

        return null;
    }

    public static bool StartsWithCq(string? text)
    {
        var first = Tokens(text).FirstOrDefault();
        return first is not null && first.Equals("CQ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Roger tokens: <c>R73</c>, <c>RR73</c>, <c>RRR</c>, <c>ROGER</c>, or a leading <c>R</c> before a report or grid.</summary>
    public static bool ContainsRoger(string? text)
    {
        var tokens = Tokens(text).Select(t => t.ToUpperInvariant()).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t is "R73" or "RR73" or "RRR" or "ROGER" or "QSL")
                return true;
            if (t.StartsWith('R') && t.Length > 1 && char.IsDigit(t[1]) && ReportToken().IsMatch(t[1..]))
                return true;
            if (t == "R" && i + 1 < tokens.Count)
                return true;
        }

        return false;
    }

    /// <summary>Sign-off tokens: <c>73</c>, <c>R73</c>, <c>RR73</c>, <c>TU</c>, <c>LOGGED</c>.</summary>
    public static bool ContainsSignOff(string? text) =>
        Tokens(text).Any(t => t.ToUpperInvariant() is "73" or "R73" or "RR73" or "TU" or "TNX" or "LOGGED");

    /// <summary>Removes an SSID suffix and uppercases, for example <c>g0abc-7</c> gives <c>G0ABC</c>.</summary>
    public static string BaseCallsign(string? call)
    {
        if (string.IsNullOrWhiteSpace(call))
            return "";
        var c = call.Trim().ToUpperInvariant().TrimEnd('*');
        var dash = c.IndexOf('-');
        return dash > 0 ? c[..dash] : c;
    }

    public static bool SameStation(string? a, string? b)
    {
        var x = BaseCallsign(a);
        return x.Length > 0 && string.Equals(x, BaseCallsign(b), StringComparison.Ordinal);
    }

    /// <summary>Loose amateur callsign shape: letters and digits with at least one of each, 3 to 7 characters before any SSID.</summary>
    public static bool LooksLikeCallsign(string? call)
    {
        var c = BaseCallsign(call);
        if (c.Length is < 3 or > 7)
            return false;
        return c.Any(char.IsDigit) && c.Any(char.IsLetter) && c.All(char.IsLetterOrDigit);
    }

    private static IEnumerable<string> Tokens(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : TokenSplit().Split(text).Where(t => t.Length > 0);

    [GeneratedRegex("^[A-Ra-r]{2}[0-9]{2}([A-Xa-x]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex GridToken();

    [GeneratedRegex("^5[1-9][1-9]?$", RegexOptions.CultureInvariant)]
    private static partial Regex ReportToken();

    [GeneratedRegex("[^A-Za-z0-9/+-]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSplit();
}
