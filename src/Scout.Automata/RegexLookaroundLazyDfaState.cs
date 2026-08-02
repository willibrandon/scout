namespace Scout;

/// <summary>
/// Stores one contextual lazy-DFA state and its compressed transitions.
/// </summary>
internal sealed class RegexLookaroundLazyDfaState(
    int[] nfaStates,
    byte previousContext,
    bool accepting,
    int byteClassCount)
{
    private byte _singleTransitionClass;
    private RegexLookaroundLazyDfaState? _singleTransition;
    private RegexLookaroundLazyDfaState?[]? _denseTransitions;
    private RegexLookaroundLazyDfaState? _endOfInputTransition;
    private bool _hasEndOfInputTransition;

    internal int[] NfaStates { get; } = nfaStates;

    internal byte PreviousContext { get; } = previousContext;

    internal bool Accepting { get; } = accepting;

    internal bool WouldAllocateDenseTransitionTable(byte byteClass)
    {
        return _denseTransitions is null &&
            _singleTransition is not null &&
            byteClass != _singleTransitionClass;
    }

    internal bool TryGetTransition(byte byteClass, out RegexLookaroundLazyDfaState? state)
    {
        RegexLookaroundLazyDfaState?[]? dense = _denseTransitions;
        if (dense is not null)
        {
            state = dense[byteClass];
            return state is not null;
        }

        RegexLookaroundLazyDfaState? single = _singleTransition;
        if (single is not null && byteClass == _singleTransitionClass)
        {
            state = single;
            return true;
        }

        state = null;
        return false;
    }

    internal void AddTransition(byte byteClass, RegexLookaroundLazyDfaState state)
    {
        RegexLookaroundLazyDfaState?[]? dense = _denseTransitions;
        if (dense is not null)
        {
            dense[byteClass] = state;
            return;
        }

        RegexLookaroundLazyDfaState? single = _singleTransition;
        if (single is null)
        {
            _singleTransitionClass = byteClass;
            _singleTransition = state;
            return;
        }

        dense = new RegexLookaroundLazyDfaState[byteClassCount];
        dense[_singleTransitionClass] = single;
        dense[byteClass] = state;
        _singleTransition = null;
        _denseTransitions = dense;
    }

    internal bool TryGetEndOfInputTransition(out RegexLookaroundLazyDfaState? state)
    {
        state = _endOfInputTransition;
        return _hasEndOfInputTransition;
    }

    internal void SetEndOfInputTransition(RegexLookaroundLazyDfaState state)
    {
        _endOfInputTransition = state;
        _hasEndOfInputTransition = true;
    }
}
