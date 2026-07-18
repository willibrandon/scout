namespace Scout;

/// <summary>
/// Stores match spans and replacement spans for one buffered replacement line.
/// </summary>
internal sealed class ReplacementLineAccumulator()
{
    private readonly List<int> _starts = [];
    private readonly List<int> _lengths = [];
    private readonly List<int> _searchStarts = [];
    private readonly List<long> _replacementColumns = [];
    private readonly List<int> _replacementLengths = [];

    /// <summary>
    /// Gets the match starts relative to the current line.
    /// </summary>
    internal List<int> Starts => _starts;

    /// <summary>
    /// Gets the match lengths for the current line.
    /// </summary>
    internal List<int> Lengths => _lengths;

    /// <summary>
    /// Gets the authoritative search starts used to replay captures.
    /// </summary>
    internal List<int> SearchStarts => _searchStarts;

    /// <summary>
    /// Gets the one-based columns of expanded replacements.
    /// </summary>
    internal List<long> ReplacementColumns => _replacementColumns;

    /// <summary>
    /// Gets the lengths of expanded replacements.
    /// </summary>
    internal List<int> ReplacementLengths => _replacementLengths;

    /// <summary>
    /// Clears all state associated with the current line while retaining reusable storage.
    /// </summary>
    internal void Clear()
    {
        _starts.Clear();
        _lengths.Clear();
        _searchStarts.Clear();
        _replacementColumns.Clear();
        _replacementLengths.Clear();
    }
}
