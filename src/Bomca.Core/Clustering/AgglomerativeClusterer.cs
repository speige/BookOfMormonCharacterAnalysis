using Bomca.Core.Model;

namespace Bomca.Core.Clustering;

public sealed class Cluster
{
    public string ClusterId { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public double MeanLinkage { get; set; }
}

/// <summary>
/// Average-linkage agglomerative clustering over the scored pair graph. Edges that were never scored
/// count as zero similarity, which keeps the result conservative: a chain of weak look-alikes cannot
/// fuse two real character classes.
/// </summary>
public static class AgglomerativeClusterer
{
    public static (List<Cluster> clusters, Dictionary<string, string> assignment) Clusterize(
        IEnumerable<string> ids, IEnumerable<GlyphPair> pairs, double threshold)
    {
        var idList = ids.Distinct().ToList();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < idList.Count; i++) index[idList[i]] = i;

        var weights = new Dictionary<(int, int), double>();
        foreach (var p in pairs)
        {
            if (p.Breakdown.Combined < threshold) continue;
            if (!index.TryGetValue(p.A, out var a) || !index.TryGetValue(p.B, out var b)) continue;
            var key = a < b ? (a, b) : (b, a);
            weights[key] = Math.Max(weights.TryGetValue(key, out var w) ? w : 0, p.Breakdown.Combined);
        }

        var active = Enumerable.Range(0, idList.Count).ToList();
        var members = active.ToDictionary(i => i, i => new List<int> { i });
        var linkageCache = new Dictionary<(int, int), double>();

        double Linkage(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (linkageCache.TryGetValue(key, out var cached)) return cached;
            double sum = 0;
            var count = 0;
            foreach (var ma in members[a])
            {
                foreach (var mb in members[b])
                {
                    var k = ma < mb ? (ma, mb) : (mb, ma);
                    sum += weights.TryGetValue(k, out var w) ? w : 0;
                    count++;
                }
            }
            var value = count == 0 ? 0 : sum / count;
            linkageCache[key] = value;
            return value;
        }

        while (true)
        {
            double best = 0;
            int bestA = -1, bestB = -1;
            for (var i = 0; i < active.Count; i++)
            {
                for (var j = i + 1; j < active.Count; j++)
                {
                    var a = active[i];
                    var b = active[j];
                    var link = Linkage(a, b);
                    if (link <= best) continue;
                    best = link;
                    bestA = a;
                    bestB = b;
                }
            }

            if (bestA < 0 || best < threshold) break;
            active.Remove(bestB);
            members[bestA].AddRange(members[bestB]);
            members.Remove(bestB);
            linkageCache.Clear();
        }

        var clusters = new List<Cluster>();
        var assignment = new Dictionary<string, string>(StringComparer.Ordinal);
        var ordered = active
            .Select(a => members[a])
            .OrderByDescending(m => m.Count)
            .ThenBy(m => idList[m[0]], StringComparer.Ordinal)
            .ToList();

        for (var ci = 0; ci < ordered.Count; ci++)
        {
            var cid = $"C{ci + 1:000}";
            var cs = new Cluster { ClusterId = cid };
            double sum = 0;
            var n = 0;
            foreach (var m in ordered[ci])
            {
                cs.Members.Add(idList[m]);
                assignment[idList[m]] = cid;
            }
            for (var i = 0; i < ordered[ci].Count; i++)
            {
                for (var j = i + 1; j < ordered[ci].Count; j++)
                {
                    var a = ordered[ci][i];
                    var b = ordered[ci][j];
                    var k = a < b ? (a, b) : (b, a);
                    sum += weights.TryGetValue(k, out var w) ? w : 0;
                    n++;
                }
            }
            cs.MeanLinkage = n == 0 ? 1 : Math.Round(sum / n, 4);
            clusters.Add(cs);
        }

        return (clusters, assignment);
    }
}
