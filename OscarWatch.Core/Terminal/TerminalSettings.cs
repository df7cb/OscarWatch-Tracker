using System.Text.Json.Serialization;
using OscarWatch.Core.Hardware;
using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Core.Terminal;

/// <summary>How OscarWatch Terminal reaches the packet modem: AGWPE, KISS over TCP, a KISS serial TNC, or the built-in soundcard modem.</summary>
public enum TerminalTransportKind
{
    Agwpe,
    KissTcp,
    KissSerial,
    BuiltIn
}

public sealed class TerminalAgwpeSettings
{
    public const int DefaultPort = 8000;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = DefaultPort;

    /// <summary>Zero-based AGWPE radio port (UZ7HO and Direwolf list these in the <c>G</c> reply).</summary>
    public int RadioPort { get; set; }

    /// <summary>Connect when the Terminal window opens.</summary>
    public bool AutoConnect { get; set; } = true;
}

/// <summary>Whether the built-in modem may receive while it transmits.</summary>
public enum TerminalDuplexMode
{
    /// <summary>Half duplex for simplex and same-band satellites (and no satellite), full duplex for crossband.</summary>
    Auto,
    Half,
    Full
}

/// <summary>OscarWatch Terminal built-in 1200 baud AFSK soundcard modem.</summary>
public sealed class TerminalBuiltInModemSettings
{
    public const double DefaultTxLevel = 0.5;
    public const int DefaultPersist = 63;
    public const int DefaultSlotTimeMs = 100;
    public const int DefaultMaxChannelWaitMs = 5000;
    public const int MaxChannelWaitLimitMs = 30000;
    public const int DefaultTurnaroundGuardMs = 150;
    public const int MaxTurnaroundGuardMs = 1000;
    public const int DefaultWatchdogSeconds = 10;
    public const int MinWatchdogSeconds = 1;
    public const int MaxWatchdogSeconds = 60;

    /// <summary>A longer TXTAIL than this delays hearing the digipeat on a simplex satellite.</summary>
    public const int LongHalfDuplexTailMs = 50;

    /// <summary>Durable PortAudio device names; empty uses the system default.</summary>
    public string InputDeviceId { get; set; } = "";
    public string InputDeviceDisplayName { get; set; } = "";
    public string OutputDeviceId { get; set; } = "";
    public string OutputDeviceDisplayName { get; set; } = "";

    /// <summary>Output level, 0.01 to 1. Set so the radio deviates about 3 kHz.</summary>
    public double TxLevel { get; set; } = DefaultTxLevel;

    public TerminalDuplexMode Duplex { get; set; } = TerminalDuplexMode.Auto;

    /// <summary>In full duplex, still wait for a clear downlink before keying (off by default on crossband).</summary>
    public bool WaitForClearChannelInFullDuplex { get; set; }

    /// <summary>p-persistence (0 to 255): chance out of 256 of keying in each clear slot. Half duplex only.</summary>
    public int Persist { get; set; } = DefaultPersist;

    public int SlotTimeMs { get; set; } = DefaultSlotTimeMs;

    /// <summary>Give up on a busy channel after this long and report it, rather than wait for the whole pass.</summary>
    public int MaxChannelWaitMs { get; set; } = DefaultMaxChannelWaitMs;

    /// <summary>Half duplex: ignore receive audio for this long after unkeying (soundcard latency and the radio's TX to RX switch).</summary>
    public int TurnaroundGuardMs { get; set; } = DefaultTurnaroundGuardMs;

    public AfskBitFixMode BitFix { get; set; } = AfskBitFixMode.SingleBit;

    /// <summary>Unkey after this long on the air whatever is still queued.</summary>
    public int WatchdogSeconds { get; set; } = DefaultWatchdogSeconds;

    /// <summary>Refuse to transmit while the focused satellite is below the horizon.</summary>
    public bool BlockTxBelowHorizon { get; set; } = true;

    public PttMethod PttMethod { get; set; } = PttMethod.Vox;
    public PttLine PttLine { get; set; } = PttLine.Rts;
    public bool PttInvert { get; set; }
    public string SeparatePttPort { get; set; } = "";

    /// <summary>Extra wait after PTT before audio; the TXDELAY flags already cover the radio's key-up time.</summary>
    public int PttLeadMs { get; set; }

    /// <summary>Extra PTT hold after the audio has drained; TXTAIL flags already cover the end of the frame.</summary>
    public int PttTailMs { get; set; }

    public PttOptions ToPttOptions() =>
        new(PttMethod, PttLine, PttInvert, SeparatePttPort ?? "", PttLeadMs, PttTailMs);

    public void Normalise()
    {
        InputDeviceId ??= "";
        InputDeviceDisplayName ??= "";
        OutputDeviceId ??= "";
        OutputDeviceDisplayName ??= "";
        SeparatePttPort = (SeparatePttPort ?? "").Trim();
        TxLevel = double.IsFinite(TxLevel) ? Math.Clamp(TxLevel, 0.01, 1.0) : DefaultTxLevel;
        if (!Enum.IsDefined(Duplex))
            Duplex = TerminalDuplexMode.Auto;
        if (!Enum.IsDefined(BitFix))
            BitFix = AfskBitFixMode.SingleBit;
        if (!Enum.IsDefined(PttMethod))
            PttMethod = PttMethod.Vox;
        if (!Enum.IsDefined(PttLine))
            PttLine = PttLine.Rts;
        Persist = Math.Clamp(Persist, 0, 255);
        SlotTimeMs = Math.Clamp(SlotTimeMs, 10, 1000);
        MaxChannelWaitMs = Math.Clamp(MaxChannelWaitMs, 0, MaxChannelWaitLimitMs);
        TurnaroundGuardMs = Math.Clamp(TurnaroundGuardMs, 0, MaxTurnaroundGuardMs);
        WatchdogSeconds = Math.Clamp(WatchdogSeconds, MinWatchdogSeconds, MaxWatchdogSeconds);
        PttLeadMs = Math.Clamp(PttLeadMs, 0, PttOptions.MaxTimingMs);
        PttTailMs = Math.Clamp(PttTailMs, 0, PttOptions.MaxTimingMs);
    }
}

/// <summary>KISS over TCP, for example Direwolf's <c>KISSPORT</c>.</summary>
public sealed class TerminalKissTcpSettings
{
    public const int DefaultPort = 8001;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = DefaultPort;
}

/// <summary>KISS serial TNC on a COM port (8 data bits, no parity, one stop bit).</summary>
public sealed class TerminalKissSerialSettings
{
    public const int DefaultBaudRate = 9600;
    public static readonly IReadOnlyList<int> CommonBaudRates = [1200, 4800, 9600, 19200, 38400, 57600, 115200];

    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = DefaultBaudRate;
}

/// <summary>KISS parameters sent on connect, shared by KISS TCP and KISS serial.</summary>
public sealed class TerminalKissSettings
{
    public const int DefaultPersistence = 63;
    public const int DefaultSlotTimeMs = 100;
    public const int MaxTimingMs = 2550;

    /// <summary>KISS port nibble (0 to 15) frames are sent to and accepted from.</summary>
    public int Port { get; set; }

    /// <summary>p-persistence (0 to 255): chance out of 256 of keying in each clear slot.</summary>
    public int Persistence { get; set; } = DefaultPersistence;

    public int SlotTimeMs { get; set; } = DefaultSlotTimeMs;

    public bool FullDuplex { get; set; }

    public void Normalise()
    {
        Port = Math.Clamp(Port, 0, KissCodec.MaxPort);
        Persistence = Math.Clamp(Persistence, 0, 255);
        SlotTimeMs = Math.Clamp(SlotTimeMs, 0, MaxTimingMs);
    }
}

public sealed class TerminalSatelliteProfileSettings
{
    public string NoradId { get; set; } = "";
    public string Name { get; set; } = "";
    public TerminalMessageFormat Format { get; set; } = TerminalMessageFormat.Aprs;
    public string Path { get; set; } = "";
    public string UhmName { get; set; } = "";
    public string ModemHint { get; set; } = "";

    /// <summary>Forces the link type; null works it out from the satellite's frequencies.</summary>
    public TerminalLinkType? LinkType { get; set; }
}

public sealed class TerminalMacro
{
    public string Label { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>When true the macro sets To to <c>CQ</c> before sending.</summary>
    public bool SendsToCq { get; set; }
}

public sealed class TerminalAprsIsSettings
{
    public bool Enabled { get; set; }
    public string Server { get; set; } = "rotate.aprs2.net";
    public int Port { get; set; } = 14580;
    public string Passcode { get; set; } = "";
}

public sealed class TerminalUhmSettings
{
    public bool Enabled { get; set; }

    /// <summary>Send the message text on match checks (frame-level confirmation). Off means callsign-pair only.</summary>
    public bool FrameLevelMatch { get; set; }
}

/// <summary>How precisely Send position reports our location. Ambiguity blanks minute digits as APRS allows.</summary>
public enum TerminalPositionPrecision
{
    Exact,

    /// <summary>Ambiguity 2: whole minutes, about 2 km.</summary>
    About2Km,

    /// <summary>Ambiguity 3: tens of minutes, about 20 km.</summary>
    About20Km,

    /// <summary>Centre of the 4-character grid square.</summary>
    GridSquare
}

/// <summary>OscarWatch Terminal (packet digipeater) settings.</summary>
public sealed class TerminalSettings
{
    public const int MacroCount = 8;
    public const string DefaultPositionSymbol = "/[";
    public const string DefaultPositionComment = "{MYGRID4}";
    public const int DefaultMapFadeMinutes = 15;
    public const int MinMapFadeMinutes = 1;
    public const int MaxMapFadeMinutes = 120;

    public TerminalTransportKind Transport { get; set; } = TerminalTransportKind.BuiltIn;
    public TerminalAgwpeSettings Agwpe { get; set; } = new();
    public TerminalKissTcpSettings KissTcp { get; set; } = new();
    public TerminalKissSerialSettings KissSerial { get; set; } = new();
    public TerminalKissSettings Kiss { get; set; } = new();
    public TerminalBuiltInModemSettings BuiltIn { get; set; } = new();

    /// <summary>Zero-based radio port frames are sent to and accepted from: the AGWPE port, the KISS port, or 0 for the built-in modem.</summary>
    [JsonIgnore]
    public int ActiveRadioPort => Transport switch
    {
        TerminalTransportKind.Agwpe => Agwpe.RadioPort,
        TerminalTransportKind.KissTcp or TerminalTransportKind.KissSerial => Kiss.Port,
        _ => 0
    };

    [JsonIgnore]
    public bool IsKissTransport => Transport is TerminalTransportKind.KissTcp or TerminalTransportKind.KissSerial;

    /// <summary>Terminal callsign; empty uses the ground station callsign.</summary>
    public string Callsign { get; set; } = "";
    public int Ssid { get; set; }

    public List<TerminalSatelliteProfileSettings> SatelliteProfiles { get; set; } = [];
    public List<TerminalMacro> Macros { get; set; } = DefaultMacros();

    /// <summary>Message log highlight colours (hex). Each kind also shows a text tag.</summary>
    public string ToMeColour { get; set; } = "#FFF59D";
    public string CqColour { get; set; } = "#F8BBD0";
    public string MyTxColour { get; set; } = "#FFCC80";
    public string MyDigipeatedColour { get; set; } = "#FFAB91";

    public bool AutoLog { get; set; } = true;
    public bool ScrollToLastLine { get; set; } = true;
    public bool ShowCallers { get; set; } = true;

    /// <summary>The station panel shows the audio waterfall tab instead of station info.</summary>
    public bool ShowWaterfall { get; set; }
    public int TxDelaySeconds { get; set; }

    public const int DefaultModemTxDelayMs = 300;
    public const int DefaultModemTxTailMs = 100;
    public const int MaxModemTimingMs = 2000;

    /// <summary>
    /// The modem's own TXDELAY and TXTAIL. A single-VFO radio on a same-band satellite (FT-847)
    /// stays on the uplink for this long plus the frame's airtime.
    /// </summary>
    public int ModemTxDelayMs { get; set; } = DefaultModemTxDelayMs;
    public int ModemTxTailMs { get; set; } = DefaultModemTxTailMs;

    public const int MinFontSize = 10;
    public const int MaxFontSize = 28;
    public const int DefaultLogFontSize = 13;
    public const int DefaultStationFontSize = 12;

    /// <summary>Message log text size.</summary>
    public int FontSize { get; set; } = DefaultLogFontSize;
    public int QsoFontSize { get; set; } = DefaultStationFontSize;
    public int HeardFontSize { get; set; } = DefaultStationFontSize;

    /// <summary>Table column widths (see <see cref="TerminalTableLayout"/>). Empty means the defaults.</summary>
    public List<double> LogColumnWidths { get; set; } = [];
    public List<double> QsoColumnWidths { get; set; } = [];
    public List<double> HeardColumnWidths { get; set; } = [];

    /// <summary>Width of the QSO and heard stations column, and the share of its height given to QSO.</summary>
    public double CallersWidth { get; set; } = TerminalTableLayout.DefaultCallersWidth;
    public double QsoShare { get; set; } = TerminalTableLayout.DefaultQsoShare;

    public const int MinWindowWidth = 960;
    public const int MinWindowHeight = 600;

    /// <summary>Last normal (not maximised) Terminal window size and position. Null until the window has been closed once.</summary>
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool WindowMaximised { get; set; }

    /// <summary>Reply to APRS messages that carry a message number with an ack frame.</summary>
    public bool AprsAutoAck { get; set; }

    public TerminalAprsIsSettings AprsIs { get; set; } = new();
    public TerminalUhmSettings Uhm { get; set; } = new();

    /// <summary>Send position: precision, APRS symbol (table then code) and comment template (macro placeholders allowed).</summary>
    public TerminalPositionPrecision PositionPrecision { get; set; } = TerminalPositionPrecision.About2Km;
    public string PositionSymbol { get; set; } = DefaultPositionSymbol;
    public string PositionComment { get; set; } = DefaultPositionComment;

    /// <summary>Pass map: show only stations heard since AOS, and minutes before a station fades (dropped after twice this).</summary>
    public bool MapThisPassOnly { get; set; }
    public int MapFadeMinutes { get; set; } = DefaultMapFadeMinutes;
    public int? MapWindowWidth { get; set; }
    public int? MapWindowHeight { get; set; }
    public int? MapWindowX { get; set; }
    public int? MapWindowY { get; set; }

    public static List<TerminalMacro> DefaultMacros() =>
    [
        new() { Label = "CQ", Text = "CQ {MYGRID4}", SendsToCq = true },
        new() { Label = "Report", Text = "{MYGRID4} 599" },
        new() { Label = "R73", Text = "R73" },
        new() { Label = "73", Text = "73 TU" },
        new() { Label = "", Text = "" },
        new() { Label = "", Text = "" },
        new() { Label = "", Text = "" },
        new() { Label = "", Text = "" },
    ];

    /// <summary>Fills missing sections after loading older settings and clamps numeric values.</summary>
    public void Normalise()
    {
        Agwpe ??= new TerminalAgwpeSettings();
        Agwpe.Host = string.IsNullOrWhiteSpace(Agwpe.Host) ? "127.0.0.1" : Agwpe.Host.Trim();
        Agwpe.Port = Agwpe.Port is > 0 and <= 65535 ? Agwpe.Port : TerminalAgwpeSettings.DefaultPort;
        Agwpe.RadioPort = Math.Clamp(Agwpe.RadioPort, 0, 255);
        KissTcp ??= new TerminalKissTcpSettings();
        KissTcp.Host = string.IsNullOrWhiteSpace(KissTcp.Host) ? "127.0.0.1" : KissTcp.Host.Trim();
        KissTcp.Port = KissTcp.Port is > 0 and <= 65535 ? KissTcp.Port : TerminalKissTcpSettings.DefaultPort;
        KissSerial ??= new TerminalKissSerialSettings();
        KissSerial.PortName = (KissSerial.PortName ?? "").Trim();
        KissSerial.BaudRate = KissSerial.BaudRate is >= 300 and <= 921600
            ? KissSerial.BaudRate
            : TerminalKissSerialSettings.DefaultBaudRate;
        Kiss ??= new TerminalKissSettings();
        Kiss.Normalise();
        BuiltIn ??= new TerminalBuiltInModemSettings();
        BuiltIn.Normalise();
        if (!Enum.IsDefined(Transport))
            Transport = TerminalTransportKind.BuiltIn;
        Callsign ??= "";
        Ssid = Math.Clamp(Ssid, 0, 15);
        SatelliteProfiles ??= [];
        SatelliteProfiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.NoradId));
        Macros ??= [];
        Macros.RemoveAll(m => m is null);
        while (Macros.Count < MacroCount)
            Macros.Add(new TerminalMacro());
        if (Macros.Count > MacroCount)
            Macros.RemoveRange(MacroCount, Macros.Count - MacroCount);
        foreach (var m in Macros)
        {
            m.Label ??= "";
            m.Text ??= "";
        }

        TxDelaySeconds = Math.Clamp(TxDelaySeconds, 0, 60);
        ModemTxDelayMs = Math.Clamp(ModemTxDelayMs, 0, MaxModemTimingMs);
        ModemTxTailMs = Math.Clamp(ModemTxTailMs, 0, MaxModemTimingMs);
        FontSize = ClampFontSize(FontSize, DefaultLogFontSize);
        QsoFontSize = ClampFontSize(QsoFontSize, DefaultStationFontSize);
        HeardFontSize = ClampFontSize(HeardFontSize, DefaultStationFontSize);
        LogColumnWidths = TerminalTableLayout.Normalise(
            TerminalTableLayout.MigrateLogColumns(LogColumnWidths), TerminalTableLayout.DefaultLogColumns);
        QsoColumnWidths = TerminalTableLayout.Normalise(QsoColumnWidths, TerminalTableLayout.DefaultQsoColumns);
        HeardColumnWidths = TerminalTableLayout.Normalise(
            TerminalTableLayout.MigrateHeardColumns(HeardColumnWidths), TerminalTableLayout.DefaultHeardColumns);
        CallersWidth = TerminalTableLayout.ClampCallersWidth(CallersWidth);
        QsoShare = TerminalTableLayout.ClampQsoShare(QsoShare);
        if (WindowWidth is not { } w || WindowHeight is not { } h || w <= 0 || h <= 0)
        {
            WindowWidth = WindowHeight = null;
        }
        else
        {
            WindowWidth = Math.Max(w, MinWindowWidth);
            WindowHeight = Math.Max(h, MinWindowHeight);
        }
        AprsIs ??= new TerminalAprsIsSettings();
        AprsIs.Port = AprsIs.Port is > 0 and <= 65535 ? AprsIs.Port : 14580;
        AprsIs.Server ??= "";
        AprsIs.Passcode ??= "";
        Uhm ??= new TerminalUhmSettings();
        if (!Enum.IsDefined(PositionPrecision))
            PositionPrecision = TerminalPositionPrecision.About2Km;
        PositionSymbol = NormalisePositionSymbol(PositionSymbol);
        PositionComment ??= DefaultPositionComment;
        MapFadeMinutes = MapFadeMinutes <= 0 ? DefaultMapFadeMinutes : Math.Clamp(MapFadeMinutes, MinMapFadeMinutes, MaxMapFadeMinutes);
        if (MapWindowWidth is not { } mw || MapWindowHeight is not { } mh || mw <= 0 || mh <= 0)
            MapWindowWidth = MapWindowHeight = null;
    }

    /// <summary>Two printable characters (symbol table then symbol code), otherwise the default.</summary>
    public static string NormalisePositionSymbol(string? symbol)
    {
        var s = (symbol ?? "").Trim();
        return s.Length == 2 && s.All(c => c is >= '!' and <= '~') ? s : DefaultPositionSymbol;
    }

    /// <summary>Older settings files have no value (0) for newer sizes, so those fall back to the default.</summary>
    public static int ClampFontSize(int size, int fallback) =>
        size <= 0 ? fallback : Math.Clamp(size, MinFontSize, MaxFontSize);

    /// <summary>Effective callsign (Terminal override or ground station) without SSID.</summary>
    public string EffectiveCallsign(string? groundStationCallsign)
    {
        var call = string.IsNullOrWhiteSpace(Callsign) ? groundStationCallsign : Callsign;
        return TerminalText.BaseCallsign(call);
    }
}
