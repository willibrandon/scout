
namespace Scout;

internal struct JsonMatchCollector : IMatchLineSink
{
    private readonly List<JsonMatchSpan> matches;
    private readonly ReadOnlyMemory<byte>? replacement;
    private readonly IReadOnlyList<byte[]> patterns;
    private readonly bool asciiCaseInsensitive;

    public JsonMatchCollector(
        List<JsonMatchSpan> matches,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive)
    {
        this.matches = matches;
        this.replacement = replacement;
        this.patterns = patterns;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
    }

    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        int start = checked((int)matchColumn - 1);
        byte[]? expandedReplacement = replacement is ReadOnlyMemory<byte> replacementValue
            ? ReplacementFormatter.Expand(replacementValue.Span, match, patterns, asciiCaseInsensitive)
            : null;
        matches.Add(new JsonMatchSpan(start, start + match.Length, expandedReplacement));
    }
}
