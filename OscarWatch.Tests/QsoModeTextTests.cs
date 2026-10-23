using OscarWatch.Core.Logbook;

namespace OscarWatch.Tests;

public sealed class QsoModeTextTests
{
    [Fact]
    public void Format_uses_one_token_when_uplink_and_downlink_match()
    {
        Assert.Equal("FM", QsoModeText.Format("FM", "FM"));
        Assert.Equal("FM", QsoModeText.Format("FM", "fm"));
    }

    [Fact]
    public void Format_keeps_both_sides_when_they_differ()
    {
        Assert.Equal("USB/LSB", QsoModeText.Format("USB", "LSB"));
        Assert.Equal("FM/", QsoModeText.Format("FM", ""));
    }

    [Fact]
    public void Split_copies_a_single_token_onto_both_sides()
    {
        var (uplink, downlink) = QsoModeText.Split("fm");
        Assert.Equal("FM", uplink);
        Assert.Equal("FM", downlink);
    }

    [Fact]
    public void Split_reads_uplink_and_downlink_around_the_slash()
    {
        var (uplink, downlink) = QsoModeText.Split(" usb / lsb ");
        Assert.Equal("USB", uplink);
        Assert.Equal("LSB", downlink);
    }

    [Fact]
    public void Split_fills_a_blank_side_from_the_other()
    {
        var (uplink, downlink) = QsoModeText.Split("USB/");
        Assert.Equal("USB", uplink);
        Assert.Equal("USB", downlink);
    }
}
