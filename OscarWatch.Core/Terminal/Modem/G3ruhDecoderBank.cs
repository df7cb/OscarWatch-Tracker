namespace OscarWatch.Core.Terminal.Modem;

/// <summary>Two G3RUH decoder paths: normal and inverted baseband polarity.</summary>
public sealed class G3ruhDecoderBank
{
    private readonly G3ruhDemodulator _demod;
    private readonly Path _normal;
    private readonly Path _inverted;
    private long _sample;

    public G3ruhDecoderBank(int sampleRate)
    {
        _demod = new G3ruhDemodulator(sampleRate);
        _normal = new Path("G3RUH", invert: false, sampleRate);
        _inverted = new Path("G3RUH inv", invert: true, sampleRate);
    }

    public int PathCount => 2;

    public string PathName(int path) => path == 0 ? _normal.Name : _inverted.Name;

    public bool Dcd => _normal.Hdlc.Dcd || _inverted.Hdlc.Dcd;

    public long Sample => _sample;

    public float Filtered => _demod.Filtered;

    public void Reset()
    {
        _demod.Reset();
        _normal.Reset();
        _inverted.Reset();
    }

    public void Skip(int samples) => _sample += samples;

    public void Process(float sample, List<G3ruhCandidate> output)
    {
        _demod.Process(sample);
        _sample++;
        ProcessPath(_normal, output);
        ProcessPath(_inverted, output);
    }

    private void ProcessPath(Path path, List<G3ruhCandidate> output)
    {
        var demod = path.Invert ? -_demod.Filtered : _demod.Filtered;
        var sliced = path.Clock.Push(demod, path.Hdlc.Dcd);
        if (sliced < 0)
            return;

        var scrambled = sliced == 1;
        var nrzi = path.Scrambler.DescrambleBit(scrambled);
        var hdlcBit = path.Nrzi.PushLevel(nrzi);
        if (path.Hdlc.PushBit(hdlcBit) is { } frame)
            output.Add(new G3ruhCandidate(frame, path.Index, _sample));
    }

    private sealed class Path
    {
        public Path(string name, bool invert, int sampleRate)
        {
            Name = name;
            Invert = invert;
            Index = invert ? 1 : 0;
            Clock = new G3ruhClockRecovery(sampleRate);
        }

        public string Name { get; }
        public bool Invert { get; }
        public int Index { get; }
        public G3ruhClockRecovery Clock { get; }
        public G3ruhScrambler Scrambler { get; } = new();
        public G3ruhNrziDecoder Nrzi { get; } = new();
        public HdlcDecoder Hdlc { get; } = new();

        public void Reset()
        {
            Clock.Reset();
            Scrambler.Reset();
            Nrzi.Reset();
            Hdlc.Reset();
        }
    }
}

/// <summary>A candidate G3RUH frame (FCS still attached) from one decoder path.</summary>
public readonly record struct G3ruhCandidate(byte[] FrameWithFcs, int Path, long Sample);
