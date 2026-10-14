namespace OscarWatch.Core.Terminal.Uhm;

public enum UhmMatchState
{
    NotChecked,
    Waiting,
    HeardMe,
    Mutual,
    Expired
}

/// <summary>UHM confirmation state per partner callsign, fed by <c>/match</c> polling results.</summary>
public sealed class UhmMatchTracker
{
    /// <summary>Server match window; a partner without a match this long after polling began is shown as expired.</summary>
    public static readonly TimeSpan MatchWindow = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeUsers = new(StringComparer.Ordinal);

    public UhmMatchState GetState(string? call) =>
        _entries.TryGetValue(TerminalText.BaseCallsign(call), out var e) ? e.State : UhmMatchState.NotChecked;

    public bool IsMutual(string? call) => GetState(call) == UhmMatchState.Mutual;

    public bool IsActiveUser(string? call) => _activeUsers.Contains(TerminalText.BaseCallsign(call));

    public int ActiveUserCount => _activeUsers.Count;

    /// <summary>Call before polling a partner so the state shows <see cref="UhmMatchState.Waiting"/>.</summary>
    public void BeginPolling(string call, DateTime nowUtc)
    {
        var key = TerminalText.BaseCallsign(call);
        if (!_entries.TryGetValue(key, out var e) || e.State is UhmMatchState.NotChecked or UhmMatchState.Expired)
            _entries[key] = new Entry(UhmMatchState.Waiting, nowUtc, null);
    }

    /// <summary>Applies a <c>/match</c> answer (did <paramref name="call"/> hear us; is it mutual).</summary>
    public UhmMatchState ApplyMatch(string call, bool matched, bool mutual, DateTime nowUtc)
    {
        var key = TerminalText.BaseCallsign(call);
        _entries.TryGetValue(key, out var e);
        e ??= new Entry(UhmMatchState.Waiting, nowUtc, null);

        UhmMatchState state;
        if (mutual)
            state = UhmMatchState.Mutual;
        else if (matched)
            state = UhmMatchState.HeardMe;
        else if (e.State == UhmMatchState.Mutual)
            state = UhmMatchState.Mutual;
        else if (nowUtc - (e.LastMatchUtc ?? e.SinceUtc) > MatchWindow)
            state = UhmMatchState.Expired;
        else
            state = e.State == UhmMatchState.HeardMe ? UhmMatchState.HeardMe : UhmMatchState.Waiting;

        _entries[key] = new Entry(state, e.SinceUtc, matched || mutual ? nowUtc : e.LastMatchUtc);
        return state;
    }

    public void SetActiveUsers(IEnumerable<string> callsigns)
    {
        _activeUsers.Clear();
        foreach (var c in callsigns)
        {
            var key = TerminalText.BaseCallsign(c);
            if (key.Length > 0)
                _activeUsers.Add(key);
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _activeUsers.Clear();
    }

    private sealed record Entry(UhmMatchState State, DateTime SinceUtc, DateTime? LastMatchUtc);
}
