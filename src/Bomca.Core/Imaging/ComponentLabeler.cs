using Bomca.Core.Model;

namespace Bomca.Core.Imaging;

public sealed class Component
{
    public Rect Bounds { get; set; } = new();
    public int Area { get; set; }
    public double Cx { get; set; }
    public double Cy { get; set; }
    public int Label { get; set; }
}

public static class ComponentLabeler
{
    /// <summary>8-connected component labelling with a union-find pass (two-pass, single union-find).</summary>
    public static int[] Label(BoolMask mask, out List<Component> components)
    {
        var w = mask.Width;
        var h = mask.Height;
        var labels = new int[w * h];
        var parent = new List<int> { 0 };

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!mask.Data[y * w + x]) continue;
                var neighbours = new List<int>(4);
                if (x > 0 && labels[y * w + x - 1] > 0) neighbours.Add(labels[y * w + x - 1]);
                if (y > 0)
                {
                    if (labels[(y - 1) * w + x] > 0) neighbours.Add(labels[(y - 1) * w + x]);
                    if (x > 0 && labels[(y - 1) * w + x - 1] > 0) neighbours.Add(labels[(y - 1) * w + x - 1]);
                    if (x < w - 1 && labels[(y - 1) * w + x + 1] > 0) neighbours.Add(labels[(y - 1) * w + x + 1]);
                }

                if (neighbours.Count == 0)
                {
                    parent.Add(parent.Count);
                    labels[y * w + x] = parent.Count - 1;
                }
                else
                {
                    var min = neighbours.Min();
                    labels[y * w + x] = min;
                    foreach (var n in neighbours) Union(min, n);
                }
            }
        }

        // flatten
        var remap = new Dictionary<int, int>();
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] == 0) continue;
            var root = Find(labels[i]);
            if (!remap.TryGetValue(root, out var newLabel))
            {
                newLabel = remap.Count + 1;
                remap[root] = newLabel;
            }
            labels[i] = newLabel;
        }

        components = new List<Component>(remap.Count);
        for (var i = 0; i < remap.Count; i++) components.Add(new Component { Label = i + 1 });
        var minX = new int[remap.Count + 1];
        var minY = new int[remap.Count + 1];
        var maxX = new int[remap.Count + 1];
        var maxY = new int[remap.Count + 1];
        for (var i = 0; i < minX.Length; i++) { minX[i] = w; minY[i] = h; maxX[i] = -1; maxY[i] = -1; }
        var sumX = new double[remap.Count + 1];
        var sumY = new double[remap.Count + 1];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var l = labels[y * w + x];
                if (l == 0) continue;
                var c = components[l - 1];
                c.Area++;
                if (x < minX[l]) minX[l] = x;
                if (y < minY[l]) minY[l] = y;
                if (x > maxX[l]) maxX[l] = x;
                if (y > maxY[l]) maxY[l] = y;
                sumX[l] += x;
                sumY[l] += y;
            }
        }

        for (var i = 1; i <= remap.Count; i++)
        {
            var c = components[i - 1];
            c.Bounds = new Rect(minX[i], minY[i], Math.Max(1, maxX[i] - minX[i] + 1), Math.Max(1, maxY[i] - minY[i] + 1));
            c.Cx = c.Area == 0 ? 0 : sumX[i] / c.Area;
            c.Cy = c.Area == 0 ? 0 : sumY[i] / c.Area;
        }
        return labels;
    }

    /// <summary>Number of background regions fully enclosed by ink (holes), using 4-connectivity on background.</summary>
    public static int CountHoles(BoolMask mask)
    {
        var w = mask.Width;
        var h = mask.Height;
        var bg = new BoolMask(w, h);
        for (var i = 0; i < bg.Data.Length; i++) bg.Data[i] = !mask.Data[i];
        var labels = Label(bg, out var comps);
        var enclosed = 0;
        for (var i = 0; i < comps.Count; i++)
        {
            var c = comps[i];
            // touches border => outer background
            if (c.Bounds.X == 0 || c.Bounds.Y == 0 || c.Bounds.Right >= w || c.Bounds.Bottom >= h) continue;
            if (c.Area == 0) continue;
            enclosed++;
        }
        return enclosed;
    }

    /// <summary>Estimated stroke count: number of ink runs along horizontal scan lines, sampled and aggregated.</summary>
    public static int EstimateStrokeCount(BoolMask mask)
    {
        var w = mask.Width;
        var h = mask.Height;
        if (h < 3) return 0;
        var runs = new List<int>();
        for (var y = 1; y < h - 1; y += 2)
        {
            var count = 0;
            var inRun = false;
            for (var x = 0; x < w; x++)
            {
                var ink = mask.Data[y * w + x];
                if (ink && !inRun) { count++; inRun = true; }
                else if (!ink) inRun = false;
            }
            if (count > 0) runs.Add(count);
        }
        if (runs.Count == 0) return 0;
        runs.Sort();
        return Math.Max(1, runs[runs.Count / 2]);
    }
}
