namespace Bomca.Cli;

public static partial class App
{
    private static int RunCluster(Args a) => NotImplemented("cluster");
    private static int RunReview(Args a) => NotImplemented("review");
    private static int RunInventory(Args a) => NotImplemented("inventory");
    private static int RunAnalyze(Args a) => NotImplemented("analyze");
    private static Task<int> RunMatch(Args a) => Task.FromResult(NotImplemented("match"));
    private static Task<int> RunSignificance(Args a) => Task.FromResult(NotImplemented("significance"));
    private static int RunReport(Args a) => NotImplemented("report");
    private static Task<int> RunAll(Args a) => Task.FromResult(NotImplemented("all"));
    private static Task<int> RunSelfTest(Args a) => Task.FromResult(NotImplemented("selftest"));

    private static int NotImplemented(string verb)
    {
        Console.Error.WriteLine($"'{verb}' is not implemented yet");
        return 3;
    }
}
