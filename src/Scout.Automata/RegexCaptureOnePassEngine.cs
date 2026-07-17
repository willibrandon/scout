namespace Scout;

/// <summary>
/// Replays captures through a deterministic ordered-NFA path and yields to the general engine
/// when more than one consuming transition can match at the same input position.
/// </summary>
/// <param name="nfa">The capture-aware NFA to execute.</param>
internal sealed class RegexCaptureOnePassEngine(RegexNfa nfa)
{
    private const int InitialCapacity = 16;
    private const int MaximumCachedClosureStateFactor = 4;
    private const int MaximumCaptureSlotCount = 32;
    private const int MaximumStateCount = 4_096;

    private readonly RegexNfa _nfa = nfa;
    private int[] _closureStates = new int[Math.Clamp(nfa.States.Count, 1, InitialCapacity)];
    private uint[] _closurePositionMasks = new uint[Math.Clamp(nfa.States.Count, 1, InitialCapacity)];
    private uint[] _closureClearMasks = new uint[Math.Clamp(nfa.States.Count, 1, InitialCapacity)];
    private readonly int[] _dynamicStates = new int[nfa.States.Count];
    private readonly uint[] _dynamicStatePositionMasks = new uint[nfa.States.Count];
    private readonly uint[] _dynamicStateClearMasks = new uint[nfa.States.Count];
    private readonly int[]?[] _closurePlanStates = new int[nfa.States.Count][];
    private readonly uint[]?[] _closurePlanPositionMasks = new uint[nfa.States.Count][];
    private readonly uint[]?[] _closurePlanClearMasks = new uint[nfa.States.Count][];
    private readonly bool[] _closurePlanAttempted = new bool[nfa.States.Count];
    private readonly int[] _visited = new int[nfa.States.Count];
    private readonly int[] _workingSlots = new int[checked(2 * (nfa.CaptureCount + 1))];
    private readonly RegexLiteralRunTable _literalRuns = RegexLiteralRunTable.Create(nfa);
    private readonly ulong[] _asciiAtomMatches = CreateAsciiAtomMatches(nfa);
    private int[] _states = [];
    private uint[] _statePositionMasks = [];
    private uint[] _stateClearMasks = [];
    private int _cachedClosureStateCount;
    private int _stateCount;
    private int _generation;
    private bool _disabled;
    private long _successfulReplayCount;

    /// <summary>
    /// Gets a value indicating whether this runner can still attempt one-pass capture replay.
    /// </summary>
    internal bool IsEnabled => !_disabled;

    /// <summary>
    /// Gets the number of exact spans replayed without the general ordered-NFA fallback.
    /// </summary>
    internal long SuccessfulReplayCount => _successfulReplayCount;

    /// <summary>
    /// Creates a bounded one-pass capture runner when its reusable state fits the engine budget.
    /// </summary>
    /// <param name="nfa">The capture-aware NFA to inspect.</param>
    /// <returns>A one-pass runner, or <see langword="null" /> when the NFA exceeds its budget.</returns>
    internal static RegexCaptureOnePassEngine? TryCreate(RegexNfa nfa)
    {
        int captureSlotCount = checked(2 * (nfa.CaptureCount + 1));
        return captureSlotCount <= MaximumCaptureSlotCount &&
            nfa.States.Count <= MaximumStateCount
            ? new RegexCaptureOnePassEngine(nfa)
            : null;
    }

    /// <summary>
    /// Attempts to replay one authoritative match span through a deterministic capture path.
    /// </summary>
    /// <param name="haystack">The complete haystack used to evaluate predicates.</param>
    /// <param name="start">The exact match start.</param>
    /// <param name="end">The exact exclusive match end.</param>
    /// <param name="captureSlots">Receives flattened capture start and end offsets.</param>
    /// <returns>The one-pass replay outcome.</returns>
    internal RegexCaptureOnePassResult TryReplay(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        Span<int> captureSlots)
    {
        if (_disabled)
        {
            return RegexCaptureOnePassResult.Fallback;
        }

        _workingSlots.AsSpan().Fill(-1);
        _workingSlots[0] = start;
        BuildClosure(_nfa.StartState, haystack, start);

        int position = start;
        while (position < end)
        {
            int matchedIndex = -1;
            int target = -1;
            int consume = 0;
            for (int index = 0; index < _stateCount; index++)
            {
                int stateIndex = _states[index];
                RegexNfaState state = _nfa.States[stateIndex];
                int candidateTarget;
                int candidateConsume;
                if (state.Kind == RegexNfaStateKind.Atom &&
                    TryGetAtomMatchLength(
                        stateIndex,
                        state,
                        haystack,
                        position,
                        out candidateConsume) &&
                    candidateConsume <= end - position)
                {
                    candidateTarget = state.Next;
                }
                else if (state.Kind == RegexNfaStateKind.Sparse &&
                    state.TryGetSparseTarget(haystack[position], out candidateTarget))
                {
                    candidateConsume = 1;
                }
                else
                {
                    continue;
                }

                if (matchedIndex >= 0)
                {
                    _disabled = true;
                    return RegexCaptureOnePassResult.Fallback;
                }

                matchedIndex = index;
                target = candidateTarget;
                consume = candidateConsume;
            }

            if (matchedIndex < 0 || consume <= 0)
            {
                return RegexCaptureOnePassResult.Fallback;
            }

            if (_literalRuns.TryGet(
                _states[matchedIndex],
                out ReadOnlySpan<byte> literalBytes,
                out int literalSuccessor))
            {
                if (literalBytes.Length > end - position ||
                    !haystack.Slice(position, literalBytes.Length).SequenceEqual(literalBytes))
                {
                    return RegexCaptureOnePassResult.Fallback;
                }

                target = literalSuccessor;
                consume = literalBytes.Length;
            }

            ApplyCaptureActions(
                _statePositionMasks[matchedIndex],
                _stateClearMasks[matchedIndex],
                position);
            position += consume;
            BuildClosure(target, haystack, position);
        }

        for (int index = 0; index < _stateCount; index++)
        {
            if (_nfa.States[_states[index]].Kind != RegexNfaStateKind.Accept)
            {
                continue;
            }

            ApplyCaptureActions(
                _statePositionMasks[index],
                _stateClearMasks[index],
                position);
            captureSlots.Fill(-1);
            _workingSlots.CopyTo(captureSlots);
            captureSlots[0] = start;
            captureSlots[1] = end;
            _successfulReplayCount++;
            return RegexCaptureOnePassResult.Success;
        }

        return RegexCaptureOnePassResult.Fallback;
    }

    private void BuildClosure(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position)
    {
        int rootState = stateIndex;
        if (TryUseClosurePlan(rootState))
        {
            return;
        }

        bool buildClosurePlan = rootState >= 0 && !_closurePlanAttempted[rootState];
        if (buildClosurePlan)
        {
            _closurePlanAttempted[rootState] = true;
        }

        _stateCount = 0;
        int closureCount = 0;
        int generation = NextGeneration();
        bool sawPredicate = false;
        if (stateIndex >= 0)
        {
            PushClosureState(ref closureCount, stateIndex, positionMask: 0, clearMask: 0);
        }

        while (closureCount > 0)
        {
            int frameIndex = --closureCount;
            stateIndex = _closureStates[frameIndex];
            uint positionMask = _closurePositionMasks[frameIndex];
            uint clearMask = _closureClearMasks[frameIndex];
            if (_visited[stateIndex] == generation)
            {
                continue;
            }

            _visited[stateIndex] = generation;
            RegexNfaState state = _nfa.States[stateIndex];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    if (state.Alternative >= 0)
                    {
                        PushClosureState(
                            ref closureCount,
                            state.Alternative,
                            positionMask,
                            clearMask);
                    }

                    if (state.Next >= 0)
                    {
                        PushClosureState(
                            ref closureCount,
                            state.Next,
                            positionMask,
                            clearMask);
                    }

                    break;
                case RegexNfaStateKind.CaptureStart:
                    int startSlot = checked(2 * state.CaptureIndex);
                    int endSlot = startSlot + 1;
                    SetPositionAction(ref positionMask, ref clearMask, startSlot);
                    SetClearAction(ref positionMask, ref clearMask, endSlot);
                    if (state.Next >= 0)
                    {
                        PushClosureState(
                            ref closureCount,
                            state.Next,
                            positionMask,
                            clearMask);
                    }

                    break;
                case RegexNfaStateKind.CaptureEnd:
                    int slot = checked((2 * state.CaptureIndex) + 1);
                    SetPositionAction(ref positionMask, ref clearMask, slot);
                    if (state.Next >= 0)
                    {
                        PushClosureState(
                            ref closureCount,
                            state.Next,
                            positionMask,
                            clearMask);
                    }

                    break;
                case RegexNfaStateKind.Predicate:
                    sawPredicate = true;
                    if (RegexByteClass.PredicateMatches(
                            haystack,
                            position,
                            state.AtomKind,
                            state.MultiLine,
                            state.Crlf,
                            state.LineTerminator,
                            state.Utf8,
                            state.UnicodeClasses) &&
                        state.Next >= 0)
                    {
                        PushClosureState(
                            ref closureCount,
                            state.Next,
                            positionMask,
                            clearMask);
                    }

                    break;
                case RegexNfaStateKind.Atom:
                case RegexNfaStateKind.Sparse:
                case RegexNfaStateKind.Accept:
                    AddState(stateIndex, positionMask, clearMask);
                    break;
            }
        }

        _states = _dynamicStates;
        _statePositionMasks = _dynamicStatePositionMasks;
        _stateClearMasks = _dynamicStateClearMasks;
        if (buildClosurePlan && !sawPredicate)
        {
            CacheClosurePlan(rootState);
        }
    }

    private void AddState(int stateIndex, uint positionMask, uint clearMask)
    {
        int index = _stateCount++;
        _dynamicStates[index] = stateIndex;
        _dynamicStatePositionMasks[index] = positionMask;
        _dynamicStateClearMasks[index] = clearMask;
    }

    private bool TryUseClosurePlan(int rootState)
    {
        if (rootState < 0 ||
            !_closurePlanAttempted[rootState] ||
            _closurePlanStates[rootState] is not int[] states)
        {
            return false;
        }

        _states = states;
        _statePositionMasks = _closurePlanPositionMasks[rootState]!;
        _stateClearMasks = _closurePlanClearMasks[rootState]!;
        _stateCount = states.Length;
        return true;
    }

    private void CacheClosurePlan(int rootState)
    {
        int maximumCachedStateCount = checked(
            MaximumCachedClosureStateFactor * _nfa.States.Count);
        if (_stateCount > maximumCachedStateCount - _cachedClosureStateCount)
        {
            return;
        }

        int[] states = _dynamicStates.AsSpan(0, _stateCount).ToArray();
        uint[] positionMasks = _dynamicStatePositionMasks.AsSpan(0, _stateCount).ToArray();
        uint[] clearMasks = _dynamicStateClearMasks.AsSpan(0, _stateCount).ToArray();
        _closurePlanStates[rootState] = states;
        _closurePlanPositionMasks[rootState] = positionMasks;
        _closurePlanClearMasks[rootState] = clearMasks;
        _cachedClosureStateCount += _stateCount;
        _states = states;
        _statePositionMasks = positionMasks;
        _stateClearMasks = clearMasks;
    }

    private void PushClosureState(
        ref int closureCount,
        int stateIndex,
        uint positionMask,
        uint clearMask)
    {
        EnsureClosureCapacity(closureCount + 1);
        _closureStates[closureCount] = stateIndex;
        _closurePositionMasks[closureCount] = positionMask;
        _closureClearMasks[closureCount] = clearMask;
        closureCount++;
    }

    private void EnsureClosureCapacity(int requiredLength)
    {
        if (requiredLength <= _closureStates.Length)
        {
            return;
        }

        int newLength = Math.Max(requiredLength, checked(_closureStates.Length * 2));
        Array.Resize(ref _closureStates, newLength);
        Array.Resize(ref _closurePositionMasks, newLength);
        Array.Resize(ref _closureClearMasks, newLength);
    }

    private void ApplyCaptureActions(uint positionMask, uint clearMask, int position)
    {
        while (positionMask != 0)
        {
            int slot = System.Numerics.BitOperations.TrailingZeroCount(positionMask);
            _workingSlots[slot] = position;
            positionMask &= positionMask - 1;
        }

        while (clearMask != 0)
        {
            int slot = System.Numerics.BitOperations.TrailingZeroCount(clearMask);
            _workingSlots[slot] = -1;
            clearMask &= clearMask - 1;
        }
    }

    private static void SetPositionAction(ref uint positionMask, ref uint clearMask, int slot)
    {
        uint bit = 1U << slot;
        positionMask |= bit;
        clearMask &= ~bit;
    }

    private static void SetClearAction(ref uint positionMask, ref uint clearMask, int slot)
    {
        uint bit = 1U << slot;
        clearMask |= bit;
        positionMask &= ~bit;
    }

    private int NextGeneration()
    {
        if (_generation == int.MaxValue)
        {
            Array.Clear(_visited);
            _generation = 0;
        }

        return ++_generation;
    }

    private bool TryGetAtomMatchLength(
        int stateIndex,
        RegexNfaState state,
        ReadOnlySpan<byte> haystack,
        int position,
        out int length)
    {
        if ((uint)position < (uint)haystack.Length && haystack[position] <= 0x7F)
        {
            byte value = haystack[position];
            int word = (stateIndex * 2) + (value >> 6);
            if ((_asciiAtomMatches[word] & (1UL << (value & 0x3F))) != 0)
            {
                length = 1;
                return true;
            }

            length = 0;
            return false;
        }

        return state.TryGetAtomMatchLength(haystack, position, out length);
    }

    private static ulong[] CreateAsciiAtomMatches(RegexNfa nfa)
    {
        ulong[] matches = new ulong[checked(nfa.States.Count * 2)];
        byte[] haystack = new byte[1];
        for (int stateIndex = 0; stateIndex < nfa.States.Count; stateIndex++)
        {
            RegexNfaState state = nfa.States[stateIndex];
            if (state.Kind != RegexNfaStateKind.Atom)
            {
                continue;
            }

            for (int value = 0; value <= 0x7F; value++)
            {
                haystack[0] = (byte)value;
                if (state.TryGetAtomMatchLength(haystack, position: 0, out int length) &&
                    length == 1)
                {
                    int word = (stateIndex * 2) + (value >> 6);
                    matches[word] |= 1UL << (value & 0x3F);
                }
            }
        }

        return matches;
    }
}
