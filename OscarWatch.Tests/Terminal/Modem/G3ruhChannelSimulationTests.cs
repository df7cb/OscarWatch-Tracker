using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class G3ruhChannelSimulationTests
{
    private const int Rate = 48000;

    [Fact]
    public void Clean_frame_decodes_at_48_kHz()
    {
        var frame = ModemTestSignals.UiFrame("9600 G3RUH OscarWatch");
        var decoded = G3ruhReceiver.DecodeAll(ModemTestSignals.G3ruhTransmission([frame], Rate), Rate);
        var single = Assert.Single(decoded);
        Assert.Equal(frame, single.Ax25);
        Assert.False(single.Fixed);
    }

    [Fact]
    public void Clean_frames_decode_at_44_1_kHz()
    {
        const int rate = 44100;
        var frames = Enumerable.Range(0, 20).Select(i => ModemTestSignals.UiFrame($"44.1 kHz capture {i:00}")).ToList();
        var audio = frames.SelectMany(f => ModemTestSignals.G3ruhTransmission([f], rate)).ToArray();

        var decoded = G3ruhReceiver.DecodeAll(audio, rate, AfskBitFixMode.Off);

        Assert.Equal(frames, decoded.Select(d => d.Ax25));
    }

    [Fact]
    public void Loopback_with_noise_and_dc_drift()
    {
        var frame = ModemTestSignals.UiFrame("impaired 9600");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate, amplitude: 0.4);
        for (var i = 0; i < audio.Length; i++)
            audio[i] += 0.15f + 0.00001f * i;
        ModemTestSignals.AddNoise(audio, 0.02, seed: 42);

        Assert.Contains(G3ruhReceiver.DecodeAll(audio, Rate), d => d.Ax25.SequenceEqual(frame));
    }

    [Fact]
    public void Polarity_inversion_still_decodes()
    {
        var frame = ModemTestSignals.UiFrame("inverted polarity");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate);
        for (var i = 0; i < audio.Length; i++)
            audio[i] = -audio[i];

        Assert.Contains(G3ruhReceiver.DecodeAll(audio, Rate), d => d.Ax25.SequenceEqual(frame));
    }

    [Theory]
    [InlineData(1.004)]
    [InlineData(0.996)]
    public void Clock_offset_tolerance(double ratio)
    {
        var frames = Enumerable.Range(0, 3).Select(i => ModemTestSignals.UiFrame($"offset {i}")).ToList();
        var audio = ModemTestSignals.G3ruhTransmission(frames, (int)Math.Round(Rate * ratio));
        var decoded = G3ruhReceiver.DecodeAll(audio, Rate);
        Assert.Equal(frames, decoded.Select(d => d.Ax25));
    }
}
