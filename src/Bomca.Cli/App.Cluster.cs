using Bomca.Core.Clustering;
using Bomca.Core.Documents;
using Bomca.Core.Json;
using Bomca.Core.Model;
using Bomca.Core.Reporting;

namespace Bomca.Cli;

public static partial class App
{
    private static readonly double[] DefaultThresholds = { 0.60, 0.70, 0.75, 0.80, 0.85, 0.90 };

    private static (DocumentRecord doc, string dir) LoadDocument(string code)
    {
        var dir = Path.Combine(OutRoot, code);
        var path = Path.Combine(dir, "document.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"no extraction found for '{code}'. Run: bomca extract --doc {code}");
        return (JsonIo.Read<DocumentRecord>(path), dir);
    }

    public static int RunCluster(Args args)
    {
        var codes = args.GetList("doc");
        if (codes.Count == 0) codes = new List<string> { "W" };
        var threshold = args.GetDouble("threshold", 0.80);

        foreach (var code in codes)
        {
            var (doc, dir) = LoadDocument(code);
            var symbols = doc.Symbols.Where(s => s.Note != "title-row").ToList();
            if (symbols.Count == 0)
            {
                Console.WriteLine($"cluster {code}: no glyphs; skipping");
                continue;
            }

            var comparer = new GlyphComparer();
            var options = new ClusterOptions
            {
                TopK = args.GetInt("top-k", 24),
                RotationVariants = args.GetInt("rotations", 3),
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var views = comparer.Load(symbols, dir, Console.WriteLine);
            var pairs = comparer.RankPairs(views, options);
            sw.Stop();

            var record = new ClusterRecord
            {
                Doc = code,
                GeneratedUtc = DateTime.UtcNow.ToString("o"),
                Threshold = threshold,
                Method = "iou+ncc+chamfer+hu+features; prefilter by pHash/feature, avg-linkage clustering",
                Pairs = pairs,
                CandidatePairCount = pairs.Count,
            };
            JsonIo.Write(Path.Combine(dir, "pairs.json"), record);

            var (clusters, assignment) = AgglomerativeClusterer.Clusterize(symbols.Select(s => s.Id), pairs, threshold);
            JsonIo.Write(Path.Combine(dir, "clusters.json"), new
            {
                doc = code,
                threshold,
                clusterCount = clusters.Count,
                singletonCount = clusters.Count(c => c.Members.Count == 1),
                clusters,
            });

            if (args.Has("llm"))
            {
                var adjudicated = LlmAdjudication.AdjudicateAsync(comparer, views, pairs, args, Console.WriteLine).GetAwaiter().GetResult();
                if (adjudicated > 0) JsonIo.Write(Path.Combine(dir, "pairs.json"), record);
            }

            var reviewPath = Path.Combine(dir, "review.html");
            ReviewSheetBuilder.WriteGlyphPairs(reviewPath, dir, code, symbols, pairs, threshold);

            var strong = pairs.Count(p => p.Breakdown.Combined >= threshold);
            Console.WriteLine($"cluster {code}: {views.Count} glyphs, {pairs.Count} scored pairs ({strong} >= {threshold:0.00}), {clusters.Count} classes ({clusters.Count(c => c.Members.Count == 1)} singletons) in {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"  wrote {Path.Combine(dir, "pairs.json")}");
            Console.WriteLine($"  wrote {reviewPath}  <-- manual review");
        }
        return 0;
    }

    private static int RunReview(Args args)
    {
        var codes = args.GetList("doc");
        if (codes.Count == 0) codes = new List<string> { "W" };
        foreach (var code in codes)
        {
            var (doc, dir) = LoadDocument(code);
            var pairsPath = Path.Combine(dir, "pairs.json");
            if (!File.Exists(pairsPath))
            {
                Console.Error.WriteLine($"review {code}: run 'bomca cluster --doc {code}' first");
                continue;
            }
            var record = JsonIo.Read<ClusterRecord>(pairsPath);
            var symbols = doc.Symbols.Where(s => s.Note != "title-row").ToList();
            var threshold = args.GetDouble("threshold", 0.80);
            var path = Path.Combine(dir, "review.html");
            ReviewSheetBuilder.WriteGlyphPairs(path, dir, code, symbols, record.Pairs, threshold);
            Console.WriteLine($"review {code}: {path} ({record.Pairs.Count} pairs)");
        }
        return 0;
    }

    public static int RunInventory(Args args)
    {
        var codes = args.GetList("doc");
        if (codes.Count == 0) codes = new List<string> { "W" };
        var thresholds = args.GetDoubleList("thresholds");
        if (thresholds.Count == 0) thresholds = DefaultThresholds.ToList();
        var rerunCluster = args.Has("recluster");

        foreach (var code in codes)
        {
            var (doc, dir) = LoadDocument(code);
            var symbols = doc.Symbols.Where(s => s.Note != "title-row").ToList();
            var pairsPath = Path.Combine(dir, "pairs.json");
            if (rerunCluster || !File.Exists(pairsPath))
            {
                RunCluster(new ArgsBuilder().Verb("cluster").Doc(code).Build());
            }
            var record = JsonIo.Read<ClusterRecord>(pairsPath);
            var autoThreshold = AutoThreshold(record.Pairs);
            thresholds = thresholds.Select(t => Math.Round(t, 2)).Distinct().OrderBy(t => t).ToList();
            if (!thresholds.Contains(autoThreshold)) thresholds = thresholds.Append(autoThreshold).OrderBy(t => t).ToList();
            Console.WriteLine($"inventory {code}: auto threshold from pair-score distribution = {autoThreshold:0.00}");

            var versions = new List<object>();
            foreach (var t in thresholds.OrderBy(v => v))
            {
                var (clusters, assignment) = AgglomerativeClusterer.Clusterize(symbols.Select(s => s.Id), record.Pairs, t);
                var inv = new InventoryVersion
                {
                    Doc = code,
                    Threshold = t,
                    GeneratedUtc = DateTime.UtcNow.ToString("o"),
                    ClassCount = clusters.Count,
                    SingletonCount = clusters.Count(c => c.Members.Count == 1),
                    ExpectedRandomPairRate = JsonIo.Round(EstimateRandomPairRate(symbols, clusters, record.Pairs, t), 4),
                };
                foreach (var c in clusters)
                {
                    var members = c.Members.Select(id => symbols.First(s => s.Id == id)).ToList();
                    inv.Classes.Add(new InventoryClass
                    {
                        ClassId = c.ClusterId,
                        Label = $"{code}-{c.ClusterId}",
                        OccurrenceCount = members.Count,
                        Members = c.Members,
                        MeanIntraScore = c.MeanLinkage,
                        MedianGlyphHeight = Median(members.Select(m => (double)m.H).ToList()),
                        Rows = members.Select(m => $"L{m.Row + 1:00}").Distinct().OrderBy(r => r).ToList(),
                    });
                }

                var path = Path.Combine(dir, $"inventory-{code}-t{(int)Math.Round(t * 100):000}.json");
                JsonIo.Write(path, inv);
                versions.Add(new
                {
                    threshold = t,
                    autoThreshold,
                    classes = inv.ClassCount,
                    singletons = inv.SingletonCount,
                    file = Path.GetFileName(path),
                });
                Console.WriteLine($"inventory {code} t={t:0.00}: {inv.ClassCount} classes ({inv.SingletonCount} singletons, {inv.OccurrenceCount()} glyphs) -> {Path.GetFileName(path)}");
            }

            // Label every occurrence with the class from the primary (closest to 0.80) version.
            var primary = autoThreshold;
            var primaryInv = JsonIo.Read<InventoryVersion>(Path.Combine(dir, $"inventory-{code}-t{(int)Math.Round(primary * 100):000}.json"));
            var lookup = primaryInv.Classes.SelectMany(c => c.Members.Select(m => (m, c.ClassId)))
                .ToDictionary(x => x.m, x => x.ClassId, StringComparer.Ordinal);
            foreach (var s in doc.Symbols)
                if (lookup.TryGetValue(s.Id, out var cls)) s.ClassId = cls;
            JsonIo.Write(Path.Combine(dir, "document.json"), doc);

            // Flat label table: occurrence -> class + crop path (what the downstream matcher consumes).
            var labels = doc.Symbols.Where(s => s.Note != "title-row")
                .OrderBy(s => s.GlobalIndex)
                .Select(s => new { id = s.Id, cls = s.ClassId, row = s.Row, s.GlobalIndex, s.X, s.Y, s.W, s.H, crop = s.CropPath, norm = s.NormalizedPath });
            JsonIo.Write(Path.Combine(dir, $"labels-{code}-t{(int)Math.Round(primary * 100):000}.json"), labels);

            JsonIo.Write(Path.Combine(dir, "inventory-versions.json"), versions);
        }
        return 0;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>
    /// Share of pairs inside the produced classes that were never scored above threshold, i.e. how much of
    /// the clustering rests on transitive chaining rather than direct evidence.
    /// </summary>
    private static double EstimateRandomPairRate(List<SymbolOccurrence> symbols, List<Cluster> clusters, List<GlyphPair> pairs, double threshold)
    {
        var scored = new HashSet<(string, string)>();
        foreach (var p in pairs)
        {
            if (p.Breakdown.Combined < threshold) continue;
            var key = string.CompareOrdinal(p.A, p.B) < 0 ? (p.A, p.B) : (p.B, p.A);
            scored.Add(key);
        }
        double unchained = 0, total = 0;
        foreach (var c in clusters.Where(c => c.Members.Count > 1))
        {
            for (var i = 0; i < c.Members.Count; i++)
                for (var j = i + 1; j < c.Members.Count; j++)
                {
                    total++;
                    var a = c.Members[i];
                    var b = c.Members[j];
                    var key = string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
                    if (!scored.Contains(key)) unchained++;
                }
        }
        return total == 0 ? 0 : unchained / total;
    }
}

public static class InventoryVersionExtensions
{
    public static int OccurrenceCount(this InventoryVersion v) => v.Classes.Sum(c => c.OccurrenceCount);
}

/// <summary>Tiny helper to build Args inside the app for internal re-invocation.</summary>
public sealed class ArgsBuilder
{
    private string _verb = "";
    private readonly List<string> _args = new();
    public ArgsBuilder Verb(string v) { _verb = v; return this; }
    public ArgsBuilder Doc(string code) { _args.Add("--doc"); _args.Add(code); return this; }
    public ArgsBuilder Opt(string name, string value) { _args.Add("--" + name); _args.Add(value); return this; }
    public ArgsBuilder Flag(string name) { _args.Add("--" + name); return this; }
    public Args Build() => Args.Parse(new[] { _verb }.Concat(_args).ToArray());
}
