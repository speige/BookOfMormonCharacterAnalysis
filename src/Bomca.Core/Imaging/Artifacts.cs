using Bomca.Core.Imaging;
using Bomca.Core.Model;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Bomca.Core.Imaging;

public static class Artifacts
{
    private static Font? _font;

    private static Font GetFont(float size)
    {
        if (_font is null)
        {
            var candidates = new[] { "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans", "Tahoma", "Verdana" };
            foreach (var name in candidates)
            {
                if (SystemFonts.Collection.TryGet(name, out var family))
                {
                    _font = family.CreateFont(size);
                    break;
                }
            }
            if (_font is null)
            {
                var families = SystemFonts.Collection.Families.ToList();
                if (families.Count > 0) _font = families[0].CreateFont(size);
            }
        }
        return _font ?? throw new InvalidOperationException("no system font available");
    }

    /// <summary>Page image with every glyph boxed and labelled, plus title/exclusion regions in a second colour.</summary>
    public static void WriteOverlay(string sourceImage, DocumentRecord doc, string outPath)
    {
        using var img = Image.Load<Rgba32>(sourceImage);
        img.Mutate(ctx =>
        {
            Font font = null!;
            try { font = GetFont(Math.Max(10, doc.ImageWidth / 900f)); } catch { /* text is optional */ }

            foreach (var row in doc.Rows)
            {
                if (row.GlyphCount == 0) continue;
                var color = row.Skipped ? Color.Orange : Color.FromRgba(0, 90, 255, 180);
                ctx.Draw(color, 2, new RectangleF(row.X, row.Y, Math.Max(1, row.W), Math.Max(1, row.H)));
                if (font is not null)
                {
                    ctx.DrawText(row.RowId + (row.Skipped ? " (title)" : ""), font, color,
                        new PointF(row.X + 4, Math.Max(0, row.Y - font.Size - 2)));
                }
            }

            foreach (var s in doc.Symbols)
            {
                var color = s.Note == "title-row" ? Color.Orange : (s.Confidence < 0.99 ? Color.Goldenrod : Color.Red);
                ctx.Draw(color, 1.5f, new RectangleF(s.X, s.Y, Math.Max(1, s.W), Math.Max(1, s.H)));
            }

            foreach (var r in doc.Rejected)
            {
                ctx.Draw(Color.Gray, 1, new RectangleF(r.X, r.Y, Math.Max(1, r.W), Math.Max(1, r.H)));
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        img.SaveAsPng(outPath);
    }

    /// <summary>Grid of glyph crops with their occurrence IDs, for rapid human review.</summary>
    public static void WriteContactSheet(DocumentRecord doc, string outDir, string outPath, int cols = 12)
    {
        var glyphs = doc.Symbols.Where(s => s.Note != "title-row").OrderBy(s => s.GlobalIndex).ToList();
        if (glyphs.Count == 0) return;
        const int cell = 96;
        const int labelH = 16;
        var rows = (glyphs.Count + cols - 1) / cols;
        using var sheet = new Image<Rgba32>(cols * cell, rows * (cell + labelH), new Rgba32(250, 250, 245));
        Font? font = null;
        try { font = GetFont(11); } catch { }

        sheet.Mutate(ctx =>
        {
            for (var i = 0; i < glyphs.Count; i++)
            {
                var g = glyphs[i];
                var path = Path.Combine(outDir, g.CropPath);
                if (!File.Exists(path)) continue;
                using var crop = Image.Load<Rgba32>(path);
                var scaled = crop.Clone(c => c.Resize(new ResizeOptions
                {
                    Size = new Size(cell - 8, cell - 8),
                    Mode = ResizeMode.Max,
                }));
                var x = i % cols * cell;
                var y = i / cols * (cell + labelH);
                ctx.DrawImage(scaled, new Point(x + 4, y + 4), 1f);
                ctx.Draw(Color.LightGray, 1, new RectangleF(x, y, cell, cell + labelH));
                if (font is not null)
                {
                    var label = g.Id;
                    ctx.DrawText(label, font, Color.Black, new PointF(x + 2, y + cell - 2));
                }
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        sheet.SaveAsPng(outPath);
    }
}
