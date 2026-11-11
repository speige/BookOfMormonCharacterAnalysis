using System.Text.Json;
using System.Text.RegularExpressions;
using Bomca.Core.Json;

namespace Bomca.Core.Corpus;

public sealed class CorpusBuildResult
{
    public CorpusFile File { get; init; } = new();
    public string RawPath { get; init; } = "";
    public List<string> Notes { get; init; } = new();
}

/// <summary>
/// Downloads and normalises Book of Mormon text. The edition is not critical to the experiment
/// because all analysis strips punctuation; the current edition is primary and the 1830 edition is
/// optional for change statistics.
/// </summary>
public static class CorpusSource
{
    public const string CurrentUrl = "https://raw.githubusercontent.com/bcbooks/scriptures-json/master/book-of-mormon.json";
    public const string Pages1830TreeUrl = "https://api.github.com/repos/lds-restoration-documents/1830-grandin-palmyra-book-of-mormon/git/trees/HEAD?recursive=1";
    public const string Pages1830RawBase = "https://raw.githubusercontent.com/lds-restoration-documents/1830-grandin-palmyra-book-of-mormon/HEAD/";

    public static async Task<CorpusBuildResult> FetchCurrentAsync(string rawDir, bool force = false)
    {
        Directory.CreateDirectory(rawDir);
        var rawPath = Path.Combine(rawDir, "book-of-mormon.json");
        if (force || !File.Exists(rawPath))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("bomca/0.1");
            var bytes = await http.GetByteArrayAsync(CurrentUrl);
            await File.WriteAllBytesAsync(rawPath, bytes);
        }

        var file = ParseCurrent(rawPath);
        var sha = JsonIo.Sha256File(rawPath);
        file.Sha256 = sha;
        file.Source = Path.GetFileName(rawPath);
        file.SourceUrl = CurrentUrl;
        file.GeneratedUtc = DateTime.UtcNow.ToString("o");
        return new CorpusBuildResult { File = file, RawPath = rawPath };
    }

    public static CorpusFile ParseCurrent(string rawPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(rawPath));
        var root = doc.RootElement;
        var corpus = new CorpusFile { Edition = "current", Source = Path.GetFileName(rawPath) };

        if (root.TryGetProperty("title_page", out var titlePage) && titlePage.ValueKind == JsonValueKind.Object)
        {
            var tp = new CorpusBook { Name = "Title Page" };
            var ch = new CorpusChapter { Number = 1 };
            ch.Verses.Add(new CorpusVerse
            {
                Number = 1,
                Ref = "Title Page",
                Text = CollectText(titlePage),
            });
            tp.Chapters.Add(ch);
            corpus.Books.Add(tp);
        }

        JsonElement booksEl;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("books", out var b)) booksEl = b;
        else if (root.ValueKind == JsonValueKind.Array) booksEl = root;
        else throw new InvalidDataException("unrecognised corpus JSON: no 'books' array");

        foreach (var bookEl in booksEl.EnumerateArray())
        {
            var book = new CorpusBook
            {
                Name = bookEl.TryGetProperty("book", out var bn) ? bn.GetString() ?? "unknown" : "unknown",
            };
            if (!bookEl.TryGetProperty("chapters", out var chapters)) continue;
            foreach (var chEl in chapters.EnumerateArray())
            {
                var chapter = new CorpusChapter
                {
                    Number = chEl.TryGetProperty("chapter", out var cn) && cn.TryGetInt32(out var ci) ? ci : 0,
                };
                if (chEl.TryGetProperty("verses", out var verses))
                {
                    foreach (var vEl in verses.EnumerateArray())
                    {
                        var text = vEl.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                        var num = vEl.TryGetProperty("verse", out var vn) && vn.TryGetInt32(out var vi) ? vi : chapter.Verses.Count + 1;
                        var reference = vEl.TryGetProperty("reference", out var r) ? r.GetString() ?? "" : "";
                        chapter.Verses.Add(new CorpusVerse { Number = num, Ref = reference, Text = text });
                    }
                }
                book.Chapters.Add(chapter);
            }
            corpus.Books.Add(book);
        }

        corpus.WordCount = corpus.Books.Sum(bk => bk.Chapters.Sum(c => c.Verses.Sum(v => Normalizer.Tokenize(v.Text).Count())));
        corpus.VerseCount = corpus.Books.Sum(bk => bk.Chapters.Sum(c => c.Verses.Count));
        return corpus;
    }

    private static string CollectText(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Object => string.Join(' ', el.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString())),
        JsonValueKind.Array => string.Join(' ', el.EnumerateArray().Select(CollectText)),
        _ => "",
    };

    /// <summary>
    /// Fetch the 1830 edition as one chapter per source page. The transcription is page-markdown with
    /// running heads and editorial comments; both are stripped so only period prose remains.
    /// </summary>
    public static async Task<CorpusBuildResult> Fetch1830Async(string rawDir, bool force = false, int concurrency = 8)
    {
        Directory.CreateDirectory(rawDir);
        var cacheDir = Path.Combine(rawDir, "1830-pages");
        Directory.CreateDirectory(cacheDir);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("bomca/0.1");

        var treeJson = await http.GetStringAsync(Pages1830TreeUrl);
        var paths = Regex.Matches(treeJson, "\"path\":\\s*\"(book-of-mormon-1830-pages/page-[0-9]+\\.md)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var corpus = new CorpusFile
        {
            Edition = "1830",
            Source = "book-of-mormon-1830-pages (lds-restoration-documents GitHub)",
            SourceUrl = Pages1830TreeUrl,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
        };
        var book = new CorpusBook { Name = "1830 edition" };

        var semaphore = new SemaphoreSlim(concurrency);
        var tasks = paths.Select(async p =>
        {
            var local = Path.Combine(cacheDir, Path.GetFileName(p));
            if (force || !File.Exists(local))
            {
                await semaphore.WaitAsync();
                try
                {
                    var text = await http.GetStringAsync(Pages1830RawBase + p);
                    await File.WriteAllTextAsync(local, text);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            return local;
        }).ToArray();
        var files = await Task.WhenAll(tasks);

        var chapterNo = 0;
        foreach (var f in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            chapterNo++;
            var prose = Clean1830Page(File.ReadAllText(f));
            if (string.IsNullOrWhiteSpace(prose)) continue;
            var chapter = new CorpusChapter { Number = chapterNo };
            chapter.Verses.Add(new CorpusVerse
            {
                Number = 1,
                Ref = $"1830 p.{Regex.Match(Path.GetFileName(f), "[0-9]+").Value}",
                Text = prose,
            });
            book.Chapters.Add(chapter);
        }
        corpus.Books.Add(book);
        corpus.WordCount = corpus.Books.Sum(bk => bk.Chapters.Sum(c => c.Verses.Sum(v => Normalizer.Tokenize(v.Text).Count())));
        corpus.VerseCount = corpus.Books.Sum(bk => bk.Chapters.Sum(c => c.Verses.Count));

        var combined = Path.Combine(rawDir, "1830-combined.md");
        await File.WriteAllTextAsync(combined, string.Join("\n\n", files.OrderBy(f => f).Select(File.ReadAllText)));
        corpus.Sha256 = JsonIo.Sha256File(combined);
        return new CorpusBuildResult { File = corpus, RawPath = combined };
    }

    private static string Clean1830Page(string md)
    {
        var text = Regex.Replace(md, "<!--.*?-->", " ", RegexOptions.Singleline);
        var lines = text.Split('\n');
        var kept = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("**Header:**", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith("|") || line.StartsWith("---")) continue;
            kept.Add(line);
        }
        return string.Join(' ', kept).Replace("  ", " ").Trim();
    }
}
