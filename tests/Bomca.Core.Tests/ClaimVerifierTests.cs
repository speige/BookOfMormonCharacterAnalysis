using Bomca.Core.Corpus;
using Bomca.Core.External;
using Xunit;

namespace Bomca.Core.Tests;

public class ClaimVerifierTests
{
    [Fact]
    public void RunsOf_CountsConsecutiveSequences()
    {
        var runs = ClaimVerifier.RunsOf(new List<int> { 3, 4, 5, 9, 12, 13 });
        Assert.Equal(3, runs.Runs);
        Assert.Equal(3, runs.MaxRun);
        Assert.Equal(new[] { 3, 1, 2 }, runs.RunLengths);
    }

    [Fact]
    public void NumeralRunTest_DetectsPlantedRuns()
    {
        // 30 glyphs; numeral-like glyphs occupy four consecutive runs of 3-4.
        var positions = new List<int> { 2, 3, 4, 9, 10, 11, 12, 18, 19, 20, 25, 26 };
        var (observed, nullRuns, p, z) = ClaimVerifier.NumeralRunTest(positions, 30, 200, 1234);

        Assert.Equal(4, observed.Runs);
        Assert.Equal(3.0, observed.MeanRun, 3);
        Assert.True(p <= 0.05, $"planted runs should be significant (p={p}, z={z}, null mean {nullRuns.MeanRun})");
        Assert.True(z > 1.0);
    }

    [Fact]
    public void NumeralRunTest_DoesNotFlagScatteredPositions()
    {
        var positions = new List<int> { 1, 4, 7, 10, 13, 16, 19, 22, 25, 28 };
        var (observed, _, p, _) = ClaimVerifier.NumeralRunTest(positions, 30, 200, 99);

        Assert.Equal(10, observed.Runs);
        Assert.True(p > 0.05, $"scattered positions should not look like runs (p={p})");
    }

    [Fact]
    public void TextClaims_AreCheckedAgainstTheCorpus()
    {
        var file = SyntheticDataset.BuildCorpusFile(
            "and it came to pass that in the thirty and fourth year the people were destroyed".Split(' '), 1, 40);
        var corpus = CorpusIndex.Build(file);
        var claims = new List<TextClaim>
        {
            new() { Claim = "34th year", Pattern = "thirty and fourth year", ExpectedRefs = new List<string> { "1 Nephi" } },
            new() { Claim = "absent", Pattern = "ninety and second year", ExpectedRefs = new List<string> { "1 Nephi" } },
        };

        var results = ClaimVerifier.CheckTextClaims(corpus, claims);

        Assert.Equal("consistent", results[0].Status);
        Assert.Equal("pattern-absent", results[1].Status);
        Assert.Equal(0, results[1].Occurrences);
    }

    [Fact]
    public void PositionalNumerals_MapsClaimedIdsOntoTheStream()
    {
        // 100 glyph stream; the claim numbers 1..50 for the top segment and 51..100 for the bottom.
        var scores = new double[100];
        for (var i = 0; i < 20; i++) scores[i] = 0.9;    // numerals concentrated at the start
        var segments = new List<ClaimSegment>
        {
            new() { Name = "top", Rows = new List<int> { 1, 2 }, CharacterRange = new List<int> { 1, 50 } },
            new() { Name = "bottom", Rows = new List<int> { 3, 4 }, CharacterRange = new List<int> { 51, 100 } },
        };
        var claims = new List<NumeralClaim>
        {
            new() { Ids = new List<string> { "C-2", "C-3", "C-4", "C-5" }, Value = 19 },
        };

        var result = ClaimVerifier.PositionalNumerals(scores, segments, claims, 200, 7);

        Assert.Equal(4, result.ClaimedNumerals);
        Assert.True(result.MeanScoreAtClaimed > result.MeanScoreRandomSameCount,
            $"claimed positions ({result.MeanScoreAtClaimed}) should beat random ({result.MeanScoreRandomSameCount})");
        Assert.True(result.PValue <= 0.10, $"p={result.PValue}");
    }

    [Fact]
    public void NumeralScores_AreDenseAndBounded()
    {
        var glyphs = new List<Bomca.Core.Model.SymbolOccurrence>
        {
            new() { Id = "A", GlobalIndex = 0, StrokeCount = 6, Aspect = 3.0, InkDensity = 0.2, Area = 500, H = 80 },
            new() { Id = "B", GlobalIndex = 1, StrokeCount = 1, Aspect = 1.0, InkDensity = 0.15, Area = 900, H = 120 },
        };
        var scores = ClaimVerifier.NumeralScores(glyphs);

        Assert.Equal(2, scores.Length);
        Assert.All(scores, s => Assert.InRange(s, 0, 1));
        Assert.True(scores[0] > scores[1], "the comb glyph should score higher than the ornate glyph");
    }

    [Fact]
    public void ClaimFile_LoadsAndIsSelfConsistent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "grover-claims.json");
        if (!File.Exists(path)) return; // fixture not copied; covered by the CLI run
        var claims = ClaimVerifier.Load(path);

        Assert.Equal(2, claims.Segments.Count);
        Assert.NotEmpty(claims.NumeralClaims);
        Assert.All(claims.NumeralClaims, c => Assert.True(c.Ids.Count >= 2));
        Assert.NotEmpty(claims.TextClaims);
    }
}
