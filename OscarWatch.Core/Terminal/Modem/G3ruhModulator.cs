namespace OscarWatch.Core.Terminal.Modem;

/// <summary>9600 baud G3RUH baseband transmit: NRZI-scrambled bits shaped with a raised-cosine filter.</summary>
public static class G3ruhModulator
{
    public const int Baud = 9600;
    public const double Alpha = 0.5;
    public const int SymbolSpan = 6;

    public static float[] Modulate(IReadOnlyList<bool> scrambledBits, int sampleRate, double amplitude)
    {
        if (sampleRate < 38400)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "9600 baud G3RUH needs at least 38400 Hz sample rate.");
        if (scrambledBits.Count == 0)
            return [];

        amplitude = Math.Clamp(amplitude, 0, 1);
        var samplesPerSymbol = sampleRate / (double)Baud;
        var filter = DesignRootRaisedCosineInternal(sampleRate);
        var half = filter.Length / 2;
        var symbolCount = scrambledBits.Count;
        var total = (int)Math.Ceiling((symbolCount - 1) * samplesPerSymbol + half * 2);
        var output = new float[total];

        for (var s = 0; s < symbolCount; s++)
        {
            var level = scrambledBits[s] ? 1.0 : -1.0;
            var centre = (int)Math.Round(s * samplesPerSymbol) + half;
            for (var t = 0; t < filter.Length; t++)
            {
                var index = centre - half + t;
                if (index >= 0 && index < total)
                    output[index] += (float)(level * amplitude * filter[t]);
            }
        }

        ApplyRamp(output, sampleRate);
        return output;
    }

    /// <summary>Alternating symbols for setting the radio data-port level.</summary>
    public static float[] TestPattern(double seconds, int sampleRate, double amplitude)
    {
        var symbols = Math.Max(1, (int)(seconds * Baud));
        var bits = new bool[symbols];
        for (var i = 0; i < symbols; i++)
            bits[i] = i % 2 == 0;
        return Modulate(bits, sampleRate, amplitude);
    }

    private static void ApplyRamp(float[] output, int sampleRate)
    {
        var ramp = Math.Min(output.Length / 2, (int)(sampleRate * AfskModulator.RampMs / 1000));
        if (ramp <= 0)
            return;
        for (var i = 0; i < ramp; i++)
        {
            var g = (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / ramp));
            output[i] *= g;
            output[output.Length - 1 - i] *= g;
        }
    }

    internal static float[] DesignRootRaisedCosineInternal(int sampleRate)
    {
        var alpha = Alpha;
        var symbolSpan = SymbolSpan;
        return DesignRootRaisedCosine(sampleRate, alpha, symbolSpan);
    }

    private static float[] DesignRootRaisedCosine(int sampleRate, double alpha, int symbolSpan)
    {
        var samplesPerSymbol = sampleRate / (double)Baud;
        var length = (int)Math.Ceiling(symbolSpan * samplesPerSymbol) | 1;
        var half = length / 2;
        var taps = new float[length];
        var sum = 0.0;
        for (var n = -half; n <= half; n++)
        {
            var t = n / samplesPerSymbol;
            double value;
            if (Math.Abs(t) < 1e-12)
                value = 1.0 - alpha + 4 * alpha / Math.PI;
            else if (Math.Abs(Math.Abs(4 * alpha * t) - 1.0) < 1e-9)
            {
                var a = alpha;
                value = a / Math.Sqrt(2) *
                        ((1 + 2 / Math.PI) * Math.Sin(Math.PI / (4 * a)))
                        + ((1 - 2 / Math.PI) * Math.Cos(Math.PI / (4 * a)));
            }
            else
            {
                value = (Math.Sin(Math.PI * t * (1 - alpha))
                         + 4 * alpha * t * Math.Cos(Math.PI * t * (1 + alpha)))
                        / (Math.PI * t * (1 - (4 * alpha * t) * (4 * alpha * t)));
            }

            taps[n + half] = (float)value;
            sum += value;
        }

        var norm = (float)sum;
        for (var i = 0; i < taps.Length; i++)
            taps[i] /= norm;
        return taps;
    }
}
