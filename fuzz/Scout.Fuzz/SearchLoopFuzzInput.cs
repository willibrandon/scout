
namespace Scout;

internal readonly struct SearchLoopFuzzInput
{
    private SearchLoopFuzzInput(
        byte[] needle,
        byte[] haystack,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxMatchingLines,
        bool crlf,
        bool nullData)
    {
        Needle = needle;
        Haystack = haystack;
        AsciiCaseInsensitive = asciiCaseInsensitive;
        InvertMatch = invertMatch;
        LineRegexp = lineRegexp;
        WordRegexp = wordRegexp;
        MaxMatchingLines = maxMatchingLines;
        Crlf = crlf;
        NullData = nullData;
    }

    internal byte[] Needle { get; }

    internal byte[] Haystack { get; }

    internal bool AsciiCaseInsensitive { get; }

    internal bool InvertMatch { get; }

    internal bool LineRegexp { get; }

    internal bool WordRegexp { get; }

    internal ulong? MaxMatchingLines { get; }

    internal bool Crlf { get; }

    internal bool NullData { get; }

    internal static SearchLoopFuzzInput Parse(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
        {
            return new SearchLoopFuzzInput([], [], false, false, false, false, null, false, false);
        }

        byte options = input[0];
        ReadOnlySpan<byte> payload = input[1..];
        int separator = payload.IndexOf((byte)0);
        ReadOnlySpan<byte> needle = separator < 0 ? payload : payload[..separator];
        ReadOnlySpan<byte> haystack = separator < 0 ? [] : payload[(separator + 1)..];
        ulong? maxMatchingLines = (options & 64) == 0 ? null : (ulong)((options >> 6) + 1);

        return new SearchLoopFuzzInput(
            needle.ToArray(),
            haystack.ToArray(),
            asciiCaseInsensitive: (options & 1) != 0,
            invertMatch: (options & 2) != 0,
            lineRegexp: (options & 4) != 0,
            wordRegexp: (options & 8) != 0,
            maxMatchingLines,
            crlf: (options & 16) != 0,
            nullData: (options & 32) != 0);
    }
}
