using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Bomca.Core.Imaging;

public static class MaskIo
{
    public static void SaveMask(BoolMask mask, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var img = new Image<L8>(mask.Width, mask.Height);
        img.ProcessPixelRows(acc =>
        {
            for (var y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new L8(mask.Data[y * mask.Width + x] ? (byte)255 : (byte)0);
            }
        });
        img.SaveAsPng(path);
    }

    public static BoolMask LoadMask(string path)
    {
        using var img = Image.Load<L8>(path);
        var m = new BoolMask(img.Width, img.Height);
        img.ProcessPixelRows(acc =>
        {
            for (var y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    m.Data[y * m.Width + x] = row[x].PackedValue > 127;
            }
        });
        return m;
    }

    public static void SaveGray(GrayImage gray, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        gray.ToImage().SaveAsPng(path);
    }
}
