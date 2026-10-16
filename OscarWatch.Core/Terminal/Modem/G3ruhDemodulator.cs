namespace OscarWatch.Core.Terminal.Modem;

/// <summary>9600 baud G3RUH baseband front end: DC block, low-pass, AGC.</summary>
public sealed class G3ruhDemodulator
{
    public const int MinSampleRate = 38400;
    public const int PreferredSampleRate = 48000;

    private const int LowPassTaps = 31;
    private const float HpfAlpha = 0.9995f;
    private const float AgcFastAttack = 0.65f;
    private const float AgcSlowDecay = 0.00025f;

    private readonly float[] _lowPass;
    private readonly float[] _delay = new float[LowPassTaps * 2];
    private int _delayIndex;
    private float _hpfState;
    private float _agcGain = 1f;

    public G3ruhDemodulator(int sampleRate)
    {
        SampleRate = sampleRate;
        _lowPass = DesignLowPass(sampleRate, cutoffHz: 7200);
    }

    public int SampleRate { get; }

    public float Filtered { get; private set; }

    public void Reset()
    {
        Array.Clear(_delay);
        _delayIndex = 0;
        _hpfState = 0;
        _agcGain = 1f;
        Filtered = 0;
    }

    public void Process(float sample)
    {
        var highPass = sample - _hpfState;
        _hpfState += (1 - HpfAlpha) * highPass;

        _delayIndex = (_delayIndex + LowPassTaps - 1) % LowPassTaps;
        _delay[_delayIndex] = highPass;
        _delay[_delayIndex + LowPassTaps] = highPass;
        var filtered = 0f;
        for (var i = 0; i < LowPassTaps; i++)
            filtered += _lowPass[i] * _delay[_delayIndex + i];

        var magnitude = Math.Abs(filtered);
        var target = magnitude > _agcGain ? AgcFastAttack : AgcSlowDecay;
        _agcGain += target * (Math.Max(magnitude, 1e-6f) - _agcGain);
        Filtered = filtered / Math.Max(_agcGain, 1e-4f);
    }

    private static float[] DesignLowPass(int sampleRate, double cutoffHz)
    {
        var taps = new float[LowPassTaps];
        var fc = cutoffHz / sampleRate;
        var sum = 0.0;
        var mid = LowPassTaps / 2;
        for (var n = 0; n < LowPassTaps; n++)
        {
            var x = n - mid;
            var value = x == 0
                ? 2 * fc
                : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
            value *= 0.54 - 0.46 * Math.Cos(2 * Math.PI * n / (LowPassTaps - 1));
            taps[n] = (float)value;
            sum += value;
        }

        for (var i = 0; i < taps.Length; i++)
            taps[i] /= (float)sum;
        return taps;
    }
}
