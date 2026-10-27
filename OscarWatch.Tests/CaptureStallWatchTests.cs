using OscarWatch.Core.Recording;

namespace OscarWatch.Tests;

public sealed class CaptureStallWatchTests
{
    private static readonly DateTime T0 = new(2026, 10, 15, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Flowing_audio_never_asks_for_a_reopen()
    {
        var watch = new CaptureStallWatch(T0);
        for (var s = 0; s < 60; s++)
            Assert.False(watch.OnRead(T0.AddSeconds(s), gotSamples: true));
        Assert.Equal(0, watch.Reopens);
    }

    [Fact]
    public void Empty_reads_inside_the_timeout_are_normal()
    {
        var watch = new CaptureStallWatch(T0);
        Assert.False(watch.OnRead(T0.AddSeconds(2.9), gotSamples: false));
    }

    [Fact]
    public void Silence_past_the_timeout_asks_once_then_waits_another_timeout()
    {
        var watch = new CaptureStallWatch(T0);
        Assert.True(watch.OnRead(T0.AddSeconds(3), gotSamples: false));
        Assert.False(watch.OnRead(T0.AddSeconds(4), gotSamples: false));
        Assert.True(watch.OnRead(T0.AddSeconds(6), gotSamples: false));
        Assert.Equal(2, watch.Reopens);
    }

    [Fact]
    public void Audio_restarts_the_timer()
    {
        var watch = new CaptureStallWatch(T0);
        watch.OnRead(T0.AddSeconds(2), gotSamples: true);
        Assert.False(watch.OnRead(T0.AddSeconds(4.5), gotSamples: false));
        Assert.True(watch.OnRead(T0.AddSeconds(5), gotSamples: false));
    }

    [Fact]
    public void Paused_time_does_not_count_as_a_stall()
    {
        var watch = new CaptureStallWatch(T0);
        Assert.False(watch.OnRead(T0.AddSeconds(10), gotSamples: false, paused: true));
        Assert.False(watch.OnRead(T0.AddSeconds(12), gotSamples: false));
        Assert.True(watch.OnRead(T0.AddSeconds(13), gotSamples: false));
    }

    [Fact]
    public void Failed_reopen_doubles_retry_delay_up_to_the_cap()
    {
        var watch = new CaptureStallWatch(T0);
        Assert.True(watch.OnRead(T0.AddSeconds(3), gotSamples: false));
        watch.NoteReopenResult(succeeded: false);
        Assert.Equal(TimeSpan.FromSeconds(6), watch.RetryDelay);

        watch.NoteReopenResult(succeeded: false);
        watch.NoteReopenResult(succeeded: false);
        watch.NoteReopenResult(succeeded: false);
        watch.NoteReopenResult(succeeded: false);
        Assert.Equal(CaptureStallWatch.MaxRetryDelay, watch.RetryDelay);

        watch.NoteReopenResult(succeeded: true);
        Assert.Equal(CaptureStallWatch.DefaultTimeout, watch.RetryDelay);
    }
}
