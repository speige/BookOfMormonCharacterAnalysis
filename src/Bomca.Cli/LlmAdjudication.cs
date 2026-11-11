using Bomca.Core.Clustering;
using Bomca.Core.Documents;
using Bomca.Core.Model;
using Bomca.Core.Semantic;

namespace Bomca.Cli;

/// <summary>
/// Optional adjudication of borderline glyph pairs with a locally hosted model (gemma is multimodal, so
/// the two crops are sent as images). Classical scores remain authoritative when the model is missing;
/// verdicts are cached on disk by the Ollama client.
/// </summary>
public static class LlmAdjudication
{
    public static async Task<int> AdjudicateAsync(
        GlyphComparer comparer,
        List<GlyphView> views,
        List<GlyphPair> pairs,
        Args args,
        Action<string>? log = null)
    {
        var lo = args.GetDouble("llm-low", 0.55);
        var hi = args.GetDouble("llm-high", 0.85);
        var max = args.GetInt("llm-max", 60);

        var client = new OllamaClient();
        if (!await client.AvailableAsync())
        {
            log?.Invoke("llm: Ollama not reachable; skipping adjudication (classical scores stand)");
            return 0;
        }

        var models = await client.ModelsAsync();
        var model = client.DefaultModel;
        if (models.Count > 0 && !models.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase)))
        {
            model = models.FirstOrDefault(m => m.Contains("gemma", StringComparison.OrdinalIgnoreCase)) ?? models[0];
            log?.Invoke($"llm: model '{client.DefaultModel}' not installed; using '{model}'");
        }

        var byId = views.ToDictionary(v => v.Occ.Id, v => v, StringComparer.Ordinal);
        var targets = pairs.Where(p => p.Breakdown.Combined >= lo && p.Breakdown.Combined < hi).Take(max).ToList();
        var changed = 0;

        foreach (var pair in targets)
        {
            if (!byId.TryGetValue(pair.A, out var a) || !byId.TryGetValue(pair.B, out var b)) continue;
            var bytesA = TryReadCrop(a.Occ);
            var bytesB = TryReadCrop(b.Occ);
            if (bytesA is null || bytesB is null) continue;

            var prompt =
                "Two cropped images from a 19th-century handwritten document of unknown characters.\n" +
                "Image 1 and image 2 may be (a) the same character written twice by the same hand,\n" +
                "(b) two different characters that share a visual style, or (c) one character and a drawing slip.\n" +
                "Judge only SHAPE: overall structure, stroke order, proportions, distinctive marks.\n" +
                "Ignore ink darkness, paper tone, size and small rotation.\n" +
                "Answer with JSON only: same=true|false, confidence=0.0-1.0, reason=<=12 words";

            var response = await client.GenerateAsync(prompt, model, new[] { bytesA, bytesB });
            if (string.IsNullOrWhiteSpace(response)) continue;
            pair.Method = "classical+llm:" + model;
            pair.LlmVerdict = response.Replace("\n", " ").Trim();
            var verdict = ParseVerdict(response);
            if (verdict is not { } v) continue;
            if (!v.Same && pair.Kind == "same-symbol") { pair.Kind = "variant-of"; changed++; }
            if (v.Same && pair.Kind == "similar-style") { pair.Kind = "variant-of"; changed++; }
        }

        log?.Invoke($"llm: adjudicated {targets.Count} borderline pairs with {model} ({client.Calls} calls, {client.CacheHits} cache hits), {changed} kind changes");
        return targets.Count;
    }

    private static byte[]? TryReadCrop(SymbolOccurrence occ)
    {
        var root = Path.Combine(DocumentCatalog.RepoRoot(), "out", occ.Doc);
        var path = Path.Combine(root, occ.CropPath);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Parses the model's answer without regex escapes: whitespace is stripped first.</summary>
    internal static (bool Same, double Confidence)? ParseVerdict(string text)
    {
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '"' && c != '\'').ToArray())
            .ToLowerInvariant();

        var i = compact.IndexOf("same:", StringComparison.Ordinal);
        if (i < 0) i = compact.IndexOf("same=", StringComparison.Ordinal);
        if (i < 0) return null;
        var rest = compact[(i + 5)..];
        var isSame = rest.StartsWith("true", StringComparison.Ordinal);

        double confidence = 0.5;
        var ci = compact.IndexOf("confidence:", StringComparison.Ordinal);
        if (ci < 0) ci = compact.IndexOf("confidence=", StringComparison.Ordinal);
        if (ci >= 0)
        {
            var start = ci + "confidence:".Length;
            var end = start;
            while (end < compact.Length && (char.IsDigit(compact[end]) || compact[end] == '.')) end++;
            if (double.TryParse(compact[start..end], System.Globalization.CultureInfo.InvariantCulture, out var c))
                confidence = c;
        }
        return (isSame, confidence);
    }
}
