using Bomca.Core.Json;

namespace Bomca.Cli;

/// <summary>Append-only run log so every artifact is traceable to its inputs, thresholds and git state.</summary>
public sealed class RunManifest
{
    public string Verb { get; set; } = "";
    public string StartedUtc { get; set; } = "";
    public string FinishedUtc { get; set; } = "";
    public int DurationMs { get; set; }
    public string? GitSha { get; set; }
    public string ToolVersion { get; set; } = "0.1.0";
    public Dictionary<string, string> Arguments { get; set; } = new();
    public List<string> Inputs { get; set; } = new();
    public Dictionary<string, string> Outputs { get; set; } = new();
    public List<string> Log { get; set; } = new();
    public string? Status { get; set; }
    public string? Error { get; set; }
}

public static class ManifestStore
{
    public static string Root => Path.Combine(Bomca.Core.Documents.DocumentCatalog.RepoRoot(), "out");

    public static RunManifest Start(string verb, IEnumerable<string> argv)
    {
        var m = new RunManifest
        {
            Verb = verb,
            StartedUtc = DateTime.UtcNow.ToString("o"),
            GitSha = TryGitSha(),
        };
        var arr = argv.ToArray();
        for (var i = 0; i < arr.Length; i++)
        {
            if (!arr[i].StartsWith("--")) continue;
            var key = arr[i][2..].Split('=')[0];
            var hasValue = arr[i].Contains('=') || (i + 1 < arr.Length && !arr[i + 1].StartsWith("--"));
            m.Arguments[key] = arr[i].Contains('=') ? arr[i].Split('=', 2)[1]
                : hasValue ? arr[i + 1] : "true";
        }
        return m;
    }

    public static void Finish(RunManifest m, string? error = null)
    {
        m.FinishedUtc = DateTime.UtcNow.ToString("o");
        m.DurationMs = (int)(DateTime.Parse(m.FinishedUtc) - DateTime.Parse(m.StartedUtc)).TotalMilliseconds;
        if (error is null)
        {
            m.Status = "ok";
        }
        else
        {
            m.Status = "error";
            m.Error = error;
        }

        var path = Path.Combine(Root, "runs.json");
        var all = JsonIo.Read<List<RunManifest>>(path);
        all.Add(m);
        JsonIo.Write(path, all);
    }

    private static string? TryGitSha()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                WorkingDirectory = Bomca.Core.Documents.DocumentCatalog.RepoRoot(),
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var s = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }
}
