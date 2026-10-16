using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class G3ruhReceiverClusterTests
{
    private const int Rate = 48000;

    [Fact]
    public void Back_to_back_short_frames_both_decode_clean()
    {
        var f1 = ModemTestSignals.ShortUiFrame("a");
        var f2 = ModemTestSignals.ShortUiFrame("b");
        var audio = ModemTestSignals.G3ruhTransmission([f1, f2], Rate, txDelayMs: 50, txTailMs: 20);
        var decoded = G3ruhReceiver.DecodeAll(audio, Rate);
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(f1));
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(f2));
    }

    [Fact]
    public void Back_to_back_short_frames_repair_does_not_drop_the_neighbour()
    {
        var clean = ModemTestSignals.ShortUiFrame("ok");
        var corrupt = ModemTestSignals.ShortUiFrame("bad");
        corrupt[10] ^= 0x01;
        var audio = ModemTestSignals.G3ruhTransmission([clean, corrupt], Rate, txDelayMs: 50, txTailMs: 20);

        var decoded = G3ruhReceiver.DecodeAll(audio, Rate, AfskBitFixMode.SingleBit);
        Assert.Equal(2, decoded.Count);
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(clean) && !d.Fixed);
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(corrupt));
    }

}
