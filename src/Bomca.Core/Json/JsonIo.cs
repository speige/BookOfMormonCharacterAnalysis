using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bomca.Core.Json;

public static class JsonIo
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };

    public static T Read<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
    }

    public static void Write<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static string Sha256Text(string text)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }

    /// <summary>Round to n decimals so artifacts are stable and diffable.</summary>
    public static double Round(double v, int digits = 4) => Math.Round(v, digits, MidpointRounding.AwayFromZero);
}
