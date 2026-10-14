using OscarWatch.Core.Terminal;

namespace OscarWatch.Terminal;

/// <summary>Builds the modem transport selected in <see cref="TerminalSettings.Transport"/>.</summary>
public static class TerminalTransportFactory
{
    public static bool IsSupported(TerminalTransportKind kind) => kind == TerminalTransportKind.Agwpe;

    public static ITerminalModemTransport Create(TerminalSettings settings, string callsign) =>
        settings.Transport switch
        {
            TerminalTransportKind.Agwpe => new AgwpeClient(settings.Agwpe.Host, settings.Agwpe.Port, callsign),
            _ => throw new NotSupportedException($"Terminal transport {settings.Transport} is not available yet.")
        };
}
