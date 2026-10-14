using System.Net;
using System.Net.Sockets;
using System.Text;
using OscarWatch.Core.Terminal;
using OscarWatch.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class AgwpeClientTests
{
    [Fact]
    public async Task Handshake_receive_ports_and_send_over_loopback()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await using var client = new AgwpeClient("127.0.0.1", port, "MM9SQL-1");
        var received = new TaskCompletionSource<TerminalReceivedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.FrameReceived += f => received.TrySetResult(f);

        var acceptTask = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(CancellationToken.None);
        using var server = await acceptTask;
        var stream = server.GetStream();
        var reader = new AgwpeFrameReader();

        var handshake = await ReadFramesAsync(stream, reader, 4);
        Assert.Equal(['R', 'G', 'X', 'k'], handshake.Select(f => f.Kind));
        Assert.Equal("MM9SQL", handshake[2].CallFrom);
        Assert.Equal(TerminalTransportState.Connected, client.State);

        await stream.WriteAsync(new AgwpeFrame(0, 'G', 0, "", "", Encoding.ASCII.GetBytes("1;Port1 with SoundCard Ch: A;")).Encode());
        var ports = await client.GetRadioPortsAsync(CancellationToken.None);
        Assert.Equal("Port1 with SoundCard Ch: A", Assert.Single(ports).Description);

        var ax25 = Ax25Frame.CreateUi(Ax25Address.Parse("CQ"), Ax25Address.Parse("G0ABC"), [], "JO45").Encode();
        await stream.WriteAsync(AgwpeFrame.RawSend(0, ax25).Encode());
        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ax25, frame.Ax25);

        await client.SendAsync(ax25, 0, CancellationToken.None);
        AgwpeFrame? sent = null;
        for (var i = 0; i < 3 && sent is null; i++)
            sent = (await ReadFramesAsync(stream, reader, 1)).FirstOrDefault(f => f.Kind == 'K');
        Assert.NotNull(sent);
        Assert.Equal(ax25, sent.RawAx25Payload.ToArray());

        server.Close();
        await client.WaitForDisconnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TerminalTransportState.Disconnected, client.State);
    }

    [Fact]
    public async Task Connect_failure_sets_error_state()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await using var client = new AgwpeClient("127.0.0.1", port, "MM9SQL");
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(CancellationToken.None));
        Assert.Equal(TerminalTransportState.Error, client.State);
    }

    private static async Task<List<AgwpeFrame>> ReadFramesAsync(NetworkStream stream, AgwpeFrameReader reader, int count)
    {
        var frames = new List<AgwpeFrame>();
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (frames.Count < count)
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            if (read == 0)
                break;
            frames.AddRange(reader.Append(buffer.AsSpan(0, read)));
        }

        return frames;
    }
}
