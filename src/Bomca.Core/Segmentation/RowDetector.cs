using Bomca.Core.Imaging;
using Bomca.Core.Model;

namespace Bomca.Core.Segmentation;

public static class RowDetector
{
    /// <summary>
    /// Group connected components into text rows by finding valleys in a smoothed histogram of
    /// component centres. Valleys are far more reliable than peaks for handwritten pages whose rows
    /// are close together: a row is a broad hump, so peak picking tends to split one row into two.
    /// </summary>
    public static List<(int y0, int y1, string kind)> Detect(List<Component> comps, SegmentationParams p)
    {
        var result = new List<(int, int, string)>();
        if (comps.Count == 0) return result;

        var minY = comps.Min(c => (int)Math.Floor(c.Cy));
        var maxY = comps.Max(c => (int)Math.Ceiling(c.Cy));
        var bin = Math.Max(1, p.RowBinPx);
        var nBins = (maxY - minY) / bin + 2;
        var hist = new double[nBins];
        foreach (var c in comps)
        {
            var b = (int)((c.Cy - minY) / bin);
            if (b >= 0 && b < nBins) hist[b] += Math.Max(1, c.Area);
        }

        var smooth = Smooth(hist, 6);
        var maxVal = smooth.Max();
        if (maxVal <= 0) return result;

        var minDistBins = Math.Max(1, p.RowMinPeakDistancePx / bin / 2);

        var valleys = new List<int>();
        for (var i = 1; i < nBins - 1; i++)
        {
            if (smooth[i] > maxVal * 0.22) continue;
            var isMin = true;
            for (var k = Math.Max(0, i - minDistBins); k <= Math.Min(nBins - 1, i + minDistBins); k++)
            {
                if (smooth[k] < smooth[i]) { isMin = false; break; }
            }
            if (isMin) valleys.Add(i);
        }

        var boundaries = new List<double>();
        var group = new List<int>();
        foreach (var v in valleys)
        {
            if (group.Count > 0 && v - group[^1] > minDistBins)
            {
                boundaries.Add(Pick(group, smooth));
                group.Clear();
            }
            group.Add(v);
        }
        if (group.Count > 0) boundaries.Add(Pick(group, smooth));
        boundaries.Sort();

        var edges = new List<double> { double.NegativeInfinity };
        edges.AddRange(boundaries.Select(b => (double)(minY + b * bin)));
        edges.Add(double.PositiveInfinity);

        var bands = new List<(int y0, int y1, int count, int top, int bottom)>();
        for (var i = 0; i < edges.Count - 1; i++)
        {
            var y0 = edges[i];
            var y1 = edges[i + 1];
            var inBand = comps.Where(c => c.Cy >= y0 && c.Cy < y1).ToList();
            if (inBand.Count == 0) continue;
            bands.Add(((int)Math.Max(0, y0), (int)y1, inBand.Count,
                inBand.Min(c => c.Bounds.Y), inBand.Max(c => c.Bounds.Bottom)));
        }

        if (bands.Count == 0) return result;

        // Merge runt bands (usually dust or a fragment split off a real row) into the nearest neighbour.
        var maxCount = bands.Max(b => b.count);
        var minCount = Math.Max(2, (int)(maxCount * 0.15));
        var merged = new List<(int y0, int y1, int count, int top, int bottom)>();
        foreach (var b in bands)
        {
            if (b.count < minCount && merged.Count > 0 && merged[^1].count >= b.count)
            {
                var prev = merged[^1];
                merged[^1] = (prev.y0, b.y1, prev.count + b.count, Math.Min(prev.top, b.top), Math.Max(prev.bottom, b.bottom));
            }
            else
            {
                merged.Add(b);
            }
        }

        // Drop a sparse band at the bottom of the page (paper-edge shadow, torn edge).
        if (merged.Count >= 3)
        {
            var counts = merged.Select(b => b.count).OrderBy(c => c).ToList();
            var medianCount = counts[counts.Count / 2];
            var heights = merged.Select(b => (double)(b.bottom - b.top)).OrderBy(h => h).ToList();
            var medianHeight = heights[heights.Count / 2];

            bool IsArtifact((int y0, int y1, int count, int top, int bottom) b)
                => b.count < medianCount * 0.4 || (b.bottom - b.top) < medianHeight * 0.5;

            while (merged.Count > 3 && IsArtifact(merged[^1])) merged.RemoveAt(merged.Count - 1);
        }

        if (Environment.GetEnvironmentVariable("BOMCA_DEBUG") == "1")
        {
            var dbgPath = Path.Combine(Environment.GetEnvironmentVariable("BOMCA_DEBUG_DIR") ?? ".", "row-bands.csv");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbgPath))!);
                var lines = new List<string> { "y0,y1,count,top,bottom" };
                lines.AddRange(merged.Select(m => $"{m.y0},{m.y1},{m.count},{m.top},{m.bottom}"));
                File.WriteAllLines(dbgPath, lines);
            }
            catch { /* diagnostics are best-effort */ }
        }

        // Bands must partition the page: use the valley edges, clamped to the real ink extent,
        // otherwise neighbouring rows overlap and glyphs get assigned to two rows at once.
        for (var i = 0; i < merged.Count; i++)
        {
            var b = merged[i];
            var y0 = i == 0 ? b.top : Math.Max(b.top, b.y0);
            var y1 = i == merged.Count - 1 ? b.bottom : Math.Min(b.bottom, b.y1);
            if (y1 <= y0) y1 = b.bottom;
            result.Add((y0, y1, "text"));
        }
        return result;
    }

    private static double Pick(List<int> group, double[] smooth)
    {
        var best = group[0];
        foreach (var g in group)
            if (smooth[g] < smooth[best]) best = g;
        return best;
    }

    private static double[] Smooth(double[] values, int radius)
    {
        var res = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            double s = 0;
            var n = 0;
            for (var k = Math.Max(0, i - radius); k <= Math.Min(values.Length - 1, i + radius); k++) { s += values[k]; n++; }
            res[i] = s / n;
        }
        return res;
    }
}

/// <summary>
/// Builds glyphs out of components. Strokes of one character are fused with a small morphological
/// closing before grouping, which is far more stable than pairwise gap heuristics for connected
/// handwriting; bounding boxes are then recomputed from the *original* ink so crops stay tight.
/// </summary>
public static class GlyphGrouper
{
    public static List<GroupedGlyph> Group(List<Component> comps, SegmentationParams p)
    {
        if (comps.Count == 0) return new List<GroupedGlyph>();

        var heights = comps.Select(c => (double)c.Bounds.H).OrderBy(h => h).ToList();
        var medianH = heights[heights.Count / 2];
        var radius = (int)Math.Clamp(Math.Round(medianH * 0.05), 2, 8);

        var minX = comps.Min(c => c.Bounds.X);
        var minY = comps.Min(c => c.Bounds.Y);
        var maxX = comps.Max(c => c.Bounds.Right);
        var maxY = comps.Max(c => c.Bounds.Bottom);
        var w = maxX - minX + 2 * radius + 2;
        var h = maxY - minY + 2 * radius + 2;

        var bandMask = new BoolMask(w, h);
        foreach (var c in comps)
        {
            for (var y = c.Bounds.Y; y < c.Bounds.Bottom; y++)
            {
                for (var x = c.Bounds.X; x < c.Bounds.Right; x++)
                {
                    var yy = y - minY + radius;
                    var xx = x - minX + radius;
                    if (yy < 0 || xx < 0 || yy >= h || xx >= w) continue;
                    bandMask.Data[yy * w + xx] = true;
                }
            }
        }

        var fused = bandMask.Dilate(radius);
        var labels = ComponentLabeler.Label(fused, out _);

        // Assign each original component to the label under its bounding-box majority.
        var groups = new Dictionary<int, List<Component>>();
        foreach (var c in comps)
        {
            var counts = new Dictionary<int, int>();
            for (var y = c.Bounds.Y; y < c.Bounds.Bottom; y += 2)
            {
                for (var x = c.Bounds.X; x < c.Bounds.Right; x += 2)
                {
                    var yy = y - minY + radius;
                    var xx = x - minX + radius;
                    if (yy < 0 || xx < 0 || yy >= h || xx >= w) continue;
                    var l = labels[yy * w + xx];
                    if (l == 0) continue;
                    counts[l] = counts.TryGetValue(l, out var n) ? n + 1 : 1;
                }
            }
            var label = counts.Count == 0 ? -c.Label : counts.OrderByDescending(kv => kv.Value).First().Key;
            if (!groups.TryGetValue(label, out var list)) groups[label] = list = new List<Component>();
            list.Add(c);
        }

        var glyphs = new List<GroupedGlyph>();
        foreach (var (_, members) in groups)
        {
            var x0 = members.Min(m => m.Bounds.X);
            var y0 = members.Min(m => m.Bounds.Y);
            var x1 = members.Max(m => m.Bounds.Right);
            var y1 = members.Max(m => m.Bounds.Bottom);
            var area = members.Sum(m => m.Area);
            var conf = members.Count == 1 ? 1.0 : 1.0 / (1.0 + 0.12 * (members.Count - 1));
            glyphs.Add(new GroupedGlyph
            {
                Bounds = new Rect(x0, y0, x1 - x0, y1 - y0),
                Area = area,
                ComponentCount = members.Count,
                Confidence = Math.Round(conf, 3),
            });
        }

        // Split implausibly wide or tall groups: a stray connecting mark must not weld two characters.
        var final = new List<GroupedGlyph>();
        foreach (var g in glyphs.OrderBy(g => g.Bounds.X))
        {
            var maxW = Math.Max(medianH * 3.0, 34);
            var maxH = Math.Max(medianH * 3.2, 34);
            var parts = Math.Max(1, (int)Math.Ceiling(g.Bounds.W / maxW));
            if (g.Bounds.W <= maxW && g.Bounds.H <= maxH)
            {
                final.Add(g);
                continue;
            }

            if (g.Bounds.W > maxW)
            {
                var partW = (double)g.Bounds.W / parts;
                for (var k = 0; k < parts; k++)
                {
                    var x0 = g.Bounds.X + (int)Math.Round(k * partW);
                    var x1 = k == parts - 1 ? g.Bounds.Right : g.Bounds.X + (int)Math.Round((k + 1) * partW);
                    final.Add(new GroupedGlyph
                    {
                        Bounds = new Rect(x0, g.Bounds.Y, Math.Max(1, x1 - x0), g.Bounds.H),
                        Area = g.Area / parts,
                        ComponentCount = g.ComponentCount,
                        Confidence = 0.6,
                    });
                }
            }
            else
            {
                final.Add(new GroupedGlyph
                {
                    Bounds = g.Bounds,
                    Area = g.Area,
                    ComponentCount = g.ComponentCount,
                    Confidence = 0.6,
                });
            }
        }
        return final;
    }
}
