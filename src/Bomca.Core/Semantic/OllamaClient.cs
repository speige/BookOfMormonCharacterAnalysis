using System.Text;
using System.Text.Json;
using Bomca.Core.Json;

namespace Bomca.Core.Semantic;

/// <summary>
/// Minimal Ollama client (no SDK dependency): generate, vision generate and embed, with an on-disk
/// response cache so re-runs are free and reproducible. Every call degrades gracefully to null.
/// </summary>
public sealed class OllamaClient
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    public string BaseUrl { get; }
    public string DefaultModel { get; }
    public string EmbedModel { get; }
    public bool Verbose { get; set; }
    public int CacheHits { get; private set; }
    public int Calls { get; private set; }

    public OllamaClient(string? cacheDir = null, string? baseUrl = null)
    {
        BaseUrl = baseUrl
                  ?? Environment.GetEnvironmentVariable("OLLAMA_HOST")
                  ?? "http://localhost:11434";
        if (!BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) BaseUrl = "http://" + BaseUrl;
        DefaultModel = Environment.GetEnvironmentVariable("BOMCA_LLM_MODEL") ?? "gemma4:12b-it-q4_K_M";
        EmbedModel = Environment.GetEnvironmentVariable("BOMCA_EMBED_MODEL") ?? "nomic-embed-text";
        _cacheDir = cacheDir ?? Path.Combine(Bomca.Core.Documents.DocumentCatalog.RepoRoot(), "out", "cache", "ollama");
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<bool> AvailableAsync()
    {
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl}/api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ModelsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync($"{BaseUrl}/api/tags");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models)) return new List<string>();
            return models.EnumerateArray()
                .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public Task<string?> GenerateAsync(string prompt, string? model = null, IReadOnlyList<byte[]>? images = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model ?? DefaultModel,
            ["prompt"] = prompt,
            ["stream"] = false,
        };
        if (images is { Count: > 0 })
            payload["images"] = images.Select(Convert.ToBase64String).ToList();
        return PostAsync("/api/generate", payload, resp =>
        {
            using var doc = JsonDocument.Parse(resp);
            return doc.RootElement.TryGetProperty("response", out var r) ? r.GetString() : null;
        });
    }

    /// <summary>Text-only generation with a hard guarantee that no image bytes are sent.</summary>
    public Task<string?> GenerateTextAsync(string prompt, string? model = null) => GenerateAsync(prompt, model);

    public async Task<float[]?> EmbedAsync(string text, string? model = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model ?? EmbedModel,
            ["input"] = text,
        };
        var result = await PostAsync("/api/embed", payload, resp =>
        {
            using var doc = JsonDocument.Parse(resp);
            if (doc.RootElement.TryGetProperty("embeddings", out var arr) && arr.GetArrayLength() > 0)
            {
                var emb = arr[0];
                return (object)emb.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            }
            return null;
        });
        return result as float[];
    }

    private async Task<T?> PostAsync<T>(string path, Dictionary<string, object> payload, Func<string, T?> extract)
        where T : class
    {
        var key = JsonIo.Sha256Text(JsonSerializer.Serialize(payload));
        var cachePath = Path.Combine(_cacheDir, key + ".json");
        if (File.Exists(cachePath))
        {
            CacheHits++;
            try
            {
                using var cached = JsonDocument.Parse(File.ReadAllText(cachePath));
                if (cached.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    return t.GetString() as T ?? (T?)(object?)null;
            }
            catch
            {
                // fall through and regenerate
            }
        }

        Calls++;
        try
        {
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(BaseUrl + path, body);
            if (!resp.IsSuccessStatusCode)
            {
                if (Verbose) Console.Error.WriteLine($"ollama {path}: HTTP {(int)resp.StatusCode}");
                return null;
            }
            var text = await resp.Content.ReadAsStringAsync();
            var value = extract(text);
            if (value is null) return null;
            Directory.CreateDirectory(_cacheDir);
            JsonIo.Write(cachePath, new { payload = key, text = value.ToString(), createdUtc = DateTime.UtcNow.ToString("o") });
            return value;
        }
        catch (Exception ex)
        {
            if (Verbose) Console.Error.WriteLine($"ollama {path}: {ex.Message}");
            return null;
        }
    }
}
