using OscarWatch.Core.Geo;
using OscarWatch.Core.Models;

namespace OscarWatch.Core.Terminal;

public enum TerminalPositionOrigin
{
    Gps,
    Station,
    Grid
}

/// <summary>Where Send position takes our location from, and the location itself.</summary>
public sealed record TerminalPositionFix(double Latitude, double Longitude, TerminalPositionOrigin Origin)
{
    public string Grid6
    {
        get
        {
            var grid = MaidenheadGrid.FromLatLon(
                Math.Clamp(Latitude, -89.9999, 89.9999), Math.Clamp(Longitude, -179.9999, 179.9999), 6);
            return grid[..4] + grid[4..].ToLowerInvariant();
        }
    }
}

public static class TerminalPositionSource
{
    /// <summary>A GPS fix older than this is ignored in favour of the station location.</summary>
    public static readonly TimeSpan MaxGpsFixAge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A recent GPS fix first, then the station latitude and longitude, then the centre of the station grid.
    /// Null when none of them is usable.
    /// </summary>
    public static TerminalPositionFix? Resolve(bool gpsEnabled, GpsConnectionStatus? gps, GroundStation? station, DateTime nowUtc)
    {
        if (gpsEnabled && gps is { HasFix: true, LatitudeDeg: { } glat, LongitudeDeg: { } glon }
            && gps.FixUtc is { } fixUtc && nowUtc - fixUtc <= MaxGpsFixAge && IsValid(glat, glon))
            return new TerminalPositionFix(glat, glon, TerminalPositionOrigin.Gps);

        if (station is null)
            return null;

        var lat = station.LatitudeDeg;
        var lon = station.LongitudeDeg;
        if (IsValid(lat, lon) && !(lat == 0 && lon == 0))
            return new TerminalPositionFix(lat, lon, TerminalPositionOrigin.Station);

        var grid = (station.GridSquare ?? "").Trim();
        if (grid.Length < 4)
            return null;
        try
        {
            var (glat2, glon2) = MaidenheadGrid.ToLatLonCenter(grid);
            return new TerminalPositionFix(glat2, glon2, TerminalPositionOrigin.Grid);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The APRS position payload for <paramref name="fix"/> using the precision, symbol and comment settings.
    /// Grid placeholders in the comment use the fix, cut to 4 characters unless the precision is exact.
    /// </summary>
    public static string BuildPayload(TerminalPositionFix fix, TerminalSettings settings, string myCall)
    {
        var myGrid = settings.PositionPrecision == TerminalPositionPrecision.Exact ? fix.Grid6 : fix.Grid6[..4];
        var lat = fix.Latitude;
        var lon = fix.Longitude;
        var ambiguity = 0;
        switch (settings.PositionPrecision)
        {
            case TerminalPositionPrecision.About2Km:
                ambiguity = 2;
                break;
            case TerminalPositionPrecision.About20Km:
                ambiguity = 3;
                break;
            case TerminalPositionPrecision.GridSquare:
                (lat, lon) = MaidenheadGrid.ToLatLonCenter(MaidenheadGrid.FromLatLon(
                    Math.Clamp(lat, -89.9999, 89.9999), Math.Clamp(lon, -179.9999, 179.9999), 4));
                break;
        }

        var symbol = TerminalSettings.NormalisePositionSymbol(settings.PositionSymbol);
        var comment = TerminalMessageCodec.ExpandMacro(settings.PositionComment ?? "", myCall, myGrid, null);
        return AprsPayloadCodec.BuildPosition(lat, lon, symbol[0], symbol[1], comment, ambiguity);
    }

    private static bool IsValid(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
}
