namespace OscarWatch.Core.Terminal.Modem;

/// <summary>G3RUH data scrambler: polynomial x^17 + x^12 + 1, self-synchronising on TX and RX.</summary>
public sealed class G3ruhScrambler
{
    private const uint Mask = 0x1FFFF;
    private uint _lfsr = Mask;

    public void Reset() => _lfsr = Mask;

    /// <summary>Descrambles one bit from the slicer. The LFSR shifts in the scrambled input bit.</summary>
    public bool DescrambleBit(bool scrambled)
    {
        var feedback = ((_lfsr >> 16) ^ (_lfsr >> 11)) & 1;
        var clear = scrambled ^ feedback != 0;
        _lfsr = ((_lfsr << 1) | (scrambled ? 1u : 0u)) & Mask;
        return clear;
    }

    /// <summary>Scrambles one NRZI level for transmit. The LFSR shifts in the scrambled output bit.</summary>
    public bool ScrambleNrziBit(bool nrziLevel)
    {
        var feedback = ((_lfsr >> 16) ^ (_lfsr >> 11)) & 1;
        var scrambled = nrziLevel ^ feedback != 0;
        _lfsr = ((_lfsr << 1) | (scrambled ? 1u : 0u)) & Mask;
        return scrambled;
    }

    /// <summary>Scrambles NRZI bits for transmit. The LFSR shifts in the scrambled output bit.</summary>
    public static bool[] ScrambleBits(IReadOnlyList<bool> nrziBits)
    {
        var scrambler = new G3ruhScrambler();
        var output = new bool[nrziBits.Count];
        for (var i = 0; i < nrziBits.Count; i++)
            output[i] = scrambler.ScrambleNrziBit(nrziBits[i]);
        return output;
    }
}
