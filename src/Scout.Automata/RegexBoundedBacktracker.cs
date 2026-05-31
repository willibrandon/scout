using System;

namespace Scout;

internal sealed class RegexBoundedBacktracker
{
    private const int MaxBacktrackCells = 262_144;

    private readonly RegexNfa nfa;
    private readonly PikeVm fallback;

    public RegexBoundedBacktracker(RegexNfa nfa)
    {
        this.nfa = nfa;
        fallback = new PikeVm(nfa);
    }

    public static bool CanCompile(RegexNfa nfa)
    {
        for (int index = 0; index < nfa.States.Count; index++)
        {
            if (nfa.States[index].Kind is RegexNfaStateKind.Split
                or RegexNfaStateKind.GreedyLoopSplit
                or RegexNfaStateKind.LazyLoopSplit)
            {
                return false;
            }
        }

        return true;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int positionCount = haystack.Length - start + 1;
        if (positionCount > MaxBacktrackCells / nfa.States.Count)
        {
            return fallback.TryMatchAt(haystack, start, out length);
        }

        bool[][] visiting = new bool[nfa.States.Count][];
        for (int index = 0; index < visiting.Length; index++)
        {
            visiting[index] = new bool[positionCount];
        }

        if (TryMatchState(nfa.StartState, start, start, haystack, visiting, out int end))
        {
            length = end - start;
            return true;
        }

        length = 0;
        return false;
    }

    private bool TryMatchState(
        int stateIndex,
        int start,
        int position,
        ReadOnlySpan<byte> haystack,
        bool[][] visiting,
        out int end)
    {
        end = 0;
        if (stateIndex < 0)
        {
            return false;
        }

        int relativePosition = position - start;
        if (visiting[stateIndex][relativePosition])
        {
            return false;
        }

        visiting[stateIndex][relativePosition] = true;
        try
        {
            RegexNfaState state = nfa.States[stateIndex];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Accept:
                    end = position;
                    return true;
                case RegexNfaStateKind.Atom:
                    return RegexByteClass.TryGetAtomMatchLength(
                            haystack,
                            position,
                            state.AtomKind,
                            state.Value.Span,
                            state.CaseInsensitive,
                            state.MultiLine,
                            state.DotMatchesNewline,
                            state.Crlf,
                            state.LineTerminator,
                            state.Utf8,
                            state.UnicodeClasses,
                            out int consume) &&
                        TryMatchState(state.Next, start, position + consume, haystack, visiting, out end);
                case RegexNfaStateKind.Predicate:
                    return RegexByteClass.PredicateMatches(
                            haystack,
                            position,
                            state.AtomKind,
                            state.MultiLine,
                            state.Crlf,
                            state.LineTerminator,
                            state.Utf8,
                            state.UnicodeClasses) &&
                        TryMatchState(state.Next, start, position, haystack, visiting, out end);
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    return TryMatchState(state.Next, start, position, haystack, visiting, out end) ||
                        TryMatchState(state.Alternative, start, position, haystack, visiting, out end);
                default:
                    return false;
            }
        }
        finally
        {
            visiting[stateIndex][relativePosition] = false;
        }
    }
}
