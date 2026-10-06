using System.Collections.Concurrent;
using Scout.IO.Ignore;

namespace Scout;

/// <summary>
/// Verifies upstream traversal behavior through the exported library surface.
/// </summary>
[TestClass]
public sealed class UpstreamWalkRegressionTests
{
    /// <summary>
    /// Verifies empty builders and path sequences perform no traversal or visitor creation.
    /// </summary>
    [TestMethod]
    public void EmptyWalksYieldNoEntries()
    {
        Assert.IsEmpty(new WalkBuilder().Build());
        Assert.IsEmpty(WalkBuilder.FromPaths([]).Build());
        Assert.IsEmpty(Walk.FromPaths([]));
        Assert.IsEmpty(new FileWalker().Enumerate(Array.Empty<string>()));
        int factories = 0;
        new WalkBuilder().BuildParallel().Run(() =>
        {
            factories++;
            return _ => WalkState.Continue;
        });
        Assert.AreEqual(0, factories);
    }

    /// <summary>
    /// Verifies factory input is copied once and builder mutation does not modify an existing walk.
    /// </summary>
    [TestMethod]
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
        Assert.AreEqual(first, Assert.ContainsSingle(static entry => entry.IsFile, walk).FullPath);
        Assert.AreEqual(2, builder.Build().Count(static entry => entry.IsFile));
        Assert.ThrowsExactly<ArgumentNullException>(() => WalkBuilder.FromPaths(null!));
        Assert.ThrowsExactly<ArgumentException>(() => Walk.FromPaths([string.Empty]));
    }

    /// <summary>
    /// Verifies anchored parent ignore patterns are independent of root order and traversal concurrency.
    /// </summary>
    /// <param name="ignoreFile">The parent ignore-file name.</param>
    /// <param name="reverse">Whether sibling roots are supplied in reverse order.</param>
    [TestMethod]
    [DataRow(".gitignore", false)]
    [DataRow(".gitignore", true)]
    [DataRow(".rgignore", false)]
    [DataRow(".rgignore", true)]
    public void ParentIgnoreRulesRetainEachRootsBase(string ignoreFile, bool reverse)
    {
        using var fixture = new DirectoryFixture();
        Directory.CreateDirectory(System.IO.Path.Join(fixture.Root, ".git"));
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
        Assert.AreSequenceEqual(new[] { a, b }, builder.Build().Where(static entry => entry.IsFile).Select(static entry => entry.FullPath).Order(StringComparer.Ordinal));
        var parallel = new ConcurrentBag<string>();
        builder.Threads(3).BuildParallel().Run(() => entry =>
        {
            if (entry.IsFile)
            {
                parallel.Add(entry.FullPath);
            }

            return WalkState.Continue;
        });
        Assert.AreSequenceEqual(new[] { a, b }, parallel.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies errors retain path and depth and each walk has independent quit state.
    /// </summary>
    [TestMethod]
    public void MissingRootsReportContextAndContinueOrQuit()
    {
        using var fixture = new DirectoryFixture();
        string existing = fixture.Write("present");
        string missing = System.IO.Path.Join(fixture.Root, "missing");
        var errors = new List<WalkException>();
        WalkBuilder builder = WalkBuilder.FromPaths([missing, existing]).GitGlobal(false).ErrorHandler(error =>
        {
            errors.Add(error);
            return WalkState.Continue;
        });
        Assert.AreEqual(existing, Assert.ContainsSingle(builder.Build()).FullPath);
        WalkException failure = Assert.ContainsSingle(errors);
        Assert.AreEqual(0, failure.Depth);
        Assert.IsTrue(failure.Path.TryGetText(out string text));
        Assert.AreEqual(missing, text);
        Assert.IsInstanceOfType<IOException>(failure.InnerException);
        Walk quitWalk = builder.ErrorHandler(_ => WalkState.Quit).Build();
        Assert.IsEmpty(quitWalk);
        Assert.IsEmpty(quitWalk);
        Assert.ThrowsExactly<WalkException>(() => Walk.FromPaths([missing]).ToArray());
    }

    /// <summary>
    /// Verifies metadata failures retain the file path and depth after the entry was yielded.
    /// </summary>
    [TestMethod]
    public void MetadataFailuresRemainContextual()
    {
        using var fixture = new DirectoryFixture();
        string path = fixture.Write("file");
        DirEntry entry = Assert.ContainsSingle(static entry => entry.IsFile, new WalkBuilder(fixture.Root).GitGlobal(false).Build());
        File.Delete(path);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            WalkException error = Assert.ThrowsExactly<WalkException>(() => entry.Length);
            Assert.AreEqual(1, error.Depth);
            Assert.IsTrue(error.Path.TryGetText(out string text));
            Assert.AreEqual(path, text);
            Assert.ThrowsExactly<WalkException>(() => entry.Identity);
        }
        else
        {
            Assert.IsNotNull(entry.Length);
        }
    }
}
