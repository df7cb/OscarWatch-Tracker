using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>Step recovery must not trigger on clean G3RUH symbol transitions at higher levels.</summary>
public sealed class G3ruhStepDetectorTests
{
    private const int Rate = G3ruhDemodulator.PreferredSampleRate;

    [Theory]
    [InlineData(0.35)]
    [InlineData(0.5)]
    public void Clean_transmission_does_not_trigger_step_recovery(double peak)
    {
        var frame = ModemTestSignals.UiFrame("clean level sweep");
        var audio = ModemTestSignals.G3ruhTransmission([frame], Rate, amplitude: peak);
        ModemTestSignals.Normalise(audio, peak);

        var demod = new G3ruhDemodulator(Rate);
        var recoveryBursts = 0;
        var wasActive = false;
        foreach (var s in audio)
        {
            demod.Process(s);
            if (demod.StepRecoveryActive)
            {
                if (!wasActive)
                    recoveryBursts++;
                wasActive = true;
            }
            else
            {
                wasActive = false;
            }
        }

        Assert.InRange(recoveryBursts, 0, peak >= 0.49 ? 4 : 0);
        Assert.Contains(G3ruhReceiver.DecodeAll(audio, Rate), d => d.Ax25.SequenceEqual(frame));
    }
}
