namespace Bomca.Cli;

public static partial class App
{
    public static async Task<int> RunAsync(string[] argv)
    {
        var args = Args.Parse(argv);
        if (args.Verb is "help" or "--help" or "-h" or "")
        {
            Console.WriteLine(HelpText);
            return 0;
        }

        var manifest = ManifestStore.Start(args.Verb, argv);
        var verbArgs = new List<string>();
        try
        {
            int code;
            switch (args.Verb)
            {
                case "extract":
                    code = RunExtract(args);
                    break;
                case "cluster":
                    code = RunCluster(args);
                    break;
                case "review":
                    code = RunReview(args);
                    break;
                case "corpus":
                    code = await RunCorpus(args);
                    break;
                case "inventory":
                    code = RunInventory(args);
                    break;
                case "analyze":
                    code = RunAnalyze(args);
                    break;
                case "match":
                    code = await RunMatch(args);
                    break;
                case "significance":
                    code = await RunSignificance(args);
                    break;
                case "report":
                    code = RunReport(args);
                    break;
                case "verify":
                    code = RunVerify(args);
                    break;
                case "focus":
                    code = RunFocus(args);
                    break;
                case "all":
                    code = await RunAll(args);
                    break;
                case "selftest":
                    code = await RunSelfTest(args);
                    break;
                default:
                    Console.Error.WriteLine($"unknown verb '{args.Verb}'");
                    Console.Error.WriteLine(HelpText);
                    ManifestStore.Finish(manifest, $"unknown verb '{args.Verb}'");
                    return 2;
            }

            ManifestStore.Finish(manifest);
            return code;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("BOMCA_TRACE") == "1")
                Console.Error.WriteLine(ex);
            ManifestStore.Finish(manifest, ex.Message);
            return 1;
        }
    }

    public const string HelpText = """
        bomca - Book of Mormon character analysis pipeline

        Usage: bomca <verb> [options]

        Verbs
          extract       Segment glyph images out of the source documents and write document.json,
                        per-glyph crops (out/<DOC>/crops) and 96x96 normalised masks.
          cluster       Compare glyph shapes and rank candidate duplicate pairs.
          review        Write the human review sheet (review.html) for a document.
          corpus        fetch|build the Book of Mormon text corpus.
          inventory     Group glyphs into canonical classes at one or more similarity thresholds.
                        Writes one inventory file per threshold plus labels-<DOC>-tNNN.json.
          analyze       Structural tests: row geometry, black square, tick glyphs, compression.
          match         Run the decipherment search (fingerprints, joint constraints, alignment, anchors).
          significance  Randomised controls, FDR correction and held-out validation.
          verify        Test an external translation claim (e.g. --source grover) against the glyphs and corpus.
          focus         Fuzzy contextual search: where does an approximate paraphrase belong? Writes a focus
                        file that 'match --focus' uses to skip the exhaustive corpus scan.
          report        Build the self-contained HTML report.
          all           Run the whole pipeline end to end with default settings.
          selftest      Run the synthetic end-to-end checks (fake corpus + fake symbols).

        Common options
          --doc W|B|C|FW|all        target document(s)
          --out-subdir <name>       write under out/<name> instead of out/
          --json                    machine-readable stdout

        Extract options
          --merge-gap <ratio>       glyph merge gap as a fraction of median glyph height (default 0.10)
          --no-skip-title           keep the handwritten title row as data
          --min-area <px>           minimum component area (default 40)

        Cluster options
          --threshold <0..1>        similarity threshold for the reported duplicate set (default 0.80)
          --top-k <n>               candidate pairs per glyph kept by the prefilter (default 40)
          --llm                     ask Ollama to adjudicate borderline pairs

        Match options
          --reverse                 read the glyph stream right-to-left (the external claim's direction)
          --split <n>               search n independent segments of the document separately
          --exclude-numerals        drop numeral-like glyphs before searching (numerals carry dates, not words)
          --widen                   widen phrase targets with corpus-constrained synonyms (Ollama)
          --focus <file>            restrict the window scanner to focused regions (bomca focus output)
          --focus-auto              same, using out/<DOC>/focus-<source>.json

        Inventory options
          --thresholds 0.7,0.8,0.9  one inventory version per threshold
        """;
}
