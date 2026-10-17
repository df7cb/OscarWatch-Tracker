using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class G3ruhStreamModulatorTests
{
    private const int Rate = 48000;

    [Fact]
    public void Single_append_matches_one_shot_modulation()
    {
        var frames = Enumerable.Range(0, 3).Select(i => ModemTestSignals.UiFrame($"one {i}")).ToList();
        var bits = HdlcEncoder.EncodeG3ruhTransmission(frames, txDelayMs: 80, txTailMs: 20);
        var expected = G3ruhModulator.Modulate(bits, Rate, 0.4);
        var got = new G3ruhStreamModulator(Rate, 0.4).Finish(bits);
        Assert.Equal(expected.Length, got.Length);
        var maxDiff = expected.Zip(got, (a, b) => Math.Abs(a - b)).Max();
        Assert.InRange(maxDiff, 0, 0.002);
    }

    [Fact]
    public void Chunked_append_matches_one_shot_modulation()
    {
        var frames = Enumerable.Range(0, 3).Select(i => ModemTestSignals.UiFrame($"chunk {i}")).ToList();
        var bits = HdlcEncoder.EncodeG3ruhTransmission(frames, txDelayMs: 80, txTailMs: 20);
        var expected = new G3ruhStreamModulator(Rate, 0.4).Finish(bits);

        var stream = new G3ruhStreamModulator(Rate, 0.4);
        var chunks = new List<float>();
        const int chunkBits = 37;
        for (var i = 0; i < bits.Length; i += chunkBits)
        {
            var slice = bits.Skip(i).Take(Math.Min(chunkBits, bits.Length - i)).ToArray();
            chunks.AddRange(stream.Append(slice));
        }

        chunks.AddRange(stream.Finish([]));
        Assert.Equal(expected.Length, chunks.Count);
        var maxDiff = 0.0;
        for (var i = 0; i < expected.Length; i++)
            maxDiff = Math.Max(maxDiff, Math.Abs(expected[i] - chunks[i]));
        Assert.Equal(expected.Length, chunks.Count);
        Assert.InRange(maxDiff, 0, 0.08);
    }
}
