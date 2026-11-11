using Bomca.Core.Model;
using Bomca.Core.Segmentation;

namespace Bomca.Core.Documents;

public sealed class DocumentSpec
{
    public string Code = "";
    public string Name = "";
    public string Kind = "";
    /// <summary>One image = one document. When multiple, each becomes a row of the same document.</summary>
    public string[] Images = Array.Empty<string>();
    public bool SingleRow;
    public SegmentationParams Params = new();
    public string? SourceUrl;
    public string? Notes;
    /// <summary>Normalised coordinates for the four labelled Cowdery/Williams glyphs live in a review file.</summary>
    public string? RegionFile;
}

/// <summary>
/// The five datasets distinguished in the research notes. Only W (primary), B (secondary) and the
/// labelled pairs (calibration) are digitised; the Pure Language / Egyptian Alphabet material stays
/// behind a flag so it can never leak into the decipherment corpus by accident.
/// </summary>
public static class DocumentCatalog
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bomca.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    public static string RawDir => Path.Combine(RepoRoot(), "data", "raw");
    public static string ReviewDir => Path.Combine(RepoRoot(), "review");

    public static DocumentSpec? Get(string code) => code.ToUpperInvariant() switch
    {
        "W" => new DocumentSpec
        {
            Code = "W",
            Name = "Caractors (John Whitmer copy)",
            Kind = "plate-transcription",
            Images = new[] { Path.Combine(RawDir, "caractors-whitmer.jpg") },
            SourceUrl = "https://churchhistorycatalog.churchofjesuschrist.org/",
            Notes = "Primary dataset. Whitmer copy of an earlier transcription of Book of Mormon characters.",
            Params = new SegmentationParams
            {
                SkipTitleRow = true,
                MergeGapRatio = 0.10,
                MinComponentArea = 40,
                RowMinPeakDistancePx = 90,
                InkThresholdFactor = 0.72,
            },
        },
        "B" => new DocumentSpec
        {
            Code = "B",
            Name = "Stick of Joseph broadside (1844)",
            Kind = "broadside",
            Images = new[]
            {
                Path.Combine(RawDir, "broadside", "stick-of-joseph-line1.png"),
                Path.Combine(RawDir, "broadside", "stick-of-joseph-line2.png"),
                Path.Combine(RawDir, "broadside", "stick-of-joseph-line3.png"),
            },
            SingleRow = true,
            SourceUrl = "https://www.josephsmithpapers.org/",
            Notes = "Secondary dataset. Three engraved lines said to be copied from the plates.",
            Params = new SegmentationParams
            {
                SkipTitleRow = false,
                MergeGapRatio = 0.12,
                MinComponentArea = 25,
                InkThresholdFactor = 0.72,
            },
        },
        "C" => new DocumentSpec
        {
            Code = "C",
            Name = "Oliver Cowdery character document",
            Kind = "labeled-pairs",
            Images = new[] { Path.Combine(RawDir, "labeled", "cowdery-characters.jpg") },
            SourceUrl = "https://www.josephsmithpapers.org/",
            Notes = "Calibration dataset: two unknown characters labelled 'The Book of Mormon', two labelled 'The interpreters of languages'.",
            RegionFile = Path.Combine(ReviewDir, "regions-C.json"),
            Params = new SegmentationParams { SkipTitleRow = false, MinComponentArea = 40 },
        },
        "FW" => new DocumentSpec
        {
            Code = "FW",
            Name = "Frederick G. Williams character document",
            Kind = "labeled-pairs",
            Images = new[] { Path.Combine(RawDir, "labeled", "williams-characters.jpg") },
            SourceUrl = "https://www.josephsmithpapers.org/",
            Notes = "Independent copy of the Cowdery labelled pairs; useful for stroke-level comparison.",
            RegionFile = Path.Combine(ReviewDir, "regions-FW.json"),
            Params = new SegmentationParams { SkipTitleRow = false, MinComponentArea = 40 },
        },
        _ => null,
    };

    public static IEnumerable<string> AllCodes => new[] { "W", "B", "C", "FW" };

    public static List<DocumentRecord> Extract(string code, string outRoot, Action<string>? log = null)
    {
        var spec = Get(code) ?? throw new ArgumentException($"unknown document code '{code}'");
        var results = new List<DocumentRecord>();
        var missing = spec.Images.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0)
        {
            log?.Invoke($"skip {code}: missing image(s) {string.Join(", ", missing.Select(Path.GetFileName))}");
            return results;
        }

        if (spec.Images.Length == 1)
        {
            var manual = LoadManualBoxes(spec, log);
            var segmenter = new PageSegmenter();
            var doc = segmenter.Run(new PageSegmenter.Options
            {
                DocCode = spec.Code,
                Name = spec.Name,
                Kind = spec.Kind,
                SourceUrl = spec.SourceUrl,
                Notes = spec.Notes,
                ImagePath = spec.Images[0],
                Params = spec.Params,
                SingleRow = spec.SingleRow,
                ManualGlyphBoxes = manual ?? new List<Rect>(),
                Debug = Environment.GetEnvironmentVariable("BOMCA_DEBUG") == "1",
            }, Path.Combine(outRoot, spec.Code));
            results.Add(doc);
            log?.Invoke($"{code}: {doc.Symbols.Count(s => s.Note != "title-row")} glyphs in {doc.Rows.Count(r => !r.Skipped)} rows");
            return results;
        }

        // Multiple images -> one multi-row document.
        var combined = new DocumentRecord
        {
            DocCode = spec.Code,
            Name = spec.Name,
            Kind = spec.Kind,
            SourceImage = string.Join("; ", spec.Images.Select(Path.GetFileName)),
            SourceUrl = spec.SourceUrl,
            Notes = spec.Notes,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Params = spec.Params,
        };
        var outDir = Path.Combine(outRoot, spec.Code);
        var gIdx = 0;
        for (var i = 0; i < spec.Images.Length; i++)
        {
            var seg = new PageSegmenter().Run(new PageSegmenter.Options
            {
                DocCode = spec.Code,
                Name = spec.Name,
                Kind = spec.Kind,
                ImagePath = spec.Images[i],
                Params = spec.Params,
                SingleRow = true,
                Notes = spec.Notes,
                Debug = Environment.GetEnvironmentVariable("BOMCA_DEBUG") == "1",
            }, outDir);
            seg.Sha256 = null;
            foreach (var sym in seg.Symbols)
            {
                sym.Row = i;
                var parts = sym.Id.Split('-');
                sym.Id = $"{spec.Code}-L{i + 1:00}-{parts[2]}";
                sym.GlobalIndex = gIdx++;
                combined.Symbols.Add(sym);
            }
            foreach (var row in seg.Rows)
            {
                row.Index = i;
                row.RowId = $"{spec.Code}-L{i + 1:00}";
                row.Kind = "text";
                combined.Rows.Add(row);
            }
            combined.ImageWidth = Math.Max(combined.ImageWidth, seg.ImageWidth);
            combined.ImageHeight += seg.ImageHeight;
            combined.Gaps.AddRange(seg.Gaps);
        }
        results.Add(combined);
        log?.Invoke($"{code}: {combined.Symbols.Count} glyphs in {combined.Rows.Count} rows");
        return results;
    }

    private static List<Rect>? LoadManualBoxes(DocumentSpec spec, Action<string>? log)
    {
        if (spec.RegionFile is null || !File.Exists(spec.RegionFile)) return null;
        try
        {
            var boxes = Json.JsonIo.Read<List<Rect>>(spec.RegionFile);
            if (boxes.Count == 0) return null;
            log?.Invoke($"{spec.Code}: using {boxes.Count} manual regions from review file");
            return boxes;
        }
        catch (Exception ex)
        {
            log?.Invoke($"{spec.Code}: could not read region file ({ex.Message}); falling back to auto-segmentation");
            return null;
        }
    }
}
