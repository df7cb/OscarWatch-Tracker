using OscarWatch.Core.Models;
using OscarWatch.Core.Radio;

namespace OscarWatch.Core.Terminal;

/// <summary>How the satellite's uplink and downlink relate, which decides whether the radio can receive while transmitting.</summary>
public enum TerminalLinkType
{
    /// <summary>No frequencies known (or no satellite).</summary>
    Unknown,

    /// <summary>One frequency up and down, for example the ISS on 145.825 MHz.</summary>
    Simplex,

    /// <summary>Different frequencies in the same band, so the radio still cannot listen while it transmits.</summary>
    SameBand,

    /// <summary>Uplink and downlink in different bands.</summary>
    Crossband
}

/// <summary>Resolved Terminal settings for one satellite.</summary>
public sealed record TerminalSatelliteProfile(
    string NoradId,
    string Name,
    TerminalMessageFormat Format,
    string Path,
    string UhmName,
    string ModemHint,
    bool IsBuiltIn,
    TerminalLinkType LinkType = TerminalLinkType.Unknown);

/// <summary>Built-in per-satellite defaults plus detection of packet digipeater modes in the satellite database.</summary>
public static class TerminalSatelliteProfiles
{
    public const string IssNoradId = "25544";
    public const string GreenCubeNoradId = "53106";

    public const string ModemHintAfsk1200 = "1200 AFSK";
    public const string ModemHintBpsk1200 = "BPSK 1200 (UZ7HO only)";
    public const string ModemHintGmsk9600 = "9600 GMSK (external modem)";
    public const string ModemHintG3ruh9600 = "9600 G3RUH";

    public static IReadOnlyList<TerminalSatelliteProfile> BuiltIn { get; } =
    [
        new(IssNoradId, "ISS", TerminalMessageFormat.Aprs, "ARISS", "ISS", ModemHintAfsk1200, true, TerminalLinkType.Simplex),
        new(GreenCubeNoradId, "IO-117", TerminalMessageFormat.PlainText, "", "IO-117", ModemHintBpsk1200, true, TerminalLinkType.Simplex),
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
        if (t.Contains("GMSK", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Skylink", StringComparison.OrdinalIgnoreCase))
            return ModemHintGmsk9600;
        if (IsG3ruh9600ModeType(t))
            return ModemHintG3ruh9600;
        return ModemHintAfsk1200;
    }

    /// <summary>True for AX.25 packet mode types that use 9600 baud G3RUH FSK (not GMSK, Skylink, or BPSK).</summary>
    public static bool IsG3ruh9600ModeType(string? modeType)
    {
        if (string.IsNullOrWhiteSpace(modeType))
            return false;
        var t = modeType;
        if (t.Contains("BPSK", StringComparison.OrdinalIgnoreCase)
            || t.Contains("GMSK", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Skylink", StringComparison.OrdinalIgnoreCase)
            || t.Contains("SSDV", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Telemetry", StringComparison.OrdinalIgnoreCase))
            return false;
        if (t.Contains("G3RUH", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!t.Contains("9k6", StringComparison.OrdinalIgnoreCase) && !t.Contains("9600", StringComparison.OrdinalIgnoreCase))
            return false;
        return t.Contains("Packet", StringComparison.OrdinalIgnoreCase)
               || t.Contains("Digipeater", StringComparison.OrdinalIgnoreCase)
               || t.Contains("APRS", StringComparison.OrdinalIgnoreCase)
               || t.Contains("FSK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a profile hint selects 9600 G3RUH on the built-in modem.</summary>
    public static bool ModemHintIsG3ruh9600(string? modemHint)
    {
        var hint = modemHint ?? "";
        return hint.Contains("G3RUH", StringComparison.OrdinalIgnoreCase)
               || hint.Contains("9600 G3RUH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when a profile modem hint implies 9600 baud airtime (G3RUH, GMSK, or other 9k6 modes).
    /// Independent of whether the built-in modem supports the mode.
    /// </summary>
    public static bool ModemHintUses9600Baud(string? modemHint)
    {
        var hint = modemHint ?? "";
        if (hint.Contains("1200", StringComparison.Ordinal))
            return false;
        if (ModemHintIsG3ruh9600(hint))
            return true;
        if (hint.Contains("9600", StringComparison.Ordinal)
            || hint.Contains("9k6", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// True when the built-in modem can work a satellite with this hint: 1200 AFSK, 9600 G3RUH, or an
    /// unknown hint that does not name an unsupported mode (BPSK, GMSK, Skylink, and similar).
    /// </summary>
    public static bool IsSupportedByBuiltIn(string? modemHint)
    {
        var hint = modemHint ?? "";
        if (hint.Contains("BPSK", StringComparison.OrdinalIgnoreCase))
            return false;
        if (ModemHintIsG3ruh9600(hint))
            return true;
        if (hint.Contains("GMSK", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("Skylink", StringComparison.OrdinalIgnoreCase))
            return false;
        if (hint.Contains("9600", StringComparison.Ordinal)
            || hint.Contains("9k6", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>Link type for one uplink and downlink pair.</summary>
    public static TerminalLinkType ClassifyLink(double uplinkKHz, double downlinkKHz)
    {
        if (uplinkKHz <= 0 || downlinkKHz <= 0)
            return TerminalLinkType.Unknown;
        if (RigSatModeHelper.IsSameBandSimplex(downlinkKHz, uplinkKHz))
            return TerminalLinkType.Simplex;

        // 70 cm and 23 cm share a band region, so also require the frequencies to be close.
        var sameRegion = RigSatModeHelper.GetSatBandRegion(uplinkKHz) == RigSatModeHelper.GetSatBandRegion(downlinkKHz);
        var close = Math.Abs(uplinkKHz - downlinkKHz) / Math.Max(uplinkKHz, downlinkKHz) < 0.2;
        return sameRegion && close ? TerminalLinkType.SameBand : TerminalLinkType.Crossband;
    }

    /// <summary>Link type of the satellite's packet digipeater mode, or of its first mode with both frequencies.</summary>
    public static TerminalLinkType ClassifyLink(IEnumerable<SatelliteTransponderMode>? modes)
    {
        var withFrequencies = (modes ?? []).Where(m => m.UplinkKHz > 0 && m.DownlinkKHz > 0).ToList();
        var mode = withFrequencies.FirstOrDefault(m => IsDigipeaterModeType(m.Type)) ?? withFrequencies.FirstOrDefault();
        return mode is null ? TerminalLinkType.Unknown : ClassifyLink(mode.UplinkKHz, mode.DownlinkKHz);
    }

    /// <summary>
    /// Operator override first, then the built-in default, then a generic APRS profile when any of
    /// <paramref name="modeTypes"/> is a packet digipeater mode. Null when the satellite has no digipeater.
    /// <paramref name="linkType"/> comes from the satellite's frequencies and is used unless an override forces one.
    /// </summary>
    public static TerminalSatelliteProfile? Resolve(
        string? noradId,
        string? satelliteName,
        IEnumerable<string>? modeTypes,
        IReadOnlyList<TerminalSatelliteProfileSettings>? overrides,
        TerminalLinkType linkType = TerminalLinkType.Unknown)
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
                false,
                custom.LinkType ?? Known(linkType, builtIn));
        }

        var known = FindBuiltIn(norad);
        if (known is not null)
            return known with { LinkType = Known(linkType, known) };

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
            false,
            linkType);
    }

    public static TerminalSatelliteProfile? FindBuiltIn(string? noradId) =>
        BuiltIn.FirstOrDefault(p => string.Equals(p.NoradId, noradId?.Trim(), StringComparison.Ordinal));

    private static TerminalLinkType Known(TerminalLinkType fromFrequencies, TerminalSatelliteProfile? builtIn) =>
        fromFrequencies != TerminalLinkType.Unknown ? fromFrequencies : builtIn?.LinkType ?? TerminalLinkType.Unknown;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
