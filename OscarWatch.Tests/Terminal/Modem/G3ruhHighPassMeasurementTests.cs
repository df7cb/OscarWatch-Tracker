using OscarWatch.Core.Terminal.Modem;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>Justifies the G3RUH DC blocker corner against scrambled baseband content.</summary>
public sealed class G3ruhHighPassMeasurementTests
{
    private const int Rate = G3ruhDemodulator.PreferredSampleRate;
    private readonly ITestOutputHelper _output;

    public G3ruhHighPassMeasurementTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Scrambled_9600_energy_below_12_hz_is_small()
    {
        var frame = ModemTestSignals.UiFrame("spectral measurement key fill");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate, silenceSeconds: 0.05);
        var start = ModemTestSignals.G3ruhPayloadStart(Rate, 0.05);
        var payload = audio.AsSpan(start);

        var k = G3ruhDemodulator.CoefficientForCornerHz(G3ruhDemodulator.HighPassCornerHz, Rate);
        var dc = 0f;
        var sumSqIn = 0.0;
        var sumSqOut = 0.0;
        foreach (var sample in payload)
        {
            sumSqIn += sample * sample;
            var error = sample - dc;
            dc += k * error;
            var hp = sample - dc;
            sumSqOut += hp * hp;
        }

        var retained = sumSqOut / Math.Max(sumSqIn, 1e-12);
        _output.WriteLine(
            $"After {G3ruhDemodulator.HighPassCornerHz} Hz blocker, {retained * 100:F1}% of baseband power remains (payload only).");
        Assert.InRange(retained, 0.85, 1.05);
    }
}
