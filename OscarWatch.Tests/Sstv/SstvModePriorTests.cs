using System.Text.Json;
using OscarWatch.Core.Models;
using OscarWatch.Core.Sstv;

namespace OscarWatch.Tests.Sstv;

public sealed class SstvModePriorTests
{
    private const int Rate = 24000;

    [Fact]
    public void Satellite_database_lists_the_modes_each_sstv_satellite_sends()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "satellite_database.json");
        if (!File.Exists(path))
            path = Path.Combine(FindRepoRoot(), "OscarWatch", "Assets", "satellite_database.json");
        var entries = JsonSerializer.Deserialize<List<SatelliteRadioEntry>>(File.ReadAllText(path))!;

        Assert.Equivalent(
            new[] { SstvModeId.Pd120, SstvModeId.Pd180, SstvModeId.Robot36 },
            SstvModePriors.For(entries.First(e => e.Name == "ISS")),
            strict: true);
        Assert.Equal([SstvModeId.Robot36], SstvModePriors.For(entries.First(e => e.Name == "UmKA-1")));
        Assert.Equal([SstvModeId.Robot36], SstvModePriors.For(entries.First(e => e.Name == "SAKHACUBE-CHOLBON")));
        Assert.Null(SstvModePriors.For(null));
    }

    [Fact]
    public void Unknown_names_in_the_database_are_ignored()
    {
        var entry = new SatelliteRadioEntry
        {
            Modes = [new SatelliteTransponderMode { SstvModes = ["robot36", "NotAMode"] }],
        };
        Assert.Equal([SstvModeId.Robot36], SstvModePriors.For(entry));
        Assert.Null(SstvModePriors.For(new SatelliteRadioEntry { Modes = [new SatelliteTransponderMode()] }));
    }

    [Fact]
    public void A_vis_with_one_doubtful_bit_is_read_as_the_expected_mode()
    {
        var mode = SstvModeTable.Robot36;
        var settings = new SstvTestEncoder.Settings { DoubtfulVisBit = 3 };
        var audio = SstvTestEncoder.Encode(mode, Rate, settings);

        var without = SstvRoundTripTests.Decode(audio, new SstvDecoderOptions { DetectWithoutVis = false }, Rate);
        var with = SstvRoundTripTests.Decode(audio, new SstvDecoderOptions
        {
            DetectWithoutVis = false,
            ExpectedModes = [SstvModeId.Robot36],
        }, Rate);

        Assert.Empty(without);
        var image = Assert.Single(with);
        Assert.Equal(mode, image.Mode);
        Assert.True(image.FromVis);
    }

    [Fact]
    public void A_complete_header_for_another_mode_is_still_decoded()
    {
        var mode = SstvModeTable.MartinM1;
        var audio = SstvTestEncoder.Encode(mode, Rate, new SstvTestEncoder.Settings());
        var images = SstvRoundTripTests.Decode(audio, new SstvDecoderOptions { ExpectedModes = [SstvModeId.Robot36] }, Rate);

        Assert.Contains(images, i => i.Mode == mode && i.FromVis);
    }

    [Fact]
    public void Pictures_without_a_vis_are_only_looked_for_in_the_expected_modes()
    {
        var mode = SstvModeTable.Robot36;
        var audio = SstvTestEncoder.Encode(mode, Rate, new SstvTestEncoder.Settings { IncludeVis = false });

        var expected = SstvRoundTripTests.Decode(audio, new SstvDecoderOptions { ExpectedModes = [SstvModeId.Robot36] }, Rate);
        var other = SstvRoundTripTests.Decode(audio, new SstvDecoderOptions { ExpectedModes = [SstvModeId.Pd120] }, Rate);

        Assert.Contains(expected, i => i.Mode == mode);
        Assert.DoesNotContain(other, i => i.Mode == mode);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OscarWatch.sln")) && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root");
    }
}
