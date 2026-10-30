using OscarWatch.Core.Sstv;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Sstv;

/// <summary>
/// Decodes pictures sent over a simulated satellite FM link: real FM threshold clicks from
/// the receiver's discriminator, fading, drop-outs, de-emphasis and Doppler time
/// compression. The lock and picture error against carrier-to-noise ratio (CNR) is the
/// curve decoder changes are judged on.
/// </summary>
public sealed class SstvChannelSimulationTests(ITestOutputHelper output)
{
    internal const int Rate = 24000;

    /// <summary>Mean error at or under which a picture counts as usable.</summary>
    internal const double UsableError = 30;

    internal sealed record Point(double CnrDb, double LockPercent, double MeanError, bool Decoded);

    /// <summary>Decode one picture; a lost picture scores lock 0 and error 128.</summary>
    internal static Point Measure(SstvMode mode, SstvTestEncoder.Settings settings, SstvDecoderOptions options)
    {
        var audio = SstvTestEncoder.Encode(mode, Rate, settings);
        var images = SstvRoundTripTests.Decode(audio, options, Rate);
        var image = images.OrderByDescending(i => i.LockPercent).FirstOrDefault(i => i.Mode == mode);
        return image is null
            ? new Point(settings.CnrDb ?? double.PositiveInfinity, 0, 128, false)
            : new Point(settings.CnrDb ?? double.PositiveInfinity, image.LockPercent, SstvRoundTripTests.MeanError(mode, image.Image), true);
    }

    internal static List<Point> Curve(
        SstvMode mode,
        IEnumerable<double> cnrDb,
        Func<SstvDecoderOptions> options,
        Func<double, SstvTestEncoder.Settings>? settings = null)
    {
        settings ??= cnr => new SstvTestEncoder.Settings { CnrDb = cnr };
        return cnrDb.AsParallel().AsOrdered().Select(c => Measure(mode, settings(c), options())).ToList();
    }

    /// <summary>Lowest CNR from which every stronger point in the curve is usable.</summary>
    internal static double Threshold(IReadOnlyList<Point> curve)
    {
        var threshold = double.PositiveInfinity;
        foreach (var p in curve.OrderByDescending(p => p.CnrDb))
        {
            if (!p.Decoded || p.MeanError > UsableError)
                break;
            threshold = p.CnrDb;
        }

        return threshold;
    }

    internal static void Report(ITestOutputHelper output, string label, IEnumerable<Point> curve)
    {
        foreach (var p in curve)
            output.WriteLine($"{label} CNR {p.CnrDb,5:0.0} dB: lock {p.LockPercent,5:0.0}%, error {p.MeanError,6:0.0}{(p.Decoded ? "" : " (lost)")}");
    }

    [Fact]
    public void Strong_fm_link_decodes_like_clean_audio()
    {
        var mode = SstvModeTable.Robot36;
        var p = Measure(mode, new SstvTestEncoder.Settings { CnrDb = 30 }, new SstvDecoderOptions());

        Assert.True(p.Decoded);
        Assert.True(p.LockPercent > 98, $"lock {p.LockPercent:0.0}%");
        Assert.True(p.MeanError < 12, $"error {p.MeanError:0.0}");
    }

    [Fact]
    public void Picture_degrades_as_the_link_weakens()
    {
        var mode = SstvModeTable.Robot36;
        var curve = Curve(mode, [4, 6, 8, 10, 12, 16, 20], () => new SstvDecoderOptions());
        Report(output, mode.Name, curve);

        Assert.True(curve[^1].MeanError < curve[0].MeanError, "a strong link should beat a weak one");
        Assert.True(curve[^1].LockPercent > 95);
        Assert.True(double.IsFinite(Threshold(curve)), "some CNR in the sweep should give a usable picture");
        Assert.True(Threshold(curve) > 4, "the weakest link should be below threshold, or the sweep says nothing");
    }

    [Fact]
    public void Fm_threshold_noise_is_impulsive_not_gaussian()
    {
        // Below threshold the discriminator output has heavy tails: a few samples far off,
        // which is what a pixel mean cannot average away.
        var audio = SstvTestEncoder.Encode(SstvModeTable.Robot36, Rate, new SstvTestEncoder.Settings { CnrDb = 5 });
        var clean = SstvTestEncoder.Encode(SstvModeTable.Robot36, Rate, new SstvTestEncoder.Settings { CnrDb = 60 });
        var diff = audio.Zip(clean, (a, b) => (double)(a - b)).ToArray();
        var rms = Math.Sqrt(diff.Average(d => d * d));
        var kurtosis = diff.Average(d => Math.Pow(d / rms, 4));

        Assert.True(kurtosis > 6, $"kurtosis {kurtosis:0.0} (3 is Gaussian)");
    }

    [Fact]
    public void Spin_fading_and_dropouts_still_give_a_picture()
    {
        var mode = SstvModeTable.Robot36;
        var p = Measure(mode, new SstvTestEncoder.Settings
        {
            CnrDb = 16,
            FadePeriodSeconds = 4,
            FadeDepthDb = 14,
            DropoutEverySeconds = 9,
            DropoutSeconds = 0.4,
        }, new SstvDecoderOptions());
        output.WriteLine($"lock {p.LockPercent:0.0}%, error {p.MeanError:0.0}");

        Assert.True(p.Decoded);
        Assert.True(p.LockPercent > 70, $"lock {p.LockPercent:0.0}%");
    }

    [Fact]
    public void Receiver_de_emphasis_tilt_is_tolerated()
    {
        var mode = SstvModeTable.MartinM2;
        var p = Measure(mode, new SstvTestEncoder.Settings { CnrDb = 25, DeEmphasisUs = 75 }, new SstvDecoderOptions());

        Assert.True(p.Decoded);
        Assert.True(p.LockPercent > 95, $"lock {p.LockPercent:0.0}%");
        Assert.True(p.MeanError < 15, $"error {p.MeanError:0.0}");
    }

    [Fact]
    public void Doppler_time_compression_through_a_pass_does_not_bend_the_picture()
    {
        // An overhead LEO pass sweeps the range rate from about +7 to -7 km/s: ±23 ppm.
        var mode = SstvModeTable.Pd120;
        var p = Measure(mode, new SstvTestEncoder.Settings { TimeCompressionPpm = 23, ClockRatio = 1.00004 }, new SstvDecoderOptions());

        Assert.True(p.Decoded);
        Assert.True(p.MeanError < 9, $"error {p.MeanError:0.0}");
    }
}
