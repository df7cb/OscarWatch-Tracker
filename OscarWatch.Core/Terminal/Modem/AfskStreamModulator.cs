namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// AFSK for one keyed transmission built in pieces, so frames queued while it is on the air can be
/// appended. NRZI state, phase and bit timing carry across pieces; the ramp is applied only at the
/// start of the first piece and the end of <see cref="Finish"/>.
/// </summary>
public sealed class AfskStreamModulator
{
    private readonly int _sampleRate;
    private readonly double _samplesPerBit;
    private readonly double _amplitude;
    private readonly double _markStep;
    private readonly double _spaceStep;
    private readonly int _rampSamples;
    private bool _tone = true;
    private double _phase;
    private long _bits;
    private long _samples;

    public AfskStreamModulator(int sampleRate, double amplitude)
    {
        if (sampleRate < 8000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate;
        _samplesPerBit = sampleRate / AfskModulator.Baud;
        _amplitude = Math.Clamp(amplitude, 0, 1);
        _markStep = 2 * Math.PI * AfskModulator.MarkHz / sampleRate;
        _spaceStep = 2 * Math.PI * AfskModulator.SpaceHz / sampleRate;
        _rampSamples = (int)(sampleRate * AfskModulator.RampMs / 1000);
    }

    /// <summary>Seconds of audio produced so far.</summary>
    public double Seconds => _samples / (double)_sampleRate;

    /// <summary>Modulates HDLC bits (before NRZI) and returns the next piece of audio.</summary>
    public float[] Append(IReadOnlyList<bool> hdlcBits) => Render(hdlcBits, rampOut: false);

    /// <summary>Last piece of the transmission, ramped down at the end.</summary>
    public float[] Finish(IReadOnlyList<bool> hdlcBits) => Render(hdlcBits, rampOut: true);

    private float[] Render(IReadOnlyList<bool> hdlcBits, bool rampOut)
    {
        if (hdlcBits.Count == 0)
            return [];

        var firstBit = _bits;
        var endBit = _bits + hdlcBits.Count;
        var endSample = (long)Math.Ceiling(endBit * _samplesPerBit);
        var output = new float[Math.Max(0, endSample - _samples)];
        var startSample = _samples;

        var currentBit = -1L;
        for (var i = 0; i < output.Length; i++)
        {
            var n = startSample + i;
            var bit = Math.Min(endBit - 1, (long)(n / _samplesPerBit));
            while (currentBit < bit)
            {
                currentBit = currentBit < firstBit ? firstBit : currentBit + 1;
                if (!hdlcBits[(int)(currentBit - firstBit)])
                    _tone = !_tone;
            }

            var gain = n < _rampSamples ? 0.5 - 0.5 * Math.Cos(Math.PI * n / _rampSamples) : 1.0;
            output[i] = (float)(_amplitude * gain * Math.Sin(_phase));
            _phase += _tone ? _markStep : _spaceStep;
            if (_phase > 2 * Math.PI)
                _phase -= 2 * Math.PI;
        }

        // Bits that produced no samples of their own (fractional timing) still advance NRZI.
        while (currentBit < endBit - 1)
        {
            currentBit = currentBit < firstBit ? firstBit : currentBit + 1;
            if (!hdlcBits[(int)(currentBit - firstBit)])
                _tone = !_tone;
        }

        if (rampOut)
        {
            var ramp = Math.Min(output.Length, _rampSamples);
            for (var i = 0; i < ramp; i++)
                output[output.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / ramp));
        }

        _bits = endBit;
        _samples = endSample;
        return output;
    }
}
