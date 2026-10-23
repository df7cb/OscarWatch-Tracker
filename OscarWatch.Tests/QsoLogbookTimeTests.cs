using System.Globalization;
using OscarWatch.Core.Logbook;

namespace OscarWatch.Tests;

public sealed class QsoLogbookTimeTests
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    [Fact]
    public void FormatQsoUtc_always_uses_utc_not_local()
    {
        var utc = new DateTime(2020, 2, 12, 17, 10, 0, DateTimeKind.Utc);
        var formatted = QsoLogbookTime.FormatQsoUtc(utc, use24HourClock: true, EnGb);
        Assert.Equal("12/02/2020 17:10", formatted);
    }

    [Fact]
    public void FormatQsoUtc_uses_regional_date_order()
    {
        var utc = new DateTime(2020, 2, 12, 17, 10, 0, DateTimeKind.Utc);
        var culture = CultureInfo.GetCultureInfo("en-US");
        var formatted = QsoLogbookTime.FormatQsoUtc(utc, use24HourClock: true, culture);
        Assert.Equal("2/12/2020 17:10", formatted);
    }

    [Fact]
    public void NormalizeToUtc_converts_local_to_utc()
    {
        var local = new DateTime(2020, 2, 12, 17, 10, 0, DateTimeKind.Local);
        var normalized = QsoLogbookTime.NormalizeToUtc(local);
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(local.ToUniversalTime(), normalized);
    }

    [Fact]
    public void FormatLiveUtcClock_uses_regional_format_and_utc_suffix()
    {
        var utc = new DateTime(2020, 2, 12, 17, 10, 5, DateTimeKind.Utc);
        Assert.Equal("12/02/2020 17:10:05 UTC", QsoLogbookTime.FormatLiveUtcClock(utc, use24HourClock: true, EnGb));
    }

    [Fact]
    public void FormatLiveUtcClock_respects_12_hour_setting()
    {
        var utc = new DateTime(2020, 2, 12, 17, 10, 5, DateTimeKind.Utc);
        var formatted = QsoLogbookTime.FormatLiveUtcClock(utc, use24HourClock: false, EnGb);
        Assert.Contains("5:10", formatted);
        Assert.Contains("PM", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("UTC", formatted);
    }

    [Fact]
    public void TryCombineUtcCalendar_keeps_the_picker_date_when_the_offset_is_not_utc()
    {
        var date = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.FromHours(1));
        Assert.True(QsoLogbookTime.TryCombineUtcCalendar(date, new TimeSpan(23, 30, 5), out var utc));
        Assert.Equal(new DateTime(2026, 10, 23, 23, 30, 5, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void TryCombineUtcCalendar_rejects_a_time_outside_one_day()
    {
        var date = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        Assert.False(QsoLogbookTime.TryCombineUtcCalendar(date, TimeSpan.FromHours(24), out _));
        Assert.False(QsoLogbookTime.TryCombineUtcCalendar(date, TimeSpan.FromMinutes(-1), out _));
    }
}
