using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class FrameBitFixerTests
{
    [Fact]
    public void Single_flipped_bit_is_repaired()
    {
        var frame = ModemTestSignals.UiFrame("=5555.55N/00333.33W-test");
        var damaged = Fcs16.Append(frame);
        damaged[20] ^= 0x04;

        var repaired = FrameBitFixer.TryFix(damaged, AfskBitFixMode.SingleBit);

        Assert.Equal(frame, repaired);
    }

    [Fact]
    public void Adjacent_pair_needs_the_pair_mode()
    {
        var frame = ModemTestSignals.UiFrame("pair test");
        var damaged = Fcs16.Append(frame);
        damaged[18] ^= 0x18;

        Assert.Null(FrameBitFixer.TryFix(damaged, AfskBitFixMode.SingleBit));
        Assert.Equal(frame, FrameBitFixer.TryFix(damaged, AfskBitFixMode.SingleAndAdjacentPair));
    }

    [Fact]
    public void Off_never_repairs()
    {
        var damaged = Fcs16.Append(ModemTestSignals.UiFrame("off"));
        damaged[16] ^= 0x01;

        Assert.Null(FrameBitFixer.TryFix(damaged, AfskBitFixMode.Off));
    }

    [Fact]
    public void Random_blocks_are_rejected_even_when_a_flip_passes_the_fcs()
    {
        var random = new Random(7);
        var accepted = 0;
        for (var trial = 0; trial < 300; trial++)
        {
            var block = new byte[120];
            random.NextBytes(block);
            if (FrameBitFixer.TryFix(block, AfskBitFixMode.SingleAndAdjacentPair) is not null)
                accepted++;
        }

        Assert.Equal(0, accepted);
    }

    [Fact]
    public void Plausibility_rejects_bad_callsigns()
    {
        Assert.True(FrameBitFixer.IsPlausibleUi(ModemTestSignals.UiFrame("ok")));

        var bad = ModemTestSignals.UiFrame("bad");
        bad[8] = (byte)('#' << 1);
        Assert.False(FrameBitFixer.IsPlausibleUi(bad));
    }
}
