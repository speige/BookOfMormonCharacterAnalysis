using Bomca.Core.Corpus;
using Bomca.Core.Matching;
using Bomca.Core.Model;
using Bomca.Core.Significance;

namespace Bomca.Core.Tests;

/// <summary>
/// Builds a small fake Book-of-Mormon-shaped corpus plus a fake glyph stream with a known planted mapping,
/// so every search algorithm can be verified without touching real images or the real text.
/// </summary>
public static class SyntheticDataset
{
    public static readonly string[] FillerWords =
    {
        "nephi", "laman", "lemuel", "sam", "jarom", "omni", "ammon", "aaron", "omner", "himni",
        "zarahemla", "moroni", "mormon", "ether", "shiblon", "helaman", "kishkumen", "coriantumr",
    };

    public sealed class PlantedPhrase
    {
        public string Phrase = "";
        public List<int> Positions = new();
        public string ClassId = "";
    }

    public sealed class Dataset
    {
        public CorpusFile CorpusFile = new();
        public CorpusIndex Corpus = null!;
        public GlyphStream Stream = null!;
        public List<PlantedPhrase> Planted = new();
        public int TrueWordStart;
        public int TrueSpanWords;
        public int FillerClassCount = 8;
    }

    /// <summary>
    /// Layout: 3000 words of filler with six planted phrases clustered around word 1200. The glyph stream
    /// covers only the planted span, so a correct algorithm must recover both the span and the pairing.
    /// Phrase words are stamped into a reserved region so no phrase can be partially overwritten.
    /// </summary>
    public static Dataset Build(int seed = 7, int spanWords = 2000, int fillerGlyphs = 0, int totalWords = 16000, int trueStart = 5000)
    {
        if (fillerGlyphs <= 0) fillerGlyphs = spanWords / 8;
        var rng = new Random(seed);
        var planted = new List<PlantedPhrase>
        {
            New("and it came to", "G01"),
            New("behold the lord", "G02"),
            New("the people of", "G03"),
            New("in the land of", "G04"),
            New("yea and it", "G05"),
            New("the sons of", "G06"),
            New("upon the face of", "G07"),
            New("the word of the", "G08"),
        };
        // Distinct occurrence counts make each planted phrase individually identifiable inside the span.
        var perPhrase = new[] { 9, 8, 7, 6, 5, 4, 3, 3 };

        var total = totalWords;
        var words = new string[total];
        var used = new bool[total];
        for (var i = 0; i < total; i++) words[i] = FillerWords[rng.Next(FillerWords.Length)];

        for (var i = 0; i < planted.Count; i++)
        {
            var parts = planted[i].Phrase.Split(' ');
            var placed = 0;
            var attempts = 0;
            while (placed < perPhrase[i] && attempts < 4000)
            {
                attempts++;
                var pos = trueStart + rng.Next(2, spanWords - parts.Length - 2);
                var free = true;
                for (var k = -1; k <= parts.Length && free; k++)
                {
                    var at = pos + k;
                    if (at >= 0 && at < total && used[at]) free = false;
                }
                if (!free) continue;
                for (var k = 0; k < parts.Length; k++)
                {
                    words[pos + k] = parts[k];
                    used[pos + k] = true;
                }
                used[pos - 1] = true;
                planted[i].Positions.Add(pos);
                placed++;
            }
            planted[i].Positions.Sort();
        }

        // Realistic density: every planted phrase also occurs outside the span at the same rate, so the
        // corpus looks like a real book where a passage is not the only place a phrase appears.
        var outsideFactor = (total - spanWords) / (double)spanWords;
        foreach (var p in planted)
        {
            var extras = (int)Math.Round(p.Positions.Count * outsideFactor);
            var parts = p.Phrase.Split(' ');
            var placedExtras = 0;
            var attempts = 0;
            while (placedExtras < extras && attempts < 20000)
            {
                attempts++;
                var pos = rng.Next(0, total - parts.Length - 1);
                if (pos >= trueStart - 2 && pos <= trueStart + spanWords + 2) continue;
                var free = true;
                for (var k = -1; k <= parts.Length && free; k++)
                {
                    var at = pos + k;
                    if (at >= 0 && at < total && used[at]) free = false;
                }
                if (!free) continue;
                for (var k = 0; k < parts.Length; k++)
                {
                    words[pos + k] = parts[k];
                    used[pos + k] = true;
                }
                used[pos - 1] = true;
                placedExtras++;
            }
        }

        var corpusFile = BuildCorpusFile(words, versesPerChapter: 2, wordsPerVerse: 60);
        var corpus = CorpusIndex.Build(corpusFile);

        // One glyph per (spanWords / glyphs) words: glyph index -> word position is linear.
        var glyphCount = fillerGlyphs + planted.Sum(p => p.Positions.Count);
        var classes = new string[glyphCount];
        var rng2 = new Random(seed + 1);
        for (var i = 0; i < glyphCount; i++) classes[i] = $"F{rng2.Next(1, 9):00}";

        var place = new List<(int idx, string cls)>();
        foreach (var p in planted)
        {
            foreach (var pos in p.Positions)
            {
                var rel = (pos - trueStart) / (double)spanWords;
                var idx = Math.Clamp((int)Math.Round(rel * (glyphCount - 1)), 0, glyphCount - 1);
                while (place.Any(x => x.idx == idx)) idx = (idx + 1) % glyphCount;
                place.Add((idx, p.ClassId));
            }
        }
        foreach (var (idx, cls) in place) classes[idx] = cls;

        var stream = new GlyphStream
        {
            Doc = "SYN",
            ClassIds = classes,
            OccurrenceIds = Enumerable.Range(0, glyphCount).Select(i => $"SYN-L01-{i + 1:000}").ToArray(),
            Rows = Enumerable.Repeat(0, glyphCount).ToArray(),
            Threshold = 0.8,
        };

        return new Dataset
        {
            CorpusFile = corpusFile,
            Corpus = corpus,
            Stream = stream,
            Planted = planted,
            TrueWordStart = trueStart,
            TrueSpanWords = spanWords,
        };
    }

    private static PlantedPhrase New(string phrase, string cls) => new() { Phrase = phrase, ClassId = cls };

    public static CorpusFile BuildCorpusFile(string[] words, int versesPerChapter, int wordsPerVerse)
    {
        var file = new CorpusFile { Edition = "synthetic", Source = "synthetic", WordCount = words.Length };
        var book = new CorpusBook { Name = "1 Nephi" };
        var chapter = new CorpusChapter { Number = 1 };
        var verse = new CorpusVerse { Number = 1, Ref = "1 Nephi 1:1" };
        var tokens = new List<string>();
        var verseNo = 1;
        var chapterNo = 1;
        var versesInChapter = 0;
        foreach (var w in words)
        {
            tokens.Add(w);
            if (tokens.Count >= wordsPerVerse)
            {
                verse.Text = string.Join(' ', tokens);
                chapter.Verses.Add(verse);
                tokens.Clear();
                verseNo++;
                versesInChapter++;
                verse = new CorpusVerse { Number = verseNo, Ref = $"1 Nephi {chapterNo}:{verseNo}" };
                if (versesInChapter >= versesPerChapter)
                {
                    book.Chapters.Add(chapter);
                    chapterNo++;
                    chapter = new CorpusChapter { Number = chapterNo };
                    versesInChapter = 0;
                    verseNo = 1;
                    verse = new CorpusVerse { Number = 1, Ref = $"1 Nephi {chapterNo}:1" };
                }
            }
        }
        if (tokens.Count > 0)
        {
            verse.Text = string.Join(' ', tokens);
            chapter.Verses.Add(verse);
        }
        book.Chapters.Add(chapter);
        file.Books.Add(book);
        file.VerseCount = file.Books.Sum(b => b.Chapters.Sum(c => c.Verses.Count));
        return file;
    }

    /// <summary>Scores a reported passage against the planted truth.</summary>
    public static bool PassageMatches(Dataset data, PassageCandidate candidate, double tolerance = 0.35)
    {
        var toleranceWords = (int)Math.Round(data.TrueSpanWords * tolerance);
        return Math.Abs(candidate.WordStart - data.TrueWordStart) <= toleranceWords;
    }

    public static MatchOptions Options(int span) => new()
    {
        MinOccurrences = 2,
        MaxPhrases = 400,
        MaxPhraseLength = 5,
        JointTopHypotheses = 12,
        WindowSizes = new[] { span / 2, span, span * 2 },
        WarpStride = 40,
        PassageTake = 10,
        RunAlignment = true,
        AnchorConcepts = new[] { "land", "lord" },
        WarpGate = 0.30,
    };
}
