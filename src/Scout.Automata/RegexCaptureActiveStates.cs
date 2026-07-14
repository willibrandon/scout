namespace Scout;

/// <summary>
/// Stores an insertion-ordered capture-aware NFA frontier and reusable capture slots.
/// </summary>
/// <param name="stateCount">The number of states in the owning NFA.</param>
/// <param name="slotCount">The number of flattened capture slots per active thread.</param>
internal sealed class RegexCaptureActiveStates(int stateCount, int slotCount)
{
    private const int InitialCapacity = 16;

    private readonly int _slotCount = slotCount;
    private readonly List<CaptureThread> _threads = new(Math.Clamp(stateCount, 1, InitialCapacity));
    private readonly HashSet<long> _visited = new(Math.Clamp(stateCount, 1, InitialCapacity));
    private int[] _slots = new int[checked(Math.Clamp(stateCount, 1, InitialCapacity) * slotCount)];

    /// <summary>
    /// Gets the number of consuming or accepting threads in insertion order.
    /// </summary>
    public int Count => _threads.Count;

    /// <summary>
    /// Gets a consuming or accepting thread by insertion index.
    /// </summary>
    /// <param name="index">The insertion index.</param>
    /// <returns>The thread at the requested index.</returns>
    public CaptureThread this[int index] => _threads[index];

    /// <summary>
    /// Clears logical state while retaining all allocated buffers.
    /// </summary>
    public void Clear()
    {
        _threads.Clear();
        _visited.Clear();
    }

    /// <summary>
    /// Marks a state and position as visited by the current ordered frontier.
    /// </summary>
    /// <param name="state">The NFA state index.</param>
    /// <param name="position">The byte position.</param>
    /// <returns><see langword="true" /> when this is the first visit.</returns>
    public bool TryVisit(int state, int position)
    {
        return _visited.Add(CreateKey(state, position));
    }

    /// <summary>
    /// Adds a consuming or accepting thread whose closure state is already marked visited.
    /// </summary>
    /// <param name="state">The NFA state index.</param>
    /// <param name="position">The byte position.</param>
    /// <param name="slots">The flattened capture slots to copy into the thread row.</param>
    public void AddVisitedThread(int state, int position, ReadOnlySpan<int> slots)
    {
        int index = _threads.Count;
        EnsureSlotCapacity(index + 1);
        slots.CopyTo(GetSlots(index));
        _threads.Add(new CaptureThread(state, position));
    }

    /// <summary>
    /// Adds a consuming or accepting thread when its state and position are not already present.
    /// </summary>
    /// <param name="state">The NFA state index.</param>
    /// <param name="position">The byte position.</param>
    /// <param name="slots">The flattened capture slots to copy into the thread row.</param>
    /// <returns><see langword="true" /> when the thread was added.</returns>
    public bool TryAddThread(int state, int position, ReadOnlySpan<int> slots)
    {
        if (!TryVisit(state, position))
        {
            return false;
        }

        AddVisitedThread(state, position, slots);
        return true;
    }

    /// <summary>
    /// Gets the reusable capture slot row for a thread.
    /// </summary>
    /// <param name="index">The thread insertion index.</param>
    /// <returns>The flattened capture slots for the thread.</returns>
    public Span<int> GetSlots(int index)
    {
        return _slots.AsSpan(checked(index * _slotCount), _slotCount);
    }

    private static long CreateKey(int state, int position)
    {
        return ((long)position << 32) | (uint)state;
    }

    private void EnsureSlotCapacity(int rowCount)
    {
        int requiredLength = checked(rowCount * _slotCount);
        if (requiredLength <= _slots.Length)
        {
            return;
        }

        int doubledLength = checked(_slots.Length * 2);
        int newLength = Math.Max(requiredLength, doubledLength);
        Array.Resize(ref _slots, newLength);
    }
}
