using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests;

public sealed class TerminalTransmitWindowTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Single_frame_is_delay_plus_airtime_plus_tail()
    {
        var window = new TerminalTransmitWindow();
        var hold = window.Add(60, 1200, txDelayMs: 300, txTailMs: 100, Now);

        var expected = TimeSpan.FromMilliseconds(400) + TerminalTransmitWindow.FrameAirtime(60, 1200);
        Assert.Equal(expected, hold);
    }

    [Fact]
    public void Frame_queued_during_a_transmission_adds_only_its_airtime()
    {
        var window = new TerminalTransmitWindow();
        var first = window.Add(60, 1200, 300, 100, Now);
        var second = window.Add(60, 1200, 300, 100, Now + TimeSpan.FromMilliseconds(50));

        var airtime = TerminalTransmitWindow.FrameAirtime(60, 1200);
        Assert.Equal(first + airtime - TimeSpan.FromMilliseconds(50), second);
    }

    [Fact]
    public void Frame_after_the_transmission_ended_starts_a_new_delay()
    {
        var window = new TerminalTransmitWindow();
        var first = window.Add(60, 1200, 300, 100, Now);
        var second = window.Add(60, 1200, 300, 100, Now + TimeSpan.FromSeconds(10));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Airtime_at_1200_baud_is_about_half_a_second_for_sixty_bytes()
    {
        var airtime = TerminalTransmitWindow.FrameAirtime(60, 1200);
        Assert.InRange(airtime.TotalMilliseconds, 450, 520);
    }

    [Theory]
    [InlineData(TerminalSatelliteProfiles.ModemHintAfsk1200, 1200)]
    [InlineData(TerminalSatelliteProfiles.ModemHintBpsk1200, 1200)]
    [InlineData(TerminalSatelliteProfiles.ModemHintG3ruh9600, 9600)]
    [InlineData(null, 1200)]
    public void Baud_follows_the_modem_hint(string? hint, int baud) =>
        Assert.Equal(baud, TerminalTransmitWindow.BaudForModemHint(hint));
}
