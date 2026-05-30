using System;

namespace Scout;

/// <summary>
/// Resolves ripgrep-compatible search thread counts.
/// </summary>
public static class SearchThreadPlanner
{
    private const int MaxDefaultThreadCount = 12;

    /// <summary>
    /// Resolves the effective number of search threads.
    /// </summary>
    /// <param name="requestedThreads">The requested thread count, or <see langword="null" /> for the default.</param>
    /// <param name="sortEnabled">Whether sorted output is enabled.</param>
    /// <param name="isOneFile">Whether the search has a single file or standard-input subject.</param>
    /// <returns>The effective number of search threads.</returns>
    public static ulong Resolve(ulong? requestedThreads, bool sortEnabled, bool isOneFile)
    {
        return Resolve(requestedThreads, sortEnabled, isOneFile, Environment.ProcessorCount);
    }

    /// <summary>
    /// Resolves the effective number of search threads.
    /// </summary>
    /// <param name="requestedThreads">The requested thread count, or <see langword="null" /> for the default.</param>
    /// <param name="sortEnabled">Whether sorted output is enabled.</param>
    /// <param name="isOneFile">Whether the search has a single file or standard-input subject.</param>
    /// <param name="availableParallelism">The platform's available parallelism.</param>
    /// <returns>The effective number of search threads.</returns>
    public static ulong Resolve(
        ulong? requestedThreads,
        bool sortEnabled,
        bool isOneFile,
        int availableParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(availableParallelism, 1);

        if (sortEnabled || isOneFile)
        {
            return 1;
        }

        if (requestedThreads is ulong threads && threads != 0)
        {
            return threads;
        }

        return (ulong)Math.Min(availableParallelism, MaxDefaultThreadCount);
    }
}
