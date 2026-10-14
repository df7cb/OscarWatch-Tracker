using System.Globalization;
using OscarWatch.Core.Geo;

namespace OscarWatch.Core.Terminal;

/// <summary>Where a heard station is, and how far away, from its APRS position or grid.</summary>
public static class TerminalStationGeo
{
    public const double EarthRadiusKm = 6371.0;

    /// <summary>The precise APRS position when known, otherwise the centre of <paramref name="grid"/>.</summary>
    public static bool TryLocate(AprsPosition? position, string? grid, out double lat, out double lon)
    {
        if (position is not null)
        {
            lat = position.Latitude;
            lon = position.Longitude;
            return true;
        }

        lat = 0;
        lon = 0;
        if (string.IsNullOrWhiteSpace(grid) || grid.Trim().Length < 4)
            return false;
        try
        {
            (lat, lon) = MaidenheadGrid.ToLatLonCenter(grid.Trim());
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or FormatException)
        {
            return false;
        }
    }

    public static (double DistanceKm, double BearingDeg) DistanceBearing(double fromLat, double fromLon, double toLat, double toLon)
    {
        var deg = SphericalGeo.AngularDistanceDeg(fromLat, fromLon, toLat, toLon);
        var km = deg * Math.PI / 180.0 * EarthRadiusKm;
        var bearing = SphericalGeo.InitialBearingDeg(fromLat, fromLon, toLat, toLon);
        return (km, bearing);
    }

    /// <summary>For example <c>1234 km 045°</c>.</summary>
    public static string FormatDistanceBearing(double distanceKm, double bearingDeg) =>
        string.Create(CultureInfo.InvariantCulture, $"{distanceKm:0} km {Math.Round(bearingDeg) % 360:000}°");
}
