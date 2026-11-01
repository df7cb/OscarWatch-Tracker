using OscarWatch.Core.Display;
using OscarWatch.Core.Models;
using OscarWatch.Core.Orbit;
using Zeptomoby.OrbitTools;
using SatelliteOrbit = Zeptomoby.OrbitTools.Orbit;

namespace OscarWatch.Orbit;

public sealed class BruteForcePassPredictor : IPassPredictor
{
    private static readonly TimeSpan CoarseStep = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefineTolerance = TimeSpan.FromSeconds(1);

    public Task<IReadOnlyList<PassInfo>> GetPassesAsync(
        SatelliteCatalogEntry satellite,
        GroundStation site,
        DateTime utcStart,
        DateTime utcEnd,
        double minimumElevationDeg,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var orbit = OrbitToolsMapping.CreateOrbit(satellite);
            var groundSite = OrbitToolsMapping.CreateSite(site);
            var mask = site.HorizonMask ?? new HorizonMask();

            var passes = new List<PassInfo>();
            var t = utcStart;
            var inPass = false;
            DateTime? aos = null;
            double maxEl = double.MinValue;
            DateTime maxElTime = t;
            double aosAz = 0;
            double losAz = 0;

            while (t <= utcEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // One PositionEci + GetLookAngle per coarse sample (was IsVisible + ElAt).
                var (visible, el) = EvaluateCoarseSample(orbit, groundSite, mask, minimumElevationDeg, t);

                if (!inPass && visible)
                {
                    inPass = true;
                    aos = RefineBoundary(orbit, groundSite, mask, minimumElevationDeg, t - CoarseStep, t, rising: true);
                    aosAz = AzAt(orbit, groundSite, aos.Value);
                    maxEl = el;
                    maxElTime = aos.Value;
                }
                else if (inPass)
                {
                    if (el > maxEl)
                    {
                        maxEl = el;
                        maxElTime = t;
                    }

                    if (!visible)
                    {
                        var los = RefineBoundary(orbit, groundSite, mask, minimumElevationDeg, t - CoarseStep, t, rising: false);
                        losAz = AzAt(orbit, groundSite, los);

                        passes.Add(new PassInfo
                        {
                            SatelliteName = satellite.Name,
                            NoradId = satellite.NoradId,
                            CatalogEntry = satellite,
                            AosUtc = PassUtc.Normalize(aos!.Value),
                            LosUtc = PassUtc.Normalize(los),
                            MaxElevationDeg = maxEl,
                            MaxElevationUtc = PassUtc.Normalize(maxElTime),
                            AosAzimuthDeg = aosAz,
                            LosAzimuthDeg = losAz
                        });

                        inPass = false;
                        aos = null;
                        maxEl = double.MinValue;
                    }
                }

                t += CoarseStep;
            }

            return (IReadOnlyList<PassInfo>)passes;
        }, cancellationToken);
    }

    /// <summary>
    /// Coarse-step visibility and elevation from a single look-angle evaluation.
    /// </summary>
    internal static (bool Visible, double ElevationDeg) EvaluateCoarseSample(
        SatelliteOrbit orbit,
        Site groundSite,
        HorizonMask mask,
        double minimumElevationDeg,
        DateTime time)
    {
        try
        {
            var look = groundSite.GetLookAngle(orbit.PositionEci(time));
            var visible = look.ElevationDeg >= mask.EffectiveFloor(look.AzimuthDeg, minimumElevationDeg);
            return (visible, look.ElevationDeg);
        }
        catch
        {
            return (false, -90);
        }
    }

    private static DateTime RefineBoundary(
        SatelliteOrbit orbit,
        Site site,
        HorizonMask mask,
        double minimumElevationDeg,
        DateTime before,
        DateTime after,
        bool rising)
    {
        var lo = before;
        var hi = after;

        while ((hi - lo) > RefineTolerance)
        {
            var mid = lo + (hi - lo) / 2;
            var above = IsVisibleAt(orbit, site, mask, minimumElevationDeg, mid);
            if (rising)
            {
                if (above)
                    hi = mid;
                else
                    lo = mid;
            }
            else
            {
                if (above)
                    lo = mid;
                else
                    hi = mid;
            }
        }

        return lo + (hi - lo) / 2;
    }

    private static bool IsVisibleAt(
        SatelliteOrbit orbit,
        Site site,
        HorizonMask mask,
        double minimumElevationDeg,
        DateTime time)
    {
        try
        {
            var look = site.GetLookAngle(orbit.PositionEci(time));
            return look.ElevationDeg >= mask.EffectiveFloor(look.AzimuthDeg, minimumElevationDeg);
        }
        catch
        {
            return false;
        }
    }

    private static double AzAt(SatelliteOrbit orbit, Site site, DateTime time)
    {
        try
        {
            return site.GetLookAngle(orbit.PositionEci(time)).AzimuthDeg;
        }
        catch
        {
            return 0;
        }
    }
}
