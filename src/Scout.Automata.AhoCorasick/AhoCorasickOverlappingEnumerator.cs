
namespace Scout;

/// <summary>
/// Enumerates overlapping Aho-Corasick matches in a haystack.
/// </summary>
public ref struct AhoCorasickOverlappingEnumerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly ReadOnlySpan<byte> haystack;
    private int state;
    private int nextIndex;
    private int boundaryOffset;
    private int end;
    private int outputIndex;
    private int emptyIndex;
    private AhoCorasickMatch current;
    private bool hasCurrent;

    internal AhoCorasickOverlappingEnumerator(AhoCorasickAutomaton automaton, ReadOnlySpan<byte> haystack)
    {
        this.automaton = automaton;
        this.haystack = haystack;
        state = 0;
        nextIndex = 0;
        boundaryOffset = 0;
        end = 0;
        outputIndex = -1;
        emptyIndex = 0;
        current = default;
        hasCurrent = false;
    }

    /// <summary>
    /// Gets the current match.
    /// </summary>
    public AhoCorasickMatch Current => hasCurrent
        ? current
        : throw new InvalidOperationException("enumeration has no current value");

    /// <summary>
    /// Advances to the next overlapping match.
    /// </summary>
    /// <returns><see langword="true" /> when another match was found.</returns>
    public bool MoveNext()
    {
        while (true)
        {
            if (emptyIndex < automaton.EmptyPatternCount)
            {
                current = automaton.GetEmptyPatternMatch(emptyIndex, boundaryOffset);
                emptyIndex++;
                hasCurrent = true;
                return true;
            }

            if (outputIndex >= 0)
            {
                if (outputIndex < automaton.GetOutputCount(state))
                {
                    current = automaton.GetOutputMatch(state, outputIndex, end);
                    outputIndex++;
                    hasCurrent = true;
                    return true;
                }

                outputIndex = -1;
                boundaryOffset = end;
                emptyIndex = 0;
                continue;
            }

            if (nextIndex >= haystack.Length)
            {
                hasCurrent = false;
                return false;
            }

            state = automaton.NextStateForEnumerator(state, haystack[nextIndex]);
            nextIndex++;
            end = nextIndex;
            outputIndex = 0;
        }
    }
}
