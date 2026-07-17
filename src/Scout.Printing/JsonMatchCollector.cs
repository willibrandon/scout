namespace Scout;

/// <summary>
/// Collects match spans and optional replacement metadata for one JSON output record.
/// </summary>
/// <param name="matches">The list that receives match spans.</param>
/// <param name="replacement">The optional replacement template.</param>
/// <param name="searchPlan">The optional authoritative regex search plan.</param>
internal struct JsonMatchCollector(
    List<JsonMatchSpan> matches,
    ReadOnlyMemory<byte>? replacement,
    RegexSearchPlan? searchPlan = null) : IMatchLineSink, IDisposable
{
    private readonly List<JsonMatchSpan> _matches = matches;
    private readonly ReadOnlyMemory<byte>? _replacement = replacement;
    private readonly RegexSearchPlan? _searchPlan = searchPlan;
    private RegexCaptureReplaySession? _captureSession;
    private readonly (
        ReplacementTemplate Template,
        int[] CaptureSlots)? _captureState =
        replacement is ReadOnlyMemory<byte> replacementValue
            ? CreateCaptureState(replacementValue, searchPlan)
            : null;

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
        byte[]? expandedReplacement = null;
        if (_replacement is ReadOnlyMemory<byte> replacementValue &&
            _captureState is { } captureState)
        {
            IReplacementCaptureProvider? captureProvider = GetCaptureProvider(captureState.Template);
            expandedReplacement = captureProvider is null
                ? ReplacementFormatter.Expand(
                    replacementValue.Span,
                    line,
                    start,
                    match.Length,
                    searchPlan: null,
                    captureState.Template,
                    captureState.CaptureSlots)
                : ReplacementFormatter.Expand(
                    replacementValue.Span,
                    line,
                    start,
                    match.Length,
                    captureProvider,
                    start,
                    captureState.Template,
                    captureState.CaptureSlots);
        }

        _matches.Add(new JsonMatchSpan(start, start + match.Length, expandedReplacement));
    }

    private IReplacementCaptureProvider? GetCaptureProvider(ReplacementTemplate template)
    {
        if (!template.RequiresSubcaptures || _searchPlan is null)
        {
            return null;
        }

        return _captureSession ??= new RegexCaptureReplaySession(_searchPlan);
    }

    /// <summary>
    /// Returns the operation-scoped capture runner when this collector used native subcaptures.
    /// </summary>
    public void Dispose()
    {
        _captureSession?.Dispose();
        _captureSession = null;
    }

    private static (
        ReplacementTemplate Template,
        int[] CaptureSlots) CreateCaptureState(
            ReadOnlyMemory<byte> replacement,
            RegexSearchPlan? searchPlan)
    {
        var template = ReplacementTemplate.Create(replacement.Span);
        int captureCount = template.RequiresSubcaptures
            ? Math.Max(template.HighestCapture, searchPlan?.CaptureCount ?? 0)
            : 0;
        return (
            template,
            new int[checked(2 * (captureCount + 1))]);
    }
}
