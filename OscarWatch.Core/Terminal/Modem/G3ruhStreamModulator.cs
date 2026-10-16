namespace OscarWatch.Core.Terminal.Modem;

/// <summary>Streaming G3RUH modulator for chained frames during one PTT burst.</summary>
public sealed class G3ruhStreamModulator
{
    private readonly double _samplesPerSymbol;
    private readonly double _amplitude;
    private readonly float[] _filter;
    private readonly int _filterHalf;
    private readonly int _rampSamples;
    private readonly List<bool> _symbols = new(4096);
    private readonly List<float> _buffer = new(8192);
    private int _symbolsRendered;
    private long _emitSample;
    private bool _rampInApplied;

    public G3ruhStreamModulator(int sampleRate, double amplitude)
    {
        if (sampleRate < 38400)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _samplesPerSymbol = sampleRate / (double)G3ruhModulator.Baud;
        _amplitude = Math.Clamp(amplitude, 0, 1);
        _filter = G3ruhModulator.DesignRootRaisedCosineInternal(sampleRate);
        _filterHalf = _filter.Length / 2;
        _rampSamples = (int)(sampleRate * AfskModulator.RampMs / 1000);
    }

    public float[] Append(IReadOnlyList<bool> scrambledBits)
    {
        if (scrambledBits.Count > 0)
            _symbols.AddRange(scrambledBits);
        return Emit(finish: false);
    }

    public float[] Finish(IReadOnlyList<bool> scrambledBits)
    {
        if (scrambledBits.Count > 0)
            _symbols.AddRange(scrambledBits);
        return _symbols.Count == 0 ? [] : Emit(finish: true);
    }

    private float[] Emit(bool finish)
    {
        var endSymbol = _symbols.Count;
        var globalEnd = (int)Math.Ceiling((endSymbol - 1) * _samplesPerSymbol + _filterHalf * 2);
        var emitEnd = finish ? globalEnd : Math.Max((int)_emitSample, globalEnd - _filter.Length);
        var start = (int)_emitSample;
        var length = Math.Max(0, emitEnd - start);
        if (length == 0)
            return [];

        while (_buffer.Count < globalEnd)
            _buffer.Add(0);

        for (var s = _symbolsRendered; s < endSymbol; s++)
            AddSymbol(s);
        _symbolsRendered = endSymbol;

        if (!_rampInApplied && start == 0)
        {
            var ramp = Math.Min(_rampSamples, emitEnd);
            for (var i = 0; i < ramp; i++)
                _buffer[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / _rampSamples));
            _rampInApplied = true;
        }

        if (finish)
        {
            var ramp = Math.Min(globalEnd / 2, _rampSamples);
            for (var i = 0; i < ramp; i++)
                _buffer[globalEnd - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / ramp));
        }

        var output = new float[length];
        for (var i = 0; i < length; i++)
            output[i] = _buffer[start + i];

        _emitSample = emitEnd;
        return output;
    }

    private void AddSymbol(int s)
    {
        var level = _symbols[s] ? 1.0 : -1.0;
        var centre = (int)Math.Round(s * _samplesPerSymbol) + _filterHalf;
        for (var t = 0; t < _filter.Length; t++)
        {
            var index = centre - _filterHalf + t;
            if (index >= 0 && index < _buffer.Count)
                _buffer[index] += (float)(level * _amplitude * _filter[t]);
        }
    }
}
