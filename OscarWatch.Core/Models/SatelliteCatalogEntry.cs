namespace OscarWatch.Core.Models;

public sealed class SatelliteCatalogEntry
{
    public required string Name { get; init; }
    public required string NoradId { get; init; }
    public required string Line1 { get; init; }
    public required string Line2 { get; init; }
    public DateTime? EpochUtc { get; init; }

    /// <summary>
    /// Position of this satellite in the enabled-satellite list, assigned when that list is loaded.
    /// Views map it to a palette colour so one satellite keeps the same colour in the map, sky plot
    /// and pass timeline. Raw (not wrapped); the UI applies the palette length modulo.
    /// </summary>
    public int EnabledSatelliteIndex { get; set; }

    public bool IsStale(TimeSpan maxAge) =>
        EpochUtc.HasValue && DateTime.UtcNow - EpochUtc.Value > maxAge;
}
