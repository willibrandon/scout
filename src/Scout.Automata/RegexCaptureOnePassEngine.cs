namespace Scout;

/// <summary>
/// Replays captures through a deterministic ordered-NFA path and yields to the general engine
/// when more than one consuming transition can match at the same input position.
/// </summary>
/// <param name="nfa">The capture-aware NFA to execute.</param>
internal sealed class RegexCaptureOnePassEngine(RegexNfa nfa)
{
    private const int InitialCapacity = 16;
    private const int MaximumCaptureSlotCount = 32;
    private const int MaximumStateCount = 4_096;

    private readonly RegexNfa _nfa = nfa;
    private readonly List<RegexCaptureClosureFrame> _closureStack =
        new(Math.Clamp(nfa.States.Count, 1, InitialCapacity));
    private readonly List<int> _states = new(Math.Clamp(nfa.States.Count, 1, InitialCapacity));
    private readonly int[] _visited = new int[nfa.States.Count];
    private readonly int _slotCount = checked(2 * (nfa.CaptureCount + 1));
    private readonly int[] _workingSlots = new int[checked(2 * (nfa.CaptureCount + 1))];
    private readonly ulong[] _asciiAtomMatches = CreateAsciiAtomMatches(nfa);
    private int[] _stateSlots = new int[
        checked(Math.Clamp(nfa.States.Count, 1, InitialCapacity) *
        (2 * (nfa.CaptureCount + 1)))];
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
        BuildClosure(_nfa.StartState, haystack, start, _workingSlots);

        int position = start;
        while (position < end)
        {
            int matchedIndex = -1;
            int target = -1;
            int consume = 0;
            for (int index = 0; index < _states.Count; index++)
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

            ReadOnlySpan<int> slots = GetSlots(matchedIndex);
            position += consume;
            BuildClosure(target, haystack, position, slots);
        }

        for (int index = 0; index < _states.Count; index++)
        {
            if (_nfa.States[_states[index]].Kind != RegexNfaStateKind.Accept)
            {
                continue;
            }

            captureSlots.Fill(-1);
            GetSlots(index).CopyTo(captureSlots);
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
        int position,
        ReadOnlySpan<int> slots)
    {
        slots.CopyTo(_workingSlots);
        _states.Clear();
        _closureStack.Clear();
        int generation = NextGeneration();
        if (stateIndex >= 0)
        {
            _closureStack.Add(RegexCaptureClosureFrame.Explore(stateIndex));
        }

        while (_closureStack.Count > 0)
        {
            int frameIndex = _closureStack.Count - 1;
            RegexCaptureClosureFrame frame = _closureStack[frameIndex];
            _closureStack.RemoveAt(frameIndex);
            if (frame.Kind == RegexCaptureClosureFrameKind.RestoreSlot)
            {
                _workingSlots[frame.Slot] = frame.PreviousValue;
                continue;
            }

            stateIndex = frame.State;
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
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Alternative));
                    }

                    if (state.Next >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.CaptureStart:
                    int startSlot = checked(2 * state.CaptureIndex);
                    int endSlot = startSlot + 1;
                    _closureStack.Add(
                        RegexCaptureClosureFrame.Restore(startSlot, _workingSlots[startSlot]));
                    _workingSlots[startSlot] = position;
                    _closureStack.Add(
                        RegexCaptureClosureFrame.Restore(endSlot, _workingSlots[endSlot]));
                    _workingSlots[endSlot] = -1;
                    if (state.Next >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.CaptureEnd:
                    int slot = checked((2 * state.CaptureIndex) + 1);
                    _closureStack.Add(
                        RegexCaptureClosureFrame.Restore(slot, _workingSlots[slot]));
                    _workingSlots[slot] = position;
                    if (state.Next >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.Predicate:
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
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.Atom:
                case RegexNfaStateKind.Sparse:
                case RegexNfaStateKind.Accept:
                    AddState(stateIndex, _workingSlots);
                    break;
            }
        }
    }

    private void AddState(int stateIndex, ReadOnlySpan<int> slots)
    {
        int index = _states.Count;
        EnsureSlotCapacity(index + 1);
        slots.CopyTo(GetSlots(index));
        _states.Add(stateIndex);
    }

    private Span<int> GetSlots(int index)
    {
        return _stateSlots.AsSpan(checked(index * _slotCount), _slotCount);
    }

    private void EnsureSlotCapacity(int rowCount)
    {
        int requiredLength = checked(rowCount * _slotCount);
        if (requiredLength <= _stateSlots.Length)
        {
            return;
        }

        int doubledLength = checked(_stateSlots.Length * 2);
        Array.Resize(ref _stateSlots, Math.Max(requiredLength, doubledLength));
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
