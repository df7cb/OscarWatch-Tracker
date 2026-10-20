using OscarWatch.Help;

namespace OscarWatch.Tests;

public class HelpLauncherTests
{
    [Theory]
    [InlineData(null, "https://oscarwatch.org/docs/tracker")]
    [InlineData("", "https://oscarwatch.org/docs/tracker")]
    [InlineData("index.html", "https://oscarwatch.org/docs/tracker")]
    [InlineData("ft4", "https://oscarwatch.org/docs/ft4")]
    [InlineData("satellite-link.html", "https://oscarwatch.org/docs/satellite-link")]
    [InlineData("settings#gps", "https://oscarwatch.org/docs/settings#gps")]
    [InlineData("settings.html#ffmpeg", "https://oscarwatch.org/docs/settings#ffmpeg")]
    [InlineData("Radio-ICOM", "https://oscarwatch.org/docs/radio-icom")]
    public void BuildUrl_maps_pages_to_website_docs(string? page, string expected)
    {
        Assert.Equal(expected, HelpLauncher.BuildUrl(page));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("C:\\help\\index.html")]
    [InlineData("https://example.com/")]
    [InlineData("ft4?x=1")]
    public void BuildUrl_falls_back_to_the_introduction_for_unsafe_input(string page)
    {
        Assert.Equal("https://oscarwatch.org/docs/tracker", HelpLauncher.BuildUrl(page));
    }

    [Fact]
    public void BuildUrl_drops_unsafe_anchors()
    {
        Assert.Equal("https://oscarwatch.org/docs/settings", HelpLauncher.BuildUrl("settings#\"><script>"));
    }
}
