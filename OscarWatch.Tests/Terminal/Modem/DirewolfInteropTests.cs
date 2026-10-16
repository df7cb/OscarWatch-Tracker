using System.Diagnostics;
using System.Text.RegularExpressions;
using OscarWatch.Core.Sstv;
using OscarWatch.Core.Terminal.Modem;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>
/// Interoperability with Direwolf's offline tools. Runs when <c>OSCARWATCH_DIREWOLF_DIR</c> points at a folder
/// holding <c>atest</c> and <c>gen_packets</c> from a Direwolf build; otherwise each test returns early.
/// Only WAV files are exchanged, so nothing is ever transmitted.
/// </summary>
public sealed partial class DirewolfInteropTests
{
    private const int SampleRate = 44100;
    private readonly ITestOutputHelper _output;

    public DirewolfInteropTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Direwolf_decodes_every_frame_we_modulate()
    {
        if (Tool("atest") is not { } atest)
            return;

        const int count = 20;
        var audio = new List<float>();
        for (var i = 0; i < count; i++)
        {
            var frame = ModemTestSignals.UiFrame($":MM9SQL   :OscarWatch interop {i:00} the quick brown fox{{{i}");
            audio.AddRange(ModemTestSignals.Transmission([frame], SampleRate, txDelayMs: 150, txTailMs: 30));
        }

        var wav = TempWav("oscarwatch-to-direwolf");
        try
        {
            WriteWav16(wav, audio.ToArray(), SampleRate);
            var decoded = RunAtest(atest, wav);
            _output.WriteLine($"Direwolf atest decoded {decoded} of {count}");
            Assert.Equal(count, decoded);
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [Fact]
    public void Direwolf_decodes_every_9600_frame_we_modulate()
    {
        if (Tool("atest") is not { } atest)
            return;

        const int count = 10;
        var audio = new List<float>();
        for (var i = 0; i < count; i++)
        {
            var frame = ModemTestSignals.UiFrame($":MM9SQL   :G3RUH interop {i:00} test{{{i}");
            audio.AddRange(ModemTestSignals.G3ruhTransmission([frame], SampleRate, txDelayMs: 150, txTailMs: 30));
        }

        var wav = TempWav("oscarwatch-g3ruh-to-direwolf");
        try
        {
            WriteWav16(wav, audio.ToArray(), SampleRate);
            var decoded = RunAtest9600(atest, wav);
            _output.WriteLine($"Direwolf atest -B 9600 decoded {decoded} of {count}");
            Assert.Equal(count, decoded);
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [Fact]
    public void We_decode_at_least_as_many_gen_packets_9600_frames_as_direwolf()
    {
        if (Tool("gen_packets") is not { } gen || Tool("atest") is not { } atest)
            return;

        var wav = TempWav("direwolf-g3ruh-to-oscarwatch");
        try
        {
            Run(gen, $"-B 9600 -n 50 -r {SampleRate} -o \"{wav}\"");
            var direwolf = RunAtest9600(atest, wav);
            var (samples, rate) = SstvWavReader.Read(wav);
            var ours = G3ruhReceiver.DecodeAll(samples, rate, AfskBitFixMode.Off).Count;
            _output.WriteLine($"gen_packets -B 9600 -n 50: Direwolf {direwolf}, OscarWatch {ours}");
            Assert.True(direwolf > 0, "atest decoded nothing; check the Direwolf build");
            Assert.True(ours >= direwolf * 0.90, $"OscarWatch decoded {ours}, Direwolf {direwolf}");
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [Fact]
    public void We_decode_at_least_as_many_gen_packets_frames_as_direwolf()
    {
        if (Tool("gen_packets") is not { } gen || Tool("atest") is not { } atest)
            return;

        var wav = TempWav("direwolf-to-oscarwatch");
        try
        {
            Run(gen, $"-n 100 -r {SampleRate} -o \"{wav}\"");
            var direwolf = RunAtest(atest, wav);
            var (samples, rate) = SstvWavReader.Read(wav);
            var ours = AfskReceiver.DecodeAll(samples, rate, AfskBitFixMode.Off).Count;
            _output.WriteLine($"gen_packets -n 100: Direwolf {direwolf}, OscarWatch {ours} (both with bit fixing off)");
            Assert.True(direwolf > 0, "atest decoded nothing; check the Direwolf build");
            Assert.True(ours >= direwolf * 0.95, $"OscarWatch decoded {ours}, Direwolf {direwolf}");
        }
        finally
        {
            File.Delete(wav);
        }
    }

    private static string? Tool(string name)
    {
        var folder = Environment.GetEnvironmentVariable("OSCARWATCH_DIREWOLF_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        foreach (var candidate in new[] { name + ".exe", name })
        {
            var path = Path.Combine(folder, candidate);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string TempWav(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.wav");

    private int RunAtest(string atest, string wav) => RunAtest(atest, wav, baudFlag: "");

    private int RunAtest9600(string atest, string wav) => RunAtest(atest, wav, baudFlag: "-B 9600 ");

    private int RunAtest(string atest, string wav, string baudFlag)
    {
        var output = Run(atest, $"{baudFlag}-F0 \"{wav}\"");
        var match = PacketsDecoded().Match(output);
        Assert.True(match.Success, "Unexpected atest output: " + output);
        return int.Parse(match.Groups[1].Value);
    }

    private string Run(string exe, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), $"{Path.GetFileName(exe)} did not finish");
        var text = stdout.Result + stderr.Result;
        _output.WriteLine($"{Path.GetFileName(exe)} {arguments}: exit {process.ExitCode}");
        return text;
    }

    private static void WriteWav16(string path, float[] samples, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (var s in samples)
            w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * 32767));
    }

    [GeneratedRegex(@"(\d+)\s+packets decoded", RegexOptions.IgnoreCase)]
    private static partial Regex PacketsDecoded();
}
