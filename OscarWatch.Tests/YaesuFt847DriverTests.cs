using OscarWatch.Core.Models;
using OscarWatch.Core.Radio;
using OscarWatch.Rig;

namespace OscarWatch.Tests;

public sealed class YaesuFt847DriverTests
{
    [Fact]
    public void Open_sends_cat_on()
    {
        var transport = new RecordingYaesuCatTransport();
        var driver = new YaesuFt847Driver(transport);
        driver.Open();

        Assert.True(transport.SentFrames.Count >= 1);
        Assert.Equal(YaesuFt847CatCodec.CatOn.ToArray(), transport.SentFrames[0]);
    }

    [Fact]
    public void SetSatelliteMode_and_frequencies_use_sat_vfos()
    {
        var transport = new RecordingYaesuCatTransport();
        var driver = new YaesuFt847Driver(transport);
        driver.Open();
        driver.SetSatelliteMode(true);
        driver.SelectVfo(RigVfo.Main);
        driver.SetFrequencyHz(435_750_000);
        driver.SelectVfo(RigVfo.Sub);
        driver.SetFrequencyHz(145_900_010);

        Assert.Contains(transport.SentFrames, f => f.SequenceEqual(YaesuFt847CatCodec.SatelliteModeOn.ToArray()));
        Assert.Contains(transport.SentFrames, f => f.Length == 5 && f[4] == 0x11);
        Assert.Contains(transport.SentFrames, f => f.Length == 5 && f[4] == 0x21);
    }

    [Fact]
    public void SetMode_FM_sends_wide_fm_byte()
    {
        var transport = new RecordingYaesuCatTransport();
        var driver = new YaesuFt847Driver(transport);
        driver.Open();
        driver.SetSatelliteMode(true);
        driver.SetMode("FM");

        Assert.Contains(transport.SentFrames, f => f.Length == 5 && f[0] == 0x08);
        Assert.DoesNotContain(transport.SentFrames, f => f.Length == 5 && f[0] == 0x88);
    }

    [Fact]
    public void SetMode_FMN_sends_narrow_fm_byte()
    {
        var transport = new RecordingYaesuCatTransport();
        var driver = new YaesuFt847Driver(transport);
        driver.Open();
        driver.SetSatelliteMode(true);
        driver.SetMode("FMN");

        Assert.Contains(transport.SentFrames, f => f.Length == 5 && f[0] == 0x88);
    }

    [Fact]
    public void Pass_init_FM_sets_wide_fm_on_sat_rx_and_sat_tx()
    {
        var transport = new RecordingYaesuCatTransport();
        var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var settings = new RigSettings
        {
            Enabled = true,
            Type = RigType.YaesuFt847,
            Port = "COM1",
            CatDelayMs = 0
        };

        var mode = new SatelliteTransponderMode
        {
            Type = "Voice U/V",
            DownlinkKHz = 145_960,
            UplinkKHz = 435_250,
            DownlinkMode = "FM",
            UplinkMode = "FM",
            Doppler = "NOR"
        };

        controller.Update(settings, new RigTrackingContext
        {
            TrackState = new SatelliteTrackState
            {
                Name = "AO-91",
                NoradId = "43017",
                Subpoint = new GeoCoordinate(0, 0),
                LookAngles = new LookAngles(180, 30, 800, 0)
            },
            Mode = mode,
            Corrected = DopplerFrequencyCalculator.Compute(mode, 0, 0)
        });

        var modeFrames = transport.SentFrames
            .Where(f => f.Length == 5 && f[1] == 0 && f[2] == 0 && f[3] == 0 && f[4] is 0x17 or 0x27)
            .ToList();

        Assert.Equal(2, modeFrames.Count);
        Assert.All(modeFrames, f => Assert.Equal(0x08, f[0]));
        Assert.Contains(modeFrames, f => f[4] == 0x17);
        Assert.Contains(modeFrames, f => f[4] == 0x27);
    }

    [Fact]
    public void Pass_init_ISS_cross_band_repeater_uses_wide_fm_not_narrow()
    {
        var transport = new RecordingYaesuCatTransport();
        var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var settings = new RigSettings
        {
            Enabled = true,
            Type = RigType.YaesuFt847,
            Port = "COM1",
            CatDelayMs = 0,
            Region = RigRegion.USA
        };

        var mode = new SatelliteTransponderMode
        {
            Type = "Cross band repeater",
            DownlinkKHz = 437_800,
            UplinkKHz = 145_990,
            DownlinkMode = "FM",
            UplinkMode = "FM",
            Doppler = "NOR",
            CtcssHz = 67.0
        };

        controller.Update(settings, new RigTrackingContext
        {
            TrackState = new SatelliteTrackState
            {
                Name = "ISS",
                NoradId = "25544",
                Subpoint = new GeoCoordinate(0, 0),
                LookAngles = new LookAngles(180, 30, 800, 0)
            },
            Mode = mode,
            Corrected = DopplerFrequencyCalculator.Compute(mode, 0, 0),
            SelectedCtcssHz = 67.0
        });

        var modeFrames = transport.SentFrames
            .Where(f => f.Length == 5 && f[1] == 0 && f[2] == 0 && f[3] == 0 && f[4] is 0x17 or 0x27)
            .ToList();

        Assert.Equal(2, modeFrames.Count);
        Assert.All(modeFrames, f => Assert.Equal(0x08, f[0]));
        Assert.DoesNotContain(transport.SentFrames, f => f.Length == 5 && f[0] == 0x88);
    }

    [Fact]
    public void Pass_init_FM_us_region_ctcss_uses_encode_only_on_sat_tx()
    {
        var transport = new RecordingYaesuCatTransport();
        var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var settings = new RigSettings
        {
            Enabled = true,
            Type = RigType.YaesuFt847,
            Port = "COM1",
            CatDelayMs = 0,
            Region = RigRegion.USA
        };

        var mode = new SatelliteTransponderMode
        {
            Type = "FM VOICE",
            DownlinkKHz = 436_795,
            UplinkKHz = 145_850,
            DownlinkMode = "FM",
            UplinkMode = "FM",
            Doppler = "NOR",
            CtcssHz = 67.0
        };

        controller.Update(settings, new RigTrackingContext
        {
            TrackState = new SatelliteTrackState
            {
                Name = "SO-50",
                NoradId = "25544",
                Subpoint = new GeoCoordinate(0, 0),
                LookAngles = new LookAngles(180, 30, 800, 0)
            },
            Mode = mode,
            Corrected = DopplerFrequencyCalculator.Compute(mode, 0, 0),
            SelectedCtcssHz = 67.0
        });

        var encodeOn = YaesuFt847CatCodec.BuildCtcssOnCommand(
            encoderOnly: true,
            YaesuFt847VfoTarget.SatTx,
            satelliteMode: true);
        var decodeOn = YaesuFt847CatCodec.BuildCtcssOnCommand(
            encoderOnly: false,
            YaesuFt847VfoTarget.SatTx,
            satelliteMode: true);

        Assert.Contains(transport.SentFrames, f => f.SequenceEqual(encodeOn));
        Assert.DoesNotContain(transport.SentFrames, f => f.SequenceEqual(decodeOn));
        AssertEncodeOnFollowsSatMode(transport.SentFrames, encodeOn);
    }

    [Fact]
    public void Pass_init_ISS_reasserts_ctcss_encoder_after_wide_fm()
    {
        var transport = new RecordingYaesuCatTransport();
        var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var settings = new RigSettings
        {
            Enabled = true,
            Type = RigType.YaesuFt847,
            Port = "COM1",
            CatDelayMs = 0,
            Region = RigRegion.EU
        };

        var mode = new SatelliteTransponderMode
        {
            Type = "Cross band repeater",
            DownlinkKHz = 437_800,
            UplinkKHz = 145_990,
            DownlinkMode = "FM",
            UplinkMode = "FM",
            Doppler = "NOR",
            CtcssHz = 67.0
        };

        controller.Update(settings, new RigTrackingContext
        {
            TrackState = new SatelliteTrackState
            {
                Name = "ISS",
                NoradId = "25544",
                Subpoint = new GeoCoordinate(0, 0),
                LookAngles = new LookAngles(180, 30, 800, 0)
            },
            Mode = mode,
            Corrected = DopplerFrequencyCalculator.Compute(mode, 0, 0),
            SelectedCtcssHz = 67.0
        });

        var encodeOn = YaesuFt847CatCodec.BuildCtcssOnCommand(
            encoderOnly: true,
            YaesuFt847VfoTarget.SatTx,
            satelliteMode: true);

        AssertEncodeOnFollowsSatMode(transport.SentFrames, encodeOn);
    }

    private static void AssertEncodeOnFollowsSatMode(List<byte[]> frames, byte[] encodeOn)
    {
        var lastMode = -1;
        var lastEncode = -1;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Length == 5 && frame[4] is 0x17 or 0x27)
                lastMode = i;
            if (frame.SequenceEqual(encodeOn))
                lastEncode = i;
        }

        Assert.True(lastMode >= 0, "Expected SAT RX/TX FM mode frames.");
        Assert.True(lastEncode > lastMode, "CTCSS encoder must be sent after the SAT mode commands.");
    }

    [Fact]
    public void Pass_init_same_band_packet_leaves_satellite_mode_and_uses_simplex()
    {
        var transport = new RecordingYaesuCatTransport();
        var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var settings = new RigSettings
        {
            Enabled = true,
            Type = RigType.YaesuFt847,
            Port = "COM1",
            CatDelayMs = 0
        };

        var mode = new SatelliteTransponderMode
        {
            Type = "Packet",
            DownlinkKHz = 145_825,
            UplinkKHz = 145_825,
            DownlinkMode = "FM",
            UplinkMode = "FM",
            Doppler = "NOR"
        };

        controller.Update(settings, new RigTrackingContext
        {
            TrackState = new SatelliteTrackState
            {
                Name = "ISS",
                NoradId = "25544",
                Subpoint = new GeoCoordinate(0, 0),
                LookAngles = new LookAngles(180, 20, 800, 0)
            },
            Mode = mode,
            Corrected = DopplerFrequencyCalculator.Compute(mode, 0, 0)
        });

        Assert.Contains(transport.SentFrames, f => f.SequenceEqual(YaesuFt847CatCodec.SatelliteModeOff.ToArray()));
        Assert.DoesNotContain(transport.SentFrames, f => f.SequenceEqual(YaesuFt847CatCodec.SatelliteModeOn.ToArray()));
        Assert.Contains(transport.SentFrames, f => f.Length == 5 && f[4] == 0x01 && YaesuFt847CatCodec.DecodeFrequency10Hz(f) == 145_825_000);
        Assert.DoesNotContain(transport.SentFrames, f => f.Length == 5 && f[4] is 0x11 or 0x21);
        Assert.Contains(transport.SentFrames, f => f.SequenceEqual(YaesuFt847CatCodec.BuildRepeaterShiftCommand(0)));
    }

    [Fact]
    public void Pass_init_same_band_on_approach_keeps_the_downlink_on_main()
    {
        var transport = new RecordingYaesuCatTransport();
        using var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var (rxHz, txHz) = StartSameBandPass(controller, rangeRateKmS: -7.0);
        Assert.True(rxHz > txHz);

        var frames = transport.SentFrames.ToList();
        Assert.Contains(frames, f => f.SequenceEqual(YaesuFt847CatCodec.SatelliteModeOff.ToArray()));
        Assert.DoesNotContain(frames, f => f.SequenceEqual(YaesuFt847CatCodec.SatelliteModeOn.ToArray()));
        Assert.Equal(rxHz, LastMainHz(frames));
        Assert.DoesNotContain(frames, f => f.Length == 5 && f[4] is 0x11 or 0x21 or 0xF9);
        Assert.DoesNotContain(frames, f => f.SequenceEqual(YaesuFt847CatCodec.BuildRepeaterShiftCommand(-1)));
    }

    [Fact]
    public void Same_band_transmit_hold_puts_the_uplink_on_main_then_returns()
    {
        var transport = new RecordingYaesuCatTransport();
        using var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var (rxHz, txHz) = StartSameBandPass(controller, rangeRateKmS: -7.0);

        Assert.True(controller.TryHoldSameBandTransmit(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2)));
        Assert.Equal(txHz, LastMainHz(transport.SentFrames.ToList()));

        Thread.Sleep(600);
        controller.DrainCommandQueueForTests();
        Assert.Equal(rxHz, LastMainHz(transport.SentFrames.ToList()));
    }

    [Fact]
    public void Same_band_transmit_hold_is_declined_cross_band()
    {
        var transport = new RecordingYaesuCatTransport();
        using var controller = new RigController(_ => new YaesuFt847Driver(transport));
        var mode = new SatelliteTransponderMode
        {
            Type = "FM",
            DownlinkKHz = 437_800,
            UplinkKHz = 145_990,
            DownlinkMode = "FM",
            UplinkMode = "FM",
            Doppler = "NOR"
        };
        controller.Update(Ft847Settings(), PassContext(mode, -7.0));

        Assert.False(controller.TryHoldSameBandTransmit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
    }

    private static (long RxHz, long TxHz) StartSameBandPass(RigController controller, double rangeRateKmS)
    {
        var mode = new SatelliteTransponderMode
        {
            Type = "Packet",
            DownlinkKHz = 145_825,
            UplinkKHz = 145_825,
            DownlinkMode = "FM",
            UplinkMode = "FM",
            Doppler = "NOR"
        };
        var context = PassContext(mode, rangeRateKmS);
        controller.Update(Ft847Settings(), context);
        return (CatHz(context.Corrected!.RadioReceiveKHz), CatHz(context.Corrected.RadioTransmitKHz));
    }

    private static RigSettings Ft847Settings() => new()
    {
        Enabled = true,
        Type = RigType.YaesuFt847,
        Port = "COM1",
        CatDelayMs = 0
    };

    private static RigTrackingContext PassContext(SatelliteTransponderMode mode, double rangeRateKmS) => new()
    {
        TrackState = new SatelliteTrackState
        {
            Name = "ISS",
            NoradId = "25544",
            Subpoint = new GeoCoordinate(0, 0),
            LookAngles = new LookAngles(180, 20, 800, rangeRateKmS)
        },
        Mode = mode,
        Corrected = DopplerFrequencyCalculator.Compute(mode, rangeRateKmS, 0)
    };

    private static long LastMainHz(List<byte[]> frames) =>
        YaesuFt847CatCodec.DecodeFrequency10Hz(frames.Last(f => f.Length == 5 && f[4] == 0x01));

    private static long CatHz(double kHz)
    {
        var hz = (long)Math.Round(kHz * 1000.0);
        return (long)(Math.Round(hz / 10.0) * 10);
    }

    [Fact]
    public void SupportsVfoExchange_is_false()
    {
        var driver = new YaesuFt847Driver(new RecordingYaesuCatTransport());
        Assert.False(driver.SupportsVfoExchange);
    }

    [Fact]
    public void Dispose_sends_cat_off()
    {
        var transport = new RecordingYaesuCatTransport();
        var driver = new YaesuFt847Driver(transport);
        driver.Open();
        driver.Dispose();
        Assert.Contains(transport.SentFrames, f => f.SequenceEqual(YaesuFt847CatCodec.CatOff.ToArray()));
    }
}
