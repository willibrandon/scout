using System;

namespace Scout;

internal static class FuzzTargetRunner
{
    internal static void Run(FuzzTargetKind target, ReadOnlySpan<byte> input)
    {
        switch (target)
        {
            case FuzzTargetKind.RegexParse:
                RegexParseFuzzTarget.Run(input);
                return;
            case FuzzTargetKind.GlobCompile:
                GlobCompileFuzzTarget.Run(input);
                return;
            case FuzzTargetKind.SearchLoop:
                SearchLoopFuzzTarget.Run(input);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown fuzz target.");
        }
    }
}
