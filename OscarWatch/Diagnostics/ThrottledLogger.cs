using Serilog;

namespace OscarWatch.Diagnostics;

/// <summary>
/// Emits the first matching warning with a full stack trace, then suppresses repeats until
/// <see cref="SummaryInterval"/> elapses or <see cref="Reset"/> is called.
/// </summary>
public sealed class ThrottledLogger
{
    public static readonly TimeSpan DefaultSummaryInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger _logger;
    private readonly object _gate = new();
    private int _suppressedSinceLastEmit;
    private DateTime _lastEmitUtc = DateTime.MinValue;

    public ThrottledLogger(ILogger logger) => _logger = logger;

    public TimeSpan SummaryInterval { get; set; } = DefaultSummaryInterval;

    public void Reset()
    {
        lock (_gate)
            _suppressedSinceLastEmit = 0;
    }

    public void Warning(string messageTemplate, params object?[] propertyValues) =>
        Warning(null, messageTemplate, propertyValues);

    public void Warning(Exception? exception, string messageTemplate, params object?[] propertyValues)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_suppressedSinceLastEmit == 0 || now - _lastEmitUtc >= SummaryInterval)
            {
                if (_suppressedSinceLastEmit > 1)
                {
                    _logger.Warning(
                        "Previous warning repeated {RepeatCount} times (identical messages suppressed for {Minutes:0.#} min)",
                        _suppressedSinceLastEmit - 1,
                        SummaryInterval.TotalMinutes);
                }

                _suppressedSinceLastEmit = 1;
                _lastEmitUtc = now;
                if (exception is null)
                    _logger.Warning(messageTemplate, propertyValues);
                else
                    _logger.Warning(exception, messageTemplate, propertyValues);
                return;
            }

            _suppressedSinceLastEmit++;
        }
    }
}
