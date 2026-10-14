using OscarWatch.Terminal;

namespace OscarWatch.Tests.Terminal;

/// <summary>Receive-only smoke test against a real modem; runs when <c>OSCARWATCH_AGWPE_LIVE=host:port</c> is set.</summary>
public sealed class AgwpeLiveModemTests
{
    [Fact]
    public async Task Connects_and_lists_ports_when_live_modem_configured()
    {
        var target = Environment.GetEnvironmentVariable("OSCARWATCH_AGWPE_LIVE");
        if (string.IsNullOrWhiteSpace(target))
            return;

        var parts = target.Split(':');
        await using var client = new AgwpeClient(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 8000, "N0CALL");
        await client.ConnectAsync(CancellationToken.None);
        var ports = await client.GetRadioPortsAsync(CancellationToken.None);

        Assert.Equal(TerminalTransportState.Connected, client.State);
        Assert.NotEmpty(ports);
        Assert.StartsWith("AGWPE ", client.ModemDescription, StringComparison.Ordinal);
    }
}
