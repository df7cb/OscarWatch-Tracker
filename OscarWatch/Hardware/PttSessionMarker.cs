using OscarWatch.Core.Hardware;
using Serilog;

namespace OscarWatch.Hardware;

/// <summary>
/// A small file that exists only while PTT is up. Found at startup, it means OscarWatch stopped
/// while transmitting, and a radio keyed over CAT may still be on the air.
/// </summary>
public sealed class PttSessionMarker
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PttSessionMarker>();

    public PttSessionMarker(string path) => Path = path;

    public string Path { get; }

    /// <summary>Marker for the Terminal built-in modem, beside settings.json.</summary>
    public static PttSessionMarker ForTerminal(string settingsPath) =>
        new(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(settingsPath) ?? ".", "terminal-ptt-keyed.marker"));

    public bool Exists => File.Exists(Path);

    /// <summary>The PTT method recorded when the marker was written, or null.</summary>
    public PttMethod? RecordedMethod
    {
        get
        {
            try
            {
                if (!File.Exists(Path))
                    return null;
                var parts = File.ReadAllText(Path).Split('|');
                return parts.Length >= 2 && Enum.TryParse<PttMethod>(parts[1], out var method) ? method : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public void MarkKeyed(string label, PttMethod method)
    {
        try
        {
            File.WriteAllText(Path, $"{label}|{method}|{DateTime.UtcNow:O}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "PTT marker write failed");
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "PTT marker delete failed");
        }
    }
}
