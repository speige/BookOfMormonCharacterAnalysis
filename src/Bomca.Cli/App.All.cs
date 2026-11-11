namespace Bomca.Cli;

public static partial class App
{
    public static async Task<int> RunAll(Args args)
    {
        var steps = new List<string>();
        void Step(string name) { steps.Add(name); Console.WriteLine($"== {name}"); }

        Step("extract");
        RunExtract(new ArgsBuilder().Verb("extract").Opt("doc", "all").Build());

        if (!File.Exists(Path.Combine(DerivedDir, "current.json")))
        {
            Step("corpus fetch");
            await RunCorpus(new ArgsBuilder().Verb("corpus").Opt("edition", "current").Build() is var _ ? Args.Parse(new[] { "corpus", "fetch", "--edition", "current" }) : Args.Parse(new[] { "corpus" }));
        }
        Step("corpus build");
        RunCorpus(Args.Parse(new[] { "corpus", "build" }));

        foreach (var code in new[] { "W", "B" })
        {
            var docPath = System.IO.Path.Combine(OutRoot, code, "document.json");
            if (!System.IO.File.Exists(docPath)) continue;
            Step($"cluster {code}");
            RunCluster(Args.Parse(new[] { "cluster", "--doc", code, "--top-k", "40" }));
            Step($"inventory {code}");
            RunInventory(Args.Parse(new[] { "inventory", "--doc", code }));
            Step($"analyze {code}");
            RunAnalyze(Args.Parse(new[] { "analyze", "--doc", code }));
            Step($"match {code}");
            await RunMatch(Args.Parse(new[] { "match", "--doc", code }));
            Step($"significance {code}");
            await RunSignificance(Args.Parse(new[] { "significance", "--doc", code, "--iterations", args.GetInt("iterations", 100).ToString() }));
        }

        Step("report");
        RunReport(args);
        Console.WriteLine($"all: completed {steps.Count} steps");
        return 0;
    }
}
