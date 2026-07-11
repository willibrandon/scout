namespace Scout;

/// <summary>
/// Incrementally builds a nearly minimal UTF-8 compile-plan DAG from ordered byte sequences.
/// </summary>
internal sealed class RegexUtf8ByteTriePlanCompiler
{
    private const int CompiledStateCacheCapacity = 10_000;
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly RegexUtf8ByteTriePlan?[] _compiled = new RegexUtf8ByteTriePlan[CompiledStateCacheCapacity];
    private readonly List<List<RegexUtf8ByteTriePlanTransition>> _uncompiledTransitions = [[]];
    private readonly List<RegexUtf8ByteRange?> _uncompiledLastTransitions = [null];
    private readonly RegexUtf8ByteTriePlan _accepting = new(0, accept: true, transitions: []);
    private int _nextId = 1;
    private bool _hasSequences;

    /// <summary>
    /// Adds the next lexicographically ordered, disjoint UTF-8 byte sequence.
    /// </summary>
    /// <param name="sequence">The sequence to add.</param>
    /// <exception cref="InvalidOperationException">
    /// The sequence is empty, duplicated, or not ordered after previously added sequences.
    /// </exception>
    public void Add(RegexUtf8ByteSequence sequence)
    {
        if (sequence.Length == 0)
        {
            throw new InvalidOperationException("A UTF-8 compile-plan sequence cannot be empty.");
        }

        int prefixLength = 0;
        int comparableLength = Math.Min(sequence.Length, _uncompiledLastTransitions.Count);
        while (prefixLength < comparableLength &&
            _uncompiledLastTransitions[prefixLength] is RegexUtf8ByteRange previous &&
            previous.Equals(sequence[prefixLength]))
        {
            prefixLength++;
        }

        if (prefixLength == sequence.Length)
        {
            throw new InvalidOperationException("UTF-8 compile-plan sequences must be unique and ordered.");
        }

        CompileFrom(prefixLength);
        AddSuffix(sequence, prefixLength);
        _hasSequences = true;
    }

    /// <summary>
    /// Completes the incremental construction and returns its immutable compile plan.
    /// </summary>
    /// <returns>The root of the minimized compile-plan DAG.</returns>
    /// <exception cref="InvalidOperationException">No sequences were added.</exception>
    public RegexUtf8ByteTriePlan Finish()
    {
        if (!_hasSequences)
        {
            throw new InvalidOperationException("A UTF-8 compile plan requires at least one sequence.");
        }

        CompileFrom(0);
        if (_uncompiledTransitions.Count != 1 || _uncompiledLastTransitions[0].HasValue)
        {
            throw new InvalidOperationException("The UTF-8 compile-plan root was not fully frozen.");
        }

        return Intern(_uncompiledTransitions[0].ToArray());
    }

    private void CompileFrom(int from)
    {
        RegexUtf8ByteTriePlan next = _accepting;
        while (from + 1 < _uncompiledTransitions.Count)
        {
            next = Intern(PopAndFreeze(next));
        }

        FreezeLastTransition(_uncompiledTransitions.Count - 1, next);
    }

    private void AddSuffix(RegexUtf8ByteSequence sequence, int start)
    {
        int last = _uncompiledLastTransitions.Count - 1;
        if (_uncompiledLastTransitions[last].HasValue)
        {
            throw new InvalidOperationException("The shared UTF-8 sequence prefix was not frozen.");
        }

        _uncompiledLastTransitions[last] = sequence[start];
        for (int index = start + 1; index < sequence.Length; index++)
        {
            _uncompiledTransitions.Add([]);
            _uncompiledLastTransitions.Add(sequence[index]);
        }
    }

    private RegexUtf8ByteTriePlanTransition[] PopAndFreeze(RegexUtf8ByteTriePlan target)
    {
        int index = _uncompiledTransitions.Count - 1;
        FreezeLastTransition(index, target);
        RegexUtf8ByteTriePlanTransition[] transitions = _uncompiledTransitions[index].ToArray();
        _uncompiledTransitions.RemoveAt(index);
        _uncompiledLastTransitions.RemoveAt(index);
        return transitions;
    }

    private void FreezeLastTransition(int index, RegexUtf8ByteTriePlan target)
    {
        if (_uncompiledLastTransitions[index] is not RegexUtf8ByteRange range)
        {
            return;
        }

        List<RegexUtf8ByteTriePlanTransition> transitions = _uncompiledTransitions[index];
        if (transitions.Count > 0 && transitions[^1].End >= range.Start)
        {
            throw new InvalidOperationException("UTF-8 compile-plan transitions must be ordered and non-overlapping.");
        }

        transitions.Add(new RegexUtf8ByteTriePlanTransition(range.Start, range.End, target));
        _uncompiledLastTransitions[index] = null;
    }

    private RegexUtf8ByteTriePlan Intern(RegexUtf8ByteTriePlanTransition[] transitions)
    {
        int slot = ComputeCacheSlot(transitions);
        RegexUtf8ByteTriePlan? existing = _compiled[slot];
        if (existing is not null && existing.HasTransitions(transitions))
        {
            return existing;
        }

        var created = new RegexUtf8ByteTriePlan(_nextId++, accept: false, transitions);
        _compiled[slot] = created;
        return created;
    }

    private static int ComputeCacheSlot(ReadOnlySpan<RegexUtf8ByteTriePlanTransition> transitions)
    {
        ulong hash = FnvOffsetBasis;
        for (int index = 0; index < transitions.Length; index++)
        {
            RegexUtf8ByteTriePlanTransition transition = transitions[index];
            hash = unchecked((hash ^ transition.Start) * FnvPrime);
            hash = unchecked((hash ^ transition.End) * FnvPrime);
            hash = unchecked((hash ^ (uint)transition.Target.Id) * FnvPrime);
        }

        return (int)(hash % CompiledStateCacheCapacity);
    }
}
