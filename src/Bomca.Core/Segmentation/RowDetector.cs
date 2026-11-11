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

        // Candidate boundaries: local minima that are clearly empty relative to the busiest row.
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

        // Collapse adjacent valley indices to the deepest one in each run.
        var boundaries = new List<double>();
        var group = new List<int>();
        foreach (var v in valleys)
        {
            if (group.Count > 0 && v - group[^1] > minDistBins) { boundaries.Add(Pick(group, smooth)); group.Clear(); }
            group.Add(v);
        }
        if (group.Count > 0) boundaries.Add(Pick(group, smooth));

        boundaries.Sort();

        // Boundaries are histogram bin indices; convert to page pixels before grouping.
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

        // Merge runt bands (typically dots or a fragment split off a real row) into the nearest neighbour.
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
            else if (b.count < minCount && merged.Count == 0)
            {
                merged.Add(b);
            }
            else
            {
                merged.Add(b);
            }
        }

        // Drop faint/sparse bands at the top or bottom of the page (paper edge shadow, torn edge).
        if (merged.Count >= 3)
        {
            var counts = merged.Select(b => b.count).OrderBy(c => c).ToList();
            var medianCount = counts[counts.Count / 2];
            var heights = merged.Select(b => (double)(b.bottom - b.top)).OrderBy(h => h).ToList();
            var medianHeight = heights[heights.Count / 2];

            bool IsArtifact((int y0, int y1, int count, int top, int bottom) b)
                => b.count < medianCount * 0.4 || (b.bottom - b.top) < medianHeight * 0.5;

            while (merged.Count > 3 && IsArtifact(merged[^1])) merged.RemoveAt(merged.Count - 1);
            while (merged.Count > 3 && IsArtifact(merged[0])) merged.RemoveAt(0);
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
            if (y1 <= y0) { y1 = b.bottom; }
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

public static class GlyphGrouper
{
    /// <summary>
    /// Merge components that belong to the same glyph: small horizontal gap, vertical overlap,
    /// and a width cap so that two adjacent glyphs never merge across a real word gap.
    /// </summary>
    public static List<GroupedGlyph> Group(List<Component> comps, SegmentationParams p)
    {
        if (comps.Count == 0) return new List<GroupedGlyph>();

        var heights = comps.Select(c => (double)c.Bounds.H).OrderBy(h => h).ToList();
        var medianH = heights[heights.Count / 2];
        var gapLimit = Math.Min(p.MergeGapMaxPx, Math.Max(2.0, medianH * p.MergeGapRatio));

        var n = comps.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        var order = Enumerable.Range(0, n).OrderBy(i => comps[i].Bounds.X).ToList();
        var merges = 0;
        for (var oi = 0; oi < order.Count; oi++)
        {
            var i = order[oi];
            for (var oj = oi + 1; oj < order.Count; oj++)
            {
                var j = order[oj];
                if (comps[j].Bounds.X > comps[i].Bounds.Right + gapLimit) break;
                var a = comps[i].Bounds;
                var b = comps[j].Bounds;
                var gap = b.X > a.Right ? b.X - a.Right : (a.X > b.Right ? a.X - b.Right : 0);
                if (gap > gapLimit) continue;
                var vOverlap = a.VOverlap(b);
                var minH = Math.Min(a.H, b.H);
                if (minH == 0) continue;
                if (vOverlap / (double)minH < p.MergeVOverlapRatio) continue;
                // Size caps: refuse to build glyphs wider or taller than a plausible character.
                var mergedW = Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X);
                if (mergedW > Math.Max(medianH * 2.8, 24)) continue;
                var mergedH = Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y);
                if (mergedH > Math.Max(medianH * 3.0, 24)) continue;
                if (Find(i) != Find(j)) { Union(i, j); merges++; }
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list)) groups[root] = list = new List<int>();
            list.Add(i);
        }

        var result = new List<GroupedGlyph>();
        foreach (var (_, members) in groups)
        {
            var x0 = members.Min(m => comps[m].Bounds.X);
            var y0 = members.Min(m => comps[m].Bounds.Y);
            var x1 = members.Max(m => comps[m].Bounds.Right);
            var y1 = members.Max(m => comps[m].Bounds.Bottom);
            var bounds = new Rect(x0, y0, x1 - x0, y1 - y0);
            var area = members.Sum(m => comps[m].Area);
            var conf = members.Count == 1 ? 1.0 : 1.0 / (1.0 + 0.12 * (members.Count - 1));
            result.Add(new GroupedGlyph
            {
                Bounds = bounds,
                Area = area,
                ComponentCount = members.Count,
                Confidence = Math.Round(conf, 3),
            });
        }

        // Split groups whose width is implausible (likely two glyphs glued by a stray stroke).
        var final = new List<GroupedGlyph>();
        foreach (var g in result.OrderBy(g => g.Bounds.X))
        {
            var maxW = Math.Max(medianH * 3.4, 30);
            if (g.Bounds.W <= maxW || g.ComponentCount <= 1)
            {
                final.Add(g);
                continue;
            }
            var ratio = (int)Math.Ceiling(g.Bounds.W / maxW);
            var partW = (double)g.Bounds.W / ratio;
            for (var k = 0; k < ratio; k++)
            {
                var x0 = g.Bounds.X + (int)Math.Round(k * partW);
                var x1 = k == ratio - 1 ? g.Bounds.Right : g.Bounds.X + (int)Math.Round((k + 1) * partW);
                final.Add(new GroupedGlyph
                {
                    Bounds = new Rect(x0, g.Bounds.Y, Math.Max(1, x1 - x0), g.Bounds.H),
                    Area = g.Area / ratio,
                    ComponentCount = g.ComponentCount,
                    Confidence = 0.6,
                });
            }
        }
        return final;
    }
}
