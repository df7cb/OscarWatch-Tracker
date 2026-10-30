namespace OscarWatch.Core.Sstv;

/// <summary>
/// Manual slant correction: the operator marks two points on an edge that should be vertical
/// (usually the sync edge), and each row is moved sideways so the edge stands straight.
/// </summary>
public static class SstvSlantFix
{
    /// <summary>Points closer together than this many rows give no usable slope.</summary>
    public const double MinRowSpan = 4;

    /// <summary>
    /// Straighten <paramref name="image"/> so the edge through (<paramref name="x1"/>, <paramref name="y1"/>)
    /// and (<paramref name="x2"/>, <paramref name="y2"/>) becomes vertical at <paramref name="x1"/>.
    /// Pixels moved off one side wrap round to the other, as a slanted SSTV line does.
    /// Returns null when the points are too close in height to give a slope.
    /// </summary>
    public static SstvImage? Straighten(SstvImage image, double x1, double y1, double x2, double y2)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2)
            || Math.Abs(y2 - y1) < MinRowSpan)
            return null;

        var slope = (x2 - x1) / (y2 - y1);
        var width = image.Width;
        var result = new SstvImage(width, image.Height);
        var src = image.Rgb;
        var dst = result.Rgb;
        for (var y = 0; y < image.Height; y++)
        {
            var shift = slope * (y - y1);
            var whole = (int)Math.Floor(shift);
            var frac = shift - whole;
            var row = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                var a = row + Wrap(x + whole, width) * 3;
                var b = row + Wrap(x + whole + 1, width) * 3;
                var o = row + x * 3;
                for (var c = 0; c < 3; c++)
                    dst[o + c] = (byte)Math.Clamp(Math.Round(src[a + c] * (1 - frac) + src[b + c] * frac), 0, 255);
            }
        }

        return result;
    }

    /// <summary>A copy of <paramref name="decoded"/> carrying <paramref name="image"/> instead of its picture.</summary>
    public static SstvDecodedImage WithImage(SstvDecodedImage decoded, SstvImage image) => new()
    {
        Mode = decoded.Mode,
        Image = image,
        LineQuality = decoded.LineQuality,
        FromVis = decoded.FromVis,
        Complete = decoded.Complete,
        LockPercent = decoded.LockPercent,
        OffsetHz = decoded.OffsetHz,
        ClockPpm = decoded.ClockPpm,
        StartSeconds = decoded.StartSeconds,
        EndSeconds = decoded.EndSeconds,
        LineFailures = decoded.LineFailures,
        LineSyncScores = decoded.LineSyncScores,
        LineNoise = decoded.LineNoise,
        RepairedLines = decoded.RepairedLines,
    };

    private static int Wrap(int x, int width)
    {
        x %= width;
        return x < 0 ? x + width : x;
    }
}
