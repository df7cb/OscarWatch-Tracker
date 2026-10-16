using OscarWatch.Core.Display;
using OscarWatch.Core.Geo;
using OscarWatch.Core.Models;
using OscarWatch.Core.Orbit;
using OscarWatch.Core.Radio;
using OscarWatch.Core.Services;
using OscarWatch.Localization;
using OscarWatch.ViewModels;

namespace OscarWatch.Tests;

public class FrequencyOverlayViewModelTests
{
    [Fact]
    public void Mode_switch_persists_offsets_for_new_mode_not_previous_spinner_values()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0,
            RememberOffsets = true
        };
        settings.Current.FrequencySelections["RS-44"].SetOffsetsForMode("SSB Transponder", 0, 4.025);
        settings.Current.FrequencySelections["RS-44"].SetOffsetsForMode("FT4", 0, 0);

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_667,
                        UplinkKHz = 145_937.61,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    },
                    new SatelliteTransponderMode
                    {
                        Type = "FT4",
                        DownlinkKHz = 435_611,
                        UplinkKHz = 145_993.61,
                        DownlinkMode = "DATA-USB",
                        UplinkMode = "DATA-LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "99999",
            Subpoint = new GeoCoordinate(57, 18),
            LookAngles = new LookAngles(180, 25, 800, 2.5)
        });

        Assert.Equal("SSB Transponder", vm.SelectedMode?.Type);
        Assert.InRange(vm.ReceiveOffsetKHz, 4.024, 4.026);

        vm.SelectedMode = vm.AvailableModes.First(m => m.Type == "FT4");

        Assert.InRange(vm.ReceiveOffsetKHz, -0.001, 0.001);
        var stored = settings.Current.FrequencySelections["RS-44"];
        Assert.InRange(stored.ModeOffsets["FT4"].ReceiveOffsetKHz, -0.001, 0.001);
        Assert.InRange(stored.ModeOffsets["SSB Transponder"].ReceiveOffsetKHz, 4.024, 4.026);
    }

    [Fact]
    public void Doppler_strategy_persists_per_mode()
    {
        var settings = new TestSettingsService();
        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_667,
                        UplinkKHz = 145_937.61,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    },
                    new SatelliteTransponderMode
                    {
                        Type = "FT4",
                        DownlinkKHz = 435_611,
                        UplinkKHz = 145_993.61,
                        DownlinkMode = "DATA-USB",
                        UplinkMode = "DATA-LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "99999",
            Subpoint = new GeoCoordinate(57, 18),
            LookAngles = new LookAngles(180, 25, 800, 2.5)
        });

        Assert.True(vm.ShowDopplerStrategyRow);
        Assert.True(vm.IsFullDopplerSelected);

        vm.SetDopplerStrategy(DopplerStrategy.DownlinkOnly);
        Assert.True(vm.IsTxFixedDopplerSelected);

        vm.SelectedMode = vm.AvailableModes.First(m => m.Type == "FT4");
        Assert.True(vm.IsFullDopplerSelected);

        vm.SelectedMode = vm.AvailableModes.First(m => m.Type == "SSB Transponder");
        Assert.True(vm.IsTxFixedDopplerSelected);

        var stored = settings.Current.FrequencySelections["RS-44"];
        Assert.Equal(DopplerStrategy.DownlinkOnly, stored.GetDopplerStrategyForMode("SSB Transponder"));
        Assert.Equal(DopplerStrategy.Full, stored.GetDopplerStrategyForMode("FT4"));
    }

    [Fact]
    public void EnsureOverlayWithinHost_clamps_position_using_measured_overlay_size()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencyOverlayX = 500;
        settings.Current.FrequencyOverlayY = 900;
        var database = new TestSatelliteDatabaseService([]);
        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);

        vm.EnsureOverlayWithinHost(800, 600, 400, 300);

        Assert.InRange(vm.OverlayX, 8, 800 - 400 - 8);
        Assert.InRange(vm.OverlayY, 8, 600 - 300 - 8);
        Assert.Equal(392, vm.OverlayX, precision: 0);
        Assert.Equal(292, vm.OverlayY, precision: 0);
        Assert.Equal(500, settings.Current.FrequencyOverlayX);
        Assert.Equal(900, settings.Current.FrequencyOverlayY);
    }

    [Fact]
    public void PersistOverlayPosition_writes_current_display_coordinates()
    {
        var settings = new TestSettingsService();
        var database = new TestSatelliteDatabaseService([]);
        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);

        vm.SetOverlayPosition(240, 180);
        vm.PersistOverlayPosition();

        Assert.Equal(240, settings.Current.FrequencyOverlayX);
        Assert.Equal(180, settings.Current.FrequencyOverlayY);
    }

    [Fact]
    public void ReloadLayoutFromSettings_restores_saved_overlay_layout()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencyOverlayX = 320;
        settings.Current.FrequencyOverlayY = 140;
        settings.Current.FrequencyOverlayCollapsed = true;
        var database = new TestSatelliteDatabaseService([]);
        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);

        vm.SetOverlayPosition(12, 12);
        vm.ReloadLayoutFromSettings();

        Assert.Equal(320, vm.OverlayX);
        Assert.Equal(140, vm.OverlayY);
        Assert.True(vm.IsCollapsed);
    }

    [Fact]
    public void ToggleCollapse_persists_and_updates_compact_summary()
    {
        var settings = new TestSettingsService();
        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "JO-97",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_475,
                        UplinkKHz = 145_920,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        Assert.False(vm.IsCollapsed);

        vm.Update(new SatelliteTrackState
        {
            Name = "JO-97",
            NoradId = "1",
            Subpoint = new GeoCoordinate(52, -4),
            LookAngles = new LookAngles(180, 25, 800, 0)
        });

        Assert.Contains("JO-97", vm.CollapsedSummaryText, StringComparison.Ordinal);
        Assert.Contains("SSB Transponder", vm.CollapsedSummaryText, StringComparison.Ordinal);
        Assert.Contains("/", vm.CollapsedSummaryText, StringComparison.Ordinal);

        vm.ToggleCollapseCommand.Execute(null);
        Assert.True(vm.IsCollapsed);
        Assert.True(settings.Current.FrequencyOverlayCollapsed);
        Assert.Equal("▶", vm.CollapseToggleGlyph);

        vm.ToggleCollapseCommand.Execute(null);
        Assert.False(vm.IsCollapsed);
        Assert.False(settings.Current.FrequencyOverlayCollapsed);
        Assert.Equal("▼", vm.CollapseToggleGlyph);
    }

    [Fact]
    public void Cw_uplink_toggle_persists_per_mode_and_updates_rig_context()
    {
        var settings = new TestSettingsService();
        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "JO-97",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_475,
                        UplinkKHz = 145_920,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(new SatelliteTrackState
        {
            Name = "JO-97",
            NoradId = "1",
            Subpoint = new GeoCoordinate(52, -4),
            LookAngles = new LookAngles(180, 25, 800, 0)
        });

        Assert.True(vm.ShowOperatingStyleRow);
        Assert.False(vm.IsCwUplink);

        settings.Current.Rig = new RigSettings { CwKeepSidebandDownlink = true };

        vm.SetCwUplink(true);
        Assert.True(vm.IsCwUplink);
        Assert.Contains("· CW", vm.CollapsedSummaryText, StringComparison.Ordinal);
        Assert.Contains("USB/LSB", vm.OperatingStyleHint, StringComparison.OrdinalIgnoreCase);

        var ctx = vm.TryBuildRigTrackingContext(new SatelliteTrackState
        {
            Name = "JO-97",
            NoradId = "1",
            Subpoint = new GeoCoordinate(52, -4),
            LookAngles = new LookAngles(180, 25, 800, 0)
        });
        Assert.NotNull(ctx);
        Assert.Equal("CW", ctx.EffectiveUplinkMode);
        Assert.Equal("USB", ctx.EffectiveDownlinkMode);

        Assert.True(settings.Current.FrequencySelections["JO-97"].GetCwUplinkForMode("SSB Transponder"));
    }

    [Fact]
    public void Cw_operating_style_uses_separate_stored_receive_offset()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["JO-97"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };
        settings.Current.FrequencySelections["JO-97"].SetOffsetsForMode("SSB Transponder", 0, 2.0);
        settings.Current.FrequencySelections["JO-97"].CwReceiveOffsetKHzByMode["SSB Transponder"] = -1.5;

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "JO-97",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_475,
                        UplinkKHz = 145_920,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(new SatelliteTrackState
        {
            Name = "JO-97",
            NoradId = "1",
            Subpoint = new GeoCoordinate(52, -4),
            LookAngles = new LookAngles(180, 25, 800, 0)
        });

        Assert.InRange(vm.ReceiveOffsetKHz, 1.999, 2.001);

        vm.SetCwUplink(true);
        Assert.InRange(vm.ReceiveOffsetKHz, -1.501, -1.499);

        vm.SetCwUplink(false);
        Assert.InRange(vm.ReceiveOffsetKHz, 1.999, 2.001);
    }

    [Fact]
    public void Store_offset_in_cw_mode_persists_cw_receive_offset_without_changing_voice()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["JO-97"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };
        settings.Current.FrequencySelections["JO-97"].SetOffsetsForMode("SSB Transponder", 0, 2.0);

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "JO-97",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_475,
                        UplinkKHz = 145_920,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(new SatelliteTrackState
        {
            Name = "JO-97",
            NoradId = "1",
            Subpoint = new GeoCoordinate(52, -4),
            LookAngles = new LookAngles(180, 25, 800, 0)
        });

        vm.SetCwUplink(true);
        vm.AdjustReceiveOffsetHz(-500);
        vm.StoreOffsetCommand.Execute(null);

        var stored = settings.Current.FrequencySelections["JO-97"];
        Assert.InRange(stored.CwReceiveOffsetKHzByMode["SSB Transponder"], 1.499, 1.501);
        Assert.InRange(stored.ModeOffsets["SSB Transponder"].ReceiveOffsetKHz, 1.999, 2.001);
        Assert.Contains("CW", vm.OffsetAppliedHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Offset_hint_updates_when_another_satellite_is_selected()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.DefaultLanguage);
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0,
            RememberOffsets = true
        };
        settings.Current.FrequencySelections["RS-44"].SetOffsetsForMode("SSB Transponder", 0, -3);
        settings.Current.FrequencySelections["JO-97"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0,
            RememberOffsets = true
        };
        settings.Current.FrequencySelections["JO-97"].SetOffsetsForMode("SSB Transponder", 0, 1.5);

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes = [ReverseSsbMode()]
            },
            new SatelliteRadioEntry
            {
                Name = "JO-97",
                Modes = [ReverseSsbMode()]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "44909",
            Subpoint = new GeoCoordinate(57, 18),
            LookAngles = new LookAngles(180, 25, 800, 0)
        });

        Assert.Contains("-3.000", vm.OffsetAppliedHint, StringComparison.Ordinal);
        Assert.Contains("Tune Main for uplink", vm.OffsetAppliedHint, StringComparison.Ordinal);

        vm.Update(new SatelliteTrackState
        {
            Name = "JO-97",
            NoradId = "43803",
            Subpoint = new GeoCoordinate(52, -4),
            LookAngles = new LookAngles(90, 20, 600, 0)
        });

        Assert.Contains("+1.500", vm.OffsetAppliedHint, StringComparison.Ordinal);
        Assert.DoesNotContain("-3.000", vm.OffsetAppliedHint, StringComparison.Ordinal);
        Assert.InRange(vm.ReceiveOffsetKHz, 1.499, 1.501);

        vm.StoreOffsetCommand.Execute(null);
        var storedHint = vm.OffsetAppliedHint;
        Assert.Contains("Stored", storedHint, StringComparison.Ordinal);

        vm.Update(new SatelliteTrackState
        {
            Name = "JO-97",
            NoradId = "43803",
            Subpoint = new GeoCoordinate(52, -4),
            LookAngles = new LookAngles(91, 21, 600, 0.1)
        });

        Assert.Equal(storedHint, vm.OffsetAppliedHint);
    }

    [Fact]
    public void Offset_adjustment_does_not_persist_until_store()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };
        settings.Current.FrequencySelections["RS-44"].SetOffsetsForMode("SSB Transponder", 0, 1.0);

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_667,
                        UplinkKHz = 145_937.61,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "99999",
            Subpoint = new GeoCoordinate(57, 18),
            LookAngles = new LookAngles(180, 25, 800, 2.5)
        });

        vm.AdjustReceiveOffsetHz(500);
        Assert.InRange(vm.ReceiveOffsetKHz, 1.499, 1.501);
        Assert.InRange(
            settings.Current.FrequencySelections["RS-44"].ModeOffsets["SSB Transponder"].ReceiveOffsetKHz,
            0.999,
            1.001);

        vm.StoreOffsetCommand.Execute(null);
        Assert.InRange(
            settings.Current.FrequencySelections["RS-44"].ModeOffsets["SSB Transponder"].ReceiveOffsetKHz,
            1.499,
            1.501);
    }

    [Fact]
    public void Offset_toggle_swaps_spinner_value_without_losing_stored_legs()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };
        settings.Current.FrequencySelections["RS-44"].SetOffsetsForMode("SSB Transponder", 1.5, 2.0);

        var database = CreateRs44Database();
        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(Rs44TrackState());

        Assert.InRange(vm.ActiveOffsetKHz, 1.999, 2.001);
        Assert.False(vm.IsTransmitOffsetSelected);

        vm.IsTransmitOffsetSelected = true;
        Assert.InRange(vm.ActiveOffsetKHz, 1.499, 1.501);
        Assert.InRange(vm.ReceiveOffsetKHz, 1.999, 2.001);

        vm.IsTransmitOffsetSelected = false;
        Assert.InRange(vm.ActiveOffsetKHz, 1.999, 2.001);
    }

    [Fact]
    public void Store_rx_offset_preserves_existing_tx_offset()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };
        settings.Current.FrequencySelections["RS-44"].SetOffsetsForMode("SSB Transponder", 1.25, 0);

        var vm = new FrequencyOverlayViewModel(settings, CreateRs44Database(), LocalizationService.Instance);
        vm.Update(Rs44TrackState());
        vm.ReceiveOffsetKHz = 3.0;
        vm.StoreOffsetCommand.Execute(null);

        var stored = settings.Current.FrequencySelections["RS-44"].ModeOffsets["SSB Transponder"];
        Assert.InRange(stored.ReceiveOffsetKHz, 2.999, 3.001);
        Assert.InRange(stored.TransmitOffsetKHz, 1.249, 1.251);
    }

    [Fact]
    public void Rx_offset_accepts_and_stores_large_correction_for_wide_passbands()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };

        var vm = new FrequencyOverlayViewModel(settings, CreateRs44Database(), LocalizationService.Instance);
        vm.Update(Rs44TrackState());
        vm.ReceiveOffsetKHz = 10.5;
        vm.StoreOffsetCommand.Execute(null);

        Assert.InRange(vm.ReceiveOffsetKHz, 10.499, 10.501);
        var stored = settings.Current.FrequencySelections["RS-44"].ModeOffsets["SSB Transponder"];
        Assert.InRange(stored.ReceiveOffsetKHz, 10.499, 10.501);
    }

    [Fact]
    public void Tx_offset_accepts_and_stores_large_correction_for_wide_passbands()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };

        var vm = new FrequencyOverlayViewModel(settings, CreateRs44Database(), LocalizationService.Instance);
        vm.Update(Rs44TrackState());
        vm.IsTransmitOffsetSelected = true;
        vm.TransmitOffsetKHz = 10.5;
        vm.StoreOffsetCommand.Execute(null);

        Assert.InRange(vm.TransmitOffsetKHz, 10.499, 10.501);
        var stored = settings.Current.FrequencySelections["RS-44"].ModeOffsets["SSB Transponder"];
        Assert.InRange(stored.TransmitOffsetKHz, 10.499, 10.501);
    }

    [Fact]
    public void Active_offset_clamps_at_twenty_kHz()
    {
        var vm = new FrequencyOverlayViewModel(new TestSettingsService(), CreateRs44Database(), LocalizationService.Instance);
        vm.Update(Rs44TrackState());

        vm.ReceiveOffsetKHz = 25.0;
        Assert.Equal(FrequencyOverlayViewModel.OffsetMaxKHz, vm.ReceiveOffsetKHz);

        vm.AdjustReceiveOffsetHz(5000);
        Assert.Equal(FrequencyOverlayViewModel.OffsetMaxKHz, vm.ReceiveOffsetKHz);

        vm.ReceiveOffsetKHz = -25.0;
        Assert.Equal(FrequencyOverlayViewModel.OffsetMinKHz, vm.ReceiveOffsetKHz);

        vm.IsTransmitOffsetSelected = true;
        vm.TransmitOffsetKHz = 25.0;
        Assert.Equal(FrequencyOverlayViewModel.OffsetMaxKHz, vm.TransmitOffsetKHz);

        vm.AdjustActiveOffsetHz(5000);
        Assert.Equal(FrequencyOverlayViewModel.OffsetMaxKHz, vm.TransmitOffsetKHz);

        vm.TransmitOffsetKHz = -25.0;
        Assert.Equal(FrequencyOverlayViewModel.OffsetMinKHz, vm.TransmitOffsetKHz);
    }

    [Fact]
    public void Store_tx_offset_preserves_existing_rx_offset()
    {
        var settings = new TestSettingsService();
        settings.Current.FrequencySelections["RS-44"] = new SatelliteFrequencySelection
        {
            ModeType = "SSB Transponder",
            ModeIndex = 0
        };
        settings.Current.FrequencySelections["RS-44"].SetOffsetsForMode("SSB Transponder", 0, 2.0);

        var vm = new FrequencyOverlayViewModel(settings, CreateRs44Database(), LocalizationService.Instance);
        vm.Update(Rs44TrackState());
        vm.IsTransmitOffsetSelected = true;
        vm.TransmitOffsetKHz = -0.75;
        vm.StoreOffsetCommand.Execute(null);

        var stored = settings.Current.FrequencySelections["RS-44"].ModeOffsets["SSB Transponder"];
        Assert.InRange(stored.TransmitOffsetKHz, -0.751, -0.749);
        Assert.InRange(stored.ReceiveOffsetKHz, 1.999, 2.001);
    }

    [Fact]
    public void Lead_tuning_panel_toggles_with_l_and_applies_live_without_persist()
    {
        var settings = new TestSettingsService();
        settings.Current.Rig.DopplerCatLeadEnabled = true;
        settings.Current.Rig.DopplerCatLeadGainPercent = 100;
        settings.Current.Rig.DopplerCatLeadMs = 0;

        var vm = new FrequencyOverlayViewModel(settings, CreateRs44Database(), LocalizationService.Instance);
        vm.Update(Rs44TrackState());

        Assert.False(vm.ShowLeadTuningPanel);
        Assert.False(vm.ShowLeadTuningPanelVisible);

        vm.ToggleLeadTuningPanel();
        Assert.True(vm.ShowLeadTuningPanel);
        Assert.True(vm.ShowLeadTuningPanelVisible);
        Assert.Equal(100, vm.LiveLeadGainPercent);
        Assert.Equal(0, vm.LiveLeadMs);

        vm.LiveLeadGainPercent = 75;
        Assert.Equal(75, settings.Current.Rig.DopplerCatLeadGainPercent);

        vm.ToggleLeadTuningPanel();
        Assert.False(vm.ShowLeadTuningPanel);
        Assert.Equal(75, settings.Current.Rig.DopplerCatLeadGainPercent);
    }

    [Fact]
    public void Both_offsets_shift_displayed_frequencies()
    {
        var vm = new FrequencyOverlayViewModel(new TestSettingsService(), CreateRs44Database(), LocalizationService.Instance);
        vm.Update(Rs44TrackState());
        vm.ReceiveOffsetKHz = 1.0;
        vm.TransmitOffsetKHz = 0.5;
        vm.RefreshFrequencyDisplay();

        var baseline = DopplerFrequencyCalculator.Compute(vm.SelectedMode!, 0, 0);
        var both = DopplerFrequencyCalculator.Compute(vm.SelectedMode!, 0, 1.0, transmitOffsetKHz: 0.5);

        Assert.InRange(both.RadioReceiveKHz - baseline.RadioReceiveKHz, 0.9, 1.1);
        Assert.InRange(both.RadioTransmitKHz - baseline.RadioTransmitKHz, 0.4, 0.6);
    }

    [Fact]
    public void Doppler_lead_indicator_reflects_setting_and_active_blend()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.DefaultLanguage);
        var settings = new TestSettingsService();
        settings.Current.Rig.CatDelayMs = 100;

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_667,
                        UplinkKHz = 145_937.61,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var state = new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "99999",
            Subpoint = new GeoCoordinate(0, 0, 400),
            LookAngles = new LookAngles(180, 30, 800, -1.0)
        };

        settings.Current.Rig.DopplerCatLeadEnabled = false;
        var vm = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        vm.Update(state);
        Assert.False(vm.ShowDopplerLeadIndicator);
        Assert.False(vm.IsDopplerLeadActive);

        settings.Current.Rig.DopplerCatLeadEnabled = true;
        var withLead = new FrequencyOverlayViewModel(
            settings,
            database,
            LocalizationService.Instance,
            new LeadRatePropagator(slopeRate: 0.5, leadRate: 3.5));
        withLead.Update(state);

        Assert.True(withLead.ShowDopplerLeadIndicator);
        Assert.True(withLead.IsDopplerLeadActive);
        Assert.Contains("Lead Doppler", withLead.DopplerLeadToolTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Doppler_lead_indicator_hidden_below_horizon_even_when_blend_active()
    {
        var settings = new TestSettingsService();
        settings.Current.Rig.CatDelayMs = 100;
        settings.Current.Rig.DopplerCatLeadEnabled = true;

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_667,
                        UplinkKHz = 145_937.61,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var belowHorizon = new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "99999",
            Subpoint = new GeoCoordinate(0, 0, 400),
            LookAngles = new LookAngles(180, -12, 800, 4.5)
        };

        var vm = new FrequencyOverlayViewModel(
            settings,
            database,
            LocalizationService.Instance,
            new LeadRatePropagator(slopeRate: 0.5, leadRate: 3.5));
        vm.Update(belowHorizon);

        Assert.False(vm.ShowDopplerLeadIndicator);
        Assert.False(vm.IsDopplerLeadActive);
        Assert.Equal("", vm.DopplerLeadToolTip);
    }

    [Fact]
    public void Cat_lead_radio_row_differs_from_snapshot_when_lead_rate_differs()
    {
        var settings = new TestSettingsService();
        settings.Current.Rig.CatDelayMs = 100;

        var database = new TestSatelliteDatabaseService(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_667,
                        UplinkKHz = 145_937.61,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

        var state = new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "99999",
            Subpoint = new GeoCoordinate(0, 0, 400),
            LookAngles = new LookAngles(180, 30, 800, -1.0)
        };

        settings.Current.Rig.DopplerCatLeadEnabled = false;
        var withoutLead = new FrequencyOverlayViewModel(settings, database, LocalizationService.Instance);
        withoutLead.Update(state);

        settings.Current.Rig.DopplerCatLeadEnabled = true;
        var withLead = new FrequencyOverlayViewModel(
            settings,
            database,
            LocalizationService.Instance,
            new LeadRatePropagator(slopeRate: 0.5, leadRate: 3.5));
        withLead.Update(state);

        Assert.NotEqual(withoutLead.RadioReceiveText, withLead.RadioReceiveText);
        Assert.Equal(withoutLead.SatelliteReceiveText, withLead.SatelliteReceiveText);
    }

    [Fact]
    public void Radio_row_matches_rig_doppler_calculator_with_lead_and_passband()
    {
        var settings = new TestSettingsService();
        settings.Current.Rig.DopplerCatLeadEnabled = true;
        settings.Current.Rig.DopplerCatLeadMs = 40;
        settings.Current.Rig.DopplerCatLeadGainPercent = 100;
        settings.Current.Rig.CatDelayMs = 50;

        var state = Rs44TrackState();
        var propagator = new LeadRatePropagator(slopeRate: 0.5, leadRate: 3.5);
        var vm = new FrequencyOverlayViewModel(settings, CreateRs44Database(), LocalizationService.Instance, propagator);
        vm.Update(state);
        vm.ReceiveOffsetKHz = -1.2;
        vm.TransmitOffsetKHz = -1.5;
        // First satellite focus suspends passband sync until the rig reports a cleared trim.
        vm.SyncRigPassbandAdjustments(0, 0);
        vm.SyncRigPassbandAdjustments(-10.222, 10.222);

        var utc = DateTime.UtcNow;
        var lead = DopplerCatLead.ResolveRangeRates(
            propagator,
            settings.Current.Rig,
            settings.Current.GroundStation,
            state,
            utc);
        var expected = DopplerFrequencyCalculator.Compute(
            vm.SelectedMode!,
            lead.RxRangeRateKmPerSec,
            vm.ReceiveOffsetKHz,
            vm.TransmitOffsetKHz,
            -10.222,
            10.222,
            vm.DopplerStrategy,
            lead.TxRangeRateKmPerSec);

        Assert.Equal(
            FrequencyDisplayFormat.FormatMHz(expected.RadioReceiveKHz),
            vm.RadioReceiveText);
        Assert.Equal(
            FrequencyDisplayFormat.FormatMHz(expected.RadioTransmitKHz),
            vm.RadioTransmitText);
    }

    [Fact]
    public void CanReportSatelliteStatus_requires_enabled_token_sat_and_mode()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.DefaultLanguage);
        var settings = new TestSettingsService();
        var vm = new FrequencyOverlayViewModel(settings, CreateRs44Database(), LocalizationService.Instance);

        Assert.False(vm.CanReportSatelliteStatus);
        Assert.False(vm.ReportSatelliteStatusCommand.CanExecute(null));

        settings.Current.SatelliteStatus = new SatelliteStatusSettings
        {
            Enabled = true,
            ApiToken = "token"
        };
        vm.RefreshSatelliteStatusReportAvailability();
        Assert.False(vm.CanReportSatelliteStatus);

        vm.Update(new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "44909",
            Subpoint = new GeoCoordinate(57, 18),
            LookAngles = new LookAngles(180, 25, 800, 0)
        });

        Assert.True(vm.CanReportSatelliteStatus);
        Assert.True(vm.ReportSatelliteStatusCommand.CanExecute(null));

        settings.Current.SatelliteStatus.ApiToken = "";
        vm.RefreshSatelliteStatusReportAvailability();
        Assert.False(vm.CanReportSatelliteStatus);

        settings.Current.SatelliteStatus.ApiToken = "token";
        settings.Current.SatelliteStatus.Enabled = false;
        vm.RefreshSatelliteStatusReportAvailability();
        Assert.False(vm.CanReportSatelliteStatus);

        settings.Current.SatelliteStatus.Enabled = true;
        vm.Update(new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "44909",
            Subpoint = new GeoCoordinate(57, 18),
            LookAngles = new LookAngles(180, -1.0, 800, 0)
        });
        Assert.True(vm.CanReportSatelliteStatus);

        vm.Update(new SatelliteTrackState
        {
            Name = "RS-44",
            NoradId = "44909",
            Subpoint = new GeoCoordinate(57, 18),
            LookAngles = new LookAngles(180, -1.01, 800, 0)
        });
        Assert.False(vm.CanReportSatelliteStatus);
    }

    private static SatelliteTransponderMode ReverseSsbMode() => new()
    {
        Type = "SSB Transponder",
        DownlinkKHz = 435_667,
        UplinkKHz = 145_937.61,
        DownlinkMode = "USB",
        UplinkMode = "LSB",
        Doppler = "REV"
    };

    private static TestSatelliteDatabaseService CreateRs44Database() =>
        new(
        [
            new SatelliteRadioEntry
            {
                Name = "RS-44",
                Modes =
                [
                    new SatelliteTransponderMode
                    {
                        Type = "SSB Transponder",
                        DownlinkKHz = 435_667,
                        UplinkKHz = 145_937.61,
                        DownlinkMode = "USB",
                        UplinkMode = "LSB",
                        Doppler = "REV"
                    }
                ]
            }
        ]);

    private static SatelliteTrackState Rs44TrackState() => new()
    {
        Name = "RS-44",
        NoradId = "99999",
        Subpoint = new GeoCoordinate(57, 18),
        LookAngles = new LookAngles(180, 25, 800, 0)
    };

    private sealed class LeadRatePropagator(double slopeRate, double leadRate) : IOrbitPropagator
    {
        private DateTime? _resolveUtc;

        public void Clear() { }
        public void LoadSatellite(SatelliteCatalogEntry entry) { }
        public void RemoveSatellite(string noradId) { }
        public GeoCoordinate GetSubpoint(string noradId, DateTime utc) => new(0, 0, 400);
        public EciPosition GetEciPosition(string noradId, DateTime utc) => new(0, 0, 0);
        public bool HasSatellite(string noradId) => true;
        public IReadOnlyCollection<string> LoadedNoradIds => ["99999"];

        public LookAngles GetLookAngles(string noradId, GroundStation site, DateTime utc)
        {
            if (_resolveUtc is null)
                _resolveUtc = utc.AddSeconds(-DopplerCatLead.RangeRateSlopeSampleSec);

            var deltaSec = (utc - _resolveUtc.Value).TotalSeconds;
            if (deltaSec is >= 0.99 and <= 1.01)
                return new LookAngles(180, 30, 800, slopeRate);

            if (deltaSec is >= 0.04 and <= 0.06)
                return new LookAngles(180, 30, 800, leadRate);

            return new LookAngles(180, 30, 800, -1.0);
        }
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public string? LoadError => null;
        public bool CanPersist => true;
        public string SettingsPath { get; } = Path.Combine(Path.GetTempPath(), "oscarwatch-test-settings.json");
        public string SerializeCurrent() => "{}";
        public Task ReplaceAndSaveAsync(AppSettings imported, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Load() { }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RequestSave() { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SyncGridFromLatLon() { }
        public void SyncLatLonFromGrid() { }
        public void EnsureSavedStations() { }
        public void ApplyActiveStation() { }
        public void SyncActiveStationFromGroundStation() { }
    }

    private sealed class TestSatelliteDatabaseService(IReadOnlyList<SatelliteRadioEntry> entries) : ISatelliteDatabaseService
    {
        public IReadOnlyList<SatelliteRadioEntry> Entries { get; } = entries;
        public string ActiveDatabasePath { get; } = "test";
        public bool IsUsingUserDatabase => false;

        public SatelliteRadioEntry? TryGetEntry(string satelliteName, string? noradId = null) =>
            Entries.FirstOrDefault(e => e.Name.Equals(satelliteName, StringComparison.OrdinalIgnoreCase))
            ?? (noradId is null
                ? null
                : Entries.FirstOrDefault(e =>
                    string.Equals(e.NoradId, noradId, StringComparison.OrdinalIgnoreCase)));

        public void Reload() { }
    }
}
