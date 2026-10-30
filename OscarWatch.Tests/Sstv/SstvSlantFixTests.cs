using OscarWatch.Core.Sstv;

namespace OscarWatch.Tests.Sstv;

public sealed class SstvSlantFixTests
{
    [Fact]
    public void Marked_edge_ends_up_vertical()
    {
        var image = new SstvImage(320, 256);
        for (var y = 0; y < image.Height; y++)
        {
            var x = (int)Math.Round(40 + 0.25 * y);
            image.SetPixel(x, y, 255, 255, 255);
        }

        var straight = SstvSlantFix.Straighten(image, 40, 0, 40 + 0.25 * 200, 200);

        Assert.NotNull(straight);
        for (var y = 0; y < straight.Height; y++)
        {
            var brightest = Enumerable.Range(0, straight.Width).MaxBy(x => straight.GetPixel(x, y).R);
            Assert.InRange(brightest, 39, 41);
        }
    }

    [Fact]
    public void Pixels_moved_off_one_side_wrap_round()
    {
        var image = new SstvImage(100, 64);
        for (var y = 0; y < image.Height; y++)
            image.SetPixel(0, y, 255, 0, 0);

        // Leaning one pixel per row to the right moves each row left, so column 0 wraps to the right side.
        var straight = SstvSlantFix.Straighten(image, 10, 0, 70, 60)!;

        Assert.Equal(255, straight.GetPixel(0, 0).R);
        Assert.Equal(255, straight.GetPixel(90, 10).R);
        Assert.Equal(255, straight.GetPixel(50, 50).R);
        Assert.Equal(64 * 255, Enumerable.Range(0, 64).Sum(y => Enumerable.Range(0, 100).Sum(x => straight.GetPixel(x, y).R)));
    }

    [Fact]
    public void Points_on_nearly_the_same_row_give_no_slope()
    {
        var image = new SstvImage(320, 256);

        Assert.Null(SstvSlantFix.Straighten(image, 10, 100, 60, 102));
    }
}
