using System;

namespace Scout;

/// <summary>
/// Enumerates non-overlapping Aho-Corasick matches in a haystack.
/// </summary>
public ref struct AhoCorasickEnumerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly ReadOnlySpan<byte> haystack;
    private readonly bool anchored;
    private int offset;
    private int skipEmptyAt;
    private AhoCorasickMatch current;
    private bool hasCurrent;

    internal AhoCorasickEnumerator(AhoCorasickAutomaton automaton, ReadOnlySpan<byte> haystack, bool anchored)
    {
        this.automaton = automaton;
        this.haystack = haystack;
        this.anchored = anchored;
        offset = 0;
        skipEmptyAt = -1;
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
    /// Advances to the next non-overlapping match.
    /// </summary>
    /// <returns><see langword="true" /> when another match was found.</returns>
    public bool MoveNext()
    {
        AhoCorasickMatch? next = automaton.FindNextNonOverlapping(haystack, offset, skipEmptyAt, anchored);
        if (next is null)
        {
            hasCurrent = false;
            return false;
        }

        current = next.Value;
        if (current.IsEmpty)
        {
            offset = current.End + 1;
            skipEmptyAt = -1;
        }
        else
        {
            offset = current.End;
            skipEmptyAt = current.End;
        }

        hasCurrent = true;
        return true;
    }
}
