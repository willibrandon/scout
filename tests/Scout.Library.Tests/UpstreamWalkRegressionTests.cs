using System.Collections.Concurrent;
using Scout.IO.Ignore;

namespace Scout;

/// <summary>
/// Verifies upstream traversal behavior through the exported library surface.
/// </summary>
public sealed class UpstreamWalkRegressionTests
{
    /// <summary>
    /// Verifies empty builders and path sequences perform no traversal or visitor creation.
    /// </summary>
    [Fact]
    public void EmptyWalksYieldNoEntries()
    {
        Assert.Empty(new WalkBuilder().Build());
        Assert.Empty(WalkBuilder.FromPaths([]).Build());
        Assert.Empty(Walk.FromPaths([]));
        Assert.Empty(new FileWalker().Enumerate(Array.Empty<string>()));
        int factories = 0;
        new WalkBuilder().BuildParallel().Run(() =>
        {
            factories++;
            return _ => WalkState.Continue;
        });
        Assert.Equal(0, factories);
    }

    /// <summary>
    /// Verifies factory input is copied once and builder mutation does not modify an existing walk.
    /// </summary>
    [Fact]
    public void PathFactoriesSnapshotTheirInput()
    {
        using var fixture = new DirectoryFixture();
        string first = fixture.Write("a/file");
        string second = fixture.Write("b/file");
        List<string> paths = [System.IO.Path.GetDirectoryName(first)!];
        WalkBuilder builder = WalkBuilder.FromPaths(paths).GitGlobal(false);
        Walk walk = builder.Build();
        paths.Clear();
        builder.Add(System.IO.Path.GetDirectoryName(second)!);
        Assert.Equal(first, Assert.Single(walk, static entry => entry.IsFile).FullPath);
        Assert.Equal(2, builder.Build().Count(static entry => entry.IsFile));
        Assert.Throws<ArgumentNullException>(() => WalkBuilder.FromPaths(null!));
        Assert.Throws<ArgumentException>(() => Walk.FromPaths([string.Empty]));
    }

    /// <summary>
    /// Verifies anchored parent ignore patterns are independent of root order and traversal concurrency.
    /// </summary>
    /// <param name="ignoreFile">The parent ignore-file name.</param>
    /// <param name="reverse">Whether sibling roots are supplied in reverse order.</param>
    [Theory]
    [InlineData(".gitignore", false)]
    [InlineData(".gitignore", true)]
    [InlineData(".rgignore", false)]
    [InlineData(".rgignore", true)]
    public void ParentIgnoreRulesRetainEachRootsBase(string ignoreFile, bool reverse)
    {
        using var fixture = new DirectoryFixture();
        Directory.CreateDirectory(System.IO.Path.Combine(fixture.Root, ".git"));
        fixture.Write(ignoreFile, "/a/ignored\n/b/ignored\n");
        fixture.Write("a/ignored");
        fixture.Write("b/ignored");
        string a = fixture.Write("a/keep");
        string b = fixture.Write("b/keep");
        string[] roots = [System.IO.Path.GetDirectoryName(a)!, System.IO.Path.GetDirectoryName(b)!];
        if (reverse)
        {
            Array.Reverse(roots);
        }

        WalkBuilder builder = WalkBuilder.FromPaths(roots).GitGlobal(false);
        Assert.Equal(new[] { a, b }, builder.Build().Where(static entry => entry.IsFile).Select(static entry => entry.FullPath).Order(StringComparer.Ordinal));
        var parallel = new ConcurrentBag<string>();
        builder.Threads(3).BuildParallel().Run(() => entry =>
        {
            if (entry.IsFile)
            {
                parallel.Add(entry.FullPath);
            }

            return WalkState.Continue;
        });
        Assert.Equal(new[] { a, b }, parallel.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies errors retain path and depth and each walk has independent quit state.
    /// </summary>
    [Fact]
    public void MissingRootsReportContextAndContinueOrQuit()
    {
        using var fixture = new DirectoryFixture();
        string existing = fixture.Write("present");
        string missing = System.IO.Path.Combine(fixture.Root, "missing");
        var errors = new List<WalkException>();
        WalkBuilder builder = WalkBuilder.FromPaths([missing, existing]).GitGlobal(false).ErrorHandler(error =>
        {
            errors.Add(error);
            return WalkState.Continue;
        });
        Assert.Equal(existing, Assert.Single(builder.Build()).FullPath);
        WalkException failure = Assert.Single(errors);
        Assert.Equal(0, failure.Depth);
        Assert.True(failure.Path.TryGetText(out string text));
        Assert.Equal(missing, text);
        Assert.IsAssignableFrom<IOException>(failure.InnerException);
        Walk quitWalk = builder.ErrorHandler(_ => WalkState.Quit).Build();
        Assert.Empty(quitWalk);
        Assert.Empty(quitWalk);
        Assert.Throws<WalkException>(() => Walk.FromPaths([missing]).ToArray());
    }

    /// <summary>
    /// Verifies metadata failures retain the file path and depth after the entry was yielded.
    /// </summary>
    [Fact]
    public void MetadataFailuresRemainContextual()
    {
        using var fixture = new DirectoryFixture();
        string path = fixture.Write("file");
        DirEntry entry = Assert.Single(new WalkBuilder(fixture.Root).GitGlobal(false).Build(), static entry => entry.IsFile);
        File.Delete(path);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            WalkException error = Assert.Throws<WalkException>(() => entry.Length);
            Assert.Equal(1, error.Depth);
            Assert.True(error.Path.TryGetText(out string text));
            Assert.Equal(path, text);
            Assert.Throws<WalkException>(() => entry.Identity);
        }
        else
        {
            Assert.NotNull(entry.Length);
        }
    }
}
