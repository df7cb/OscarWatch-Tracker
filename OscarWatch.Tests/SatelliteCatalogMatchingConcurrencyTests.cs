using OscarWatch.Core.Models;
using OscarWatch.Core.Services;

namespace OscarWatch.Tests;

/// <summary>
/// Stresses parenthesis-index caching under concurrent callers (UI + live tracking worker),
/// mirroring <see cref="TleService.GetEnabledSatellites"/> allocating a new name set per call.
/// </summary>
public sealed class SatelliteCatalogMatchingConcurrencyTests : IDisposable
{
    private static readonly string[] SampleEnabledNames =
    [
        "RADFXSAT (FOX-1B)",
        "SAUDISAT 1C (SO-50)",
        "AO-91 (Fox-1B RadFxSat)",
        "ISS (ZARYA)",
        "QO-100",
    ];

    private static readonly SatelliteCatalogEntry[] SampleCatalog =
    [
        new()
        {
            Name = "FOX-1B",
            NoradId = "43017",
            Line1 = "1 43017U 17073E   24001.00000000  .00000000  00000-0  00000-0 0  0000",
            Line2 = "2 43017  00.0000 000.0000 0000000 000.0000 000.0000 15.00000000 00000",
        },
        new()
        {
            Name = "SO-50",
            NoradId = "27607",
            Line1 = "1 27607U 03031C   24001.00000000  .00000000  00000-0  00000-0 0  0000",
            Line2 = "2 27607  00.0000 000.0000 0000000 000.0000 000.0000 15.00000000 00000",
        },
        new()
        {
            Name = "ISS (ZARYA)",
            NoradId = "25544",
            Line1 = "1 25544U 98067A   24001.00000000  .00000000  00000-0  00000-0 0  0000",
            Line2 = "2 25544  00.0000 000.0000 0000000 000.0000 000.0000 15.00000000 00000",
        },
    ];

    public void Dispose() => SatelliteCatalogMatching.ResetCache();

    [Fact]
    public void GetOrBuildParenthesisIndex_concurrent_new_sets_does_not_throw()
    {
        const int threadCount = 16;
        const int iterationsPerThread = 500;
        var gate = new Barrier(threadCount);
        var errors = new List<Exception>();
        var lockObj = new object();

        var threads = Enumerable.Range(0, threadCount).Select(_ => new Thread(() =>
        {
            try
            {
                gate.SignalAndWait();
                for (var i = 0; i < iterationsPerThread; i++)
                {
                    var enabledNames = new HashSet<string>(SampleEnabledNames, StringComparer.OrdinalIgnoreCase);
                    var enabledIds = SatelliteCatalogMatching.CreateNoradIdSet(["25544"]);

                    foreach (var sat in SampleCatalog)
                    {
                        SatelliteCatalogMatching.IsEnabled(sat, enabledIds, enabledNames);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (lockObj)
                    errors.Add(ex);
            }
        })).ToArray();

        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join(TimeSpan.FromSeconds(30));

        Assert.Empty(errors);
    }
}
