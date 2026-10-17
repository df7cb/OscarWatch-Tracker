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
        var expected = ModemTestSignals.ShortUiFrame("bad");
        const int txDelayMs = 50;
        const int txTailMs = 20;
        var preambleFlags = HdlcEncoder.FlagsFor(txDelayMs, HdlcEncoder.G3ruhBaud);
        var hdlcBits = HdlcEncoder.EncodeBits(
            [clean, expected],
            preambleFlags,
            HdlcEncoder.FlagsFor(txTailMs, HdlcEncoder.G3ruhBaud));
        var secondFrameStart = HdlcEncoder.EncodeBits([clean], preambleFlags, tailFlags: 0).Count;
        var damagedDataBit = StuffedBitOffset(Fcs16.Append(expected), expected.Length - 1, bitIndex: 0);
        hdlcBits[secondFrameStart + damagedDataBit] = !hdlcBits[secondFrameStart + damagedDataBit];
        var symbols = G3ruhScrambler.ScrambleBits(HdlcEncoder.ApplyNrzi(hdlcBits));
        var keyed = G3ruhModulator.Modulate(symbols, Rate, 0.35);
        var silence = Rate / 5;
        var audio = new float[keyed.Length + 2 * silence];
        keyed.CopyTo(audio, silence);

        var decoded = G3ruhReceiver.DecodeAll(audio, Rate, AfskBitFixMode.SingleBit);
        Assert.Equal(2, decoded.Count);
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(clean) && !d.Fixed);
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(expected) && d.Fixed);
    }

    private static int StuffedBitOffset(ReadOnlySpan<byte> frameWithFcs, int byteIndex, int bitIndex)
    {
        var offset = 0;
        var ones = 0;
        for (var b = 0; b < frameWithFcs.Length; b++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                if (b == byteIndex && bit == bitIndex)
                    return offset;

                var one = ((frameWithFcs[b] >> bit) & 1) != 0;
                offset++;
                if (one)
                {
                    if (++ones == 5)
                    {
                        offset++;
                        ones = 0;
                    }
                }
                else
                {
                    ones = 0;
                }
            }
        }

        throw new ArgumentOutOfRangeException(nameof(byteIndex));
    }
}
