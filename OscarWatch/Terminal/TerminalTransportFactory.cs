using OscarWatch.Core.Terminal;

namespace OscarWatch.Terminal;

/// <summary>Builds the modem transport selected in <see cref="TerminalSettings.Transport"/>.</summary>
public static class TerminalTransportFactory
{
    public static bool IsSupported(TerminalTransportKind kind) =>
        kind is TerminalTransportKind.Agwpe or TerminalTransportKind.BuiltIn;

    /// <param name="createBuiltIn">Builds the built-in modem with its soundcard and PTT keyer.</param>
    public static ITerminalModemTransport Create(
        TerminalSettings settings,
        string callsign,
        Func<BuiltInModemTransport>? createBuiltIn = null) =>
        settings.Transport switch
        {
            TerminalTransportKind.Agwpe => new AgwpeClient(settings.Agwpe.Host, settings.Agwpe.Port, callsign),
            TerminalTransportKind.BuiltIn when createBuiltIn is not null => createBuiltIn(),
            _ => throw new NotSupportedException($"Terminal transport {settings.Transport} is not available yet.")
        };
}
