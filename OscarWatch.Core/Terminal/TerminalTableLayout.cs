using System.Globalization;

namespace OscarWatch.Core.Terminal;

/// <summary>Column widths for the Terminal tables. A width of 0 marks the column that fills the remaining space.</summary>
public static class TerminalTableLayout
{
    public const double MinColumnWidth = 24;
    public const double MaxColumnWidth = 600;

    public const double DefaultCallersWidth = 460;
    public const double MinCallersWidth = 240;
    public const double MaxCallersWidth = 1600;

    public const double DefaultQsoShare = 1.0 / 3.0;
    public const double MinQsoShare = 0.1;
    public const double MaxQsoShare = 0.9;

    /// <summary>Accent bar, Time, Type, From, To, Delay, Message.</summary>
    public static IReadOnlyList<double> DefaultLogColumns { get; } = [12, 76, 96, 104, 92, 56, 0];

    /// <summary>Call, Grid, El, State, UHM, Logged.</summary>
    public static IReadOnlyList<double> DefaultQsoColumns { get; } = [88, 60, 44, 0, 72, 60];

    /// <summary>Call, Grid, Since, CQ/TX, El, UHM, Marker.</summary>
    public static IReadOnlyList<double> DefaultHeardColumns { get; } = [88, 60, 52, 52, 44, 72, 0];

    /// <summary>
    /// Returns a usable copy of <paramref name="widths"/>: the defaults when the shape does not match
    /// (missing, older layout, or the fill column moved), otherwise the saved widths clamped to sensible limits.
    /// </summary>
    public static List<double> Normalise(IReadOnlyList<double>? widths, IReadOnlyList<double> defaults)
    {
        if (widths is null || widths.Count != defaults.Count)
            return [.. defaults];

        var result = new List<double>(defaults.Count);
        for (var i = 0; i < defaults.Count; i++)
        {
            var fill = defaults[i] == 0;
            if (fill != (widths[i] == 0) || !double.IsFinite(widths[i]))
                return [.. defaults];
            result.Add(fill ? 0 : Math.Round(Math.Clamp(widths[i], MinColumnWidth, MaxColumnWidth)));
        }

        return result;
    }

    /// <summary>Grid column definitions text, for example <c>88,60,44,*,72,60</c>.</summary>
    public static string ToColumnDefinitions(IReadOnlyList<double> widths) =>
        string.Join(",", widths.Select(w => w == 0 ? "*" : w.ToString("0", CultureInfo.InvariantCulture)));

    public static double ClampCallersWidth(double width) =>
        double.IsFinite(width) && width > 0 ? Math.Clamp(width, MinCallersWidth, MaxCallersWidth) : DefaultCallersWidth;

    public static double ClampQsoShare(double share) =>
        double.IsFinite(share) && share > 0 ? Math.Clamp(share, MinQsoShare, MaxQsoShare) : DefaultQsoShare;
}
