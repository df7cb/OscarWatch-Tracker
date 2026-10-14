using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class AprsIsFormatterTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Formats_gate_line_with_q_construct()
    {
        var frame = Ax25Frame.CreateUi(
            Ax25Address.Parse("APRS"),
            Ax25Address.Parse("G0ABC-7"),
            [new Ax25Address("RS0ISS", 0, true)],
            ">CQ IO91");

        Assert.Equal("G0ABC-7>APRS,RS0ISS*,qAR,MM9SQL:>CQ IO91", AprsIsFormatter.FormatGateLine(frame, "mm9sql-1"));
    }

    [Fact]
    public void Formats_without_path()
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse("G0ABC"), [], ">hi");
        Assert.Equal("G0ABC>APRS,qAR,MM9SQL:>hi", AprsIsFormatter.FormatGateLine(frame, "MM9SQL"));
    }

    [Theory]
    [InlineData("NOGATE")]
    [InlineData("RFONLY")]
    [InlineData("TCPIP")]
    public void Refuses_no_gate_paths(string element)
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse("G0ABC"), [Ax25Address.Parse(element)], ">x");
        Assert.Null(AprsIsFormatter.FormatGateLine(frame, "MM9SQL"));
    }

    [Fact]
    public void Refuses_third_party_internet_traffic_and_queries()
    {
        var thirdParty = Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse("G0ABC"), [], "}M0XYZ>APRS,TCPIP,G0ABC*:>hi");
        Assert.Null(AprsIsFormatter.FormatGateLine(thirdParty, "MM9SQL"));
        var query = Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse("G0ABC"), [], "?APRS?");
        Assert.Null(AprsIsFormatter.FormatGateLine(query, "MM9SQL"));
    }

    [Fact]
    public void Login_line_uses_minus_one_without_passcode()
    {
        Assert.Equal("user MM9SQL pass -1 vers OscarWatch 1.0", AprsIsFormatter.LoginLine("mm9sql-5", "", "OscarWatch", "1.0"));
        Assert.Equal("user MM9SQL pass 12345 vers OscarWatch 1.0", AprsIsFormatter.LoginLine("MM9SQL", "12345", "OscarWatch", "1.0"));
    }

    [Fact]
    public void Dupe_filter_ignores_path_and_expires()
    {
        var direct = Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse("G0ABC"), [Ax25Address.Parse("ARISS")], ">x");
        var repeated = Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse("G0ABC"), [new Ax25Address("RS0ISS", 0, true)], ">x");
        var filter = new AprsIsDupeFilter();

        Assert.True(filter.TryAccept(AprsIsFormatter.DupeKey(direct), T0));
        Assert.False(filter.TryAccept(AprsIsFormatter.DupeKey(repeated), T0.AddSeconds(5)));
        Assert.True(filter.TryAccept(AprsIsFormatter.DupeKey(repeated), T0.AddSeconds(31)));
    }
}
