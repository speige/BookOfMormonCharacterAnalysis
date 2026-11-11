using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Bomca.Core.Imaging;

/// <summary>Single-channel 8-bit-ish float image with the primitives the pipeline needs.</summary>
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }
    public float[] Px { get; }

    public GrayImage(int width, int height)
    {
        Width = width; Height = height; Px = new float[width * height];
    }

    public GrayImage(int width, int height, float[] px)
    {
        Width = width; Height = height; Px = px;
    }

    public float this[int x, int y]
    {
        get => Px[y * Width + x];
        set => Px[y * Width + x] = value;
    }

    public static GrayImage Load(string path, double rotateDegrees = 0)
    {
        using var img = Image.Load<Rgba32>(path);
        if (Math.Abs(rotateDegrees) > 0.01)
        {
            img.Mutate(c => c.Rotate((float)rotateDegrees));
        }
        return FromImage(img);
    }

    public static GrayImage FromImage(Image<Rgba32> img)
    {
        var g = new GrayImage(img.Width, img.Height);
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    g.Px[y * g.Width + x] = 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;
                }
            }
        });
        return g;
    }

    public Image<Rgba32> ToImage()
    {
        var img = new Image<Rgba32>(Width, Height);
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < Width; x++)
                {
                    var b = (byte)Math.Clamp(Px[y * Width + x], 0, 255);
                    row[x] = new Rgba32(b, b, b);
                }
            }
        });
        return img;
    }

    public GrayImage Crop(Model.Rect r)
    {
        var x0 = Math.Clamp(r.X, 0, Width);
        var y0 = Math.Clamp(r.Y, 0, Height);
        var x1 = Math.Clamp(r.Right, 0, Width);
        var y1 = Math.Clamp(r.Bottom, 0, Height);
        var w = Math.Max(1, x1 - x0);
        var h = Math.Max(1, y1 - y0);
        var outImg = new GrayImage(w, h);
        for (var y = 0; y < h; y++)
            Array.Copy(Px, (y0 + y) * Width + x0, outImg.Px, y * w, w);
        return outImg;
    }

    /// <summary>Separable box blur using prefix sums. Radius in pixels.</summary>
    public GrayImage BoxBlur(int radius)
    {
        if (radius < 1) return this;
        var tmp = new float[Px.Length];
        var sum = new double[Math.Max(Width, Height) + 1];

        // horizontal
        for (var y = 0; y < Height; y++)
        {
            var rowOff = y * Width;
            sum[0] = 0;
            for (var x = 0; x < Width; x++) sum[x + 1] = sum[x] + Px[rowOff + x];
            for (var x = 0; x < Width; x++)
            {
                var a = Math.Max(0, x - radius);
                var b = Math.Min(Width, x + radius + 1);
                tmp[rowOff + x] = (float)((sum[b] - sum[a]) / (b - a));
            }
        }

        var res = new float[Px.Length];
        // vertical
        for (var x = 0; x < Width; x++)
        {
            sum[0] = 0;
            for (var y = 0; y < Height; y++) sum[y + 1] = sum[y] + tmp[y * Width + x];
            for (var y = 0; y < Height; y++)
            {
                var a = Math.Max(0, y - radius);
                var b = Math.Min(Height, y + radius + 1);
                res[y * Width + x] = (float)((sum[b] - sum[a]) / (b - a));
            }
        }
        return new GrayImage(Width, Height, res);
    }

    /// <summary>Divide out low-frequency illumination. Output is scaled so the paper sits near the input mean.</summary>
    public GrayImage FlattenIllumination(int radius)
    {
        var bg = BoxBlur(radius);
        var target = bg.Median();
        var res = new float[Px.Length];
        for (var i = 0; i < Px.Length; i++)
        {
            var d = bg.Px[i] < 1f ? 1f : bg.Px[i];
            res[i] = Math.Clamp(Px[i] * (float)(target / d), 0f, 255f);
        }
        return new GrayImage(Width, Height, res);
    }

    public double Mean()
    {
        double s = 0;
        foreach (var v in Px) s += v;
        return s / Px.Length;
    }

    public double Median() => Percentile(0.5);


    public double Percentile(double p)
    {
        var hist = new int[256];
        foreach (var v in Px) hist[Math.Clamp((int)v, 0, 255)]++;
        var target = (long)(p * Px.Length);
        long acc = 0;
        for (var i = 0; i < 256; i++)
        {
            acc += hist[i];
            if (acc >= target) return i;
        }
        return 255;
    }

    /// <summary>Otsu's method on the 8-bit histogram.</summary>
    public double Otsu()
    {
        var hist = new long[256];
        foreach (var v in Px) hist[Math.Clamp((int)v, 0, 255)]++;
        var total = Px.Length;
        double sumAll = 0;
        for (var i = 0; i < 256; i++) sumAll += i * (double)hist[i];
        double sumB = 0, wB = 0, best = 0;
        var bestThr = 128.0;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            var wF = total - wB;
            if (wF == 0) break;
            sumB += t * (double)hist[t];
            var mB = sumB / wB;
            var mF = (sumAll - sumB) / wF;
            var between = wB * wF * (mB - mF) * (mB - mF);
            if (between > best) { best = between; bestThr = t; }
        }
        return bestThr;
    }

    /// <summary>Sauvola local threshold (robust to paper texture and ink fading).</summary>
    public BoolMask Sauvola(int window, double k = 0.2, double dynamicRange = 128.0)
    {
        var radius = Math.Max(1, window / 2);
        var meanImg = BoxBlur(radius);
        var sq = new GrayImage(Width, Height);
        for (var i = 0; i < Px.Length; i++) sq.Px[i] = Px[i] * Px[i];
        var meanSq = sq.BoxBlur(radius);

        var mask = new BoolMask(Width, Height);
        for (var i = 0; i < Px.Length; i++)
        {
            var m = meanImg.Px[i];
            var v = Math.Max(0, meanSq.Px[i] - m * m);
            var sigma = (float)Math.Sqrt(v);
            var t = m * (1 + k * (sigma / dynamicRange - 1));
            mask.Data[i] = Px[i] < t;
        }
        return mask;
    }

    /// <summary>Chamfer 3-4 distance transform of the ink pixels in a mask.</summary>
    public static float[] DistanceTransform(BoolMask mask)
    {
        var w = mask.Width;
        var h = mask.Height;
        var d = new float[w * h];
        const float big = 1e9f;
        for (var i = 0; i < d.Length; i++) d[i] = mask.Data[i] ? big : 0f;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (d[i] == 0f) continue;
                var best = d[i];
                if (x > 0) best = Math.Min(best, d[i - 1] + 3);
                if (y > 0) best = Math.Min(best, d[i - w] + 3);
                if (x > 0 && y > 0) best = Math.Min(best, d[i - w - 1] + 4);
                if (x < w - 1 && y > 0) best = Math.Min(best, d[i - w + 1] + 4);
                d[i] = best;
            }
        }
        for (var y = h - 1; y >= 0; y--)
        {
            for (var x = w - 1; x >= 0; x--)
            {
                var i = y * w + x;
                if (d[i] == 0f) continue;
                var best = d[i];
                if (x < w - 1) best = Math.Min(best, d[i + 1] + 3);
                if (y < h - 1) best = Math.Min(best, d[i + w] + 3);
                if (x < w - 1 && y < h - 1) best = Math.Min(best, d[i + w + 1] + 4);
                if (x > 0 && y < h - 1) best = Math.Min(best, d[i + w - 1] + 4);
                d[i] = best;
            }
        }
        for (var i = 0; i < d.Length; i++) d[i] /= 3f;
        return d;
    }

    /// <summary>Bilinear resize.</summary>
    public GrayImage Resize(int newW, int newH)
    {
        var res = new GrayImage(newW, newH);
        var sx = (double)Width / newW;
        var sy = (double)Height / newH;
        for (var y = 0; y < newH; y++)
        {
            var fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, Height - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(Height - 1, y0 + 1);
            var wy = fy - y0;
            for (var x = 0; x < newW; x++)
            {
                var fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, Width - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(Width - 1, x0 + 1);
                var wx = fx - x0;
                var v00 = Px[y0 * Width + x0];
                var v01 = Px[y0 * Width + x1];
                var v10 = Px[y1 * Width + x0];
                var v11 = Px[y1 * Width + x1];
                res.Px[y * newW + x] = (float)((v00 * (1 - wx) + v01 * wx) * (1 - wy) + (v10 * (1 - wx) + v11 * wx) * wy);
            }
        }
        return res;
    }

    /// <summary>Scale so the longest side equals <paramref name="maxSide"/>, preserving aspect. Does not upscale.</summary>
    public GrayImage FitLongest(int maxSide)
    {
        var longest = Math.Max(Width, Height);
        if (longest <= maxSide) return this;
        var s = (double)maxSide / longest;
        return Resize(Math.Max(1, (int)Math.Round(Width * s)), Math.Max(1, (int)Math.Round(Height * s)));
    }

    /// <summary>Map a sub-image into a fixed square canvas: aspect preserved by fitting to the canvas edge, then centred.</summary>
    public GrayImage FitSquare(int size)
    {
        var s = Math.Min((double)size / Width, (double)size / Height);
        var w = Math.Max(1, (int)Math.Round(Width * s));
        var h = Math.Max(1, (int)Math.Round(Height * s));
        var scaled = Resize(w, h);
        var canvas = new GrayImage(size, size);
        var ox = (size - w) / 2;
        var oy = (size - h) / 2;
        for (var y = 0; y < h; y++)
            Array.Copy(scaled.Px, y * w, canvas.Px, (oy + y) * size + ox, w);
        return canvas;
    }

    public GrayImage ToContrast(float low = 0f, float high = 255f)
    {
        var res = new float[Px.Length];
        var span = Math.Max(1e-6f, high - low);
        for (var i = 0; i < Px.Length; i++)
            res[i] = Math.Clamp((Px[i] - low) / span * 255f, 0, 255);
        return new GrayImage(Width, Height, res);
    }
}

/// <summary>Binary mask over an image region.</summary>
public sealed class BoolMask
{
    public int Width { get; }
    public int Height { get; }
    public bool[] Data { get; }

    public BoolMask(int width, int height)
    {
        Width = width; Height = height; Data = new bool[width * height];
    }

    public BoolMask(int width, int height, bool[] data)
    {
        Width = width; Height = height; Data = data;
    }

    public bool this[int x, int y]
    {
        get => Data[y * Width + x];
        set => Data[y * Width + x] = value;
    }

    public int Count()
    {
        var n = 0;
        foreach (var b in Data) if (b) n++;
        return n;
    }

    public static BoolMask FromGray(GrayImage g, double threshold)
    {
        var m = new BoolMask(g.Width, g.Height);
        for (var i = 0; i < g.Px.Length; i++) m.Data[i] = g.Px[i] < threshold;
        return m;
    }

    public BoolMask Crop(Model.Rect r)
    {
        var x0 = Math.Clamp(r.X, 0, Width);
        var y0 = Math.Clamp(r.Y, 0, Height);
        var x1 = Math.Clamp(r.Right, 0, Width);
        var y1 = Math.Clamp(r.Bottom, 0, Height);
        var w = Math.Max(1, x1 - x0);
        var h = Math.Max(1, y1 - y0);
        var m = new BoolMask(w, h);
        for (var y = 0; y < h; y++)
            Array.Copy(Data, (y0 + y) * Width + x0, m.Data, y * w, w);
        return m;
    }

    /// <summary>Copy the mask into a fixed square canvas (aspect fit + centre), nearest-neighbour.</summary>
    public BoolMask FitSquare(int size)
    {
        var s = Math.Min((double)size / Width, (double)size / Height);
        var w = Math.Max(1, (int)Math.Round(Width * s));
        var h = Math.Max(1, (int)Math.Round(Height * s));
        var ox = (size - w) / 2;
        var oy = (size - h) / 2;
        var m = new BoolMask(size, size);
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Min(Height - 1, (int)(y / s));
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Min(Width - 1, (int)(x / s));
                m.Data[(oy + y) * size + ox + x] = Data[sy * Width + sx];
            }
        }
        return m;
    }

    public double Iou(BoolMask other)
    {
        if (other.Width != Width || other.Height != Height) return 0;
        int inter = 0, union = 0;
        for (var i = 0; i < Data.Length; i++)
        {
            var a = Data[i];
            var b = other.Data[i];
            if (a || b) union++;
            if (a && b) inter++;
        }
        return union == 0 ? 0 : (double)inter / union;
    }

    public BoolMask Dilate(int radius)
    {
        if (radius <= 0) return this;
        var horiz = new bool[Data.Length];
        for (var y = 0; y < Height; y++)
        {
            var off = y * Width;
            for (var x = 0; x < Width; x++)
            {
                if (!Data[off + x]) continue;
                var a = Math.Max(0, x - radius);
                var b = Math.Min(Width - 1, x + radius);
                for (var k = a; k <= b; k++) horiz[off + k] = true;
            }
        }
        var res = new bool[Data.Length];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!horiz[y * Width + x]) continue;
                var a = Math.Max(0, y - radius);
                var b = Math.Min(Height - 1, y + radius);
                for (var k = a; k <= b; k++) res[k * Width + x] = true;
            }
        }
        return new BoolMask(Width, Height, res);
    }

    public BoolMask Erode(int radius) => Invert().Dilate(radius).Invert();

    public BoolMask Invert()
    {
        var res = new bool[Data.Length];
        for (var i = 0; i < Data.Length; i++) res[i] = !Data[i];
        return new BoolMask(Width, Height, res);
    }

    /// <summary>Remove components smaller than minArea (in place semantics: returns new mask).</summary>
    public BoolMask RemoveSmallComponents(int minArea)
    {
        var labels = ComponentLabeler.Label(this, out var comps);
        var keep = new BoolMask(Width, Height);
        for (var i = 0; i < Data.Length; i++)
        {
            var l = labels[i];
            if (l > 0 && comps[l - 1].Area >= minArea) keep.Data[i] = true;
        }
        return keep;
    }
}
