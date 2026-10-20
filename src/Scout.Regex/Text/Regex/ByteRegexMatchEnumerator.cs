namespace Scout.Text.Regex;

/// <summary>
/// Enumerates the non-overlapping matches of a <see cref="ByteRegex" /> in an input.
/// </summary>
/// <remarks>
/// Empty matches follow Rust's <c>find_iter</c>: each search resumes at the end of the previous match, and an
/// empty match that ends where the previous match ended is skipped by moving one byte forward.
/// </remarks>
public ref struct ByteRegexMatchEnumerator
{
    private readonly RegexAutomaton _automaton;
    private readonly ReadOnlySpan<byte> _input;
    private int _offset;
    private int _suppressedEmptyStart;

    internal ByteRegexMatchEnumerator(RegexAutomaton automaton, ReadOnlySpan<byte> input, int startAt)
    {
        _automaton = automaton;
        _input = input;
        _offset = Math.Min(startAt, input.Length + 1);
        _suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        Current = default;
    }

    /// <summary>
    /// Gets the match at the current position; defined only after <see cref="MoveNext" /> returned <see langword="true" />.
    /// </summary>
    public ByteRegexMatch Current { get; private set; }

    /// <summary>
    /// Returns this enumerator so the instance can be used in a <see langword="foreach" /> statement.
    /// </summary>
    /// <returns>This enumerator.</returns>
    public readonly ByteRegexMatchEnumerator GetEnumerator()
    {
        return this;
    }

    /// <summary>
    /// Advances to the next non-overlapping match.
    /// </summary>
    /// <returns><see langword="true" /> when another match was found.</returns>
    public bool MoveNext()
    {
        while (_offset <= _input.Length)
        {
            RegexMatch? match = _automaton.Find(_input, _offset);
            if (!match.HasValue)
            {
                break;
            }

            var span = new MatcherMatch(match.Value.Start, match.Value.Length);
            if (MatchIterator.IsSuppressedEmpty(span, _suppressedEmptyStart))
            {
                _offset = MatchIterator.AdvanceAfterSuppressedEmpty(span, _input.Length);
                _suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            Current = ByteRegexMatch.FromRegexMatch(match.Value);
            _offset = MatchIterator.AdvanceAfterReported(span, _input.Length, ref _suppressedEmptyStart);
            return true;
        }

        _offset = _input.Length + 1;
        return false;
    }
}
