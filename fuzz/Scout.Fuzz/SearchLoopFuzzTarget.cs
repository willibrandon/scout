using System;

namespace Scout;

internal static class SearchLoopFuzzTarget
{
    internal static void Run(ReadOnlySpan<byte> input)
    {
        var fuzzInput = SearchLoopFuzzInput.Parse(input);
        var sink = new CountingLineSink();
        _ = LiteralLineSearcher.Search(
            fuzzInput.Haystack,
            fuzzInput.Needle,
            ref sink,
            asciiCaseInsensitive: fuzzInput.AsciiCaseInsensitive,
            invertMatch: fuzzInput.InvertMatch,
            lineRegexp: fuzzInput.LineRegexp,
            wordRegexp: fuzzInput.WordRegexp,
            maxMatchingLines: fuzzInput.MaxMatchingLines,
            crlf: fuzzInput.Crlf,
            nullData: fuzzInput.NullData);
    }
}
