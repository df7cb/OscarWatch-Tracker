namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Documented synthetic Doppler/discriminator impairments the 9600 G3RUH receiver tolerates at 48 kHz.
/// Values are verified by <c>G3ruhDopplerRobustnessTests</c>. They model the FM data-port baseline after
/// demodulation (constant offset, CAT steps of a few hundred Hz to about 1 kHz RF, and slow uncorrected drift).
/// </summary>
public static class G3ruhDopplerTolerance
{
    /// <summary>Constant discriminator baseline offset (normalised data-port level).</summary>
    public const float MaxConstantDiscriminatorOffset = 0.42f;

    /// <summary>Sudden baseline step applied mid-frame or between frames (same units).</summary>
    public const float MaxDiscriminatorStep = 0.38f;

    /// <summary>Linear discriminator drift across one frame (volts per second at nominal 0.35 symbol level).</summary>
    public const float MaxDiscriminatorRampVoltsPerSecond = 2.5f;
}
