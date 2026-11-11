namespace Bomca.Core.Corpus;

public sealed class CorpusFile
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Edition { get; set; } = "current";
    public string Source { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Sha256 { get; set; }
    public string GeneratedUtc { get; set; } = "";
    public int WordCount { get; set; }
    public int VerseCount { get; set; }
    public string Normalization { get; set; } = "lowercase; punctuation removed; apostrophes removed; whitespace collapsed";
    public List<CorpusBook> Books { get; set; } = new();
}

public sealed class CorpusBook
{
    public string Name { get; set; } = "";
    public List<CorpusChapter> Chapters { get; set; } = new();
}

public sealed class CorpusChapter
{
    public int Number { get; set; }
    public List<CorpusVerse> Verses { get; set; } = new();
}

public sealed class CorpusVerse
{
    public int Number { get; set; }
    public string Ref { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class VerseSpan
{
    public int Book { get; set; }
    public string BookName { get; set; } = "";
    public int Chapter { get; set; }
    public int Verse { get; set; }
    public string Ref { get; set; } = "";
    public int TokenStart { get; set; }
    public int TokenCount { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>Flattened, normalised token stream over the whole corpus with verse addressing.</summary>
public sealed class CorpusIndex
{
    public string Edition { get; init; } = "";
    public string[] Tokens { get; init; } = Array.Empty<string>();
    public int[] VerseOfToken { get; init; } = Array.Empty<int>();
    public List<VerseSpan> Verses { get; init; } = new();
    public string[] BookNames { get; init; } = Array.Empty<string>();
    public int[] BookOfToken { get; init; } = Array.Empty<int>();
    public int[] ChapterOfToken { get; init; } = Array.Empty<int>();
    public Dictionary<string, int> UnigramCounts { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int>[] NgramCounts { get; init; } = Array.Empty<Dictionary<string, int>>();
    public int[] DistinctWordIds { get; init; } = Array.Empty<int>();

    public int Length => Tokens.Length;

    public static CorpusIndex Build(CorpusFile file, int maxN = 8)
    {
        var tokens = new List<string>(file.WordCount + 1024);
        var verseOf = new List<int>(tokens.Capacity);
        var books = new List<string>();
        var bookOf = new List<int>(tokens.Capacity);
        var chapterOf = new List<int>(tokens.Capacity);
        var spans = new List<VerseSpan>();
        var bookNames = new List<string>();
        var seenBooks = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var book in file.Books)
        {
            if (!seenBooks.TryGetValue(book.Name, out var bookIdx))
            {
                bookIdx = bookNames.Count;
                bookNames.Add(book.Name);
                seenBooks[book.Name] = bookIdx;
            }

            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses)
                {
                    var verseIdx = spans.Count;
                    var start = tokens.Count;
                    foreach (var t in Normalizer.Tokenize(verse.Text))
                    {
                        tokens.Add(t);
                        verseOf.Add(verseIdx);
                        bookOf.Add(bookIdx);
                        chapterOf.Add(chapter.Number);
                    }
                    spans.Add(new VerseSpan
                    {
                        Book = bookIdx,
                        BookName = book.Name,
                        Chapter = chapter.Number,
                        Verse = verse.Number,
                        Ref = string.IsNullOrWhiteSpace(verse.Ref) ? $"{book.Name} {chapter.Number}:{verse.Number}" : verse.Ref,
                        TokenStart = start,
                        TokenCount = tokens.Count - start,
                        Text = verse.Text,
                    });
                }
            }
        }

        var tokenArr = tokens.ToArray();
        var unigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tokenArr) unigrams[t] = unigrams.TryGetValue(t, out var c) ? c + 1 : 1;

        var ngrams = new Dictionary<string, int>[maxN + 1];
        var sb = new System.Text.StringBuilder();
        for (var n = 1; n <= maxN; n++)
        {
            var dict = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i + n <= tokenArr.Length; i++)
            {
                sb.Clear();
                for (var k = 0; k < n; k++)
                {
                    if (k > 0) sb.Append(' ');
                    sb.Append(tokenArr[i + k]);
                }
                var key = sb.ToString();
                dict[key] = dict.TryGetValue(key, out var c) ? c + 1 : 1;
            }
            ngrams[n] = dict;
        }

        return new CorpusIndex
        {
            Edition = file.Edition,
            Tokens = tokenArr,
            VerseOfToken = verseOf.ToArray(),
            Verses = spans,
            BookNames = bookNames.ToArray(),
            BookOfToken = bookOf.ToArray(),
            ChapterOfToken = chapterOf.ToArray(),
            UnigramCounts = unigrams,
            NgramCounts = ngrams,
            DistinctWordIds = Enumerable.Range(0, unigrams.Count).ToArray(),
        };
    }

    /// <summary>All token positions where the given normalised phrase starts.</summary>
    public List<int> PositionsOf(string phrase)
    {
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>();
        if (parts.Length == 0 || parts.Length > Length) return result;
        for (var i = 0; i + parts.Length <= Length; i++)
        {
            var ok = true;
            for (var k = 0; k < parts.Length; k++)
            {
                if (!string.Equals(Tokens[i + k], parts[k], StringComparison.Ordinal)) { ok = false; break; }
            }
            if (ok) result.Add(i);
        }
        return result;
    }

    public int CountOf(string phrase) => phrase.Contains(' ')
        ? (NgramCounts.Length > PhraseLength(phrase) ? NgramCounts[PhraseLength(phrase)].TryGetValue(phrase, out var c) ? c : 0 : 0)
        : UnigramCounts.TryGetValue(phrase, out var c2) ? c2 : 0;

    private static int PhraseLength(string phrase) => phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    public string ReferenceAt(int tokenIndex)
    {
        if (tokenIndex < 0 || tokenIndex >= VerseOfToken.Length) return "?";
        return Verses[VerseOfToken[tokenIndex]].Ref;
    }

    public string SnippetAt(int tokenIndex, int words = 14)
    {
        var start = Math.Max(0, tokenIndex - words / 2);
        var end = Math.Min(Length, start + words);
        return string.Join(' ', Tokens[start..end]);
    }

    public IEnumerable<string> DistinctWords() => UnigramCounts.Keys;

    /// <summary>Words that appear at least <paramref name="minCount"/> times, most frequent first.</summary>
    public List<(string Word, int Count)> FrequentWords(int minCount = 2)
        => UnigramCounts.Where(kv => kv.Value >= minCount).OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();

    public List<(string Phrase, int Count)> FrequentPhrases(int n, int minCount = 2, int take = 200)
    {
        if (n >= NgramCounts.Length) return new List<(string, int)>();
        return NgramCounts[n].Where(kv => kv.Value >= minCount)
            .OrderByDescending(kv => kv.Value)
            .Take(take)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }
}

public static class Normalizer
{
    /// <summary>
    /// Tokenise for statistical work: lowercase, fold typographic quotes, remove apostrophes and
    /// all punctuation, collapse whitespace. "It's" and "its" intentionally normalise the same way.
    /// </summary>
    public static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var sb = new System.Text.StringBuilder(24);
        foreach (var ch in text)
        {
            var c = Fold(ch);
            if (char.IsLetter(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c is '\'' or '\u2019' or '\u02bc')
            {
                // drop apostrophes: "it's" -> "its", "Lord's" -> "lords"
            }
            else
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    public static string NormalizeText(string text) => string.Join(' ', Tokenize(text));

    private static char Fold(char c) => c switch
    {
        '\u2018' or '\u2019' or '\u02bc' or '\u00b4' => '\'',
        '\u201c' or '\u201d' => '"',
        '\u2013' or '\u2014' or '\u2212' => ' ',
        '\u00a0' => ' ',
        _ => c,
    };
}
