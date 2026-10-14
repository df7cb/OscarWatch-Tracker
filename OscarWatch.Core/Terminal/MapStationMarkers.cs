namespace OscarWatch.Core.Terminal;

/// <summary>A heard station drawn on the Terminal pass map.</summary>
/// <param name="IsPrecise">True for an APRS position, false for a grid square centre.</param>
/// <param name="IsFaded">Heard longer ago than the fade time; drawn with a dashed ring.</param>
public sealed record MapStationMarker(
    string Call,
    double Latitude,
    double Longitude,
    DateTime LastHeardUtc,
    bool IsPrecise,
    bool IsFaded,
    string ToolTipText);

/// <summary>Input for <see cref="MapStationMarkers.Build"/>: one heard station.</summary>
public sealed record MapStationSource(string Call, AprsPosition? Position, string? Grid, DateTime LastHeardUtc);

public static class MapStationMarkers
{
    /// <summary>
    /// Stations with a known location, newest first. Stations heard more than <paramref name="fade"/> ago are
    /// faded, and dropped after twice that. With <paramref name="passStartUtc"/>, only stations heard since then are kept.
    /// </summary>
    public static IReadOnlyList<MapStationMarker> Build(
        IEnumerable<MapStationSource> stations,
        DateTime nowUtc,
        TimeSpan fade,
        DateTime? passStartUtc,
        Func<MapStationSource, string>? toolTip = null)
    {
        var result = new List<MapStationMarker>();
        foreach (var s in stations.OrderByDescending(s => s.LastHeardUtc))
        {
            var age = nowUtc - s.LastHeardUtc;
            if (age > fade + fade)
                continue;
            if (passStartUtc is { } start && s.LastHeardUtc < start)
                continue;
            if (!TerminalStationGeo.TryLocate(s.Position, s.Grid, out var lat, out var lon))
                continue;

            result.Add(new MapStationMarker(
                s.Call,
                lat,
                lon,
                s.LastHeardUtc,
                s.Position is not null,
                age > fade,
                toolTip?.Invoke(s) ?? s.Call));
        }

        return result;
    }
}
