using System.IO.Ports;
using System.Net.Sockets;
using OscarWatch.Core.Terminal;
using Serilog;

namespace OscarWatch.Terminal;

/// <summary>Parameters a KISS TNC is given on connect.</summary>
public sealed record KissParameters(int Port, int TxDelayMs, int Persistence, int SlotTimeMs, int TxTailMs, bool FullDuplex)
{
    public static KissParameters From(TerminalSettings settings) => new(
        settings.Kiss.Port,
        settings.ModemTxDelayMs,
        settings.Kiss.Persistence,
        settings.Kiss.SlotTimeMs,
        settings.ModemTxTailMs,
        settings.Kiss.FullDuplex);
}

/// <summary>Byte link under a KISS transport. <see cref="ReadAsync"/> returns 0 when the link has closed.</summary>
public interface IKissLink : IDisposable
{
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}

/// <summary>
/// KISS modem transport over any byte link (TCP to Direwolf, or a serial TNC). On connect it sends the
/// TXDELAY, persistence, slot time, TXTAIL and full duplex parameters. The TNC must already be in KISS mode;
/// no command is sent to enter or leave it.
/// </summary>
public sealed class KissTransport : ITerminalModemTransport
{
    private static readonly ILogger Log = Serilog.Log.ForContext<KissTransport>();

    private readonly Func<CancellationToken, Task<IKissLink>> _open;
    private readonly KissParameters _parameters;
    private readonly string _endpoint;
    private readonly string _description;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _gate = new();

    private IKissLink? _link;
    private CancellationTokenSource? _readCts;
    private TaskCompletionSource _disconnected = CompletedSource();
    private TerminalTransportState _state = TerminalTransportState.Disconnected;

    public KissTransport(
        TerminalTransportKind kind,
        string endpoint,
        string description,
        KissParameters parameters,
        Func<CancellationToken, Task<IKissLink>> open)
    {
        Kind = kind;
        _endpoint = endpoint;
        _description = description;
        _parameters = parameters;
        _open = open;
    }

    /// <summary>KISS over TCP, for example Direwolf on port 8001.</summary>
    public static KissTransport Tcp(string host, int port, KissParameters parameters)
    {
        host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        return new KissTransport(
            TerminalTransportKind.KissTcp,
            $"{host}:{port}",
            "KISS TCP",
            parameters,
            ct => KissTcpLink.OpenAsync(host, port, ct));
    }

    /// <summary>KISS serial TNC. <paramref name="describeConflict"/> returns a message when the COM port is already in use.</summary>
    public static KissTransport Serial(
        string portName,
        int baudRate,
        KissParameters parameters,
        Func<string, string?>? describeConflict = null)
    {
        portName = (portName ?? "").Trim();
        return new KissTransport(
            TerminalTransportKind.KissSerial,
            portName,
            "KISS serial",
            parameters,
            _ =>
            {
                if (portName.Length == 0)
                    throw new InvalidOperationException("Choose the COM port of the KISS TNC in Terminal settings.");
                if (describeConflict?.Invoke(portName) is { Length: > 0 } conflict)
                    throw new InvalidOperationException(conflict);
                return Task.FromResult<IKissLink>(KissSerialLink.Open(portName, baudRate));
            });
    }

    public TerminalTransportKind Kind { get; }

    public TerminalTransportCapabilities Capabilities { get; } = new(
        RadioPortList: false,
        OwnTxEcho: false,
        SignalQuality: false,
        TxQueueDepth: false);

    public TerminalTransportState State => _state;
    public string? ModemDescription { get; private set; }

    public event Action<TerminalTransportState>? StateChanged;
    public event Action<TerminalReceivedFrame>? FrameReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await CloseAsync().ConfigureAwait(false);
        SetState(TerminalTransportState.Connecting);

        IKissLink link;
        try
        {
            link = await _open(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            SetState(TerminalTransportState.Error);
            throw;
        }

        var readCts = new CancellationTokenSource();
        lock (_gate)
        {
            _link = link;
            _readCts = readCts;
            _disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = Task.Run(() => ReadLoopAsync(link, readCts.Token));

        try
        {
            var p = _parameters;
            foreach (var frame in KissCodec.ParameterFrames(p.Port, p.TxDelayMs, p.Persistence, p.SlotTimeMs, p.TxTailMs, p.FullDuplex))
                await WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await CloseAsync().ConfigureAwait(false);
            SetState(TerminalTransportState.Error);
            throw;
        }

        ModemDescription = _description;
        SetState(TerminalTransportState.Connected);
        Log.Information("{Description} connected to {Endpoint}", _description, _endpoint);
    }

    public Task WaitForDisconnectAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_gate)
            task = _disconnected.Task;
        return task.WaitAsync(cancellationToken);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> ax25Frame, int radioPort, CancellationToken cancellationToken)
    {
        if (_state != TerminalTransportState.Connected)
            throw new InvalidOperationException("The modem is not connected.");
        await WriteAsync(KissCodec.EncodeData(radioPort, ax25Frame.Span), cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<TerminalRadioPort>> GetRadioPortsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TerminalRadioPort>>([]);

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    private async Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        IKissLink? link;
        lock (_gate)
            link = _link;
        if (link is null)
            throw new InvalidOperationException("The modem is not connected.");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await link.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(IKissLink link, CancellationToken cancellationToken)
    {
        var reader = new KissFrameReader();
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await link.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                foreach (var frame in reader.Append(buffer.AsSpan(0, read)))
                {
                    if (frame.Command == KissCommand.Data && frame.Data.Length > 0)
                        FrameReceived?.Invoke(new TerminalReceivedFrame(frame.Data, DateTime.UtcNow, frame.Port));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
                                       or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "{Description} read loop ended", _description);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            SetState(TerminalTransportState.Disconnected);
            Log.Information("{Description} connection to {Endpoint} closed", _description, _endpoint);
        }

        TaskCompletionSource done;
        lock (_gate)
            done = _disconnected;
        done.TrySetResult();
    }

    private Task CloseAsync()
    {
        IKissLink? link;
        CancellationTokenSource? cts;
        TaskCompletionSource disconnected;
        lock (_gate)
        {
            link = _link;
            cts = _readCts;
            disconnected = _disconnected;
            _link = null;
            _readCts = null;
        }

        if (link is null)
            return Task.CompletedTask;

        try
        {
            cts?.Cancel();
            link.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "{Description} close", _description);
        }

        cts?.Dispose();
        disconnected.TrySetResult();
        SetState(TerminalTransportState.Disconnected);
        return Task.CompletedTask;
    }

    private void SetState(TerminalTransportState state)
    {
        if (_state == state)
            return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    private static TaskCompletionSource CompletedSource()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();
        return tcs;
    }
}

internal sealed class KissTcpLink : IKissLink
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private KissTcpLink(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<IKissLink> OpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return new KissTcpLink(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _stream.ReadAsync(buffer, cancellationToken);

    public async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Serial TNC link. Reads block on a worker thread with a short timeout so cancellation and close are noticed.</summary>
internal sealed class KissSerialLink : IKissLink
{
    private const int ReadTimeoutMs = 250;
    private const int WriteTimeoutMs = 2000;

    private readonly SerialPort _port;

    private KissSerialLink(SerialPort port) => _port = port;

    public static IKissLink Open(string portName, int baudRate)
    {
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = ReadTimeoutMs,
            WriteTimeout = WriteTimeoutMs,
            DtrEnable = true,
            RtsEnable = true
        };
        try
        {
            port.Open();
            port.DiscardInBuffer();
            return new KissSerialLink(port);
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            var chunk = new byte[buffer.Length];
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_port.IsOpen)
                    return 0;
                try
                {
                    var read = _port.Read(chunk, 0, chunk.Length);
                    if (read > 0)
                    {
                        chunk.AsSpan(0, read).CopyTo(buffer.Span);
                        return read;
                    }
                }
                catch (TimeoutException)
                {
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }, CancellationToken.None));

    public Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var array = bytes.ToArray();
            _port.Write(array, 0, array.Length);
        }, cancellationToken);

    public void Dispose() => _port.Dispose();
}
