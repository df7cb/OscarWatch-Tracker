using OscarWatch.Core.Models;
using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class TerminalTextAndProfileTests
{
    [Theory]
    [InlineData("JO45", "JO45")]
    [InlineData("rr jn70 qsl?", "JN70")]
    [InlineData("IO85TS hi", "IO85ts")]
    [InlineData("RR73", null)]
    [InlineData("599 hw?", null)]
    public void Finds_grid(string text, string? expected)
    {
        Assert.Equal(expected, TerminalText.FindGrid(text));
    }

    [Theory]
    [InlineData("599 hw?", "599")]
    [InlineData("R599", "599")]
    [InlineData("JO45 57", "57")]
    [InlineData("73", null)]
    public void Finds_report(string text, string? expected)
    {
        Assert.Equal(expected, TerminalText.FindReport(text));
    }

    [Theory]
    [InlineData("R73", true)]
    [InlineData("RR73", true)]
    [InlineData("R 599", true)]
    [InlineData("R599", true)]
    [InlineData("599 hw?", false)]
    [InlineData("JO45", false)]
    public void Detects_roger(string text, bool expected)
    {
        Assert.Equal(expected, TerminalText.ContainsRoger(text));
    }

    [Theory]
    [InlineData("73 TU", true)]
    [InlineData("LOGGED 73", true)]
    [InlineData("JO45", false)]
    public void Detects_sign_off(string text, bool expected)
    {
        Assert.Equal(expected, TerminalText.ContainsSignOff(text));
    }

    [Fact]
    public void Base_callsign_strips_ssid_and_repeated_marker()
    {
        Assert.Equal("G0ABC", TerminalText.BaseCallsign("g0abc-7"));
        Assert.Equal("RS0ISS", TerminalText.BaseCallsign("RS0ISS*"));
        Assert.True(TerminalText.SameStation("MM9SQL-1", "mm9sql"));
        Assert.False(TerminalText.SameStation("", ""));
    }

    [Fact]
    public void Built_in_profiles_for_iss_and_greencube()
    {
        var iss = TerminalSatelliteProfiles.Resolve("25544", "ISS (ZARYA)", ["Packet"], null);
        Assert.NotNull(iss);
        Assert.Equal(TerminalMessageFormat.Aprs, iss!.Format);
        Assert.Equal("ARISS", iss.Path);
        Assert.Equal("ISS", iss.UhmName);

        var gc = TerminalSatelliteProfiles.Resolve("53106", "GREENCUBE", ["Digipeater"], null);
        Assert.Equal(TerminalMessageFormat.PlainText, gc!.Format);
        Assert.Equal("IO-117", gc.UhmName);
        Assert.Equal(TerminalSatelliteProfiles.ModemHintBpsk1200, gc.ModemHint);
    }

    [Fact]
    public void Generic_profile_for_database_digipeater_modes()
    {
        var p = TerminalSatelliteProfiles.Resolve("62391", "SAT-X", ["Packet (9k6 GFSK/G3RUH)"], null);
        Assert.NotNull(p);
        Assert.Equal(TerminalMessageFormat.Aprs, p!.Format);
        Assert.Equal(TerminalSatelliteProfiles.ModemHintG3ruh9600, p.ModemHint);
        Assert.Equal("SAT-X", p.UhmName);
    }

    [Fact]
    public void No_profile_without_digipeater_mode()
    {
        Assert.Null(TerminalSatelliteProfiles.Resolve("27607", "SO-50", ["FM Voice"], null));
    }

    [Fact]
    public void Override_wins_and_keeps_built_in_names_when_blank()
    {
        var overrides = new List<TerminalSatelliteProfileSettings>
        {
            new() { NoradId = "25544", Format = TerminalMessageFormat.PlainText, Path = "rs0iss" }
        };
        var p = TerminalSatelliteProfiles.Resolve("25544", "ISS", ["Packet"], overrides);
        Assert.Equal(TerminalMessageFormat.PlainText, p!.Format);
        Assert.Equal("RS0ISS", p.Path);
        Assert.Equal("ISS", p.UhmName);
        Assert.False(p.IsBuiltIn);
    }

    [Theory]
    [InlineData(145825, 145825, TerminalLinkType.Simplex)]
    [InlineData(145850, 145825, TerminalLinkType.SameBand)]
    [InlineData(435300, 437400, TerminalLinkType.SameBand)]
    [InlineData(145990, 437800, TerminalLinkType.Crossband)]
    [InlineData(1268000, 435000, TerminalLinkType.Crossband)]
    [InlineData(0, 145825, TerminalLinkType.Unknown)]
    public void Link_type_from_uplink_and_downlink(double uplinkKHz, double downlinkKHz, TerminalLinkType expected) =>
        Assert.Equal(expected, TerminalSatelliteProfiles.ClassifyLink(uplinkKHz, downlinkKHz));

    [Fact]
    public void Link_type_prefers_the_packet_mode_over_voice()
    {
        var modes = new[]
        {
            new SatelliteTransponderMode { Type = "FM Voice", UplinkKHz = 145990, DownlinkKHz = 437800 },
            new SatelliteTransponderMode { Type = "APRS", UplinkKHz = 145825, DownlinkKHz = 145825 }
        };
        Assert.Equal(TerminalLinkType.Simplex, TerminalSatelliteProfiles.ClassifyLink(modes));
        Assert.Equal(TerminalLinkType.Unknown, TerminalSatelliteProfiles.ClassifyLink([]));
    }

    [Fact]
    public void Profile_carries_the_link_type_and_an_override_can_force_it()
    {
        var generic = TerminalSatelliteProfiles.Resolve("62391", "SAT-X", ["Packet"], null, TerminalLinkType.Crossband);
        Assert.Equal(TerminalLinkType.Crossband, generic!.LinkType);

        var iss = TerminalSatelliteProfiles.Resolve("25544", "ISS", ["Packet"], null);
        Assert.Equal(TerminalLinkType.Simplex, iss!.LinkType);

        var overrides = new List<TerminalSatelliteProfileSettings>
        {
            new() { NoradId = "62391", LinkType = TerminalLinkType.SameBand }
        };
        var forced = TerminalSatelliteProfiles.Resolve("62391", "SAT-X", ["Packet"], overrides, TerminalLinkType.Crossband);
        Assert.Equal(TerminalLinkType.SameBand, forced!.LinkType);
    }

    [Theory]
    [InlineData(TerminalSatelliteProfiles.ModemHintAfsk1200, true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData(TerminalSatelliteProfiles.ModemHintBpsk1200, false)]
    [InlineData(TerminalSatelliteProfiles.ModemHintG3ruh9600, true)]
    [InlineData("9k6 GMSK", false)]
    [InlineData("Packet (9k6 GMSK/Skylink)", false)]
    public void Built_in_modem_supports_afsk_and_g3ruh(string? hint, bool expected) =>
        Assert.Equal(expected, TerminalSatelliteProfiles.IsSupportedByBuiltIn(hint));

    [Theory]
    [InlineData("Packet (9k6 GFSK/G3RUH)", true)]
    [InlineData("Digipeater (9k6 FSK)", true)]
    [InlineData("Packet (9k6 APRS)", true)]
    [InlineData("Packet (9k6 GMSK/Skylink)", false)]
    [InlineData("SSDV 9k6 BPSK", false)]
    [InlineData("9k6 BPSK Telemetry", false)]
    public void Mode_type_hint_maps_g3ruh_only_for_fsk_ax25(string modeType, bool g3ruh) =>
        Assert.Equal(g3ruh, TerminalSatelliteProfiles.IsG3ruh9600ModeType(modeType));

    [Fact]
    public void Knacksat_packet_aprs_profile_uses_g3ruh()
    {
        var p = TerminalSatelliteProfiles.Resolve("67683", "KNACKSAT-2", ["Packet (9k6 APRS)"], null);
        Assert.Equal(TerminalSatelliteProfiles.ModemHintG3ruh9600, p!.ModemHint);
    }

    [Fact]
    public void Grbbeta_skylink_stays_unsupported()
    {
        var p = TerminalSatelliteProfiles.Resolve("60237", "GRBBeta", ["Packet (9k6 GMSK/Skylink)"], null);
        Assert.NotNull(p);
        Assert.Equal(TerminalSatelliteProfiles.ModemHintGmsk9600, p!.ModemHint);
        Assert.False(TerminalSatelliteProfiles.IsSupportedByBuiltIn(p.ModemHint));
    }

    [Fact]
    public void Settings_normalise_pads_macros_and_clamps()
    {
        var s = new TerminalSettings { Macros = [new TerminalMacro { Label = "CQ" }], Ssid = 40, FontSize = 2 };
        s.Agwpe.Port = 0;
        s.Normalise();
        Assert.Equal(TerminalSettings.MacroCount, s.Macros.Count);
        Assert.Equal(15, s.Ssid);
        Assert.Equal(10, s.FontSize);
        Assert.Equal(8000, s.Agwpe.Port);
    }

    [Fact]
    public void Settings_normalise_defaults_missing_list_text_sizes_and_clamps_large_ones()
    {
        var s = new TerminalSettings { QsoFontSize = 0, HeardFontSize = 99 };
        s.Normalise();
        Assert.Equal(TerminalSettings.DefaultStationFontSize, s.QsoFontSize);
        Assert.Equal(TerminalSettings.MaxFontSize, s.HeardFontSize);
    }

    [Fact]
    public void Table_layout_keeps_saved_widths_and_clamps_them()
    {
        var widths = TerminalTableLayout.Normalise([5, 70, 2000, 0, 72, 60], TerminalTableLayout.DefaultQsoColumns);
        Assert.Equal([TerminalTableLayout.MinColumnWidth, 70, TerminalTableLayout.MaxColumnWidth, 0, 72, 60], widths);
        Assert.Equal("24,70,600,*,72,60", TerminalTableLayout.ToColumnDefinitions(widths));
    }

    [Fact]
    public void Table_layout_falls_back_to_defaults_when_the_shape_changes()
    {
        Assert.Equal(TerminalTableLayout.DefaultQsoColumns, TerminalTableLayout.Normalise([], TerminalTableLayout.DefaultQsoColumns));
        Assert.Equal(TerminalTableLayout.DefaultQsoColumns,
            TerminalTableLayout.Normalise([88, 60, 44, 50, 72, 0], TerminalTableLayout.DefaultQsoColumns));
    }

    [Fact]
    public void Settings_normalise_clamps_splitter_layout()
    {
        var s = new TerminalSettings { CallersWidth = 10, QsoShare = double.NaN };
        s.Normalise();
        Assert.Equal(TerminalTableLayout.MinCallersWidth, s.CallersWidth);
        Assert.Equal(TerminalTableLayout.DefaultQsoShare, s.QsoShare);
        Assert.Equal(TerminalTableLayout.DefaultHeardColumns, s.HeardColumnWidths);
    }

    [Fact]
    public void Settings_normalise_enforces_minimum_window_size_and_drops_partial_sizes()
    {
        var small = new TerminalSettings { WindowWidth = 300, WindowHeight = 200 };
        small.Normalise();
        Assert.Equal(TerminalSettings.MinWindowWidth, small.WindowWidth);
        Assert.Equal(TerminalSettings.MinWindowHeight, small.WindowHeight);

        var partial = new TerminalSettings { WindowWidth = 1400 };
        partial.Normalise();
        Assert.Null(partial.WindowWidth);
        Assert.Null(partial.WindowHeight);
    }

    [Fact]
    public void Effective_callsign_falls_back_to_ground_station()
    {
        Assert.Equal("MM9SQL", new TerminalSettings().EffectiveCallsign("mm9sql"));
        Assert.Equal("G0ABC", new TerminalSettings { Callsign = "g0abc-2" }.EffectiveCallsign("MM9SQL"));
    }
}
