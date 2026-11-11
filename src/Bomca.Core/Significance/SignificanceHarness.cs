using Bomca.Core.Corpus;
using Bomca.Core.Json;
using Bomca.Core.Matching;

namespace Bomca.Core.Significance;

public sealed class NullScore
{
    public int Index { get; set; }
    public double Fingerprint { get; set; }
    public double Passage { get; set; }
    public double Best { get; set; }
}

public sealed class HypothesisTest
{
    public string ClassId { get; set; } = "";
    public string Phrase { get; set; } = "";
    public double Score { get; set; }
    public double PValue { get; set; }
    public double QValue { get; set; }
    public bool Significant { get; set; }
}

public sealed class HeldOutResult
{
    public string ClassId { get; set; } = "";
    public string Phrase { get; set; } = "";
    public int DiscoveryHalfGlyphs { get; set; }
    public int TestHalfGlyphs { get; set; }
    public double DiscoveryScore { get; set; }
    public double TestScore { get; set; }
    public double TestNullMean { get; set; }
    public double TestNullSd { get; set; }
    public double TestPValue { get; set; }
    public bool Supported { get; set; }
    public string Note { get; set; } = "";
}

public sealed class SignificanceReport
{
    public string Doc { get; set; } = "";
    public string Edition { get; set; } = "";
    public string GeneratedUtc { get; set; } = "";
    public int Iterations { get; set; }
    public int Seed { get; set; }
    public double ObservedBest { get; set; }
    public double ObservedFingerprint { get; set; }
    public double ObservedPassage { get; set; }
    public double NullMean { get; set; }
    public double NullSd { get; set; }
    public double NullMax { get; set; }
    public double Z { get; set; }
    public double PValue { get; set; }
    public int HypothesesTested { get; set; }
    public double Alpha { get; set; }
    public int SignificantHypotheses { get; set; }
    public List<HypothesisTest> Hypotheses { get; set; } = new();
    public List<NullScore> Nulls { get; set; } = new();
    public HeldOutResult? HeldOut { get; set; }
    public List<string> Verdict { get; set; } = new();
}

public static class SignificanceHarness
{
    public static SignificanceReport Run(
        GlyphStream stream,
        CorpusIndex corpus,
        MatchOptions opt,
        int iterations = 200,
        int seed = 20260101,
        double alpha = 0.01,
        Action<string>? log = null)
    {
        var report = new SignificanceReport
        {
            Doc = stream.Doc,
            Edition = corpus.Edition,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Iterations = iterations,
            Seed = seed,
            Alpha = alpha,
        };

        log?.Invoke($"significance: observed run over {stream.Length} glyphs");
        var phrases = MatchEngine.CandidatePhrases(corpus, opt);
        var positions = phrases.ToDictionary(p => p.Phrase, p => corpus.PositionsOf(p.Phrase), StringComparer.Ordinal);
        var observed = MatchEngine.Run(stream, corpus, opt, null, positions);
        report.ObservedFingerprint = observed.Fingerprints.FirstOrDefault()?.Score ?? 0;
        report.ObservedPassage = observed.Passages.FirstOrDefault()?.Combined ?? 0;
        report.ObservedBest = MatchEngine.BestScore(observed);

        var nullOpt = new MatchOptions
        {
            MinOccurrences = opt.MinOccurrences,
            MaxHypotheses = opt.MaxHypotheses,
            MaxPhrases = opt.MaxPhrases,
            MaxPhraseLength = opt.MaxPhraseLength,
            JointTopHypotheses = opt.JointTopHypotheses,
            WindowSizes = opt.WindowSizes,
            WarpStride = opt.WarpStride * 2,
            PassageTake = opt.PassageTake,
            RunAlignment = false,
            AnchorConcepts = Array.Empty<string>(),
            AnchorWindow = opt.AnchorWindow,
            Seed = opt.Seed,
        };

        for (var i = 0; i < iterations; i++)
        {
            var shuffled = stream.Shuffle(seed + i);
            var run = MatchEngine.Run(shuffled, corpus, nullOpt, null, positions);
            report.Nulls.Add(new NullScore
            {
                Index = i,
                Fingerprint = JsonIo.Round(run.Fingerprints.FirstOrDefault()?.Score ?? 0, 4),
                Passage = JsonIo.Round(run.Passages.FirstOrDefault()?.WarpScore ?? 0, 4),
                Best = JsonIo.Round(MatchEngine.BestScore(run), 4),
            });
            if ((i + 1) % Math.Max(1, iterations / 10) == 0)
                log?.Invoke($"significance: {i + 1}/{iterations} shuffled runs (running max {report.Nulls.Max(n => n.Best):0.000})");
        }

        var nullScores = report.Nulls.Select(n => n.Best).ToList();
        report.NullMean = JsonIo.Round(nullScores.Average(), 4);
        report.NullSd = JsonIo.Round(Sd(nullScores), 4);
        report.NullMax = JsonIo.Round(nullScores.Max(), 4);
        report.Z = report.NullSd <= 1e-9 ? 0 : JsonIo.Round((report.ObservedBest - report.NullMean) / report.NullSd, 3);
        report.PValue = JsonIo.Round(EmpiricalP(nullScores, report.ObservedBest), 5);

        // Per-hypothesis empirical p-values against the null best-score distribution, then BH-FDR.
        var hypotheses = observed.Fingerprints.Take(50).ToList();
        report.HypothesesTested = hypotheses.Count;
        foreach (var h in hypotheses)
        {
            report.Hypotheses.Add(new HypothesisTest
            {
                ClassId = h.ClassId,
                Phrase = h.Phrase,
                Score = h.Score,
                PValue = JsonIo.Round(EmpiricalP(nullScores, h.Score), 5),
            });
        }
        ApplyBenjaminiHochberg(report.Hypotheses, alpha);
        report.SignificantHypotheses = report.Hypotheses.Count(h => h.Significant);

        report.HeldOut = HeldOut(stream, corpus, observed, opt, seed, alpha, log);

        if (report.PValue <= alpha)
            report.Verdict.Add($"Observed best score {report.ObservedBest:0.000} exceeds {report.Nulls.Count(n => n.Best >= report.ObservedBest)} of {iterations} shuffled streams (p = {report.PValue:0.0000}, z = {report.Z:0.0}).");
        else
            report.Verdict.Add($"Observed best score {report.ObservedBest:0.000} is not distinguishable from shuffled controls (p = {report.PValue:0.0000}, null max {report.NullMax:0.000}).");

        report.Verdict.Add(report.SignificantHypotheses > 0
            ? $"{report.SignificantHypotheses} of {report.HypothesesTested} reported hypotheses survive Benjamini-Hochberg FDR at alpha = {alpha}."
            : $"No hypothesis survives FDR at alpha = {alpha}; treat the ranking as exploratory.");

        if (report.HeldOut is { } ho)
            report.Verdict.Add(ho.Supported
                ? $"Held-out check passed: the discovery hypothesis also scores {ho.TestScore:0.000} on the untouched half (p = {ho.TestPValue:0.0000})."
                : $"Held-out check not passed: test-half score {ho.TestScore:0.000} vs null mean {ho.TestNullMean:0.000} (p = {ho.TestPValue:0.0000}).");

        return report;
    }

    public static double EmpiricalP(List<double> nullScores, double observed)
    {
        if (nullScores.Count == 0) return 1;
        var ge = nullScores.Count(s => s >= observed);
        return (ge + 1.0) / (nullScores.Count + 1.0);
    }

    public static void ApplyBenjaminiHochberg(List<HypothesisTest> tests, double alpha)
    {
        var m = tests.Count;
        if (m == 0) return;
        var ordered = tests.OrderBy(t => t.PValue).ToList();
        var k = -1;
        for (var i = 0; i < m; i++)
        {
            if (ordered[i].PValue <= (i + 1.0) / m * alpha) k = i;
        }
        for (var i = 0; i < m; i++)
        {
            var q = Math.Min(1.0, ordered[i].PValue * m / (i + 1.0));
            ordered[i].QValue = JsonIo.Round(q, 5);
            ordered[i].Significant = i <= k;
        }
    }

    /// <summary>
    /// Discovery on the first half of the glyph stream, test on the second half. The discovery
    /// hypothesis must beat its own shuffled control on data it was not fitted to.
    /// </summary>
    private static HeldOutResult? HeldOut(
        GlyphStream stream,
        CorpusIndex corpus,
        MatchResult observed,
        MatchOptions opt,
        int seed,
        double alpha,
        Action<string>? log)
    {
        if (stream.Length < 40) return null;
        var discovery = stream.Half(0);
        var test = stream.Half(1);

        var discoveryPositions = discovery.PositionsByClass();
        var testPositions = test.PositionsByClass();

        // The discovery hypothesis must be testable in both halves: a class with fewer than three
        // occurrences in a half has no usable fingerprint there.
        var best = observed.Fingerprints.FirstOrDefault(h =>
            discoveryPositions.TryGetValue(h.ClassId, out var d) && d.Count >= 3 &&
            testPositions.TryGetValue(h.ClassId, out var t) && t.Count >= 3);
        if (best is null)
        {
            log?.Invoke("significance: no hypothesis has enough occurrences in both halves for a held-out check");
            return null;
        }
        var phrasePositions = corpus.PositionsOf(best.Phrase);
        var discoveryScore = FingerprintScore(discovery, best.ClassId, phrasePositions);
        var testScore = FingerprintScore(test, best.ClassId, phrasePositions);

        var nullScores = new List<double>();
        for (var i = 0; i < 40; i++)
        {
            var shuffled = test.Shuffle(seed + 7919 + i);
            nullScores.Add(FingerprintScore(shuffled, best.ClassId, phrasePositions));
        }
        var p = EmpiricalP(nullScores, testScore);
        var mean = nullScores.Average();
        var supported = p <= Math.Max(alpha, 0.05) && testScore > mean;

        log?.Invoke($"significance: held-out discovery {discoveryScore:0.000} / test {testScore:0.000} (null mean {mean:0.000}, p {p:0.000})");

        return new HeldOutResult
        {
            ClassId = best.ClassId,
            Phrase = best.Phrase,
            DiscoveryHalfGlyphs = discovery.Length,
            TestHalfGlyphs = test.Length,
            DiscoveryScore = JsonIo.Round(discoveryScore, 4),
            TestScore = JsonIo.Round(testScore, 4),
            TestNullMean = JsonIo.Round(mean, 4),
            TestNullSd = JsonIo.Round(Sd(nullScores), 4),
            TestPValue = JsonIo.Round(p, 5),
            Supported = supported,
            Note = "Fingerprint re-evaluated on the untouched half of the glyph stream; the phrase positions are unchanged, only the glyph occurrences are new.",
        };
    }

    /// <summary>Fingerprint score for one (class, phrase) pair on an arbitrary glyph stream.</summary>
    public static double FingerprintScore(GlyphStream stream, string classId, List<int> phrasePositions)
    {
        var glyphPositions = stream.PositionsByClass().TryGetValue(classId, out var gp) ? gp : new List<int>();
        if (glyphPositions.Count < 3 || phrasePositions.Count < 3) return 0;

        var glyphGaps = GapsNormalized(glyphPositions);
        var phraseGaps = Gaps(phrasePositions);
        var total = phraseGaps.Sum();
        if (total <= 0 || glyphGaps.Count == 0) return 0;
        var k = glyphGaps.Count;

        var best = 0.0;
        for (var start = 0; start + k < phrasePositions.Count; start++)
        {
            var window = phraseGaps.Skip(start).Take(k).ToList();
            var wTotal = window.Sum();
            if (wTotal <= 0) continue;
            var norm = window.Select(g => g / wTotal).ToList();
            var distance = 0.0;
            for (var i = 0; i < k; i++) distance += Math.Abs(glyphGaps[i] - norm[i]);
            var score = 1 - distance / 2.0;
            if (score > best) best = score;
        }
        return best;
    }

    private static List<double> GapsNormalized(List<int> positions)
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

    private static double Sd(List<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }
}
