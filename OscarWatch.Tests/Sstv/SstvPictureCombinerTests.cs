using OscarWatch.Core.Sstv;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Sstv;

public sealed class SstvPictureCombinerTests(ITestOutputHelper output)
{
    private const int Rate = 24000;

    private static SstvDecodedImage Receive(SstvMode mode, double dropoutStart, SstvTestEncoder.PixelSource? pixel = null, int seed = 1)
    {
        var settings = new SstvTestEncoder.Settings
        {
            CnrDb = 12,
            Seed = seed,
            DropoutStartSeconds = dropoutStart,
            DropoutEverySeconds = 1000,
            DropoutSeconds = 4,
        };
        var audio = pixel is null
            ? SstvTestEncoder.Encode(mode, Rate, settings)
            : SstvTestEncoder.Encode(mode, pixel, Rate, settings);
        return SstvRoundTripTests.Decode(audio, new SstvDecoderOptions { HoldThroughFades = true }, Rate)
            .Where(i => i.Mode == mode)
            .MaxBy(i => i.LockPercent)!;
    }

    [Theory]
    [InlineData(SstvModeId.Robot36)]
    [InlineData(SstvModeId.Pd120)]
    public void Two_damaged_copies_merge_into_a_better_picture(SstvModeId id)
    {
        var mode = SstvModeTable.Get(id);
        var seconds = mode.TransmittedLines * mode.LineMs / 1000.0;
        var first = Receive(mode, seconds * 0.3, seed: 1);
        var second = Receive(mode, seconds * 0.7, seed: 2);
        var combiner = new SstvPictureCombiner();

        Assert.Null(combiner.Add(first));
        var combined = combiner.Add(second);

        Assert.NotNull(combined);
        Assert.Equal(2, combined.Copies);
        var a = SstvRoundTripTests.MeanError(mode, first.Image);
        var b = SstvRoundTripTests.MeanError(mode, second.Image);
        var c = SstvRoundTripTests.MeanError(mode, combined.Image);
        output.WriteLine($"{mode.Name}: first {a:0.0}, second {b:0.0}, combined {c:0.0}");
        Assert.True(c < Math.Min(a, b) * 0.8, $"combined {c:0.0}, copies {a:0.0} and {b:0.0}");
    }

    [Fact]
    public void A_different_picture_is_not_merged()
    {
        var mode = SstvModeTable.Robot36;
        var combiner = new SstvPictureCombiner();
        combiner.Add(Receive(mode, 1000));
        SstvTestEncoder.PixelSource other = (x, y) => ((x * 7 + y * 3) % 256, (y * 5) % 256, 255 - (x % 256));

        Assert.Null(combiner.Add(Receive(mode, 1000, other, seed: 3)));
    }

    [Fact]
    public void A_copy_placed_a_few_lines_off_is_lined_up()
    {
        var mode = SstvModeTable.Robot36;
        var first = Receive(mode, 1000);
        var shifted = Receive(mode, 1000, seed: 4);
        shifted.Image.ShiftDown(12);

        // Either order: the merged picture is drawn in the rows of the newest copy.
        var combiner = new SstvPictureCombiner();
        combiner.Add(shifted);
        var combined = combiner.Add(first);
        Assert.NotNull(combined);
        var error = SstvRoundTripTests.MeanError(mode, combined.Image);
        output.WriteLine($"error {error:0.0}, one copy {SstvRoundTripTests.MeanError(mode, first.Image):0.0}");
        Assert.True(error < SstvRoundTripTests.MeanError(mode, first.Image) + 1, $"error {error:0.0}");
    }
}
