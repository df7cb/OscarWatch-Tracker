using OscarWatch.Core.Sstv;
using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Modem;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>
/// Satellite recordings: clips from OscarWatch pass recordings or SatNOGS observations of ISS APRS and
/// other 1200 AFSK digipeaters. Put <c>name.wav</c> and <c>name.expected.txt</c> (one TNC2 monitor line
/// per expected frame, for example <c>MM9SQL-7&gt;APRS,ARISS*:CQ</c>) in testdata/space, or in the folder in
/// <c>OSCARWATCH_SPACE_CORPUS</c>. Nothing runs when the folder is absent; the audio is never committed.
/// SatNOGS clips need their licence checked and a credit in CREDITS.md before they are shared.
/// </summary>
public sealed class SpaceCorpusTests
{
    private readonly ITestOutputHelper _output;

    public SpaceCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Recorded_passes_decode_their_expected_frames()
    {
        var folder = Environment.GetEnvironmentVariable("OSCARWATCH_SPACE_CORPUS");
        if (string.IsNullOrWhiteSpace(folder))
            folder = FindRepoFolder();
        if (folder is null || !Directory.Exists(folder))
            return;

        foreach (var wav in Directory.EnumerateFiles(folder, "*.wav").Order(StringComparer.Ordinal))
        {
            var expectedPath = Path.ChangeExtension(wav, ".expected.txt");
            if (!File.Exists(expectedPath))
                continue;

            var expected = File.ReadAllLines(expectedPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();
            var (samples, rate) = SstvWavReader.Read(wav);
            var decoded = AfskReceiver.DecodeAll(samples, rate, AfskBitFixMode.SingleBit)
                .Select(f => Ax25Frame.TryParse(f.Ax25, out var frame) && frame is not null ? frame.ToMonitorString() : "")
                .ToHashSet(StringComparer.Ordinal);

            var found = expected.Count(decoded.Contains);
            _output.WriteLine($"{Path.GetFileName(wav)}: {found} of {expected.Count} expected, {decoded.Count} decoded");
            Assert.True(found * 10 >= expected.Count * 9, $"{Path.GetFileName(wav)} decoded {found} of {expected.Count}");
        }
    }

    private static string? FindRepoFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "testdata", "space");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
