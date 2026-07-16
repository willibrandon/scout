namespace Scout;

/// <summary>
/// Stores one lazily materialized DFA state and promotes its transition representation as needed.
/// </summary>
/// <param name="nfaStates">The ordered NFA-state closure represented by this state.</param>
/// <param name="acceptIndex">The first accepting NFA-state index, or a negative value.</param>
internal sealed class RegexLazyDfaState(int[] nfaStates, int acceptIndex)
{
    private byte _singleTransitionByte;
    private RegexLazyDfaState? _singleTransition;
    private RegexLazyDfaState?[]? _denseTransitions;
    private bool _hasObservedSelfLoop;
    private bool _acceleratorComputed;
    private byte[]? _acceleratorNeedles;

    /// <summary>
    /// Gets the ordered NFA-state closure represented by this state.
    /// </summary>
    public int[] NfaStates { get; } = nfaStates;

    /// <summary>
    /// Gets the first accepting NFA-state index, or a negative value.
    /// </summary>
    public int AcceptIndex { get; } = acceptIndex;

    /// <summary>
    /// Gets a value indicating whether accelerator analysis has completed.
    /// </summary>
    public bool AcceleratorComputed => _acceleratorComputed;

    /// <summary>
    /// Gets a value indicating whether a materialized transition has returned to this state.
    /// </summary>
    public bool HasObservedSelfLoop => _hasObservedSelfLoop;

    /// <summary>
    /// Determines whether adding a transition will allocate the dense reference table.
    /// </summary>
    /// <param name="value">The input byte for the transition.</param>
    /// <returns><see langword="true" /> when adding the transition will allocate the table.</returns>
    public bool WouldAllocateDenseTransitionTable(byte value)
    {
        return _denseTransitions is null &&
            _singleTransition is not null &&
            value != _singleTransitionByte;
    }

    /// <summary>
    /// Gets the accelerator needles when the state has a useful self-loop accelerator.
    /// </summary>
    /// <param name="needles">Receives the accelerator needles.</param>
    /// <returns><see langword="true" /> when an accelerator is available.</returns>
    public bool TryGetAccelerator(out byte[] needles)
    {
        needles = _acceleratorNeedles ?? [];
        return _acceleratorComputed && _acceleratorNeedles is not null;
    }

    /// <summary>
    /// Records the completed accelerator analysis for this state.
    /// </summary>
    /// <param name="needles">The useful needles, or <see langword="null" /> when none exist.</param>
    public void SetAccelerator(byte[]? needles)
    {
        _acceleratorNeedles = needles;
        _acceleratorComputed = true;
    }

    /// <summary>
    /// Attempts to get the transition already learned for one byte.
    /// </summary>
    /// <param name="value">The input byte.</param>
    /// <param name="state">Receives the destination state.</param>
    /// <returns><see langword="true" /> when the transition is materialized.</returns>
    public bool TryGetTransition(byte value, out RegexLazyDfaState? state)
    {
        RegexLazyDfaState?[]? dense = _denseTransitions;
        if (dense is not null)
        {
            state = dense[value];
            return state is not null;
        }

        RegexLazyDfaState? single = _singleTransition;
        if (single is not null && value == _singleTransitionByte)
        {
            state = single;
            return true;
        }

        state = null;
        return false;
    }

    /// <summary>
    /// Adds one newly materialized transition, promoting two distinct bytes to a dense table.
    /// </summary>
    /// <param name="value">The input byte.</param>
    /// <param name="state">The destination state.</param>
    public void AddTransition(byte value, RegexLazyDfaState state)
    {
        _hasObservedSelfLoop |= ReferenceEquals(this, state);
        RegexLazyDfaState?[]? dense = _denseTransitions;
        if (dense is not null)
        {
            dense[value] = state;
            return;
        }

        RegexLazyDfaState? single = _singleTransition;
        if (single is null)
        {
            _singleTransitionByte = value;
            _singleTransition = state;
            return;
        }

        dense = new RegexLazyDfaState[256];
        dense[_singleTransitionByte] = single;
        dense[value] = state;
        _singleTransition = null;
        _denseTransitions = dense;
    }
}
