namespace OscarWatch.Core.Hardware;

/// <summary>How OscarWatch keys the uplink (FT4 and the Terminal built-in modem).</summary>
public enum PttMethod
{
    /// <summary>Play audio only; radio VOX or interface keys from audio.</summary>
    Vox = 0,

    /// <summary>CAT transmit / PTT command on the connected rig.</summary>
    Cat = 1,

    /// <summary>Toggle RTS or DTR on the open CAT serial port.</summary>
    CatPortHandshake = 2,

    /// <summary>Dedicated COM port that only asserts RTS or DTR.</summary>
    SeparateComPort = 3,

    /// <summary>Prompt the operator to key and unkey for each transmission.</summary>
    Manual = 4
}

/// <summary>Which handshake line to assert for hardware PTT.</summary>
public enum PttLine
{
    Rts = 0,
    Dtr = 1
}

/// <summary>One feature's PTT settings, read fresh for each key so Settings changes apply at once.</summary>
public sealed record PttOptions(
    PttMethod Method,
    PttLine Line,
    bool Invert,
    string SeparatePort,
    int LeadMs,
    int TailMs)
{
    public const int MaxTimingMs = 2000;

    public int ClampedLeadMs => Math.Clamp(LeadMs, 0, MaxTimingMs);
    public int ClampedTailMs => Math.Clamp(TailMs, 0, MaxTimingMs);
}
