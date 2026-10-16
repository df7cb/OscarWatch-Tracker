namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Builds the tone sequence for one keyed transmission: TXDELAY flags, each frame with its FCS
/// (bit stuffed, sent least significant bit first) separated by a flag, then TXTAIL flags. NRZI:
/// a 0 bit changes tone, a 1 bit keeps it.
/// </summary>
public static class HdlcEncoder
{
    public const byte Flag = 0x7E;
    public const int Baud = 1200;
    public const int G3ruhBaud = 9600;

    /// <summary>Whole flags needed to fill <paramref name="milliseconds"/> at the given baud (at least one).</summary>
    public static int FlagsFor(int milliseconds, int baud = Baud) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, milliseconds) * baud / 8000.0));

    /// <summary>HDLC bits before NRZI. Flags are never stuffed; frame bytes and FCS are.</summary>
    public static List<bool> EncodeBits(IEnumerable<byte[]> ax25Frames, int preambleFlags, int tailFlags)
    {
        var bits = new List<bool>(4096);
        for (var i = 0; i < Math.Max(1, preambleFlags); i++)
            AppendByte(bits, Flag);

        foreach (var frame in ax25Frames)
        {
            var ones = 0;
            foreach (var b in Fcs16.Append(frame))
            {
                for (var bit = 0; bit < 8; bit++)
                {
                    var one = ((b >> bit) & 1) != 0;
                    bits.Add(one);
                    if (one)
                    {
                        if (++ones == 5)
                        {
                            bits.Add(false);
                            ones = 0;
                        }
                    }
                    else
                    {
                        ones = 0;
                    }
                }
            }

            AppendByte(bits, Flag);
        }

        for (var i = 1; i < Math.Max(1, tailFlags); i++)
            AppendByte(bits, Flag);
        return bits;
    }

    /// <summary>HDLC bits for <paramref name="count"/> flags.</summary>
    public static List<bool> FlagBits(int count)
    {
        var bits = new List<bool>(Math.Max(0, count) * 8);
        for (var i = 0; i < count; i++)
            AppendByte(bits, Flag);
        return bits;
    }

    /// <summary>NRZI: a 0 bit toggles state, a 1 bit holds it.</summary>
    public static bool[] ApplyNrzi(IReadOnlyList<bool> hdlcBits, bool startHigh = true)
    {
        var nrzi = new bool[hdlcBits.Count];
        var level = startHigh;
        for (var i = 0; i < hdlcBits.Count; i++)
        {
            if (!hdlcBits[i])
                level = !level;
            nrzi[i] = level;
        }

        return nrzi;
    }

    /// <summary>NRZI: returns true for mark (1200 Hz) and false for space (2200 Hz) per bit.</summary>
    public static bool[] ToTones(IReadOnlyList<bool> bits, bool startMark = true) => ApplyNrzi(bits, startMark);

    /// <summary>Tone sequence for a whole 1200 baud transmission.</summary>
    public static bool[] EncodeTransmission(IEnumerable<byte[]> ax25Frames, int txDelayMs, int txTailMs) =>
        ToTones(EncodeBits(ax25Frames, FlagsFor(txDelayMs), FlagsFor(txTailMs)));

    /// <summary>Scrambled NRZI bits for part of a 9600 baud G3RUH transmission.</summary>
    public static bool[] EncodeG3ruhBits(IEnumerable<byte[]> ax25Frames, int preambleFlags, int tailFlags) =>
        G3ruhScrambler.ScrambleBits(ApplyNrzi(EncodeBits(ax25Frames, preambleFlags, tailFlags)));

    /// <summary>Scrambled NRZI bits for a whole 9600 baud G3RUH transmission.</summary>
    public static bool[] EncodeG3ruhTransmission(IEnumerable<byte[]> ax25Frames, int txDelayMs, int txTailMs) =>
        EncodeG3ruhBits(ax25Frames, FlagsFor(txDelayMs, G3ruhBaud), FlagsFor(txTailMs, G3ruhBaud));

    private static void AppendByte(List<bool> bits, byte value)
    {
        for (var bit = 0; bit < 8; bit++)
            bits.Add(((value >> bit) & 1) != 0);
    }
}
