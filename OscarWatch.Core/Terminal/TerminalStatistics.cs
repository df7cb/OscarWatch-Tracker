namespace OscarWatch.Core.Terminal;

public enum TerminalOwnFrameResult
{
    /// <summary>Our frame came back through the satellite.</summary>
    Digipeated,

    /// <summary>The modem echoed our transmission locally; not shown and not counted.</summary>
    LocalEcho
}

/// <summary>Session counters for the statistics panel and own-frame digipeat matching.</summary>
public sealed class TerminalStatistics
{
    /// <summary>Own frames heard sooner than this without a repeated digipeater are treated as local modem echoes.</summary>
    public static readonly TimeSpan MinimumDigipeatDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a sent frame waits to be heard back before it no longer matches.</summary>
    public static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(2);

    private readonly List<SentFrame> _sent = [];
    private readonly HashSet<string> _uniqueCalls = new(StringComparer.Ordinal);
    private readonly List<double> _delaysSeconds = [];

    public int UniqueCallsigns => _uniqueCalls.Count;
    public int MyTx => _sent.Count;
    public int MyRx { get; private set; }
    public int Digipeated { get; private set; }
    public int MyDigipeated => _sent.Count(s => s.Confirmed);
    public int UhmUsers { get; set; }

    public double? MyDigipeatSuccessPercent =>
        _sent.Count == 0 ? null : 100.0 * MyDigipeated / _sent.Count;

    public double? MedianDelaySeconds
    {
        get
        {
            if (_delaysSeconds.Count == 0)
                return null;
            var sorted = _delaysSeconds.Order().ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
    }

    public void RecordSent(string infoText, DateTime utc) =>
        _sent.Add(new SentFrame(infoText, utc));

    /// <summary>Counts a received frame from another station (or the satellite).</summary>
    public void RecordReceived(TerminalDecodedMessage message, TerminalMessageKind kind)
    {
        if (message.IsDigipeated)
            Digipeated++;
        if (kind == TerminalMessageKind.ToMe)
            MyRx++;
        if (kind is not (TerminalMessageKind.Telemetry or TerminalMessageKind.MyDigipeated)
            && TerminalText.LooksLikeCallsign(message.From))
            _uniqueCalls.Add(TerminalText.BaseCallsign(message.From));
    }

    /// <summary>
    /// Matches a received copy of our own frame to the newest unexpired send with the same info text.
    /// Frames with a repeated digipeater always count; without a path, only after <see cref="MinimumDigipeatDelay"/>.
    /// </summary>
    public TerminalOwnFrameResult EvaluateOwnFrame(string infoText, bool hasBeenDigipeated, DateTime utc, out TimeSpan? delay)
    {
        delay = null;
        SentFrame? match = null;
        for (var i = _sent.Count - 1; i >= 0; i--)
        {
            var s = _sent[i];
            if (utc - s.Utc > MatchWindow)
                break;
            if (s.Utc <= utc && string.Equals(s.InfoText, infoText, StringComparison.Ordinal))
            {
                match = s;
                break;
            }
        }

        if (match is not null)
            delay = utc - match.Utc;

        var digipeated = hasBeenDigipeated || (delay is { } d && d >= MinimumDigipeatDelay);
        if (!digipeated)
            return TerminalOwnFrameResult.LocalEcho;

        Digipeated++;
        if (match is not null && !match.Confirmed)
        {
            match.Confirmed = true;
            _delaysSeconds.Add(delay!.Value.TotalSeconds);
        }

        return TerminalOwnFrameResult.Digipeated;
    }

    public void Reset()
    {
        _sent.Clear();
        _uniqueCalls.Clear();
        _delaysSeconds.Clear();
        MyRx = 0;
        Digipeated = 0;
    }

    private sealed class SentFrame(string infoText, DateTime utc)
    {
        public string InfoText { get; } = infoText;
        public DateTime Utc { get; } = utc;
        public bool Confirmed { get; set; }
    }
}
