using OscarWatch.Core.Models;
using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class TerminalPositionTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static GroundStation Station(double lat = 55.8667, double lon = -4.2583, string grid = "IO75uu") =>
        new() { LatitudeDeg = lat, LongitudeDeg = lon, GridSquare = grid };

    private static GpsConnectionStatus Gps(DateTime fixUtc, double lat = 51.0, double lon = 0.5) =>
        new(true, true, lat, lon, 10, 8, fixUtc, null);

    [Fact]
    public void Fresh_gps_fix_wins()
    {
        var fix = TerminalPositionSource.Resolve(true, Gps(Now.AddSeconds(-30)), Station(), Now)!;
        Assert.Equal(TerminalPositionOrigin.Gps, fix.Origin);
        Assert.Equal(51.0, fix.Latitude);
    }

    [Fact]
    public void Stale_or_disabled_gps_falls_back_to_station()
    {
        var stale = TerminalPositionSource.Resolve(true, Gps(Now - TerminalPositionSource.MaxGpsFixAge - TimeSpan.FromSeconds(1)), Station(), Now)!;
        Assert.Equal(TerminalPositionOrigin.Station, stale.Origin);

        var disabled = TerminalPositionSource.Resolve(false, Gps(Now), Station(), Now)!;
        Assert.Equal(TerminalPositionOrigin.Station, disabled.Origin);
        Assert.Equal(55.8667, disabled.Latitude);
    }

    [Fact]
    public void Unset_station_coordinates_fall_back_to_grid_centre()
    {
        var zero = TerminalPositionSource.Resolve(false, null, Station(0, 0, "IO85"), Now)!;
        Assert.Equal(TerminalPositionOrigin.Grid, zero.Origin);
        Assert.Equal(55.5, zero.Latitude, 6);
        Assert.Equal(-3.0, zero.Longitude, 6);

        var invalid = TerminalPositionSource.Resolve(false, null, Station(double.NaN, 200, "IO85"), Now)!;
        Assert.Equal(TerminalPositionOrigin.Grid, invalid.Origin);
    }

    [Fact]
    public void No_usable_location_is_null()
    {
        Assert.Null(TerminalPositionSource.Resolve(false, null, Station(0, 0, ""), Now));
        Assert.Null(TerminalPositionSource.Resolve(false, null, null, Now));
    }

    [Theory]
    [InlineData(TerminalPositionPrecision.Exact, "=5552.00N/00415.50W[IO75uu")]
    [InlineData(TerminalPositionPrecision.About2Km, "=5552.  N/00415.  W[IO75")]
    [InlineData(TerminalPositionPrecision.About20Km, "=555 .  N/0041 .  W[IO75")]
    [InlineData(TerminalPositionPrecision.GridSquare, "=5530.00N/00500.00W[IO75")]
    public void Payload_follows_precision(TerminalPositionPrecision precision, string expected)
    {
        var settings = new TerminalSettings
        {
            PositionPrecision = precision,
            PositionSymbol = "/[",
            PositionComment = "{MYGRID4}"
        };
        if (precision == TerminalPositionPrecision.Exact)
            settings.PositionComment = "{MYGRID}";

        var fix = new TerminalPositionFix(55.8667, -4.2583, TerminalPositionOrigin.Station);
        Assert.Equal(expected, TerminalPositionSource.BuildPayload(fix, settings, "MM9SQL"));
    }

    [Fact]
    public void Payload_uses_default_symbol_when_setting_is_invalid()
    {
        var settings = new TerminalSettings { PositionPrecision = TerminalPositionPrecision.Exact, PositionSymbol = "x", PositionComment = "{MYCALL}" };
        var fix = new TerminalPositionFix(55.8667, -4.2583, TerminalPositionOrigin.Gps);
        Assert.Equal("=5552.00N/00415.50W[MM9SQL", TerminalPositionSource.BuildPayload(fix, settings, "MM9SQL"));
    }

    [Fact]
    public void Grid_source_is_hidden_without_gps()
    {
        Assert.Null(TerminalPositionSource.GridSource(null, null, Now));
        Assert.Null(TerminalPositionSource.GridSource(new GpsSettings { Enabled = false, AutoUpdateStation = true }, Gps(Now), Now));
    }

    [Fact]
    public void Grid_source_follows_gps_only_when_applied_and_fresh()
    {
        var applied = new GpsSettings { Enabled = true, AutoUpdateStation = true };
        Assert.Equal(TerminalGridSource.Gps, TerminalPositionSource.GridSource(applied, Gps(Now.AddSeconds(-10)), Now));
        Assert.Equal(TerminalGridSource.GpsNoFix, TerminalPositionSource.GridSource(applied, Gps(Now.AddMinutes(-5)), Now));
        Assert.Equal(TerminalGridSource.GpsNoFix, TerminalPositionSource.GridSource(applied, null, Now));

        var notApplied = new GpsSettings { Enabled = true, AutoUpdateStation = false };
        Assert.Equal(TerminalGridSource.Saved, TerminalPositionSource.GridSource(notApplied, Gps(Now), Now));
    }

    [Theory]
    [InlineData("IO75uu", "IO75tv", false)]
    [InlineData("IO75uu", "io75UU", false)]
    [InlineData("IO75xx", "IO85aa", true)]
    [InlineData(null, "IO85aa", false)]
    [InlineData("IO85aa", "", false)]
    public void Grid_change_counts_only_four_character_square(string? previous, string? current, bool expected)
    {
        Assert.Equal(expected, TerminalPositionSource.Grid4Changed(previous, current));
    }

    private static MapStationSource Src(string call, int minutesAgo, string? grid = "IO85", AprsPosition? position = null) =>
        new(call, position, grid, Now.AddMinutes(-minutesAgo));

    [Fact]
    public void Markers_fade_then_drop_and_sort_newest_first()
    {
        var markers = MapStationMarkers.Build(
            [Src("OLD", 31), Src("FADED", 20), Src("FRESH", 2)],
            Now, TimeSpan.FromMinutes(15), null);

        Assert.Equal(new[] { "FRESH", "FADED" }, markers.Select(m => m.Call));
        Assert.False(markers[0].IsFaded);
        Assert.True(markers[1].IsFaded);
    }

    [Fact]
    public void Markers_pass_filter_and_location()
    {
        var position = new AprsPosition(10, 20, '/', '-', null, null, null);
        var markers = MapStationMarkers.Build(
            [Src("BEFORE", 10), Src("APRS", 2, null, position), Src("NOWHERE", 1, null)],
            Now, TimeSpan.FromMinutes(15), Now.AddMinutes(-5), s => "tip " + s.Call);

        var m = Assert.Single(markers);
        Assert.Equal("APRS", m.Call);
        Assert.True(m.IsPrecise);
        Assert.Equal(10, m.Latitude);
        Assert.Equal("tip APRS", m.ToolTipText);
    }

    [Fact]
    public void Distance_and_bearing_are_formatted()
    {
        var (km, bearing) = TerminalStationGeo.DistanceBearing(55.8667, -4.2583, 51.5074, -0.1278);
        Assert.InRange(km, 530, 580);
        Assert.InRange(bearing, 140, 160);
        Assert.Equal("555 km 045°", TerminalStationGeo.FormatDistanceBearing(555.2, 44.6));
        Assert.Equal("10 km 000°", TerminalStationGeo.FormatDistanceBearing(10, 359.7));
    }

    [Fact]
    public void Old_heard_layout_gains_distance_column()
    {
        List<double> old = [88, 60, 52, 52, 44, 72, 0];
        var migrated = TerminalTableLayout.MigrateHeardColumns(old)!;
        Assert.Equal(TerminalTableLayout.DefaultHeardColumns.Count, migrated.Count);
        Assert.Equal(TerminalTableLayout.DefaultHeardColumns[5], migrated[5]);
        Assert.Equal(72, migrated[6]);
        Assert.Null(TerminalTableLayout.MigrateHeardColumns(null));
    }

    [Fact]
    public void Old_log_layout_gains_quality_column_and_keeps_widths()
    {
        List<double> old = [30, 80, 96, 110, 92, 60, 0];
        var migrated = TerminalTableLayout.Normalise(
            TerminalTableLayout.MigrateLogColumns(old), TerminalTableLayout.DefaultLogColumns);
        Assert.Equal([30, 80, 96, 110, 92, 60, 48, 0], migrated);
    }

    [Fact]
    public void Collapsed_quality_column_has_zero_width()
    {
        Assert.Equal("12,76,96,104,92,56,0,*", TerminalTableLayout.ToColumnDefinitions(
            TerminalTableLayout.DefaultLogColumns, TerminalTableLayout.QualityColumnIndex));
    }
}
