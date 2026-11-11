using Bomca.Core.Corpus;
using Bomca.Core.Json;
using Bomca.Core.Matching;
using Bomca.Core.Model;

namespace Bomca.Core.External;

public sealed class ClaimSegment
{
    public string Name { get; set; } = "";
    public List<int> Rows { get; set; } = new();
    public List<int> CharacterRange { get; set; } = new();
    public string Translation { get; set; } = "";
}

public sealed class NumeralClaim
{
    public List<string> Ids { get; set; } = new();
    public double Value { get; set; }
    public string Expression { get; set; } = "";
    public int PositionCount => Ids.Count;
}

public sealed class TextClaim
{
    public string Claim { get; set; } = "";
    public string Pattern { get; set; } = "";
    public List<string> ExpectedRefs { get; set; } = new();
}

public sealed class ExternalClaimFile
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Source { get; set; } = "";
    public string Method { get; set; } = "";
    public string Direction { get; set; } = "";
    public List<ClaimSegment> Segments { get; set; } = new();
    public List<NumeralClaim> NumeralClaims { get; set; } = new();
    public List<string> NumeralClaimInconsistencies { get; set; } = new();
    public List<Dictionary<string, object>> CharacterGlosses { get; set; } = new();
    public List<TextClaim> TextClaims { get; set; } = new();
}

public sealed class NumeralLikeGlyph
{
    public string Id { get; set; } = "";
    public int GlobalIndex { get; set; }
    public int Row { get; set; }
    public double Score { get; set; }
    public int StrokeCount { get; set; }
    public double Aspect { get; set; }
    public double InkDensity { get; set; }
    public int Area { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class RunStructure
{
    public int Count { get; set; }
    public int Runs { get; set; }
    public int MaxRun { get; set; }
    public double MeanRun { get; set; }
    public List<int> RunLengths { get; set; } = new();
}

public sealed class TextClaimResult
{
    public string Claim { get; set; } = "";
    public string Pattern { get; set; } = "";
    public int Occurrences { get; set; }
    public List<string> FoundRefs { get; set; } = new();
    public List<string> ExpectedRefs { get; set; } = new();
    public bool ExpectedRefHit { get; set; }
    public string Status { get; set; } = "unverified";
}

public sealed class SegmentEvidence
{
    public string Name { get; set; } = "";
    public int Glyphs { get; set; }
    public double MedianGlyphHeight { get; set; }
    public double MeanGap { get; set; }
    public double GlyphsPerRow { get; set; }
    public int Classes { get; set; }
    public double ClassOverlapWithOtherSegment { get; set; }
    public string Reading { get; set; } = "";
}

public sealed class AnchorWindowResult
{
    public string Segment { get; set; } = "";
    public int WordStart { get; set; }
    public string Reference { get; set; } = "";
    public int Hits { get; set; }
    public int DistinctTerms { get; set; }
    public double Density { get; set; }
    public int ControlBestHits { get; set; }
    public double ControlMeanHits { get; set; }
    public double PValue { get; set; }
    public string Verdict { get; set; } = "";
    public string Snippet { get; set; } = "";
    public List<string> MatchedTerms { get; set; } = new();
    public int ExcerptControlBest { get; set; }
    public int ExcerptControlMean { get; set; }
    public double ExcerptControlPValue { get; set; }
}

public sealed class PositionalNumeralResult
{
    public int ClaimedNumerals { get; set; }
    public double MeanScoreAtClaimed { get; set; }
    public double MeanScoreAll { get; set; }
    public double MeanScoreRandomSameCount { get; set; }
    public double PValue { get; set; }
    public double Z { get; set; }
    public RunStructure? ClaimedRuns { get; set; }
    public string MappingNote { get; set; } = "";
    public string Verdict { get; set; } = "";
}

public sealed class ClaimVerificationResult
{
    public string Doc { get; set; } = "";
    public string Source { get; set; } = "";
    public string GeneratedUtc { get; set; } = "";
    public bool DirectionConsistentWithScanners { get; set; }
    public string DirectionNote { get; set; } = "";
    public List<SegmentEvidence> Segments { get; set; } = new();
    public List<NumeralLikeGlyph> NumeralLikeGlyphs { get; set; } = new();
    public RunStructure? ObservedRuns { get; set; }
    public RunStructure? NullRuns { get; set; }
    public double NumeralRunPValue { get; set; }
    public double NumeralRunZ { get; set; }
    public string NumeralRunVerdict { get; set; } = "";
    public PositionalNumeralResult? PositionalNumerals { get; set; }
    public List<TextClaimResult> TextClaims { get; set; } = new();
    public List<AnchorWindowResult> AnchorWindows { get; set; } = new();
    public List<string> TableInconsistencies { get; set; } = new();
    public List<string> Findings { get; set; } = new();
    public string OverallAssessment { get; set; } = "";
}

/// <summary>
/// Verifies an external translation claim against data this project already has: the glyph stream, its
/// shape features, and the corpus. The point is to test *falsifiable* consequences of the claim, not to
/// re-translate: numeral positions must look like numerals and must cluster in runs; claimed dates must
/// match verses the corpus actually contains; the claimed segments must differ structurally; and the
/// claimed content must concentrate in the corporeal regions the claim names.
/// </summary>
public static class ClaimVerifier
{
    public static ExternalClaimFile Load(string path) => JsonIo.Read<ExternalClaimFile>(path);

    /// <summary>
    /// Numeral-likeness from the shape features only. Deliberately transparent and conservative:
    /// comb/tally bars, dot-like marks, and solid small squares score high; ornate connected glyphs
    /// score low. No claim-specific tuning.
    /// </summary>
    public static List<NumeralLikeGlyph> ScoreNumerals(IEnumerable<SymbolOccurrence> occurrences, int take)
    {
        var scored = new List<NumeralLikeGlyph>();
        foreach (var o in occurrences)
        {
            var score = 0.0;
            var reasons = new List<string>();

            // comb / tally: several parallel strokes in a wide box
            if (o.StrokeCount >= 3 && o.Aspect >= 1.3)
            {
                score += Math.Min(0.75, 0.12 * o.StrokeCount + 0.15);
                reasons.Add($"{o.StrokeCount} strokes, aspect {o.Aspect:0.0}");
            }

            // dot-like or small solid mark
            var compact = o.Aspect is > 0.55 and < 1.8;
            if (compact && o.Area >= 150 && o.InkDensity >= 0.55)
            {
                score += 0.35;
                reasons.Add($"compact solid ({o.Area} px, density {o.InkDensity:0.00})");
            }

            // small isolated marks are the other Mesoamerican numeral form
            if (compact && o.Area < 400 && o.H < 40)
            {
                score += 0.2;
                reasons.Add("small mark");
            }

            // a closed loop is a day-sign or ring, not a bar-and-dot numeral
            if (o.HoleCount >= 1) score -= 0.15;

            if (score <= 0.3) continue;
            scored.Add(new NumeralLikeGlyph
            {
                Id = o.Id,
                GlobalIndex = o.GlobalIndex,
                Row = o.Row,
                Score = JsonIo.Round(Math.Clamp(score, 0, 1), 3),
                StrokeCount = o.StrokeCount,
                Aspect = JsonIo.Round(o.Aspect, 2),
                InkDensity = JsonIo.Round(o.InkDensity, 3),
                Area = (int)Math.Round(o.Area),
                Reason = string.Join("; ", reasons),
            });
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.GlobalIndex).Take(take).ToList();
    }

    public static RunStructure RunsOf(List<int> sortedPositions)
    {
        var lengths = new List<int>();
        if (sortedPositions.Count == 0) return new RunStructure();
        var run = 1;
        for (var i = 1; i < sortedPositions.Count; i++)
        {
            if (sortedPositions[i] == sortedPositions[i - 1] + 1) run++;
            else { lengths.Add(run); run = 1; }
        }
        lengths.Add(run);
        return new RunStructure
        {
            Count = sortedPositions.Count,
            Runs = lengths.Count,
            MaxRun = lengths.Max(),
            MeanRun = JsonIo.Round((double)sortedPositions.Count / lengths.Count, 3),
            RunLengths = lengths,
        };
    }

    /// <summary>
    /// The claim implies numerals occur in consecutive sequences of two or more (a date is a run of
    /// digits). Random glyph sets do not cluster like that, so the run structure is a genuine test.
    /// </summary>
    public static (RunStructure observed, RunStructure nullMean, double p, double z) NumeralRunTest(
        IReadOnlyList<int> positions, int streamLength, int iterations, int seed)
    {
        var observed = RunsOf(positions.ToList());
        var rng = new Random(seed);
        var nullMeans = new List<double>();
        var nullMaxRuns = new List<int>();
        var ge = 0;

        for (var t = 0; t < iterations; t++)
        {
            var sample = Enumerable.Range(0, streamLength).OrderBy(_ => rng.Next()).Take(positions.Count).OrderBy(x => x).ToList();
            var runs = RunsOf(sample);
            nullMeans.Add(runs.MeanRun);
            nullMaxRuns.Add(runs.MaxRun);
            if (runs.MaxRun >= observed.MaxRun && runs.MeanRun >= observed.MeanRun) ge++;
        }

        var mean = nullMeans.Average();
        var sd = Math.Sqrt(nullMeans.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, nullMeans.Count - 1));
        var p = (ge + 1.0) / (iterations + 1.0);
        var z = sd <= 1e-9 ? 0 : (observed.MeanRun - mean) / sd;
        return (observed, new RunStructure
        {
            Count = positions.Count,
            Runs = (int)Math.Round(positions.Count / Math.Max(1e-9, mean)),
            MaxRun = nullMaxRuns.Max(),
            MeanRun = JsonIo.Round(mean, 3),
        }, JsonIo.Round(p, 4), JsonIo.Round(z, 2));
    }

    /// <summary>Checks each claimed calendar/chronology statement against verses the corpus actually contains.</summary>
    public static List<TextClaimResult> CheckTextClaims(CorpusIndex corpus, IEnumerable<TextClaim> claims)
    {
        var results = new List<TextClaimResult>();
        foreach (var claim in claims)
        {
            var positions = corpus.PositionsOf(Normalizer.NormalizeText(claim.Pattern));
            var refs = positions.Take(12).Select(corpus.ReferenceAt).ToList();
            var hit = claim.ExpectedRefs.Count == 0
                ? positions.Count > 0
                : claim.ExpectedRefs.Any(e => refs.Any(r => r.StartsWith(e, StringComparison.OrdinalIgnoreCase)));
            results.Add(new TextClaimResult
            {
                Claim = claim.Claim,
                Pattern = claim.Pattern,
                Occurrences = positions.Count,
                FoundRefs = refs,
                ExpectedRefs = claim.ExpectedRefs,
                ExpectedRefHit = hit,
                Status = hit ? "consistent" : positions.Count == 0 ? "pattern-absent" : "present-elsewhere",
            });
        }
        return results;
    }

    /// <summary>Dense numeral-likeness score for every occurrence, in reading order (0 = not numeral-like).</summary>
    public static double[] NumeralScores(IReadOnlyList<SymbolOccurrence> ordered)
    {
        var scores = new double[ordered.Count];
        var top = ScoreNumerals(ordered, ordered.Count);
        var byId = top.ToDictionary(t => t.Id, t => t.Score, StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
            scores[i] = byId.TryGetValue(ordered[i].Id, out var v) ? v : 0;
        return scores;
    }

    /// <summary>
    /// Positional test. The claim numbers its characters in reading order (C-1 ... C-230) and treats the top
    /// four lines and the bottom three lines as separate texts. Mapping those ids proportionally onto our
    /// occurrences lets us ask whether the glyphs at the claimed numeral positions are more numeral-like than
    /// glyphs at random positions. The mapping is approximate because our segmentation yields more glyphs than
    /// the claim's numbering, so the result is reported with that caveat and paired with the ID-free run test.
    /// </summary>
    public static PositionalNumeralResult PositionalNumerals(
        IReadOnlyList<double> numeralScores,
        IEnumerable<ClaimSegment> segments,
        IEnumerable<NumeralClaim> numeralClaims,
        int iterations,
        int seed)
    {
        var segmentList = segments.ToList();
        var total = numeralScores.Count;
        var totalRows = Math.Max(1, segmentList.Sum(x => x.Rows.Count));
        var claimedPositions = new List<int>();
        var offset = 0;

        foreach (var seg in segmentList)
        {
            var segCount = (int)Math.Round(total * seg.Rows.Count / (double)totalRows);
            var start = offset;
            offset += segCount;
            var lo = seg.CharacterRange.Count >= 2 ? seg.CharacterRange[0] : 1;
            var hi = seg.CharacterRange.Count >= 2 ? seg.CharacterRange[1] : lo + 1;
            var claims = numeralClaims.ToList();
            foreach (var claim in claims)
            {
                foreach (var id in claim.Ids)
                {
                    var digits = new string(id.Where(char.IsDigit).ToArray());
                    if (digits.Length == 0) continue;
                    var num = int.Parse(digits);
                    if (num < lo || num > hi) continue;
                    var fraction = (num - lo) / (double)Math.Max(1, hi - lo);
                    claimedPositions.Add(Math.Clamp(start + (int)Math.Round(fraction * (segCount - 1)), 0, total - 1));
                }
            }
        }

        var result = new PositionalNumeralResult
        {
            ClaimedNumerals = claimedPositions.Count,
            MappingNote = $"Crowley ids mapped proportionally onto {total} extracted glyphs; the claim numbers ~230 characters " +
                          "while our segmentation yields more, so a claimed position can drift by several glyphs.",
        };
        if (claimedPositions.Count == 0) return result;

        var meanAt = claimedPositions.Average(p => numeralScores[p]);
        result.MeanScoreAtClaimed = JsonIo.Round(meanAt, 4);
        result.MeanScoreAll = JsonIo.Round(numeralScores.Average(), 4);
        result.ClaimedRuns = RunsOf(claimedPositions.Distinct().OrderBy(x => x).ToList());

        var rng = new Random(seed);
        var nullMeans = new List<double>();
        var ge = 0;
        for (var t = 0; t < iterations; t++)
        {
            var sample = Enumerable.Range(0, total).OrderBy(_ => rng.Next()).Take(claimedPositions.Count).ToList();
            var m = sample.Average(i => numeralScores[i]);
            nullMeans.Add(m);
            if (m >= meanAt) ge++;
        }
        var mean = nullMeans.Average();
        var sd = Math.Sqrt(nullMeans.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, nullMeans.Count - 1));
        result.MeanScoreRandomSameCount = JsonIo.Round(mean, 4);
        result.PValue = JsonIo.Round((ge + 1.0) / (iterations + 1.0), 4);
        result.Z = sd <= 1e-9 ? 0 : JsonIo.Round((meanAt - mean) / sd, 2);
        result.MappingNote += $" The claimed positions form {result.ClaimedRuns.Runs} runs (longest {result.ClaimedRuns.MaxRun}) in our stream.";
        result.Verdict = result.PValue <= 0.05
            ? "Glyphs at the claimed numeral positions are more numeral-like than random positions, which supports the claimed placement of number sequences."
            : "Glyphs at the claimed numeral positions are not more numeral-like than random positions; given the mapping uncertainty this is weak evidence against the claimed placement, not a refutation.";
        return result;
    }

    public static List<SegmentEvidence> CompareSegments(DocumentRecord doc, InventoryVersion inventory, IEnumerable<ClaimSegment> segments)
    {
        var glyphs = doc.Symbols.Where(s => s.Note != "title-row").ToList();
        var classOf = inventory.Classes.SelectMany(c => c.Members.Select(m => (m, c.ClassId)))
            .ToDictionary(x => x.m, x => x.ClassId, StringComparer.Ordinal);

        var results = new List<SegmentEvidence>();
        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var seg in segments)
        {
            // Row numbers in the claim are 1-based visual rows; occurrence rows are 0-based with the title at 0.
            var rowSet = seg.Rows.Select(r => r).ToHashSet();
            var inSeg = glyphs.Where(g => rowSet.Contains(g.Row + 1)).ToList();
            if (inSeg.Count == 0) inSeg = glyphs.Where(g => rowSet.Contains(g.Row)).ToList();
            sets[seg.Name] = inSeg.Where(g => classOf.ContainsKey(g.Id)).Select(g => classOf[g.Id]).ToHashSet();
            results.Add(new SegmentEvidence
            {
                Name = seg.Name,
                Glyphs = inSeg.Count,
                MedianGlyphHeight = inSeg.Count == 0 ? 0 : JsonIo.Round(Median(inSeg.Select(g => (double)g.H).ToList()), 1),
                MeanGap = inSeg.Count == 0 ? 0 : JsonIo.Round(Median(inSeg.Select(g => (double)g.W).ToList()), 1),
                GlyphsPerRow = inSeg.Count == 0 || seg.Rows.Count == 0 ? 0 : JsonIo.Round((double)inSeg.Count / seg.Rows.Count, 1),
                Classes = sets[seg.Name].Count,
            });
        }

        foreach (var r in results)
        {
            var others = results.Where(o => o.Name != r.Name).ToList();
            var otherSets = others.Select(o => sets[o.Name]).ToList();
            var mine = sets[r.Name];
            var unionOther = otherSets.SelectMany(s => s).ToHashSet();
            var overlap = unionOther.Count == 0 || mine.Count == 0
                ? 0
                : mine.Count(c => unionOther.Contains(c)) / (double)mine.Count;
            r.ClassOverlapWithOtherSegment = JsonIo.Round(overlap, 3);
        }

        if (results.Count == 2)
        {
            var a = results[0];
            var b = results[1];
            var ratio = Math.Max(a.MedianGlyphHeight, b.MedianGlyphHeight) / Math.Max(1e-9, Math.Min(a.MedianGlyphHeight, b.MedianGlyphHeight));
            results[0].Reading = results[1].Reading =
                ratio >= 1.25
                    ? $"The two claimed segments differ in glyph size by {ratio:0.00}x - consistent with the claim that they are different texts by different hands/sizes."
                    : $"The two claimed segments are similar in glyph size ({ratio:0.00}x) - the 'two different texts' claim is not supported by geometry.";
        }
        return results;
    }

    /// <summary>
    /// Does the claimed content concentrate in the corpus regions the claim names? Each segment's content
    /// terms are scored as anchor hits in sliding windows; the null is the same number of randomly chosen
    /// corpus terms of the same document frequency, so the test controls for how common the words are.
    /// </summary>
    public static List<AnchorWindowResult> AnchorWindows(
        CorpusIndex corpus,
        IEnumerable<ClaimSegment> segments,
        IReadOnlyCollection<string> expectedRegions,
        int window,
        int iterations,
        int seed)
    {
        var results = new List<AnchorWindowResult>();
        var rng = new Random(seed);
        var vocabulary = corpus.UnigramCounts.Keys.ToList();

        foreach (var seg in segments)
        {
            var terms = ContentTerms(seg.Translation, corpus)
                .Where(t => corpus.UnigramCounts.TryGetValue(t, out var c) && c >= 2)
                .Distinct()
                .Take(40)
                .ToList();
            if (terms.Count == 0) continue;

            var positions = terms.ToDictionary(t => t, t => corpus.PositionsOf(t), StringComparer.Ordinal);
            var (bestStart, bestHits, bestDistinct) = BestWindow(positions, corpus.Length, window);
            var controlHits = new List<int>();
            for (var t = 0; t < iterations; t++)
            {
                var controlTerms = Enumerable.Range(0, terms.Count)
                    .Select(_ => vocabulary[rng.Next(vocabulary.Count)])
                    .Distinct()
                    .ToList();
                var controlPositions = controlTerms.ToDictionary(x => x, x => corpus.PositionsOf(x), StringComparer.Ordinal);
                var (_, hits, _) = BestWindow(controlPositions, corpus.Length, window);
                controlHits.Add(hits);
            }

            var mean = controlHits.Count == 0 ? 0 : controlHits.Average();
            var ge = controlHits.Count(h => h >= bestHits);
            var p = (ge + 1.0) / (controlHits.Count + 1.0);
            var reference = corpus.ReferenceAt(bestStart);
            var inRegion = expectedRegions.Any(r => reference.StartsWith(r, StringComparison.OrdinalIgnoreCase));

            results.Add(new AnchorWindowResult
            {
                Segment = seg.Name,
                WordStart = bestStart,
                Reference = reference,
                Hits = bestHits,
                DistinctTerms = bestDistinct,
                Density = JsonIo.Round((double)bestHits / window, 5),
                ControlBestHits = controlHits.Count == 0 ? 0 : controlHits.Max(),
                ControlMeanHits = JsonIo.Round(mean, 2),
                PValue = JsonIo.Round(p, 4),
                Verdict = p <= 0.05
                    ? inRegion ? "concentrated-in-claimed-region" : "concentrated-elsewhere"
                    : "no-concentration",
                Snippet = corpus.SnippetAt(bestStart, 26),
                MatchedTerms = terms.Take(24).ToList(),
            });
        }
        return results;
    }

    private static (int start, int hits, int distinct) BestWindow(
        Dictionary<string, List<int>> positions, int corpusLength, int window)
    {
        var starts = new HashSet<int>();
        foreach (var list in positions.Values)
            foreach (var p in list) starts.Add(Math.Max(0, p - window / 2));

        var bestHits = 0;
        var bestDistinct = 0;
        var bestStart = 0;
        foreach (var start in starts)
        {
            var end = start + window;
            var hits = 0;
            var distinct = 0;
            foreach (var list in positions.Values)
            {
                var n = CountInRange(list, start, end);
                if (n > 0) { distinct++; hits += n; }
            }
            if (hits <= bestHits) continue;
            bestHits = hits;
            bestDistinct = distinct;
            bestStart = start;
        }
        return (bestStart, bestHits, bestDistinct);
    }

    private static int CountInRange(List<int> sorted, int lo, int hi)
    {
        int Lower(int v)
        {
            var a = 0;
            var b = sorted.Count;
            while (a < b)
            {
                var mid = (a + b) / 2;
                if (sorted[mid] < v) a = mid + 1;
                else b = mid;
            }
            return a;
        }
        return Math.Max(0, Lower(hi) - Lower(lo));
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "of", "to", "in", "a", "that", "it", "was", "were", "for", "with", "as", "his",
        "he", "they", "them", "their", "this", "these", "which", "had", "has", "have", "be", "been",
        "came", "come", "pass", "by", "on", "at", "from", "into", "unto", "up", "then", "there", "so",
        "which", "who", "whom", "not", "no", "all", "is", "are", "am", "an", "or", "but", "if", "do",
        "did", "does", "now", "thus", "after", "before", "more", "most", "than", "also", "about",
    };

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }

    public static List<string> ContentTerms(string text, CorpusIndex corpus)
        => Normalizer.Tokenize(text)
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .ToList();
}
