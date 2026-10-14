using System.Text;

namespace OscarWatch.Core.Terminal;

/// <summary>
/// AX.25 frame without FCS, the form carried by AGWPE raw frames, KISS data frames and (after HDLC
/// deframing) a built-in modem. Only UI frames carry text; other frame types keep control bytes only.
/// </summary>
public sealed class Ax25Frame
{
    public const byte UiControl = 0x03;
    public const byte NoLayer3Pid = 0xF0;
    public const int MaxDigipeaters = 8;

    public Ax25Frame(
        Ax25Address destination,
        Ax25Address source,
        IReadOnlyList<Ax25Address> digipeaters,
        byte control,
        byte? pid,
        byte[] info)
    {
        if (digipeaters.Count > MaxDigipeaters)
            throw new ArgumentException($"At most {MaxDigipeaters} digipeaters are allowed.", nameof(digipeaters));

        Destination = destination;
        Source = source;
        Digipeaters = digipeaters;
        Control = control;
        Pid = pid;
        Info = info;
    }

    public Ax25Address Destination { get; }
    public Ax25Address Source { get; }
    public IReadOnlyList<Ax25Address> Digipeaters { get; }
    public byte Control { get; }
    public byte? Pid { get; }
    public byte[] Info { get; }

    /// <summary>True for UI (unnumbered information) frames, ignoring the P/F bit.</summary>
    public bool IsUi => (Control & 0xEF) == UiControl;

    /// <summary>True when any digipeater in the path has its H bit set.</summary>
    public bool HasBeenDigipeated => Digipeaters.Any(d => d.HasBeenRepeated);

    /// <summary>Info field as display text: UTF-8, control characters replaced, trailing CR/LF removed.</summary>
    public string InfoText => SanitiseText(Info);

    /// <summary>Builds a UI frame with PID F0 (no layer 3).</summary>
    public static Ax25Frame CreateUi(
        Ax25Address destination,
        Ax25Address source,
        IReadOnlyList<Ax25Address> digipeaters,
        string info) =>
        new(destination, source, digipeaters, UiControl, NoLayer3Pid, Encoding.UTF8.GetBytes(info));

    /// <summary>Parses a comma or space separated digipeater path such as <c>ARISS</c> or <c>WIDE1-1,WIDE2-1</c>.</summary>
    public static IReadOnlyList<Ax25Address> ParsePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        var list = new List<Ax25Address>();
        foreach (var part in path.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Ax25Address.TryParse(part, out var address))
                list.Add(address with { HasBeenRepeated = false });
            if (list.Count == MaxDigipeaters)
                break;
        }

        return list;
    }

    public byte[] Encode()
    {
        var addressCount = 2 + Digipeaters.Count;
        var pidLength = Pid.HasValue ? 1 : 0;
        var buffer = new byte[addressCount * Ax25Address.EncodedLength + 1 + pidLength + Info.Length];
        var span = buffer.AsSpan();

        Destination.Encode(span[..7], highBit: true, last: false);
        Source.Encode(span.Slice(7, 7), highBit: false, last: Digipeaters.Count == 0);
        for (var i = 0; i < Digipeaters.Count; i++)
        {
            var digi = Digipeaters[i];
            digi.Encode(
                span.Slice((2 + i) * 7, 7),
                highBit: digi.HasBeenRepeated,
                last: i == Digipeaters.Count - 1);
        }

        var offset = addressCount * 7;
        buffer[offset++] = Control;
        if (Pid.HasValue)
            buffer[offset++] = Pid.Value;
        Info.CopyTo(span[offset..]);
        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out Ax25Frame? frame)
    {
        frame = null;
        if (data.Length < 15)
            return false;

        var addresses = new List<Ax25Address>(4);
        var offset = 0;
        var last = false;
        while (!last)
        {
            if (offset + 7 > data.Length || addresses.Count >= 2 + MaxDigipeaters)
                return false;

            var address = Ax25Address.Decode(data.Slice(offset, 7), out var highBit, out last);
            if (address.Callsign.Length == 0)
                return false;

            // H bit only means "repeated" for digipeaters; C bits on destination and source are not exposed.
            if (addresses.Count >= 2)
                address = address with { HasBeenRepeated = highBit };
            addresses.Add(address);
            offset += 7;
        }

        if (addresses.Count < 2 || offset >= data.Length)
            return false;

        var control = data[offset++];
        byte? pid = null;
        var isInfoBearing = (control & 0x01) == 0 || (control & 0xEF) == UiControl;
        if (isInfoBearing)
        {
            if (offset >= data.Length)
                return false;
            pid = data[offset++];
        }

        var info = isInfoBearing ? data[offset..].ToArray() : [];
        frame = new Ax25Frame(addresses[0], addresses[1], addresses.Skip(2).ToList(), control, pid, info);
        return true;
    }

    /// <summary>TNC2 monitor format, for example <c>G0ABC&gt;APRS,RS0ISS*:CQ</c>. The last repeated digipeater gets <c>*</c>.</summary>
    public string ToMonitorString()
    {
        var sb = new StringBuilder();
        sb.Append(Source.Text).Append('>').Append(Destination.Text);
        var lastRepeated = -1;
        for (var i = 0; i < Digipeaters.Count; i++)
        {
            if (Digipeaters[i].HasBeenRepeated)
                lastRepeated = i;
        }

        for (var i = 0; i < Digipeaters.Count; i++)
        {
            sb.Append(',').Append(Digipeaters[i].Text);
            if (i == lastRepeated)
                sb.Append('*');
        }

        sb.Append(':').Append(InfoText);
        return sb.ToString();
    }

    /// <summary>Monitor path text without the info field, for example <c>ARISS*</c>.</summary>
    public string PathText()
    {
        var lastRepeated = -1;
        for (var i = 0; i < Digipeaters.Count; i++)
        {
            if (Digipeaters[i].HasBeenRepeated)
                lastRepeated = i;
        }

        return string.Join(',', Digipeaters.Select((d, i) => i == lastRepeated ? d.Text + "*" : d.Text));
    }

    internal static string SanitiseText(ReadOnlySpan<byte> bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(char.IsControl(c) ? ' ' : c);
        return sb.ToString().TrimEnd();
    }
}
