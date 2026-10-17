namespace OscarWatch.Core.Terminal.Modem;

/// <summary>Stateful NRZI and G3RUH scrambler for one keyed 9600 burst (initial, chained, and TXTAIL).</summary>
public sealed class G3ruhStreamEncoder
{
    private bool _nrziLevel = true;
    private readonly G3ruhScrambler _scrambler = new();

    public void Reset()
    {
        _nrziLevel = true;
        _scrambler.Reset();
    }

    public List<bool> AppendFrames(IEnumerable<byte[]> ax25Frames, int preambleFlags, int tailFlags) =>
        AppendHdlcBits(HdlcEncoder.EncodeBits(ax25Frames, preambleFlags, tailFlags));

    /// <summary>More frames in the same burst after the first batch (no extra leading flags).</summary>
    public List<bool> AppendContinuationFrames(IEnumerable<byte[]> ax25Frames) =>
        AppendHdlcBits(HdlcEncoder.EncodeBits(ax25Frames, preambleFlags: 0, tailFlags: 0, leadingFlags: false));

    public List<bool> AppendHdlcBits(IReadOnlyList<bool> hdlcBits)
    {
        var output = new List<bool>(hdlcBits.Count);
        foreach (var bit in hdlcBits)
        {
            if (!bit)
                _nrziLevel = !_nrziLevel;
            output.Add(_scrambler.ScrambleNrziBit(_nrziLevel));
        }

        return output;
    }

    public List<bool> AppendFlags(int count) => AppendHdlcBits(HdlcEncoder.FlagBits(count));
}
