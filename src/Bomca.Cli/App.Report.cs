using Bomca.Core.Analysis;
using Bomca.Core.Corpus;
using Bomca.Core.Documents;
using Bomca.Core.Json;
using Bomca.Core.Matching;
using Bomca.Core.Model;
using Bomca.Core.Reporting;
using Bomca.Core.Significance;

namespace Bomca.Cli;

public static partial class App
{
    public static int RunReport(Args args)
    {
        var codes = args.GetList("doc");
        if (codes.Count == 0) codes = new List<string> { "W", "B", "C", "FW" };

        var input = new ReportBuilder.Input { GitSha = TryGitSha() };

        var corpusPath = Path.Combine(DerivedDir, "current.json");
        if (File.Exists(corpusPath))
            input.Corpus = CorpusIndex.Build(JsonIo.Read<CorpusFile>(corpusPath));

        foreach (var code in codes)
        {
            var dir = Path.Combine(OutRoot, code);
            var docPath = Path.Combine(dir, "document.json");
            if (!File.Exists(docPath)) continue;
            input.Documents.Add(JsonIo.Read<DocumentRecord>(docPath));

            var versionsPath = Path.Combine(dir, "inventory-versions.json");
            if (File.Exists(versionsPath))
            {
                foreach (var v in JsonIo.Read<List<Dictionary<string, object>>>(versionsPath))
                {
                    var file = v.TryGetValue("file", out var f) ? f?.ToString() : null;
                    if (file is null) continue;
                    var invPath = Path.Combine(dir, file);
                    if (!File.Exists(invPath)) continue;
                    var inv = JsonIo.Read<InventoryVersion>(invPath);
                    // Only surface a manageable number of versions in the report.
                    input.Inventories.Add(inv);
                }
            }

            var analysisPath = Path.Combine(dir, "analysis.json");
            if (File.Exists(analysisPath)) input.Analyses.Add(JsonIo.Read<StructureAnalysis>(analysisPath));

            var matchPath = Path.Combine(dir, "matches.json");
            if (File.Exists(matchPath)) input.Matches.Add(JsonIo.Read<MatchResult>(matchPath));

            var sigPath = Path.Combine(dir, "significance.json");
            if (File.Exists(sigPath)) input.Significance.Add(JsonIo.Read<SignificanceReport>(sigPath));
        }

        input.ClaimLedger = LoadClaimLedger();
        var reportPath = Path.Combine(OutRoot, "report.html");
        ReportBuilder.Write(reportPath, input, OutRoot);
        Console.WriteLine($"report: {reportPath} ({input.Documents.Count} documents, {input.Analyses.Count} analyses, {input.Matches.Count} match runs, {input.Significance.Count} significance runs)");
        return 0;
    }

    private static List<string> LoadClaimLedger()
    {
        var path = Path.Combine(DocumentCatalog.RepoRoot(), "docs", "claim-ledger.md");
        if (!File.Exists(path)) return new List<string>();
        var claims = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal)) continue;
            var body = trimmed[2..].Trim();
            if (body.Contains('|')) claims.Add(body);
        }
        return claims;
    }

    private static string? TryGitSha()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                WorkingDirectory = DocumentCatalog.RepoRoot(),
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
