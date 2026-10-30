using OscarWatch.Core.Sstv;

namespace OscarWatch.Tests.Sstv;

/// <summary>Test-only SSTV transmitter, so decoder tests do not need recordings.</summary>
internal static class SstvTestEncoder
{
    public sealed class Settings
    {
        /// <summary>Sender clock: every duration is multiplied by this (1.005 runs 0.5% slow).</summary>
        public double ClockRatio { get; init; } = 1.0;
        /// <summary>Audio offset in Hz added to every tone (an off-tune receiver).</summary>
        public double OffsetHz { get; init; }
        /// <summary>Offset change in Hz per second (Doppler drift on SSB audio).</summary>
        public double DriftHzPerSecond { get; init; }
        /// <summary>Gaussian noise RMS. Tones have amplitude 0.5.</summary>
        public double NoiseRms { get; init; }
        /// <summary>Average rate of short impulses, like the pops of a receiver near FM threshold.</summary>
        public double ClicksPerSecond { get; init; }
        /// <summary>Peak amplitude of each impulse. Tones have amplitude 0.5.</summary>
        public double ClickAmplitude { get; init; } = 1.5;
        /// <summary>Period of noise bursts in milliseconds, like a fading signal. Zero for none.</summary>
        public double BurstEveryMs { get; init; }
        public double BurstMs { get; init; }
        /// <summary>Gaussian noise RMS added during a burst, on top of <see cref="NoiseRms"/>.</summary>
        public double BurstNoiseRms { get; init; }
        public int Seed { get; init; } = 1234;
        public bool IncludeVis { get; init; } = true;
        /// <summary>Overrides the VIS code sent, for example to send a bad parity.</summary>
        public int? VisCodeOverride { get; init; }
        /// <summary>Send this VIS data bit at 1200 Hz, halfway between a 1 and a 0.</summary>
        public int? DoubtfulVisBit { get; init; }
        public bool FlipParity { get; init; }
        public double LeadSeconds { get; init; } = 0.5;
        public double TailSeconds { get; init; } = 1.0;

        /// <summary>
        /// Doppler time compression: the sender's timing runs this many ppm slow at the start
        /// and fast by the same amount at the end, sweeping linearly (range rate over c on a pass).
        /// </summary>
        public double TimeCompressionPpm { get; init; }

        /// <summary>
        /// When set, the audio is sent over a simulated FM radio link at this carrier-to-noise
        /// ratio in <see cref="IfBandwidthHz"/>, so the receiver's own discriminator produces the
        /// threshold clicks, instead of noise being added to the audio.
        /// </summary>
        public double? CnrDb { get; init; }
        public double IfBandwidthHz { get; init; } = 12000;
        /// <summary>Peak FM deviation of a full-level tone (amplitude 0.5).</summary>
        public double DeviationHz { get; init; } = 3000;
        /// <summary>Receiver audio low-pass after the discriminator.</summary>
        public double AudioLowPassHz { get; init; } = 3000;
        /// <summary>
        /// Uncompensated receiver de-emphasis time constant (0 for flat audio). 75 µs tilts
        /// 2300 Hz about 2 dB below 1500 Hz, as from a speaker output on a flat transmitter.
        /// </summary>
        public double DeEmphasisUs { get; init; }
        /// <summary>Spin fading of the carrier: period in seconds (0 for none) and depth in dB.</summary>
        public double FadePeriodSeconds { get; init; }
        public double FadeDepthDb { get; init; }
        /// <summary>Carrier drop-outs (antenna nulls, obstructions): every so often, for so long.</summary>
        public double DropoutEverySeconds { get; init; }
        public double DropoutSeconds { get; init; }
        /// <summary>Time of the first drop-out.</summary>
        public double DropoutStartSeconds { get; init; }
    }

    public delegate (double R, double G, double B) PixelSource(int x, int y);

    /// <summary>Smooth pattern with four horizontal cycles, so position errors show and blur does not.</summary>
    public static (double R, double G, double B) TestPattern(int x, int y, int width, int height)
    {
        var r = 127.5 + 110 * Math.Sin(2 * Math.PI * 4 * x / width);
        var g = 20 + 215.0 * y / Math.Max(1, height - 1);
        var b = 127.5 + 100 * Math.Cos(2 * Math.PI * 3 * x / width) * Math.Cos(2 * Math.PI * y / height);
        return (r, g, b);
    }

    public static float[] Encode(SstvMode mode, int rate, Settings? settings = null)
    {
        settings ??= new Settings();
        return Encode(mode, (x, y) => TestPattern(x, y, mode.Width, mode.Height), rate, settings);
    }

    public static float[] Encode(SstvMode mode, PixelSource pixel, int rate, Settings settings)
    {
        var audio = EncodeAudio(mode, pixel, rate, settings);
        return settings.CnrDb is { } cnr ? FmLink(audio, rate, cnr, settings) : audio;
    }

    private static float[] EncodeAudio(SstvMode mode, PixelSource pixel, int rate, Settings settings)
    {
        var totalMs = 1000 * (settings.LeadSeconds + (settings.IncludeVis ? 0.91 : 0) + mode.ImageSeconds);
        var tone = new ToneWriter(rate, settings, totalMs);
        tone.Silence(settings.LeadSeconds);

        if (settings.IncludeVis)
        {
            tone.Add(1900, 300);
            tone.Add(1200, 10);
            tone.Add(1900, 300);
            tone.Add(1200, 30);
            var code = settings.VisCodeOverride ?? mode.VisCode;
            var ones = 0;
            for (var i = 0; i < 7; i++)
            {
                var bit = (code >> i) & 1;
                ones += bit;
                tone.Add(i == settings.DoubtfulVisBit ? 1200 : bit == 1 ? 1100 : 1300, 30);
            }

            var parity = (ones & 1) ^ (settings.FlipParity ? 1 : 0);
            tone.Add(parity == 1 ? 1100 : 1300, 30);
            tone.Add(1200, 30);
        }

        if (mode.LeadInMs > 0)
            tone.Add(1200, mode.LeadInMs);

        for (var line = 0; line < mode.TransmittedLines; line++)
            EncodeLine(mode, pixel, line, tone);

        tone.Silence(settings.TailSeconds);
        return tone.ToArray();
    }

    private static void EncodeLine(SstvMode mode, PixelSource pixel, int line, ToneWriter tone)
    {
        var row = line * mode.RowsPerLine;
        var segments = mode.Segments;
        for (var s = 0; s < segments.Count; s++)
        {
            var seg = segments[s];
            switch (seg.Channel)
            {
                case SstvChannel.Sync:
                    tone.Add(1200, seg.Ms);
                    break;
                case SstvChannel.Gap:
                    tone.Add(GapHz(mode, segments, s, line), seg.Ms);
                    break;
                default:
                    var values = new double[mode.Width];
                    for (var x = 0; x < mode.Width; x++)
                        values[x] = ChannelValue(mode, pixel, seg.Channel, x, row, line);
                    tone.Scan(values, seg.Ms);
                    break;
            }
        }
    }

    private static double GapHz(SstvMode mode, IReadOnlyList<SstvSegment> segments, int index, int line)
    {
        if (mode.Id is not (SstvModeId.Robot36 or SstvModeId.Robot72))
            return 1500;

        var seg = segments[index];
        if (seg.Ms == 1.5)
            return 1900;
        if (seg.Ms != 4.5)
            return 1500;

        var next = segments[index + 2].Channel;
        var blue = next == SstvChannel.BMinusY
            || (next == SstvChannel.AlternatingChroma && line % 2 == 1);
        return blue ? 2300 : 1500;
    }

    private static double ChannelValue(SstvMode mode, PixelSource pixel, SstvChannel channel, int x, int row, int line)
    {
        (double R, double G, double B) Px(int y) => pixel(x, Math.Clamp(y, 0, mode.Height - 1));

        switch (channel)
        {
            case SstvChannel.Red: return Px(row).R;
            case SstvChannel.Green: return Px(row).G;
            case SstvChannel.Blue: return Px(row).B;
            case SstvChannel.Y0: return Luma(Px(row));
            case SstvChannel.Y1: return Luma(Px(row + 1));
            case SstvChannel.RMinusY:
                return mode.RowsPerLine == 2 ? (Cr(Px(row)) + Cr(Px(row + 1))) / 2 : Cr(Px(row));
            case SstvChannel.BMinusY:
                return mode.RowsPerLine == 2 ? (Cb(Px(row)) + Cb(Px(row + 1))) / 2 : Cb(Px(row));
            case SstvChannel.AlternatingChroma:
                return line % 2 == 0
                    ? (Cr(Px(row)) + Cr(Px(row + 1))) / 2
                    : (Cb(Px(row - 1)) + Cb(Px(row))) / 2;
            default:
                return 0;
        }
    }

    public static double Luma((double R, double G, double B) p) => 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
    public static double Cb((double R, double G, double B) p) => 128 - 0.168736 * p.R - 0.331264 * p.G + 0.5 * p.B;
    public static double Cr((double R, double G, double B) p) => 128 + 0.5 * p.R - 0.418688 * p.G - 0.081312 * p.B;

    /// <summary>
    /// FM radio link: frequency-modulate the audio onto a carrier, fade it, add complex noise
    /// at the carrier-to-noise ratio, IF filter, discriminate, then filter the audio as a
    /// receiver does. Filter delays are removed so the timing matches the input.
    /// </summary>
    private static float[] FmLink(float[] audio, int rate, double cnrDb, Settings settings)
    {
        var random = new Random(settings.Seed + 7);
        var kf = settings.DeviationHz / 0.5;
        var cnr = Math.Pow(10, cnrDb / 10);
        var sigma = Math.Sqrt(rate / (2 * settings.IfBandwidthHz * cnr));
        var n = audio.Length;
        var i = new double[n];
        var q = new double[n];
        var phase = 0.0;
        for (var k = 0; k < n; k++)
        {
            phase += 2 * Math.PI * kf * audio[k] / rate;
            if (phase > Math.PI)
                phase -= 2 * Math.PI;
            else if (phase < -Math.PI)
                phase += 2 * Math.PI;
            var a = CarrierLevel(k / (double)rate, settings);
            i[k] = a * Math.Cos(phase) + sigma * Gaussian(random);
            q[k] = a * Math.Sin(phase) + sigma * Gaussian(random);
        }

        var ifTaps = SstvFirFilter.LowPass(settings.IfBandwidthHz / 2, rate, Math.Max(31, rate / 600 | 1));
        i = Filter(i, ifTaps);
        q = Filter(q, ifTaps);

        var output = new double[n];
        for (var k = 1; k < n; k++)
        {
            var dphi = Math.Atan2(q[k] * i[k - 1] - i[k] * q[k - 1], i[k] * i[k - 1] + q[k] * q[k - 1]);
            output[k] = dphi * rate / (2 * Math.PI) / kf;
        }

        output = Filter(output, SstvFirFilter.LowPass(settings.AudioLowPassHz, rate, Math.Max(31, rate / 400 | 1)));
        if (settings.DeEmphasisUs > 0)
        {
            var alpha = 1 - Math.Exp(-1 / (rate * settings.DeEmphasisUs * 1e-6));
            // Keep a 1900 Hz tone at its level, so only the tilt across the band changes.
            var w = 2 * Math.PI * 1900 * settings.DeEmphasisUs * 1e-6;
            var gain = Math.Sqrt(1 + w * w);
            var y = 0.0;
            for (var k = 0; k < n; k++)
            {
                y += alpha * (output[k] - y);
                output[k] = y * gain;
            }
        }

        var result = new float[n];
        for (var k = 0; k < n; k++)
            result[k] = (float)Math.Clamp(output[k], -1.5, 1.5);
        return result;
    }

    private static double CarrierLevel(double seconds, Settings settings)
    {
        var sinceDropout = seconds - settings.DropoutStartSeconds;
        if (settings.DropoutEverySeconds > 0 && settings.DropoutSeconds > 0
            && sinceDropout >= 0 && sinceDropout % settings.DropoutEverySeconds < settings.DropoutSeconds)
            return 0;
        if (settings.FadePeriodSeconds <= 0 || settings.FadeDepthDb <= 0)
            return 1;
        var depth = 0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds / settings.FadePeriodSeconds);
        return Math.Pow(10, -settings.FadeDepthDb * depth / 20);
    }

    /// <summary>Zero-phase FIR: the filter's delay is taken out of the output.</summary>
    private static double[] Filter(double[] input, double[] taps)
    {
        var delay = (taps.Length - 1) / 2;
        var output = new double[input.Length];
        for (var k = 0; k < input.Length; k++)
        {
            double sum = 0;
            for (var t = 0; t < taps.Length; t++)
            {
                var j = k + delay - t;
                if (j >= 0 && j < input.Length)
                    sum += taps[t] * input[j];
            }

            output[k] = sum;
        }

        return output;
    }

    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private sealed class ToneWriter(int rate, Settings settings, double totalMs)
    {
        private readonly List<float> _samples = new();
        private readonly Random _random = new(settings.Seed);
        private readonly Random _clickRandom = new(settings.Seed + 1);
        private double _phase;
        private double _timeMs;
        private int _clickLeft;
        private int _clickLength;
        private double _clickPeak;

        public void Silence(double seconds)
        {
            var n = (int)(seconds * rate);
            for (var i = 0; i < n; i++)
                _samples.Add((float)Noise());
        }

        public void Add(double hz, double ms) => Generate(ms, _ => hz);

        /// <summary>One tone per pixel, 1500 Hz black to 2300 Hz white.</summary>
        public void Scan(double[] values, double ms)
        {
            var w = values.Length;
            Generate(ms, fraction =>
            {
                var x = Math.Clamp((int)(fraction * w), 0, w - 1);
                return 1500 + Math.Clamp(values[x], 0, 255) / 255.0 * 800;
            });
        }

        /// <summary>Emit samples for a span of sender time, keeping the phase continuous.</summary>
        private void Generate(double ms, Func<double, double> hzAt)
        {
            var startMs = _timeMs;
            var compression = settings.TimeCompressionPpm * (1 - 2 * Math.Clamp(_timeMs / totalMs, 0, 1)) * 1e-6;
            var endMs = _timeMs + ms * settings.ClockRatio * (1 + compression);
            var firstSample = (long)Math.Ceiling(startMs * rate / 1000.0);
            var lastSample = (long)Math.Ceiling(endMs * rate / 1000.0);
            for (var n = firstSample; n < lastSample; n++)
            {
                var tMs = n * 1000.0 / rate;
                var fraction = (tMs - startMs) / (endMs - startMs);
                var seconds = tMs / 1000.0;
                var hz = hzAt(fraction) + settings.OffsetHz + settings.DriftHzPerSecond * seconds;
                _phase += 2 * Math.PI * hz / rate;
                if (_phase > 2 * Math.PI)
                    _phase -= 2 * Math.PI;
                _samples.Add((float)(0.5 * Math.Sin(_phase) + Noise()));
            }

            _timeMs = endMs;
        }

        /// <summary>Impairments for the next sample: Gaussian noise, noise bursts and impulses.</summary>
        private double Noise()
        {
            var n = _samples.Count;
            var rms = settings.NoiseRms;
            if (settings.BurstEveryMs > 0 && settings.BurstMs > 0)
            {
                var period = settings.BurstEveryMs * rate / 1000.0;
                if (n % period < settings.BurstMs * rate / 1000.0)
                    rms = Math.Sqrt(rms * rms + settings.BurstNoiseRms * settings.BurstNoiseRms);
            }

            var value = 0.0;
            if (rms > 0)
            {
                var u1 = 1.0 - _random.NextDouble();
                var u2 = _random.NextDouble();
                value = rms * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }

            if (settings.ClicksPerSecond > 0)
            {
                if (_clickLeft == 0 && _clickRandom.NextDouble() < settings.ClicksPerSecond / rate)
                {
                    // About 0.3 ms long, one lobe, either polarity.
                    _clickLength = Math.Max(3, rate * 3 / 10000);
                    _clickLeft = _clickLength;
                    _clickPeak = settings.ClickAmplitude * (_clickRandom.NextDouble() < 0.5 ? -1 : 1);
                }

                if (_clickLeft > 0)
                {
                    var t = (double)(_clickLength - _clickLeft) / _clickLength;
                    value += _clickPeak * Math.Sin(Math.PI * t);
                    _clickLeft--;
                }
            }

            return value;
        }

        public float[] ToArray() => _samples.ToArray();
    }
}
