namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Streaming rational resampler (upsample by L, low-pass, downsample by M) with a Kaiser-windowed
/// sinc. Used to bring 44.1 or 48 kHz soundcard audio to the 12 kHz demodulator rate without the
/// aliasing a linear interpolator lets into the 1200 to 2200 Hz band.
/// </summary>
public sealed class PolyphaseResampler
{
    private const double StopbandDb = 70;
    private const double KaiserBeta = 6.76;

    private readonly int _up;
    private readonly int _down;
    private readonly int _tapsPerPhase;
    private readonly float[] _coefficients;
    private readonly float[] _history;
    private int _historyIndex;
    private int _phase;

    /// <param name="cutoffHz">Centre of the transition band.</param>
    /// <param name="transitionHz">Full transition width; the stopband starts at cutoff plus half of this.</param>
    public PolyphaseResampler(int inputRate, int outputRate, double cutoffHz = 4500, double transitionHz = 3000)
    {
        if (inputRate <= 0 || outputRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputRate));

        var g = Gcd(inputRate, outputRate);
        _up = outputRate / g;
        _down = inputRate / g;
        InputRate = inputRate;
        OutputRate = outputRate;

        cutoffHz = Math.Min(cutoffHz, 0.5 * Math.Min(inputRate, outputRate));
        var upRate = (double)inputRate * _up;
        var length = (int)Math.Ceiling((StopbandDb - 7.95) / (2.285 * 2 * Math.PI * transitionHz / upRate)) + 1;
        _tapsPerPhase = Math.Max(4, (length + _up - 1) / _up);
        var n = _tapsPerPhase * _up;
        _coefficients = new float[n];

        var fc = cutoffHz / upRate;
        var centre = (n - 1) / 2.0;
        var i0Beta = BesselI0(KaiserBeta);
        var sum = 0.0;
        var raw = new double[n];
        for (var i = 0; i < n; i++)
        {
            var x = i - centre;
            var sinc = Math.Abs(x) < 1e-9 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
            var r = 2.0 * i / (n - 1) - 1;
            var w = BesselI0(KaiserBeta * Math.Sqrt(Math.Max(0, 1 - r * r))) / i0Beta;
            raw[i] = sinc * w;
            sum += raw[i];
        }

        // DC gain L across all taps, so each output phase (which sees one tap in L) has unity gain.
        for (var i = 0; i < n; i++)
            _coefficients[i] = (float)(raw[i] * _up / sum);

        _history = new float[_tapsPerPhase * 2];
    }

    public int InputRate { get; }
    public int OutputRate { get; }

    /// <summary>Group delay in output samples.</summary>
    public double DelayOutputSamples => (_tapsPerPhase * _up - 1) / 2.0 / _down;

    public void Reset()
    {
        Array.Clear(_history);
        _historyIndex = 0;
        _phase = 0;
    }

    /// <summary>Appends resampled output for <paramref name="input"/> to <paramref name="output"/>.</summary>
    public void Process(ReadOnlySpan<float> input, List<float> output)
    {
        if (_up == 1 && _down == 1)
        {
            foreach (var s in input)
                output.Add(s);
            return;
        }

        foreach (var sample in input)
        {
            // History is mirrored so a contiguous span always holds the latest taps (newest first after reversal).
            _historyIndex = (_historyIndex + _history.Length / 2 - 1) % (_history.Length / 2);
            _history[_historyIndex] = sample;
            _history[_historyIndex + _tapsPerPhase] = sample;

            while (_phase < _up)
            {
                var acc = 0f;
                var h = _phase;
                var x = _historyIndex;
                for (var j = 0; j < _tapsPerPhase; j++)
                {
                    acc += _coefficients[h] * _history[x + j];
                    h += _up;
                }

                output.Add(acc);
                _phase += _down;
            }

            _phase -= _up;
        }
    }

    public float[] ProcessAll(ReadOnlySpan<float> input)
    {
        var output = new List<float>((int)((long)input.Length * _up / _down) + 8);
        Process(input, output);
        return output.ToArray();
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }

    private static double BesselI0(double x)
    {
        var sum = 1.0;
        var term = 1.0;
        var half = x / 2;
        for (var k = 1; k < 50; k++)
        {
            term *= half / k * (half / k);
            sum += term;
            if (term < sum * 1e-12)
                break;
        }

        return sum;
    }
}
