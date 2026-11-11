namespace Bomca.Core.Model;

public sealed class Rect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public Rect() { }
    public Rect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    public int Right => X + W;
    public int Bottom => Y + H;
    public double Cx => X + W / 2.0;
    public double Cy => Y + H / 2.0;
    public int Area => W * H;

    public int HDistance(Rect o) => o.X > Right ? o.X - Right : (X > o.Right ? X - o.Right : 0);
    public int VOverlap(Rect o) => Math.Max(0, Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y));
    public bool Intersects(Rect o) => HDistance(o) == 0 && VOverlap(o) > 0;

    public Rect Inflate(int px) => new(X - px, Y - px, W + 2 * px, H + 2 * px);
    public override string ToString() => $"[{X},{Y} {W}x{H}]";
}

public sealed class SegmentationParams
{
    /// <summary>Radius (px) of the background estimate used for illumination flattening.</summary>
    public int BackgroundRadius { get; set; } = 101;
    /// <summary>Ink threshold as a multiple of the flattened image mean. Lower = more ink.</summary>
    public double InkThresholdFactor { get; set; } = 0.72;
    /// <summary>Optional per-document absolute threshold override (0 = auto).</summary>
    public double InkThresholdAbsolute { get; set; }
    /// <summary>Components smaller than this many pixels are treated as noise/specks.</summary>
    public int MinComponentArea { get; set; } = 40;
    /// <summary>Histogram bin size used to find row bands from component centres.</summary>
    public int RowBinPx { get; set; } = 6;
    /// <summary>Minimum separation (px) between detected row peaks.</summary>
    public int RowMinPeakDistancePx { get; set; } = 90;
    /// <summary>Merge two components into one glyph when the horizontal gap is below this fraction of the row's median glyph height.</summary>
    public double MergeGapRatio { get; set; } = 0.12;
    /// <summary>Never merge across a horizontal gap larger than this many pixels (absolute cap).</summary>
    public int MergeGapMaxPx { get; set; } = 28;
    /// <summary>Minimum vertical overlap ratio for two components to be merged into one glyph.</summary>
    public double MergeVOverlapRatio { get; set; } = 0.25;
    /// <summary>Minimum glyph height in px after merging (filters dust and border remnants).</summary>
    public int MinGlyphHeightPx { get; set; } = 26;
    /// <summary>Glyphs taller than this multiple of the row median height are almost always over-merges.</summary>
    public double MaxGlyphHeightRatio { get; set; } = 3.0;
    /// <summary>Minimum ink area (px) for a glyph candidate.</summary>
    public int MinGlyphArea { get; set; } = 150;
    /// <summary>Skip the topmost row band (the handwritten title) when true.</summary>
    public bool SkipTitleRow { get; set; } = true;
    /// <summary>Optional manual page crop (paper region); empty = auto-trim.</summary>
    public Rect? PageCrop { get; set; }
    /// <summary>Optional manual exclusion boxes (title bbox, frayed corners, museum stamps).</summary>
    public List<Rect> Exclusions { get; set; } = new();
    /// <summary>Optional rotation in degrees applied before analysis.</summary>
    public double RotateDegrees { get; set; }
}

public sealed class DocumentRecord
{
    public string SchemaVersion { get; set; } = "1.0";
    public string DocCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string SourceImage { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string? Sha256 { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string? Notes { get; set; }
    public string GeneratedUtc { get; set; } = "";
    public SegmentationParams Params { get; set; } = new();
    public List<RowRecord> Rows { get; set; } = new();
    public List<SymbolOccurrence> Symbols { get; set; } = new();
    public List<SegmentGap> Gaps { get; set; } = new();
    public List<SegmentRejection> Rejected { get; set; } = new();
}

public sealed class RowRecord
{
    public int Index { get; set; }
    public string RowId { get; set; } = "";
    /// <summary>title | text</summary>
    public string Kind { get; set; } = "text";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public double MedianGlyphHeight { get; set; }
    public double MeanInnerGapPx { get; set; }
    public double MedianInnerGapPx { get; set; }
    /// <summary>Whitespace between the last glyph of this row and the row's right text margin, in px.</summary>
    public double TrailingGapPx { get; set; }
    /// <summary>Trailing gap expressed in units of the row's median glyph width (how many glyphs would fit).</summary>
    public double TrailingGapInGlyphs { get; set; }
    public int GlyphCount { get; set; }
    public bool Skipped { get; set; }
}

public sealed class SegmentRejection
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public int Area { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SymbolOccurrence
{
    public string Id { get; set; } = "";
    public string Doc { get; set; } = "";
    public int Row { get; set; }
    public int Ordinal { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    /// <summary>Index of this glyph within the document's global reading order (title excluded).</summary>
    public int GlobalIndex { get; set; }
    public int ComponentCount { get; set; }
    public int HoleCount { get; set; }
    public int StrokeCount { get; set; }
    public double Area { get; set; }
    public double InkDensity { get; set; }
    public double Aspect { get; set; }
    public double StrokeWidth { get; set; }
    public double[] HuMoments { get; set; } = Array.Empty<double>();
    public ulong PHash { get; set; }
    public string CropPath { get; set; } = "";
    public string NormalizedPath { get; set; } = "";
    /// <summary>Segmentation confidence: 1.0 = clean single component, lower when merging was required.</summary>
    public double Confidence { get; set; } = 1;
    public bool Manual { get; set; }
    /// <summary>Canonical class label assigned by the inventory stage (per threshold version).</summary>
    public string? ClassId { get; set; }
    public string? Note { get; set; }
}

public sealed class SimilarityBreakdown
{
    public double Iou { get; set; }
    public double Ncc { get; set; }
    public double Chamfer { get; set; }
    public double Moment { get; set; }
    public double Feature { get; set; }
    public double Combined { get; set; }
    public double BestRotationDeg { get; set; }
    public bool Mirrored { get; set; }
}

public sealed class GlyphPair
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public SimilarityBreakdown Breakdown { get; set; } = new();
    /// <summary>same-symbol | variant-of | similar-style</summary>
    public string Kind { get; set; } = "same-symbol";
    public string Method { get; set; } = "classical";
    public string? LlmVerdict { get; set; }
    public string? ReviewStatus { get; set; }
}

public sealed class ClusterRecord
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Doc { get; set; } = "";
    public string GeneratedUtc { get; set; } = "";
    public double Threshold { get; set; }
    public string Method { get; set; } = "";
    public List<GlyphPair> Pairs { get; set; } = new();
    /// <summary>All pairs considered after prefilter, for the ranked-review UI.</summary>
    public int CandidatePairCount { get; set; }
}

public sealed class InventoryClass
{
    public string ClassId { get; set; } = "";
    public string Label { get; set; } = "";
    public int OccurrenceCount { get; set; }
    public List<string> Members { get; set; } = new();
    public double MeanIntraScore { get; set; }
    public double MedianGlyphHeight { get; set; }
    public List<string> Rows { get; set; } = new();
    public string? Note { get; set; }
}

public sealed class InventoryVersion
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Doc { get; set; } = "";
    public double Threshold { get; set; }
    public string Method { get; set; } = "average-linkage";
    public string GeneratedUtc { get; set; } = "";
    public int ClassCount { get; set; }
    public int SingletonCount { get; set; }
    public double ExpectedRandomPairRate { get; set; }
    public List<InventoryClass> Classes { get; set; } = new();
}

public sealed class SegmentGap
{
    public string RowId { get; set; } = "";
    public int AfterOrdinal { get; set; }
    public double GapPx { get; set; }
    public double GapInGlyphs { get; set; }
}
