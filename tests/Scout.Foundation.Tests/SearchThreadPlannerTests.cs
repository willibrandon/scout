
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
    /// Verifies default macOS directory search fan-out keeps enough workers for hosted file I/O.
    /// </summary>
    [Fact]
    public void SearchWalkPlanningUsesMacOsDefaultDirectorySearchThreads()
    {
        var lowArgs = new CliLowArgs();
        int upstreamDefault = Math.Min(Environment.ProcessorCount, 12);
        int expected = OperatingSystem.IsMacOS() ? SearchWalkPlanning.GetMacOsDefaultSearchWalkThreadCount(upstreamDefault) : upstreamDefault;

        int threads = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);

        Assert.Equal(expected, threads);
    }

    /// <summary>
    /// Verifies default macOS directory search fan-out uses the measured hosted-runner target.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 4)]
    [InlineData(12, 4)]
    public void SearchWalkPlanningUsesMacOsDefaultDirectorySearchThreadMatrix(int upstreamDefault, int expectedThreads)
    {
        int threads = SearchWalkPlanning.GetMacOsDefaultSearchWalkThreadCount(upstreamDefault);

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
    /// Verifies explicit directory search thread counts bypass platform default caps.
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
