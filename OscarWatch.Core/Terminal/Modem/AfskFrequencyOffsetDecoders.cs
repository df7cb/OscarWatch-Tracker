namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Extra mark/space LO offsets for parallel 1200 baud AFSK decoders (Direwolf-style). Chosen to cover
/// residual FM audio tone error between CAT Doppler steps (default threshold 350 Hz) with margin past
/// the measured fixed-LO cliff near +450 Hz.
/// </summary>
public static class AfskFrequencyOffsetDecoders
{
    public const int MarkHz = 1200;
    public const int SpaceHz = 2200;

    /// <summary>Non-zero offsets paired with the centre bank at 0 Hz.</summary>
    public static readonly int[] ExtraOffsetsHz = [-350, -175, 175, 350];

    public static int ChannelCount(bool enabled) => enabled ? 1 + ExtraOffsetsHz.Length : 1;

    public static IEnumerable<int> AllOffsetsHz(bool enabled)
    {
        yield return 0;
        if (!enabled)
            yield break;
        foreach (var o in ExtraOffsetsHz)
            yield return o;
    }
}
