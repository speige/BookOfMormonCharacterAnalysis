using Bomca.Core.Analysis;
using Bomca.Core.Clustering;
using Bomca.Core.Corpus;
using Bomca.Core.Json;
using Bomca.Core.Matching;
using Bomca.Core.Model;
using Bomca.Core.Reporting;
using Bomca.Core.Semantic;
using Bomca.Core.Significance;

namespace Bomca.Cli;

public static partial class App
{
    /// <summary>Data-driven inventory threshold: one and a quarter standard deviations above the mean pair score.</summary>
    public static double AutoThreshold(List<GlyphPair> pairs)
    {
        if (pairs.Count < 10) return 0.70;
        var scores = pairs.Select(p => p.Breakdown.Combined).ToList();
        var mean = scores.Average();
        var sd = Math.Sqrt(scores.Sum(s => (s - mean) * (s - mean)) / Math.Max(1, scores.Count - 1));
        return Math.Round(Math.Clamp(mean + 1.25 * sd, 0.55, 0.90), 4);
    }

    public static (InventoryVersion inventory, string path) LoadInventory(string code, double? threshold)
    {
        var dir = Path.Combine(OutRoot, code);
        var versionsPath = Path.Combine(dir, "inventory-versions.json");
        if (!File.Exists(versionsPath))
            throw new FileNotFoundException($"no inventory for '{code}'. Run: bomca inventory --doc {code}");

        var versions = JsonIo.Read<List<InventoryVersionRef>>(versionsPath);
        var target = threshold ?? versions.OrderBy(v => Math.Abs(v.Threshold - v.AutoThreshold)).First().Threshold;
        var best = versions.OrderBy(v => Math.Abs(v.Threshold - target)).First();
        var path = Path.Combine(dir, best.File);
        return (JsonIo.Read<InventoryVersion>(path), path);
    }

    private static (GlyphStream stream, CorpusIndex corpus, InventoryVersion inventory) LoadMatchInputs(Args args)
    {
        var code = args.GetList("doc").FirstOrDefault() ?? "W";
        var corpusPath = Path.Combine(DerivedDir, args.Get("edition", "current") + ".json");
        if (!File.Exists(corpusPath))
            throw new FileNotFoundException($"no corpus at {corpusPath}. Run: bomca corpus fetch");
        var corpus = CorpusIndex.Build(JsonIo.Read<CorpusFile>(corpusPath));
        var (doc, _) = LoadDocument(code);
        var (inventory, _) = LoadInventory(code, args.Has("threshold") ? args.GetDouble("threshold", 0.8) : null);
        var stream = GlyphStream.FromDocument(doc, inventory);
        return (stream, corpus, inventory);
    }

    public static int RunAnalyze(Args args)
    {
        var codes = args.GetList("doc");
        if (codes.Count == 0) codes = new List<string> { "W" };
        var corpusWords = 267471;
        var corpusPath = Path.Combine(DerivedDir, "current.json");
        if (File.Exists(corpusPath))
            corpusWords = CorpusIndex.Build(JsonIo.Read<CorpusFile>(corpusPath)).Length;

        foreach (var code in codes)
        {
            var (doc, dir) = LoadDocument(code);
            var (inventory, invPath) = LoadInventory(code, args.Has("threshold") ? args.GetDouble("threshold", 0.8) : null);
            var spec = Bomca.Core.Documents.DocumentCatalog.Get(code);
            var analysis = StructureAnalyzer.Analyze(doc, inventory, corpusWords,
                spec is null ? null : (spec.FragmentInches.W, spec.FragmentInches.H));
            JsonIo.Write(Path.Combine(dir, "analysis.json"), analysis);

            Console.WriteLine($"analyze {code}: {analysis.GlyphCount} glyphs, {analysis.ClassCount} classes at t={analysis.Threshold:0.00}");
            Console.WriteLine($"  zipf slope {analysis.ZipfSlope:0.00} (R² {analysis.ZipfRSquared:0.00}); row height trend {analysis.RowHeightTrend:0.0} px/row");
            Console.WriteLine($"  black squares {analysis.BlackSquares.Count}; comb glyphs {analysis.CombGlyphs.Count} (stroke count varies: {analysis.CombStrokeCountVaries})");
            Console.WriteLine($"  repeat regularity vs chance {analysis.RepeatRegularityVsChance:0.00}x; symbols/in² {analysis.Compression.SymbolsPerSquareInch:0.0}");
            foreach (var scenario in analysis.Compression.Scenarios)
                Console.WriteLine($"  compression [{scenario.Name}]: ~{scenario.EstimatedSymbols:N0} symbols, {scenario.WordsPerSymbol:0.00} words/symbol -> {scenario.Interpretation}");
            foreach (var o in analysis.Observations) Console.WriteLine($"  * {o}");
        }
        return 0;
    }

    public static async Task<int> RunMatch(Args args)
    {
        var (stream, corpus, inventory) = LoadMatchInputs(args);
        var opt = new MatchOptions
        {
            MinOccurrences = args.GetInt("min-occurrences", 3),
            MaxPhrases = args.GetInt("max-phrases", 400),
            MaxPhraseLength = args.GetInt("max-phrase-length", 4),
            JointTopHypotheses = args.GetInt("joint-top", 12),
            WarpStride = args.GetInt("warp-stride", 40),
            PassageTake = args.GetInt("passages", 20),
            RunAlignment = !args.Has("no-align"),
            WarpGate = args.GetDouble("warp-gate", 0.42),
        };
        if (args.GetList("windows").Count > 0)
            opt.WindowSizes = args.GetList("windows").Select(int.Parse).ToArray();

        var widen = args.Has("widen");
        if (widen)
        {
            var vocabulary = corpus.UnigramCounts.Keys.ToHashSet(StringComparer.Ordinal);
            var synonymService = new SynonymService(enabled: true);
            var probes = args.GetList("widen-terms");
            if (probes.Count == 0) probes = new List<string> { "god", "angel", "earth", "interpreter", "book" };
            var expansions = new Dictionary<string, List<string>>();
            foreach (var term in probes)
            {
                var expanded = await synonymService.ExpandAsync(term, vocabulary);
                expansions[term] = expanded;
                Console.WriteLine($"match: widened '{term}' -> {string.Join(", ", expanded)}");
            }
            JsonIo.Write(Path.Combine(OutRoot, stream.Doc, "synonyms.json"), expansions);
        }

        var result = MatchEngine.Run(stream, corpus, opt, Console.WriteLine);
        result.Notes.Add(widen
            ? "Synonym widening was enabled: phrase targets include model-proposed near-synonyms drawn from the corpus vocabulary."
            : "Synonyms were not widened; targets are literal corpus phrases. Use --widen to widen the search.");

        var dir = Path.Combine(OutRoot, stream.Doc);
        JsonIo.Write(Path.Combine(dir, "matches.json"), result);
        MatchingReportBuilder.Write(Path.Combine(dir, "matches.html"), result, corpus, stream, inventory);

        Console.WriteLine($"match {stream.Doc}: best fingerprint {result.Fingerprints.FirstOrDefault()?.Score:0.000}, best passage {result.Passages.FirstOrDefault()?.Combined:0.000}");
        foreach (var h in result.Fingerprints.Take(8))
            Console.WriteLine($"  A {h.Score:0.000}  {h.ClassId} ({h.Occurrences}x) <-> \"{h.Phrase}\" ({h.PhraseOccurrences}x)");
        foreach (var p in result.Passages.Take(5))
            Console.WriteLine($"  B/C {p.Combined:0.000}  {p.Reference} [w{p.WordStart}+{p.WindowWords}] {p.Snippet}");
        return 0;
    }

    public static async Task<int> RunSignificance(Args args)
    {
        var (stream, corpus, _) = LoadMatchInputs(args);
        var opt = new MatchOptions
        {
            MinOccurrences = args.GetInt("min-occurrences", 3),
            RunAlignment = false,
            AnchorConcepts = Array.Empty<string>(),
        };
        var iterations = args.GetInt("iterations", 200);
        var report = SignificanceHarness.Run(stream, corpus, opt, iterations, args.GetInt("seed", 20260101), args.GetDouble("alpha", 0.01), Console.WriteLine);
        var dir = Path.Combine(OutRoot, stream.Doc);
        JsonIo.Write(Path.Combine(dir, "significance.json"), report);
        Console.WriteLine($"significance {stream.Doc}: observed {report.ObservedBest:0.000} vs null {report.NullMean:0.000} ± {report.NullSd:0.000} (max {report.NullMax:0.000}); p = {report.PValue:0.0000}, z = {report.Z:0.0}");
        foreach (var v in report.Verdict) Console.WriteLine($"  {v}");
        return 0;
    }

    private sealed class InventoryVersionRef
    {
        public double Threshold { get; set; }
        public double AutoThreshold { get; set; }
        public int Classes { get; set; }
        public int Singletons { get; set; }
        public string File { get; set; } = "";
    }
}
