using System.Net;
using System.Net.Sockets;
using OscarWatch.Core.Terminal;
using OscarWatch.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class KissTransportTests
{
    [Fact]
    public async Task Tcp_sends_parameters_then_receives_and_sends_frames()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await using var transport = KissTransport.Tcp("127.0.0.1", port, new KissParameters(1, 300, 63, 100, 50, false));
        var received = new TaskCompletionSource<TerminalReceivedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.FrameReceived += f => received.TrySetResult(f);

        var acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(CancellationToken.None);
        using var server = await acceptTask;
        var stream = server.GetStream();
        var reader = new KissFrameReader();

        var parameters = await ReadFramesAsync(stream, reader, 5);
        Assert.Equal(
            [KissCommand.TxDelay, KissCommand.Persistence, KissCommand.SlotTime, KissCommand.TxTail, KissCommand.FullDuplex],
            parameters.Select(f => f.Command));
        Assert.All(parameters, f => Assert.Equal(1, f.Port));
        Assert.Equal([30], parameters[0].Data);
        Assert.Equal(TerminalTransportState.Connected, transport.State);
        Assert.Equal("KISS TCP", transport.ModemDescription);

        var ax25 = Ax25Frame.CreateUi(Ax25Address.Parse("CQ"), Ax25Address.Parse("G0ABC"), [], "JO45").Encode();
        await stream.WriteAsync(KissCodec.EncodeData(1, ax25));
        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ax25, frame.Ax25);
        Assert.Equal(1, frame.RadioPort);

        await transport.SendAsync(ax25, 1, CancellationToken.None);
        var sent = Assert.Single(await ReadFramesAsync(stream, reader, 1));
        Assert.Equal(KissCommand.Data, sent.Command);
        Assert.Equal(ax25, sent.Data);

        server.Close();
        await transport.WaitForDisconnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TerminalTransportState.Disconnected, transport.State);
    }

    [Fact]
    public async Task Serial_without_a_port_fails_to_connect()
    {
        await using var transport = KissTransport.Serial("", 9600, new KissParameters(0, 300, 63, 100, 50, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ConnectAsync(CancellationToken.None));
        Assert.Equal(TerminalTransportState.Error, transport.State);
    }

    [Fact]
    public async Task Serial_port_conflict_is_reported_before_opening()
    {
        await using var transport = KissTransport.Serial(
            "COM250", 9600, new KissParameters(0, 300, 63, 100, 50, false), _ => "in use by the radio");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ConnectAsync(CancellationToken.None));
        Assert.Equal("in use by the radio", ex.Message);
    }

    [Fact]
    public void Settings_normalise_kiss_values_and_use_the_kiss_port()
    {
        var settings = new TerminalSettings
        {
            Transport = TerminalTransportKind.KissSerial,
            KissTcp = new TerminalKissTcpSettings { Host = " ", Port = 0 },
            KissSerial = new TerminalKissSerialSettings { PortName = " COM7 ", BaudRate = 0 },
            Kiss = new TerminalKissSettings { Port = 20, Persistence = 400, SlotTimeMs = -5 },
        };

        settings.Normalise();

        Assert.Equal("127.0.0.1", settings.KissTcp.Host);
        Assert.Equal(TerminalKissTcpSettings.DefaultPort, settings.KissTcp.Port);
        Assert.Equal("COM7", settings.KissSerial.PortName);
        Assert.Equal(TerminalKissSerialSettings.DefaultBaudRate, settings.KissSerial.BaudRate);
        Assert.Equal(15, settings.Kiss.Port);
        Assert.Equal(255, settings.Kiss.Persistence);
        Assert.Equal(0, settings.Kiss.SlotTimeMs);
        Assert.Equal(15, settings.ActiveRadioPort);
        Assert.True(TerminalTransportFactory.IsSupported(TerminalTransportKind.KissTcp));
        Assert.IsType<KissTransport>(TerminalTransportFactory.Create(settings, "MM9SQL"));
    }

    private static async Task<List<KissFrame>> ReadFramesAsync(NetworkStream stream, KissFrameReader reader, int count)
    {
        var frames = new List<KissFrame>();
        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (frames.Count < count)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read <= 0)
                break;
            frames.AddRange(reader.Append(buffer.AsSpan(0, read)));
        }

        return frames;
    }
}
