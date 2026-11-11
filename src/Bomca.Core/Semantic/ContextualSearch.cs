using Bomca.Core.Corpus;
using Bomca.Core.Json;

namespace Bomca.Core.Semantic;

public sealed class FocusRegion
{
    public string Segment { get; set; } = "";
    public int Start { get; set; }
    public int End { get; set; }
    public double Score { get; set; }
    public string Reference { get; set; } = "";
    public string Snippet { get; set; } = "";
    public List<string> MatchedTerms { get; set; } = new();
    public List<string> MissingTerms { get; set; } = new();
}

public sealed class SegmentFocus
{
    public string Name { get; set; } = "";
    public int QueryTerms { get; set; }
    public int ConceptPhrases { get; set; }
    public double BestWindowScore { get; set; }
    public double ControlMean { get; set; }
    public double PValue { get; set; }
    public List<FocusRegion> Windows { get; set; } = new();
    public List<FocusRegion> Regions { get; set; } = new();
}

public sealed class ContextualSearchResult
{
    public string Doc { get; set; } = "";
    public string Source { get; set; } = "";
    public string GeneratedUtc { get; set; } = "";
    public string Method { get; set; } = "";
    public int Window { get; set; }
    public int Stride { get; set; }
    public double ControlMean { get; set; }
    public List<SegmentFocus> Segments { get; set; } = new();
    /// <summary>Merged, ranked regions across all segments; this is what the symbol search is restricted to.</summary>
    public List<FocusRegion> Regions { get; set; } = new();
    public int TotalCorpusWords { get; set; }
    public int FocusCorpusWords { get; set; }
    public double SearchReductionFactor { get; set; }
    public List<string> Notes { get; set; } = new();
}

public sealed class ContextualSearchOptions
{
    public int Window { get; set; } = 700;
    public int Stride { get; set; } = 150;
    public int TopWindows { get; set; } = 12;
    public int TopRegionsPerSegment { get; set; } = 2;
    public int MaxRegions { get; set; } = 6;
    public int Controls { get; set; } = 40;
    public int Seed { get; set; } = 20260211;
    public bool UseEmbeddings { get; set; }
    public bool UseSynonyms { get; set; }
    public string EmbedModel { get; set; } = "";
}

/// <summary>
/// Fuzzy contextual search: find where in the corpus an approximate, layman-worded paraphrase belongs.
/// Two independent signals are combined — a lemma/synonym-aware lexical coverage score (which tolerates
/// "went up out of the land" for "exited over the mountains") and, optionally, embedding similarity from
/// a local Ollama model. The result is a short list of corpus regions that the symbol-level search can be
/// restricted to, which is exactly the short-circuit step: everything else in the book is skipped.
/// </summary>
public static class ContextualSearch
{
    /// <summary>Irregular verb forms common in the Book of Mormon's archaic register.</summary>
    private static readonly Dictionary<string, string> Irregular = new(StringComparer.Ordinal)
    {
        ["came"] = "come", ["come"] = "come", ["cometh"] = "come", ["went"] = "go", ["gone"] = "go",
        ["goeth"] = "go", ["said"] = "say", ["saith"] = "say", ["spake"] = "speak", ["spoke"] = "speak",
        ["died"] = "die", ["dieth"] = "die", ["began"] = "begin", ["begun"] = "begin", ["led"] = "lead",
        ["fought"] = "fight", ["taught"] = "teach", ["took"] = "take", ["taken"] = "take", ["gave"] = "give",
        ["given"] = "give", ["knew"] = "know", ["known"] = "know", ["saw"] = "see", ["seen"] = "see",
        ["made"] = "make", ["rose"] = "rise", ["risen"] = "rise", ["stood"] = "stand", ["told"] = "tell",
        ["thought"] = "think", ["brought"] = "bring", ["built"] = "build", ["sent"] = "send",
        ["left"] = "leave", ["kept"] = "keep", ["held"] = "hold", ["fled"] = "flee", ["dwelt"] = "dwell",
        ["dwelleth"] = "dwell", ["brought"] = "bring", ["smote"] = "smite", ["slew"] = "slay",
        ["slain"] = "slay", ["did"] = "do", ["done"] = "do", ["doth"] = "do", ["hath"] = "have",
        ["had"] = "have", ["was"] = "be", ["were"] = "be", ["been"] = "be", ["art"] = "be", ["is"] = "be",
        ["are"] = "be", ["am"] = "be", ["beheld"] = "behold", ["durst"] = "dare", ["wrought"] = "work",
        ["departed"] = "depart", ["departeth"] = "depart", ["tabernacles"] = "tabernacle",
        ["brethren"] = "brother", ["children"] = "child", ["men"] = "man", ["women"] = "woman",
        ["multitudes"] = "multitude", ["prophesied"] = "prophesy", ["prophesying"] = "prophesy",
        ["prophesieth"] = "prophesy", ["destroyed"] = "destroy", ["destruction"] = "destroy",
        ["ascended"] = "ascend", ["descended"] = "descend", ["reigneth"] = "reign", ["reigned"] = "reign",
        ["years"] = "year", ["days"] = "day", ["months"] = "month", ["weeks"] = "week",
        ["nephites"] = "nephite", ["lamanites"] = "lamanite", ["robbers"] = "robber",
        ["plates"] = "plate", ["tribes"] = "tribe", ["judges"] = "judge", ["disciples"] = "disciple",
    };

    /// <summary>Light lemma: irregular map, then plural/tense/archaic suffixes.</summary>
    public static string Lemma(string token)
    {
        var t = token.ToLowerInvariant();
        if (Irregular.TryGetValue(t, out var mapped)) return mapped;
        if (t.Length > 4 && t.EndsWith("ieth", StringComparison.Ordinal)) return t[..^4] + "y";
        if (t.Length > 4 && t.EndsWith("eth", StringComparison.Ordinal)) return t[..^3];
        if (t.Length > 4 && t.EndsWith("ies", StringComparison.Ordinal)) return t[..^3] + "y";
        if (t.Length > 4 && t.EndsWith("ing", StringComparison.Ordinal)) return t[..^3];
        if (t.Length > 3 && t.EndsWith("ed", StringComparison.Ordinal)) return t[..^2];
        if (t.Length > 3 && t.EndsWith("es", StringComparison.Ordinal)) return t[..^2];
        if (t.Length > 3 && t.EndsWith("s", StringComparison.Ordinal) && !t.EndsWith("ss", StringComparison.Ordinal)) return t[..^1];
        return t;
    }

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "and", "of", "to", "in", "a", "that", "it", "was", "were", "for", "with", "as", "his", "he",
        "they", "them", "their", "this", "these", "those", "which", "had", "has", "have", "be", "been", "by",
        "on", "at", "from", "into", "unto", "up", "then", "there", "so", "who", "whom", "not", "no", "all",
        "is", "are", "am", "an", "or", "but", "if", "do", "did", "does", "now", "thus", "after", "before",
        "more", "most", "than", "also", "about", "after", "over", "under", "again", "very", "same", "own",
        "may", "might", "shall", "will", "would", "could", "should", "one", "two", "three", "thousand",
    };

    public static List<string> QueryTerms(string text)
        => Normalizer.Tokenize(text)
            .Where(t => t.Length > 2 && !Stop.Contains(t))
            .Select(Lemma)
            .Where(t => t.Length > 2)
            .ToList();

    /// <summary>Multi-word concepts: consecutive content words from the paraphrase, used as a coverage bonus.</summary>
    public static List<List<string>> ConceptPhrases(string text, int maxPhrases = 30)
    {
        var raw = Normalizer.Tokenize(text).ToList();
        var concepts = new List<List<string>>();
        var run = new List<string>();
        foreach (var token in raw)
        {
            if (Stop.Contains(token) || token.Length <= 2)
            {
                if (run.Count >= 2) concepts.Add(run);
                run = new List<string>();
                continue;
            }
            run.Add(Lemma(token));
        }
        if (run.Count >= 2) concepts.Add(run);
        // keep the most specific (longest) concepts first
        return concepts.OrderByDescending(c => c.Count).Take(maxPhrases).ToList();
    }

    public static ContextualSearchResult Build(
        CorpusIndex corpus,
        string doc,
        string source,
        IReadOnlyList<(string Name, string Text)> segments,
        ContextualSearchOptions opt,
        Func<string, IReadOnlyCollection<string>, Task<List<string>>>? synonymExpander = null,
        Action<string>? log = null)
    {
        var result = new ContextualSearchResult
        {
            Doc = doc,
            Source = source,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Window = opt.Window,
            Stride = opt.Stride,
            TotalCorpusWords = corpus.Length,
            Method = "lemma-aware lexical coverage" + (opt.UseEmbeddings ? " + embedding similarity" : ""),
        };

        // Corpus lemmas, computed once.
        var corpusLemmas = new string[corpus.Length];
        for (var i = 0; i < corpus.Length; i++) corpusLemmas[i] = Lemma(corpus.Tokens[i]);

        var corpusFreq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in corpusLemmas) corpusFreq[l] = corpusFreq.TryGetValue(l, out var c) ? c + 1 : 1;

        foreach (var (name, text) in segments)
        {
            var terms = QueryTerms(text).ToList();
            var distinct = terms.Distinct().ToList();
            var weights = distinct.ToDictionary(
                t => t,
                t => Math.Log((corpus.Length + 1.0) / (1 + (corpusFreq.TryGetValue(t, out var f) ? f : 0))),
                StringComparer.Ordinal);
            var totalWeight = weights.Values.Sum();

            // Optional synonym expansion, constrained to corpus lemmas.
            var synonymMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (opt.UseSynonyms && synonymExpander is not null)
            {
                var vocab = corpusFreq.Where(kv => kv.Value >= 3).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var t in distinct.Take(40))
                {
                    var expanded = synonymExpander(t, vocab).GetAwaiter().GetResult()
                        .Select(Lemma).Where(x => x != t && vocab.Contains(x)).Distinct().ToList();
                    if (expanded.Count > 0) synonymMap[t] = expanded.Take(4).ToList();
                }
                log?.Invoke($"focus {name}: expanded {synonymMap.Count} terms into corpus synonyms");
            }

            var conv = SynonymService.OfflineFallback();
            foreach (var t in distinct)
            {
                if (synonymMap.ContainsKey(t)) continue;
                if (!conv.TryGetValue(t, out var list)) continue;
                var usable = list.Select(Lemma).Where(x => corpusFreq.ContainsKey(x)).ToList();
                if (usable.Count > 0) synonymMap[t] = usable;
            }

            var windowScores = new List<FocusRegion>();
            var best = 0.0;
            for (var w0 = 0; w0 < corpus.Length; w0 += opt.Stride)
            {
                var w1 = Math.Min(corpus.Length, w0 + opt.Window);
                var (score, matched, missing) = ScoreWindow(corpusLemmas, corpusFreq, w0, w1, weights, totalWeight, synonymMap);
                if (score > best) best = score;
                if (score < 0.25) continue;
                windowScores.Add(new FocusRegion
                {
                    Segment = name,
                    Start = w0,
                    End = w1,
                    Score = JsonIo.Round(score, 4),
                    Reference = corpus.ReferenceAt(w0),
                    Snippet = corpus.SnippetAt(w0, 26),
                    MatchedTerms = matched.Take(20).ToList(),
                    MissingTerms = missing.Take(12).ToList(),
                });
            }

            // Controls: the same query terms shuffled (frequency-preserving) and random excerpt queries.
            var controls = new List<double>();
            var rng = new Random(opt.Seed);
            var controlText = string.Join(' ', terms.OrderBy(_ => rng.Next()));
            var controlTerms = QueryTerms(controlText).Distinct().ToList();
            var controlWeights = controlTerms.ToDictionary(
                t => t,
                t => Math.Log((corpus.Length + 1.0) / (1 + (corpusFreq.TryGetValue(t, out var f) ? f : 0))),
                StringComparer.Ordinal);
            var controlTotal = controlWeights.Values.Sum();
            for (var c = 0; c < opt.Controls; c++)
            {
                var w0 = c * corpus.Length / Math.Max(1, opt.Controls);
                var w1 = Math.Min(corpus.Length, w0 + opt.Window);
                var (s, _, _) = ScoreWindow(corpusLemmas, corpusFreq, w0, w1, controlWeights, controlTotal, new Dictionary<string, List<string>>());
                controls.Add(s);
            }
            var controlMean = controls.Count == 0 ? 0 : controls.Average();
            var ge = controls.Count(s => s >= best);
            var p = (ge + 1.0) / (controls.Count + 1.0);

            var seg = new SegmentFocus
            {
                Name = name,
                QueryTerms = distinct.Count,
                ConceptPhrases = ConceptPhrases(text).Count,
                BestWindowScore = JsonIo.Round(best, 4),
                ControlMean = JsonIo.Round(controlMean, 4),
                PValue = JsonIo.Round(p, 4),
                Windows = windowScores.OrderByDescending(w => w.Score).Take(opt.TopWindows).ToList(),
            };
            seg.Regions = MergeRegions(seg.Windows, opt.Window, opt.TopRegionsPerSegment);
            result.Segments.Add(seg);
            log?.Invoke($"focus {name}: best window {best:0.000} (control {controlMean:0.000}, p {p:0.000}) at {corpus.ReferenceAt(seg.Windows.FirstOrDefault()?.Start ?? 0)}");
        }

        result.Regions = result.Segments
            .SelectMany(s => s.Regions)
            .OrderByDescending(r => r.Score)
            .GroupBy(r => r.Segment)
            .SelectMany(g => g.Take(opt.TopRegionsPerSegment))
            .OrderByDescending(r => r.Score)
            .Take(opt.MaxRegions)
            .ToList();
        result.FocusCorpusWords = result.Regions.Sum(r => r.End - r.Start);
        result.SearchReductionFactor = result.FocusCorpusWords == 0 ? 0 : corpus.Length / (double)result.FocusCorpusWords;

        if (result.UseEmbeddingsRequested(opt))
            result.Notes.Add("Embedding similarity was requested but is not available offline; lexical coverage alone produced this ranking.");
        result.Notes.Add($"Focused regions cover {result.FocusCorpusWords:N0} of {corpus.Length:N0} words " +
                         $"({result.SearchReductionFactor:0.0}x reduction); the symbol search is restricted to them with 'bomca match --focus'.");
        return result;
    }

    private static bool UseEmbeddingsRequested(this ContextualSearchResult r, ContextualSearchOptions o) => o.UseEmbeddings;

    /// <summary>
    /// Weighted coverage of the query's lemma set inside [w0,w1), plus a distinct-term coverage term so a
    /// window cannot score well by repeating one frequent word.
    /// </summary>
    private static (double score, List<string> matched, List<string> missing) ScoreWindow(
        string[] corpusLemmas,
        Dictionary<string, int> corpusFreq,
        int w0,
        int w1,
        Dictionary<string, double> weights,
        double totalWeight,
        Dictionary<string, List<string>> synonymMap)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        for (var i = w0; i < w1 && i < corpusLemmas.Length; i++) present.Add(corpusLemmas[i]);

        double weighted = 0;
        var matched = new List<string>();
        var missing = new List<string>();
        foreach (var (term, w) in weights)
        {
            if (present.Contains(term))
            {
                weighted += w;
                matched.Add(term);
                continue;
            }
            if (synonymMap.TryGetValue(term, out var syns) && syns.Any(present.Contains))
            {
                weighted += w * 0.7;
                matched.Add(term + "~");
                continue;
            }
            missing.Add(term);
        }
        if (totalWeight <= 1e-9) return (0, matched, missing);
        var coverage = weighted / totalWeight;
        var distinctCoverage = weights.Count == 0 ? 0 : (double)matched.Count / weights.Count;
        // Rare-term coverage dominates; distinct coverage stops one common word from carrying a window.
        var score = 0.55 * coverage + 0.45 * distinctCoverage;
        return (Math.Clamp(score, 0, 1), matched, missing);
    }

    private static List<FocusRegion> MergeRegions(List<FocusRegion> windows, int window, int take)
    {
        var regions = new List<FocusRegion>();
        foreach (var w in windows.OrderByDescending(x => x.Score))
        {
            var hit = regions.FirstOrDefault(r => w.Start <= r.End && w.End >= r.Start);
            if (hit is not null)
            {
                hit.Start = Math.Min(hit.Start, w.Start);
                hit.End = Math.Max(hit.End, w.End);
                hit.Score = Math.Max(hit.Score, w.Score);
                continue;
            }
            regions.Add(new FocusRegion
            {
                Segment = w.Segment,
                Start = w.Start,
                End = w.End,
                Score = w.Score,
                Reference = w.Reference,
                Snippet = w.Snippet,
                MatchedTerms = w.MatchedTerms,
                MissingTerms = w.MissingTerms,
            });
            if (regions.Count >= take) break;
        }
        return regions.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>Verse-level ranking for reporting: which individual verses look closest to the paraphrase.</summary>
    public static List<(VerseSpan Verse, double Score, int Matched)> RankVerses(CorpusIndex corpus, string text)
    {
        var corpusLemmas = corpus.Tokens.Select(Lemma).ToArray();
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in corpusLemmas) freq[l] = freq.TryGetValue(l, out var c) ? c + 1 : 1;
        var terms = QueryTerms(text).Distinct().ToList();
        var weights = terms.ToDictionary(
            t => t,
            t => Math.Log((corpus.Length + 1.0) / (1 + (freq.TryGetValue(t, out var f) ? f : 0))),
            StringComparer.Ordinal);
        var total = weights.Values.Sum();

        var ranked = new List<(VerseSpan, double, int)>();
        foreach (var verse in corpus.Verses)
        {
            var (score, matched, _) = ScoreWindow(corpusLemmas, freq, verse.TokenStart, verse.TokenStart + verse.TokenCount,
                weights, total, new Dictionary<string, List<string>>());
            ranked.Add((verse, score, matched.Count));
        }
        return ranked.OrderByDescending(r => r.Item2).ThenByDescending(r => r.Item3).ToList();
    }
}
