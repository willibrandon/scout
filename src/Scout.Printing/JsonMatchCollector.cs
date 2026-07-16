namespace Scout;

/// <summary>
/// Collects match spans and optional replacement metadata for one JSON output record.
/// </summary>
/// <param name="matches">The list that receives match spans.</param>
/// <param name="replacement">The optional replacement template.</param>
/// <param name="patterns">The ordered regex patterns.</param>
/// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
/// <param name="capturePlan">The optional authoritative capture plan.</param>
internal struct JsonMatchCollector(
    List<JsonMatchSpan> matches,
    ReadOnlyMemory<byte>? replacement,
    IReadOnlyList<byte[]> patterns,
    bool asciiCaseInsensitive,
    ReplacementCapturePlan? capturePlan = null) : IMatchLineSink
{
    private readonly List<JsonMatchSpan> _matches = matches;
    private readonly ReadOnlyMemory<byte>? _replacement = replacement;
    private readonly IReadOnlyList<byte[]> _patterns = patterns;
    private readonly ReplacementCapturePlan? _capturePlan = capturePlan;
    private readonly (
        ReplacementTemplate Template,
        int[] CaptureSlots)? _captureState =
        replacement is ReadOnlyMemory<byte> replacementValue
            ? CreateCaptureState(replacementValue, capturePlan)
            : null;
    private readonly bool _asciiCaseInsensitive = asciiCaseInsensitive;

    /// <summary>
    /// Adds one match and expands replacement metadata in its original containing-line context.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchByteOffset">The zero-based byte offset of the match.</param>
    /// <param name="matchColumn">The one-based byte column of the match.</param>
    /// <param name="line">The containing line bytes.</param>
    /// <param name="match">The matching bytes.</param>
    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        int start = checked((int)matchColumn - 1);
        byte[]? expandedReplacement =
            _replacement is ReadOnlyMemory<byte> replacementValue &&
            _captureState is { } captureState
            ? ReplacementFormatter.Expand(
                replacementValue.Span,
                line,
                start,
                match.Length,
                _patterns,
                _asciiCaseInsensitive,
                _capturePlan,
                captureState.Template,
                captureState.CaptureSlots)
            : null;
        _matches.Add(new JsonMatchSpan(start, start + match.Length, expandedReplacement));
    }

    private static (
        ReplacementTemplate Template,
        int[] CaptureSlots) CreateCaptureState(
            ReadOnlyMemory<byte> replacement,
            ReplacementCapturePlan? capturePlan)
    {
        var template = ReplacementTemplate.Create(
            replacement.Span,
            capturePlan?.CaptureCount ?? 0);
        int captureCount = Math.Max(template.HighestCapture, capturePlan?.CaptureCount ?? 0);
        return (
            template,
            new int[checked(2 * (captureCount + 1))]);
    }
}
