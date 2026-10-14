using OscarWatch.Core.Terminal;

namespace OscarWatch.Terminal;

public enum TerminalTransportState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>What a transport can report beyond plain frames, so the UI can hide unsupported columns.</summary>
public sealed record TerminalTransportCapabilities(
    bool RadioPortList,
    bool OwnTxEcho,
    bool SignalQuality,
    bool TxQueueDepth);

/// <summary>A received AX.25 frame (no FCS) plus optional modem metadata.</summary>
public sealed record TerminalReceivedFrame(
    byte[] Ax25,
    DateTime Utc,
    int RadioPort,
    int? Quality = null,
    string? DecoderName = null);

public sealed record TerminalRadioPort(int Index, string Description);

/// <summary>
/// Frame-level link to a packet modem. AGWPE today; KISS TCP, KISS serial and a built-in modem implement
/// the same contract so <see cref="TerminalService"/>, the codecs and the UI do not change.
/// </summary>
public interface ITerminalModemTransport : IAsyncDisposable
{
    TerminalTransportKind Kind { get; }
    TerminalTransportCapabilities Capabilities { get; }
    TerminalTransportState State { get; }

    /// <summary>Short modem description once connected, for example <c>AGWPE 2005.127</c>.</summary>
    string? ModemDescription { get; }

    event Action<TerminalTransportState>? StateChanged;
    event Action<TerminalReceivedFrame>? FrameReceived;

    /// <summary>Connects once. Completes when the link is up; throws when it cannot connect.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Completes when an established connection drops (or immediately when not connected).</summary>
    Task WaitForDisconnectAsync(CancellationToken cancellationToken);

    Task SendAsync(ReadOnlyMemory<byte> ax25Frame, int radioPort, CancellationToken cancellationToken);

    Task<IReadOnlyList<TerminalRadioPort>> GetRadioPortsAsync(CancellationToken cancellationToken);
}
