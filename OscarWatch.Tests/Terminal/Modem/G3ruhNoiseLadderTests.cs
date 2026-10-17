using OscarWatch.Core.Terminal.Modem;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>
/// The <c>gen_packets -B 9600 -n 50</c> benchmark without Direwolf installed: 50 frames with noise rising
/// to the last one. Self-generated, so it guards against receiver regressions; independence from our own
/// modulator is what <see cref="DirewolfInteropTests"/> is for.
/// </summary>
public sealed class G3ruhNoiseLadderTests
{
    private const int Count = 50;
    private readonly ITestOutputHelper _output;

    public G3ruhNoiseLadderTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Gates sit at the scores measured when written: the receiver needs about 22 dB Eb/N0, so only the
    /// first few frames of the ladder decode. Raise them as the demodulator improves.
    /// </summary>
    [Theory]
    [InlineData(44100, 3)]
    [InlineData(48000, 5)]
    public void Decodes_the_gen_packets_9600_noise_ladder(int sampleRate, int gate)
    {
        var (audio, frames) = ModemTestSignals.G3ruhGenPacketsNoiseLadder(Count, sampleRate);

        var off = G3ruhReceiver.DecodeAll(audio, sampleRate, AfskBitFixMode.Off);
        var single = G3ruhReceiver.DecodeAll(audio, sampleRate, AfskBitFixMode.SingleBit);
        _output.WriteLine($"{sampleRate} Hz: {off.Count} of {Count} with bit fix off, {single.Count} with single-bit fix");

        Assert.All(single, d => Assert.Contains(frames, f => f.SequenceEqual(d.Ax25)));
        Assert.True(off.Count >= gate, $"Decoded {off.Count} of {Count}, gate {gate}");
    }
}
