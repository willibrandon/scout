namespace Scout;

/// <summary>
/// Supplies replacement captures by replaying the authoritative PCRE2 expression.
/// </summary>
/// <param name="regex">The compiled PCRE2 expression used by the search.</param>
/// <param name="recordTerminator">The terminator excluded from line-oriented PCRE2 subjects.</param>
internal sealed class Pcre2ReplacementCaptureProvider(
    Pcre2Regex regex,
    ReadOnlyMemory<byte> recordTerminator = default) : IReplacementCaptureProvider
{
    private readonly Pcre2Regex _regex = regex ?? throw new ArgumentNullException(nameof(regex));
    private readonly ReadOnlyMemory<byte> _recordTerminator = recordTerminator;

    /// <inheritdoc />
    int IReplacementCaptureProvider.CaptureCount => _regex.CaptureCount;

    /// <inheritdoc />
    IReadOnlyDictionary<string, int> IReplacementCaptureProvider.CaptureNames => _regex.CaptureNames;

    /// <inheritdoc />
    bool IReplacementCaptureProvider.TryCollectCaptureSlots(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        int searchStart,
        Span<int> captureSlots)
    {
        if (!_recordTerminator.IsEmpty)
        {
            haystack = Pcre2SearchOperations.GetPcre2MatchLine(
                haystack,
                _recordTerminator);
        }

        return _regex.TryCollectCaptureSlots(
            haystack,
            matchStart,
            matchLength,
            searchStart,
            captureSlots);
    }
}
