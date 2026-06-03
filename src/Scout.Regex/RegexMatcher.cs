
namespace Scout;

/// <summary>
/// Adapts Scout's byte regex automaton to the matcher callback ABI.
/// </summary>
public sealed class RegexMatcher
{
    private readonly RegexAutomaton automaton;

    private RegexMatcher(RegexAutomaton automaton)
    {
        this.automaton = automaton;
    }

    /// <summary>
    /// Compiles a regex matcher.
    /// </summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <returns>The compiled matcher.</returns>
    public static RegexMatcher Compile(ReadOnlySpan<byte> pattern)
    {
        return new RegexMatcher(RegexAutomaton.Compile(pattern));
    }

    /// <summary>
    /// Finds the first match in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public MatcherMatch? Find(ReadOnlySpan<byte> haystack)
    {
        RegexMatch? match = automaton.Find(haystack);
        return match.HasValue ? new MatcherMatch(match.Value.Start, match.Value.Length) : null;
    }

    /// <summary>
    /// Returns a value indicating whether the pattern matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when a match exists.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return automaton.IsMatch(haystack);
    }

    /// <summary>
    /// Iterates all non-overlapping matches using a struct sink.
    /// </summary>
    /// <typeparam name="TSink">The sink type.</typeparam>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="sink">The sink receiving matches.</param>
    /// <returns>The number of reported matches.</returns>
    public int ForEachMatch<TSink>(ReadOnlySpan<byte> haystack, ref TSink sink)
        where TSink : struct, IMatcherSink
    {
        int count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= haystack.Length)
        {
            RegexMatch? regexMatch = automaton.Find(haystack, offset);
            if (!regexMatch.HasValue)
            {
                return count;
            }

            var match = new MatcherMatch(regexMatch.Value.Start, regexMatch.Value.Length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, haystack.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!sink.Matched(haystack, match))
            {
                return count;
            }

            count++;
            offset = MatchIterator.AdvanceAfterReported(match, haystack.Length, ref suppressedEmptyStart);
        }

        return count;
    }

    /// <summary>
    /// Iterates all non-overlapping matches using a function pointer and explicit state.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="callback">The synchronous callback. It must not store <paramref name="haystack" /> beyond the call.</param>
    /// <param name="state">An explicit state pointer whose lifetime must cover this call.</param>
    /// <returns>The number of reported matches.</returns>
    public unsafe int ForEachMatch(
        ReadOnlySpan<byte> haystack,
        delegate* managed<void*, ReadOnlySpan<byte>, MatcherMatch, bool> callback,
        void* state)
    {
        int count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= haystack.Length)
        {
            RegexMatch? regexMatch = automaton.Find(haystack, offset);
            if (!regexMatch.HasValue)
            {
                return count;
            }

            var match = new MatcherMatch(regexMatch.Value.Start, regexMatch.Value.Length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, haystack.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!callback(state, haystack, match))
            {
                return count;
            }

            count++;
            offset = MatchIterator.AdvanceAfterReported(match, haystack.Length, ref suppressedEmptyStart);
        }

        return count;
    }
}
