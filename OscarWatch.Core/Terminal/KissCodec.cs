namespace OscarWatch.Core.Terminal;

/// <summary>KISS command nibble (low four bits of the first byte of every frame).</summary>
public enum KissCommand : byte
{
    Data = 0x00,
    TxDelay = 0x01,
    Persistence = 0x02,
    SlotTime = 0x03,
    TxTail = 0x04,
    FullDuplex = 0x05
}

/// <summary>One unescaped KISS frame: port (0 to 15), command, and the bytes after the command byte.</summary>
public sealed record KissFrame(int Port, KissCommand Command, byte[] Data);

/// <summary>
/// KISS TNC framing (Chepponis and Karn): FEND-delimited frames, FEND and FESC escaped in the body,
/// and a leading command byte carrying the port in the high nibble. Data frames carry AX.25 without FCS.
/// </summary>
public static class KissCodec
{
    public const byte Fend = 0xC0;
    public const byte Fesc = 0xDB;
    public const byte Tfend = 0xDC;
    public const byte Tfesc = 0xDD;

    public const int MaxPort = 15;

    /// <summary>Largest frame body accepted; anything bigger means the stream is out of step.</summary>
    public const int MaxFrameLength = 64 * 1024;

    public static byte CommandByte(int port, KissCommand command) =>
        (byte)(((Math.Clamp(port, 0, MaxPort) & 0x0F) << 4) | ((byte)command & 0x0F));

    /// <summary>FEND, command byte, escaped payload, FEND.</summary>
    public static byte[] Encode(int port, KissCommand command, ReadOnlySpan<byte> payload)
    {
        var output = new List<byte>(payload.Length + 8) { Fend };
        AppendEscaped(output, CommandByte(port, command));
        foreach (var b in payload)
            AppendEscaped(output, b);
        output.Add(Fend);
        return output.ToArray();
    }

    public static byte[] EncodeData(int port, ReadOnlySpan<byte> ax25Frame) =>
        Encode(port, KissCommand.Data, ax25Frame);

    /// <summary>A one-byte parameter command such as persistence or full duplex.</summary>
    public static byte[] EncodeParameter(int port, KissCommand command, int value) =>
        Encode(port, command, [(byte)Math.Clamp(value, 0, 255)]);

    /// <summary>TXDELAY, slot time and TXTAIL are sent in 10 ms units.</summary>
    public static byte[] EncodeTiming(int port, KissCommand command, int milliseconds) =>
        EncodeParameter(port, command, ToTenMsUnits(milliseconds));

    public static int ToTenMsUnits(int milliseconds) =>
        Math.Clamp((int)Math.Round(Math.Max(0, milliseconds) / 10.0, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>The parameter commands sent on connect, in TXDELAY, P, SlotTime, TXtail, FullDuplex order.</summary>
    public static IReadOnlyList<byte[]> ParameterFrames(
        int port, int txDelayMs, int persistence, int slotTimeMs, int txTailMs, bool fullDuplex) =>
    [
        EncodeTiming(port, KissCommand.TxDelay, txDelayMs),
        EncodeParameter(port, KissCommand.Persistence, persistence),
        EncodeTiming(port, KissCommand.SlotTime, slotTimeMs),
        EncodeTiming(port, KissCommand.TxTail, txTailMs),
        EncodeParameter(port, KissCommand.FullDuplex, fullDuplex ? 1 : 0),
    ];

    private static void AppendEscaped(List<byte> output, byte b)
    {
        switch (b)
        {
            case Fend:
                output.Add(Fesc);
                output.Add(Tfend);
                break;
            case Fesc:
                output.Add(Fesc);
                output.Add(Tfesc);
                break;
            default:
                output.Add(b);
                break;
        }
    }
}

/// <summary>Accumulates stream bytes and yields complete, unescaped KISS frames.</summary>
public sealed class KissFrameReader
{
    private readonly List<byte> _body = new(512);
    private bool _inFrame;
    private bool _escape;

    /// <summary>Appends received bytes and returns every frame they complete. Empty frames (FEND FEND) are skipped.</summary>
    public IReadOnlyList<KissFrame> Append(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<KissFrame>();
        foreach (var b in bytes)
        {
            if (b == KissCodec.Fend)
            {
                if (_inFrame && _body.Count > 0)
                    frames.Add(ToFrame());
                _body.Clear();
                _escape = false;
                _inFrame = true;
                continue;
            }

            if (!_inFrame)
                continue;

            if (_escape)
            {
                _escape = false;
                _body.Add(b switch
                {
                    KissCodec.Tfend => KissCodec.Fend,
                    KissCodec.Tfesc => KissCodec.Fesc,
                    _ => b,
                });
            }
            else if (b == KissCodec.Fesc)
            {
                _escape = true;
                continue;
            }
            else
            {
                _body.Add(b);
            }

            if (_body.Count > KissCodec.MaxFrameLength)
            {
                _body.Clear();
                _inFrame = false;
            }
        }

        return frames;
    }

    public void Reset()
    {
        _body.Clear();
        _inFrame = false;
        _escape = false;
    }

    private KissFrame ToFrame()
    {
        var type = _body[0];
        var data = _body.Count > 1 ? _body.GetRange(1, _body.Count - 1).ToArray() : [];
        return new KissFrame(type >> 4, (KissCommand)(type & 0x0F), data);
    }
}
