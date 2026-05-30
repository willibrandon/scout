using System;
using SharpFuzz;

namespace Scout;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TryGetTarget(args, out FuzzTargetKind target))
        {
            PrintUsage();
            return 2;
        }

        Fuzzer.OutOfProcess.Run(stream =>
        {
            byte[] input = FuzzInput.ReadAll(stream);
            FuzzTargetRunner.Run(target, input);
        });
        return 0;
    }

    private static bool TryGetTarget(string[] args, out FuzzTargetKind target)
    {
        target = FuzzTargetKind.RegexParse;
        return args.Length == 1 &&
            FuzzTargetKindParser.TryParse(args[0], out target);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("usage: Scout.Fuzz <regex-parse|glob-compile|search-loop>");
    }
}
