
namespace Scout;

internal readonly struct GlobFuzzInput
{
    private GlobFuzzInput(byte[] pattern, byte[] candidate, GlobOptions options)
    {
        Pattern = pattern;
        Candidate = candidate;
        Options = options;
    }

    internal byte[] Pattern { get; }

    internal byte[] Candidate { get; }

    internal GlobOptions Options { get; }

    internal static GlobFuzzInput Parse(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
        {
            return new GlobFuzzInput([], [], GlobOptions.Unix);
        }

        byte options = input[0];
        ReadOnlySpan<byte> payload = input[1..];
        int separator = payload.IndexOf((byte)0);
        ReadOnlySpan<byte> pattern = separator < 0 ? payload : payload[..separator];
        ReadOnlySpan<byte> candidate = separator < 0 ? [] : payload[(separator + 1)..];

        return new GlobFuzzInput(
            pattern.ToArray(),
            candidate.ToArray(),
            CreateOptions(options));
    }

    private static GlobOptions CreateOptions(byte options)
    {
        bool windowsSeparators = (options & 1) != 0;
        return new GlobOptions(
            literalSeparator: (options & 2) != 0,
            backslashEscapes: !windowsSeparators && (options & 4) == 0,
            asciiCaseInsensitive: (options & 8) != 0,
            pathSeparators: windowsSeparators ? [(byte)'/', (byte)'\\'] : [(byte)'/'],
            matchBaseName: (options & 16) != 0,
            emptyAlternates: (options & 32) != 0,
            allowUnclosedClass: (options & 64) != 0);
    }
}
