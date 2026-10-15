namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Front end at 12 kHz: band-pass prefilter (removes the DC shift that residual Doppler leaves on FM
/// audio, hum and hiss), quadrature tone correlators at 1200 and 2200 Hz for each window length,
/// a per-tone AGC, and a delay-line discriminator for diversity. Positive outputs mean mark.
/// </summary>
public sealed class AfskDemodulator
{
    public const int SampleRate = 12000;
    public const int LoTableLength = 60;
    private const int PrefilterTaps = 41;
    private const int DiscriminatorDelay = 2;
    private const int DiscriminatorWindow = 10;
    private const float AgcFastAttack = 0.70f;
    private const float AgcSlowDecay = 0.00033f;

    // cos(2πf·2/12000): 1200 Hz gives 0.309, 2200 Hz gives -0.669; midway is the decision point.
    private static readonly float DiscriminatorThreshold =
        (float)((Math.Cos(2 * Math.PI * 1200 * DiscriminatorDelay / SampleRate)
                 + Math.Cos(2 * Math.PI * 2200 * DiscriminatorDelay / SampleRate)) / 2);

    private static readonly float[] Prefilter = DesignBandPass(PrefilterTaps, 700, 2700);
    private static readonly float[] CosMark = LoTable(1200, Math.Cos);
    private static readonly float[] SinMark = LoTable(1200, Math.Sin);
    private static readonly float[] CosSpace = LoTable(2200, Math.Cos);
    private static readonly float[] SinSpace = LoTable(2200, Math.Sin);

    private readonly float[] _pre = new float[PrefilterTaps * 2];
    private int _preIndex;
    private int _lo;
    private readonly Correlator[] _correlators;
    private readonly float[] _discDelay = new float[DiscriminatorDelay];
    private int _discDelayIndex;
    private readonly MovingSum _discProduct = new(DiscriminatorWindow);
    private readonly MovingSum _discPower = new(DiscriminatorWindow);

    public AfskDemodulator(IReadOnlyList<int> correlatorWindows)
    {
        _correlators = correlatorWindows.Select(w => new Correlator(w)).ToArray();
    }

    public int CorrelatorCount => _correlators.Length;

    /// <summary>Band-passed input, for level and DCD diagnostics.</summary>
    public float Filtered { get; private set; }

    public float Discriminator { get; private set; }

    public float MarkAmplitude(int correlator) => _correlators[correlator].Mark;
    public float SpaceAmplitude(int correlator) => _correlators[correlator].Space;

    /// <summary>Mark minus space after each tone has its own AGC, so twist is removed before slicing.</summary>
    public float AgcOutput(int correlator) => _correlators[correlator].AgcOutput;

    public void Reset()
    {
        Array.Clear(_pre);
        _preIndex = 0;
        foreach (var c in _correlators)
            c.Reset();
        Array.Clear(_discDelay);
        _discDelayIndex = 0;
        _discProduct.Reset();
        _discPower.Reset();
        Filtered = 0;
        Discriminator = 0;
    }

    public void Process(float sample)
    {
        _preIndex = (_preIndex + PrefilterTaps - 1) % PrefilterTaps;
        _pre[_preIndex] = sample;
        _pre[_preIndex + PrefilterTaps] = sample;
        var x = 0f;
        for (var i = 0; i < PrefilterTaps; i++)
            x += Prefilter[i] * _pre[_preIndex + i];
        Filtered = x;

        var lo = _lo;
        _lo = (_lo + 1) % LoTableLength;
        var im = x * CosMark[lo];
        var qm = x * SinMark[lo];
        var isp = x * CosSpace[lo];
        var qs = x * SinSpace[lo];
        foreach (var c in _correlators)
            c.Push(im, qm, isp, qs);

        var delayed = _discDelay[_discDelayIndex];
        _discDelay[_discDelayIndex] = x;
        _discDelayIndex = (_discDelayIndex + 1) % DiscriminatorDelay;
        var product = _discProduct.Push(x * delayed);
        var power = _discPower.Push(x * x);
        Discriminator = power > 1e-9f ? product / power - DiscriminatorThreshold : 0f;
    }

    private static float[] LoTable(double hz, Func<double, double> f)
    {
        // 1200 Hz repeats every 10 samples and 2200 Hz every 60 at 12 kHz, so one 60-sample table is exact.
        var table = new float[LoTableLength];
        for (var n = 0; n < LoTableLength; n++)
            table[n] = (float)f(2 * Math.PI * hz * n / SampleRate);
        return table;
    }

    private static float[] DesignBandPass(int taps, double lowHz, double highHz)
    {
        var h = new float[taps];
        var centre = (taps - 1) / 2.0;
        var fl = lowHz / SampleRate;
        var fh = highHz / SampleRate;
        var peak = 0.0;
        var raw = new double[taps];
        for (var i = 0; i < taps; i++)
        {
            var x = i - centre;
            double LowPass(double fc) => Math.Abs(x) < 1e-9 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
            var w = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (taps - 1));
            raw[i] = (LowPass(fh) - LowPass(fl)) * w;
        }

        // Normalise to unity gain at the middle of the band.
        var re = 0.0;
        var im = 0.0;
        var omega = 2 * Math.PI * 1700 / SampleRate;
        for (var i = 0; i < taps; i++)
        {
            re += raw[i] * Math.Cos(omega * i);
            im += raw[i] * Math.Sin(omega * i);
        }

        peak = Math.Sqrt(re * re + im * im);
        for (var i = 0; i < taps; i++)
            h[i] = (float)(raw[i] / peak);
        return h;
    }

    private sealed class Correlator
    {
        private readonly MovingSum _im;
        private readonly MovingSum _qm;
        private readonly MovingSum _is;
        private readonly MovingSum _qs;
        private readonly Agc _markAgc = new();
        private readonly Agc _spaceAgc = new();

        public Correlator(int window)
        {
            _im = new MovingSum(window);
            _qm = new MovingSum(window);
            _is = new MovingSum(window);
            _qs = new MovingSum(window);
        }

        public float Mark { get; private set; }
        public float Space { get; private set; }
        public float AgcOutput { get; private set; }

        public void Push(float im, float qm, float isp, float qs)
        {
            var a = _im.Push(im);
            var b = _qm.Push(qm);
            var c = _is.Push(isp);
            var d = _qs.Push(qs);
            Mark = MathF.Sqrt(a * a + b * b);
            Space = MathF.Sqrt(c * c + d * d);
            AgcOutput = _markAgc.Push(Mark) - _spaceAgc.Push(Space);
        }

        public void Reset()
        {
            _im.Reset();
            _qm.Reset();
            _is.Reset();
            _qs.Reset();
            _markAgc.Reset();
            _spaceAgc.Reset();
            Mark = Space = AgcOutput = 0;
        }
    }

    /// <summary>Peak and valley trackers with fast attack and slow decay; output is centred and scaled to about ±0.5.</summary>
    private sealed class Agc
    {
        private float _peak;
        private float _valley;

        public float Push(float x)
        {
            _peak = x >= _peak ? x * AgcFastAttack + _peak * (1 - AgcFastAttack) : x * AgcSlowDecay + _peak * (1 - AgcSlowDecay);
            _valley = x <= _valley ? x * AgcFastAttack + _valley * (1 - AgcFastAttack) : x * AgcSlowDecay + _valley * (1 - AgcSlowDecay);
            var span = _peak - _valley;
            return span > 1e-9f ? (x - 0.5f * (_peak + _valley)) / span : 0f;
        }

        public void Reset() => _peak = _valley = 0;
    }

    private sealed class MovingSum
    {
        private readonly float[] _buffer;
        private int _index;
        private double _sum;

        public MovingSum(int length) => _buffer = new float[Math.Max(1, length)];

        public float Push(float x)
        {
            _sum += x - _buffer[_index];
            _buffer[_index] = x;
            _index = (_index + 1) % _buffer.Length;
            return (float)_sum;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _index = 0;
            _sum = 0;
        }
    }
}
