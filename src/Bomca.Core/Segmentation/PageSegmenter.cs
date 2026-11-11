using Bomca.Core.Descriptors;
using Bomca.Core.Imaging;
using Bomca.Core.Json;
using SixLabors.ImageSharp;
using Bomca.Core.Model;

namespace Bomca.Core.Segmentation;

public sealed class PageSegmenter
{
    public const int NormSize = 64;

    public sealed class Options
    {
        public string DocCode = "";
        public string Name = "";
        public string Kind = "";
        public string? SourceUrl;
        public string? Notes;
        public string ImagePath = "";
        public SegmentationParams Params = new();
        /// <summary>Skip automatic line detection: the whole content bbox is one row.</summary>
        public bool SingleRow;
        /// <summary>When set, these page-space boxes are used as glyphs verbatim (manual transcription aid).</summary>
        public List<Rect> ManualGlyphBoxes = new();
        /// <summary>Write component and histogram diagnostics to out/<DOC>/debug for threshold tuning.</summary>
        public bool Debug;
    }

    public DocumentRecord Run(Options opt, string outDir)
    {
        var p = opt.Params;
        var gray = GrayImage.Load(opt.ImagePath, p.RotateDegrees);
        if (p.PageCrop is { } crop && crop.W > 0)
        {
            var full = new Rect(0, 0, gray.Width, gray.Height);
            var inter = Intersect(full, crop);
            if (inter.W > 0) gray = gray.Crop(inter);
        }

        var flat = gray.FlattenIllumination(p.BackgroundRadius);
        var thr = p.InkThresholdAbsolute > 0 ? p.InkThresholdAbsolute : flat.Mean() * p.InkThresholdFactor;
        var ink = BoolMask.FromGray(flat, thr);

        foreach (var ex in p.Exclusions)
        {
            for (var y = Math.Max(0, ex.Y); y < Math.Min(ink.Height, ex.Bottom); y++)
                for (var x = Math.Max(0, ex.X); x < Math.Min(ink.Width, ex.Right); x++)
                    ink.Data[y * ink.Width + x] = false;
        }

        // Drop specks so they cannot influence row detection.
        ink = ink.RemoveSmallComponents(p.MinComponentArea);

        var doc = new DocumentRecord
        {
            DocCode = opt.DocCode,
            Name = opt.Name,
            Kind = opt.Kind,
            SourceImage = Path.GetFileName(opt.ImagePath),
            SourceUrl = opt.SourceUrl,
            Sha256 = JsonIo.Sha256File(opt.ImagePath),
            ImageWidth = gray.Width,
            ImageHeight = gray.Height,
            Notes = opt.Notes,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Params = p,
        };

        ComponentLabeler.Label(ink, out var comps);
        var kept = new List<Component>();
        foreach (var c in comps)
        {
            if (c.Area < p.MinComponentArea) continue;
            // Reject paper-edge shadows and torn-border artefacts before they can seed a row.
            var b = c.Bounds;
            var isBorderLine = b.W >= gray.Width * 0.5 && b.H <= gray.Height * 0.08;
            var touchesBorder = b.X <= 2 || b.Y <= 2 || b.Right >= ink.Width - 2 || b.Bottom >= ink.Height - 2;
            if (isBorderLine || touchesBorder)
            {
                doc.Rejected.Add(new SegmentRejection { X = b.X, Y = b.Y, W = b.W, H = b.H, Area = c.Area, Reason = isBorderLine ? "border-line" : "touches-border" });
                continue;
            }
            kept.Add(c);
        }
        comps = kept;

        if (opt.Debug)
        {
            var dbg = Path.Combine(outDir, "debug");
            Directory.CreateDirectory(dbg);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("x,y,w,h,area,cx,cy");

            foreach (var c in comps)
                sb.AppendLine(string.Join(",", c.Bounds.X, c.Bounds.Y, c.Bounds.W, c.Bounds.H, c.Area, JsonIo.Round(c.Cx, 2), JsonIo.Round(c.Cy, 2)));
            File.WriteAllText(Path.Combine(dbg, "components.csv"), sb.ToString());
            MaskIo.SaveMask(ink, Path.Combine(dbg, "ink-mask.png"));
            MaskIo.SaveGray(flat, Path.Combine(dbg, "flattened-gray.png"));
        }

        Environment.SetEnvironmentVariable("BOMCA_DEBUG_DIR", Path.Combine(outDir, "debug"));
        List<(int y0, int y1, string kind)> bands;
        if (opt.ManualGlyphBoxes.Count > 0)
        {
            bands = new List<(int, int, string)>();
        }
        else if (opt.SingleRow)
        {
            var content = ContentBounds(ink);
            bands = new List<(int, int, string)> { (content.Y, content.Bottom, "text") };
        }
        else
        {
            bands = RowDetector.Detect(comps, p);
        }

        var glyphBoxes = new List<(Rect box, int row, double conf, int compCount)>();
        var gaps = new List<SegmentGap>();
        var rejected = new List<SegmentRejection>();

        if (opt.ManualGlyphBoxes.Count > 0)
        {
            var rowIndex = 0;
            var ordered = opt.ManualGlyphBoxes.OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
            foreach (var b in ordered)
            {
                glyphBoxes.Add((b, rowIndex, 1.0, 0));
            }
            bands = new List<(int, int, string)>
            {
                (ordered.Min(b => b.Y), ordered.Max(b => b.Bottom), "text"),
            };
        }
        else
        {
            // A paper-edge shadow survives thresholding as faint ink. Drop faint edge bands
            // before they can become rows: their pixels sit just under the ink threshold.
            var keptBands = new List<(int y0, int y1, string kind, double faint, double meanGray)>();
            for (var bi = 0; bi < bands.Count; bi++)
            {
                var (y0, y1, kind) = bands[bi];
                var (faint, meanGray) = BandInkStats(ink, flat, y0, y1, thr);
                var isBottomEdge = bi == bands.Count - 1 && y0 > flat.Height * 0.8;
                if (isBottomEdge && faint > 0.55)
                {
                    rejected.Add(new SegmentRejection
                    {
                        X = 0, Y = y0, W = ink.Width, H = Math.Max(1, y1 - y0),
                        Area = 0, Reason = $"faint-edge-band(faint={faint:0.00},mean={meanGray:0.0})",
                    });
                    continue;
                }
                keptBands.Add((y0, y1, kind, faint, meanGray));
            }

            for (var bi = 0; bi < keptBands.Count; bi++)
            {
                var (y0, y1, kind, _, _) = keptBands[bi];
                var inBand = comps.Where(c => c.Cy >= y0 && c.Cy < y1).ToList();
                if (inBand.Count == 0) continue;
                var rowIdx = doc.Rows.Count;
                var grouped = GlyphGrouper.Group(inBand, p);
                foreach (var g in grouped)
                {
                    if (g.Bounds.H < p.MinGlyphHeightPx || g.Area < p.MinGlyphArea)
                    {
                        rejected.Add(new SegmentRejection
                        {
                            X = g.Bounds.X, Y = g.Bounds.Y, W = g.Bounds.W, H = g.Bounds.H,
                            Area = g.Area, Reason = "too-short",
                        });
                        continue;
                    }
                    glyphBoxes.Add((g.Bounds, rowIdx, g.Confidence, g.ComponentCount));
                }

                var row = new RowRecord
                {
                    Index = rowIdx,
                    RowId = $"{opt.DocCode}-L{rowIdx + 1:00}",
                    Kind = kind,
                };
                row.Y = y0;
                row.H = y1 - y0;
                // The handwritten title is always the topmost band of the page.
                row.Skipped = p.SkipTitleRow && rowIdx == 0;
                if (row.Skipped) row.Kind = "title";
                doc.Rows.Add(row);
            }
        }

        // Global reading order, title excluded.
        var orderedRows = doc.Rows.Where(r => !r.Skipped).ToList();
        var globalIndex = 0;
        for (var ri = 0; ri < doc.Rows.Count; ri++)
        {
            var row = doc.Rows[ri];
            var boxes = glyphBoxes.Where(g => g.row == ri).OrderBy(g => g.box.X).ToList();
            row.GlyphCount = boxes.Count;
            if (boxes.Count == 0)
            {
                row.X = 0; row.W = 0;
                continue;
            }
            row.X = boxes.Min(b => b.box.X);
            row.W = boxes.Max(b => b.box.Right) - row.X;
            var heights = boxes.Select(b => (double)b.box.H).OrderBy(h => h).ToList();
            row.MedianGlyphHeight = heights[heights.Count / 2];

            var innerGaps = new List<double>();
            for (var i = 1; i < boxes.Count; i++)
                innerGaps.Add(boxes[i].box.X - boxes[i - 1].box.Right);
            if (innerGaps.Count > 0)
            {
                row.MeanInnerGapPx = innerGaps.Average();
                var sorted = innerGaps.OrderBy(g => g).ToList();
                row.MedianInnerGapPx = sorted[sorted.Count / 2];
            }

            for (var i = 0; i < boxes.Count; i++)
            {
                var (box, _, conf, compCount) = boxes[i];
                var occ = BuildOccurrence(opt.DocCode, ri, row, box, i, flat, gray, thr, outDir);
                if (row.Skipped)
                {
                    occ.Note = "title-row";
                }
                else
                {
                    occ.GlobalIndex = globalIndex++;
                }
                occ.Confidence = conf;
                occ.ComponentCount = compCount;
                doc.Symbols.Add(occ);

                if (i > 0)
                {
                    var gapPx = box.X - boxes[i - 1].box.Right;
                    var denom = Math.Max(1.0, row.MedianGlyphHeight);
                    gaps.Add(new SegmentGap
                    {
                        RowId = row.RowId,
                        AfterOrdinal = i,
                        GapPx = gapPx,
                        GapInGlyphs = JsonIo.Round(gapPx / denom, 3),
                    });
                }
            }
        }

        // Trailing gap: distance from the row's last glyph to the writing block's right edge.
        var blockRight = doc.Rows.Where(r => !r.Skipped).Select(r => r.X + r.W).DefaultIfEmpty(0).Max();
        foreach (var row in doc.Rows)
        {
            if (row.GlyphCount == 0) continue;
            var last = doc.Symbols.Where(s => s.Row == row.Index && s.Note != "title-row").OrderBy(s => s.X).LastOrDefault();
            var right = last is null ? row.X + row.W : last.X + last.W;
            row.TrailingGapPx = Math.Max(0, blockRight - right);
            var denom = Math.Max(1.0, row.MedianGlyphHeight);
            row.TrailingGapInGlyphs = JsonIo.Round(row.TrailingGapPx / denom, 3);
        }

        doc.Rejected = rejected;
        doc.Gaps = gaps;
        return doc;
    }


    /// <summary>
    /// Fraction of a band's ink pixels that are only barely below the ink threshold (faint), plus their
    /// mean flattened grey level. Paper-edge shadows are almost entirely faint; real ink is not.
    /// </summary>
    private static (double faint, double meanGray) BandInkStats(BoolMask ink, GrayImage flat, int y0, int y1, double thr)
    {
        var ya = Math.Max(0, y0);
        var yb = Math.Min(ink.Height, y1);
        var limit = thr * 0.90;
        long inkCount = 0, faintCount = 0;
        double sum = 0;
        for (var y = ya; y < yb; y++)
        {
            var off = y * ink.Width;
            for (var x = 0; x < ink.Width; x++)
            {
                if (!ink.Data[off + x]) continue;
                inkCount++;
                var v = flat.Px[off + x];
                sum += v;
                if (v > limit) faintCount++;
            }
        }
        if (inkCount == 0) return (0, 0);
        return ((double)faintCount / inkCount, sum / inkCount);
    }

    private static Rect ContentBounds(BoolMask ink)
    {
        int minX = ink.Width, minY = ink.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < ink.Height; y++)
        {
            for (var x = 0; x < ink.Width; x++)
            {
                if (!ink.Data[y * ink.Width + x]) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) return new Rect(0, 0, ink.Width, ink.Height);
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static SymbolOccurrence BuildOccurrence(
        string docCode, int rowIdx, RowRecord row, Rect box, int ordinal,
        GrayImage flat, GrayImage gray, double globalThr, string outDir)
    {
        var pad = 6;
        var cropRect = Intersect(new Rect(0, 0, flat.Width, flat.Height), box.Inflate(pad));
        var crop = flat.Crop(cropRect);

        var otsuCrop = crop.Otsu();
        var localThr = Math.Clamp(otsuCrop, globalThr * 0.75, globalThr * 1.25);
        var localMask = BoolMask.FromGray(crop, localThr);
        localMask = localMask.RemoveSmallComponents(8);
        if (localMask.Count() == 0) localMask = BoolMask.FromGray(crop, globalThr);

        // Tight ink bbox inside the padded crop (segmentation boxes from the global mask can be loose).
        var tight = TightBounds(localMask, pad);
        var tightMask = localMask.Crop(tight);
        var tightGray = crop.Crop(tight);

        var norm = tightMask.FitSquare(NormSize);
        var normGray = tightGray.FitSquare(96);

        var id = $"{docCode}-L{rowIdx + 1:00}-{ordinal + 1:00}";
        var cropDir = Path.Combine(outDir, "crops");
        var normDir = Path.Combine(outDir, "norm64");
        Directory.CreateDirectory(cropDir);
        Directory.CreateDirectory(normDir);

        var cropPath = Path.Combine(cropDir, id + ".png");
        var normPath = Path.Combine(normDir, id + ".png");
        tightGray.ToImage().SaveAsPng(cropPath);
        MaskIo.SaveMask(norm, normPath);

        var area = tightMask.Count();
        var perim = Perimeter(tightMask);
        var hu = HuMoments(tightMask);
        var holes = ComponentLabeler.CountHoles(tightMask);
        var strokes = ComponentLabeler.EstimateStrokeCount(tightMask);

        return new SymbolOccurrence
        {
            Id = id,
            Doc = docCode,
            Row = rowIdx,
            Ordinal = ordinal,
            X = tight.X + cropRect.X,
            Y = tight.Y + cropRect.Y,
            W = tight.W,
            H = tight.H,
            Area = area,
            InkDensity = JsonIo.Round((double)area / Math.Max(1, tight.W * tight.H), 4),
            Aspect = JsonIo.Round((double)tight.W / Math.Max(1, tight.H), 4),
            StrokeWidth = JsonIo.Round(perim <= 0 ? 0 : 2.0 * area / perim, 3),
            HoleCount = holes,
            StrokeCount = strokes,
            HuMoments = hu,
            PHash = ShapeDescriptor.DHash(tightMask),
            CropPath = Relative(outDir, cropPath),
            NormalizedPath = Relative(outDir, normPath),
        };
    }

    internal static string Relative(string outDir, string path) =>
        Path.GetRelativePath(outDir, path).Replace('\\', '/');

    private static Rect TightBounds(BoolMask mask, int fallbackPad)
    {
        int minX = mask.Width, minY = mask.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                if (!mask.Data[y * mask.Width + x]) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        if (maxX < 0) return new Rect(0, 0, Math.Max(1, mask.Width - fallbackPad), Math.Max(1, mask.Height - fallbackPad));
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static double Perimeter(BoolMask m)
    {
        var per = 0L;
        for (var y = 0; y < m.Height; y++)
            for (var x = 0; x < m.Width; x++)
            {
                if (!m.Data[y * m.Width + x]) continue;
                if (x == 0 || !m.Data[y * m.Width + x - 1]) per++;
                if (x == m.Width - 1 || !m.Data[y * m.Width + x + 1]) per++;
                if (y == 0 || !m.Data[(y - 1) * m.Width + x]) per++;
                if (y == m.Height - 1 || !m.Data[(y + 1) * m.Width + x]) per++;
            }
        return per;
    }

    /// <summary>Seven log-scaled Hu invariant moments of the ink mask.</summary>
    public static double[] HuMoments(BoolMask m)
    {
        double m00 = 0, m10 = 0, m01 = 0;
        for (var y = 0; y < m.Height; y++)
            for (var x = 0; x < m.Width; x++)
                if (m.Data[y * m.Width + x]) { m00++; m10 += x; m01 += y; }
        if (m00 < 1) return new double[7];
        var cx = m10 / m00;
        var cy = m01 / m00;
        double mu20 = 0, mu02 = 0, mu11 = 0, mu30 = 0, mu03 = 0, mu21 = 0, mu12 = 0;
        for (var y = 0; y < m.Height; y++)
            for (var x = 0; x < m.Width; x++)
            {
                if (!m.Data[y * m.Width + x]) continue;
                double dx = x - cx, dy = y - cy;
                mu20 += dx * dx;
                mu02 += dy * dy;
                mu11 += dx * dy;
                mu30 += dx * dx * dx;
                mu03 += dy * dy * dy;
                mu21 += dx * dx * dy;
                mu12 += dx * dy * dy;
            }
        var n20 = mu20 / Math.Pow(m00, 2);
        var n02 = mu02 / Math.Pow(m00, 2);
        var n11 = mu11 / Math.Pow(m00, 2);
        var n30 = mu30 / Math.Pow(m00, 2.5);
        var n03 = mu03 / Math.Pow(m00, 2.5);
        var n21 = mu21 / Math.Pow(m00, 2.5);
        var n12 = mu12 / Math.Pow(m00, 2.5);

        var h1 = n20 + n02;
        var h2 = (n20 - n02) * (n20 - n02) + 4 * n11 * n11;
        var h3 = (n30 - 3 * n12) * (n30 - 3 * n12) + (3 * n21 - n03) * (3 * n21 - n03);
        var h4 = (n30 + n12) * (n30 + n12) + (n21 + n03) * (n21 + n03);
        var h5 = (n30 - 3 * n12) * (n30 + n12) * ((n30 + n12) * (n30 + n12) - 3 * (n21 + n03) * (n21 + n03))
                 + (3 * n21 - n03) * (n21 + n03) * (3 * (n30 + n12) * (n30 + n12) - (n21 + n03) * (n21 + n03));
        var h6 = (n20 - n02) * ((n30 + n12) * (n30 + n12) - (n21 + n03) * (n21 + n03))
                 + 4 * n11 * (n30 + n12) * (n21 + n03);
        var h7 = (3 * n21 - n03) * (n30 + n12) * ((n30 + n12) * (n30 + n12) - 3 * (n21 + n03) * (n21 + n03))
                 - (n30 - 3 * n12) * (n21 + n03) * (3 * (n30 + n12) * (n30 + n12) - (n21 + n03) * (n21 + n03));

        double S(double v) => Math.Sign(v) * Math.Log10(1 + Math.Abs(v) * 1e3);
        return new[] { h1, h2, h3, h4, h5, h6, h7 }.Select(v => JsonIo.Round(S(v), 6)).ToArray();
    }

    internal static Rect Intersect(Rect a, Rect b)
    {
        var x0 = Math.Max(a.X, b.X);
        var y0 = Math.Max(a.Y, b.Y);
        var x1 = Math.Min(a.Right, b.Right);
        var y1 = Math.Min(a.Bottom, b.Bottom);
        return new Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }
}

public sealed class GroupedGlyph
{
    public Rect Bounds { get; set; } = new();
    public int Area { get; set; }
    public int ComponentCount { get; set; }
    public double Confidence { get; set; } = 1;
}
