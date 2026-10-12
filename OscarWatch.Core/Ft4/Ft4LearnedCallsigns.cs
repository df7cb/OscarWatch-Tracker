namespace OscarWatch.Core.Ft4;

/// <summary>
/// Non-standard callsigns (prefixed, a suffix other than /P or /R, or longer than six characters) heard in full.
/// FT4 sends these as a hash, so a station shows as &lt;...&gt; until its full call has been
/// decoded once. Keeping them between sessions lets the decoder name the station straight away.
/// </summary>
public sealed class Ft4LearnedCallsigns
{
    /// <summary>
    /// The native decoder table holds 256 calls and drops new ones when full,
    /// so leave plenty of room for calls heard during the session.
    /// </summary>
    public const int MaxCount = 150;

    /// <summary>Worked non-standard calls from the logbook, loaded first when FT4 starts.</summary>
    public const int MaxFromLogbook = 50;

    /// <summary>Total calls loaded at FT4 start, leaving room in the 256-entry decoder table for the pass.</summary>
    public const int MaxSeeded = 180;

    private readonly object _gate = new();
    private readonly List<string> _calls = new();
    private bool _dirty;

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OscarWatch",
        "ft4-learned-callsigns.txt");

    /// <summary>Most recently heard first.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
            return _calls.ToArray();
    }

    /// <summary>True when the call would be sent as a hash and is worth remembering.</summary>
    public static bool IsWorthLearning(string? call)
    {
        if (string.IsNullOrWhiteSpace(call))
            return false;

        var c = call.Trim();
        if (c.Length > 11 || c.Equals("CQ", StringComparison.OrdinalIgnoreCase) || c.Contains('<') || c.Contains('.'))
            return false;

        foreach (var ch in c)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '/')
                return false;
        }

        // A grid square is two letters and two digits, such as JN01. It is not a callsign.
        if (IsGridSquare(c))
            return false;

        // /P and /R after a normal call are a flag bit in the message, not a hash.
        return Ft4MessageCodec.IsHashedCall(c);
    }

    private static bool IsGridSquare(string call) =>
        call.Length == 4
        && char.IsAsciiLetter(call[0])
        && char.IsAsciiLetter(call[1])
        && char.IsAsciiDigit(call[2])
        && char.IsAsciiDigit(call[3]);

    /// <summary>
    /// Calls to load into the decoder: worked stations from the logbook first, then calls heard
    /// on air, without duplicates and up to <see cref="MaxSeeded"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildSeedList(IEnumerable<string> logbookCalls, IEnumerable<string> heardCalls)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seeds = new List<string>();

        void AddFrom(IEnumerable<string> calls, int limit)
        {
            foreach (var call in calls)
            {
                if (seeds.Count >= limit)
                    return;
                if (!IsWorthLearning(call))
                    continue;
                var c = call.Trim().ToUpperInvariant();
                if (seen.Add(c))
                    seeds.Add(c);
            }
        }

        AddFrom(logbookCalls, MaxFromLogbook);
        AddFrom(heardCalls, MaxSeeded);
        return seeds;
    }

    /// <summary>Remember a call heard in full. Returns true when the list changed.</summary>
    public bool Note(string? call)
    {
        if (!IsWorthLearning(call))
            return false;

        var c = call!.Trim().ToUpperInvariant();
        lock (_gate)
        {
            var index = _calls.IndexOf(c);
            if (index == 0)
                return false;
            if (index > 0)
                _calls.RemoveAt(index);

            _calls.Insert(0, c);
            if (_calls.Count > MaxCount)
                _calls.RemoveRange(MaxCount, _calls.Count - MaxCount);
            _dirty = true;
            return true;
        }
    }

    public void Load(string path)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path))
                return;
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        lock (_gate)
        {
            _calls.Clear();
            foreach (var line in lines)
            {
                if (!IsWorthLearning(line))
                    continue;
                var c = line.Trim().ToUpperInvariant();
                if (!_calls.Contains(c))
                    _calls.Add(c);
                if (_calls.Count >= MaxCount)
                    break;
            }

            _dirty = false;
        }
    }

    /// <summary>Writes the list when it has changed. Returns false when the write failed.</summary>
    public bool SaveIfChanged(string path)
    {
        string[] lines;
        lock (_gate)
        {
            if (!_dirty)
                return true;
            lines = _calls.ToArray();
            _dirty = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
                _dirty = true;
            return false;
        }
    }
}
