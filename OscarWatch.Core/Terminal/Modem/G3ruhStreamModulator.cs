namespace OscarWatch.Core.Terminal.Modem;

/// <summary>Streaming G3RUH modulator for chained frames during one PTT burst.</summary>
public sealed class G3ruhStreamModulator
{
    private readonly int _sampleRate;
    private readonly double _samplesPerSymbol;
    private readonly double _amplitude;
    private readonly float[] _filter;
    private readonly int _filterHalf;
    private readonly int _rampSamples;
    private long _symbols;
    private long _samples;

    public G3ruhStreamModulator(int sampleRate, double amplitude)
    {
        if (sampleRate < 38400)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate;
        _samplesPerSymbol = sampleRate / (double)G3ruhModulator.Baud;
        _amplitude = Math.Clamp(amplitude, 0, 1);
        _filter = G3ruhModulator.DesignRootRaisedCosineInternal(sampleRate);
        _filterHalf = _filter.Length / 2;
        _rampSamples = (int)(sampleRate * AfskModulator.RampMs / 1000);
    }

    public float[] Append(IReadOnlyList<bool> scrambledBits) => Render(scrambledBits, rampOut: false);

    public float[] Finish(IReadOnlyList<bool> scrambledBits) => Render(scrambledBits, rampOut: true);

    private float[] Render(IReadOnlyList<bool> bits, bool rampOut)
    {
        if (bits.Count == 0)
            return [];

        var firstSymbol = _symbols;
        var endSymbol = _symbols + bits.Count;
        var endSample = (long)Math.Ceiling((endSymbol - 1) * _samplesPerSymbol) + _filterHalf * 2;
        var startSample = _samples;
        var length = Math.Max(0, endSample - startSample);
        var output = new float[length];

        for (var s = 0; s < bits.Count; s++)
        {
            var level = bits[s] ? 1.0 : -1.0;
            var centre = (long)Math.Round((firstSymbol + s) * _samplesPerSymbol) + _filterHalf;
            for (var t = 0; t < _filter.Length; t++)
            {
                var index = centre - _filterHalf + t - startSample;
                if (index >= 0 && index < length)
                    output[index] += (float)(level * _amplitude * _filter[t]);
            }
        }

        if (startSample < _rampSamples)
        {
            for (var i = 0; i < output.Length; i++)
            {
                var n = startSample + i;
                if (n >= _rampSamples)
                    break;
                output[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * n / _rampSamples));
            }
        }

        if (rampOut)
        {
            var ramp = Math.Min(output.Length, _rampSamples);
            for (var i = 0; i < ramp; i++)
                output[output.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / ramp));
        }

        _symbols = endSymbol;
        _samples = endSample;
        return output;
    }
}
