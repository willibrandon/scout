namespace Scout;

/// <summary>
/// Measures the bytes an action allocates on the calling thread without counting garbage collector bookkeeping.
/// </summary>
/// <remarks>
/// <see cref="GC.GetAllocatedBytesForCurrentThread" /> subtracts only the unused tail of the thread's current
/// allocation context. When a collection retires that context inside a measured window, its unused tail stays
/// counted, so one collection can add up to one context to the reading. Hosted runners work with much smaller
/// gen0 budgets than developer machines and share them across parallel test classes, which is where that shows
/// up. A collection only ever adds bytes, so the first window no collection overlapped is the exact figure.
/// </remarks>
internal static class AllocationMeter
{
    private const int MaximumAttempts = 16;

    /// <summary>
    /// Runs an action and returns the bytes it allocated on the current thread.
    /// </summary>
    /// <param name="action">The action to measure; it runs again when a collection overlapped the window, so it must reset any state it accumulates.</param>
    /// <returns>The allocated bytes of the first window no collection overlapped, or the smallest reading when every attempt overlapped one.</returns>
    internal static long Measure(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        long smallest = long.MaxValue;
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            int collections = GC.CollectionCount(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (GC.CollectionCount(0) == collections)
            {
                return allocated;
            }

            smallest = Math.Min(smallest, allocated);
        }

        return smallest;
    }
}
