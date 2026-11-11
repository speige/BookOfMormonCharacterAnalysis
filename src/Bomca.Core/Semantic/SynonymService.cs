using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bomca.Core.Semantic;

/// <summary>
/// Vocabulary-constrained synonym expansion: the model may only choose words that already exist in the
/// corpus, which keeps the search space finite and every expansion auditable. Fails closed (returns the
/// input only) when the model is unavailable.
/// </summary>
public sealed class SynonymService
{
    private readonly OllamaClient _client;
    public bool Enabled { get; }

    public SynonymService(OllamaClient? client = null, bool enabled = true)
    {
        _client = client ?? new OllamaClient();
        Enabled = enabled && Environment.GetEnvironmentVariable("BOMCA_NO_LLM") != "1";
    }

    /// <summary>Built-in fallbacks so the widening step still works without a model.</summary>
    private static readonly Dictionary<string, string[]> Offline = new(StringComparer.Ordinal)
    {
        ["god"] = new[] { "lord", "father", "almighty", "jehovah" },
        ["lord"] = new[] { "god", "master" },
        ["angel"] = new[] { "angels", "messenger", "messengers" },
        ["angels"] = new[] { "angel", "messengers" },
        ["earth"] = new[] { "land", "world", "ground" },
        ["son"] = new[] { "sons", "child", "children" },
        ["interpreter"] = new[] { "interpreters", "translation", "urim", "thummim" },
        ["interpreters"] = new[] { "interpreter", "translation" },
        ["book"] = new[] { "record", "records", "plates" },
        ["people"] = new[] { "nation", "nations", "multitude" },
        ["destroy"] = new[] { "destruction", "slay", "perish" },
        ["came"] = new[] { "come", "went", "go" },
        ["behold"] = new[] { "see", "look" },
    };

    /// <summary>The built-in synonym table, exposed so the contextual search can run with no model.</summary>
    public static Dictionary<string, string[]> OfflineFallback() => Offline;

    public async Task<List<string>> ExpandAsync(string word, IReadOnlyCollection<string> vocabulary, int max = 12)
    {
        var results = new HashSet<string>(StringComparer.Ordinal) { word };
        foreach (var w in Offline.TryGetValue(word, out var list) ? list : Array.Empty<string>())
            if (vocabulary.Contains(w)) results.Add(w);

        if (!Enabled) return results.Take(max).ToList();
        if (!await _client.AvailableAsync()) return results.Take(max).ToList();

        var vocab = vocabulary.Where(v => v.Length > 1).OrderBy(v => v, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("You are a careful lexicographer working on an early-19th-century English religious text.");
        sb.AppendLine($"Target word: \"{word}\"");
        sb.AppendLine("From the vocabulary list below, choose words that could stand in for the target word in that text");
        sb.AppendLine("(synonyms, near-synonyms, singular/plural forms, or the same concept worded differently).");
        sb.AppendLine("Rules: use ONLY words that appear in the list; never invent words; return at most " + max + " words;");
        sb.AppendLine("return a JSON array of lowercase strings and nothing else.");
        sb.AppendLine();
        sb.AppendLine("VOCABULARY:");
        sb.AppendLine(string.Join(' ', vocab));

        var response = await _client.GenerateTextAsync(sb.ToString());
        if (string.IsNullOrWhiteSpace(response)) return results.Take(max).ToList();

        foreach (Match m in Regex.Matches(response, "\"([a-z]{2,20})\""))
        {
            var candidate = m.Groups[1].Value;
            if (vocabulary.Contains(candidate)) results.Add(candidate);
        }
        foreach (Match m in Regex.Matches(response, "\\b([a-z]{2,20})\\b"))
        {
            var candidate = m.Groups[1].Value;
            if (vocabulary.Contains(candidate)) results.Add(candidate);
        }

        return results.Take(max).ToList();
    }

    /// <summary>Expand a phrase by expanding each token independently, then re-joining.</summary>
    public async Task<List<string>> ExpandPhraseAsync(string phrase, IReadOnlyCollection<string> vocabulary, int maxPerWord = 4)
    {
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var options = new List<List<string>>();
        foreach (var p in parts)
        {
            var expanded = await ExpandAsync(p, vocabulary, maxPerWord);
            options.Add(expanded);
        }

        var combos = new List<string> { "" };
        foreach (var opts in options)
        {
            var next = new List<string>();
            foreach (var prefix in combos)
            {
                foreach (var o in opts)
                {
                    var joined = prefix.Length == 0 ? o : prefix + " " + o;
                    next.Add(joined);
                    if (next.Count >= 48) break;
                }
                if (next.Count >= 48) break;
            }
            combos = next;
        }
        return combos.Distinct().ToList();
    }
}
