using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>Synthetic AFSK signals and channel impairments for the modem tests.</summary>
internal static class ModemTestSignals
{
    public static byte[] UiFrame(string text, string source = "MM9SQL-7", string path = "ARISS") =>
        Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse(source), Ax25Frame.ParsePath(path), text).Encode();

    /// <summary>One keyed transmission with leading and trailing silence.</summary>
    public static float[] Transmission(
        IEnumerable<byte[]> frames,
        int sampleRate,
        double amplitude = 0.5,
        int txDelayMs = 200,
        int txTailMs = 30,
        double silenceSeconds = 0.2,
        double toneOffsetHz = 0) =>
        TransmissionAtToneFrequencies(frames, sampleRate, amplitude, txDelayMs, txTailMs, silenceSeconds,
            AfskModulator.MarkHz + toneOffsetHz, AfskModulator.SpaceHz + toneOffsetHz);

    public static float[] TransmissionAtToneFrequencies(
        IEnumerable<byte[]> frames,
        int sampleRate,
        double amplitude,
        int txDelayMs,
        int txTailMs,
        double silenceSeconds,
        double markHz,
        double spaceHz)
    {
        var tones = HdlcEncoder.EncodeTransmission(frames, txDelayMs, txTailMs);
        var audio = ModulateToneList(tones, sampleRate, amplitude, markHz, spaceHz);
        var silence = (int)(sampleRate * silenceSeconds);
        var result = new float[audio.Length + 2 * silence];
        audio.CopyTo(result, silence);
        return result;
    }

    /// <summary>Linear tone offset drift across one transmission (simulates mistune changing during a frame).</summary>
    public static float[] TransmissionWithToneDrift(
        byte[] frame,
        int sampleRate,
        double startOffsetHz,
        double endOffsetHz,
        double amplitude = 0.5)
    {
        var tones = HdlcEncoder.EncodeTransmission([frame], 200, 30);
        var samplesPerBit = sampleRate / AfskModulator.Baud;
        var total = (int)Math.Ceiling(tones.Length * samplesPerBit);
        var silence = (int)(sampleRate * 0.2);
        var output = new float[total + 2 * silence];
        var phase = 0.0;
        for (var n = 0; n < total; n++)
        {
            var t = n / (double)Math.Max(1, total - 1);
            var offset = startOffsetHz + (endOffsetHz - startOffsetHz) * t;
            var markHz = AfskModulator.MarkHz + offset;
            var spaceHz = AfskModulator.SpaceHz + offset;
            var bit = Math.Min(tones.Length - 1, (int)(n / samplesPerBit));
            var step = 2 * Math.PI * (tones[bit] ? markHz : spaceHz) / sampleRate;
            output[n + silence] = (float)(amplitude * Math.Sin(phase));
            phase += step;
            if (phase > 2 * Math.PI)
                phase -= 2 * Math.PI;
        }

        return output;
    }

    private static float[] ModulateToneList(IReadOnlyList<bool> tones, int sampleRate, double amplitude, double markHz, double spaceHz)
    {
        var samplesPerBit = sampleRate / AfskModulator.Baud;
        var toneCount = tones.Count;
        var total = (int)Math.Ceiling(toneCount * samplesPerBit);
        var output = new float[total];
        var phase = 0.0;
        var markStep = 2 * Math.PI * markHz / sampleRate;
        var spaceStep = 2 * Math.PI * spaceHz / sampleRate;
        for (var n = 0; n < total; n++)
        {
            var bit = Math.Min(toneCount - 1, (int)(n / samplesPerBit));
            output[n] = (float)(amplitude * Math.Sin(phase));
            phase += tones[bit] ? markStep : spaceStep;
            if (phase > 2 * Math.PI)
                phase -= 2 * Math.PI;
        }

        return output;
    }

    public static void AddNoise(float[] samples, double sigma, int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < samples.Length; i++)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            samples[i] += (float)(sigma * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
    }

    /// <summary>One-pole low-pass with a 300 Hz corner: the 6 dB per octave tilt of speaker audio.</summary>
    public static void DeEmphasise(float[] samples, int sampleRate)
    {
        var a = Math.Exp(-2 * Math.PI * 300 / sampleRate);
        var y = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            y = a * y + (1 - a) * samples[i];
            samples[i] = (float)y;
        }

        Normalise(samples, 0.5);
    }

    /// <summary>First difference: rising about 6 dB per octave, as a pre-emphasised mic input would give.</summary>
    public static void PreEmphasise(float[] samples)
    {
        var previous = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            samples[i] = x - 0.95f * previous;
            previous = x;
        }

        Normalise(samples, 0.5);
    }

    public static void Normalise(float[] samples, double peak)
    {
        var max = samples.Max(Math.Abs);
        if (max <= 0)
            return;
        var g = (float)(peak / max);
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= g;
    }
}
