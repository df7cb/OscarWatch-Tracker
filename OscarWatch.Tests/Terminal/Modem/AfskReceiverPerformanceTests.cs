using System.Diagnostics;
using OscarWatch.Core.Terminal.Modem;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Terminal.Modem;

public sealed class AfskReceiverPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public AfskReceiverPerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Frequency_offset_banks_cost_is_reported()
    {
        const int sampleRate = 48000;
        const double seconds = 30;
        var frame = ModemTestSignals.UiFrame("cpu bench");
        var audio = ModemTestSignals.Transmission([frame], sampleRate, silenceSeconds: seconds);
        var frames = new List<AfskDecodedFrame>();

        var sw = Stopwatch.StartNew();
        var off = new AfskReceiver(sampleRate, frequencyOffsetDecoders: false);
        for (var i = 0; i < audio.Length; i += 4096)
            off.Process(audio.AsSpan(i, Math.Min(4096, audio.Length - i)), frames);
        off.Flush(frames);
        sw.Stop();
        var baselineMs = sw.Elapsed.TotalMilliseconds;

        frames.Clear();
        sw.Restart();
        var on = new AfskReceiver(sampleRate, frequencyOffsetDecoders: true);
        for (var i = 0; i < audio.Length; i += 4096)
            on.Process(audio.AsSpan(i, Math.Min(4096, audio.Length - i)), frames);
        on.Flush(frames);
        sw.Stop();
        var offsetMs = sw.Elapsed.TotalMilliseconds;

        var baselinePerSec = baselineMs / seconds;
        var offsetPerSec = offsetMs / seconds;
        var ratio = offsetPerSec / baselinePerSec;
        _output.WriteLine($"30 s audio at {sampleRate} Hz: baseline {baselinePerSec:0.00} ms/s, offset banks {offsetPerSec:0.00} ms/s, ratio {ratio:0.00}x");

        Assert.True(ratio < 4.0, $"offset decoder cost ratio {ratio:0.00}x exceeded guard");
    }
}
