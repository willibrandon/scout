using System;

namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible search thread planning.
/// </summary>
public sealed class SearchThreadPlannerTests
{
    /// <summary>
    /// Verifies the default thread count is capped at twelve.
    /// </summary>
    [Theory]
    [InlineData(1, 1UL)]
    [InlineData(4, 4UL)]
    [InlineData(12, 12UL)]
    [InlineData(64, 12UL)]
    public void ResolveCapsDefaultThreadCount(int availableParallelism, ulong expectedThreads)
    {
        ulong threads = SearchThreadPlanner.Resolve(null, sortEnabled: false, isOneFile: false, availableParallelism);

        Assert.Equal(expectedThreads, threads);
    }

    /// <summary>
    /// Verifies explicit thread counts override the default cap.
    /// </summary>
    [Fact]
    public void ResolveUsesExplicitThreadCount()
    {
        ulong threads = SearchThreadPlanner.Resolve(64, sortEnabled: false, isOneFile: false, availableParallelism: 2);

        Assert.Equal(64UL, threads);
    }

    /// <summary>
    /// Verifies sorted output and single-file searches force serial execution.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ResolveForcesSerialForSortedOrSingleFileSearches(bool sortEnabled, bool isOneFile)
    {
        ulong threads = SearchThreadPlanner.Resolve(64, sortEnabled, isOneFile, availableParallelism: 64);

        Assert.Equal(1UL, threads);
    }

    /// <summary>
    /// Verifies zero explicit threads behave like the upstream default request.
    /// </summary>
    [Fact]
    public void ResolveTreatsZeroAsDefault()
    {
        ulong threads = SearchThreadPlanner.Resolve(0, sortEnabled: false, isOneFile: false, availableParallelism: 64);

        Assert.Equal(12UL, threads);
    }

    /// <summary>
    /// Verifies invalid available parallelism is rejected.
    /// </summary>
    [Fact]
    public void ResolveRejectsInvalidAvailableParallelism()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SearchThreadPlanner.Resolve(null, sortEnabled: false, isOneFile: false, availableParallelism: 0));
    }
}
