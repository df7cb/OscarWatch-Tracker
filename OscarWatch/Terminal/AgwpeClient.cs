using System.Net.Sockets;
using OscarWatch.Core.Terminal;
using Serilog;

namespace OscarWatch.Terminal;

/// <summary>
/// AGWPE TCP client for Direwolf and UZ7HO Soundmodem. On connect it asks for the version (<c>R</c>) and
/// radio ports (<c>G</c>), registers the callsign (<c>X</c>) and enables raw monitoring (<c>k</c>).
/// Frames are received and sent as raw AX.25 (<c>K</c>).
/// </summary>
public sealed class AgwpeClient : ITerminalModemTransport
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AgwpeClient>();
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PortListTimeout = TimeSpan.FromSeconds(3);

    private readonly string _host;
    private readonly int _port;
    private readonly string _callsign;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _gate = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private TaskCompletionSource _disconnected = CompletedSource();
    private TaskCompletionSource<IReadOnlyList<TerminalRadioPort>> _ports = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TerminalTransportState _state = TerminalTransportState.Disconnected;

    public AgwpeClient(string host, int port, string callsign)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = port;
        _callsign = TerminalText.BaseCallsign(callsign);
    }

    public TerminalTransportKind Kind => TerminalTransportKind.Agwpe;

    public TerminalTransportCapabilities Capabilities { get; } = new(
        RadioPortList: true,
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

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            SetState(TerminalTransportState.Error);
            throw;
        }

        var readCts = new CancellationTokenSource();
        lock (_gate)
        {
            _client = client;
            _stream = client.GetStream();
            _readCts = readCts;
            _disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ports = new TaskCompletionSource<IReadOnlyList<TerminalRadioPort>>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = Task.Run(() => ReadLoopAsync(client.GetStream(), readCts.Token));

        await WriteFrameAsync(AgwpeFrame.VersionRequest(), cancellationToken).ConfigureAwait(false);
        await WriteFrameAsync(AgwpeFrame.PortInfoRequest(), cancellationToken).ConfigureAwait(false);
        if (TerminalText.LooksLikeCallsign(_callsign))
            await WriteFrameAsync(AgwpeFrame.Register(_callsign), cancellationToken).ConfigureAwait(false);
        await WriteFrameAsync(AgwpeFrame.RawMonitorToggle(), cancellationToken).ConfigureAwait(false);

        ModemDescription = "AGWPE";
        SetState(TerminalTransportState.Connected);
        Log.Information("AGWPE connected to {Host}:{Port}", _host, _port);
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
        await WriteFrameAsync(AgwpeFrame.RawSend(radioPort, ax25Frame.Span), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TerminalRadioPort>> GetRadioPortsAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<TerminalRadioPort>> task;
        lock (_gate)
            task = _ports.Task;
        if (!task.IsCompleted && _state == TerminalTransportState.Connected)
            await WriteFrameAsync(AgwpeFrame.PortInfoRequest(), cancellationToken).ConfigureAwait(false);

        try
        {
            return await task.WaitAsync(PortListTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    private async Task WriteFrameAsync(AgwpeFrame frame, CancellationToken cancellationToken)
    {
        NetworkStream? stream;
        lock (_gate)
            stream = _stream;
        if (stream is null)
            throw new InvalidOperationException("The modem is not connected.");

        var bytes = frame.Encode();
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var reader = new AgwpeFrameReader();
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                foreach (var frame in reader.Append(buffer.AsSpan(0, read)))
                    HandleFrame(frame);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or ObjectDisposedException)
        {
            Log.Warning(ex, "AGWPE read loop ended");
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            SetState(TerminalTransportState.Disconnected);
            Log.Information("AGWPE connection to {Host}:{Port} closed", _host, _port);
        }

        TaskCompletionSource done;
        lock (_gate)
            done = _disconnected;
        done.TrySetResult();
    }

    private void HandleFrame(AgwpeFrame frame)
    {
        switch (frame.Kind)
        {
            case AgwpeFrame.KindRaw:
                if (frame.Data.Length > 1)
                {
                    FrameReceived?.Invoke(new TerminalReceivedFrame(
                        frame.RawAx25Payload.ToArray(),
                        DateTime.UtcNow,
                        frame.Port));
                }

                break;
            case AgwpeFrame.KindVersion:
                if (AgwpeFrame.TryParseVersion(frame.Data, out var major, out var minor))
                {
                    ModemDescription = $"AGWPE {major}.{minor}";
                    StateChanged?.Invoke(_state);
                }

                break;
            case AgwpeFrame.KindPortInfo:
            {
                var ports = AgwpeFrame.ParsePortInfo(frame.Data)
                    .Select((description, index) => new TerminalRadioPort(index, description))
                    .ToList();
                TaskCompletionSource<IReadOnlyList<TerminalRadioPort>> tcs;
                lock (_gate)
                    tcs = _ports;
                tcs.TrySetResult(ports);
                break;
            }
            case AgwpeFrame.KindRegister:
                if (frame.Data.Length > 0 && frame.Data[0] != 1)
                    Log.Information("AGWPE callsign registration for {Call} was refused", _callsign);
                break;
        }
    }

    private Task CloseAsync()
    {
        TcpClient? client;
        CancellationTokenSource? cts;
        TaskCompletionSource disconnected;
        lock (_gate)
        {
            client = _client;
            cts = _readCts;
            disconnected = _disconnected;
            _client = null;
            _stream = null;
            _readCts = null;
        }

        if (client is null)
            return Task.CompletedTask;

        try
        {
            cts?.Cancel();
            client.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AGWPE close");
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
