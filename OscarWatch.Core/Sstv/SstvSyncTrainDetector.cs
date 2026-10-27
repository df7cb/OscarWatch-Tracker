namespace OscarWatch.Core.Sstv;

/// <param name="Mode">Mode whose line period and sync length matched.</param>
/// <param name="FirstSyncIndex">Sync start of the earliest line in the run.</param>
/// <param name="OffsetHz">Measured sync frequency minus 1200 Hz.</param>
public readonly record struct SstvSyncTrainResult(SstvMode Mode, long FirstSyncIndex, double OffsetHz);

/// <summary>
/// Picks the mode from the picture itself when the VIS header was lost, which is common
/// at the start of a noisy pass: sync pulses repeat at the mode's line period and have
/// the mode's sync length, so a regular run of them identifies the mode.
/// </summary>
public sealed class SstvSyncTrainDetector
{
    private const int StepsChecked = 5;
    private const int HitsNeeded = 4;

    /// <summary>
    /// A noisy sync can dip for a couple of milliseconds and be seen as two short pulses.
    /// Fragments closer than this are one pulse. Distinct lines are much further apart.
    /// </summary>
    private const double BridgeMs = 4;

    /// <summary>How far from the middle of a line another sync may sit and still count.</summary>
    private const double GapSyncWindowMs = 20;

    private readonly int _rate;

    public SstvSyncTrainDetector(int sampleRate) => _rate = sampleRate;

    public bool TryFind(
        SstvSignalBuffer buffer,
        long from,
        double offsetHz,
        IReadOnlyList<SstvMode> candidates,
        out SstvSyncTrainResult result)
    {
        result = default;
        var pulses = FindPulses(buffer, Math.Max(from, buffer.Start), buffer.End, offsetHz);
        if (pulses.Count < HitsNeeded + 1)
            return false;

        SstvMode? bestMode = null;
        var bestStart = 0L;
        var bestHits = 0;
        var bestError = double.MaxValue;
        var bestPeriod = 0.0;
        var bestSync = 0.0;
        var bestTol = 0.0;
        foreach (var mode in candidates)
        {
            var period = mode.LineMs * _rate / 1000.0;
            var syncLen = mode.SyncMs * _rate / 1000.0;
            var tol = 0.004 * period + 0.002 * _rate;
            if (buffer.End - buffer.Start < period * (StepsChecked + 1))
                continue;

            for (var i = 0; i < pulses.Count; i++)
            {
                var p = pulses[i];
                if (!LengthMatches(p.Length, syncLen))
                    continue;
                if (p.Start + period * StepsChecked > buffer.End)
                    break;

                var hits = 0;
                var error = 0.0;
                for (var k = 1; k <= StepsChecked; k++)
                {
                    var expected = p.Start + k * period;
                    var q = Nearest(pulses, expected);
                    if (q is { } hit && Math.Abs(hit.Start - expected) <= tol && LengthMatches(hit.Length, syncLen))
                    {
                        hits++;
                        error += Math.Abs(hit.Start - expected);
                    }
                }

                if (hits < HitsNeeded)
                    continue;

                // Robot 72 is twice Robot 36 and uses the same sync, so every other
                // Robot 36 pulse sits on the Robot 72 grid. Syncs in the gaps mean
                // the shorter line is the one actually sent.
                if (GapsHoldAnotherSync(pulses, p.Start, period, syncLen))
                    continue;

                error /= hits;
                if (bestMode is not null && IsHarmonicPeriod(period, bestPeriod, syncLen, bestSync))
                    break;

                var shorterFits = bestMode is not null && IsHarmonicPeriod(bestPeriod, period, bestSync, syncLen);
                if (shorterFits || hits > bestHits || (hits == bestHits && error < bestError))
                {
                    bestMode = mode;
                    bestStart = p.Start;
                    bestHits = hits;
                    bestError = error;
                    bestPeriod = period;
                    bestSync = syncLen;
                    bestTol = tol;
                }

                break;
            }
        }

        if (bestMode is null)
            return false;

        bestStart = EarliestOnGrid(pulses, bestStart, bestPeriod, bestSync, bestTol);
        var len = (int)Math.Round(bestMode.SyncMs * _rate / 1000.0);
        var syncHz = SstvSyncDetector.SyncHz + offsetHz;
        var measured = SstvSyncDetector.MeasureHz(buffer, bestStart, len, syncHz);
        var offset = double.IsNaN(measured) ? offsetHz : measured - SstvSyncDetector.SyncHz;
        result = new SstvSyncTrainResult(bestMode, bestStart, offset);
        return true;
    }

    /// <summary>
    /// True when <paramref name="longer"/> is an integer multiple of <paramref name="shorter"/>
    /// and the two modes could be seeing the same sync pulses.
    /// </summary>
    private static bool IsHarmonicPeriod(double longer, double shorter, double longerSync, double shorterSync)
    {
        if (shorter <= 0 || longer < shorter * 1.5)
            return false;
        if (!LengthMatches(longerSync, shorterSync) || !LengthMatches(shorterSync, longerSync))
            return false;

        var ratio = longer / shorter;
        var nearest = Math.Round(ratio);
        return nearest >= 2 && Math.Abs(ratio - nearest) <= 0.02 * nearest;
    }

    /// <summary>
    /// Sync-length pulses near the middle of several lines. A genuine line has porch and
    /// picture there, so this is a mode whose period is a multiple of the real one.
    /// </summary>
    private bool GapsHoldAnotherSync(
        List<(long Start, double Length)> pulses,
        long start,
        double period,
        double syncLen)
    {
        var window = GapSyncWindowMs * _rate / 1000.0;
        var found = 0;
        for (var k = 0; k < StepsChecked; k++)
        {
            var mid = start + (k + 0.5) * period;
            if (HasPulseBetween(pulses, mid - window, mid + window, syncLen))
                found++;
        }

        return found >= (StepsChecked + 1) / 2;
    }

    private static bool HasPulseBetween(
        List<(long Start, double Length)> pulses,
        double lo,
        double hi,
        double syncLen)
    {
        var low = 0;
        var high = pulses.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (pulses[mid].Start < lo)
                low = mid + 1;
            else
                high = mid;
        }

        for (var i = low; i < pulses.Count && pulses[i].Start <= hi; i++)
        {
            if (LengthMatches(pulses[i].Length, syncLen))
                return true;
        }

        return false;
    }

    /// <summary>Walk back to the first sync still on this line period, so the picture is not cropped.</summary>
    private static long EarliestOnGrid(
        List<(long Start, double Length)> pulses,
        long start,
        double period,
        double syncLen,
        double tol)
    {
        for (var n = 0; n < 40; n++)
        {
            var expected = start - period;
            var q = Nearest(pulses, expected);
            if (q is not { } prev || prev.Start >= start)
                break;
            if (Math.Abs(prev.Start - expected) > tol || !LengthMatches(prev.Length, syncLen))
                break;
            start = prev.Start;
        }

        return start;
    }

    private static bool LengthMatches(double measured, double expected) =>
        measured >= expected * 0.55 && measured <= expected * 1.6;

    private static (long Start, double Length)? Nearest(List<(long Start, double Length)> pulses, double position)
    {
        var lo = 0;
        var hi = pulses.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (pulses[mid].Start < position)
                lo = mid + 1;
            else
                hi = mid;
        }

        (long Start, double Length)? best = null;
        for (var i = Math.Max(0, lo - 1); i <= Math.Min(pulses.Count - 1, lo); i++)
        {
            if (best is null || Math.Abs(pulses[i].Start - position) < Math.Abs(best.Value.Start - position))
                best = pulses[i];
        }

        return best;
    }

    /// <summary>Runs where a 2 ms average of the sync score stays above one half.</summary>
    private List<(long Start, double Length)> FindPulses(SstvSignalBuffer buffer, long from, long to, double offsetHz)
    {
        var pulses = new List<(long, double)>();
        var expected = SstvSyncDetector.SyncHz + offsetHz;
        var win = Math.Max(1, _rate / 500);
        var sum = 0.0;
        var inRun = false;
        long runStart = 0;
        for (var i = from; i < to; i++)
        {
            sum += SstvSyncDetector.SampleScore(buffer[i], expected);
            if (i - win >= from)
                sum -= SstvSyncDetector.SampleScore(buffer[i - win], expected);
            if (i - from < win)
                continue;

            var on = sum / win > 0.5;
            var centre = i - win / 2;
            if (on && !inRun)
            {
                inRun = true;
                runStart = centre;
            }
            else if (!on && inRun)
            {
                inRun = false;
                pulses.Add((runStart, centre - runStart));
            }
        }

        return BridgeSplits(pulses);
    }

    /// <summary>Join fragments of one sync that a brief dip split apart.</summary>
    private List<(long Start, double Length)> BridgeSplits(List<(long Start, double Length)> pulses)
    {
        if (pulses.Count == 0)
            return pulses;

        var maxGap = BridgeMs * _rate / 1000.0;
        var bridged = new List<(long Start, double Length)>(pulses.Count);
        var start = pulses[0].Start;
        var end = pulses[0].Start + pulses[0].Length;
        for (var i = 1; i < pulses.Count; i++)
        {
            var pulse = pulses[i];
            var pulseEnd = pulse.Start + pulse.Length;
            if (pulse.Start - end <= maxGap)
                end = Math.Max(end, pulseEnd);
            else
            {
                bridged.Add((start, end - start));
                start = pulse.Start;
                end = pulseEnd;
            }
        }

        bridged.Add((start, end - start));
        return bridged;
    }
}
