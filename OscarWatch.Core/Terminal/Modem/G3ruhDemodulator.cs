namespace OscarWatch.Core.Terminal.Modem;

/// <summary>9600 baud G3RUH baseband front end: DC block, low-pass, AGC.</summary>
public sealed class G3ruhDemodulator
{
    public const int MinSampleRate = 38400;
    public const int PreferredSampleRate = 48000;

    /// <summary>
    /// Normal high-pass corner (Hz). Chosen at 48 kHz so the −3 dB point sits well below the scrambled
    /// 9600 baud lobe (measured −20 dB near 800 Hz on synthetic keys) while tracking slow baseline wander.
    /// </summary>
    public const float HighPassCornerHz = 12f;

    /// <summary>Corner (Hz) while fast DC recovery is active after a CAT step.</summary>
    public const float StepRecoveryCornerHz = 380f;

    /// <summary>Slow DC tracker corner (Hz) used to recognise persistent baseline shifts.</summary>
    public const float SlowBaselineCornerHz = 2.5f;

    /// <summary>Per-sample jump that may start step confirmation (not applied until baseline shift persists).</summary>
    public const float StepDetectJumpMin = 0.14f;

    public const float StepDetectJumpMax = 0.55f;

    /// <summary>Slow baseline must move at least this much before a jump counts as a CAT step.</summary>
    public const float StepPersistShiftMin = 0.10f;

    /// <summary>Delay before confirming a step (~3.3 ms at 48 kHz).</summary>
    public const int StepConfirmSamples = 160;

    /// <summary>Fraction of a confirmed step applied to the fast DC estimate.</summary>
    public const float StepSnapGain = 0.92f;

    /// <summary>How long fast tracking stays engaged after a step (10 ms at 48 kHz).</summary>
    public const int StepRecoveryDurationSamples = 480;

    private const int LowPassTaps = 31;
    private const float AgcFastAttack = 0.65f;
    private const float AgcSlowDecay = 0.00025f;

    private readonly float[] _lowPass;
    private readonly float[] _delay = new float[LowPassTaps * 2];
    private readonly float _kSlow;
    private readonly float _kFast;
    private readonly float _kBaseline;
    private int _delayIndex;
    private float _dcFast;
    private float _dcSlow;
    private float _lastSample;
    private float _slowAtJumpCandidate;
    private float _pendingJump;
    private float _confirmLevelSum;
    private int _confirmLevelCount;
    private float _agcGain = 1f;
    private int _stepRecoverySamples;
    private int _stepConfirmRemaining;

    public G3ruhDemodulator(int sampleRate)
    {
        SampleRate = sampleRate;
        _kSlow = CoefficientForCornerHz(HighPassCornerHz, sampleRate);
        _kFast = CoefficientForCornerHz(StepRecoveryCornerHz, sampleRate);
        _kBaseline = CoefficientForCornerHz(SlowBaselineCornerHz, sampleRate);
        _lowPass = DesignLowPass(sampleRate, cutoffHz: 7200);
    }

    public int SampleRate { get; }

    public float Filtered { get; private set; }

    /// <summary>True while the demodulator is in fast DC recovery after a baseline step.</summary>
    public bool StepRecoveryActive => _stepRecoverySamples > 0;

    public void Reset()
    {
        Array.Clear(_delay);
        _delayIndex = 0;
        _dcFast = 0;
        _dcSlow = 0;
        _lastSample = 0;
        _pendingJump = 0;
        _slowAtJumpCandidate = 0;
        _agcGain = 1f;
        _stepRecoverySamples = 0;
        _stepConfirmRemaining = 0;
        _confirmLevelSum = 0;
        _confirmLevelCount = 0;
        Filtered = 0;
    }

    public void Process(float sample)
    {
        _dcSlow += _kBaseline * (sample - _dcSlow);
        var jump = sample - _lastSample;
        _lastSample = sample;
        if (_stepConfirmRemaining > 0)
        {
            _confirmLevelSum += sample;
            _confirmLevelCount++;
            if (--_stepConfirmRemaining == 0)
            {
                var postMean = _confirmLevelSum / Math.Max(1, _confirmLevelCount);
                var shift = postMean - _slowAtJumpCandidate;
                var confirmed = Math.Abs(shift) >= StepPersistShiftMin && Math.Sign(shift) == Math.Sign(_pendingJump);
                if (confirmed && Math.Abs(_pendingJump) < 0.30f)
                {
                    confirmed = Math.Abs(shift - _pendingJump) <= Math.Max(0.08f, Math.Abs(_pendingJump) * 0.35f);
                }
                else if (Math.Abs(_pendingJump) >= 0.30f)
                {
                    confirmed = Math.Abs(shift) >= 0.04f;
                }

                if (confirmed)
                {
                    _stepRecoverySamples = StepRecoveryDurationSamples;
                    _dcFast += StepSnapGain * shift;
                }
            }
        }
        else if (Math.Abs(jump) is var absJump && absJump <= StepDetectJumpMax
                 && absJump >= StepDetectJumpMin + 0.10f * Math.Clamp(_agcGain, 0.35f, 1f))
        {
            if (absJump >= 0.34f)
            {
                _stepRecoverySamples = StepRecoveryDurationSamples;
                _dcFast += StepSnapGain * jump;
            }
            else
            {
                _pendingJump = jump;
                _slowAtJumpCandidate = _dcSlow;
                _confirmLevelSum = sample;
                _confirmLevelCount = 1;
                _stepConfirmRemaining = StepConfirmSamples;
            }
        }

        if (_stepRecoverySamples > 0)
            _stepRecoverySamples--;

        var error = sample - _dcFast;
        var k = _stepRecoverySamples > 0 ? _kFast : _kSlow;
        _dcFast += k * error;
        var highPass = sample - _dcFast;

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

    /// <summary>One-pole high-pass coefficient: state += k·(x−state), k ≈ 2π·fc/fs.</summary>
    internal static float CoefficientForCornerHz(double cornerHz, int sampleRate) =>
        (float)(2 * Math.PI * cornerHz / sampleRate);

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
