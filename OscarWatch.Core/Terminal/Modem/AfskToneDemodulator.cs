namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Quadrature tone correlators and delay-line discriminator on band-passed audio at
/// <paramref name="MarkHz"/> and <paramref name="SpaceHz"/>.
/// </summary>
public sealed class AfskToneDemodulator
{
    private const int DiscriminatorDelay = 2;
    private const int DiscriminatorWindow = 10;

    private readonly float[] _cosMark;
    private readonly float[] _sinMark;
    private readonly float[] _cosSpace;
    private readonly float[] _sinSpace;
    private readonly float _discriminatorThreshold;
    private readonly AfskDemodulator.Correlator[] _correlators;
    private int _lo;
    private readonly float[] _discDelay = new float[DiscriminatorDelay];
    private int _discDelayIndex;
    private readonly AfskDemodulator.MovingSum _discProduct = new(DiscriminatorWindow);
    private readonly AfskDemodulator.MovingSum _discPower = new(DiscriminatorWindow);

    public AfskToneDemodulator(double markHz, double spaceHz, IReadOnlyList<int> correlatorWindows)
    {
        MarkHz = markHz;
        SpaceHz = spaceHz;
        _cosMark = LoTable(markHz, Math.Cos);
        _sinMark = LoTable(markHz, Math.Sin);
        _cosSpace = LoTable(spaceHz, Math.Cos);
        _sinSpace = LoTable(spaceHz, Math.Sin);
        _discriminatorThreshold = (float)((Math.Cos(2 * Math.PI * markHz * DiscriminatorDelay / AfskDemodulator.SampleRate)
                                           + Math.Cos(2 * Math.PI * spaceHz * DiscriminatorDelay / AfskDemodulator.SampleRate)) / 2);
        _correlators = correlatorWindows.Select(w => new AfskDemodulator.Correlator(w)).ToArray();
    }

    public double MarkHz { get; }
    public double SpaceHz { get; }

    public int CorrelatorCount => _correlators.Length;

    public float Discriminator { get; private set; }

    public float MarkAmplitude(int correlator) => _correlators[correlator].Mark;
    public float SpaceAmplitude(int correlator) => _correlators[correlator].Space;
    public float AgcOutput(int correlator) => _correlators[correlator].AgcOutput;

    public void Reset()
    {
        foreach (var c in _correlators)
            c.Reset();
        Array.Clear(_discDelay);
        _discDelayIndex = 0;
        _discProduct.Reset();
        _discPower.Reset();
        Discriminator = 0;
        _lo = 0;
    }

    public void ProcessFiltered(float x)
    {
        var lo = _lo;
        _lo = (_lo + 1) % AfskDemodulator.LoTableLength;
        var im = x * _cosMark[lo];
        var qm = x * _sinMark[lo];
        var isp = x * _cosSpace[lo];
        var qs = x * _sinSpace[lo];
        foreach (var c in _correlators)
            c.Push(im, qm, isp, qs);

        var delayed = _discDelay[_discDelayIndex];
        _discDelay[_discDelayIndex] = x;
        _discDelayIndex = (_discDelayIndex + 1) % DiscriminatorDelay;
        var product = _discProduct.Push(x * delayed);
        var power = _discPower.Push(x * x);
        Discriminator = power > 1e-9f ? product / power - _discriminatorThreshold : 0f;
    }

    private static float[] LoTable(double hz, Func<double, double> f)
    {
        var table = new float[AfskDemodulator.LoTableLength];
        for (var n = 0; n < AfskDemodulator.LoTableLength; n++)
            table[n] = (float)f(2 * Math.PI * hz * n / AfskDemodulator.SampleRate);
        return table;
    }
}
