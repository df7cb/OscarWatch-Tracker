using System.Globalization;
using OscarWatch.Core.Display;

namespace OscarWatch.Core.Logbook;

public static class QsoLogbookTime
{
    public static DateTime NormalizeToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static string FormatQsoUtc(DateTime utc, bool use24HourClock, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var instant = NormalizeToUtc(utc);
        var clockFormat = PassDisplayFormat.FromSettings(use24HourClock);
        var datePattern = culture.DateTimeFormat.ShortDatePattern;
        var timePattern = PassDisplayFormat.GetTimePattern(clockFormat, culture: culture);
        return instant.ToString($"{datePattern} {timePattern}", culture);
    }

    /// <summary>
    /// Builds a UTC instant from the calendar date and clock time shown in the pickers.
    /// Uses the date picker's own year, month, and day, so a local offset on
    /// <paramref name="date"/> does not move the QSO onto the previous UTC date.
    /// </summary>
    public static bool TryCombineUtcCalendar(DateTimeOffset date, TimeSpan time, out DateTime utc)
    {
        utc = default;
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            return false;

        utc = new DateTime(date.Year, date.Month, date.Day, time.Hours, time.Minutes, time.Seconds, DateTimeKind.Utc);
        return true;
    }

    public static string FormatLiveUtcClock(DateTime utc, bool use24HourClock, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var instant = NormalizeToUtc(utc);
        var clockFormat = PassDisplayFormat.FromSettings(use24HourClock);
        var datePattern = culture.DateTimeFormat.ShortDatePattern;
        var timePattern = PassDisplayFormat.GetTimePattern(clockFormat, includeSeconds: true, culture);
        return $"{instant.ToString($"{datePattern} {timePattern}", culture)} UTC";
    }
}
