using System.Diagnostics;

namespace Scout;

/// <summary>
/// Measures the bytes an action allocates on the calling thread without counting garbage collector bookkeeping.
/// </summary>
/// <remarks>
/// <see cref="GC.GetAllocatedBytesForCurrentThread" /> subtracts only the unused tail of the thread's current
/// allocation context. When a collection retires that context inside a measured window, its unused tail stays
/// counted, so one collection can add up to one context to the reading. The meter runs the action inside a
/// no-GC region, which holds every thread's collections off while the process stays within the region's budget,
/// so the first window is exact. When the runtime refuses the region or another thread exhausts its budget, the
/// meter repeats the window until one passes without a collection, bounded by a time limit so a slow action on a
/// loaded machine cannot stall its test.
/// </remarks>
internal static class AllocationMeter
{
    private const long QuietRegionBudget = 32L * 1024 * 1024;
    private const int MaximumAttempts = 16;
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs an action and returns the bytes it allocated on the current thread.
    /// </summary>
    /// <param name="action">The action to measure; it runs again when a collection overlapped the window, so it must reset any state it accumulates.</param>
    /// <returns>The allocated bytes of the first window no collection overlapped, or the smallest reading when every attempt overlapped one.</returns>
    internal static long Measure(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        long smallest = long.MaxValue;
        long started = Stopwatch.GetTimestamp();
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            bool quiet = TryEnterQuietRegion();
            int collections = GC.CollectionCount(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            bool overlapped = GC.CollectionCount(0) != collections;
            if (quiet && !TryLeaveQuietRegion())
            {
                overlapped = true;
            }

            if (!overlapped)
            {
                return allocated;
            }

            smallest = Math.Min(smallest, allocated);
            if (Stopwatch.GetElapsedTime(started) >= MaximumDuration)
            {
                break;
            }
        }

        return smallest;
    }

    private static bool TryEnterQuietRegion()
    {
        try
        {
            return GC.TryStartNoGCRegion(QuietRegionBudget);
        }
        catch (InvalidOperationException)
        {
            // Another thread already holds the process-wide region; its budget shields this window as well.
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            // The runtime cannot reserve the budget on this configuration.
            return false;
        }
    }

    private static bool TryLeaveQuietRegion()
    {
        try
        {
            GC.EndNoGCRegion();
            return true;
        }
        catch (InvalidOperationException)
        {
            // The runtime already left the region because allocations exceeded its budget or a collection was induced.
            return false;
        }
    }
}
