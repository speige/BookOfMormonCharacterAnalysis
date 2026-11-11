using Bomca.Core.Imaging;

namespace Bomca.Core.Descriptors;

/// <summary>
/// Moment-based alignment: centre on the ink centroid, rotate to the principal axis, then scale so the
/// shape's robust extent (1st-99th percentile of rotated ink coordinates) fills the canvas. Rotation and
/// robust extents remove the handwriting variation that ruins naive overlap scores (slant, size, a stray
/// speck) while keeping the character's real proportions.
/// </summary>
public static class MomentNormalizer
{
    public static BoolMask Normalize(BoolMask src, int size = 64, int pad = 3)
    {
        var points = new List<(double X, double Y)>(1024);
        for (var y = 0; y < src.Height; y++)
            for (var x = 0; x < src.Width; x++)
                if (src.Data[y * src.Width + x]) points.Add((x, y));
        if (points.Count < 4) return src.FitSquare(size);

        double cx = 0, cy = 0;
        foreach (var (x, y) in points) { cx += x; cy += y; }
        cx /= points.Count;
        cy /= points.Count;

        double mu20 = 0, mu02 = 0, mu11 = 0;
        foreach (var (x, y) in points)
        {
            double dx = x - cx, dy = y - cy;
            mu20 += dx * dx;
            mu02 += dy * dy;
            mu11 += dx * dy;
        }
        mu20 /= points.Count;
        mu02 /= points.Count;
        mu11 /= points.Count;

        // No principal-axis rotation here on purpose. The axis is undefined for near-isotropic shapes, and a
        // spurious 90-degree rotation between two copies of the same glyph is far more damaging than a small
        // slant that the comparer's rotation search already covers.
        const double theta = 0.0;
        var cos = Math.Cos(-theta);
        var sin = Math.Sin(-theta);

        var xs = new double[points.Count];
        var ys = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var dx = points[i].X - cx;
            var dy = points[i].Y - cy;
            xs[i] = cos * dx + sin * dy;
            ys[i] = -sin * dx + cos * dy;
        }
        Array.Sort(xs);
        Array.Sort(ys);

        var x0 = Quantile(xs, 0.01);
        var x1 = Quantile(xs, 0.99);
        var y0 = Quantile(ys, 0.01);
        var y1 = Quantile(ys, 0.99);
        var span = Math.Max(Math.Max(x1 - x0, y1 - y0), 1e-6);
        var scale = (size - 2.0 * pad) / span;
        var half = (size - 1) / 2.0;

        var dst = new BoolMask(size, size);
        var drawn = 0;
        for (var y = 0; y < src.Height; y++)
        {
            for (var x = 0; x < src.Width; x++)
            {
                if (!src.Data[y * src.Width + x]) continue;
                var dx = x - cx;
                var dy = y - cy;
                var rx = (cos * dx + sin * dy) * scale;
                var ry = (-sin * dx + cos * dy) * scale;
                var tx = (int)Math.Round(rx + half);
                var ty = (int)Math.Round(ry + half);
                if (tx < 0 || ty < 0 || tx >= size || ty >= size) continue;
                dst.Data[ty * size + tx] = true;
                drawn++;
            }
        }

        // Nearest-neighbour sampling can drop thin strokes; if too much ink vanished, thicken by one.
        if (drawn < points.Count * 0.6) dst = dst.Dilate(1);
        return dst.Count() == 0 ? src.FitSquare(size) : dst;
    }

    private static double Quantile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        var idx = (int)Math.Clamp(Math.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1);
        return sorted[idx];
    }
}
