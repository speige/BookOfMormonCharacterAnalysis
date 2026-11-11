using Bomca.Core.Json;
using Bomca.Core.Model;

namespace Bomca.Core.Analysis;

public sealed class RowGeometry
{
    public string RowId { get; set; } = "";
    public int Index { get; set; }
    public int GlyphCount { get; set; }
    public double MedianGlyphHeight { get; set; }
    public double MeanInnerGapPx { get; set; }
    public double TrailingGapPx { get; set; }
    public double TrailingGapInGlyphs { get; set; }
    public int BlockLeft { get; set; }
    public int BlockRight { get; set; }
    public double InkDensityMean { get; set; }
}

public sealed class SpecialGlyph
{
    public string Id { get; set; } = "";
    public string ClassId { get; set; } = "";
    public int Row { get; set; }
    public int Ordinal { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public double Aspect { get; set; }
    public double InkDensity { get; set; }
    public int StrokeCount { get; set; }
    public int HoleCount { get; set; }
    public string Note { get; set; } = "";
}

public sealed class ClassStatistics
{
    public string ClassId { get; set; } = "";
    public int Occurrences { get; set; }
    public List<int> GlobalPositions { get; set; } = new();
    public List<double> NormalizedGaps { get; set; } = new();
    public double GapRegularity { get; set; }
    public bool LineStartBias { get; set; }
    public bool LineEndBias { get; set; }
}

public sealed class StructureAnalysis
{
    public string Doc { get; set; } = "";
    public string GeneratedUtc { get; set; } = "";
    public double Threshold { get; set; }
    public int GlyphCount { get; set; }
    public int ClassCount { get; set; }
    public int SingletonCount { get; set; }
    public double ZipfSlope { get; set; }
    public double ZipfRSquared { get; set; }
    public List<RowGeometry> Rows { get; set; } = new();
    public double RowHeightTrend { get; set; }
    public double RowHeightTrendRSquared { get; set; }
    public List<SpecialGlyph> BlackSquares { get; set; } = new();
    public List<SpecialGlyph> CombGlyphs { get; set; } = new();
    public bool CombStrokeCountVaries { get; set; }
    public List<ClassStatistics> Classes { get; set; } = new();
    public double RepeatRegularityVsChance { get; set; }
    public double MeanNormalizedGapEntropy { get; set; }
    public int GlyphsOnLineStarts { get; set; }
    public CompressionModel Compression { get; set; } = new();
    public List<string> Observations { get; set; } = new();
}

public sealed class CompressionScenario
{
    public string Name { get; set; } = "";
    public double InscribedSquareFeet { get; set; }
    public double UsableFraction { get; set; }
    public double SymbolsPerSquareInch { get; set; }
    public double EstimatedSymbols { get; set; }
    public double WordsPerSymbol { get; set; }
    public double PageEquivalentFraction { get; set; }
    public string Interpretation { get; set; } = "";
}

public sealed class CompressionModel
{
    public int SymbolsInSurvivingFragment { get; set; }
    public double FragmentWidthIn { get; set; }
    public double FragmentHeightIn { get; set; }
    public double SymbolsPerSquareInch { get; set; }
    public int CorpusWords { get; set; }
    public List<CompressionScenario> Scenarios { get; set; } = new();
    public string Caveat { get; set; } = "Inputs are historical estimates with wide error bars; use to rank models, not to prove one.";
}

public static class StructureAnalyzer
{
    /// <summary>
    /// Measures the geometric and statistical properties the research notes call out: size drift down the
    /// page, the row-4 trailing gap, the black square, the comb glyphs, repeat st ructure and compression.
    /// </summary>
    public static StructureAnalysis Analyze(
        DocumentRecord doc,
        InventoryVersion inventory,
        int corpusWords,
        (double wIn, double hIn)? fragmentInches = null)
    {
        var inches = fragmentInches ?? (7.0, 3.0);
        var glyphs = doc.Symbols.Where(s => s.Note != "title-row").OrderBy(s => s.GlobalIndex).ToList();
        var analysis = new StructureAnalysis
        {
            Doc = doc.DocCode,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Threshold = inventory.Threshold,
            GlyphCount = glyphs.Count,
            ClassCount = inventory.ClassCount,
            SingletonCount = inventory.SingletonCount,
        };

        // --- row geometry -------------------------------------------------
        var blockRight = 0;
        var blockLeft = int.MaxValue;
        foreach (var row in doc.Rows.Where(r => !r.Skipped && r.GlyphCount > 0))
        {
            var inRow = glyphs.Where(g => g.Row == row.Index).ToList();
            if (inRow.Count == 0) continue;
            blockRight = Math.Max(blockRight, inRow.Max(g => g.X + g.W));
            blockLeft = Math.Min(blockLeft, inRow.Min(g => g.X));
            analysis.Rows.Add(new RowGeometry
            {
                RowId = row.RowId,
                Index = row.Index,
                GlyphCount = inRow.Count,
                MedianGlyphHeight = JsonIo.Round(row.MedianGlyphHeight, 2),
                MeanInnerGapPx = JsonIo.Round(row.MeanInnerGapPx, 2),
                TrailingGapPx = JsonIo.Round(row.TrailingGapPx, 2),
                TrailingGapInGlyphs = JsonIo.Round(row.TrailingGapInGlyphs, 3),
                BlockLeft = inRow.Min(g => g.X),
                BlockRight = inRow.Max(g => g.X + g.W),
                InkDensityMean = JsonIo.Round(inRow.Average(g => g.InkDensity), 4),
            });
        }
        analysis.GlyphsOnLineStarts = glyphs.Count(g => analysis.Rows.Any(r => r.Index == g.Row && g.X - r.BlockLeft < 25));

        (analysis.RowHeightTrend, analysis.RowHeightTrendRSquared) = LinearTrend(analysis.Rows.Select((r, i) => (double)i).ToList(),
            analysis.Rows.Select(r => r.MedianGlyphHeight).ToList());
        if (analysis.RowHeightTrend < -1.5)
            analysis.Observations.Add($"Glyph height shrinks down the page (slope {analysis.RowHeightTrend:0.0} px per row, R² {analysis.RowHeightTrendRSquared:0.00}), consistent with a copyist compressing to fit the sheet.");
        var biggestTail = analysis.Rows.OrderByDescending(r => r.TrailingGapInGlyphs).FirstOrDefault();
        if (biggestTail is not null && biggestTail.TrailingGapInGlyphs > 1.5)
            analysis.Observations.Add($"Row {biggestTail.RowId} ends {biggestTail.TrailingGapInGlyphs:0.0} glyph-widths short of the block margin (row starts here were not forced by space).");

        // --- black square -------------------------------------------------
        var squareCandidates = glyphs.Where(g => g.Aspect is >= 0.7 and <= 1.4 && g.InkDensity >= 0.72 && g.Area >= 500).ToList();
        analysis.BlackSquares = squareCandidates.Select(g => new SpecialGlyph
        {
            Id = g.Id, ClassId = g.ClassId ?? "", Row = g.Row, Ordinal = g.Ordinal,
            W = g.W, H = g.H, Aspect = JsonIo.Round(g.Aspect), InkDensity = JsonIo.Round(g.InkDensity),
            StrokeCount = g.StrokeCount, HoleCount = g.HoleCount,
        }).ToList();
        if (analysis.BlackSquares.Count > 0)
        {
            var atLineStart = analysis.BlackSquares.Count(s => analysis.Rows.Any(r => r.Index == s.Row && glyphs.First(g => g.Id == s.Id).X - r.BlockLeft < 40));
            analysis.Observations.Add($"Found {analysis.BlackSquares.Count} solid-square glyphs; {atLineStart} sit at a line start. " +
                                      (atLineStart == analysis.BlackSquares.Count && analysis.BlackSquares.Count > 1
                                          ? "A separator/marker role is plausible."
                                          : "A separator role is not supported by position."));
        }

        // --- comb / tick glyphs -------------------------------------------
        analysis.CombGlyphs = glyphs.Where(g => g.StrokeCount >= 3 && g.Aspect >= 1.4 && g.HoleCount <= 1)
            .Select(g => new SpecialGlyph
            {
                Id = g.Id, ClassId = g.ClassId ?? "", Row = g.Row, Ordinal = g.Ordinal,
                W = g.W, H = g.H, Aspect = JsonIo.Round(g.Aspect), InkDensity = JsonIo.Round(g.InkDensity),
                StrokeCount = g.StrokeCount, HoleCount = g.HoleCount,
                Note = "comb/tick candidate",
            }).ToList();
        var combCodes = analysis.CombGlyphs.Select(c => c.ClassId).Where(c => c.Length > 0).Distinct().ToList();
        var strokeCountsPerClass = combCodes
            .Select(c => glyphs.Where(g => g.ClassId == c).Select(g => g.StrokeCount).Distinct().Count())
            .ToList();
        analysis.CombStrokeCountVaries = strokeCountsPerClass.Any(n => n > 1);
        if (combCodes.Count > 0)
            analysis.Observations.Add(analysis.CombStrokeCountVaries
                ? "Comb-like glyphs vary in stroke count between occurrences: a numeral/tally reading stays open."
                : "Comb-like glyphs keep a constant stroke count: better read as one character than as a numeral.");

        // --- class statistics ---------------------------------------------
        foreach (var cls in inventory.Classes)
        {
            var members = cls.Members.Select(id => glyphs.First(g => g.Id == id)).OrderBy(g => g.GlobalIndex).ToList();
            var positions = members.Select(m => m.GlobalIndex).ToList();
            var gaps = new List<double>();
            for (var i = 1; i < positions.Count; i++) gaps.Add(positions[i] - positions[i - 1]);
            var total = gaps.Sum();
            var norm = total <= 0 ? new List<double>() : gaps.Select(g => JsonIo.Round(g / total, 4)).ToList();
            var regularity = 0.0;
            if (norm.Count >= 2)
            {
                var mean = norm.Average();
                var sd = Math.Sqrt(norm.Select(v => (v - mean) * (v - mean)).Sum() / norm.Count);
                regularity = JsonIo.Round(1 - Math.Min(1, sd / Math.Max(1e-6, mean)), 4);
            }
            var rowGeometry = analysis.Rows.Where(r => r.Index == members[0].Row).ToList();
            analysis.Classes.Add(new ClassStatistics
            {
                ClassId = cls.ClassId,
                Occurrences = members.Count,
                GlobalPositions = positions,
                NormalizedGaps = norm,
                GapRegularity = regularity,
                LineStartBias = rowGeometry.Count > 0 && members.Count(m => m.X - rowGeometry[0].BlockLeft < 25) > members.Count * 0.5,
                LineEndBias = rowGeometry.Count > 0 && members.Count(m => rowGeometry[0].BlockRight - (m.X + m.W) < 25) > members.Count * 0.5,
            });
        }

        // --- Zipf ----------------------------------------------------------
        var counts = inventory.Classes.Select(c => (double)c.OccurrenceCount).Where(c => c > 0).OrderByDescending(c => c).ToList();
        var ranks = Enumerable.Range(1, counts.Count).Select(r => (double)r).ToList();
        var logRanks = ranks.Select(Math.Log10).ToList();
        var logCounts = counts.Select(c => Math.Log10(Math.Max(1e-9, c))).ToList();
        (analysis.ZipfSlope, analysis.ZipfRSquared) = LinearTrend(logRanks, logCounts);
        analysis.Observations.Add($"Rank-frequency slope {analysis.ZipfSlope:0.00} over {counts.Count} classes (R² {analysis.ZipfRSquared:0.00}).");

        // --- repeat structure vs chance -----------------------------------
        analysis.RepeatRegularityVsChance = RepeatRegularity(doc, glyphs, inventory, seed: 12345);
        analysis.MeanNormalizedGapEntropy = analysis.Classes.Where(c => c.NormalizedGaps.Count > 0).Select(c => c.GapRegularity).DefaultIfEmpty(0).Average();

        // --- compression ---------------------------------------------------
        var perGlyphArea = glyphs.Select(g => (g.W * (double)g.H)).OrderBy(a => a).ToList();
        var medianGlyphArea = perGlyphArea.Count == 0 ? 1 : perGlyphArea[perGlyphArea.Count / 2];
        var fragmentArea = perGlyphArea.Sum();
        analysis.Compression = new CompressionModel
        {
            SymbolsInSurvivingFragment = glyphs.Count,
            FragmentWidthIn = inches.wIn,
            FragmentHeightIn = inches.hIn,
            SymbolsPerSquareInch = JsonIo.Round(glyphs.Count / Math.Max(1e-6, inches.wIn * inches.hIn), 1),
            CorpusWords = corpusWords,
        };
        var anchors = new (string Name, double SqFt, double Usable)[]
        {
            ("Dale low (30 sq ft), sealed excluded", 30, 1.0),
            ("Dale mid (58 sq ft), two-thirds sealed", 58, 0.33),
            ("Dale high (86 sq ft), all inscribed", 86, 1.0),
        };
        foreach (var (name, sqft, usable) in anchors)
        {
            var symbols = analysis.Compression.SymbolsPerSquareInch * sqft * 144 * usable;
            analysis.Compression.Scenarios.Add(new CompressionScenario
            {
                Name = name,
                InscribedSquareFeet = sqft,
                UsableFraction = usable,
                SymbolsPerSquareInch = analysis.Compression.SymbolsPerSquareInch,
                EstimatedSymbols = Math.Round(symbols),
                WordsPerSymbol = JsonIo.Round(corpusWords / Math.Max(1.0, symbols), 2),
                PageEquivalentFraction = JsonIo.Round(glyphs.Count / Math.Max(1.0, symbols), 5),
                Interpretation = corpusWords / Math.Max(1.0, symbols) >= 5
                    ? "logographic-or-compressed (each symbol carries several English words)"
                    : corpusWords / Math.Max(1.0, symbols) >= 1.5
                        ? "mixed/syllabic (symbol to word ratio above 1)"
                        : "alphabetic-density (near one word per symbol)",
            });
        }

        return analysis;
    }

    /// <summary>
    /// Mean absolute difference between observed normalised gap vectors and those of an equal-frequency
    /// random sequence. Values well above chance indicate real periodic structure in the glyph stream.
    /// </summary>
    private static double RepeatRegularity(DocumentRecord doc, List<SymbolOccurrence> glyphs, InventoryVersion inventory, int seed)
    {
        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var g in glyphs)
        {
            if (g.ClassId is null) continue;
            if (!positions.TryGetValue(g.ClassId, out var list)) positions[g.ClassId] = list = new List<int>();
            list.Add(g.GlobalIndex);
        }
        var repeats = positions.Where(kv => kv.Value.Count >= 3).ToList();
        // Without classes that repeat at least three times there is nothing to measure; report "no evidence"
        // (1.0 = same as chance) rather than a misleading zero.
        if (repeats.Count < 3) return 1.0;

        double Observed() => repeats.Average(kv => MeanAbsNormGap(kv.Value));

        var rng = new Random(seed);
        var ids = glyphs.Select(g => g.ClassId ?? "").ToList();
        double chance = 0;
        const int trials = 20;
        for (var t = 0; t < trials; t++)
        {
            var shuffled = ids.OrderBy(_ => rng.Next()).ToList();
            var idx = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < shuffled.Count; i++)
            {
                if (!idx.TryGetValue(shuffled[i], out var list)) idx[shuffled[i]] = list = new List<int>();
                list.Add(i);
            }
            var vals = repeats.Select(kv => idx.TryGetValue(kv.Key, out var l) && l.Count >= 3 ? MeanAbsNormGap(l) : 0).ToList();
            chance += vals.Average();
        }
        chance /= trials;
        var observed = Observed();
        return JsonIo.Round(chance <= 1e-9 ? 0 : observed / chance, 4);
    }

    private static double MeanAbsNormGap(List<int> pos)
    {
        var gaps = new List<double>();
        for (var i = 1; i < pos.Count; i++) gaps.Add(pos[i] - pos[i - 1]);
        var total = gaps.Sum();
        if (total <= 0) return 0;
        var norm = gaps.Select(g => g / total).ToList();
        var mean = norm.Average();
        return norm.Average(v => Math.Abs(v - mean));
    }

    public static (double slope, double r2) LinearTrend(List<double> xs, List<double> ys)
    {
        if (xs.Count < 2 || xs.Count != ys.Count) return (0, 0);
        var mx = xs.Average();
        var my = ys.Average();
        double num = 0, den = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            num += (xs[i] - mx) * (ys[i] - my);
            den += (xs[i] - mx) * (xs[i] - mx);
        }
        if (Math.Abs(den) < 1e-12) return (0, 0);
        var slope = num / den;
        var intercept = my - slope * mx;
        double ssTot = 0, ssRes = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            var pred = slope * xs[i] + intercept;
            ssTot += (ys[i] - my) * (ys[i] - my);
            ssRes += (ys[i] - pred) * (ys[i] - pred);
        }
        var r2 = ssTot <= 1e-12 ? 0 : 1 - ssRes / ssTot;
        return (JsonIo.Round(slope, 4), JsonIo.Round(r2, 4));
    }
}
