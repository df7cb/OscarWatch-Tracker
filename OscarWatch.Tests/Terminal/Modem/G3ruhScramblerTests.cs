using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class G3ruhScramblerTests
{
    [Fact]
    public void Scrambler_round_trips()
    {
        var nrzi = HdlcEncoder.ApplyNrzi(HdlcEncoder.EncodeBits([ModemTestSignals.UiFrame("scrambler")], 3, 2));
        var scrambled = G3ruhScrambler.ScrambleBits(nrzi);
        var descrambler = new G3ruhScrambler();
        var recovered = new bool[scrambled.Length];
        for (var i = 0; i < scrambled.Length; i++)
            recovered[i] = descrambler.DescrambleBit(scrambled[i]);

        Assert.Equal(nrzi, recovered);
    }
}
