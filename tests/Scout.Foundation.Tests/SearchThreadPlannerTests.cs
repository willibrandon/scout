
namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible search thread planning.
/// </summary>
[TestClass]
public sealed class SearchThreadPlannerTests
{
    /// <summary>
    /// Verifies the default thread count is capped at twelve.
    /// </summary>
    [TestMethod]
    [DataRow(1, 1UL)]
    [DataRow(4, 4UL)]
    [DataRow(12, 12UL)]
    [DataRow(64, 12UL)]
    public void ResolveCapsDefaultThreadCount(int availableParallelism, ulong expectedThreads)
    {
        ulong threads = SearchThreadPlanner.Resolve(null, sortEnabled: false, isOneFile: false, availableParallelism);

        Assert.AreEqual(expectedThreads, threads);
    }

    /// <summary>
    /// Verifies explicit thread counts override the default cap.
    /// </summary>
    [TestMethod]
    public void ResolveUsesExplicitThreadCount()
    {
        ulong threads = SearchThreadPlanner.Resolve(64, sortEnabled: false, isOneFile: false, availableParallelism: 2);

        Assert.AreEqual(64UL, threads);
    }

    /// <summary>
    /// Verifies sorted output and single-file searches force serial execution.
    /// </summary>
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ResolveForcesSerialForSortedOrSingleFileSearches(bool sortEnabled, bool isOneFile)
    {
        ulong threads = SearchThreadPlanner.Resolve(64, sortEnabled, isOneFile, availableParallelism: 64);

        Assert.AreEqual(1UL, threads);
    }

    /// <summary>
    /// Verifies zero explicit threads behave like the upstream default request.
    /// </summary>
    [TestMethod]
    public void ResolveTreatsZeroAsDefault()
    {
        ulong threads = SearchThreadPlanner.Resolve(0, sortEnabled: false, isOneFile: false, availableParallelism: 64);

        Assert.AreEqual(12UL, threads);
    }

    /// <summary>
    /// Verifies default directory search fan-out applies the measured macOS ceiling.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(expected, threads);
    }

    /// <summary>
    /// Verifies macOS replacement searches use enough directory workers for capture rendering.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(expected, threads);
    }

    /// <summary>
    /// Verifies the measured macOS directory-search ceilings for ordinary and replacement output.
    /// </summary>
    /// <param name="replacement">Whether replacement rendering is active.</param>
    /// <param name="upstreamDefault">The platform-neutral planner result.</param>
    /// <param name="expectedThreads">The expected macOS worker count.</param>
    [TestMethod]
    [DataRow(false, 1, 1)]
    [DataRow(false, 2, 2)]
    [DataRow(false, 3, 3)]
    [DataRow(false, 4, 3)]
    [DataRow(false, 12, 3)]
    [DataRow(true, 1, 1)]
    [DataRow(true, 3, 3)]
    [DataRow(true, 6, 6)]
    [DataRow(true, 12, 6)]
    public void SearchWalkPlanningUsesMacOsDirectorySearchThreadMatrix(
        bool replacement,
        int upstreamDefault,
        int expectedThreads)
    {
        int threads = SearchWalkPlanning.GetMacOsDefaultSearchWalkThreadCount(
            upstreamDefault,
            replacement);

        Assert.AreEqual(expectedThreads, threads);
    }

    /// <summary>
    /// Verifies default macOS large-file search fan-out respects the ordered segment-worker bound.
    /// </summary>
    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    [DataRow(3, 3)]
    [DataRow(4, 3)]
    [DataRow(5, 3)]
    [DataRow(12, 3)]
    public void SearchWalkPlanningCapsMacOsDefaultLargeFileSearchThreads(int upstreamDefault, int expectedThreads)
    {
        int threads = SearchWalkPlanning.GetMacOsDefaultLargeFileSearchThreadCount(upstreamDefault);

        Assert.AreEqual(expectedThreads, threads);
    }

    /// <summary>
    /// Verifies an active file-level worker pool suppresses nested large-file segment workers.
    /// </summary>
    [TestMethod]
    public void SearchWalkPlanningDisablesLargeFileSegmentWorkersUnderOuterParallelism()
    {
        var lowArgs = new CliLowArgs();

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(
            lowArgs,
            allowSegmentParallelism: false);

        Assert.AreEqual(1, threads);
    }

    /// <summary>
    /// Verifies default serial large-file searches keep Scout's ordered internal segment workers.
    /// </summary>
    [TestMethod]
    public void SearchWalkPlanningKeepsDefaultSerialLargeFileSearchParallelism()
    {
        var lowArgs = new CliLowArgs();
        int upstreamDefault = Math.Min(Environment.ProcessorCount, 12);
        int expected = Math.Min(
            upstreamDefault,
            SearchWalkPlanning.MaximumLargeFileSegmentWorkerCount);

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(
            lowArgs,
            allowSegmentParallelism: true);

        Assert.AreEqual(expected, threads);
    }

    /// <summary>
    /// Verifies serial large-file searches bound an explicit segment-worker count.
    /// </summary>
    /// <param name="requestedThreads">The requested search-wide thread count.</param>
    /// <param name="expectedThreads">The expected ordered segment-worker count.</param>
    [TestMethod]
    [DataRow(1UL, 1)]
    [DataRow(2UL, 2)]
    [DataRow(3UL, 3)]
    [DataRow(4UL, 3)]
    [DataRow(12UL, 3)]
    public void SearchWalkPlanningBoundsExplicitSerialLargeFileSearchThreads(
        ulong requestedThreads,
        int expectedThreads)
    {
        var lowArgs = new CliLowArgs();
        lowArgs.SetThreads(requestedThreads);

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(
            lowArgs,
            allowSegmentParallelism: true);

        Assert.AreEqual(expectedThreads, threads);
    }

    /// <summary>
    /// Verifies explicit directory search thread counts are honored.
    /// </summary>
    [TestMethod]
    public void SearchWalkPlanningHonorsExplicitDirectorySearchThreads()
    {
        var lowArgs = new CliLowArgs();
        lowArgs.SetThreads(12);

        int threads = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);

        Assert.AreEqual(12, threads);
    }

    /// <summary>
    /// Verifies an explicit outer worker count does not create nested large-file workers.
    /// </summary>
    [TestMethod]
    public void SearchWalkPlanningSuppressesNestedWorkersWithExplicitOuterThreads()
    {
        var lowArgs = new CliLowArgs();
        lowArgs.SetThreads(12);

        int threads = SearchWalkPlanning.GetLargeFileSearchThreadCount(
            lowArgs,
            allowSegmentParallelism: false);

        Assert.AreEqual(1, threads);
    }

    /// <summary>
    /// Verifies invalid available parallelism is rejected.
    /// </summary>
    [TestMethod]
    public void ResolveRejectsInvalidAvailableParallelism()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SearchThreadPlanner.Resolve(null, sortEnabled: false, isOneFile: false, availableParallelism: 0));
    }
}
