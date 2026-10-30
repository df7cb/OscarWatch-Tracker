using OscarWatch.Core.Models;

namespace OscarWatch.Core.Sstv;

/// <summary>SSTV modes a satellite is known to send, from the satellite database.</summary>
public static class SstvModePriors
{
    /// <summary>Every known mode across the satellite's downlinks, or null when none are listed.</summary>
    public static SstvModeId[]? For(SatelliteRadioEntry? entry)
    {
        if (entry is null)
            return null;
        var modes = entry.Modes
            .SelectMany(m => m.SstvModes ?? [])
            .Select(name => Enum.TryParse<SstvModeId>(name, ignoreCase: true, out var id) ? id : (SstvModeId?)null)
            .OfType<SstvModeId>()
            .Distinct()
            .ToArray();
        return modes.Length == 0 ? null : modes;
    }
}
