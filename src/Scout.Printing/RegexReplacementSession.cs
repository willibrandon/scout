namespace Scout;

/// <summary>
/// Expands native-regex replacements through one parsed template and one operation-scoped
/// exact-capture runner.
/// </summary>
/// <param name="replacement">The replacement expression bytes.</param>
/// <param name="searchPlan">The authoritative regex plan that supplies subcaptures.</param>
internal sealed class RegexReplacementSession(
    ReadOnlyMemory<byte> replacement,
    RegexSearchPlan searchPlan) : IDisposable
{
    private readonly ReadOnlyMemory<byte> _replacement = replacement;
    private readonly RegexSearchPlan _searchPlan = searchPlan;
    private readonly ReplacementTemplate _template =
        ReplacementTemplate.Create(replacement.Span);
    private RegexCaptureReplaySession? _captureSession;
    private int[]? _captureSlots;

    /// <summary>
    /// Expands one replacement against an authoritative match span.
    /// </summary>
    /// <param name="haystack">The complete capture haystack.</param>
    /// <param name="matchStart">The authoritative match start.</param>
    /// <param name="matchLength">The authoritative match length.</param>
    /// <returns>The expanded replacement bytes.</returns>
    internal byte[] Expand(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength)
    {
        IReplacementCaptureProvider? captureProvider = GetCaptureProvider();
        return captureProvider is null
            ? ReplacementFormatter.Expand(
                _replacement.Span,
                haystack,
                matchStart,
                matchLength,
                searchPlan: null,
                _template,
                GetCaptureSlots())
            : ReplacementFormatter.Expand(
                _replacement.Span,
                haystack,
                matchStart,
                matchLength,
                captureProvider,
                matchStart,
                _template,
                GetCaptureSlots());
    }

    /// <summary>
    /// Replaces known match spans in one multiline output record.
    /// </summary>
    /// <param name="line">The output record slice.</param>
    /// <param name="captureHaystack">The complete authoritative capture haystack.</param>
    /// <param name="lineStartInHaystack">The record start in <paramref name="captureHaystack" />.</param>
    /// <param name="starts">The authoritative match starts.</param>
    /// <param name="lengths">The authoritative match lengths.</param>
    /// <param name="replacementColumns">Receives one-based replacement columns.</param>
    /// <param name="replacementLengths">Receives replacement lengths.</param>
    /// <returns>The replaced record bytes.</returns>
    internal byte[] ReplaceLine(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> captureHaystack,
        int lineStartInHaystack,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        List<long> replacementColumns,
        List<int> replacementLengths)
    {
        IReplacementCaptureProvider? captureProvider = GetCaptureProvider();
        return captureProvider is null
            ? ReplacementFormatter.ReplaceLine(
                line,
                starts,
                lengths,
                _replacement.Span,
                replacementColumns,
                replacementLengths,
                searchPlan: null,
                _template,
                GetCaptureSlots())
            : ReplacementFormatter.ReplaceLine(
                line,
                captureHaystack,
                lineStartInHaystack,
                starts,
                lengths,
                _replacement.Span,
                replacementColumns,
                replacementLengths,
                captureProvider,
                starts,
                _template,
                GetCaptureSlots());
    }

    /// <summary>
    /// Returns the operation-scoped capture runner when this replacement uses subcaptures.
    /// </summary>
    public void Dispose()
    {
        _captureSession?.Dispose();
        _captureSession = null;
    }

    private IReplacementCaptureProvider? GetCaptureProvider()
    {
        if (!_template.RequiresSubcaptures)
        {
            return null;
        }

        return _captureSession ??= new RegexCaptureReplaySession(_searchPlan);
    }

    private int[] GetCaptureSlots()
    {
        int captureCount = _template.RequiresSubcaptures
            ? Math.Max(_template.HighestCapture, _searchPlan.CaptureCount)
            : 0;
        return _captureSlots ??= new int[checked(2 * (captureCount + 1))];
    }
}
