using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class PolyphaseResamplerTests
{
    [Theory]
    [InlineData(48000, 1000)]
    [InlineData(48000, 1700)]
    [InlineData(48000, 2400)]
    [InlineData(44100, 1000)]
    [InlineData(44100, 1700)]
    [InlineData(44100, 2400)]
    public void Passband_is_flat_from_1000_to_2400_hz(int inputRate, double hz)
    {
        var gainDb = MeasureGainDb(inputRate, hz);

        Assert.InRange(gainDb, -0.1, 0.1);
    }

    [Theory]
    [InlineData(48000, 6500)]
    [InlineData(48000, 9000)]
    [InlineData(48000, 15000)]
    [InlineData(44100, 6500)]
    [InlineData(44100, 9000)]
    [InlineData(44100, 15000)]
    public void Stopband_rejects_tones_that_would_alias(int inputRate, double hz)
    {
        var gainDb = MeasureGainDb(inputRate, hz);

        Assert.True(gainDb < -55, $"{hz} Hz at {inputRate} Hz was only {gainDb:0.0} dB down");
    }

    [Fact]
    public void Output_length_follows_the_ratio()
    {
        var resampler = new PolyphaseResampler(44100, 12000);

        var output = resampler.ProcessAll(new float[44100]);

        Assert.InRange(output.Length, 11999, 12001);
    }

    private static double MeasureGainDb(int inputRate, double hz)
    {
        var resampler = new PolyphaseResampler(inputRate, AfskDemodulator.SampleRate);
        var input = new float[inputRate];
        for (var i = 0; i < input.Length; i++)
            input[i] = (float)Math.Sin(2 * Math.PI * hz * i / inputRate);

        var output = resampler.ProcessAll(input);
        var skip = output.Length / 4;
        var rms = Math.Sqrt(output.Skip(skip).Take(output.Length / 2).Average(s => (double)s * s));
        return 20 * Math.Log10(rms / Math.Sqrt(0.5));
    }
}
