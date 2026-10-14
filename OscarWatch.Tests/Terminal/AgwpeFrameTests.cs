using System.Buffers.Binary;
using System.Text;
using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class AgwpeFrameTests
{
    [Fact]
    public void Header_layout_matches_agwpe_spec()
    {
        var bytes = new AgwpeFrame(1, 'X', 0xF0, "MM9SQL", "G0ABC", [1, 2, 3]).Encode();

        Assert.Equal(AgwpeFrame.HeaderLength + 3, bytes.Length);
        Assert.Equal(1, bytes[0]);
        Assert.Equal((byte)'X', bytes[4]);
        Assert.Equal(0xF0, bytes[6]);
        Assert.Equal("MM9SQL", Encoding.ASCII.GetString(bytes, 8, 6));
        Assert.Equal(0, bytes[14]);
        Assert.Equal("G0ABC", Encoding.ASCII.GetString(bytes, 18, 5));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28, 4)));
        Assert.Equal([1, 2, 3], bytes[36..]);
    }

    [Fact]
    public void Reader_round_trips_frames_split_across_reads()
    {
        var a = new AgwpeFrame(0, 'K', 0, "", "", [0, 9, 8, 7]).Encode();
        var b = AgwpeFrame.PortInfoRequest().Encode();
        var stream = a.Concat(b).ToArray();

        var reader = new AgwpeFrameReader();
        var first = reader.Append(stream.AsSpan(0, 20));
        Assert.Empty(first);
        var rest = reader.Append(stream.AsSpan(20));

        Assert.Equal(2, rest.Count);
        Assert.Equal('K', rest[0].Kind);
        Assert.Equal([9, 8, 7], rest[0].RawAx25Payload.ToArray());
        Assert.Equal('G', rest[1].Kind);
        Assert.Empty(rest[1].Data);
    }

    [Fact]
    public void Reader_rejects_oversized_length()
    {
        var header = new byte[AgwpeFrame.HeaderLength];
        header[4] = (byte)'K';
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28, 4), 10_000_000);

        var reader = new AgwpeFrameReader();
        Assert.Throws<InvalidDataException>(() => reader.Append(header));
    }

    [Fact]
    public void Raw_send_prefixes_port_byte()
    {
        var frame = AgwpeFrame.RawSend(2, [0xAA, 0xBB]);
        Assert.Equal(2, frame.Port);
        Assert.Equal('K', frame.Kind);
        Assert.Equal([0, 0xAA, 0xBB], frame.Data);
    }

    [Fact]
    public void Parses_port_info_reply()
    {
        var ports = AgwpeFrame.ParsePortInfo(Encoding.ASCII.GetBytes("2;Port1 1200 AFSK;Port2 BPSK 1200;\0"));
        Assert.Equal(["Port1 1200 AFSK", "Port2 BPSK 1200"], ports);
    }

    [Fact]
    public void Parses_uz7ho_port_info_reply_with_padding()
    {
        var ports = AgwpeFrame.ParsePortInfo(Encoding.ASCII.GetBytes("1;Port1 with SoundCard Ch: A;                     "));
        Assert.Equal(["Port1 with SoundCard Ch: A"], ports);
    }

    [Fact]
    public void Parses_version_reply()
    {
        var data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data, 2005);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 127);
        Assert.True(AgwpeFrame.TryParseVersion(data, out var major, out var minor));
        Assert.Equal(2005, major);
        Assert.Equal(127, minor);
    }
}
