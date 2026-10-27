using OscarWatch.Core.Sstv;

namespace OscarWatch.Tests.Sstv;

public sealed class SstvInputSilenceTests
{
    private const int Rate = 12000;

    [Fact]
    public void Silence_is_reported_only_after_the_full_wait()
    {
        var watch = new SstvInputSilence(Rate);
        var chunk = new float[Rate];

        for (var s = 1; s < SstvInputSilence.SilentSeconds; s++)
            watch.Add(chunk);
        Assert.False(watch.IsSilent);

        watch.Add(chunk);
        Assert.True(watch.IsSilent);
        Assert.False(watch.HeardSound);
    }

    [Fact]
    public void Sound_clears_silence_and_is_remembered()
    {
        var watch = new SstvInputSilence(Rate);
        var quiet = new float[Rate * (int)SstvInputSilence.SilentSeconds];
        watch.Add(quiet);
        Assert.True(watch.IsSilent);

        var hiss = Hiss(Rate, rms: 0.01);
        watch.Add(hiss);
        Assert.False(watch.IsSilent);
        Assert.True(watch.HeardSound);

        watch.Add(new float[Rate]);
        Assert.False(watch.IsSilent);
        Assert.True(watch.HeardSound);
    }

    [Fact]
    public void Receiver_hiss_is_not_silence_but_a_digital_floor_is()
    {
        Assert.False(SstvInputSilence.IsSilentRecording(Hiss(Rate, rms: 0.005)));
        Assert.True(SstvInputSilence.IsSilentRecording(new float[Rate]));
        Assert.True(SstvInputSilence.IsSilentRecording(Hiss(Rate, rms: 0.0001)));
    }

    private static float[] Hiss(int length, double rms)
    {
        var random = new Random(3);
        var samples = new float[length];
        for (var i = 0; i < length; i++)
            samples[i] = (float)(rms * Math.Sqrt(3) * (2 * random.NextDouble() - 1));
        return samples;
    }
}
