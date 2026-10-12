using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4LearnedCallsignsTests
{
    [Theory]
    [InlineData("PA/DL1ABC", true)]
    [InlineData("MM9SQL/M", true)]
    [InlineData("MM9SQL/P", false)]
    [InlineData("G4ABC/R", false)]
    [InlineData("PA/DL1ABC/P", true)]
    [InlineData("3DA0ABCD", true)]
    [InlineData("MM9SQL", false)]
    [InlineData("CQ", false)]
    [InlineData("<...>", false)]
    [InlineData("...", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("AB/CD/EF/GH/IJ", false)]
    [InlineData("JN01", false)]
    [InlineData("jn01", false)]
    [InlineData("IO87", false)]
    public void IsWorthLearning_keeps_hashed_calls_only(string? call, bool expected) =>
        Assert.Equal(expected, Ft4LearnedCallsigns.IsWorthLearning(call));

    [Fact]
    public void Note_keeps_most_recent_first_without_duplicates()
    {
        var learned = new Ft4LearnedCallsigns();
        Assert.True(learned.Note("pa/dl1abc"));
        Assert.True(learned.Note("G4ABC/M"));
        Assert.False(learned.Note("G4ABC/M"));
        Assert.True(learned.Note("PA/DL1ABC"));
        Assert.False(learned.Note("MM9SQL"));

        Assert.Equal(["PA/DL1ABC", "G4ABC/M"], learned.Snapshot());
    }

    [Fact]
    public void Note_caps_the_list()
    {
        var learned = new Ft4LearnedCallsigns();
        for (var i = 0; i < Ft4LearnedCallsigns.MaxCount + 20; i++)
            learned.Note($"G{i}ABC/M");

        var calls = learned.Snapshot();
        Assert.Equal(Ft4LearnedCallsigns.MaxCount, calls.Count);
        Assert.Equal($"G{Ft4LearnedCallsigns.MaxCount + 19}ABC/M", calls[0]);
    }

    [Fact]
    public void BuildSeedList_puts_logbook_first_and_drops_duplicates()
    {
        var seeds = Ft4LearnedCallsigns.BuildSeedList(
            ["PA/DL1ABC", "MM9SQL", "g4abc/m"],
            ["G4ABC/M", "EA8/G0ABC"]);

        Assert.Equal(["PA/DL1ABC", "G4ABC/M", "EA8/G0ABC"], seeds);
    }

    [Fact]
    public void BuildSeedList_caps_logbook_and_total()
    {
        var logbook = Enumerable.Range(0, 100).Select(i => $"L{i}ABC/M").ToArray();
        var heard = Enumerable.Range(0, 300).Select(i => $"H{i}ABC/M").ToArray();

        var seeds = Ft4LearnedCallsigns.BuildSeedList(logbook, heard);

        Assert.Equal(Ft4LearnedCallsigns.MaxSeeded, seeds.Count);
        Assert.Equal(Ft4LearnedCallsigns.MaxFromLogbook, seeds.Count(s => s.StartsWith('L')));
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ow-learned-{Guid.NewGuid():N}.txt");
        try
        {
            var learned = new Ft4LearnedCallsigns();
            learned.Note("PA/DL1ABC");
            learned.Note("G4ABC/M");
            Assert.True(learned.SaveIfChanged(path));

            var reloaded = new Ft4LearnedCallsigns();
            reloaded.Load(path);
            Assert.Equal(["G4ABC/M", "PA/DL1ABC"], reloaded.Snapshot());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_skips_junk_lines_and_missing_file()
    {
        var learned = new Ft4LearnedCallsigns();
        learned.Load(Path.Combine(Path.GetTempPath(), $"ow-missing-{Guid.NewGuid():N}.txt"));
        Assert.Empty(learned.Snapshot());

        var path = Path.Combine(Path.GetTempPath(), $"ow-learned-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(path, ["", "<...>", "MM9SQL", "g4abc/m", "G4ABC/M"]);
            learned.Load(path);
            Assert.Equal(["G4ABC/M"], learned.Snapshot());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
