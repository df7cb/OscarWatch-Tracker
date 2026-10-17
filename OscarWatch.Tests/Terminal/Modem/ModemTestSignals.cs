using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Modem;

namespace OscarWatch.Tests.Terminal.Modem;

/// <summary>Synthetic AFSK signals and channel impairments for the modem tests.</summary>
internal static class ModemTestSignals
{
    public static byte[] UiFrame(string text, string source = "MM9SQL-7", string path = "ARISS") =>
        Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse(source), Ax25Frame.ParsePath(path), text).Encode();

    /// <summary>Short UI frame (~16 ms airtime at 9600 baud) for back-to-back frame tests.</summary>
    public static byte[] ShortUiFrame(string tag) => UiFrame(tag, source: "N0CALL", path: "");

    /// <summary>One 9600 G3RUH keyed transmission with leading and trailing silence.</summary>
    public static float[] G3ruhTransmission(
        IEnumerable<byte[]> frames,
        int sampleRate,
        double amplitude = 0.35,
        int txDelayMs = 200,
        int txTailMs = 30,
        double silenceSeconds = 0.2)
    {
        var bits = HdlcEncoder.EncodeG3ruhTransmission(frames, txDelayMs, txTailMs);
        var audio = G3ruhModulator.Modulate(bits, sampleRate, amplitude);
        var silence = (int)(sampleRate * silenceSeconds);
        var result = new float[audio.Length + 2 * silence];
        audio.CopyTo(result, silence);
        return result;
    }

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

    /// <summary>Adds a constant discriminator baseline (FM data-port DC) across the whole buffer.</summary>
    public static void AddDiscriminatorDcOffset(Span<float> samples, float offset)
    {
        for (var i = 0; i < samples.Length; i++)
            samples[i] += offset;
    }

    /// <summary>Sudden baseline step at <paramref name="stepIndex"/> (CAT frequency hop on the data port).</summary>
    public static void AddDiscriminatorDcStep(Span<float> samples, int stepIndex, float step)
    {
        for (var i = Math.Clamp(stepIndex, 0, samples.Length); i < samples.Length; i++)
            samples[i] += step;
    }

    /// <summary>Linear discriminator drift (residual uncorrected Doppler on the FM demod output).</summary>
    public static void AddDiscriminatorDcRamp(Span<float> samples, int sampleRate, float startOffset, float voltsPerSecond)
    {
        for (var i = 0; i < samples.Length; i++)
            samples[i] += startOffset + voltsPerSecond * (i / (float)sampleRate);
    }

    /// <summary>First sample index of keyed G3RUH audio (after leading silence).</summary>
    public static int G3ruhPayloadStart(int sampleRate, double silenceSeconds = 0.2) =>
        (int)(sampleRate * silenceSeconds);

    /// <summary>Sample index at the middle of the AX.25 payload (inside the frame, after TXDELAY flags).</summary>
    public static int G3ruhMidFrameSample(int sampleRate, int txDelayMs = 200, double silenceSeconds = 0.2)
    {
        var start = G3ruhPayloadStart(sampleRate, silenceSeconds);
        var frame = UiFrame("mid-frame marker 9600 G3RUH");
        var delayFlags = HdlcEncoder.FlagsFor(txDelayMs, HdlcEncoder.G3ruhBaud);
        var keyed = HdlcEncoder.EncodeG3ruhTransmission([frame], txDelayMs, txTailMs: 0);
        var delayOnly = HdlcEncoder.EncodeG3ruhBits([], delayFlags, 0);
        var keyedSamples = G3ruhModulator.Modulate(keyed, sampleRate, 0.35).Length;
        var delaySamples = G3ruhModulator.Modulate(delayOnly, sampleRate, 0.35).Length;
        var mid = start + delaySamples + (keyedSamples - delaySamples) / 2;
        AssertMidFramePlacement(start, start + keyedSamples, mid);
        return mid;
    }

    /// <summary>Sample index in the inter-frame flag gap after <paramref name="firstFrameIndex"/>.</summary>
    public static int G3ruhInterFrameGapSample(
        IReadOnlyList<byte[]> frames,
        int firstFrameIndex,
        int sampleRate,
        int txDelayMs = 200,
        double silenceSeconds = 0.2)
    {
        var start = G3ruhPayloadStart(sampleRate, silenceSeconds);
        var delayFlags = HdlcEncoder.FlagsFor(txDelayMs, HdlcEncoder.G3ruhBaud);
        var prefix = frames.Take(firstFrameIndex + 1).ToArray();
        var bitsThroughFirst = HdlcEncoder.EncodeG3ruhBits(prefix, delayFlags, 1);
        var throughSamples = G3ruhModulator.Modulate(bitsThroughFirst, sampleRate, 0.35).Length;
        var flagSamples = (int)Math.Round(sampleRate / (double)HdlcEncoder.G3ruhBaud * 8);
        return start + throughSamples - flagSamples / 2;
    }

    private static void AssertMidFramePlacement(int keyedStart, int keyedEnd, int mid)
    {
        if (mid <= keyedStart || mid >= keyedEnd)
            throw new InvalidOperationException($"Mid-frame step {mid} lies outside keyed audio [{keyedStart}, {keyedEnd}).");
    }
}
