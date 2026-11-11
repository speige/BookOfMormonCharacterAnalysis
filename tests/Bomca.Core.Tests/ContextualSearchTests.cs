using Bomca.Core.Corpus;
using Bomca.Core.Matching;
using Bomca.Core.Semantic;
using Xunit;

namespace Bomca.Core.Tests;

public class ContextualSearchTests
{
    [Fact]
    public void Lemma_FoldsArchaicAndInflectedForms()
    {
        Assert.Equal("come", ContextualSearch.Lemma("came"));
        Assert.Equal("come", ContextualSearch.Lemma("cometh"));
        Assert.Equal("prophesy", ContextualSearch.Lemma("prophesied"));
        Assert.Equal("nephite", ContextualSearch.Lemma("nephites"));
        Assert.Equal("year", ContextualSearch.Lemma("years"));
        Assert.Equal("judge", ContextualSearch.Lemma("judges"));
        Assert.Equal("destroy", ContextualSearch.Lemma("destruction"));
    }

    [Fact]
    public void QueryTerms_DropStopWordsAndKeepConcepts()
    {
        var terms = ContextualSearch.QueryTerms("In the nineteenth year of Mosiah the Nephites departed over the mountains");
        Assert.Contains("mosiah", terms);
        Assert.Contains("nephite", terms);
        Assert.DoesNotContain("the", terms);
        Assert.DoesNotContain("of", terms);
    }

    [Fact]
    public void ConceptPhrases_KeepMultiWordRuns()
    {
        var concepts = ContextualSearch.ConceptPhrases("they obtained the twenty four plates of pure gold");
        Assert.Contains(concepts, c => c.Contains("plate"));
        Assert.Contains(concepts, c => c.Count >= 2);
    }

    /// <summary>
    /// A paraphrase that shares almost no surface forms with the target passage must still be located: this is
    /// the whole point of the fuzzy contextual search.
    /// </summary>
    [Fact]
    public void Search_FindsTheParaphrasedRegion()
    {
        var filler = "and it came to pass that the people did gather themselves together in the land";
        var target = "and it came to pass that king benjamin died and mosiah did reign in his stead " +
                     "after the plates of brass and the twenty four plates of gold were delivered unto him";
        var words = (filler + " ").Split(' ').ToList();
        var corpusWords = new List<string>();
        for (var i = 0; i < 25; i++) corpusWords.AddRange(words);
        var plantedAt = Math.Min(900, corpusWords.Count);
        corpusWords.InsertRange(plantedAt, target.Split(' '));

        var file = SyntheticDataset.BuildCorpusFile(corpusWords.ToArray(), versesPerChapter: 2, wordsPerVerse: 60);
        var corpus = CorpusIndex.Build(file);

        var paraphrase = "Benjamin passed away and Mosiah began to rule; the gold plates were handed over";
        var result = ContextualSearch.Build(corpus, "T", "test",
            new List<(string, string)> { ("segment", paraphrase) },
            new ContextualSearchOptions
            {
                Window = 200,
                Stride = 50,
                TopWindows = 5,
                TopRegionsPerSegment = 2,
                MaxRegions = 2,
                Controls = 20,
                UseSynonyms = false,
            });

        var seg = result.Segments.Single();
        Assert.NotEmpty(seg.Windows);
        var best = seg.Windows[0];
        Assert.True(Math.Abs(best.Start - plantedAt) < 300,
            $"best window started at {best.Start}, planted at {plantedAt}");
        Assert.True(seg.BestWindowScore > seg.ControlMean,
            $"best {seg.BestWindowScore} should beat control {seg.ControlMean}");
        Assert.NotEmpty(result.Regions);
        Assert.True(result.SearchReductionFactor > 1.0);
    }

    [Fact]
    public void FocusRegions_RestrictTheSymbolSearch()
    {
        var data = SyntheticDataset.Build();
        var opt = SyntheticDataset.Options(data.TrueSpanWords);

        // Focus somewhere else entirely: the planted passage must not be found.
        opt.FocusRegions = new List<(int, int)> { (0, data.TrueWordStart / 2) };
        var offTarget = MatchEngine.Run(data.Stream, data.Corpus, opt);

        // Focus on the planted region: the passage must be found.
        opt.FocusRegions = new List<(int, int)> { (data.TrueWordStart - 100, data.TrueWordStart + data.TrueSpanWords + 100) };
        var onTarget = MatchEngine.Run(data.Stream, data.Corpus, opt);

        Assert.False(offTarget.Passages.Any(p => SyntheticDataset.PassageMatches(data, p)),
            "no passage inside the off-target region should match the planted location");
        Assert.True(onTarget.Passages.Count > 0, "the focused search should still produce passages");
        Assert.True(SyntheticDataset.PassageMatches(data, onTarget.Passages[0]),
            $"focused passage at {onTarget.Passages[0].WordStart}, planted at {data.TrueWordStart}");
    }
}
