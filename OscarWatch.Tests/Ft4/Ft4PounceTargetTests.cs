using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4PounceTargetTests
{
    private const string My = "MM9SQL";

    private static Ft4DecodedMessage Line(
        string text,
        bool echo = false,
        bool transmitted = false,
        bool apriori = false,
        bool rejected = false)
    {
        Ft4MessageCodec.TryParse(text, out var callTo, out var callDe, out var extra);
        return new(DateTime.UtcNow, text, 1500, 0.2f, -8, callTo, callDe, extra, echo, transmitted, apriori, rejected);
    }

    private static Ft4PounceTarget Target(string text)
    {
        Assert.True(Ft4PounceTarget.TryParse(text, out var target));
        return target;
    }

    [Theory]
    [InlineData("IO85", "IO85")]
    [InlineData("io85ab", "IO85AB")]
    [InlineData(" JO01 ", "JO01")]
    public void Grid_text_is_a_grid_target(string text, string expected)
    {
        var target = Target(text);
        Assert.Equal(Ft4PounceTargetKind.Grid, target.Kind);
        Assert.Equal(expected, target.Value);
    }

    [Theory]
    [InlineData("mm0abc", "MM0ABC")]
    [InlineData("G4ABC/P", "G4ABC/P")]
    [InlineData("<K2MO>", "K2MO")]
    public void Callsign_text_is_a_callsign_target(string text, string expected)
    {
        var target = Target(text);
        Assert.Equal(Ft4PounceTargetKind.Callsign, target.Kind);
        Assert.Equal(expected, target.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AB")]
    [InlineData("HELLO")]
    [InlineData("12345")]
    [InlineData("MM0 ABC")]
    public void Invalid_text_does_not_parse(string text)
    {
        Assert.False(Ft4PounceTarget.TryParse(text, out _));
    }

    [Fact]
    public void Callsign_fires_on_a_call_and_grid_without_cq()
    {
        var line = Line("SM/R1CR KP04");
        Assert.True(Target("SM/R1CR").Matches(line, My));
        Assert.True(Target("R1CR").Matches(line, My));
        Assert.True(Target("KP04").Matches(line, My));
    }

    [Fact]
    public void Callsign_fires_on_cq()
    {
        Assert.True(Target("MM0ABC").Matches(Line("CQ MM0ABC IO85"), My));
    }

    [Fact]
    public void Callsign_fires_when_they_call_me()
    {
        Assert.True(Target("MM0ABC").Matches(Line("MM9SQL MM0ABC IO85"), My));
        Assert.True(Target("MM0ABC").Matches(Line("MM9SQL MM0ABC -10"), My));
    }

    [Fact]
    public void Callsign_waits_while_they_work_someone_else()
    {
        var target = Target("MM0ABC");
        var line = Line("G4ABC MM0ABC -05");
        Assert.True(target.IsFrom(line));
        Assert.False(target.Matches(line, My));
    }

    [Fact]
    public void Other_stations_do_not_fire()
    {
        Assert.False(Target("MM0ABC").Matches(Line("CQ G4ABC IO91"), My));
        Assert.False(Target("MM0ABC").Matches(Line("MM0ABC G4ABC IO91"), My));
    }

    [Fact]
    public void Portable_prefixes_and_suffixes_are_ignored()
    {
        Assert.True(Target("MM0ABC").Matches(Line("CQ MM0ABC/P IO85"), My));
        Assert.True(Target("MM0ABC/P").Matches(Line("CQ MM0ABC IO85"), My));
        Assert.True(Target("F/MM0ABC").Matches(Line("CQ MM0ABC IO85"), My));
    }

    [Fact]
    public void Grid_fires_on_cq_or_call_to_me_from_that_field()
    {
        var target = Target("IO85");
        Assert.True(target.Matches(Line("CQ MM0ABC IO85"), My));
        Assert.True(target.Matches(Line("MM9SQL MM0ABC IO85"), My));
        Assert.False(target.Matches(Line("G4ABC MM0ABC IO85"), My));
        Assert.False(target.Matches(Line("CQ G4ABC IO91"), My));
    }

    [Fact]
    public void Six_character_grid_matches_on_the_field()
    {
        Assert.True(Target("IO85ab").Matches(Line("CQ MM0ABC IO85"), My));
    }

    [Fact]
    public void Grid_target_ignores_reports_and_closings()
    {
        var target = Target("IO85");
        Assert.False(target.Matches(Line("MM9SQL MM0ABC RR73"), My));
        Assert.False(target.Matches(Line("MM9SQL MM0ABC -10"), My));
    }

    [Fact]
    public void Echo_tx_and_rejected_lines_do_not_fire()
    {
        var target = Target("MM0ABC");
        Assert.False(target.Matches(Line("CQ MM0ABC IO85", echo: true), My));
        Assert.False(target.Matches(Line("CQ MM0ABC IO85", transmitted: true), My));
        Assert.False(target.Matches(Line("MM9SQL MM0ABC -10", apriori: true, rejected: true), My));
        Assert.True(target.Matches(Line("MM9SQL MM0ABC -10", apriori: true), My));
    }
}
