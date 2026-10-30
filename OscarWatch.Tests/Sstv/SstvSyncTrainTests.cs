using OscarWatch.Core.Sstv;

namespace OscarWatch.Tests.Sstv;

/// <summary>
/// Mode pick from the sync pulse train, used when the VIS header was missed.
/// </summary>
public sealed class SstvSyncTrainTests
{
    private const int Rate = SstvDecoder.InternalRate;

    [Fact]
    public void Split_robot36_syncs_are_not_read_as_robot72()
    {
        // Robot 72 is exactly two Robot 36 lines, with the same 9 ms sync. When the
        // in-between syncs dip and break into short fragments, only the 300 ms grid
        // used to qualify, and the picture was decoded as Robot 72.
        var hz = new float[(int)(4.0 * Rate)];
        Array.Fill(hz, 1900f);
        var line = (int)Math.Round(0.150 * Rate);
        var first = (int)Math.Round(0.20 * Rate);
        for (var n = 0; first + n * line + 200 < hz.Length; n++)
            AddSync(hz, first + n * line, split: n % 2 == 1);

        var mode = Find(hz);

        Assert.Equal(SstvModeId.Robot36, mode.Id);
    }

    [Fact]
    public void Robot72_pulse_train_stays_robot72()
    {
        var hz = new float[(int)(4.0 * Rate)];
        Array.Fill(hz, 1900f);
        var line = (int)Math.Round(0.300 * Rate);
        var first = (int)Math.Round(0.20 * Rate);
        for (var n = 0; first + n * line + 200 < hz.Length; n++)
            AddSync(hz, first + n * line, split: false);

        var mode = Find(hz);

        Assert.Equal(SstvModeId.Robot72, mode.Id);
    }

    private static SstvMode Find(float[] hz)
    {
        var buffer = new SstvSignalBuffer();
        buffer.Append(hz);
        var detector = new SstvSyncTrainDetector(Rate);

        Assert.True(detector.TryFind(buffer, 0, 0, SstvModeTable.SyncTrainCandidates, out var result));
        return result.Mode;
    }

    /// <summary>9 ms at 1200 Hz. A split pulse has a 2.5 ms hole, so it arrives as two short fragments.</summary>
    private static void AddSync(float[] hz, int start, bool split)
    {
        var sync = (int)Math.Round(0.009 * Rate);
        if (!split)
        {
            Fill(hz, start, sync);
            return;
        }

        var hole = (int)Math.Round(0.0025 * Rate);
        var part = (sync - hole) / 2;
        Fill(hz, start, part);
        Fill(hz, start + part + hole, sync - part - hole);
    }

    private static void Fill(float[] hz, int start, int length)
    {
        var end = Math.Min(hz.Length, start + length);
        for (var i = Math.Max(0, start); i < end; i++)
            hz[i] = 1200f;
    }
}
