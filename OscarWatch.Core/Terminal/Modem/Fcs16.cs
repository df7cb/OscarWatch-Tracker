namespace OscarWatch.Core.Terminal.Modem;

/// <summary>AX.25 frame check sequence: CRC-16/X.25 (reflected 0x8408, initial 0xFFFF, final XOR 0xFFFF), sent low byte first.</summary>
public static class Fcs16
{
    /// <summary>CRC register value after running over a frame and its own FCS when the frame is intact.</summary>
    public const ushort GoodResidue = 0xF0B8;

    private static readonly ushort[] Table = BuildTable();

    public static ushort Compute(ReadOnlySpan<byte> data) => (ushort)(Run(0xFFFF, data) ^ 0xFFFF);

    /// <summary>True when the last two bytes are the correct FCS for the bytes before them.</summary>
    public static bool Check(ReadOnlySpan<byte> frameWithFcs) =>
        frameWithFcs.Length >= 3 && Run(0xFFFF, frameWithFcs) == GoodResidue;

    /// <summary>Returns <paramref name="frame"/> followed by its FCS, low byte first.</summary>
    public static byte[] Append(ReadOnlySpan<byte> frame)
    {
        var result = new byte[frame.Length + 2];
        frame.CopyTo(result);
        var fcs = Compute(frame);
        result[^2] = (byte)fcs;
        result[^1] = (byte)(fcs >> 8);
        return result;
    }

    private static ushort Run(ushort crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
        return crc;
    }

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)i;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0x8408) : (ushort)(crc >> 1);
            table[i] = crc;
        }

        return table;
    }
}
