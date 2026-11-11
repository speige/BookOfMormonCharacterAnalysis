using Bomca.Core.Corpus;
using Bomca.Core.Json;
using Bomca.Core.Model;

namespace Bomca.Core.Matching;

public sealed class GlyphStream
{
    public string Doc { get; init; } = "";
    public string[] ClassIds { get; init; } = Array.Empty<string>();
    public string[] OccurrenceIds { get; init; } = Array.Empty<string>();
    public int[] Rows { get; init; } = Array.Empty<int>();
    public double Threshold { get; init; }
    public int Length => ClassIds.Length;

    public static GlyphStream FromDocument(DocumentRecord doc, InventoryVersion inventory)
    {
        var glyphs = doc.Symbols.Where(s => s.Note != "title-row").OrderBy(s => s.GlobalIndex).ToList();
        var lookup = inventory.Classes.SelectMany(c => c.Members.Select(m => (m, c.ClassId)))
            .ToDictionary(x => x.m, x => x.ClassId, StringComparer.Ordinal);
        return new GlyphStream
        {
            Doc = doc.DocCode,
            ClassIds = glyphs.Select(g => lookup.TryGetValue(g.Id, out var c) ? c : "C000").ToArray(),
            OccurrenceIds = glyphs.Select(g => g.Id).ToArray(),
            Rows = glyphs.Select(g => g.Row).ToArray(),
            Threshold = inventory.Threshold,
        };
    }

    public Dictionary<string, List<int>> PositionsByClass()
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < ClassIds.Length; i++)
        {
            if (!map.TryGetValue(ClassIds[i], out var list)) map[ClassIds[i]] = list = new List<int>();
            list.Add(i);
        }
        return map;
    }

    public GlyphStream Shuffle(int seed)
    {
        var rng = new Random(seed);
        var idx = Enumerable.Range(0, Length).OrderBy(_ => rng.Next()).ToArray();
        return new GlyphStream
        {
            Doc = Doc,
            ClassIds = idx.Select(i => ClassIds[i]).ToArray(),
            OccurrenceIds = idx.Select(i => OccurrenceIds[i]).ToArray(),
            Rows = idx.Select(i => Rows[i]).ToArray(),
            Threshold = Threshold,
        };
    }

    /// <summary>Reverse the stream: the claim under test reads right-to-left.</summary>
    public GlyphStream Reversed()
    {
        var n = Length;
        return new GlyphStream
        {
            Doc = Doc,
            ClassIds = Enumerable.Range(0, n).Select(i => ClassIds[n - 1 - i]).ToArray(),
            OccurrenceIds = Enumerable.Range(0, n).Select(i => OccurrenceIds[n - 1 - i]).ToArray(),
            Rows = Enumerable.Range(0, n).Select(i => Rows[n - 1 - i]).ToArray(),
            Threshold = Threshold,
        };
    }

    /// <summary>Drop occurrences (by id) and renumber — used to remove numeral glyphs from the word stream.</summary>
    public GlyphStream Excluding(HashSet<string> ids)
    {
        var keep = Enumerable.Range(0, Length).Where(i => !ids.Contains(OccurrenceIds[i])).ToArray();
        return new GlyphStream
        {
            Doc = Doc,
            ClassIds = keep.Select(i => ClassIds[i]).ToArray(),
            OccurrenceIds = keep.Select(i => OccurrenceIds[i]).ToArray(),
            Rows = keep.Select(i => Rows[i]).ToArray(),
            Threshold = Threshold,
        };
    }

    public GlyphStream Half(int part, int parts = 2)
    {
        var lo = Length * part / parts;
        var hi = Length * (part + 1) / parts;
        return new GlyphStream
        {
            Doc = Doc,
            ClassIds = ClassIds[lo..hi],
            OccurrenceIds = OccurrenceIds[lo..hi],
            Rows = Rows[lo..hi],
            Threshold = Threshold,
        };
    }
}

public sealed class FingerprintHypothesis
{
    public string ClassId { get; set; } = "";
    public int Occurrences { get; set; }
    public string Phrase { get; set; } = "";
    public int PhraseOccurrences { get; set; }
    public double Score { get; set; }
    public double GapDistance { get; set; }
    public double CountPenalty { get; set; }
    public string Method { get; set; } = "fingerprint";
    public List<double> GlyphGaps { get; set; } = new();
    public List<double> PhraseGaps { get; set; } = new();
    public List<int> BestWindowOffsets { get; set; } = new();
}

public sealed class PassageCandidate
{
    public string Phrase { get; set; } = "";
    public string ClassId { get; set; } = "";
    public int WordStart { get; set; }
    public int WindowWords { get; set; }
    public string Reference { get; set; } = "";
    public string Snippet { get; set; } = "";
    public double WarpScore { get; set; }
    public double AlignScore { get; set; }
    public double AnchorScore { get; set; }
    public double Combined { get; set; }
    public string Method { get; set; } = "joint-warp";
    public List<string> Evidence { get; set; } = new();
    public int[] GlyphToWord { get; set; } = Array.Empty<int>();
    /// <summary>The class -> phrase mappings this window was scored against (used to align it).</summary>
    public Dictionary<string, string> Mappings { get; set; } = new();
}

public sealed class AlignRow
{
    public int GlyphIndex { get; set; }
    public string ClassId { get; set; } = "";
    public int WordStart { get; set; }
    public int WordCount { get; set; }
    public string Words { get; set; } = "";
    public double Match { get; set; }
}

public sealed class AnchorFinding
{
    public string Concept { get; set; } = "";
    public int WordStart { get; set; }
    public string Reference { get; set; } = "";
    public double Density { get; set; }
    public string Snippet { get; set; } = "";
    public List<string> EnrichedClasses { get; set; } = new();
}

public sealed class MatchResult
{
    public string Doc { get; set; } = "";
    public string Edition { get; set; } = "";
    public double Threshold { get; set; }
    public string GeneratedUtc { get; set; } = "";
    public int Glyphs { get; set; }
    public int Classes { get; set; }
    public int CorpusTokens { get; set; }
    public int CandidatePhrases { get; set; }
    public double ElapsedMs { get; set; }
    public List<FingerprintHypothesis> Fingerprints { get; set; } = new();
    public List<PassageCandidate> Passages { get; set; } = new();
    public List<AnchorFinding> Anchors { get; set; } = new();
    public Dictionary<string, string> Settings { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class MatchOptions
{
    public int MinOccurrences { get; set; } = 3;
    public int MaxHypotheses { get; set; } = 300;
    public int MaxPhrases { get; set; } = 400;
    public int MaxPhraseLength { get; set; } = 4;
    public int JointTopHypotheses { get; set; } = 12;
    /// <summary>Candidate spans (English words) that the whole glyph stream might cover.</summary>
    public int[] WindowSizes { get; set; } = { 150, 250, 400, 700, 1200, 2000, 3500 };
    public int WarpStride { get; set; } = 40;
    public int PassageTake { get; set; } = 20;
    public bool RunAlignment { get; set; } = true;
    public IReadOnlyList<string> AnchorConcepts { get; set; } = new[]
    {
        "god", "lord", "son", "angel", "earth", "interpreter", "book", "behold", "came to pass",
    };
    public int AnchorWindow { get; set; } = 200;
    public double GapWeight { get; set; } = 1.0;
    /// <summary>
    /// Restrict the window scanner to these corpus regions (from 'bomca focus'). Empty = scan everywhere.
    /// </summary>
    public List<(int Start, int End)> FocusRegions { get; set; } = new();
    /// <summary>Compression bounds used to reject implausible document spans.</summary>
    public double MinWordsPerGlyph { get; set; } = 0.8;
    public double MaxWordsPerGlyph { get; set; } = 12.0;
    public int Seed { get; set; } = 12345;
    /// <summary>Minimum span score reported by scanner B.</summary>
    public double WarpGate { get; set; } = 0.42;
    /// <summary>A hypothesis is considered to fit a window when its projection cost is at most this.</summary>
    public double MemberCostGate { get; set; } = 0.25;
    /// <summary>Minimum spacing-match score for a class to count as fitting a window.</summary>
    public double WindowFitGate { get; set; } = 0.86;
    /// <summary>Minimum number of classes that must fit the same window.</summary>
    public int MinFittingClasses { get; set; } = 3;
    /// <summary>Minimum words in a phrase for the window scanner (1 = allow single words).</summary>
    public int MinPhraseWords { get; set; } = 2;
}

/// <summary>
/// The four scanners described in the research notes, in the order they should be run:
/// A relative-distance fingerprints, B joint linear-warp constraint search, C bounded many-to-many
/// alignment refinement, D semantic anchor probing.
/// </summary>
public static class MatchEngine
{
    public static MatchResult Run(GlyphStream stream, CorpusIndex corpus, MatchOptions opt, Action<string>? log = null,
        Dictionary<string, List<int>>? precomputedPositions = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new MatchResult
        {
            Doc = stream.Doc,
            Edition = corpus.Edition,
            Threshold = stream.Threshold,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Glyphs = stream.Length,
            CorpusTokens = corpus.Length,
            Settings = new Dictionary<string, string>
            {
                ["minOccurrences"] = opt.MinOccurrences.ToString(),
                ["maxPhrases"] = opt.MaxPhrases.ToString(),
                ["maxPhraseLength"] = opt.MaxPhraseLength.ToString(),
                ["windowSizes"] = string.Join(",", opt.WindowSizes),
                ["warpStride"] = opt.WarpStride.ToString(),
                ["seed"] = opt.Seed.ToString(),
                ["focusRegions"] = opt.FocusRegions.Count.ToString(),
                ["focusWords"] = opt.FocusRegions.Sum(r => r.End - r.Start).ToString(),
            },
        };
        result.Classes = stream.PositionsByClass().Count;

        // Phrase positions depend only on the corpus, so the significance harness computes them once and
        // reuses them for every shuffled run.
        var swPhase = System.Diagnostics.Stopwatch.StartNew();
        var positions = precomputedPositions;
        if (positions is null)
        {
            var phrases = CandidatePhrases(corpus, opt);
            positions = phrases.ToDictionary(p => p.Phrase, p => corpus.PositionsOf(p.Phrase), StringComparer.Ordinal);
        }
        result.CandidatePhrases = positions.Count;
        log?.Invoke($"match: {stream.Length} glyphs / {result.Classes} classes vs {corpus.Length} tokens; {positions.Count} candidate phrases");
        result.Fingerprints = ScanFingerprints(stream, positions, opt, corpus.Length);
        log?.Invoke($"match: scanner A produced {result.Fingerprints.Count} hypotheses (best {result.Fingerprints.FirstOrDefault()?.Score:0.000}) [{swPhase.ElapsedMilliseconds} ms]");
        swPhase.Restart();

        result.Passages = ScanJointWarp(stream, corpus, positions, result.Fingerprints, opt, log);
        log?.Invoke($"match: scanner B produced {result.Passages.Count} passages (best {result.Passages.FirstOrDefault()?.Combined:0.000}) [{swPhase.ElapsedMilliseconds} ms]");
        swPhase.Restart();

        if (opt.RunAlignment)
        {
            foreach (var passage in result.Passages)
            {
                // Align each window against the mappings that produced it: this keeps the constraint set
                // coherent instead of mixing hypotheses from unrelated consensus groups.
                var dictionary = passage.Mappings.Count > 0
                    ? passage.Mappings.GroupBy(kv => kv.Key, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(x => x.Value), StringComparer.Ordinal), StringComparer.Ordinal)
                    : BuildDictionary(result.Fingerprints, opt.JointTopHypotheses);
                var (score, rows) = GappedAligner.Align(stream, corpus, passage.WordStart, passage.WindowWords, dictionary);
                // Coverage is the honest alignment signal for a partial dictionary: the share of the window's
                // words explained by an exact glyph-to-phrase match. A whole-window DP normalisation would be
                // dominated by the (unknown) majority of glyphs that have no dictionary entry yet.
                var coveredWords = rows.Where(r => r.Match >= 0.85).Sum(r => r.WordCount);
                var coverage = JsonIo.Round(Math.Clamp((double)coveredWords / Math.Max(1, passage.WindowWords), 0, 1), 5);
                passage.AlignScore = coverage;
                passage.Combined = JsonIo.Round(0.45 * passage.WarpScore + 0.55 * Math.Min(1.0, coverage * 12), 4);
                var exact = rows.Count(r => r.Match > 0.99);
                var unexplained = rows.Count(r => r.Match <= 0);
                passage.Evidence.Add($"alignment: {exact} glyphs matched to exact words, {unexplained} unexplained");
                passage.GlyphToWord = rows.Select(r => r.WordStart).ToArray();
                var firstExact = rows.FirstOrDefault(r => r.Match > 0.99);
                if (firstExact is not null)
                    passage.Evidence.Add($"first exact match: {firstExact.ClassId} -> \"{firstExact.Words}\"");
            }
            result.Passages = result.Passages.OrderByDescending(p => p.Combined).ToList();
            log?.Invoke($"match: scanner C refined top passages (best combined {result.Passages.FirstOrDefault()?.Combined:0.000})");
        }

        result.Anchors = ScanAnchors(stream, corpus, opt, log);
        sw.Stop();
        result.ElapsedMs = sw.ElapsedMilliseconds;
        log?.Invoke($"match: scanner D produced {result.Anchors.Count} anchor passages in {result.ElapsedMs} ms");

        if (result.Passages.Count == 0)
            result.Notes.Add("No passage survived the joint-warp scan. That is a result, not a failure: it means no window explains several class patterns at once.");
        return result;
    }

    public static List<(string Phrase, int Count)> CandidatePhrases(CorpusIndex corpus, MatchOptions opt)
    {
        var list = new List<(string, int)>();
        for (var n = 1; n <= opt.MaxPhraseLength; n++)
        {
            if (n >= corpus.NgramCounts.Length) break;
            list.AddRange(corpus.NgramCounts[n]
                .Where(kv => kv.Value >= opt.MinOccurrences)
                .OrderByDescending(kv => kv.Value)
                .Take(opt.MaxPhrases)
                .Select(kv => (kv.Key, kv.Value)));
        }
        return list.DistinctBy(t => t.Item1).ToList();
    }

    // ---------------------------------------------------------------- scanner A

    public static List<FingerprintHypothesis> ScanFingerprints(
        GlyphStream stream,
        Dictionary<string, List<int>> phrasePositions,
        MatchOptions opt,
        int corpusLength = 0)
    {
        // A fingerprint needs at least two gaps to be meaningful: two occurrences always produce the
        // degenerate normalised vector (1.0), which would match every phrase equally well.
        var minOcc = Math.Max(3, opt.MinOccurrences);
        var byClass = stream.PositionsByClass()
            .Where(kv => kv.Value.Count >= minOcc)
            .OrderByDescending(kv => kv.Value.Count)
            .ToList();

        // Phrase gap vectors are independent of the glyph stream: compute them once, not per pair.
        var gapCache = new Dictionary<string, (List<double> Gaps, double Total)>(StringComparer.Ordinal);
        foreach (var (phrase, occ) in phrasePositions)
        {
            if (occ.Count < minOcc) continue;
            var gaps = Gaps(occ);
            gapCache[phrase] = (gaps, gaps.Sum());
        }

        var results = new List<FingerprintHypothesis>();
        foreach (var (classId, positions) in byClass)
        {
            var glyphGaps = NormalizedGaps(positions);
            foreach (var (phrase, phraseGapsFull) in gapCache)
            {
                var occCount = phraseGapsFull.Gaps.Count + 1;
                var total = phraseGapsFull.Total;
                if (total <= 0) continue;

                // slide a window of glyphGaps.Count gaps over the phrase's occurrence gaps
                var k = glyphGaps.Count;
                var bestScore = 0.0;
                var bestDist = 1.0;
                var bestOffset = 0;
                var gapsRef = phraseGapsFull.Gaps;
                for (var start = 0; start + k < occCount; start++)
                {
                    // Rolling sum, no per-window allocations: this loop runs millions of times per search.
                    double wTotal = 0;
                    for (var t = 0; t < k; t++) wTotal += gapsRef[start + t];
                    if (wTotal <= 0) continue;
                    double distance = 0;
                    for (var t = 0; t < k; t++) distance += Math.Abs(glyphGaps[t] - gapsRef[start + t] / wTotal);
                    distance /= 2.0;
                    if (distance < bestDist) { bestDist = distance; bestScore = 1 - distance; bestOffset = start; }
                }

                // A longer phrase is more specific evidence than a single common word. Raw occurrence counts
                // are deliberately NOT compared here: a phrase occurs many times across the whole book while
                // a glyph class occurs only inside the document's span, so their global counts are not
                // comparable. Count agreement is handled by the span-consensus scanner instead.
                var countPenalty = 0.0;
                var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var specificity = 0.8 + 0.2 * Math.Min(1.0, (words - 1) / 3.0);
                var score = bestScore * specificity;

                // Compression plausibility: the implied document span must be believable for a document
                // of this many glyphs. Without this, rare long phrases dominate the ranking.
                if (corpusLength > 0)
                {
                    var impliedSpan = positions.Count * (double)corpusLength / occCount;
                    var ratio = impliedSpan / Math.Max(1, stream.Length);
                    var clamped = Math.Clamp(ratio, opt.MinWordsPerGlyph, opt.MaxWordsPerGlyph);
                    score *= Math.Exp(-Math.Abs(Math.Log(clamped / Math.Max(1e-6, ratio))));
                }
                if (score < 0.55) continue;

                results.Add(new FingerprintHypothesis
                {
                    ClassId = classId,
                    Occurrences = positions.Count,
                    Phrase = phrase,
                    PhraseOccurrences = occCount,
                    Score = JsonIo.Round(score, 4),
                    GapDistance = JsonIo.Round(bestDist, 4),
                    CountPenalty = JsonIo.Round(countPenalty, 4),
                    GlyphGaps = glyphGaps.Select(g => JsonIo.Round(g, 4)).ToList(),
                    PhraseGaps = gapsRef.Select(g => JsonIo.Round(g / total, 4)).ToList(),
                    BestWindowOffsets = new List<int> { bestOffset },
                });
            }
        }

        return results.OrderByDescending(r => r.Score).ThenByDescending(r => r.Occurrences).Take(opt.MaxHypotheses).ToList();
    }

    private static List<double> NormalizedGaps(List<int> positions)
    {
        var gaps = Gaps(positions);
        var total = gaps.Sum();
        return total <= 0 ? new List<double>() : gaps.Select(g => g / total).ToList();
    }

    private static List<double> Gaps(List<int> positions)
    {
        var gaps = new List<double>();
        for (var i = 1; i < positions.Count; i++) gaps.Add(positions[i] - positions[i - 1]);
        return gaps;
    }

    private static double L1(List<double> a, List<double> b)
    {
        var n = Math.Min(a.Count, b.Count);
        var sum = 0.0;
        for (var i = 0; i < n; i++) sum += Math.Abs(a[i] - b[i]);
        return sum + Math.Abs(a.Count - b.Count) * 0.5;
    }

    public static Dictionary<string, HashSet<string>> BuildDictionary(List<FingerprintHypothesis> hypotheses, int top)
    {
        var dict = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var h in hypotheses.Take(top))
        {
            if (!dict.TryGetValue(h.ClassId, out var set)) dict[h.ClassId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(h.Phrase);
        }
        return dict;
    }

    // ---------------------------------------------------------------- scanner B

    /// <summary>
    /// Multi-constraint window search, evaluated inside each window rather than against global occurrence
    /// lists. For a candidate window the number of phrase occurrences inside the window must equal the
    /// number of glyph occurrences of the class, and their relative spacing must match. That removes the
    /// enormous multiple-comparison freedom of sliding a short pattern over a long global occurrence list,
    /// which is what makes naive fingerprint matching find coincidences everywhere. A window scores by how
    /// many distinct classes fit at once.
    /// </summary>
    public static List<PassageCandidate> ScanJointWarp(
        GlyphStream stream,
        CorpusIndex corpus,
        Dictionary<string, List<int>> phrasePositions,
        List<FingerprintHypothesis> hypotheses,
        MatchOptions opt,
        Action<string>? log = null)
    {
        if (stream.Length < 8 || phrasePositions.Count == 0) return new List<PassageCandidate>();

        var byClass = stream.PositionsByClass()
            .Where(kv => kv.Value.Count >= 3)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        if (byClass.Count < 2) return new List<PassageCandidate>();

        // Candidate spans bracket the compression model; the document's real span is one of them.
        var spans = new List<int>();
        for (var ratio = opt.MinWordsPerGlyph; ratio <= opt.MaxWordsPerGlyph; ratio *= 1.6)
            spans.Add((int)Math.Round(stream.Length * ratio));
        spans.AddRange(opt.WindowSizes.Where(w => w >= 40));
        var deduped = new List<int>();
        foreach (var sp in spans.Where(sp => sp >= 40 && sp < corpus.Length).OrderBy(sp => sp))
        {
            if (deduped.Any(d => Math.Abs(Math.Log((double)sp / d)) < Math.Log(1.2))) continue;
            deduped.Add(sp);
        }
        log?.Invoke($"match: scanning {deduped.Count} candidate spans ({string.Join(", ", deduped)}) over {byClass.Count} classes");

        var phraseList = phrasePositions.ToList();
        var candidates = new List<PassageCandidate>();

        foreach (var span in deduped)
        {
            var stride = Math.Max(60, span / 8);
            for (var w0 = 0; w0 + span <= corpus.Length; w0 += stride)
            {
                if (opt.FocusRegions.Count > 0 && !InFocus(opt.FocusRegions, w0, w0 + span)) continue;
                var hi = w0 + span;
                // Bucket the candidate phrases by how many times they occur inside this window.
                var buckets = new Dictionary<int, List<(string Phrase, List<int> Inside)>>();
                foreach (var (phrase, occ) in phraseList)
                {
                    if (phrase.AsSpan().Count(' ') + 1 < opt.MinPhraseWords) continue;
                    var lo = LowerBound(occ, w0);
                    var up = LowerBound(occ, hi);
                    var count = up - lo;
                    if (count < 2) continue;
                    if (!buckets.TryGetValue(count, out var list)) buckets[count] = list = new List<(string, List<int>)>();
                    list.Add((phrase, occ.GetRange(lo, count)));
                }
                if (buckets.Count == 0) continue;

                var perClass = new List<(double score, string classId, string phrase, int hits)>();
                foreach (var (classId, glyphPositions) in byClass)
                {
                    if (!buckets.TryGetValue(glyphPositions.Count, out var options)) continue;
                    var glyphNorm = NormalizePositions(glyphPositions, 0, Math.Max(1, stream.Length - 1));
                    var bestScore = 0.0;
                    string bestPhrase = "";
                    var bestHits = 0;
                    foreach (var (phrase, inside) in options)
                    {
                        var phraseNorm = NormalizePositions(inside, w0, hi);
                        var distance = 0.0;
                        for (var i = 0; i < glyphNorm.Count; i++) distance += Math.Abs(glyphNorm[i] - phraseNorm[i]);
                        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        var specificity = 0.8 + 0.2 * Math.Min(1.0, (words - 1) / 3.0);
                        var fitScore = (1 - distance / 2.0) * specificity;
                        if (fitScore <= bestScore) continue;
                        bestScore = fitScore;
                        bestPhrase = phrase;
                        bestHits = 0;
                        for (var i = 0; i < glyphNorm.Count; i++)
                            if (Math.Abs(glyphNorm[i] - phraseNorm[i]) < 0.04) bestHits++;
                    }
                    if (bestScore >= opt.WindowFitGate) perClass.Add((bestScore, classId, bestPhrase, bestHits));
                }

                // Greedy bijection: each phrase may be claimed by one class only.
                var fits = new List<(double score, string classId, string phrase, int hits)>();
                var usedPhrases = new HashSet<string>(StringComparer.Ordinal);
                foreach (var f in perClass.OrderByDescending(f => f.score))
                {
                    if (!usedPhrases.Add(f.phrase)) continue;
                    fits.Add(f);
                }
                if (fits.Count < opt.MinFittingClasses) continue;
                var tightness = fits.Average(f => f.score);
                var support = Math.Min(1.0, fits.Count / 6.0);
                var score = tightness * (0.55 + 0.45 * support);
                if (score < opt.WarpGate) continue;

                // Spatial stability: a genuine alignment survives the window sliding slightly, a coincidence
                // does not. This is a cheap internal held-out test that removes most spurious windows.
                var mappings = fits.GroupBy(f => f.classId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().phrase, StringComparer.Ordinal);
                var stability = 0.0;
                var shifts = 0;
                foreach (var shift in new[] { -stride, stride })
                {
                    var shifted = ScoreMappings(stream, phrasePositions, mappings, w0 + shift, span);
                    stability += shifted;
                    shifts++;
                }
                stability = shifts == 0 ? 1.0 : stability / shifts;
                score *= 0.7 + 0.3 * stability;
                if (score < opt.WarpGate) continue;

                candidates.Add(new PassageCandidate
                {
                    Phrase = fits[0].phrase,
                    ClassId = fits[0].classId,
                    WordStart = w0,
                    WindowWords = span,
                    Reference = corpus.ReferenceAt(w0),
                    Snippet = corpus.SnippetAt(w0, 28),
                    WarpScore = JsonIo.Round(score, 4),
                    Combined = JsonIo.Round(score, 4),
                    Method = "window-fingerprint",
                    Mappings = mappings,
                    Evidence = new List<string>
                    {
                        $"span {span} words; {fits.Count} classes fit a phrase with the same occurrence count and spacing (stability {stability:0.00})",
                    }.Concat(fits.OrderByDescending(f => f.score).Take(5)
                        .Select(f => $"{f.classId} -> \"{f.phrase}\" (fit {f.score:0.00})"))
                     .ToList(),
                });
            }
        }

        var ordered = candidates.OrderByDescending(c => c.WarpScore).ToList();
        var kept = new List<PassageCandidate>();
        foreach (var c in ordered)
        {
            if (kept.Count >= opt.PassageTake) break;
            if (kept.Any(k => Math.Abs(k.WordStart - c.WordStart) < Math.Min(k.WindowWords, c.WindowWords) * 0.5)) continue;
            kept.Add(c);
        }
        return kept;
    }

    /// <summary>
    /// Re-scores a fixed class->phrase assignment at a shifted window. Used as a stability check: real
    /// alignments persist under small window shifts, chance coincidences do not.
    /// </summary>
    private static double ScoreMappings(
        GlyphStream stream,
        Dictionary<string, List<int>> phrasePositions,
        Dictionary<string, string> mappings,
        int w0,
        int span)
    {
        if (w0 < 0) return 0;
        var byClass = stream.PositionsByClass();
        var n = Math.Max(1, stream.Length - 1);
        double sum = 0;
        var count = 0;
        foreach (var (classId, phrase) in mappings)
        {
            if (!byClass.TryGetValue(classId, out var glyphPositions)) continue;
            if (!phrasePositions.TryGetValue(phrase, out var occ)) continue;
            double errSum = 0;
            foreach (var g in glyphPositions)
            {
                var projected = w0 + (double)g / n * (span - 1);
                var nearest = Nearest(occ, projected);
                var delta = Math.Abs(projected - nearest) / span;
                errSum += Math.Min(1.0, delta * 4);
            }
            sum += 1 - errSum / Math.Max(1, glyphPositions.Count);
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }

    /// <summary>Positions rescaled to [0,1] across the given range.</summary>
    private static List<double> NormalizePositions(List<int> positions, int lo, int hi)
    {
        var span = Math.Max(1e-9, hi - lo);
        return positions.Select(p => (p - lo) / span).ToList();
    }

    /// <summary>A window is inside the focus when its centre falls in a focused region.</summary>
    private static bool InFocus(List<(int Start, int End)> regions, int w0, int w1)
    {
        var centre = (w0 + w1) / 2;
        return regions.Any(r => centre >= r.Start && centre <= r.End);
    }

    private static int CountInRange(List<int> sorted, int lo, int hi)
    {
        var a = LowerBound(sorted, lo);
        var b = LowerBound(sorted, hi);
        return Math.Max(0, b - a);
    }

    private static int LowerBound(List<int> sorted, int value)
    {
        var lo = 0;
        var hi = sorted.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int Nearest(List<int> sorted, double value)
    {
        var lo = 0;
        var hi = sorted.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        if (lo > 0 && Math.Abs(sorted[lo - 1] - value) < Math.Abs(sorted[lo] - value)) return sorted[lo - 1];
        return sorted[lo];
    }

    // ---------------------------------------------------------------- scanner D

    public static List<AnchorFinding> ScanAnchors(GlyphStream stream, CorpusIndex corpus, MatchOptions opt, Action<string>? log = null)
    {
        var findings = new List<AnchorFinding>();
        var byClass = stream.PositionsByClass();
        var window = Math.Max(40, opt.AnchorWindow);

        var conceptPositions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var concept in opt.AnchorConcepts)
        {
            var variants = concept.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var positions = new List<int>();
            foreach (var v in variants)
            {
                if (corpus.UnigramCounts.ContainsKey(v)) positions.AddRange(corpus.PositionsOf(v));
            }
            conceptPositions[concept] = positions;
            if (positions.Count == 0) continue;
            positions.Sort();

            var best = new Dictionary<int, double>();
            for (var w0 = 0; w0 + window <= corpus.Length; w0 += window / 4)
            {
                var count = CountInRange(positions, w0, w0 + window);
                if (count == 0) continue;
                best[w0] = (double)count / window;
            }
            foreach (var (w0, density) in best.OrderByDescending(kv => kv.Value).Take(3))
            {
                findings.Add(new AnchorFinding
                {
                    Concept = concept,
                    WordStart = w0,
                    Reference = corpus.ReferenceAt(w0),
                    Density = JsonIo.Round(density, 5),
                    Snippet = corpus.SnippetAt(w0, 18),
                });
            }
        }

        // Which classes cluster near each concept's hits? Enrichment = observed/near-chance.
        foreach (var finding in findings)
        {
            var hitPositions = conceptPositions.TryGetValue(finding.Concept, out var cachedHits)
                ? cachedHits
                : new List<int>();
            if (hitPositions.Count == 0) continue;
            var lo = finding.WordStart;
            var hi = finding.WordStart + window;
            var localHits = hitPositions.Where(p => p >= lo && p < hi).ToList();
            if (localHits.Count == 0) continue;

            foreach (var (classId, glyphPositions) in byClass.Where(kv => kv.Value.Count >= 2))
            {
                var projected = glyphPositions.Select(g => (double)g / Math.Max(1, stream.Length - 1)).ToList();
                var hits = projected.Count(p => localHits.Any(h => Math.Abs(h - (lo + p * (hi - lo))) < window * 0.08));
                var expected = glyphPositions.Count * (double)localHits.Count / window * window * 0.16;
                if (hits >= 2 && hits > expected * 1.5) finding.EnrichedClasses.Add(classId);
            }
            if (finding.EnrichedClasses.Count > 3) finding.EnrichedClasses = finding.EnrichedClasses.Take(3).ToList();
        }

        return findings;
    }

    /// <summary>
    /// The statistic used by the significance harness: the best multi-constraint passage score. Scanner A's
    /// single-pattern score is deliberately excluded because it saturates at 1.0 whenever any short pattern
    /// finds a matching window, in real and shuffled streams alike.
    /// </summary>
    public static double BestScore(MatchResult result) => result.Passages.FirstOrDefault()?.Combined ?? 0;
}

/// <summary>
/// Bounded many-to-many alignment: one glyph may stand for up to <c>maxExpand</c> words (logographic) and
/// up to <c>maxGlyphsPerWord</c> glyphs may spell one word (alphabetic). Affine gap costs penalise
/// unexplained material on either side.
/// </summary>
public static class GappedAligner
{
    public static (double score, List<AlignRow> rows) Align(
        GlyphStream stream,
        CorpusIndex corpus,
        int wordStart,
        int windowWords,
        Dictionary<string, HashSet<string>> dictionary,
        double gapCost = 0.45,
        int maxExpand = 10,
        int maxGlyphsPerWord = 3)
    {
        var n = stream.Length;
        var m = Math.Min(windowWords, corpus.Length - wordStart);
        if (n == 0 || m <= 0) return (0, new List<AlignRow>());

        var words = corpus.Tokens.AsSpan(wordStart, m);
        var dp = new double[n + 1, m + 1];
        var back = new byte[n + 1, m + 1];
        for (var i = 1; i <= n; i++) { dp[i, 0] = dp[i - 1, 0] - gapCost; back[i, 0] = 1; }
        for (var j = 1; j <= m; j++) { dp[0, j] = dp[0, j - 1] - gapCost; back[0, j] = 2; }

        for (var i = 1; i <= n; i++)
        {
            var mapped = dictionary.TryGetValue(stream.ClassIds[i - 1], out var set) ? set : null;
            for (var j = 1; j <= m; j++)
            {
                // glyph -> glyph (deletion)
                var best = dp[i - 1, j] - gapCost;
                var move = (byte)1;

                // word -> word (insertion)
                if (dp[i, j - 1] - gapCost > best) { best = dp[i, j - 1] - gapCost; move = 2; }

                // one glyph -> one word
                var s1 = dp[i - 1, j - 1] + MatchScore(mapped, words, j - 1, 1);
                if (s1 > best) { best = s1; move = 3; }

                // one glyph -> several words (logographic expansion)
                for (var k = 2; k <= maxExpand && k <= j; k++)
                {
                    var s = dp[i - 1, j - k] + MatchScore(mapped, words, j - k, k) - 0.05 * (k - 1);
                    if (s > best) { best = s; move = (byte)(10 + k); }
                }

                // several glyphs -> one word (alphabetic spelling)
                for (var k = 2; k <= maxGlyphsPerWord && k <= i; k++)
                {
                    var anyMapped = false;
                    var covered = 0.0;
                    for (var g = 0; g < k; g++)
                    {
                        var cls = stream.ClassIds[i - k + g];
                        var set2 = dictionary.TryGetValue(cls, out var s2) ? s2 : null;
                        if (set2 is null) continue;
                        anyMapped = true;
                        if (set2.Contains(words[j - 1].ToString())) covered = 1.0;
                    }
                    if (!anyMapped) continue;
                    var s = dp[i - k, j - 1] + 0.6 * covered - 0.05 * (k - 1);
                    if (s > best) { best = s; move = (byte)(20 + k); }
                }

                dp[i, j] = best;
                back[i, j] = move;
            }
        }

        // traceback
        var rows = new List<AlignRow>();
        int ti = n, tj = m;
        while (ti > 0 || tj > 0)
        {
            var mv = back[ti, tj];
            if (mv == 0) break;
            if (mv == 1) { rows.Add(new AlignRow { GlyphIndex = ti - 1, ClassId = stream.ClassIds[ti - 1], WordStart = -1, WordCount = 0, Match = 0 }); ti--; }
            else if (mv == 2) { tj--; }
            else if (mv == 3)
            {
                var mapped = dictionary.TryGetValue(stream.ClassIds[ti - 1], out var set4) ? set4 : null;
                var text = words[tj - 1].ToString();
                rows.Add(new AlignRow
                {
                    GlyphIndex = ti - 1,
                    ClassId = stream.ClassIds[ti - 1],
                    WordStart = wordStart + tj - 1,
                    WordCount = 1,
                    Words = text,
                    Match = mapped is not null && mapped.Contains(text) ? 1 : 0,
                });
                ti--; tj--;
            }
            else if (mv >= 12 && mv <= 10 + maxExpand)
            {
                var k = mv - 10;
                var ws = tj - k;
                var text = string.Join(' ', words.Slice(ws, k).ToArray());
                var score = MatchScore(dictionary.TryGetValue(stream.ClassIds[ti - 1], out var set3) ? set3 : null, words, ws, k);
                rows.Add(new AlignRow { GlyphIndex = ti - 1, ClassId = stream.ClassIds[ti - 1], WordStart = wordStart + ws, WordCount = k, Words = text, Match = score > 0 ? 1 : 0 });
                ti--; tj -= k;
            }
            else
            {
                var k = mv - 20;
                if (k <= 0) break;
                var text = string.Join(' ', words.Slice(tj - 1, 1).ToArray());
                for (var g = 0; g < k; g++)
                {
                    rows.Add(new AlignRow
                    {
                        GlyphIndex = ti - k + g,
                        ClassId = stream.ClassIds[ti - k + g],
                        WordStart = wordStart + tj - 1,
                        WordCount = 1,
                        Words = g == 0 ? text : "",
                        Match = 0.5,
                    });
                }
                ti -= k; tj--;
            }
        }
        rows.Reverse();

        // Report the share of the shorter sequence that is explained by an exact glyph->phrase match. A plain
        // DP-score normalisation collapses to zero as soon as the dictionary is partial, which is the normal
        // state of a decipherment in progress.
        var coveredWords = rows.Where(r => r.Match >= 0.85).Sum(r => Math.Max(1, r.WordCount));
        var maxPossible = Math.Max(1, Math.Min(n, m));
        var normalized = Math.Clamp(coveredWords / (double)maxPossible, 0, 1);
        return (normalized, rows);
    }

    /// <summary>
    /// Score of emitting a word span for a glyph whose dictionary entry is a phrase. Exact match scores
    /// 1.0; a span that is a prefix/suffix of a known phrase scores 0.85 (the phrase boundary inside the
    /// document is not known); a span contained in a phrase scores 0.6.
    /// </summary>
    private static double MatchScore(HashSet<string>? mapped, ReadOnlySpan<string> words, int start, int count)
    {
        if (mapped is null) return 0;
        var span = string.Join(' ', words.Slice(start, count).ToArray());
        if (mapped.Contains(span)) return 1.0;
        if (mapped.Any(m => m.StartsWith(span + " ", StringComparison.Ordinal) ||
                            m.EndsWith(" " + span, StringComparison.Ordinal))) return 0.85;
        if (mapped.Any(m => m.Contains(span, StringComparison.Ordinal))) return 0.6;
        return 0;
    }
}
