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

    /// <summary>Log the contact when UHM reports mutual and both sides exchanged a grid or report.</summary>
    public bool LogWhenMutual { get; set; }
}

/// <summary>OscarWatch Terminal (packet digipeater) settings.</summary>
public sealed class TerminalSettings
{
    public const int MacroCount = 8;

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
        FontSize = ClampFontSize(FontSize, DefaultLogFontSize);
        QsoFontSize = ClampFontSize(QsoFontSize, DefaultStationFontSize);
        HeardFontSize = ClampFontSize(HeardFontSize, DefaultStationFontSize);
        LogColumnWidths = TerminalTableLayout.Normalise(LogColumnWidths, TerminalTableLayout.DefaultLogColumns);
        QsoColumnWidths = TerminalTableLayout.Normalise(QsoColumnWidths, TerminalTableLayout.DefaultQsoColumns);
        HeardColumnWidths = TerminalTableLayout.Normalise(HeardColumnWidths, TerminalTableLayout.DefaultHeardColumns);
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
