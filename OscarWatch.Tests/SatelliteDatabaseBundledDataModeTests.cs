using OscarWatch.Core.Radio;
using OscarWatch.Core.Services;

namespace OscarWatch.Tests;

public sealed class SatelliteDatabaseBundledDataModeTests
{
    [Fact]
    public void Bundled_database_SONATE2_APRS_uses_DATA_FM_for_CAT_data_mode()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "satellite_database.json");
        Assert.True(File.Exists(path), $"Bundled database not found at {path}");

        var entries = SatelliteDatabaseFile.Load(path);
        var sonate = entries.Single(e => e.Name == "SONATE-2");
        var aprs = sonate.Modes.Single(m => m.Type == "APRS");

        Assert.True(TransponderCatModes.IsData(aprs.DownlinkMode));
        Assert.True(TransponderCatModes.IsData(aprs.UplinkMode));
        Assert.Equal("DATA-FM", aprs.DownlinkMode);
        Assert.Equal("DATA-FM", aprs.UplinkMode);
    }
}
