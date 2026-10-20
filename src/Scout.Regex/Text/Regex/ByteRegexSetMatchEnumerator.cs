namespace Scout.Text.Regex;

/// <summary>
/// Enumerates the non-overlapping matches of a <see cref="ByteRegexSet" /> in an input.
/// </summary>
/// <remarks>
/// Each step takes the leftmost match across all patterns, breaking ties by pattern order, and empty matches
/// are handled the way <see cref="ByteRegexMatchEnumerator" /> handles them.
/// </remarks>
public ref struct ByteRegexSetMatchEnumerator
{
    private readonly PatternSet _patternSet;
    private readonly ReadOnlySpan<byte> _input;
    private int _offset;
    private int _suppressedEmptyStart;

    internal ByteRegexSetMatchEnumerator(PatternSet patternSet, ReadOnlySpan<byte> input, int startAt)
    {
        _patternSet = patternSet;
        _input = input;
        _offset = Math.Min(startAt, input.Length + 1);
        _suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        Current = default;
    }

    /// <summary>
    /// Gets the set match at the current position; defined only after <see cref="MoveNext" /> returned <see langword="true" />.
    /// </summary>
    public ByteRegexSetMatch Current { get; private set; }

    /// <summary>
    /// Returns this enumerator so the instance can be used in a <see langword="foreach" /> statement.
    /// </summary>
    /// <returns>This enumerator.</returns>
    public readonly ByteRegexSetMatchEnumerator GetEnumerator()
    {
        return this;
    }

    /// <summary>
    /// Advances to the next non-overlapping set match.
    /// </summary>
    /// <returns><see langword="true" /> when another match was found.</returns>
    public bool MoveNext()
    {
        while (_offset <= _input.Length)
        {
            PatternSetMatch? match = _patternSet.Find(_input, _offset);
            if (!match.HasValue)
            {
                break;
            }

            var span = new MatcherMatch(match.Value.Match.Start, match.Value.Match.Length);
            if (MatchIterator.IsSuppressedEmpty(span, _suppressedEmptyStart))
            {
                _offset = MatchIterator.AdvanceAfterSuppressedEmpty(span, _input.Length);
                _suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            Current = new ByteRegexSetMatch(match.Value.PatternId, ByteRegexMatch.FromRegexMatch(match.Value.Match));
            _offset = MatchIterator.AdvanceAfterReported(span, _input.Length, ref _suppressedEmptyStart);
            return true;
        }

        _offset = _input.Length + 1;
        return false;
    }
}
