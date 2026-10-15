using OscarWatch.Core.Terminal;

namespace OscarWatch.Terminal;

/// <summary>Builds the modem transport selected in <see cref="TerminalSettings.Transport"/>.</summary>
public static class TerminalTransportFactory
{
    public static bool IsSupported(TerminalTransportKind kind) =>
        kind is TerminalTransportKind.Agwpe or TerminalTransportKind.BuiltIn
            or TerminalTransportKind.KissTcp or TerminalTransportKind.KissSerial;

    /// <param name="createBuiltIn">Builds the built-in modem with its soundcard and PTT keyer.</param>
    /// <param name="describeSerialConflict">Returns a message when the KISS serial COM port is used by another device.</param>
    public static ITerminalModemTransport Create(
        TerminalSettings settings,
        string callsign,
        Func<BuiltInModemTransport>? createBuiltIn = null,
        Func<string, string?>? describeSerialConflict = null) =>
        settings.Transport switch
        {
            TerminalTransportKind.Agwpe => new AgwpeClient(settings.Agwpe.Host, settings.Agwpe.Port, callsign),
            TerminalTransportKind.KissTcp => KissTransport.Tcp(
                settings.KissTcp.Host, settings.KissTcp.Port, KissParameters.From(settings)),
            TerminalTransportKind.KissSerial => KissTransport.Serial(
                settings.KissSerial.PortName, settings.KissSerial.BaudRate, KissParameters.From(settings), describeSerialConflict),
            TerminalTransportKind.BuiltIn when createBuiltIn is not null => createBuiltIn(),
            _ => throw new NotSupportedException($"Terminal transport {settings.Transport} is not available yet.")
        };
}
