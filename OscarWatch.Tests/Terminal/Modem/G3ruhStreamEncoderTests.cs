using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class G3ruhStreamEncoderTests
{
    [Fact]
    public void Chained_batches_match_one_shot_encoding()
    {
        var frames = Enumerable.Range(0, 2).Select(i => ModemTestSignals.UiFrame($"enc {i}")).ToList();
        const int delayMs = 40;
        const int tailMs = 15;
        var delayFlags = HdlcEncoder.FlagsFor(delayMs, HdlcEncoder.G3ruhBaud);
        var tailFlags = HdlcEncoder.FlagsFor(tailMs, HdlcEncoder.G3ruhBaud);
        var oneShot = HdlcEncoder.EncodeG3ruhBits(frames, delayFlags, tailFlags);

        var encoder = new G3ruhStreamEncoder();
        var parts = new List<bool>();
        parts.AddRange(encoder.AppendFrames([frames[0]], delayFlags, 0));
        parts.AddRange(encoder.AppendContinuationFrames([frames[1]]));
        parts.AddRange(encoder.AppendFlags(Math.Max(1, tailFlags - 1)));

        Assert.Equal(oneShot, parts.ToArray());
    }
}
