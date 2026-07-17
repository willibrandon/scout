
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
    /// Verifies default directory search fan-out applies the measured macOS ceiling.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningUsesDefaultDirectorySearchThreads()
    {
        var lowArgs = new CliLowArgs();
        int upstreamDefault = Math.Min(Environment.ProcessorCount, 12);
        int expected = OperatingSystem.IsMacOS()
            ? SearchWalkPlanning.GetMacOsDefaultSearchWalkThreadCount(
                upstreamDefault,
                replacement: false)
            : upstreamDefault;

        int threads = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);

        Assert.Equal(expected, threads);
    }

    /// <summary>
    /// Verifies macOS replacement searches use enough directory workers for capture rendering.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningUsesReplacementDirectorySearchThreads()
    {
        var lowArgs = new CliLowArgs();
        lowArgs.SetReplacement("$1"u8);
        int upstreamDefault = Math.Min(Environment.ProcessorCount, 12);
        int expected = OperatingSystem.IsMacOS()
            ? SearchWalkPlanning.GetMacOsDefaultSearchWalkThreadCount(
                upstreamDefault,
                replacement: true)
            : upstreamDefault;

        int threads = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);

        Assert.Equal(expected, threads);
    }

    /// <summary>
    /// Verifies the measured macOS directory-search ceilings for ordinary and replacement output.
    /// </summary>
    /// <param name="replacement">Whether replacement rendering is active.</param>
    /// <param name="upstreamDefault">The platform-neutral planner result.</param>
    /// <param name="expectedThreads">The expected macOS worker count.</param>
    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(false, 2, 2)]
    [InlineData(false, 3, 3)]
    [InlineData(false, 4, 3)]
    [InlineData(false, 12, 3)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 3, 3)]
    [InlineData(true, 6, 6)]
    [InlineData(true, 12, 6)]
    public void SearchWalkPlanningUsesMacOsDirectorySearchThreadMatrix(
        bool replacement,
        int upstreamDefault,
        int expectedThreads)
    {
        int threads = SearchWalkPlanning.GetMacOsDefaultSearchWalkThreadCount(
            upstreamDefault,
            replacement);

        Assert.Equal(expectedThreads, threads);
    }

    /// <summary>
    /// Verifies default macOS large-file search fan-out keeps enough workers for segmented regex throughput.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(12, 4)]
    public void SearchWalkPlanningCapsMacOsDefaultLargeFileSearchThreads(int upstreamDefault, int expectedThreads)
    {
        int threads = SearchWalkPlanning.GetMacOsDefaultLargeFileSearchThreadCount(upstreamDefault);

        Assert.Equal(expectedThreads, threads);
    }

    /// <summary>
    /// Verifies default large-file search planning does not inherit the constrained directory-walk cap.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningKeepsDefaultLargeFileSearchParallelismOnMacOs()
    {
        var lowArgs = new CliLowArgs();
        int upstreamDefault = Math.Min(Environment.ProcessorCount, 12);
        int expected = OperatingSystem.IsMacOS() ? SearchWalkPlanning.GetMacOsDefaultLargeFileSearchThreadCount(upstreamDefault) : upstreamDefault;

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(lowArgs);

        Assert.Equal(expected, threads);
    }

    /// <summary>
    /// Verifies default one-file large searches keep Scout's ordered internal segment workers.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningKeepsDefaultOneFileLargeSearchParallelism()
    {
        var lowArgs = new CliLowArgs();
        int upstreamDefault = Math.Min(Environment.ProcessorCount, 12);
        int expected = OperatingSystem.IsMacOS() ? SearchWalkPlanning.GetMacOsDefaultLargeFileSearchThreadCount(upstreamDefault) : upstreamDefault;

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(lowArgs, isOneFile: true);

        Assert.Equal(expected, threads);
    }

    /// <summary>
    /// Verifies explicit one-file large searches can use Scout's ordered internal segment workers.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningAllowsExplicitOneFileLargeSearchSegmentWorkers()
    {
        var lowArgs = new CliLowArgs();
        lowArgs.SetThreads(12);

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(lowArgs, isOneFile: true);

        Assert.Equal(12, threads);
    }

    /// <summary>
    /// Verifies explicit directory search thread counts are honored.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningHonorsExplicitDirectorySearchThreads()
    {
        var lowArgs = new CliLowArgs();
        lowArgs.SetThreads(12);

        int threads = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);

        Assert.Equal(12, threads);
    }

    /// <summary>
    /// Verifies explicit large-file search thread counts bypass platform default caps.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningHonorsExplicitLargeFileSearchThreads()
    {
        var lowArgs = new CliLowArgs();
        lowArgs.SetThreads(12);

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(lowArgs);

        Assert.Equal(12, threads);
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
