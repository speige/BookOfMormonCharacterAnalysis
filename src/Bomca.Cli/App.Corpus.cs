using Bomca.Core.Corpus;
using Bomca.Core.Documents;
using Bomca.Core.Json;

namespace Bomca.Cli;

public static partial class App
{
    public static string DerivedDir => Path.Combine(DocumentCatalog.RepoRoot(), "data", "derived", "corpus");
    public static string RawCorpusDir => Path.Combine(DocumentCatalog.RepoRoot(), "data", "raw", "corpus");

    public static async Task<int> RunCorpus(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "build";
        switch (sub)
        {
            case "fetch":
                return await CorpusFetch(args);
            case "build":
                return CorpusBuild(args);
            case "stats":
                return CorpusStats(args);
            default:
                Console.Error.WriteLine($"corpus: unknown subcommand '{sub}' (fetch|build|stats)");
                return 2;
        }
    }

    private static async Task<int> CorpusFetch(Args args)
    {
        var editions = args.GetList("edition");
        if (editions.Count == 0) editions = new List<string> { "current" };
        if (editions.Contains("both")) editions = new List<string> { "current", "1830" };
        Directory.CreateDirectory(DerivedDir);

        foreach (var edition in editions)
        {
            CorpusBuildResult result = edition switch
            {
                "current" => await CorpusSource.FetchCurrentAsync(RawCorpusDir, args.Has("force")),
                "1830" => await CorpusSource.Fetch1830Async(RawCorpusDir, args.Has("force")),
                _ => throw new ArgumentException($"unknown edition '{edition}'"),
            };
            var path = Path.Combine(DerivedDir, $"{edition}.json");
            JsonIo.Write(path, result.File);
            Console.WriteLine($"corpus {edition}: {result.File.WordCount} words, {result.File.VerseCount} verses -> {path}");
        }
        return 0;
    }

    private static int CorpusBuild(Args args)
    {
        var editions = args.GetList("edition");
        if (editions.Count == 0) editions = new List<string> { "current" };
        var outDir = Path.Combine(OutRoot, "corpus");
        Directory.CreateDirectory(outDir);

        CorpusIndex? current = null;
        var summary = new List<object>();

        foreach (var edition in editions)
        {
            var path = Path.Combine(DerivedDir, $"{edition}.json");
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"corpus {edition}: missing {path} (run: bomca corpus fetch --edition {edition})");
                return 1;
            }
            var file = JsonIo.Read<CorpusFile>(path);
            var index = CorpusIndex.Build(file);
            if (edition == "current") current = index;

            var checks = new List<CorpusSanityCheck>
            {
                Check(index, "verses", index.Verses.Count, 6600, 6700),
                Check(index, "words", index.Length, 260000, 275000),
                Check(index, "and-it-came-to-pass", index.CountOf("and it came to pass"), 900, 1600),
                Check(index, "and-it-came-to-pass-that", index.CountOf("and it came to pass that"), 300, 1200),
            };

            var lookups = new Dictionary<string, CorpusLookup>
            {
                ["and it came to pass"] = Lookup(index, "and it came to pass"),
                ["and it came to pass that"] = Lookup(index, "and it came to pass that"),
                ["and behold"] = Lookup(index, "and behold"),
                ["and the lord"] = Lookup(index, "and the lord"),
                ["interpreter of languages"] = Lookup(index, "interpreter of languages"),
                ["interpreters of languages"] = Lookup(index, "interpreters of languages"),
                ["interpreters"] = Lookup(index, "interpreters"),
                ["urim and thummim"] = Lookup(index, "urim and thummim"),
                ["urim"] = Lookup(index, "urim"),
                ["thummim"] = Lookup(index, "thummim"),
                ["the book of mormon"] = Lookup(index, "the book of mormon"),
                ["brethren adieu"] = Lookup(index, "brethren adieu"),
                ["adieu"] = Lookup(index, "adieu"),
                ["it grieveth me that I should lose this tree"] = Lookup(index, "it grieveth me that I should lose this tree"),
            };

            var topWords = index.FrequentWords(2).Take(60).Select(w => new { w.Word, w.Count }).ToList();
            var phrases = new List<object>();
            foreach (var n in new[] { 2, 3, 4, 5, 6 })
                phrases.AddRange(index.FrequentPhrases(n, 3, 15).Select(p => new { n, p.Phrase, p.Count }));

            JsonIo.Write(Path.Combine(outDir, $"stats-{edition}.json"), new
            {
                edition,
                wordCount = index.Length,
                verseCount = index.Verses.Count,
                distinctWords = index.UnigramCounts.Count,
                checks,
                lookups,
                topWords,
                topPhrases = phrases,
            });

            summary.Add(new { edition, words = index.Length, verses = index.Verses.Count, distinct = index.UnigramCounts.Count, checksOk = checks.All(c => c.Ok) });
            Console.WriteLine($"corpus build {edition}: {index.Length} tokens, {index.Verses.Count} verses, {index.UnigramCounts.Count} distinct; checks {(checks.All(c => c.Ok) ? "OK" : "FAILED")}");

            foreach (var c in checks.Where(c => !c.Ok))
                Console.WriteLine($"  check failed: {c.Name} = {c.Actual} (expected {c.Min}..{c.Max})");
        }

        // Edition drift: how many tokens differ between 1830 and current, ignoring punctuation.
        var path1830 = Path.Combine(DerivedDir, "1830.json");
        if (File.Exists(path1830) && current is not null)
        {
            var old = CorpusIndex.Build(JsonIo.Read<CorpusFile>(path1830));
            var a = old.Tokens;
            var b = current.Tokens;
            double same = 0;
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++) if (a[i] == b[i]) same++;
            var drift = new
            {
                tokens1830 = a.Length,
                tokensCurrent = b.Length,
                alignedPrefix = n,
                identicalPositions = same,
                identicalRate = JsonIo.Round(same / Math.Max(1.0, n), 4),
                note = "Positional comparison only: 1830 has no verse numbers, so gaps shift the tail.",
            };
            JsonIo.Write(Path.Combine(outDir, "edition-drift.json"), drift);
            Console.WriteLine($"edition drift: {same}/{n} aligned tokens identical ({drift.identicalRate:P1})");
        }

        JsonIo.Write(Path.Combine(outDir, "index.json"), summary);
        return 0;
    }

    private static int CorpusStats(Args args)
    {
        var path = Path.Combine(DerivedDir, "current.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("corpus stats: no corpus yet (run corpus fetch first)");
            return 1;
        }
        var index = CorpusIndex.Build(JsonIo.Read<CorpusFile>(path));
        Console.WriteLine($"distinct words: {index.UnigramCounts.Count}");
        foreach (var (word, count) in index.FrequentWords(2).Take(25))
            Console.WriteLine($"  {count,7}  {word}");
        return 0;
    }

    private static CorpusSanityCheck Check(CorpusIndex index, string name, int actual, int min, int max)
        => new() { Name = name, Actual = actual, Min = min, Max = max, Ok = actual >= min && actual <= max };

    private static CorpusLookup Lookup(CorpusIndex index, string phrase)
    {
        var positions = index.PositionsOf(Normalizer.NormalizeText(phrase));
        return new CorpusLookup
        {
            Phrase = phrase,
            Count = positions.Count,
            FirstRefs = positions.Take(5).Select(index.ReferenceAt).ToList(),
        };
    }

    public sealed class CorpusSanityCheck
    {
        public string Name { get; set; } = "";
        public int Actual { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public bool Ok { get; set; }
    }

    public sealed class CorpusLookup
    {
        public string Phrase { get; set; } = "";
        public int Count { get; set; }
        public List<string> FirstRefs { get; set; } = new();
    }
}
