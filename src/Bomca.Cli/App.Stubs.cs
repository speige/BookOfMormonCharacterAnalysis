namespace Bomca.Cli;

public static partial class App
{
    private static Task<int> RunSelfTest(Args a) => Task.FromResult(NotImplemented("selftest"));

    private static int NotImplemented(string verb)
    {
        Console.Error.WriteLine($"'{verb}' is not implemented yet");
        return 3;
    }
}
