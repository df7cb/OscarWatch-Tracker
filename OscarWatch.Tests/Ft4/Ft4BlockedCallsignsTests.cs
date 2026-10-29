using System.Text.Json;
using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4BlockedCallsignsTests
{
    [Theory]
    [InlineData("g4abc", "G4ABC")]
    [InlineData(" G4ABC/P ", "G4ABC")]
    [InlineData("G4ABC/R", "G4ABC")]
    [InlineData("<G4ABC>", "G4ABC")]
    [InlineData("ea8/g4abc", "EA8/G4ABC")]
    public void Normalize_keeps_a_callsign_and_drops_portable_suffixes(string raw, string expected) =>
        Assert.Equal(expected, Ft4BlockedCallsigns.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("CQ")]
    [InlineData("IO85")]
    [InlineData("IO85ab")]
    [InlineData("<...>")]
    [InlineData("AB")]
    [InlineData("G4 ABC")]
    public void Normalize_rejects_text_that_is_not_a_callsign(string raw) =>
        Assert.Null(Ft4BlockedCallsigns.Normalize(raw));

    [Fact]
    public void Matches_is_case_insensitive_and_ignores_portable_suffixes()
    {
        var blocked = new List<string> { "g4abc/p" };
        Assert.True(Ft4BlockedCallsigns.Matches(blocked, "G4ABC"));
        Assert.True(Ft4BlockedCallsigns.Matches(blocked, "G4ABC/R"));
        Assert.False(Ft4BlockedCallsigns.Matches(blocked, "M0XYZ"));
        Assert.False(Ft4BlockedCallsigns.Matches(blocked, "M0XYZ G4ABC"));
        Assert.False(Ft4BlockedCallsigns.Matches(null, "G4ABC"));
    }

    [Fact]
    public void Clean_drops_duplicates_grids_and_stops_at_the_cap()
    {
        var raw = new List<string> { "g4abc", "G4ABC/P", "IO85", "CQ", "M0XYZ" };
        for (var i = 0; i < 60; i++)
            raw.Add($"K{i:00}ABC");

        var cleaned = Ft4BlockedCallsigns.Clean(raw);
        Assert.Equal(Ft4BlockedCallsigns.MaxCount, cleaned.Count);
        Assert.Equal("G4ABC", cleaned[0]);
        Assert.Equal("M0XYZ", cleaned[1]);
        Assert.DoesNotContain("IO85", cleaned);
    }

    [Fact]
    public void Blocked_callsigns_round_trip_in_settings_json()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = new Ft4Settings { BlockedCallsigns = ["G4ABC", "M0XYZ"] };
        var json = JsonSerializer.Serialize(settings, options);
        var again = JsonSerializer.Deserialize<Ft4Settings>(json, options);

        Assert.NotNull(again);
        Assert.Equal(["G4ABC", "M0XYZ"], again.BlockedCallsigns);
    }
}
