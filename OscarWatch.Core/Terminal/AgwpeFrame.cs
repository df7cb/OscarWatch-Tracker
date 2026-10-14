using System.Buffers.Binary;
using System.Text;

namespace OscarWatch.Core.Terminal;

/// <summary>
/// AGWPE TCP API frame: 36-byte little-endian header (port, kind, PID, CallFrom, CallTo, DataLen, user)
/// followed by <see cref="Data"/>. Direwolf and UZ7HO Soundmodem both implement this on port 8000.
/// </summary>
public sealed record AgwpeFrame(int Port, char Kind, byte Pid, string CallFrom, string CallTo, byte[] Data)
{
    public const int HeaderLength = 36;
    public const int CallFieldLength = 10;

    /// <summary>Largest data block accepted from a modem; anything bigger means the stream is out of step.</summary>
    public const int MaxDataLength = 64 * 1024;

    public const char KindVersion = 'R';
    public const char KindPortInfo = 'G';
    public const char KindRegister = 'X';
    public const char KindUnregister = 'x';
    public const char KindRawMonitorToggle = 'k';
    public const char KindRaw = 'K';

    public byte[] Encode()
    {
        var buffer = new byte[HeaderLength + Data.Length];
        var span = buffer.AsSpan();
        span[0] = (byte)Port;
        span[4] = (byte)Kind;
        span[6] = Pid;
        WriteCall(span.Slice(8, CallFieldLength), CallFrom);
        WriteCall(span.Slice(18, CallFieldLength), CallTo);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), (uint)Data.Length);
        Data.CopyTo(span[HeaderLength..]);
        return buffer;
    }

    /// <summary>Reads the header; returns false when fewer than 36 bytes are available.</summary>
    public static bool TryReadHeader(
        ReadOnlySpan<byte> header,
        out int port,
        out char kind,
        out byte pid,
        out string callFrom,
        out string callTo,
        out uint dataLength)
    {
        port = 0;
        kind = '\0';
        pid = 0;
        callFrom = "";
        callTo = "";
        dataLength = 0;
        if (header.Length < HeaderLength)
            return false;

        port = header[0];
        kind = (char)header[4];
        pid = header[6];
        callFrom = ReadCall(header.Slice(8, CallFieldLength));
        callTo = ReadCall(header.Slice(18, CallFieldLength));
        dataLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(28, 4));
        return true;
    }

    public static AgwpeFrame VersionRequest() => new(0, KindVersion, 0, "", "", []);

    public static AgwpeFrame PortInfoRequest() => new(0, KindPortInfo, 0, "", "", []);

    public static AgwpeFrame Register(string callsign) => new(0, KindRegister, 0, callsign, "", []);

    public static AgwpeFrame RawMonitorToggle() => new(0, KindRawMonitorToggle, 0, "", "", []);

    /// <summary>Raw transmit: one leading KISS-style port byte (zero), then the AX.25 frame without FCS.</summary>
    public static AgwpeFrame RawSend(int radioPort, ReadOnlySpan<byte> ax25Frame)
    {
        var data = new byte[ax25Frame.Length + 1];
        ax25Frame.CopyTo(data.AsSpan(1));
        return new AgwpeFrame(radioPort, KindRaw, 0, "", "", data);
    }

    /// <summary>For a received <c>K</c> frame, the AX.25 bytes after the leading port byte.</summary>
    public ReadOnlySpan<byte> RawAx25Payload => Data.Length > 1 ? Data.AsSpan(1) : ReadOnlySpan<byte>.Empty;

    /// <summary>Parses the <c>R</c> reply: two little-endian 32-bit integers, major then minor.</summary>
    public static bool TryParseVersion(ReadOnlySpan<byte> data, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (data.Length < 8)
            return false;
        major = BinaryPrimitives.ReadInt32LittleEndian(data[..4]);
        minor = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        return true;
    }

    /// <summary>Parses the <c>G</c> reply, for example <c>2;Port1 1200 AFSK;Port2 9600;</c>.</summary>
    public static IReadOnlyList<string> ParsePortInfo(ReadOnlySpan<byte> data)
    {
        var text = Encoding.ASCII.GetString(data).TrimEnd('\0');
        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var count))
            return [];

        return parts.Skip(1).Take(Math.Max(0, count)).ToList();
    }

    private static void WriteCall(Span<byte> destination, string call)
    {
        destination.Clear();
        var bytes = Encoding.ASCII.GetBytes(call ?? "");
        bytes.AsSpan(0, Math.Min(bytes.Length, CallFieldLength - 1)).CopyTo(destination);
    }

    private static string ReadCall(ReadOnlySpan<byte> source)
    {
        var end = source.IndexOf((byte)0);
        if (end < 0)
            end = source.Length;
        return Encoding.ASCII.GetString(source[..end]).Trim();
    }
}

/// <summary>Accumulates TCP bytes and yields complete AGWPE frames.</summary>
public sealed class AgwpeFrameReader
{
    private byte[] _buffer = new byte[4096];
    private int _count;

    /// <summary>Appends received bytes and returns every complete frame. Throws when a header claims an oversized block.</summary>
    public IReadOnlyList<AgwpeFrame> Append(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(_count + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_count));
        _count += bytes.Length;

        var frames = new List<AgwpeFrame>();
        var offset = 0;
        while (_count - offset >= AgwpeFrame.HeaderLength)
        {
            AgwpeFrame.TryReadHeader(
                _buffer.AsSpan(offset, AgwpeFrame.HeaderLength),
                out var port,
                out var kind,
                out var pid,
                out var callFrom,
                out var callTo,
                out var dataLength);

            if (dataLength > AgwpeFrame.MaxDataLength)
            {
                _count = 0;
                throw new InvalidDataException($"AGWPE frame data length {dataLength} is too large.");
            }

            var total = AgwpeFrame.HeaderLength + (int)dataLength;
            if (_count - offset < total)
                break;

            var data = _buffer.AsSpan(offset + AgwpeFrame.HeaderLength, (int)dataLength).ToArray();
            frames.Add(new AgwpeFrame(port, kind, pid, callFrom, callTo, data));
            offset += total;
        }

        if (offset > 0)
        {
            Buffer.BlockCopy(_buffer, offset, _buffer, 0, _count - offset);
            _count -= offset;
        }

        return frames;
    }

    public void Reset() => _count = 0;

    private void EnsureCapacity(int needed)
    {
        if (needed <= _buffer.Length)
            return;
        var size = _buffer.Length;
        while (size < needed)
            size *= 2;
        Array.Resize(ref _buffer, size);
    }
}
