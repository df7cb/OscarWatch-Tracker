using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class RecentSampleBufferTests
{
    [Fact]
    public void Returns_only_what_was_written_oldest_first()
    {
        var buffer = new RecentSampleBuffer(8);
        buffer.Write([1, 2, 3]);

        var dest = new float[8];
        Assert.Equal(3, buffer.CopyNewest(dest));
        Assert.Equal([1f, 2f, 3f], dest[..3]);
    }

    [Fact]
    public void Wraps_and_keeps_the_newest_samples()
    {
        var buffer = new RecentSampleBuffer(4);
        buffer.Write([1, 2, 3]);
        buffer.Write([4, 5, 6]);

        var dest = new float[4];
        Assert.Equal(4, buffer.CopyNewest(dest));
        Assert.Equal([3f, 4f, 5f, 6f], dest);

        var two = new float[2];
        Assert.Equal(2, buffer.CopyNewest(two));
        Assert.Equal([5f, 6f], two);
    }

    [Fact]
    public void A_write_longer_than_the_buffer_keeps_its_tail()
    {
        var buffer = new RecentSampleBuffer(3);
        buffer.Write([1, 2, 3, 4, 5]);

        var dest = new float[3];
        buffer.CopyNewest(dest);
        Assert.Equal([3f, 4f, 5f], dest);
    }

    [Fact]
    public void Clear_empties_the_buffer()
    {
        var buffer = new RecentSampleBuffer(3);
        buffer.Write([1, 2]);
        buffer.Clear();

        Assert.Equal(0, buffer.CopyNewest(new float[3]));
    }
}
