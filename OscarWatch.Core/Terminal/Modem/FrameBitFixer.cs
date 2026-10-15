namespace OscarWatch.Core.Terminal.Modem;

/// <summary>How hard the receiver tries to repair a frame that failed its FCS.</summary>
public enum AfskBitFixMode
{
    Off,
    SingleBit,
    SingleAndAdjacentPair
}

/// <summary>
/// Repairs frames with one flipped bit (or two adjacent bits) by trial. A repaired frame is only
/// accepted when it is a plausible UI frame, because with hundreds of trials a random block can pass
/// the 16-bit FCS by chance.
/// </summary>
public static class FrameBitFixer
{
    /// <summary>Returns the repaired frame without its FCS, or null.</summary>
    public static byte[]? TryFix(ReadOnlySpan<byte> frameWithFcs, AfskBitFixMode mode)
    {
        if (mode == AfskBitFixMode.Off || frameWithFcs.Length < HdlcDecoder.MinFrameBytes)
            return null;

        var work = frameWithFcs.ToArray();
        var bits = work.Length * 8;
        for (var i = 0; i < bits; i++)
        {
            Flip(work, i);
            if (Accept(work) is { } fixedSingle)
                return fixedSingle;

            if (mode == AfskBitFixMode.SingleAndAdjacentPair && i + 1 < bits)
            {
                Flip(work, i + 1);
                if (Accept(work) is { } fixedPair)
                    return fixedPair;
                Flip(work, i + 1);
            }

            Flip(work, i);
        }

        return null;
    }

    /// <summary>
    /// A UI frame whose every address is 1 to 6 letters or digits (space padded) and whose source
    /// looks like a real callsign.
    /// </summary>
    public static bool IsPlausibleUi(ReadOnlySpan<byte> ax25)
    {
        if (!Ax25Frame.TryParse(ax25, out var frame) || frame is null || !frame.IsUi)
            return false;

        var addressBytes = (2 + frame.Digipeaters.Count) * Ax25Address.EncodedLength;
        for (var a = 0; a < addressBytes; a += Ax25Address.EncodedLength)
        {
            var seenSpace = false;
            for (var i = 0; i < 6; i++)
            {
                var c = (char)(ax25[a + i] >> 1);
                if ((ax25[a + i] & 1) != 0)
                    return false;
                if (c == ' ')
                {
                    seenSpace = true;
                    continue;
                }

                if (seenSpace || c is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
                    return false;
            }
        }

        return TerminalText.LooksLikeCallsign(frame.Source.Callsign);
    }

    private static byte[]? Accept(byte[] work)
    {
        if (!Fcs16.Check(work))
            return null;
        var frame = work.AsSpan(0, work.Length - 2);
        return IsPlausibleUi(frame) ? frame.ToArray() : null;
    }

    private static void Flip(byte[] data, int bit) => data[bit >> 3] ^= (byte)(1 << (bit & 7));
}
