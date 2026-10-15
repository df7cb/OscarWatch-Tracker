using OscarWatch.Core.Sstv;
using OscarWatch.Core.Terminal.Modem;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>
/// WA8LMF TNC Test CD benchmark. Opt-in because the tracks take minutes to decode: set
/// <c>OSCARWATCH_TNC_TEST_CD</c> to the track folder, or to <c>1</c> for the testdata/wa8lmf folder that
/// tools/fetch-tnc-test-cd.ps1 fills. Without it each test returns early.
/// Decodes only ever go through the offline receiver, never the transmit path, APRS-IS or the log.
/// </summary>
public sealed class Wa8lmfBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public Wa8lmfBenchmarkTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Track 1: 40 minutes of saturated Los Angeles APRS from a data port. Published figures are about
    /// 1000 to 1012 for Direwolf 1.2 to 1.5; this decoder scored 988 with bit fixing off when written.
    /// For a like-for-like comparison run Direwolf's <c>atest</c> on the same WAV on the same machine.
    /// </summary>
    [Fact]
    public void Track1_rush_hour_traffic()
    {
        if (Load(1) is not { } track)
            return;

        var off = AfskReceiver.DecodeAll(track.Samples, track.Rate, AfskBitFixMode.Off);
        var single = AfskReceiver.DecodeAll(track.Samples, track.Rate, AfskBitFixMode.SingleBit);
        _output.WriteLine($"Track 1: {off.Count} frames with bit fix off, {single.Count} with single-bit fix ({single.Count(f => f.Fixed)} fixed)");
        Report(off);
        CompareWithDirewolf(1, off.Count);

        Assert.True(off.Count >= Track1Gate, $"Track 1 decoded {off.Count}, gate {Track1Gate}");
    }

    /// <summary>
    /// Track 2: the version 2.0 ISO names it "100 Mic-E bursts de-emphasised", but it holds the Track 1 traffic
    /// with WA8LMF's de-emphasis (which WB2OSZ found rolls off at about 12 dB per octave with a notch near 3.1 kHz).
    /// A tone-balance stress test rather than realistic speaker audio.
    /// </summary>
    [Fact]
    public void Track2_de_emphasised_traffic()
    {
        if (Load(2) is not { } track)
            return;

        var frames = AfskReceiver.DecodeAll(track.Samples, track.Rate, AfskBitFixMode.Off);
        _output.WriteLine($"Track 2: {frames.Count} frames with bit fix off");
        Report(frames);
        CompareWithDirewolf(2, frames.Count);

        Assert.True(frames.Count >= Track2Gate, $"Track 2 decoded {frames.Count}, gate {Track2Gate}");
    }

    /// <summary>Track 3: 100 identical Mic-E bursts from a service monitor discriminator.</summary>
    [Fact]
    public void Track3_mic_e_bursts_all_decode()
    {
        if (Load(3) is not { } track)
            return;

        var frames = AfskReceiver.DecodeAll(track.Samples, track.Rate, AfskBitFixMode.Off);
        _output.WriteLine($"Track 3: {frames.Count} of 100");

        Assert.True(frames.Count >= 99, $"Track 3 decoded {frames.Count} of 100");
    }

    /// <summary>
    /// Track 3 with white noise added in steps: percent decoded against Eb/N0, the measurement WA8LMF
    /// describes with an RF generator and the closest thing on the CD to a weak satellite downlink.
    /// </summary>
    [Fact]
    public void Track3_weak_signal_curve()
    {
        if (Load(3) is not { } track)
            return;

        var signalPower = SignalPower(track.Samples);
        var results = new List<(double EbN0, int Decoded)>();
        foreach (var ebN0Db in new[] { 20.0, 14.0, 12.0, 10.0, 8.0, 6.0 })
        {
            var noisePerHz = signalPower / 1200 / Math.Pow(10, ebN0Db / 10);
            var sigma = Math.Sqrt(noisePerHz * track.Rate / 2);
            var noisy = (float[])track.Samples.Clone();
            ModemTestSignals.AddNoise(noisy, sigma, seed: (int)ebN0Db);
            var count = AfskReceiver.DecodeAll(noisy, track.Rate, AfskBitFixMode.SingleBit).Count;
            results.Add((ebN0Db, count));
            _output.WriteLine($"Eb/N0 {ebN0Db:0} dB: {count} of 100");
        }

        // When written: 100 at 14 dB, 98 at 12 dB, 23 at 10 dB.
        Assert.True(results[0].Decoded >= 99, "20 dB should be clean");
        Assert.True(results.First(r => r.EbN0 == 14).Decoded >= 97, "14 dB regressed");
        Assert.True(results.First(r => r.EbN0 == 12).Decoded >= 88, "12 dB regressed");
    }

    /// <summary>Track 4: a mobile with flutter, multipath and loss of quieting (a proxy for satellite fading).</summary>
    [Fact]
    public void Track4_mobile_drive_test()
    {
        if (Load(4) is not { } track)
            return;

        var frames = AfskReceiver.DecodeAll(track.Samples, track.Rate, AfskBitFixMode.SingleBit);
        _output.WriteLine($"Track 4: {frames.Count} frames ({frames.Count(f => f.Fixed)} fixed)");
        Report(frames);

        Assert.True(frames.Count >= Track4Gate, $"Track 4 decoded {frames.Count}, gate {Track4Gate}");
    }

    /// <summary>
    /// Tracks 5 to 7: alternating tones flat, de-emphasised and pre-emphasised. The balance needed to
    /// level them must fall inside the slicer range, or one extreme would have no matching path.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Calibration_tone_twist_is_inside_the_slicer_range(int number)
    {
        if (Load(number) is not { } track)
            return;

        var resampler = new PolyphaseResampler(track.Rate, AfskDemodulator.SampleRate);
        var audio = resampler.ProcessAll(track.Samples);
        var demod = new AfskDemodulator([12]);
        double mark = 0, space = 0;
        for (var i = 0; i < audio.Length; i++)
        {
            demod.Process(audio[i]);
            if (i < audio.Length / 4 || i > audio.Length * 3 / 4)
                continue;
            mark += demod.MarkAmplitude(0);
            space += demod.SpaceAmplitude(0);
        }

        var twistDb = 20 * Math.Log10(mark / space);
        _output.WriteLine($"Track {number}: mark is {twistDb:+0.0;-0.0} dB relative to space");

        Assert.InRange(twistDb, -7, 11);
    }

    /// <summary>Gates sit a little under the scores measured when the modem was written; raise them as it improves.</summary>
    private const int Track1Gate = 975;
    private const int Track2Gate = 930;
    private const int Track4Gate = 95;

    private void Report(List<AfskDecodedFrame> frames)
    {
        var byPath = frames.GroupBy(f => f.DecoderName.Replace(" fixed", "", StringComparison.Ordinal))
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count()}");
        _output.WriteLine("First decoder: " + string.Join(", ", byPath));
    }

    /// <summary>
    /// Prints the difference from a local Direwolf run when <c>direwolf-atest.txt</c> sits beside the tracks,
    /// with lines such as <c>track01=1010</c> (from <c>atest -F0 track01.wav</c>, bit fixing off to match).
    /// </summary>
    private void CompareWithDirewolf(int number, int ours)
    {
        var folder = Folder();
        var file = folder is null ? null : Path.Combine(folder, "direwolf-atest.txt");
        if (file is null || !File.Exists(file))
            return;

        var key = $"track{number:D2}=";
        var line = File.ReadLines(file).FirstOrDefault(l => l.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        if (line is not null && int.TryParse(line[key.Length..], out var direwolf))
            _output.WriteLine($"Direwolf atest on this machine: {direwolf} (difference {ours - direwolf:+0;-0;0})");
    }

    private static double SignalPower(float[] samples)
    {
        // Mean power over the loudest tenth of 10 ms blocks, so the gaps between bursts do not count.
        const int block = 441;
        var powers = new List<double>();
        for (var i = 0; i + block <= samples.Length; i += block)
        {
            var p = 0.0;
            for (var j = 0; j < block; j++)
                p += samples[i + j] * (double)samples[i + j];
            powers.Add(p / block);
        }

        powers.Sort();
        return powers.Skip(powers.Count * 9 / 10).Average();
    }

    private static (float[] Samples, int Rate)? Load(int number)
    {
        if (Folder() is not { } folder)
            return null;

        var path = Path.Combine(folder, $"track{number:D2}.wav");
        return File.Exists(path) ? SstvWavReader.Read(path) : null;
    }

    private static string? Folder()
    {
        var folder = Environment.GetEnvironmentVariable("OSCARWATCH_TNC_TEST_CD");
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        return folder.Trim() is "1" or "repo" ? FindRepoTestData() : folder;
    }

    /// <summary>Walks up from the test output and from this source file, so builds outside the repo still find it.</summary>
    private static string? FindRepoTestData([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile) })
        {
            if (string.IsNullOrEmpty(start))
                continue;
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "testdata", "wa8lmf");
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
