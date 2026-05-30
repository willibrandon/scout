using System;

namespace Scout;

internal static class GlobCompileFuzzTarget
{
    internal static void Run(ReadOnlySpan<byte> input)
    {
        var fuzzInput = GlobFuzzInput.Parse(input);
        try
        {
            var glob = Glob.Parse(fuzzInput.Pattern, fuzzInput.Options);
            _ = glob.IsMatch(fuzzInput.Candidate);
        }
        catch (GlobParseException)
        {
        }
    }
}
