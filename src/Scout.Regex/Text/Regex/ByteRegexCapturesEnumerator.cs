namespace Scout.Text.Regex;

/// <summary>
/// Enumerates the non-overlapping matches of a <see cref="ByteRegex" /> in an input together with their capture groups.
/// </summary>
/// <remarks>
/// Matches are selected exactly as <see cref="ByteRegexMatchEnumerator" /> selects them, so a loop over captures
/// reports the same spans as a loop over matches, including the handling of empty matches.
/// </remarks>
public ref struct ByteRegexCapturesEnumerator
{
    private readonly RegexAutomaton _automaton;
    private readonly ReadOnlySpan<byte> _input;
    private int _offset;
    private int _suppressedEmptyStart;

    internal ByteRegexCapturesEnumerator(RegexAutomaton automaton, ReadOnlySpan<byte> input, int startAt)
    {
        _automaton = automaton;
        _input = input;
        _offset = Math.Min(startAt, input.Length + 1);
        _suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        Current = null!;
    }

    /// <summary>
    /// Gets the captures at the current position; defined only after <see cref="MoveNext" /> returned <see langword="true" />.
    /// </summary>
    public ByteRegexCaptures Current { get; private set; }

    /// <summary>
    /// Returns this enumerator so the instance can be used in a <see langword="foreach" /> statement.
    /// </summary>
    /// <returns>This enumerator.</returns>
    public readonly ByteRegexCapturesEnumerator GetEnumerator()
    {
        return this;
    }

    /// <summary>
    /// Advances to the next non-overlapping match and its captures.
    /// </summary>
    /// <returns><see langword="true" /> when another match was found.</returns>
    public bool MoveNext()
    {
        while (_offset <= _input.Length)
        {
            RegexCaptures? captures = _automaton.FindCaptures(_input, _offset);
            if (captures is null)
            {
                break;
            }

            var span = new MatcherMatch(captures.Match.Start, captures.Match.Length);
            if (MatchIterator.IsSuppressedEmpty(span, _suppressedEmptyStart))
            {
                _offset = MatchIterator.AdvanceAfterSuppressedEmpty(span, _input.Length);
                _suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            Current = new ByteRegexCaptures(captures);
            _offset = MatchIterator.AdvanceAfterReported(span, _input.Length, ref _suppressedEmptyStart);
            return true;
        }

        _offset = _input.Length + 1;
        return false;
    }
}
