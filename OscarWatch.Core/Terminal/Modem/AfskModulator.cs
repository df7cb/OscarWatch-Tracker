namespace OscarWatch.Core.Terminal.Modem;

/// <summary>Bell 202 continuous phase AFSK: mark 1200 Hz, space 2200 Hz, 1200 baud, at any output sample rate.</summary>
public static class AfskModulator
{
    public const double MarkHz = 1200;
    public const double SpaceHz = 2200;
    public const double Baud = 1200;

    /// <summary>Short raised-cosine ramp at each end so keying does not click.</summary>
    public const double RampMs = 2;

    public static float[] Modulate(IReadOnlyList<bool> tones, int sampleRate, double amplitude)
    {
        if (sampleRate < 8000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (tones.Count == 0)
            return [];

        amplitude = Math.Clamp(amplitude, 0, 1);
        var samplesPerBit = sampleRate / Baud;
        var total = (int)Math.Ceiling(tones.Count * samplesPerBit);
        var output = new float[total];
        var phase = 0.0;
        var markStep = 2 * Math.PI * MarkHz / sampleRate;
        var spaceStep = 2 * Math.PI * SpaceHz / sampleRate;
        for (var n = 0; n < total; n++)
        {
            var bit = Math.Min(tones.Count - 1, (int)(n / samplesPerBit));
            output[n] = (float)(amplitude * Math.Sin(phase));
            phase += tones[bit] ? markStep : spaceStep;
            if (phase > 2 * Math.PI)
                phase -= 2 * Math.PI;
        }

        var ramp = Math.Min(total / 2, (int)(sampleRate * RampMs / 1000));
        for (var i = 0; i < ramp; i++)
        {
            var g = (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / ramp));
            output[i] *= g;
            output[total - 1 - i] *= g;
        }

        return output;
    }

    /// <summary>Alternating, mark-only or space-only test tones for setting the transmit level.</summary>
    public static float[] TestTone(AfskTestTone kind, double seconds, int sampleRate, double amplitude)
    {
        var bits = Math.Max(1, (int)(seconds * Baud));
        var tones = new bool[bits];
        for (var i = 0; i < bits; i++)
        {
            tones[i] = kind switch
            {
                AfskTestTone.Mark => true,
                AfskTestTone.Space => false,
                _ => i % 2 == 0
            };
        }

        return Modulate(tones, sampleRate, amplitude);
    }
}

public enum AfskTestTone
{
    Alternating,
    Mark,
    Space
}
