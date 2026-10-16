namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Quadrature tone correlators and delay-line discriminator on band-passed audio at
/// <paramref name="MarkHz"/> and <paramref name="SpaceHz"/>. Uses phase-continuous NCOs so
/// arbitrary tone offsets (not only 1200/2200 Hz) stay coherent across sample boundaries.
/// </summary>
public sealed class AfskToneDemodulator
{
    private const int DiscriminatorDelay = 2;
    private const int DiscriminatorWindow = 10;

    private readonly double _markStep;
    private readonly double _spaceStep;
    private readonly float _discriminatorThreshold;
    private readonly AfskDemodulator.Correlator[] _correlators;
    private double _markPhase;
    private double _spacePhase;
    private readonly float[] _discDelay = new float[DiscriminatorDelay];
    private int _discDelayIndex;
    private readonly AfskDemodulator.MovingSum _discProduct = new(DiscriminatorWindow);
    private readonly AfskDemodulator.MovingSum _discPower = new(DiscriminatorWindow);

    public AfskToneDemodulator(double markHz, double spaceHz, IReadOnlyList<int> correlatorWindows)
    {
        MarkHz = markHz;
        SpaceHz = spaceHz;
        _markStep = 2 * Math.PI * markHz / AfskDemodulator.SampleRate;
        _spaceStep = 2 * Math.PI * spaceHz / AfskDemodulator.SampleRate;
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
        _markPhase = 0;
        _spacePhase = 0;
    }

    public void ProcessFiltered(float x)
    {
        var im = (float)(x * Math.Cos(_markPhase));
        var qm = (float)(x * Math.Sin(_markPhase));
        var isp = (float)(x * Math.Cos(_spacePhase));
        var qs = (float)(x * Math.Sin(_spacePhase));
        _markPhase += _markStep;
        _spacePhase += _spaceStep;
        if (_markPhase >= Math.PI * 2)
            _markPhase -= Math.PI * 2;
        if (_spacePhase >= Math.PI * 2)
            _spacePhase -= Math.PI * 2;

        foreach (var c in _correlators)
            c.Push(im, qm, isp, qs);

        var delayed = _discDelay[_discDelayIndex];
        _discDelay[_discDelayIndex] = x;
        _discDelayIndex = (_discDelayIndex + 1) % DiscriminatorDelay;
        var product = _discProduct.Push(x * delayed);
        var power = _discPower.Push(x * x);
        Discriminator = power > 1e-9f ? product / power - _discriminatorThreshold : 0f;
    }
}
