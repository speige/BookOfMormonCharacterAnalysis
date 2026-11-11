using Bomca.Core.Corpus;
using Bomca.Core.Clustering;
using Bomca.Core.Documents;
using Bomca.Core.External;
using Bomca.Core.Reporting;
using Bomca.Core.Json;
using Bomca.Core.Model;

namespace Bomca.Cli;

public static partial class App
{
    public static int RunVerify(Args args)
    {
        var source = args.Get("source", "grover");
        var claimsPath = Path.Combine(DocumentCatalog.RepoRoot(), "data", "external", $"{source}-claims.json");
        if (!File.Exists(claimsPath))
        {
            Console.Error.WriteLine($"verify: no claim file at {claimsPath}");
            return 1;
        }
        var claims = ClaimVerifier.Load(claimsPath);

        var code = args.GetList("doc").FirstOrDefault() ?? "W";
        var (doc, dir) = LoadDocument(code);
        var (inventory, _) = LoadInventory(code, args.Has("threshold") ? args.GetDouble("threshold", 0.8) : null);

        var corpusPath = Path.Combine(DerivedDir, "current.json");
        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine("verify: corpus missing (run: bomca corpus fetch)");
            return 1;
        }
        var corpus = CorpusIndex.Build(JsonIo.Read<CorpusFile>(corpusPath));

        var result = new ClaimVerificationResult
        {
            Doc = code,
            Source = claims.Source,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            TableInconsistencies = claims.NumeralClaimInconsistencies,
        };

        // Direction: the distance/position scanners are invariant under reversal, the aligner is not.
        result.DirectionConsistentWithScanners = true;
        result.DirectionNote =
            $"Claimed reading direction: {claims.Direction}. Fingerprint and window scanners compare inter-occurrence " +
            "distances, which are invariant under reversal, so the search result does not depend on the claimed " +
            "direction. Only the alignment scanner is direction-sensitive; use 'bomca match --reverse' to test it.";

        var glyphs = doc.Symbols.Where(s => s.Note != "title-row").ToList();

        // 1. Numeral runs -------------------------------------------------
        var claimedNumeralPositions = claims.NumeralClaims.Sum(c => c.Ids.Count);
        var numeralTake = Math.Clamp(claimedNumeralPositions, 8, Math.Max(8, glyphs.Count / 3));
        var numeralGlyphs = ClaimVerifier.ScoreNumerals(glyphs, numeralTake);
        result.NumeralLikeGlyphs = numeralGlyphs;
        var (observedRuns, nullRuns, p, z) = ClaimVerifier.NumeralRunTest(
            numeralGlyphs.Select(n => n.GlobalIndex).ToList(), glyphs.Count,
            args.GetInt("iterations", 500), args.GetInt("seed", 424242));
        result.ObservedRuns = observedRuns;
        result.NullRuns = nullRuns;
        result.NumeralRunPValue = p;
        result.NumeralRunZ = z;
        result.NumeralRunVerdict = p <= 0.05
            ? $"The {numeralGlyphs.Count} most numeral-like glyphs cluster into runs (mean run {observedRuns.MeanRun:0.00}, longest {observedRuns.MaxRun}) far more than chance (mean {nullRuns.MeanRun:0.00}); p = {p}. This supports reading consecutive glyph groups as numeral sequences."
            : $"The {numeralGlyphs.Count} most numeral-like glyphs do not cluster (mean run {observedRuns.MeanRun:0.00} vs chance {nullRuns.MeanRun:0.00}; p = {p}). The claim that they form multi-glyph numeral sequences is not supported by the glyph shapes alone.";

        // 1b. Positional numeral test (uses the claim's own character numbering) -----
        result.PositionalNumerals = ClaimVerifier.PositionalNumerals(
            ClaimVerifier.NumeralScores(glyphs), claims.Segments, claims.NumeralClaims,
            args.GetInt("iterations", 500), args.GetInt("seed", 999));
        if (result.PositionalNumerals is { } pn)
            result.Findings.Add($"Positional numeral test: {pn.ClaimedNumerals} claimed numeral positions, mean numeral-likeness {pn.MeanScoreAtClaimed:0.000} vs {pn.MeanScoreRandomSameCount:0.000} at random positions (p = {pn.PValue}, z = {pn.Z}). {pn.Verdict}");

        // 2. Text claims --------------------------------------------------
        result.TextClaims = ClaimVerifier.CheckTextClaims(corpus, claims.TextClaims);

        // 3. Segment comparison -------------------------------------------
        result.Segments = ClaimVerifier.CompareSegments(doc, inventory, claims.Segments);

        // 4. Anchor windows ----------------------------------------------
        var regionsBySegment = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["topFourLines"] = new[] { "Mosiah", "Omni", "Words of Mormon", "1 Nephi", "2 Nephi", "Jacob", "Enos", "Jarom" },
            ["bottomThreeLines"] = new[] { "3 Nephi", "4 Nephi", "Mormon", "Moroni", "Helaman" },
        };
        var anchorResults = new List<AnchorWindowResult>();
        foreach (var seg in claims.Segments)
        {
            var regions = regionsBySegment.TryGetValue(seg.Name, out var r) ? r : Array.Empty<string>();
            anchorResults.AddRange(ClaimVerifier.AnchorWindows(corpus, new[] { seg }, regions,
                args.GetInt("window", 220), args.GetInt("iterations", 200), args.GetInt("seed", 777)));
        }
        result.AnchorWindows = anchorResults;

        // 5. Findings -----------------------------------------------------
        var consistent = result.TextClaims.Count(t => t.Status == "consistent");
        result.Findings.Add($"{consistent}/{result.TextClaims.Count} of the claimed calendar/chronology statements are matched by verses the corpus actually contains at the expected reference.");
        foreach (var t in result.TextClaims.Where(t => t.Status != "consistent"))
            result.Findings.Add($"Not corroborated: {t.Claim} (pattern '{t.Pattern}' -> {t.Status}, {t.Occurrences} occurrence(s), refs {string.Join(", ", t.FoundRefs.Take(3))}).");
        result.Findings.Add(result.NumeralRunVerdict);
        foreach (var s in result.Segments)
            result.Findings.Add($"{s.Name}: {s.Glyphs} glyphs, median height {s.MedianGlyphHeight}, {s.Classes} classes, {s.ClassOverlapWithOtherSegment:P0} of its classes also appear in the other segment. {s.Reading}");
        foreach (var a in result.AnchorWindows)
            result.Findings.Add($"Anchor probe {a.Segment}: best window {a.Reference} with {a.Hits} hits ({a.DistinctTerms} distinct terms, control mean {a.ControlMeanHits}, p {a.PValue}) -> {a.Verdict}.");
        if (claims.NumeralClaimInconsistencies.Count > 0)
            result.Findings.Add($"{claims.NumeralClaimInconsistencies.Count} internal inconsistencies exist in the published character/number tables (see list).");

        var textEvidence = consistent >= Math.Max(3, result.TextClaims.Count - 1);
        var runEvidence = p <= 0.05;
        result.OverallAssessment =
            $"Text/chronology side: {(textEvidence ? "strong internal consistency" : "partial consistency")} ({consistent}/{result.TextClaims.Count} claims matched). " +
            $"Glyph side: {(runEvidence ? "numeral-run structure is present in the document" : "no numeral-run structure was detected")} (p = {p}). " +
            (textEvidence && runEvidence
                ? "The claim is internally consistent and its glyph-side prediction holds; this is supporting evidence, not proof, because a chronologically informed translator could produce the same numbers by construction."
                : "The claim is not supported by the independent glyph-side test; its textual consistency alone cannot distinguish a real reading from a text-informed reconstruction.");

        JsonIo.Write(Path.Combine(dir, $"verify-{source}.json"), result);
        ClaimReportBuilder.Write(Path.Combine(dir, $"verify-{source}.html"), result, corpus);

        Console.WriteLine($"verify {source} vs {code}: numeral runs p={result.NumeralRunPValue} (z={result.NumeralRunZ}), " +
                          $"text claims {consistent}/{result.TextClaims.Count} consistent, {result.AnchorWindows.Count} anchor probes");
        foreach (var f in result.Findings) Console.WriteLine("  * " + f);
        Console.WriteLine("  ASSESSMENT: " + result.OverallAssessment);
        return 0;
    }
}
