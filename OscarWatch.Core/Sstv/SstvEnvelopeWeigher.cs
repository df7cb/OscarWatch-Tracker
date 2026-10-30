namespace OscarWatch.Core.Sstv;

/// <summary>
/// Trust in each demodulated sample, from the subcarrier amplitude. SSTV is a constant-amplitude
/// tone, so where noise cancels it the envelope dips and the discriminator's phase step is
/// mostly noise (that is where FM clicks come from). Weighting by envelope power relative to the
/// running level is the energy-weighted frequency estimate: dips count for little, while samples
/// lifted by noise adding in phase still measure the tone well and keep full weight.
/// </summary>
public sealed class SstvEnvelopeWeigher
{
    /// <summary>Weight given to the worst samples, so a stretch of pure noise still averages to something.</summary>
    public const float MinWeight = 0.01f;

    /// <summary>Most a single sample can weigh, so one burst cannot outvote a whole pixel.</summary>
    public const float MaxWeight = 4f;

    private readonly double _alpha;
    private double _level = double.NaN;

    /// <param name="levelSeconds">Time constant of the running level.</param>
    public SstvEnvelopeWeigher(int sampleRate, double levelSeconds = 0.1)
    {
        _alpha = 1 - Math.Exp(-1 / (sampleRate * levelSeconds));
    }

    public void Process(IReadOnlyList<float> envelope, List<float> weights)
    {
        for (var i = 0; i < envelope.Count; i++)
        {
            var e = (double)envelope[i];
            if (double.IsNaN(_level) || _level <= 1e-9)
                _level = Math.Max(e, 1e-6);

            // Limit how far one sample can move the level, so a click does not drag it up.
            _level += _alpha * (Math.Clamp(e, 0.5 * _level, 2 * _level) - _level);
            var ratio = e / _level;
            weights.Add(Math.Clamp((float)(ratio * ratio), MinWeight, MaxWeight));
        }
    }
}
