using Bomca.Core.Descriptors;
using Bomca.Core.Imaging;
using Bomca.Core.Json;
using Bomca.Core.Model;

namespace Bomca.Core.Clustering;

/// <summary>Packed binary mask (1 bit per pixel) with pre-computed rotation/mirror variants for fast IoU.</summary>
public sealed class MaskBits
{
    public int Size { get; }
    public ulong[] Words { get; }
    /// <summary>Variants: 0 = identity, then rotations, then rotations of the mirrored mask.</summary>
    public ulong[][] Variants { get; }
    public ulong[][] DilatedVariants { get; }
    public BoolMask[] MaskVariants { get; }
    public int VariantCount => Variants.Length;
    public static readonly double[] Rotations = { 0, -8, 8, -16, 16 };

    public MaskBits(BoolMask mask)
    {
        Size = mask.Width;
        if (mask.Height != mask.Width) throw new ArgumentException("MaskBits requires a square mask");
        Words = Pack(mask);
        var variants = new List<ulong[]>();
        var dilated = new List<ulong[]>();
        var maskVariants = new List<BoolMask>();
        foreach (var deg in Rotations)
        {
            foreach (var src in new[] { mask, ShapeDescriptor.Mirror(mask) })
            {
                var rotated = ShapeDescriptor.Rotate(src, deg);
                variants.Add(Pack(rotated));
                dilated.Add(Pack(rotated.Dilate(1)));
                maskVariants.Add(rotated);
            }
        }
        Variants = variants.ToArray();
        DilatedVariants = dilated.ToArray();
        MaskVariants = maskVariants.ToArray();
    }

    private MaskBits(int size, ulong[][] variants, BoolMask[] maskVariants)
    {
        Size = size;
        Words = variants[0];
        Variants = variants;
        MaskVariants = maskVariants;
    }

    private static ulong[] Pack(BoolMask m)
    {
        var words = new ulong[(m.Width * m.Height + 63) / 64];
        for (var y = 0; y < m.Height; y++)
        {
            var rowOff = y * m.Width;
            for (var x = 0; x < m.Width; x++)
            {
                if (!m.Data[rowOff + x]) continue;
                var bit = rowOff + x;
                words[bit >> 6] |= 1UL << (bit & 63);
            }
        }
        return words;
    }

    public static double Iou(ulong[] a, ulong[] b)
    {
        var inter = 0;
        var union = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var or = a[i] | b[i];
            union += System.Numerics.BitOperations.PopCount(or);
            inter += System.Numerics.BitOperations.PopCount(a[i] & b[i]);
        }
        return union == 0 ? 0 : (double)inter / union;
    }

    /// <summary>Best IoU over all rotation/mirror variants. Returns the winning variant index too.</summary>
    /// <summary>
    /// Best IoU over rotations/mirrors, taking the better of an exact and a one-pixel-dilated comparison.
    /// Thin handwriting strokes shift by a pixel between two copies of the same character, which raw IoU
    /// punishes far too harshly.
    /// </summary>
    public (double iou, int variant) BestIou(MaskBits other)
    {
        double best = 0;
        var bestIdx = 0;
        for (var v = 0; v < Variants.Length; v++)
        {
            var iou = Math.Max(Iou(Variants[v], other.Words), Iou(DilatedVariants[v], other.DilatedWords));
            if (iou > best) { best = iou; bestIdx = v; }
        }
        return (best, bestIdx);
    }

    public ulong[] DilatedWords => DilatedVariants.Length > 0 ? DilatedVariants[0] : Words;

    public BoolMask ToMask()
    {
        var m = new BoolMask(Size, Size);
        for (var i = 0; i < Words.Length; i++)
        {
            var w = Words[i];
            while (w != 0)
            {
                var b = System.Numerics.BitOperations.TrailingZeroCount(w);
                var bit = i * 64 + b;
                if (bit < m.Data.Length) m.Data[bit] = true;
                w &= w - 1;
            }
        }
        return m;
    }
}

public sealed class GlyphView
{
    public required SymbolOccurrence Occ { get; init; }
    public required BoolMask Mask { get; init; }
    public required MaskBits Bits { get; init; }
    public BoolMask? RotatedMask { get; set; }
    public int BestVariant { get; set; }
}

public sealed class ClusterOptions
{
    public int TopK { get; set; } = 24;
    /// <summary>Pairs below this combined score are discarded entirely.</summary>
    public double ReportFloor { get; set; } = 0.35;
    public double SameSymbolFloor { get; set; } = 0.85;
    public int RotationVariants { get; set; } = 3;
}

/// <summary>
/// Compares glyph shapes. Cheap dialect first (pHash + scalar features) to pick candidate pairs, then an
/// exact comparison with a recorded score breakdown so every "these look alike" claim is inspectable.
/// </summary>
public sealed class GlyphComparer
{
    public static readonly double[] Weights = { 0.25, 0.15, 0.40, 0.10, 0.10 }; // iou, ncc, chamfer, moment, feature

    public List<GlyphView> Load(IEnumerable<SymbolOccurrence> occs, string docDir, Action<string>? log = null)
    {
        var views = new List<GlyphView>();
        foreach (var o in occs)
        {
            var path = Path.Combine(docDir, o.NormalizedPath);
            if (!File.Exists(path))
            {
                log?.Invoke($"cluster: missing normalised mask for {o.Id}: {path}");
                continue;
            }
            var mask = MaskIo.LoadMask(path);
            views.Add(new GlyphView { Occ = o, Mask = mask, Bits = new MaskBits(mask) });
        }
        return views;
    }

    public SimilarityBreakdown Compare(GlyphView a, GlyphView b, int rotationVariants = 3)
    {
        var iou = 0.0;
        var bestVariant = 0;
        if (rotationVariants <= 1)
        {
            iou = Math.Max(MaskBits.Iou(a.Bits.Words, b.Bits.Words), MaskBits.Iou(a.Bits.DilatedWords, b.Bits.DilatedWords));
        }
        else
        {
            var limit = Math.Min(a.Bits.Variants.Length, rotationVariants * 2);
            for (var v = 0; v < limit; v++)
            {
                var cand = Math.Max(MaskBits.Iou(a.Bits.Variants[v], b.Bits.Words),
                                    MaskBits.Iou(a.Bits.DilatedVariants[v], b.Bits.DilatedWords));
                if (cand > iou) { iou = cand; bestVariant = v; }
            }
        }

        var ncc = 0.0;
        var chamfer = 0.0;
        if (iou >= 0.25)
        {
            var aMask = a.Bits.MaskVariants[bestVariant];
            ncc = ShapeDescriptor.Ncc(aMask, b.Mask);
            chamfer = ShapeDescriptor.ChamferScore(aMask, b.Mask);
        }

        var moment = ShapeDescriptor.MomentScore(a.Occ.HuMoments, b.Occ.HuMoments);
        var feature = ShapeDescriptor.FeatureScore(a.Occ, b.Occ);
        var combined = Weights[0] * iou + Weights[1] * ncc + Weights[2] * chamfer + Weights[3] * moment + Weights[4] * feature;
        // Feature/moment agreement can never rescue shapes that do not overlap at all.
        combined *= Math.Min(1.0, 0.4 + iou);

        return new SimilarityBreakdown
        {
            Iou = JsonIo.Round(iou),
            Ncc = JsonIo.Round(ncc),
            Chamfer = JsonIo.Round(chamfer),
            Moment = JsonIo.Round(moment),
            Feature = JsonIo.Round(feature),
            Combined = JsonIo.Round(combined),
            BestRotationDeg = MaskBits.Rotations[bestVariant / 2],
            Mirrored = bestVariant % 2 == 1,
        };
    }

    /// <summary>Score every plausible pair. Candidates come from a cheap prefilter, never from an O(n^2) sweep.</summary>
    public List<GlyphPair> RankPairs(List<GlyphView> views, ClusterOptions opt, Action<int, int>? progress = null)
    {
        var pairs = new List<GlyphPair>();
        var candidates = new HashSet<(int, int)>();
        var n = views.Count;

        for (var i = 0; i < n; i++)
        {
            var scored = new List<(int j, double score)>(n);
            for (var j = 0; j < n; j++)
            {
                if (i == j) continue;
                var a = views[i];
                var b = views[j];
                // cheap gate: perceptual hash + scalar features
                var hamming = ShapeDescriptor.Hamming(a.Occ.PHash, b.Occ.PHash);
                var feature = ShapeDescriptor.FeatureScore(a.Occ, b.Occ);
                var cheap = (1.0 - hamming / 64.0) * 0.6 + feature * 0.4;
                scored.Add((j, cheap));
            }
            foreach (var (j, _) in scored.OrderByDescending(s => s.score).Take(opt.TopK))
            {
                var key = i < j ? (i, j) : (j, i);
                candidates.Add(key);
            }
            progress?.Invoke(i + 1, n);
        }

        foreach (var (i, j) in candidates)
        {
            var bd = Compare(views[i], views[j], opt.RotationVariants);
            if (bd.Combined < opt.ReportFloor) continue;
            var kind = bd.Combined >= opt.SameSymbolFloor ? "same-symbol" : bd.Combined >= 0.60 ? "variant-of" : "similar-style";
            pairs.Add(new GlyphPair
            {
                A = views[i].Occ.Id,
                B = views[j].Occ.Id,
                Breakdown = bd,
                Kind = kind,
                Method = "classical",
            });
        }

        pairs.Sort((x, y) => y.Breakdown.Combined.CompareTo(x.Breakdown.Combined));
        return pairs;
    }
}
