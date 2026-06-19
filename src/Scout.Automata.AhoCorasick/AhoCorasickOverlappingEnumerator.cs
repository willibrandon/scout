
namespace Scout;

/// <summary>
/// Enumerates overlapping Aho-Corasick matches in a haystack.
/// </summary>
public ref struct AhoCorasickOverlappingEnumerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly ReadOnlySpan<byte> haystack;
    private readonly int[] outputCounts;
    private readonly int[]? denseTransitions;
    private readonly ushort[]? compactDenseTransitions;
    private readonly bool hasEmptyPatterns;
    private int state;
    private int nextIndex;
    private int boundaryOffset;
    private int end;
    private int outputCount;
    private int outputIndex;
    private int emptyIndex;
    private AhoCorasickMatch current;
    private bool hasCurrent;

    internal AhoCorasickOverlappingEnumerator(AhoCorasickAutomaton automaton, ReadOnlySpan<byte> haystack)
    {
        this.automaton = automaton;
        this.haystack = haystack;
        outputCounts = automaton.GetOutputCountsForEnumerator();
        compactDenseTransitions = automaton.GetCompactDenseTransitionsForEnumerator();
        denseTransitions = compactDenseTransitions is null
            ? automaton.GetDenseTransitionsForEnumerator()
            : null;
        hasEmptyPatterns = automaton.EmptyPatternCount != 0;
        state = 0;
        nextIndex = 0;
        boundaryOffset = 0;
        end = 0;
        outputCount = 0;
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
        if (!hasEmptyPatterns && compactDenseTransitions is not null)
        {
            return MoveNextCompactDenseNoEmpty();
        }

        if (!hasEmptyPatterns && denseTransitions is not null)
        {
            return MoveNextDenseNoEmpty();
        }

        while (true)
        {
            if (hasEmptyPatterns && emptyIndex < automaton.EmptyPatternCount)
            {
                current = automaton.GetEmptyPatternMatch(emptyIndex, boundaryOffset);
                emptyIndex++;
                hasCurrent = true;
                return true;
            }

            if (outputIndex >= 0)
            {
                if (outputIndex < outputCount)
                {
                    current = automaton.GetOutputMatch(state, outputIndex, end);
                    outputIndex++;
                    hasCurrent = true;
                    return true;
                }

                outputIndex = -1;
                outputCount = 0;
                if (hasEmptyPatterns)
                {
                    boundaryOffset = end;
                    emptyIndex = 0;
                }

                continue;
            }

            if (nextIndex >= haystack.Length)
            {
                hasCurrent = false;
                return false;
            }

            byte value = haystack[nextIndex];
            state = compactDenseTransitions is not null
                ? compactDenseTransitions[(state * 256) + value]
                : denseTransitions is not null
                ? denseTransitions[(state * 256) + value]
                : automaton.NextStateForEnumerator(state, value);
            nextIndex++;
            end = nextIndex;
            outputCount = outputCounts[state];
            if (outputCount != 0)
            {
                outputIndex = 0;
            }
            else
            {
                if (hasEmptyPatterns)
                {
                    boundaryOffset = end;
                    emptyIndex = 0;
                }
            }
        }
    }

    private bool MoveNextCompactDenseNoEmpty()
    {
        if (outputIndex >= 0)
        {
            if (outputIndex < outputCount)
            {
                current = automaton.GetOutputMatch(state, outputIndex, end);
                outputIndex++;
                hasCurrent = true;
                return true;
            }

            outputIndex = -1;
            outputCount = 0;
        }

        ushort[] transitions = compactDenseTransitions!;
        while (nextIndex < haystack.Length)
        {
            state = transitions[(state * 256) + haystack[nextIndex]];
            nextIndex++;
            int count = outputCounts[state];
            if (count == 0)
            {
                continue;
            }

            end = nextIndex;
            outputCount = count;
            outputIndex = 1;
            current = automaton.GetOutputMatch(state, 0, end);
            hasCurrent = true;
            return true;
        }

        hasCurrent = false;
        return false;
    }

    private bool MoveNextDenseNoEmpty()
    {
        if (outputIndex >= 0)
        {
            if (outputIndex < outputCount)
            {
                current = automaton.GetOutputMatch(state, outputIndex, end);
                outputIndex++;
                hasCurrent = true;
                return true;
            }

            outputIndex = -1;
            outputCount = 0;
        }

        int[] transitions = denseTransitions!;
        while (nextIndex < haystack.Length)
        {
            state = transitions[(state * 256) + haystack[nextIndex]];
            nextIndex++;
            int count = outputCounts[state];
            if (count == 0)
            {
                continue;
            }

            end = nextIndex;
            outputCount = count;
            outputIndex = 1;
            current = automaton.GetOutputMatch(state, 0, end);
            hasCurrent = true;
            return true;
        }

        hasCurrent = false;
        return false;
    }
}
