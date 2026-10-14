namespace OscarWatch.Core.Terminal;

/// <summary>Resolved Terminal settings for one satellite.</summary>
public sealed record TerminalSatelliteProfile(
    string NoradId,
    string Name,
    TerminalMessageFormat Format,
    string Path,
    string UhmName,
    string ModemHint,
    bool IsBuiltIn);

/// <summary>Built-in per-satellite defaults plus detection of packet digipeater modes in the satellite database.</summary>
public static class TerminalSatelliteProfiles
{
    public const string IssNoradId = "25544";
    public const string GreenCubeNoradId = "53106";

    public const string ModemHintAfsk1200 = "1200 AFSK";
    public const string ModemHintBpsk1200 = "BPSK 1200 (UZ7HO only)";
    public const string ModemHintG3ruh9600 = "9600 G3RUH";

    public static IReadOnlyList<TerminalSatelliteProfile> BuiltIn { get; } =
    [
        new(IssNoradId, "ISS", TerminalMessageFormat.Aprs, "ARISS", "ISS", ModemHintAfsk1200, true),
        new(GreenCubeNoradId, "IO-117", TerminalMessageFormat.PlainText, "", "IO-117", ModemHintBpsk1200, true),
    ];

    /// <summary>True for database mode types such as <c>Packet</c>, <c>APRS</c> or <c>Digipeater (9k6 FSK)</c>.</summary>
    public static bool IsDigipeaterModeType(string? modeType)
    {
        if (string.IsNullOrWhiteSpace(modeType))
            return false;
        return modeType.Contains("Packet", StringComparison.OrdinalIgnoreCase)
               || modeType.Contains("APRS", StringComparison.OrdinalIgnoreCase)
               || modeType.Contains("Digipeater", StringComparison.OrdinalIgnoreCase);
    }

    public static string ModemHintForModeType(string? modeType)
    {
        var t = modeType ?? "";
        if (t.Contains("BPSK", StringComparison.OrdinalIgnoreCase))
            return ModemHintBpsk1200;
        if (t.Contains("9k6", StringComparison.OrdinalIgnoreCase) || t.Contains("9600", StringComparison.OrdinalIgnoreCase))
            return ModemHintG3ruh9600;
        return ModemHintAfsk1200;
    }

    /// <summary>
    /// Operator override first, then the built-in default, then a generic APRS profile when any of
    /// <paramref name="modeTypes"/> is a packet digipeater mode. Null when the satellite has no digipeater.
    /// </summary>
    public static TerminalSatelliteProfile? Resolve(
        string? noradId,
        string? satelliteName,
        IEnumerable<string>? modeTypes,
        IReadOnlyList<TerminalSatelliteProfileSettings>? overrides)
    {
        var norad = (noradId ?? "").Trim();
        var name = (satelliteName ?? "").Trim();

        var custom = overrides?.FirstOrDefault(o =>
            norad.Length > 0 && string.Equals(o.NoradId?.Trim(), norad, StringComparison.Ordinal));
        if (custom is not null)
        {
            var builtIn = FindBuiltIn(norad);
            return new TerminalSatelliteProfile(
                norad,
                FirstNonEmpty(custom.Name, builtIn?.Name, name),
                custom.Format,
                (custom.Path ?? "").Trim().ToUpperInvariant(),
                FirstNonEmpty(custom.UhmName, builtIn?.UhmName, name),
                FirstNonEmpty(custom.ModemHint, builtIn?.ModemHint, ModemHintAfsk1200),
                false);
        }

        var known = FindBuiltIn(norad);
        if (known is not null)
            return known;

        var digipeaterMode = modeTypes?.FirstOrDefault(IsDigipeaterModeType);
        if (digipeaterMode is null || norad.Length == 0)
            return null;

        return new TerminalSatelliteProfile(
            norad,
            name,
            TerminalMessageFormat.Aprs,
            "",
            name,
            ModemHintForModeType(digipeaterMode),
            false);
    }

    public static TerminalSatelliteProfile? FindBuiltIn(string? noradId) =>
        BuiltIn.FirstOrDefault(p => string.Equals(p.NoradId, noradId?.Trim(), StringComparison.Ordinal));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
