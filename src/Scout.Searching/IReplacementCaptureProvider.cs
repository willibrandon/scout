namespace Scout;

/// <summary>
/// Supplies capture slots from the authoritative engine that selected a replacement match.
/// </summary>
internal interface IReplacementCaptureProvider
{
    /// <summary>
    /// Gets the number of capturing groups.
    /// </summary>
    int CaptureCount { get; }

    /// <summary>
    /// Gets the capture indexes keyed by capture name.
    /// </summary>
    IReadOnlyDictionary<string, int> CaptureNames { get; }

    /// <summary>
    /// Collects capture slots for a match selected by this provider's engine.
    /// </summary>
    /// <param name="haystack">The complete haystack used by the engine.</param>
    /// <param name="matchStart">The reported match start.</param>
    /// <param name="matchLength">The reported match length.</param>
    /// <param name="searchStart">The start of the successful engine search.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end capture offsets.</param>
    /// <returns><see langword="true" /> when the engine reproduces the selected match and its captures.</returns>
    bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        int searchStart,
        Span<int> captureSlots);
}
