using System;

namespace Scout;

internal static class FuzzTargetKindParser
{
    internal static bool TryParse(string value, out FuzzTargetKind target)
    {
        if (string.Equals(value, "regex-parse", StringComparison.Ordinal))
        {
            target = FuzzTargetKind.RegexParse;
            return true;
        }

        if (string.Equals(value, "glob-compile", StringComparison.Ordinal))
        {
            target = FuzzTargetKind.GlobCompile;
            return true;
        }

        if (string.Equals(value, "search-loop", StringComparison.Ordinal))
        {
            target = FuzzTargetKind.SearchLoop;
            return true;
        }

        target = FuzzTargetKind.RegexParse;
        return false;
    }
}
