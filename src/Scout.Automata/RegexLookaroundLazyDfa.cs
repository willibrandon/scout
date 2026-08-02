using System.Runtime.CompilerServices;

namespace Scout;

/// <summary>
/// Executes a bounded lazy DFA with delayed acceptance for byte-safe look-around.
/// </summary>
internal sealed class RegexLookaroundLazyDfa : IRegexLazyDfaDirection
{
    private readonly RegexNfa _nfa;
    private readonly Dictionary<RegexLookaroundDfaStateKey, RegexLookaroundLazyDfaState> _states = [];
    private readonly byte[] _byteContexts = new byte[256];
    private readonly byte[] _contextRepresentatives = new byte[257];
    private readonly byte[] _byteClasses = new byte[256];
    private readonly byte[] _byteClassRepresentatives = new byte[256];
    private readonly int _byteClassCount;
    private RegexDfaBudget _budget;

    private RegexLookaroundLazyDfa(RegexNfa nfa, ulong dfaSizeLimit)
    {
        _nfa = nfa;
        _budget = new RegexDfaBudget(dfaSizeLimit);
        _ = RegexLookaroundDfaOperations.BuildPreviousContexts(
            nfa,
            _byteContexts,
            _contextRepresentatives);
        _byteClassCount = RegexLookaroundDfaOperations.BuildByteClasses(
            nfa,
            _byteContexts,
            _byteClasses,
            _byteClassRepresentatives);
    }

    /// <summary>
    /// Creates a contextual runner when its first start state fits in the DFA budget.
    /// </summary>
    internal static bool TryCreate(
        RegexNfa nfa,
        ulong dfaSizeLimit,
        out RegexLookaroundLazyDfa? dfa)
    {
        if (!RegexLookaroundDfaOperations.CanCompile(nfa))
        {
            dfa = null;
            return false;
        }

        var candidate = new RegexLookaroundLazyDfa(nfa, dfaSizeLimit);
        if (!candidate.TryGetStartState(
            RegexLookaroundDfaOperations.StartContext,
            out _))
        {
            dfa = null;
            return false;
        }

        dfa = candidate;
        return true;
    }

    public bool TryFindEnd(
        ReadOnlySpan<byte> haystack,
        int start,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int end,
        out bool gaveUp)
    {
        _ = reachabilityCache;
        int position = Math.Clamp(start, 0, haystack.Length);
        byte previousContext = position == 0
            ? RegexLookaroundDfaOperations.StartContext
            : _byteContexts[haystack[position - 1]];
        if (!TryGetStartState(previousContext, out RegexLookaroundLazyDfaState? current))
        {
            end = -1;
            gaveUp = true;
            return false;
        }

        gaveUp = false;
        int lastAcceptEnd = -1;
        while (position < haystack.Length)
        {
            if (!TryTransition(current!, haystack[position], out current))
            {
                end = lastAcceptEnd;
                gaveUp = true;
                return lastAcceptEnd >= 0;
            }

            position++;
            if (current!.Accepting)
            {
                lastAcceptEnd = position - 1;
            }

            if (current.NfaStates.Length == 0)
            {
                end = lastAcceptEnd;
                return lastAcceptEnd >= 0;
            }
        }

        if (!TryEndOfInputTransition(current!, out current))
        {
            end = lastAcceptEnd;
            gaveUp = true;
            return lastAcceptEnd >= 0;
        }

        if (current!.Accepting)
        {
            lastAcceptEnd = haystack.Length;
        }

        end = lastAcceptEnd;
        return lastAcceptEnd >= 0;
    }

    public bool TryFindStartReverse(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int matchStart,
        out bool gaveUp)
    {
        _ = reachabilityCache;
        int lowerBound = Math.Clamp(start, 0, haystack.Length);
        int position = Math.Clamp(end, lowerBound, haystack.Length);
        byte previousContext = position == haystack.Length
            ? RegexLookaroundDfaOperations.StartContext
            : _byteContexts[haystack[position]];
        if (!TryGetStartState(previousContext, out RegexLookaroundLazyDfaState? current))
        {
            matchStart = -1;
            gaveUp = true;
            return false;
        }

        gaveUp = false;
        int lastAcceptStart = -1;
        while (position > lowerBound)
        {
            if (!TryTransition(current!, haystack[position - 1], out current))
            {
                matchStart = lastAcceptStart;
                gaveUp = true;
                return lastAcceptStart >= 0;
            }

            position--;
            if (current!.Accepting)
            {
                lastAcceptStart = position + 1;
            }

            if (current.NfaStates.Length == 0)
            {
                matchStart = lastAcceptStart;
                return lastAcceptStart >= 0;
            }
        }

        bool completed = lowerBound == 0
            ? TryEndOfInputTransition(current!, out current)
            : TryTransition(current!, haystack[lowerBound - 1], out current);
        if (!completed)
        {
            matchStart = lastAcceptStart;
            gaveUp = true;
            return lastAcceptStart >= 0;
        }

        if (current!.Accepting)
        {
            lastAcceptStart = lowerBound;
        }

        matchStart = lastAcceptStart;
        return lastAcceptStart >= 0;
    }

    private bool TryGetStartState(
        byte previousContext,
        out RegexLookaroundLazyDfaState? state)
    {
        return TryIntern([_nfa.StartState], previousContext, accepting: false, out state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryTransition(
        RegexLookaroundLazyDfaState state,
        byte value,
        out RegexLookaroundLazyDfaState? nextState)
    {
        byte byteClass = _byteClasses[value];
        if (state.TryGetTransition(byteClass, out nextState))
        {
            return true;
        }

        return TryCreateTransition(state, byteClass, out nextState);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryCreateTransition(
        RegexLookaroundLazyDfaState state,
        byte byteClass,
        out RegexLookaroundLazyDfaState? nextState)
    {
        nextState = null;
        byte value = _byteClassRepresentatives[byteClass];
        RegexLookaroundDfaOperations.ResolveContextualClosure(
            _nfa,
            state.NfaStates,
            state.PreviousContext,
            value,
            _contextRepresentatives,
            out int[] consumers,
            out bool accepting);
        int[] nextRoots = RegexLookaroundDfaOperations.MoveWithoutClosure(
            _nfa,
            consumers,
            value);
        if (!_budget.TryReserveLazyTransition(
                state.WouldAllocateDenseTransitionTable(byteClass),
                _byteClassCount) ||
            !TryIntern(nextRoots, _byteContexts[value], accepting, out nextState))
        {
            return false;
        }

        state.AddTransition(byteClass, nextState!);
        return true;
    }

    private bool TryEndOfInputTransition(
        RegexLookaroundLazyDfaState state,
        out RegexLookaroundLazyDfaState? nextState)
    {
        if (state.TryGetEndOfInputTransition(out nextState))
        {
            return true;
        }

        RegexLookaroundDfaOperations.ResolveContextualClosure(
            _nfa,
            state.NfaStates,
            state.PreviousContext,
            RegexLookaroundDfaOperations.EndOfInput,
            _contextRepresentatives,
            out _,
            out bool accepting);
        if (!_budget.TryReserveLazyTransition(allocatesDenseReferenceTable: false) ||
            !TryIntern(
                [],
                RegexLookaroundDfaOperations.StartContext,
                accepting,
                out nextState))
        {
            return false;
        }

        state.SetEndOfInputTransition(nextState!);
        return true;
    }

    private bool TryIntern(
        int[] nfaStates,
        byte previousContext,
        bool accepting,
        out RegexLookaroundLazyDfaState? state)
    {
        if (nfaStates.Length == 0)
        {
            previousContext = RegexLookaroundDfaOperations.StartContext;
        }

        var key = new RegexLookaroundDfaStateKey(nfaStates, previousContext, accepting);
        if (_states.TryGetValue(key, out state))
        {
            return true;
        }

        if (!_budget.TryReserve(RegexDfaBudget.EstimateStateBytes(
            nfaStates.Length,
            denseTransitions: false)))
        {
            state = null;
            return false;
        }

        state = new RegexLookaroundLazyDfaState(
            nfaStates,
            previousContext,
            accepting,
            _byteClassCount);
        _states.Add(key, state);
        return true;
    }
}
