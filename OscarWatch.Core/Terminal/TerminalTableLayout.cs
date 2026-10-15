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

    /// <summary>Accent bar, Time, Type, From, To, Delay, Quality, Message.</summary>
    public static IReadOnlyList<double> DefaultLogColumns { get; } = [12, 76, 96, 104, 92, 56, 48, 0];

    public const int QualityColumnIndex = 6;

    /// <summary>Inserts the Quality column into log widths saved before it existed, keeping the other widths.</summary>
    public static List<double>? MigrateLogColumns(IReadOnlyList<double>? widths)
    {
        if (widths is null)
            return null;
        var list = widths.ToList();
        if (list.Count == DefaultLogColumns.Count - 1)
            list.Insert(QualityColumnIndex, DefaultLogColumns[QualityColumnIndex]);
        return list;
    }

    /// <summary>Call, Grid, El, State, UHM, Logged.</summary>
    public static IReadOnlyList<double> DefaultQsoColumns { get; } = [88, 60, 44, 0, 72, 60];

    /// <summary>Call, Grid, Since, CQ/TX, El, Distance, UHM, Marker.</summary>
    public static IReadOnlyList<double> DefaultHeardColumns { get; } = [88, 60, 52, 52, 44, 96, 72, 0];

    private const int DistanceColumnIndex = 5;

    /// <summary>Inserts the Distance column into heard widths saved before it existed, keeping the other widths.</summary>
    public static List<double>? MigrateHeardColumns(IReadOnlyList<double>? widths)
    {
        if (widths is null)
            return null;
        var list = widths.ToList();
        if (list.Count == DefaultHeardColumns.Count - 1)
            list.Insert(DistanceColumnIndex, DefaultHeardColumns[DistanceColumnIndex]);
        return list;
    }

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
    public static string ToColumnDefinitions(IReadOnlyList<double> widths, int? collapsedIndex = null) =>
        string.Join(",", widths.Select((w, i) => i == collapsedIndex ? "0"
            : w == 0 ? "*"
            : w.ToString("0", CultureInfo.InvariantCulture)));

    public static double ClampCallersWidth(double width) =>
        double.IsFinite(width) && width > 0 ? Math.Clamp(width, MinCallersWidth, MaxCallersWidth) : DefaultCallersWidth;

    public static double ClampQsoShare(double share) =>
        double.IsFinite(share) && share > 0 ? Math.Clamp(share, MinQsoShare, MaxQsoShare) : DefaultQsoShare;
}
