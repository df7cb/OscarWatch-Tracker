namespace OscarWatch.Core.Sstv;

/// <summary>A picture merged from several receptions of the same transmitted picture.</summary>
/// <param name="Copies">How many receptions went into it.</param>
public sealed record SstvCombinedPicture(SstvMode Mode, SstvImage Image, int Copies);

/// <summary>
/// Satellites such as the ISS send the same pictures again and again. Each new picture is
/// compared with earlier ones in the same mode; when one matches (allowing for a picture
/// that was placed a few lines too high or low), the copies are merged line by line,
/// each weighted by how clean it was, so fades and noise in one copy are filled from another.
/// </summary>
public sealed class SstvPictureCombiner
{
    private const int MaxKept = 40;
    private const int ThumbWidth = 80;
    /// <summary>Rows both copies must have in common, as a share of the picture height.</summary>
    private const double MinOverlap = 0.4;
    /// <summary>Mean luma difference over the shared rows below which two copies are the same picture.</summary>
    private const double MaxMatchDifference = 18;
    /// <summary>Furthest a copy may be offset, in lines.</summary>
    private const int MaxOffsetLines = 12;

    private readonly List<Copy> _copies = [];

    private sealed class Copy(SstvMode mode, SstvImage image, float[] rowWeights, float[,] thumb)
    {
        public SstvMode Mode { get; } = mode;
        public SstvImage Image { get; } = image;
        public float[] RowWeights { get; } = rowWeights;
        public float[,] Thumb { get; } = thumb;
        /// <summary>Picture this copy was merged into (copies of one picture share a group).</summary>
        public int Group { get; set; }
        /// <summary>Row of this copy that shows row 0 of the group's picture (copy row = group row + offset).</summary>
        public int Offset { get; set; }
    }

    private int _nextGroup;

    /// <summary>
    /// Remember a new picture. Returns the merged picture when it matched earlier copies,
    /// or null when it is the first copy seen.
    /// </summary>
    public SstvCombinedPicture? Add(SstvDecodedImage picture)
    {
        var copy = new Copy(picture.Mode, picture.Image.Clone(), RowWeights(picture), Thumbnail(picture.Image));
        var match = FindMatch(copy);
        if (match is { } m)
        {
            copy.Group = m.Copy.Group;
            copy.Offset = m.Copy.Offset - m.Offset;
        }
        else
        {
            copy.Group = _nextGroup++;
        }

        _copies.Add(copy);
        if (_copies.Count > MaxKept)
            _copies.RemoveAt(0);
        if (match is null)
            return null;

        var group = _copies.Where(c => c.Group == copy.Group).ToList();
        return new SstvCombinedPicture(copy.Mode, Merge(group, copy), group.Count);
    }

    private (Copy Copy, int Offset)? FindMatch(Copy copy)
    {
        (Copy Copy, int Offset, double Difference)? best = null;
        for (var i = _copies.Count - 1; i >= 0; i--)
        {
            var other = _copies[i];
            if (other.Mode.Id != copy.Mode.Id)
                continue;
            var step = copy.Mode.RowsPerLine;
            for (var offset = -MaxOffsetLines * step; offset <= MaxOffsetLines * step; offset += step)
            {
                var difference = Difference(copy, other, offset);
                if (difference < MaxMatchDifference && (best is null || difference < best.Value.Difference))
                    best = (other, offset, difference);
            }
        }

        return best is { } b ? (b.Copy, b.Offset) : null;
    }

    /// <summary>Mean luma difference where both copies have rows, with <paramref name="copy"/> row r against <paramref name="other"/> row r + offset.</summary>
    private static double Difference(Copy copy, Copy other, int offset)
    {
        var height = copy.Image.Height;
        double total = 0;
        double weight = 0;
        var rows = 0;
        for (var r = 0; r < height; r++)
        {
            var o = r + offset;
            if (o < 0 || o >= height)
                continue;
            var w = Math.Min(copy.RowWeights[r], other.RowWeights[o]);
            if (w <= 0)
                continue;
            rows++;
            double row = 0;
            for (var x = 0; x < ThumbWidth; x++)
                row += Math.Abs(copy.Thumb[r, x] - other.Thumb[o, x]);
            total += w * row / ThumbWidth;
            weight += w;
        }

        return rows < height * MinOverlap || weight <= 0 ? double.MaxValue : total / weight;
    }

    /// <summary>Weighted average of every copy in the group, in the rows of <paramref name="reference"/>.</summary>
    private static SstvImage Merge(List<Copy> group, Copy reference)
    {
        var width = reference.Image.Width;
        var height = reference.Image.Height;
        var result = new SstvImage(width, height);
        var sums = new double[width * 3];
        for (var r = 0; r < height; r++)
        {
            Array.Clear(sums);
            double total = 0;
            foreach (var c in group)
            {
                var row = r + c.Offset - reference.Offset;
                if (row < 0 || row >= height || c.Image.Width != width)
                    continue;
                var w = c.RowWeights[row];
                if (w <= 0)
                    continue;
                total += w;
                var start = row * width * 3;
                for (var i = 0; i < width * 3; i++)
                    sums[i] += w * c.Image.Rgb[start + i];
            }

            if (total <= 0)
                continue;
            for (var x = 0; x < width; x++)
                result.SetPixel(x, r, sums[x * 3] / total, sums[x * 3 + 1] / total, sums[x * 3 + 2] / total);
        }

        return result;
    }

    /// <summary>Weight of each row: 0 where nothing was decoded, less for noisy or unlocked lines.</summary>
    private static float[] RowWeights(SstvDecodedImage picture)
    {
        var mode = picture.Mode;
        var weights = new float[picture.Image.Height];
        for (var line = 0; line < picture.LineQuality.Count; line++)
        {
            var quality = picture.LineQuality[line];
            if (quality == SstvLineQuality.Missing)
                continue;
            var noise = line < picture.LineNoise.Count && float.IsFinite(picture.LineNoise[line]) ? picture.LineNoise[line] : 1f;
            var w = (quality == SstvLineQuality.Locked ? 1f : 0.5f) / MathF.Max(0.5f, noise * noise);
            for (var k = 0; k < mode.RowsPerLine; k++)
            {
                var row = line * mode.RowsPerLine + k;
                if (row < weights.Length)
                    weights[row] = w;
            }
        }

        return weights;
    }

    private static float[,] Thumbnail(SstvImage image)
    {
        var thumb = new float[image.Height, ThumbWidth];
        for (var r = 0; r < image.Height; r++)
        {
            for (var x = 0; x < ThumbWidth; x++)
            {
                var x0 = x * image.Width / ThumbWidth;
                var x1 = Math.Max(x0 + 1, (x + 1) * image.Width / ThumbWidth);
                double sum = 0;
                for (var xx = x0; xx < x1; xx++)
                {
                    var (red, green, blue) = image.GetPixel(xx, r);
                    sum += 0.299 * red + 0.587 * green + 0.114 * blue;
                }

                thumb[r, x] = (float)(sum / (x1 - x0));
            }
        }

        return thumb;
    }
}
