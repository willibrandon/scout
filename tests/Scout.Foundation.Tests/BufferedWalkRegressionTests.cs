using System.Collections.Concurrent;

namespace Scout;

/// <summary>
/// Exercises buffered traversal failure ordering and descent decisions with real entries.
/// </summary>
public sealed class BufferedWalkRegressionTests
{
    private static readonly string[] OpeningFailureEvents = ["directory", "error"];
    /// <summary>
    /// Verifies a directory entry is delivered before an opening error is reported.
    /// </summary>
    [Fact]
    public void ParallelOpeningFailureFollowsDirectoryVisitor()
    {
        string root = Directory.CreateTempSubdirectory("scout-walk-").FullName;
        try
        {
            List<string> events = [];
            WalkBuilder builder = new WalkBuilder(root).GitGlobal(false).Threads(1).ErrorHandler(error =>
            {
                Assert.Equal(0, error.Depth);
                events.Add("error");
                return WalkState.Continue;
            });
            builder.DirectoryReader = entry => new WalkDirectoryReadResult([], [new WalkException(
                OsString.FromText(entry.FullPath), entry.Depth, new UnauthorizedAccessException())]);
            builder.BuildParallel().Run(() => entry =>
            {
                Assert.True(entry.IsDirectory);
                events.Add("directory");
                return WalkState.Continue;
            });
            Assert.Equal(OpeningFailureEvents, events);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies buffered valid entries survive enumeration errors and ignore discovery precedes the visitor.
    /// </summary>
    [Fact]
    public void ParallelPartialFailureRetainsEntriesAndDiscoveredIgnores()
    {
        string root = Directory.CreateTempSubdirectory("scout-walk-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, ".rgignore"), "ignored\n");
            File.WriteAllText(Path.Combine(root, "ignored"), "needle");
            File.WriteAllText(Path.Combine(root, "kept"), "needle");
            var errors = new ConcurrentBag<WalkException>();
            var visited = new ConcurrentBag<string>();
            Walk actualReader = new WalkBuilder(root).GitGlobal(false).Build();
            WalkBuilder builder = new WalkBuilder(root).GitGlobal(false).Threads(3).ErrorHandler(error =>
            {
                errors.Add(error);
                return WalkState.Continue;
            });
            builder.DirectoryReader = entry =>
            {
                WalkDirectoryReadResult read = actualReader.ReadChildren(entry);
                return new WalkDirectoryReadResult(read.Entries, [new WalkException(
                    OsString.FromText(entry.FullPath), entry.Depth + 1, new IOException("injected read error"))]);
            };
            builder.BuildParallel().Run(() => entry =>
            {
                if (entry.Depth == 0)
                {
                    File.Delete(Path.Combine(root, ".rgignore"));
                }

                if (entry.IsFile)
                {
                    visited.Add(entry.FileName);
                }

                return WalkState.Continue;
            });
            Assert.Equal("kept", Assert.Single(visited));
            Assert.Equal(1, Assert.Single(errors).Depth);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies depth limits avoid directory enumeration.
    /// </summary>
    [Fact]
    public void ParallelDepthLimitsAvoidDirectoryReads()
    {
        string root = Directory.CreateTempSubdirectory("scout-walk-").FullName;
        try
        {
            WalkBuilder builder = new WalkBuilder(root).GitGlobal(false).MaxDepth(0);
            builder.DirectoryReader = _ => throw new InvalidOperationException("unexpected directory read");
            builder.Threads(2).BuildParallel().Run(() => _ => WalkState.Continue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
