namespace OscarWatch.Core.Sstv;

/// <param name="Psnr">Peak signal-to-noise ratio of the RGB difference in dB (higher is closer).</param>
/// <param name="Ssim">Structural similarity of the brightness, 0 to 1 (1 is identical).</param>
/// <param name="DxPixels">Sideways shift of the reference that lined the two pictures up.</param>
/// <param name="DyPixels">Vertical shift of the reference that lined the two pictures up.</param>
public readonly record struct SstvImageMatch(double Psnr, double Ssim, int DxPixels, int DyPixels);

/// <summary>
/// Scores a decoded picture against another picture of the same scene: an original
/// sent by the station, another decoder's output, or an earlier copy. The other picture
/// is scaled to the decoded size and lined up within a few pixels first, because
/// decoders crop and place the picture slightly differently.
/// </summary>
public static class SstvImageCompare
{
    private const double C1 = 0.01 * 255 * 0.01 * 255;
    private const double C2 = 0.03 * 255 * 0.03 * 255;
    private const int SsimBlock = 8;

    /// <summary>Compare <paramref name="decoded"/> with <paramref name="reference"/> (any size).</summary>
    /// <param name="maxShift">Largest shift tried in each direction, in decoded pixels.</param>
    public static SstvImageMatch Compare(SstvImage decoded, SstvImage reference, int maxShift = 8)
    {
        var w = decoded.Width;
        var h = decoded.Height;
        var scaled = reference.Width == w && reference.Height == h ? reference : Resize(reference, w, h);
        var a = Luma(decoded);
        var b = Luma(scaled);

        var bestDx = 0;
        var bestDy = 0;
        var bestError = double.MaxValue;
        for (var dy = -maxShift; dy <= maxShift; dy++)
        {
            for (var dx = -maxShift; dx <= maxShift; dx++)
            {
                var error = MeanSquare(a, b, w, h, dx, dy, maxShift, step: 2);
                if (error < bestError)
                {
                    bestError = error;
                    bestDx = dx;
                    bestDy = dy;
                }
            }
        }

        var mse = RgbMeanSquare(decoded, scaled, bestDx, bestDy, maxShift);
        var psnr = mse <= 1e-9 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
        return new SstvImageMatch(psnr, Ssim(a, b, w, h, bestDx, bestDy, maxShift), bestDx, bestDy);
    }

    /// <summary>
    /// Quick likeness of two pictures, 0 to 1, from small brightness thumbnails without any
    /// shift search. Used to pick which of several candidates to compare in full.
    /// </summary>
    public static double Likeness(SstvImage a, SstvImage b)
    {
        const int tw = 40;
        const int th = 32;
        var ta = Luma(Resize(a, tw, th));
        var tb = Luma(Resize(b, tw, th));
        return Math.Max(0, Correlation(ta, tb));
    }

    /// <summary>Brightness (BT.601 luma) of every pixel.</summary>
    public static float[] Luma(SstvImage image)
    {
        var rgb = image.Rgb;
        var luma = new float[image.Width * image.Height];
        for (int i = 0, o = 0; i < luma.Length; i++, o += 3)
            luma[i] = (float)(0.299 * rgb[o] + 0.587 * rgb[o + 1] + 0.114 * rgb[o + 2]);
        return luma;
    }

    /// <summary>Area-averaged (when shrinking) or bilinear (when enlarging) resize.</summary>
    public static SstvImage Resize(SstvImage source, int width, int height)
    {
        var result = new SstvImage(width, height);
        var sx = (double)source.Width / width;
        var sy = (double)source.Height / height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (sx <= 1 && sy <= 1)
                {
                    var (r, g, b) = Bilinear(source, (x + 0.5) * sx - 0.5, (y + 0.5) * sy - 0.5);
                    result.SetPixel(x, y, r, g, b);
                    continue;
                }

                var x0 = (int)Math.Floor(x * sx);
                var x1 = Math.Max(x0 + 1, (int)Math.Floor((x + 1) * sx));
                var y0 = (int)Math.Floor(y * sy);
                var y1 = Math.Max(y0 + 1, (int)Math.Floor((y + 1) * sy));
                double rs = 0, gs = 0, bs = 0;
                var n = 0;
                for (var yy = y0; yy < Math.Min(y1, source.Height); yy++)
                {
                    for (var xx = x0; xx < Math.Min(x1, source.Width); xx++)
                    {
                        var p = source.GetPixel(xx, yy);
                        rs += p.R;
                        gs += p.G;
                        bs += p.B;
                        n++;
                    }
                }

                if (n > 0)
                    result.SetPixel(x, y, rs / n, gs / n, bs / n);
            }
        }

        return result;
    }

    private static (double R, double G, double B) Bilinear(SstvImage image, double fx, double fy)
    {
        fx = Math.Clamp(fx, 0, image.Width - 1);
        fy = Math.Clamp(fy, 0, image.Height - 1);
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var x1 = Math.Min(x0 + 1, image.Width - 1);
        var y1 = Math.Min(y0 + 1, image.Height - 1);
        var tx = fx - x0;
        var ty = fy - y0;
        var p00 = image.GetPixel(x0, y0);
        var p10 = image.GetPixel(x1, y0);
        var p01 = image.GetPixel(x0, y1);
        var p11 = image.GetPixel(x1, y1);
        double Mix(byte a, byte b, byte c, byte d) =>
            (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
        return (Mix(p00.R, p10.R, p01.R, p11.R), Mix(p00.G, p10.G, p01.G, p11.G), Mix(p00.B, p10.B, p01.B, p11.B));
    }

    private static double MeanSquare(float[] a, float[] b, int w, int h, int dx, int dy, int margin, int step)
    {
        double sum = 0;
        var n = 0;
        for (var y = margin; y < h - margin; y += step)
        {
            var ra = y * w;
            var rb = (y + dy) * w + dx;
            for (var x = margin; x < w - margin; x += step)
            {
                var d = a[ra + x] - b[rb + x];
                sum += d * d;
                n++;
            }
        }

        return n == 0 ? double.MaxValue : sum / n;
    }

    private static double RgbMeanSquare(SstvImage a, SstvImage b, int dx, int dy, int margin)
    {
        double sum = 0;
        var n = 0;
        for (var y = margin; y < a.Height - margin; y++)
        {
            for (var x = margin; x < a.Width - margin; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x + dx, y + dy);
                double dr = pa.R - pb.R, dg = pa.G - pb.G, db = pa.B - pb.B;
                sum += dr * dr + dg * dg + db * db;
                n += 3;
            }
        }

        return n == 0 ? double.MaxValue : sum / n;
    }

    /// <summary>Mean SSIM over 8 by 8 blocks of the overlapping area.</summary>
    private static double Ssim(float[] a, float[] b, int w, int h, int dx, int dy, int margin)
    {
        double total = 0;
        var blocks = 0;
        for (var by = margin; by + SsimBlock <= h - margin; by += SsimBlock)
        {
            for (var bx = margin; bx + SsimBlock <= w - margin; bx += SsimBlock)
            {
                double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
                for (var y = by; y < by + SsimBlock; y++)
                {
                    for (var x = bx; x < bx + SsimBlock; x++)
                    {
                        double va = a[y * w + x];
                        double vb = b[(y + dy) * w + x + dx];
                        sa += va;
                        sb += vb;
                        saa += va * va;
                        sbb += vb * vb;
                        sab += va * vb;
                    }
                }

                const double n = SsimBlock * SsimBlock;
                var ma = sa / n;
                var mb = sb / n;
                var va2 = saa / n - ma * ma;
                var vb2 = sbb / n - mb * mb;
                var cov = sab / n - ma * mb;
                total += (2 * ma * mb + C1) * (2 * cov + C2) / ((ma * ma + mb * mb + C1) * (va2 + vb2 + C2));
                blocks++;
            }
        }

        return blocks == 0 ? 0 : total / blocks;
    }

    private static double Correlation(float[] a, float[] b)
    {
        double ma = 0, mb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            ma += a[i];
            mb += b[i];
        }

        ma /= a.Length;
        mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var da = a[i] - ma;
            var db = b[i] - mb;
            sab += da * db;
            saa += da * da;
            sbb += db * db;
        }

        return saa <= 1e-9 || sbb <= 1e-9 ? 0 : sab / Math.Sqrt(saa * sbb);
    }
}
