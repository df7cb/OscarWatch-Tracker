using OscarWatch.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace OscarWatch.Tests;

public sealed class ThrottledLoggerTests
{
    [Fact]
    public void Second_identical_warning_is_suppressed_until_summary_interval()
    {
        var sink = new CollectingSink();
        var log = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var throttled = new ThrottledLogger(log) { SummaryInterval = TimeSpan.FromMinutes(10) };
        throttled.Warning("Rotator connect failed on {Endpoint}", "/dev/ttyUSB0");
        throttled.Warning("Rotator connect failed on {Endpoint}", "/dev/ttyUSB0");

        Assert.Single(sink.Events);
    }

    [Fact]
    public void Reset_allows_the_next_full_warning()
    {
        var sink = new CollectingSink();
        var log = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var throttled = new ThrottledLogger(log);
        throttled.Warning("First");
        throttled.Warning("Second");
        throttled.Reset();
        throttled.Warning("Third");

        Assert.Equal(2, sink.Events.Count);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
