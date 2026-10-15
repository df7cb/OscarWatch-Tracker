using OscarWatch.Localization;

namespace OscarWatch.Tests.Localization;

public sealed class PttFailureLocalizerTests
{
    [Fact]
    public void Busy_port_uses_the_port_name()
    {
        var text = PttFailureLocalizer.Localize(
            "Serial port in use (COM8). Close the other program using it, or choose a different port.",
            new Echo());

        Assert.Equal("Ptt.PortBusy:COM8", text);
    }

    [Fact]
    public void Missing_port_uses_the_port_name()
    {
        var text = PttFailureLocalizer.Localize(
            "Serial port not found (COM4). Check the USB cable and refresh the port list.",
            new Echo());

        Assert.Equal("Ptt.PortNotFound:COM4", text);
    }

    [Fact]
    public void Other_failures_stay_as_written()
    {
        const string message = "Separate PTT COM port is not configured.";
        Assert.Equal(message, PttFailureLocalizer.Localize(message, new Echo()));
    }

    private sealed class Echo : ILocalizationService
    {
        public string Get(string key) => key;

        public string Get(string key, params object[] args) => key + ":" + string.Join(',', args);
    }
}
