namespace OscarWatch.Core.Terminal;

public enum TerminalStationState
{
    Heard,
    Cq,
    Called,
    ReportSent,
    ReportReceived,
    ReportsExchanged,
    RogerSent,
    RogerReceived,
    Complete,
    Logged
}

/// <summary>Everything Terminal knows about one heard or worked station during the session.</summary>
public sealed class TerminalStation
{
    internal TerminalStation(string call, DateTime firstHeardUtc)
    {
        Call = call;
        FirstHeardUtc = firstHeardUtc;
        LastHeardUtc = firstHeardUtc;
    }

    public string Call { get; }
    public string? Grid { get; internal set; }
    public DateTime FirstHeardUtc { get; }
    public DateTime LastHeardUtc { get; internal set; }
    public DateTime LastActivityUtc { get; internal set; }
    public int CqCount { get; internal set; }
    public int TxCount { get; internal set; }

    /// <summary>We sent at least one directed frame to this station.</summary>
    public bool WeCalled { get; internal set; }

    /// <summary>This station sent at least one frame addressed to us.</summary>
    public bool TheyCalledUs { get; internal set; }

    public string? RstSent { get; internal set; }
    public string? RstReceived { get; internal set; }
    public bool ExchangeSent { get; internal set; }
    public bool RogerSent { get; internal set; }
    public bool RogerReceived { get; internal set; }
    public bool SignOffSent { get; internal set; }
    public bool SignOffReceived { get; internal set; }
    public bool Logged { get; internal set; }
    public bool LastWasCq { get; internal set; }

    public bool IsInQso => WeCalled || TheyCalledUs;

    /// <summary>They addressed us and we know their grid or report.</summary>
    public bool ExchangeReceived => TheyCalledUs && (Grid is not null || RstReceived is not null);

    public bool ExchangesComplete => ExchangeReceived && ExchangeSent;

    public bool HasRogerOrSignOff => RogerSent || RogerReceived || SignOffSent || SignOffReceived;

    public TerminalStationState State
    {
        get
        {
            if (Logged)
                return TerminalStationState.Logged;
            if (ExchangesComplete && HasRogerOrSignOff)
                return TerminalStationState.Complete;
            if (RogerReceived)
                return TerminalStationState.RogerReceived;
            if (RogerSent)
                return TerminalStationState.RogerSent;
            if (ExchangesComplete)
                return TerminalStationState.ReportsExchanged;
            if (ExchangeReceived)
                return TerminalStationState.ReportReceived;
            if (ExchangeSent)
                return TerminalStationState.ReportSent;
            if (IsInQso)
                return TerminalStationState.Called;
            return LastWasCq ? TerminalStationState.Cq : TerminalStationState.Heard;
        }
    }
}

/// <summary>
/// Per-station QSO progress from received and sent Terminal messages. A contact is ready to log once
/// both sides have exchanged a grid or report and a roger or sign-off has been sent or received
/// (or, optionally, UHM reports mutual instead of the roger).
/// </summary>
public sealed class TerminalQsoTracker
{
    private readonly Dictionary<string, TerminalStation> _stations = new(StringComparer.Ordinal);
    private DateTime _lastCqWithExchangeUtc = DateTime.MinValue;

    /// <summary>How long after our CQ a caller is assumed to have copied the grid in it.</summary>
    public static readonly TimeSpan CqCopyWindow = TimeSpan.FromMinutes(10);

    public IReadOnlyCollection<TerminalStation> Stations => _stations.Values;

    public TerminalStation? Get(string? call) =>
        _stations.TryGetValue(TerminalText.BaseCallsign(call), out var s) ? s : null;

    public TerminalStation? OnReceived(TerminalDecodedMessage message, string myCall)
    {
        if (message.IsTelemetry || TerminalText.SameStation(message.From, myCall))
            return null;

        var call = TerminalText.BaseCallsign(message.From);
        if (!TerminalText.LooksLikeCallsign(call))
            return null;

        var station = GetOrAdd(call, message.Utc);
        station.LastHeardUtc = message.Utc;
        station.TxCount++;
        if (message.Grid is not null)
            station.Grid = message.Grid;
        station.LastWasCq = message.IsCq;
        if (message.IsCq)
            station.CqCount++;

        if (!TerminalText.SameStation(message.To, myCall))
            return station;

        if (!station.TheyCalledUs && message.Utc - _lastCqWithExchangeUtc <= CqCopyWindow)
            station.ExchangeSent = true;

        station.TheyCalledUs = true;
        station.LastActivityUtc = message.Utc;
        if (message.Report is not null)
            station.RstReceived = message.Report;
        if (message.IsRoger)
        {
            station.RogerReceived = true;
            station.ExchangeSent = true;
        }

        if (message.IsSignOff)
            station.SignOffReceived = true;
        return station;
    }

    public TerminalStation? OnSent(string? to, string text, DateTime utc)
    {
        var target = TerminalText.BaseCallsign(to);
        var hasExchange = TerminalText.FindGrid(text) is not null || TerminalText.FindReport(text) is not null;

        if (target.Length == 0 || target == TerminalMessageCodec.CqTo || target == TerminalMessageCodec.AllTo
            || !TerminalText.LooksLikeCallsign(target))
        {
            if (hasExchange)
                _lastCqWithExchangeUtc = utc;
            return null;
        }

        var station = GetOrAdd(target, utc);
        station.WeCalled = true;
        station.LastActivityUtc = utc;
        if (hasExchange)
        {
            station.ExchangeSent = true;
            station.RstSent = TerminalText.FindReport(text) ?? station.RstSent;
        }

        if (TerminalText.ContainsRoger(text))
            station.RogerSent = true;
        if (TerminalText.ContainsSignOff(text))
            station.SignOffSent = true;
        return station;
    }

    /// <summary>Whether <paramref name="station"/> can be logged automatically.</summary>
    public static bool IsReadyToLog(TerminalStation station, bool uhmMutual, bool logWhenMutual)
    {
        if (station.Logged || !station.ExchangesComplete)
            return false;
        return station.HasRogerOrSignOff || (logWhenMutual && uhmMutual);
    }

    public void MarkLogged(string call) => SetLogged(call, true);

    public void SetLogged(string call, bool logged)
    {
        var station = Get(call);
        if (station is not null)
            station.Logged = logged;
    }

    /// <summary>Stations in a QSO with activity inside <paramref name="window"/>, newest first.</summary>
    public IReadOnlyList<TerminalStation> ActivePartners(DateTime nowUtc, TimeSpan window) =>
        _stations.Values
            .Where(s => s.IsInQso && nowUtc - s.LastActivityUtc <= window)
            .OrderByDescending(s => s.LastActivityUtc)
            .ToList();

    public void Clear()
    {
        _stations.Clear();
        _lastCqWithExchangeUtc = DateTime.MinValue;
    }

    private TerminalStation GetOrAdd(string call, DateTime utc)
    {
        if (!_stations.TryGetValue(call, out var station))
        {
            station = new TerminalStation(call, utc);
            _stations[call] = station;
        }

        return station;
    }
}
