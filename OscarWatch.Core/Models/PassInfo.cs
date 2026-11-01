namespace OscarWatch.Core.Models;

public sealed class PassInfo
{
    public required string SatelliteName { get; set; }
    public required string NoradId { get; init; }
    /// <summary>
    /// Catalogue entry this pass was predicted from, so the timeline can read the shared palette index
    /// (<see cref="SatelliteCatalogEntry.EnabledSatelliteIndex"/>). Null for synthetic passes.
    /// </summary>
    public SatelliteCatalogEntry? CatalogEntry { get; init; }
    public DateTime AosUtc { get; init; }
    public DateTime LosUtc { get; init; }
    public double MaxElevationDeg { get; init; }
    public DateTime MaxElevationUtc { get; init; }
    public double AosAzimuthDeg { get; init; }
    public double LosAzimuthDeg { get; init; }

    public TimeSpan Duration => LosUtc - AosUtc;
}
