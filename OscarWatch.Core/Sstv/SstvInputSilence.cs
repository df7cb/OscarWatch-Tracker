namespace OscarWatch.Core.Sstv;

/// <summary>
/// Notices when the SSTV input carries no audio at all, usually because the wrong soundcard
/// (often the system default) is selected. Receiver hiss is far louder than the threshold,
/// so any real radio audio counts as sound.
/// </summary>
public sealed class SstvInputSilence
{
    /// <summary>Peak below this (about −60 dBFS) counts as silence.</summary>
    public const float SilentPeak = 0.001f;

    /// <summary>Continuous silence after which the input is reported as silent.</summary>
    public const double SilentSeconds = 10;

    private readonly int _sampleRate;
    private long _silentSamples;

    public SstvInputSilence(int sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : throw new ArgumentOutOfRangeException(nameof(sampleRate));
    }

    /// <summary>True once the input has stayed silent for <see cref="SilentSeconds"/>; false again when sound returns.</summary>
    public bool IsSilent => _silentSamples >= SilentSeconds * _sampleRate;

    /// <summary>True if any sound has been heard since this watch was created.</summary>
    public bool HeardSound { get; private set; }

    /// <summary>Loudest sample so far (0 to 1).</summary>
    public float LoudestPeak { get; private set; }

    public void Add(ReadOnlySpan<float> samples)
    {
        var peak = Peak(samples);
        if (peak > LoudestPeak)
            LoudestPeak = peak;

        if (peak >= SilentPeak)
        {
            HeardSound = true;
            _silentSamples = 0;
        }
        else
        {
            _silentSamples += samples.Length;
        }
    }

    /// <summary>True when a whole recording is silent, so there is nothing to decode.</summary>
    public static bool IsSilentRecording(ReadOnlySpan<float> samples) => Peak(samples) < SilentPeak;

    private static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var s in samples)
        {
            var a = Math.Abs(s);
            if (a > peak)
                peak = a;
        }

        return peak;
    }
}
