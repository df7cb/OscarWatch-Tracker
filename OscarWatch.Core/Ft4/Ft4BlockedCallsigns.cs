namespace OscarWatch.Core.Ft4;

/// <summary>
/// Callsigns that are not answered automatically while calling CQ.
/// Matching ignores case and a trailing /P or /R, so one entry covers the portable form.
/// </summary>
public static class Ft4BlockedCallsigns
{
    public const int MaxCount = 50;

    /// <summary>
    /// Callsign to store, or null when the text is not a callsign.
    /// A trailing /P or /R is removed. An unresolved hash (&lt;...&gt;) is rejected.
    /// </summary>
    public static string? Normalize(string? call)
    {
        var c = Ft4MessageCodec.NormalizeCall(call ?? "");
        if (c.Length == 0)
            return null;

        if (c.EndsWith("/P", StringComparison.Ordinal) || c.EndsWith("/R", StringComparison.Ordinal))
            c = c[..^2];

        if (c.Length is < 3 or > 11)
            return null;
        if (c is "CQ" or "DE" or "QRZ")
            return null;
        if (IsGrid(c))
            return null;

        foreach (var ch in c)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '/')
                return null;
        }

        return c;
    }

    /// <summary>True when <paramref name="call"/> matches any stored entry.</summary>
    public static bool Matches(IEnumerable<string>? blocked, string? call)
    {
        var key = Normalize(call);
        if (key is null || blocked is null)
            return false;

        foreach (var entry in blocked)
        {
            if (Normalize(entry) == key)
                return true;
        }

        return false;
    }

    /// <summary>Unique normalised calls, capped at <see cref="MaxCount"/>.</summary>
    public static List<string> Clean(IEnumerable<string>? blocked)
    {
        var result = new List<string>();
        if (blocked is null)
            return result;

        foreach (var entry in blocked)
        {
            if (result.Count >= MaxCount)
                break;
            var call = Normalize(entry);
            if (call is null || result.Contains(call))
                continue;
            result.Add(call);
        }

        return result;
    }

    private static bool IsGrid(string call)
    {
        if (call.Length is not (4 or 6))
            return false;
        if (!char.IsAsciiLetter(call[0]) || !char.IsAsciiLetter(call[1])
            || !char.IsAsciiDigit(call[2]) || !char.IsAsciiDigit(call[3]))
            return false;
        return call.Length == 4
            || (char.IsAsciiLetter(call[4]) && char.IsAsciiLetter(call[5]));
    }
}
