
namespace Scout;

internal struct JsonMatchCollector : IMatchLineSink
{
    private readonly List<JsonMatchSpan> matches;
    private readonly ReadOnlyMemory<byte>? replacement;
    private readonly IReadOnlyList<byte[]> patterns;
    private readonly ReplacementCapturePlan? capturePlan;
    private readonly ReplacementTemplate? template;
    private readonly int[]? captureStarts;
    private readonly int[]? captureLengths;
    private readonly Dictionary<string, int>? captureNames;
    private readonly bool asciiCaseInsensitive;

    public JsonMatchCollector(
        List<JsonMatchSpan> matches,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan = null)
    {
        this.matches = matches;
        this.replacement = replacement;
        this.patterns = patterns;
        this.capturePlan = capturePlan;
        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            template = ReplacementTemplate.Create(replacementValue.Span, patterns);
            captureStarts = new int[Math.Max(1, template.HighestCapture + 1)];
            captureLengths = new int[Math.Max(1, template.HighestCapture + 1)];
            captureNames = template.UsesNamedCaptureReferences
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : null;
        }
        else
        {
            template = null;
            captureStarts = null;
            captureLengths = null;
            captureNames = null;
        }

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
            ? ReplacementFormatter.Expand(
                replacementValue.Span,
                match,
                patterns,
                asciiCaseInsensitive,
                capturePlan,
                template,
                captureStarts,
                captureLengths,
                captureNames)
            : null;
        matches.Add(new JsonMatchSpan(start, start + match.Length, expandedReplacement));
    }
}
