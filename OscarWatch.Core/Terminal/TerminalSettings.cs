namespace OscarWatch.Core.Terminal;

/// <summary>How OscarWatch Terminal reaches the packet modem. Only <see cref="Agwpe"/> is implemented today.</summary>
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

/// <summary>Reserved for the KISS over TCP transport.</summary>
public sealed class TerminalKissTcpSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8001;
}

/// <summary>Reserved for the KISS serial transport.</summary>
public sealed class TerminalKissSerialSettings
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
}

public sealed class TerminalSatelliteProfileSettings
{
    public string NoradId { get; set; } = "";
    public string Name { get; set; } = "";
    public TerminalMessageFormat Format { get; set; } = TerminalMessageFormat.Aprs;
    public string Path { get; set; } = "";
    public string UhmName { get; set; } = "";
    public string ModemHint { get; set; } = "";
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

    public TerminalTransportKind Transport { get; set; } = TerminalTransportKind.Agwpe;
    public TerminalAgwpeSettings Agwpe { get; set; } = new();
    public TerminalKissTcpSettings KissTcp { get; set; } = new();
    public TerminalKissSerialSettings KissSerial { get; set; } = new();

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
        KissSerial ??= new TerminalKissSerialSettings();
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
        LogColumnWidths = TerminalTableLayout.Normalise(LogColumnWidths, TerminalTableLayout.DefaultLogColumns);
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
