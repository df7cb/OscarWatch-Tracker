using OscarWatch.Core.Terminal.Modem;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>CAT Doppler steps and residual discriminator offset/ramp on the 9600 G3RUH data port.</summary>
public sealed class G3ruhDopplerRobustnessTests
{
    private const int Rate = G3ruhDemodulator.PreferredSampleRate;
    private readonly ITestOutputHelper _output;

    public G3ruhDopplerRobustnessTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void High_pass_corner_matches_the_documented_value()
    {
        var k = G3ruhDemodulator.CoefficientForCornerHz(G3ruhDemodulator.HighPassCornerHz, Rate);
        var measuredHz = k * Rate / (2 * Math.PI);
        _output.WriteLine(
            $"Slow DC blocker: k={k:F5}, corner≈{measuredHz:F1} Hz (target {G3ruhDemodulator.HighPassCornerHz} Hz)");
        Assert.InRange(measuredHz, 10, 14);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sudden_discriminator_step_mid_frame_still_decodes(bool negativeStep)
    {
        var frame = ModemTestSignals.UiFrame("Doppler step mid frame");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate);
        var step = (negativeStep ? -1 : 1) * G3ruhDopplerTolerance.MaxDiscriminatorStep;
        var index = ModemTestSignals.G3ruhMidFrameSample(Rate);
        ModemTestSignals.AddDiscriminatorDcStep(audio, index, step);

        AssertDecoded(frame, audio, $"step {step:F2} at sample {index}");
    }

    [Fact]
    public void Sudden_discriminator_step_between_frames_still_decodes()
    {
        var f1 = ModemTestSignals.UiFrame("frame one");
        var f2 = ModemTestSignals.UiFrame("frame two");
        var audio = ModemTestSignals.G3ruhTransmission([f1, f2], Rate, txDelayMs: 200, txTailMs: 30);
        var start = ModemTestSignals.G3ruhPayloadStart(Rate);
        var gap = (int)(Rate * 0.35);
        ModemTestSignals.AddDiscriminatorDcStep(audio, start + gap, G3ruhDopplerTolerance.MaxDiscriminatorStep);

        var decoded = G3ruhReceiver.DecodeAll(audio, Rate);
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(f1));
        Assert.Contains(decoded, d => d.Ax25.SequenceEqual(f2));
    }

    [Fact]
    public void Constant_discriminator_offset_within_documented_range_decodes()
    {
        var frame = ModemTestSignals.UiFrame("constant DC offset");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate);
        ModemTestSignals.AddDiscriminatorDcOffset(audio, G3ruhDopplerTolerance.MaxConstantDiscriminatorOffset);

        AssertDecoded(frame, audio, "constant offset");
    }

    [Fact]
    public void Residual_discriminator_ramp_within_documented_range_decodes()
    {
        var frame = ModemTestSignals.UiFrame("ramp across frame");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate);
        ModemTestSignals.AddDiscriminatorDcRamp(
            audio,
            Rate,
            startOffset: 0.08f,
            voltsPerSecond: G3ruhDopplerTolerance.MaxDiscriminatorRampVoltsPerSecond);

        AssertDecoded(frame, audio, "linear ramp");
    }

    [Fact]
    public void Step_recovery_recentres_before_the_next_frame_bits()
    {
        var frame = ModemTestSignals.UiFrame("recovery timing");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate);
        var stepAt = ModemTestSignals.G3ruhMidFrameSample(Rate);
        ModemTestSignals.AddDiscriminatorDcStep(audio, stepAt, 0.35f);

        var demod = new G3ruhDemodulator(Rate);
        var activeAfterStep = 0;
        for (var i = 0; i < audio.Length; i++)
        {
            demod.Process(audio[i]);
            if (i >= stepAt && demod.StepRecoveryActive)
                activeAfterStep++;
        }

        var ms = activeAfterStep / (double)Rate * 1000;
        _output.WriteLine($"Fast DC recovery active for {ms:F1} ms after mid-frame step");
        Assert.InRange(ms, 8, 14);
        AssertDecoded(frame, audio, "step recovery window");
    }

    private void AssertDecoded(byte[] frame, float[] audio, string scenario)
    {
        var ok = G3ruhReceiver.DecodeAll(audio, Rate).Any(d => d.Ax25.SequenceEqual(frame));
        Assert.True(ok, $"Expected decode with {scenario}");
    }
}
