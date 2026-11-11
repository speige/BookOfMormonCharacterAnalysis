using Bomca.Core.Descriptors;
using Bomca.Core.Imaging;
using Bomca.Core.Model;
using Bomca.Core.Segmentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Bomca.Core.Tests;

public class ImagingTests
{
    private static string WriteSyntheticPage(string dir, int rows = 2, int cols = 3)
    {
        const int w = 900, h = 480;
        using var img = new Image<Rgba32>(w, h, new Rgba32(210, 200, 185));
        img.ProcessPixelRows(acc =>
        {
            for (var r = 0; r < rows; r++)
            {
                var y0 = 80 + r * 160;
                for (var c = 0; c < cols; c++)
                {
                    var x0 = 80 + c * 240;
                    // draw a crude glyph: vertical bar + horizontal bar
                    for (var y = y0; y < y0 + 90; y++)
                        for (var x = x0 + 30; x < x0 + 40; x++)
                            acc.GetRowSpan(y)[x] = new Rgba32(25, 20, 15);
                    for (var y = y0 + 40; y < y0 + 50; y++)
                        for (var x = x0; x < x0 + 80; x++)
                            acc.GetRowSpan(y)[x] = new Rgba32(25, 20, 15);
                }
            }
        });
        var path = Path.Combine(dir, $"page-{Guid.NewGuid():N}.png");
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public void Segmenter_FindsRowsAndGlyphs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bomca-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var image = WriteSyntheticPage(dir, rows: 2, cols: 3);
            var segmenter = new PageSegmenter();
            var doc = segmenter.Run(new PageSegmenter.Options
            {
                DocCode = "T",
                Name = "synthetic",
                Kind = "test",
                ImagePath = image,
                Params = new SegmentationParams
                {
                    SkipTitleRow = false,
                    MergeGapRatio = 0.25,
                    MinComponentArea = 20,
                    MinGlyphHeightPx = 10,
                    MinGlyphArea = 30,
                },
            }, dir);

            var textRows = doc.Rows.Count(r => r.GlyphCount > 0);
            Assert.Equal(2, textRows);
            Assert.Equal(6, doc.Symbols.Count);
            Assert.All(doc.Symbols, s => Assert.True(File.Exists(Path.Combine(dir, s.CropPath))));
            Assert.All(doc.Symbols, s => Assert.True(File.Exists(Path.Combine(dir, s.NormalizedPath))));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Segmenter_RecordsRowGeometry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bomca-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var image = WriteSyntheticPage(dir, rows: 2, cols: 3);
            var doc = new PageSegmenter().Run(new PageSegmenter.Options
            {
                DocCode = "T",
                ImagePath = image,
                Params = new SegmentationParams { SkipTitleRow = false, MinComponentArea = 20, MinGlyphHeightPx = 10, MinGlyphArea = 30 },
            }, dir);

            Assert.All(doc.Rows.Where(r => r.GlyphCount > 0), r => Assert.True(r.MedianGlyphHeight > 0));
            Assert.NotEmpty(doc.Gaps);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class DescriptorTests
{
    private static BoolMask Bar(int width, int height, int barWidth, int offset = 0, int margin = 8)
    {
        var m = new BoolMask(width, height);
        for (var y = margin; y < height - margin; y++)
            for (var x = offset; x < offset + barWidth; x++)
                m.Data[y * width + x] = true;
        return m;
    }

    [Fact]
    public void IdenticalMasksScorePerfectly()
    {
        var a = Bar(64, 64, 10);
        var b = Bar(64, 64, 10);
        var bitsA = new Bomca.Core.Clustering.MaskBits(a);
        var bitsB = new Bomca.Core.Clustering.MaskBits(b);
        Assert.Equal(1.0, MaskBitsIou(bitsA, bitsB), 3);
        Assert.Equal(1.0, ShapeDescriptor.Ncc(a, b), 3);
        Assert.True(ShapeDescriptor.MomentScore(ShapeIdentifier(a), ShapeIdentifier(b)) > 0.99);
    }

    [Fact]
    public void DifferentShapesScoreLower()
    {
        var bar = Bar(64, 64, 10);
        var block = new BoolMask(64, 64);
        for (var y = 20; y < 44; y++)
            for (var x = 20; x < 44; x++)
                block.Data[y * 64 + x] = true;

        var barBits = new Bomca.Core.Clustering.MaskBits(bar);
        var blockBits = new Bomca.Core.Clustering.MaskBits(block);
        Assert.True(MaskBitsIou(barBits, blockBits) < 0.5);
    }

    [Fact]
    public void Normalisation_SeparatesSameGlyphFromDifferentGlyph()
    {
        // The practical property the pipeline relies on: two renderings of one glyph, at modestly different
        // positions and sizes, must be closer than two different glyphs.
        var a = Cross(96, 12, 52, 20);
        var aShiftedScaled = Cross(96, 14, 62, 14);
        var different = Ring(96, 34, 14);

        var na = MomentNormalizer.Normalize(a, 96);
        var nb = MomentNormalizer.Normalize(aShiftedScaled, 96);
        var nc = MomentNormalizer.Normalize(different, 96);

        var sameIou = na.Iou(nb);
        var differentIou = na.Iou(nc);
        Assert.True(sameIou > differentIou + 0.1,
            $"same-glyph IoU {sameIou:0.00} should clearly exceed different-glyph IoU {differentIou:0.00}");
    }

    private static BoolMask Ring(int size, int radius, int thickness)
    {
        var m = new BoolMask(size, size);
        var c = size / 2.0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                if (Math.Abs(d - radius) <= thickness / 2.0) m.Data[y * size + x] = true;
            }
        return m;
    }

    private static BoolMask Cross(int size, int armWidth, int armLength, int margin)
    {
        var m = new BoolMask(size, size);
        var cx = size / 2;
        var cy = size / 2;
        for (var y = margin; y < margin + armLength; y++)
            for (var x = cx - armWidth / 2; x < cx + armWidth / 2; x++)
                if (x >= 0 && x < size && y >= 0 && y < size) m.Data[y * size + x] = true;
        for (var x = cx - armLength / 2; x < cx + armLength / 2; x++)
            for (var y = cy - armWidth / 2; y < cy + armWidth / 2; y++)
                if (x >= 0 && x < size && y >= 0 && y < size) m.Data[y * size + x] = true;
        return m;
    }

    [Fact]
    public void PHash_IsStableUnderRotation()
    {
        var a = Bar(64, 64, 10);
        var rotated = ShapeDescriptor.Rotate(a, 8);
        var hamming = ShapeDescriptor.Hamming(ShapeDescriptor.DHash(a), ShapeDescriptor.DHash(rotated));
        Assert.True(hamming < 20, $"dHash hamming {hamming} should stay small for a small rotation");
    }

    private static double MaskBitsIou(Bomca.Core.Clustering.MaskBits a, Bomca.Core.Clustering.MaskBits b)
        => Bomca.Core.Clustering.MaskBits.Iou(a.Words, b.Words);

    private static double[] ShapeIdentifier(BoolMask m)
    {
        var padded = new BoolMask(64, 64);
        for (var y = 0; y < Math.Min(64, m.Height); y++)
            for (var x = 0; x < Math.Min(64, m.Width); x++)
                padded.Data[y * 64 + x] = m.Data[y * m.Width + x];
        return HuMomentsOf(padded);
    }

    private static double[] HuMomentsOf(BoolMask m) => Bomca.Core.Segmentation.PageSegmenter.HuMoments(m);
}
