using Bomca.Core.Corpus;
using Bomca.Core.Matching;
using Bomca.Core.Significance;
using Xunit;

namespace Bomca.Core.Tests;

public class CorpusTests
{
    [Fact]
    public void Tokenizer_StripsPunctuationAndApostrophes()
    {
        var tokens = Normalizer.Tokenize("And it came to pass, that the Lord's curse\u2014yea, 'it\u2019s' here!").ToList();
        Assert.Equal(new[] { "and", "it", "came", "to", "pass", "that", "the", "lords", "curse", "yea", "its", "here" }, tokens);
    }

    [Fact]
    public void Tokenizer_NeverEmitsEmptyTokens()
    {
        var tokens = Normalizer.Tokenize("... ,, ;; -- !!").ToList();
        Assert.Empty(tokens);
    }

    [Fact]
    public void Index_MapsTokensToVerseReferences()
    {
        var file = SyntheticDataset.BuildCorpusFile(
            "alpha beta gamma delta epsilon zeta eta theta".Split(' '), versesPerChapter: 2, wordsPerVerse: 3);
        var index = CorpusIndex.Build(file);

        Assert.Equal(8, index.Length);
        Assert.Equal(3, index.Verses.Count);
        Assert.Equal("1 Nephi 1:1", index.ReferenceAt(0));
        Assert.Equal("1 Nephi 1:2", index.ReferenceAt(3));
        Assert.Equal("1 Nephi 2:1", index.ReferenceAt(6));
    }

    [Fact]
    public void Index_CountsNgramsAndPositions()
    {
        var file = SyntheticDataset.BuildCorpusFile(
            "and it came to pass and it came to pass and it came".Split(' '), versesPerChapter: 1, wordsPerVerse: 20);
        var index = CorpusIndex.Build(file);

        Assert.Equal(3, index.CountOf("and"));
        Assert.Equal(2, index.CountOf("and it came to pass"));
        Assert.Equal(2, index.PositionsOf("and it came to pass").Count);
        Assert.Equal(3, index.CountOf("and it came"));
        Assert.Equal(0, index.CountOf("and it came to pass that"));
        Assert.Equal(0, index.PositionsOf("not present").Count);
    }

    [Fact]
    public void FrequentPhrases_RanksByCount()
    {
        var file = SyntheticDataset.BuildCorpusFile(
            ("and it came to pass " + string.Join(' ', Enumerable.Repeat("alpha", 10))).Split(' '),
            versesPerChapter: 1, wordsPerVerse: 50);
        var index = CorpusIndex.Build(file);
        var top = index.FrequentPhrases(5, minCount: 1, take: 12);
        Assert.Contains(top, t => t.Phrase == "and it came to pass");
    }
}

public class FingerprintScanTests
{
    [Fact]
    public void ScannerA_RecoversPlantedClassPhrasePairs()
    {
        var data = SyntheticDataset.Build();
        var opt = SyntheticDataset.Options(data.TrueSpanWords);
        var phrases = MatchEngine.CandidatePhrases(data.Corpus, opt);
        var positions = phrases.ToDictionary(p => p.Phrase, p => data.Corpus.PositionsOf(p.Phrase));

        var hypotheses = MatchEngine.ScanFingerprints(data.Stream, positions, opt, data.Corpus.Length);

        Assert.NotEmpty(hypotheses);
        var recovered = data.Planted.Count(p => hypotheses.Take(30).Any(h => h.ClassId == p.ClassId && h.Phrase == p.Phrase));
        Assert.True(recovered >= 4, $"expected at least 4 of {data.Planted.Count} planted pairs recovered, got {recovered}");
    }

    [Fact]
    public void ScannerA_ShuffledStreamScoresLower()
    {
        var data = SyntheticDataset.Build();
        var opt = SyntheticDataset.Options(data.TrueSpanWords);
        var phrases = MatchEngine.CandidatePhrases(data.Corpus, opt);
        var positions = phrases.ToDictionary(p => p.Phrase, p => data.Corpus.PositionsOf(p.Phrase));

        var real = MatchEngine.ScanFingerprints(data.Stream, positions, opt, data.Corpus.Length).FirstOrDefault()?.Score ?? 0;
        var shuffled = MatchEngine.ScanFingerprints(data.Stream.Shuffle(99), positions, opt, data.Corpus.Length).FirstOrDefault()?.Score ?? 0;

        Assert.True(real > shuffled, $"real best {real:0.000} should exceed shuffled best {shuffled:0.000}");
    }
}

public class SpanConsensusTests
{
    [Fact]
    public void ScannerB_FindsThePlantedPassage()
    {
        var data = SyntheticDataset.Build();
        var opt = SyntheticDataset.Options(data.TrueSpanWords);
        var result = MatchEngine.Run(data.Stream, data.Corpus, opt);

        Assert.NotEmpty(result.Passages);
        Assert.True(SyntheticDataset.PassageMatches(data, result.Passages[0]),
            $"best passage started at word {result.Passages[0].WordStart}, planted at {data.TrueWordStart} (±{data.TrueSpanWords * 0.35:0})");
    }

    [Fact]
    public void ScannerB_FindsNothingInAShuffledStream()
    {
        var data = SyntheticDataset.Build();
        var opt = SyntheticDataset.Options(data.TrueSpanWords);
        var result = MatchEngine.Run(data.Stream.Shuffle(4242), data.Corpus, opt);

        var best = result.Passages.FirstOrDefault();
        Assert.True(best is null || best.WarpScore < 0.8, $"shuffled stream produced a suspicious score of {best?.WarpScore:0.000}");
    }
}

public class AlignerTests
{
    [Fact]
    public void Aligner_RecoversExactDictionaryMatches()
    {
        var data = SyntheticDataset.Build();
        var dictionary = data.Planted.ToDictionary(p => p.ClassId, p => new HashSet<string> { p.Phrase }, StringComparer.Ordinal);

        var (score, rows) = GappedAligner.Align(data.Stream, data.Corpus, data.TrueWordStart, data.TrueSpanWords, dictionary);

        Assert.True(score > 0.2, $"alignment coverage {score:0.000} too low");
        var matched = rows.Count(r => r.Match >= 0.99);
        Assert.True(matched >= 10, $"expected many exact matches, got {matched}");
    }

    [Fact]
    public void Aligner_HandlesInsertionsAndDeletions()
    {
        // glyph stream: A B C D ; corpus: a x b c y d   (x,y are unexplained words)
        var file = SyntheticDataset.BuildCorpusFile("a x b c y d".Split(' '), 1, 20);
        var corpus = CorpusIndex.Build(file);
        var stream = new GlyphStream
        {
            Doc = "T",
            ClassIds = new[] { "G1", "G2", "G3", "G4" },
            OccurrenceIds = new[] { "1", "2", "3", "4" },
            Rows = new[] { 0, 0, 0, 0 },
        };
        var dict = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["G1"] = new HashSet<string> { "a" },
            ["G2"] = new HashSet<string> { "b" },
            ["G3"] = new HashSet<string> { "c" },
            ["G4"] = new HashSet<string> { "d" },
        };

        var (score, rows) = GappedAligner.Align(stream, corpus, 0, corpus.Length, dict);
        Assert.True(score > 0.4);
        Assert.Equal(4, rows.Count(r => r.Match > 0.5));
    }

    [Fact]
    public void Aligner_SupportsLogographicExpansion()
    {
        // one glyph stands for a three-word phrase
        var file = SyntheticDataset.BuildCorpusFile("and it came to pass".Split(' '), 1, 20);
        var corpus = CorpusIndex.Build(file);
        var stream = new GlyphStream
        {
            Doc = "T",
            ClassIds = new[] { "G1" },
            OccurrenceIds = new[] { "1" },
            Rows = new[] { 0 },
        };
        var dict = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["G1"] = new HashSet<string> { "and it came to pass" },
        };
        var (score, rows) = GappedAligner.Align(stream, corpus, 0, corpus.Length, dict);
        Assert.True(score > 0.2);
        Assert.Contains(rows, r => r.WordCount >= 3);
    }
}

public class SignificanceTests
{
    [Fact]
    public void EmpiricalP_IsBoundedAndMonotone()
    {
        var nulls = new List<double> { 0.1, 0.2, 0.3, 0.4 };
        // p = (#null >= observed + 1) / (n + 1)
        // p = (#null >= observed + 1) / (n + 1)
        Assert.Equal(1.0, SignificanceHarness.EmpiricalP(nulls, 0.05), 3);
        Assert.Equal(0.4, SignificanceHarness.EmpiricalP(nulls, 0.4), 3);
        Assert.Equal(0.2, SignificanceHarness.EmpiricalP(nulls, 0.9), 3);
    }

    [Fact]
    public void BenjaminiHochberg_FlagsStrongestHypotheses()
    {
        var tests = new List<HypothesisTest>
        {
            new() { Score = 0.9, PValue = 0.001 },
            new() { Score = 0.8, PValue = 0.02 },
            new() { Score = 0.5, PValue = 0.60 },
            new() { Score = 0.4, PValue = 0.90 },
        };
        SignificanceHarness.ApplyBenjaminiHochberg(tests, 0.05);
        Assert.True(tests[0].Significant);
        Assert.False(tests[3].Significant);
        Assert.True(tests[0].QValue <= 0.01);
    }

    [Fact]
    public void Significance_PlantedSignalBeatsShuffledControls()
    {
        var data = SyntheticDataset.Build();
        var opt = SyntheticDataset.Options(data.TrueSpanWords);
        var report = SignificanceHarness.Run(data.Stream, data.Corpus, opt, iterations: 25, seed: 4242, alpha: 0.05);

        Assert.True(report.PValue <= 0.10, $"p-value {report.PValue} should be small for a planted signal (observed {report.ObservedBest:0.000}, null mean {report.NullMean:0.000})");
        Assert.True(report.ObservedBest > report.NullMean);
    }

    [Fact]
    public void Significance_NoiseStreamIsNotSignificant()
    {
        var data = SyntheticDataset.Build();
        var opt = SyntheticDataset.Options(data.TrueSpanWords);
        var noise = data.Stream.Shuffle(31337);
        var report = SignificanceHarness.Run(noise, data.Corpus, opt, iterations: 25, seed: 909, alpha: 0.05);

        Assert.True(report.PValue > 0.05, $"pure noise should not be significant (p={report.PValue}, observed {report.ObservedBest:0.000} vs null {report.NullMean:0.000})");
    }
}

public class EndToEndTests
{
    [Fact]
    public void FullPipeline_FindsThePlantedPassage()
    {
        var data = SyntheticDataset.Build(seed: 11);
        var opt = SyntheticDataset.Options(data.TrueSpanWords);

        var result = MatchEngine.Run(data.Stream, data.Corpus, opt);

        Assert.NotEmpty(result.Fingerprints);
        Assert.NotEmpty(result.Passages);
        var top = result.Passages[0];
        Assert.True(SyntheticDataset.PassageMatches(data, top),
            $"top passage at word {top.WordStart}, planted at {data.TrueWordStart}");
        Assert.True(top.Combined > 0.4, $"top combined score {top.Combined} too low");

        var plantedClass = data.Planted[0].ClassId;
        var plantedPhrase = data.Planted[0].Phrase;
        Assert.Contains(result.Fingerprints.Take(40), h => h.ClassId == plantedClass && h.Phrase == plantedPhrase);
    }
}
