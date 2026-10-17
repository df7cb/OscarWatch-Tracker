using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4StationIdentityTests
{
    [Theory]
    [InlineData("", "", Ft4StationGap.Both)]
    [InlineData("   ", "  ", Ft4StationGap.Both)]
    [InlineData("MM9SQL", "", Ft4StationGap.Locator)]
    [InlineData("MM9SQL", "IO", Ft4StationGap.Locator)]
    [InlineData("MM9SQL", "IO,JN47", Ft4StationGap.Locator)]
    [InlineData("MM9SQL", "IO JN47", Ft4StationGap.Locator)]
    [InlineData("", "IO91", Ft4StationGap.Callsign)]
    [InlineData("...", "JN47", Ft4StationGap.Callsign)]
    [InlineData("mm9sql", "io91wm", Ft4StationGap.None)]
    [InlineData("MM9SQL/P", "IO77,IO87", Ft4StationGap.None)]
    public void Gap_names_the_missing_station_field(string callsign, string locator, Ft4StationGap expected)
    {
        Assert.Equal(expected, Ft4StationIdentity.Gap(callsign, locator));
        Assert.Equal(expected == Ft4StationGap.None, Ft4StationIdentity.IsReady(callsign, locator));
    }

    [Fact]
    public void TryNormalize_accepts_a_callsign_and_four_character_locator()
    {
        var ok = Ft4StationIdentity.TryNormalize(
            " mm9sql/p ",
            " io91wm ",
            out var call,
            out var grid,
            out var error,
            out var invalid);

        Assert.True(ok);
        Assert.Equal(Ft4StationIdentityError.None, error);
        Assert.Null(invalid);
        Assert.Equal("MM9SQL/P", call);
        Assert.Equal("IO91WM", grid);
    }

    [Fact]
    public void TryNormalize_asks_for_both_when_the_form_is_empty()
    {
        var ok = Ft4StationIdentity.TryNormalize(
            "",
            "",
            out _,
            out _,
            out var error,
            out _);

        Assert.False(ok);
        Assert.Equal(Ft4StationIdentityError.BothRequired, error);
    }

    [Fact]
    public void TryNormalize_rejects_a_field_only_locator()
    {
        var ok = Ft4StationIdentity.TryNormalize(
            "HB9RYZ",
            "JN",
            out _,
            out _,
            out var error,
            out _);

        Assert.False(ok);
        Assert.Equal(Ft4StationIdentityError.LocatorRequired, error);
    }

    [Fact]
    public void TryNormalize_rejects_an_invalid_locator()
    {
        var ok = Ft4StationIdentity.TryNormalize(
            "HB9RYZ",
            "IO91zz",
            out _,
            out _,
            out var error,
            out var invalid);

        Assert.False(ok);
        Assert.Equal(Ft4StationIdentityError.LocatorInvalid, error);
        Assert.Equal("IO91ZZ", invalid);
    }
}
