using System.Text.Json;
using System.Text.Json.Serialization;
using OscarWatch.Core.Hardware;
using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal;

public sealed class TerminalBuiltInModemSettingsTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void Defaults_suit_a_simplex_satellite()
    {
        var s = new TerminalBuiltInModemSettings();
        Assert.Equal(TerminalDuplexMode.Auto, s.Duplex);
        Assert.False(s.WaitForClearChannelInFullDuplex);
        Assert.Equal(63, s.Persist);
        Assert.Equal(100, s.SlotTimeMs);
        Assert.Equal(150, s.TurnaroundGuardMs);
        Assert.Equal(AfskBitFixMode.SingleBit, s.BitFix);
        Assert.Equal(10, s.WatchdogSeconds);
        Assert.True(s.BlockTxBelowHorizon);
        Assert.Equal(PttMethod.Vox, s.PttMethod);
    }

    [Fact]
    public void Normalise_clamps_out_of_range_values()
    {
        var s = new TerminalBuiltInModemSettings
        {
            TxLevel = 5,
            Persist = 999,
            SlotTimeMs = 1,
            MaxChannelWaitMs = -5,
            TurnaroundGuardMs = 99999,
            WatchdogSeconds = 0,
            PttLeadMs = -1,
            PttTailMs = 99999,
            Duplex = (TerminalDuplexMode)42,
            BitFix = (AfskBitFixMode)42,
            InputDeviceId = null!,
            SeparatePttPort = " COM7 "
        };

        s.Normalise();

        Assert.Equal(1.0, s.TxLevel);
        Assert.Equal(255, s.Persist);
        Assert.Equal(10, s.SlotTimeMs);
        Assert.Equal(0, s.MaxChannelWaitMs);
        Assert.Equal(TerminalBuiltInModemSettings.MaxTurnaroundGuardMs, s.TurnaroundGuardMs);
        Assert.Equal(TerminalBuiltInModemSettings.MinWatchdogSeconds, s.WatchdogSeconds);
        Assert.Equal(0, s.PttLeadMs);
        Assert.Equal(PttOptions.MaxTimingMs, s.PttTailMs);
        Assert.Equal(TerminalDuplexMode.Auto, s.Duplex);
        Assert.Equal(AfskBitFixMode.SingleBit, s.BitFix);
        Assert.Equal("", s.InputDeviceId);
        Assert.Equal("COM7", s.SeparatePttPort);
    }

    [Fact]
    public void Nan_tx_level_falls_back_to_the_default()
    {
        var s = new TerminalBuiltInModemSettings { TxLevel = double.NaN };
        s.Normalise();
        Assert.Equal(TerminalBuiltInModemSettings.DefaultTxLevel, s.TxLevel);
    }

    [Fact]
    public void Older_settings_without_the_section_get_defaults()
    {
        var terminal = JsonSerializer.Deserialize<TerminalSettings>("""{ "transport": "agwpe" }""", Json)!;
        terminal.BuiltIn = null!;
        terminal.Normalise();
        Assert.NotNull(terminal.BuiltIn);
        Assert.True(terminal.BuiltIn.BlockTxBelowHorizon);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var terminal = new TerminalSettings { Transport = TerminalTransportKind.BuiltIn };
        terminal.BuiltIn.Duplex = TerminalDuplexMode.Half;
        terminal.BuiltIn.BitFix = AfskBitFixMode.SingleAndAdjacentPair;
        terminal.BuiltIn.PttMethod = PttMethod.SeparateComPort;
        terminal.BuiltIn.SeparatePttPort = "COM9";

        var json = JsonSerializer.Serialize(terminal, Json);
        var back = JsonSerializer.Deserialize<TerminalSettings>(json, Json)!;

        Assert.DoesNotContain("activeRadioPort", json, StringComparison.Ordinal);
        Assert.Equal(TerminalTransportKind.BuiltIn, back.Transport);
        Assert.Equal(TerminalDuplexMode.Half, back.BuiltIn.Duplex);
        Assert.Equal(AfskBitFixMode.SingleAndAdjacentPair, back.BuiltIn.BitFix);
        Assert.Equal(new PttOptions(PttMethod.SeparateComPort, PttLine.Rts, false, "COM9", 0, 0), back.BuiltIn.ToPttOptions());
    }

    [Fact]
    public void Active_radio_port_is_zero_for_the_built_in_modem()
    {
        var terminal = new TerminalSettings();
        terminal.Agwpe.RadioPort = 2;
        Assert.Equal(2, terminal.ActiveRadioPort);
        terminal.Transport = TerminalTransportKind.BuiltIn;
        Assert.Equal(0, terminal.ActiveRadioPort);
    }
}
