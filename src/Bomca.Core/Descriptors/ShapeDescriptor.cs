using Bomca.Core.Imaging;
using Bomca.Core.Model;

namespace Bomca.Core.Descriptors;

/// <summary>
/// Shape comparison for glyph crops. All descriptors operate on square, aspect-preserved masks,
/// so a wide glyph and a narrow glyph cannot score as identical just because both fill the canvas.
/// </summary>
public static class ShapeDescriptor
{
    /// <summary>64-bit difference hash on a 9x8 downsample of the mask (robust to stroke thickness).</summary>
    public static ulong DHash(BoolMask mask)
    {
        const int w = 9, h = 8;
        var small = new double[w * h];
        for (var y = 0; y < h; y++)
        {
            var y0 = y * mask.Height / h;
            var y1 = Math.Max(y0 + 1, (y + 1) * mask.Height / h);
            for (var x = 0; x < w; x++)
            {
                var x0 = x * mask.Width / w;
                var x1 = Math.Max(x0 + 1, (x + 1) * mask.Width / w);
                var sum = 0;
                var n = 0;
                for (var yy = y0; yy < y1 && yy < mask.Height; yy++)
                    for (var xx = x0; xx < x1 && xx < mask.Width; xx++)
                    {
                        if (mask.Data[yy * mask.Width + xx]) sum++;
                        n++;
                    }
                small[y * w + x] = n == 0 ? 0 : (double)sum / n;
            }
        }

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w - 1; x++)
            {
                if (small[y * w + x] > small[y * w + x + 1]) hash |= 1UL << bit;
                bit++;
            }
        return hash;
    }

    public static int Hamming(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);

    /// <summary>Normalised cross-correlation of the coverage-weighted masks.</summary>
    public static double Ncc(BoolMask a, BoolMask b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 0;
        double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
        for (var i = 0; i < a.Data.Length; i++)
        {
            double va = a.Data[i] ? 1 : 0;
            double vb = b.Data[i] ? 1 : 0;
            sa += va; sb += vb; saa += va * va; sbb += vb * vb; sab += va * vb;
        }
        var n = a.Data.Length;
        var cov = saa - sa * sa / n;
        var cov2 = sbb - sb * sb / n;
        var denom = Math.Sqrt(Math.Max(1e-9, cov * cov2));
        return (sab - sa * sb / n) / denom;
    }

    /// <summary>Symmetric mean Chamfer distance in pixels (lower = more similar), mapped to [0,1].</summary>
    public static double ChamferScore(BoolMask a, BoolMask b)
    {
        var da = GrayImage.DistanceTransform(a);
        var db = GrayImage.DistanceTransform(b);
        var w = a.Width;
        double sum = 0;
        var n = 0;
        for (var i = 0; i < a.Data.Length; i++)
        {
            if (a.Data[i]) { sum += db[i]; n++; }
            if (b.Data[i]) { sum += da[i]; n++; }
        }
        if (n == 0) return 0;
        var mean = sum / n;
        var diag = Math.Sqrt(w * w + a.Height * a.Height);
        return Math.Exp(-3.0 * mean / diag);
    }

    /// <summary>Distance between log-scaled Hu moment vectors, mapped to a similarity.</summary>
    public static double MomentScore(double[] a, double[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = Math.Abs(a[i] - b[i]);
            sum += Math.Min(d, 6.0);
        }
        var mean = sum / a.Length;
        return Math.Exp(-0.7 * mean);
    }

    /// <summary>Similarity of cheap scalar features (aspect, density, stroke width, hole/stroke counts).</summary>
    public static double FeatureScore(SymbolOccurrence a, SymbolOccurrence b)
    {
        double score = 1.0;
        score -= Math.Min(0.45, Math.Abs(Math.Log(Math.Max(0.05, a.Aspect) / Math.Max(0.05, b.Aspect))) * 0.35);
        score -= Math.Min(0.25, Math.Abs(a.InkDensity - b.InkDensity) * 0.6);
        score -= Math.Min(0.15, Math.Abs(Math.Log((a.StrokeWidth + 0.4) / (b.StrokeWidth + 0.4))) * 0.25);
        score -= Math.Min(0.10, Math.Abs(a.HoleCount - b.HoleCount) * 0.05);
        score -= Math.Min(0.05, Math.Abs(a.StrokeCount - b.StrokeCount) * 0.02);
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>Rotate a square mask by an arbitrary angle (nearest neighbour) about the canvas centre.</summary>
    public static BoolMask Rotate(BoolMask m, double degrees)
    {
        if (Math.Abs(degrees) < 0.01) return m;
        var rad = degrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var size = m.Width;
        var cx = (size - 1) / 2.0;
        var cy = (size - 1) / 2.0;
        var res = new BoolMask(size, size);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var sx = (int)Math.Round(cos * dx + sin * dy + cx);
                var sy = (int)Math.Round(-sin * dx + cos * dy + cy);
                if (sx < 0 || sy < 0 || sx >= size || sy >= size) continue;
                res.Data[y * size + x] = m.Data[sy * size + sx];
            }
        return res;
    }

    public static BoolMask Mirror(BoolMask m)
    {
        var res = new BoolMask(m.Width, m.Height);
        for (var y = 0; y < m.Height; y++)
            for (var x = 0; x < m.Width; x++)
                res.Data[y * m.Width + (m.Width - 1 - x)] = m.Data[y * m.Width + x];
        return res;
    }

    public static readonly double[] RotationCandidates = { 0, -8, 8, -16, 16 };
}
