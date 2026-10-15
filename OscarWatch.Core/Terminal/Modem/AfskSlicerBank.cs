namespace OscarWatch.Core.Terminal.Modem;

/// <summary>A candidate frame (FCS still attached) from one decoder path.</summary>
public readonly record struct AfskCandidate(byte[] FrameWithFcs, int Path, long Sample);

/// <summary>
/// Parallel decoder paths over one <see cref="AfskDemodulator"/>. Each path has its own decision,
/// clock recovery and HDLC state:
/// <list type="bullet">
/// <item>A: 1.2-bit correlators (each window nulls the other tone) with mark/space balance from −6 to +10 dB, so
/// flat data-port audio and de-emphasised speaker audio both decode without a twist setting;</item>
/// <item>B: one-bit correlators with a smaller set of balances;</item>
/// <item>AGC: per-tone AGC on the A correlators;</item>
/// <item>D: delay-line discriminator.</item>
/// </list>
/// </summary>
public sealed class AfskSlicerBank
{
    private const int CorrelatorA = 0;
    private const int CorrelatorB = 1;

    private static readonly int[] BalancesADb = [-6, -4, -2, 0, 2, 4, 6, 8, 10];
    private static readonly int[] BalancesBDb = [-3, 0, 3, 6];

    private readonly AfskDemodulator _demod = new([12, 10]);
    private readonly Path[] _paths;
    private long _sample;

    public AfskSlicerBank()
    {
        var paths = new List<Path>();
        foreach (var db in BalancesADb)
            paths.Add(new Path($"A{db:+0;-0;0}", PathKind.Balance, CorrelatorA, (float)Math.Pow(10, db / 20.0)));
        foreach (var db in BalancesBDb)
            paths.Add(new Path($"B{db:+0;-0;0}", PathKind.Balance, CorrelatorB, (float)Math.Pow(10, db / 20.0)));
        paths.Add(new Path("AGC", PathKind.Agc, CorrelatorA, 1));
        paths.Add(new Path("D", PathKind.Discriminator, 0, 1));
        _paths = paths.ToArray();
    }

    public int PathCount => _paths.Length;

    public string PathName(int path) => _paths[path].Name;

    /// <summary>True while any path holds DCD.</summary>
    public bool Dcd
    {
        get
        {
            foreach (var p in _paths)
            {
                if (p.Hdlc.Dcd)
                    return true;
            }

            return false;
        }
    }

    /// <summary>Samples processed at 12 kHz since construction (not cleared by <see cref="Reset"/>).</summary>
    public long Sample => _sample;

    public float Filtered => _demod.Filtered;

    public void Reset()
    {
        _demod.Reset();
        foreach (var p in _paths)
        {
            p.Clock.Reset();
            p.Hdlc.Reset();
        }
    }

    /// <summary>Counts a sample without decoding it (receive blanked while transmitting).</summary>
    public void Skip(int samples) => _sample += samples;

    public void Process(float sample, List<AfskCandidate> output)
    {
        _demod.Process(sample);
        _sample++;
        for (var i = 0; i < _paths.Length; i++)
        {
            var p = _paths[i];
            var demod = p.Kind switch
            {
                PathKind.Balance => _demod.MarkAmplitude(p.Correlator) - _demod.SpaceAmplitude(p.Correlator) * p.SpaceGain,
                PathKind.Agc => _demod.AgcOutput(p.Correlator),
                _ => _demod.Discriminator
            };

            var bit = p.Clock.Push(demod, p.Hdlc.Dcd);
            if (bit < 0)
                continue;
            if (p.Hdlc.PushBit(bit == 1) is { } frame)
                output.Add(new AfskCandidate(frame, i, _sample));
        }
    }

    private enum PathKind
    {
        Balance,
        Agc,
        Discriminator
    }

    private sealed class Path
    {
        public Path(string name, PathKind kind, int correlator, float spaceGain)
        {
            Name = name;
            Kind = kind;
            Correlator = correlator;
            SpaceGain = spaceGain;
        }

        public string Name { get; }
        public PathKind Kind { get; }
        public int Correlator { get; }
        public float SpaceGain { get; }
        public AfskClockRecovery Clock { get; } = new(AfskDemodulator.SampleRate);
        public HdlcDecoder Hdlc { get; } = new();
    }
}
