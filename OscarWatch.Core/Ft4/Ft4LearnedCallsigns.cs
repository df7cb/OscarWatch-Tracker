namespace OscarWatch.Core.Ft4;

/// <summary>
/// Non-standard callsigns (portable, prefixed, or longer than six characters) heard in full.
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

        return c.Contains('/') || c.Length > 6;
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
