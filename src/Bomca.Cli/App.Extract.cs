using Bomca.Core.Documents;
using Bomca.Core.Imaging;
using Bomca.Core.Json;

namespace Bomca.Cli;

public static partial class App
{
    public static string OutRoot => Path.Combine(DocumentCatalog.RepoRoot(), "out");

    public static int RunExtract(Args args)
    {
        var codes = args.GetList("doc");
        if (codes.Count == 0) codes = new List<string> { "all" };
        if (codes.Contains("all")) codes = DocumentCatalog.AllCodes.ToList();

        var outRoot = Path.Combine(OutRoot, args.Get("out-subdir", ""));
        var all = new List<object>();
        foreach (var code in codes)
        {
            var prevDebug = Environment.GetEnvironmentVariable("BOMCA_DEBUG");
            Environment.SetEnvironmentVariable("BOMCA_DEBUG", args.Has("debug") ? "1" : prevDebug);
            var docs = DocumentCatalog.Extract(code, outRoot, Console.WriteLine);
            Environment.SetEnvironmentVariable("BOMCA_DEBUG", prevDebug);
            foreach (var doc in docs)
            {
                var dir = Path.Combine(outRoot, doc.DocCode);
                JsonIo.Write(Path.Combine(dir, "document.json"), doc);
                var spec = DocumentCatalog.Get(doc.DocCode);
                if (spec is { Images.Length: 1 })
                {
                    Artifacts.WriteOverlay(spec.Images[0], doc, Path.Combine(dir, "overlay.png"));
                }
                Artifacts.WriteContactSheet(doc, dir, Path.Combine(dir, "contact-sheet.png"));
                all.Add(new
                {
                    doc.DocCode,
                    doc.Name,
                    doc.Kind,
                    Symbols = doc.Symbols.Count(s => s.Note != "title-row"),
                    Rows = doc.Rows.Count(r => !r.Skipped),
                    doc.ImageWidth,
                    doc.ImageHeight,
                });
            }
        }

        JsonIo.Write(Path.Combine(outRoot, "index.json"), all);
        Console.WriteLine($"extract: {all.Count} document(s) written under {outRoot}");
        return 0;
    }
}
