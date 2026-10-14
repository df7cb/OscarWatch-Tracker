using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class Ax25FrameTests
{
    // MM9SQL>APZOWT,RS0ISS*:>CQ IO85 as received from the ISS digipeater (no FCS).
    private static readonly byte[] IssDigipeatedFrame =
    [
        0x82, 0xA0, 0xB4, 0x9E, 0xAE, 0xA8, 0xE0, // APZOWT, C bit set
        0x9A, 0x9A, 0x72, 0xA6, 0xA2, 0x98, 0x60, // MM9SQL
        0xA4, 0xA6, 0x60, 0x92, 0xA6, 0xA6, 0xE1, // RS0ISS, H bit and end bit
        0x03, 0xF0,
        (byte)'>', (byte)'C', (byte)'Q', (byte)' ', (byte)'I', (byte)'O', (byte)'8', (byte)'5'
    ];

    [Fact]
    public void Parses_captured_iss_frame_with_repeated_bit()
    {
        Assert.True(Ax25Frame.TryParse(IssDigipeatedFrame, out var frame));
        Assert.NotNull(frame);
        Assert.Equal("APZOWT", frame!.Destination.Text);
        Assert.Equal("MM9SQL", frame.Source.Text);
        Assert.Single(frame.Digipeaters);
        Assert.Equal("RS0ISS", frame.Digipeaters[0].Callsign);
        Assert.True(frame.Digipeaters[0].HasBeenRepeated);
        Assert.True(frame.HasBeenDigipeated);
        Assert.True(frame.IsUi);
        Assert.Equal(">CQ IO85", frame.InfoText);
        Assert.Equal("MM9SQL>APZOWT,RS0ISS*:>CQ IO85", frame.ToMonitorString());
    }

    [Fact]
    public void Encode_matches_captured_bytes()
    {
        var frame = Ax25Frame.CreateUi(
            Ax25Address.Parse("APZOWT"),
            Ax25Address.Parse("MM9SQL"),
            [new Ax25Address("RS0ISS", 0, HasBeenRepeated: true)],
            ">CQ IO85");

        Assert.Equal(IssDigipeatedFrame, frame.Encode());
    }

    [Fact]
    public void Round_trips_ssid_and_multiple_digipeaters()
    {
        var frame = Ax25Frame.CreateUi(
            Ax25Address.Parse("CQ"),
            Ax25Address.Parse("G0ABC-7"),
            Ax25Frame.ParsePath("WIDE1-1,WIDE2-2"),
            "JO45");

        Assert.True(Ax25Frame.TryParse(frame.Encode(), out var parsed));
        Assert.Equal("G0ABC-7", parsed!.Source.Text);
        Assert.Equal(7, parsed.Source.Ssid);
        Assert.Equal(["WIDE1-1", "WIDE2-2"], parsed.Digipeaters.Select(d => d.Text));
        Assert.False(parsed.HasBeenDigipeated);
        Assert.Equal("G0ABC-7>CQ,WIDE1-1,WIDE2-2:JO45", parsed.ToMonitorString());
    }

    [Fact]
    public void Monitor_string_marks_only_last_repeated_digipeater()
    {
        var frame = Ax25Frame.CreateUi(
            Ax25Address.Parse("APRS"),
            Ax25Address.Parse("G0ABC"),
            [new Ax25Address("WIDE1", 1, true), new Ax25Address("WIDE2", 1, true), new Ax25Address("WIDE3", 1)],
            "x");

        Assert.Equal("WIDE1-1,WIDE2-1*,WIDE3-1", frame.PathText());
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOOLONGCALL")]
    [InlineData("G0ABC-16")]
    [InlineData("G0/ABC")]
    public void Rejects_invalid_addresses(string text)
    {
        Assert.False(Ax25Address.TryParse(text, out _));
    }

    [Fact]
    public void Rejects_truncated_frames()
    {
        Assert.False(Ax25Frame.TryParse(IssDigipeatedFrame.AsSpan(0, 10), out _));
        Assert.False(Ax25Frame.TryParse(IssDigipeatedFrame.AsSpan(0, 21), out _));
    }

    [Fact]
    public void Info_text_replaces_control_characters_and_trims_line_endings()
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("CQ"), Ax25Address.Parse("G0ABC"), [], "A\tB\r\n");
        Assert.Equal("A B", frame.InfoText);
    }
}
