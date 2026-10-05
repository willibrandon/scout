using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;

namespace Scout;

/// <summary>
/// Exercises buffered traversal failure ordering and descent decisions with real entries.
/// </summary>
[TestClass]
public sealed class BufferedWalkRegressionTests
{
    private static readonly string[] OpeningFailureEvents = ["directory", "error"];
    /// <summary>
    /// Verifies a directory entry is delivered before an opening error is reported.
    /// </summary>
    [TestMethod]
    public void ParallelOpeningFailureFollowsDirectoryVisitor()
    {
        string root = Directory.CreateTempSubdirectory("scout-walk-").FullName;
        try
        {
            List<string> events = [];
            WalkBuilder builder = new WalkBuilder(root).GitGlobal(false).Threads(1).ErrorHandler(error =>
            {
                Assert.AreEqual(0, error.Depth);
                events.Add("error");
                return WalkState.Continue;
            });
            builder.DirectoryReader = entry => new WalkDirectoryReadResult([], [new WalkException(
                OsString.FromText(entry.FullPath), entry.Depth, new UnauthorizedAccessException())]);
            builder.BuildParallel().Run(() => entry =>
            {
                Assert.IsTrue(entry.IsDirectory);
                events.Add("directory");
                return WalkState.Continue;
            });
            Assert.AreSequenceEqual(OpeningFailureEvents, events);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies buffered valid entries survive enumeration errors and ignore discovery precedes the visitor.
    /// </summary>
    [TestMethod]
    public void ParallelPartialFailureRetainsEntriesAndDiscoveredIgnores()
    {
        string root = Directory.CreateTempSubdirectory("scout-walk-").FullName;
        try
        {
            File.WriteAllText(Path.Join(root, ".rgignore"), "ignored\n");
            File.WriteAllText(Path.Join(root, "ignored"), "needle");
            File.WriteAllText(Path.Join(root, "kept"), "needle");
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
                    File.WriteAllText(Path.Join(root, ".rgignore"), "kept\n");
                }

                if (entry.IsFile)
                {
                    visited.Add(entry.FileName);
                }

                return WalkState.Continue;
            });
            Assert.AreEqual("kept", Assert.ContainsSingle(visited));
            Assert.AreEqual(1, Assert.ContainsSingle(errors).Depth);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies depth limits avoid directory enumeration.
    /// </summary>
    [TestMethod]
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

    /// <summary>
    /// Verifies a disappearing byte-path entry retains the real native status error.
    /// </summary>
    /// <param name="parallel">Whether to traverse with parallel workers.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DisappearingBytePathsRetainNativeErrorCause(bool parallel)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            string root = Directory.CreateTempSubdirectory("scout-walk-").FullName;
            try
            {
                string path = Path.Join(root, "vanishes");
                File.WriteAllText(path, "needle");
                byte[] bytes = Encoding.UTF8.GetBytes(path);
                Walk actualReader = new WalkBuilder(root).GitGlobal(false).Build();
                var errors = new ConcurrentBag<WalkException>();
                WalkBuilder builder = new WalkBuilder(root).GitGlobal(false).FollowLinks(true).Threads(3).ErrorHandler(error =>
                {
                    errors.Add(error);
                    return WalkState.Continue;
                });
                builder.DirectoryReader = entry =>
                {
                    WalkDirectoryReadResult read = actualReader.ReadChildren(entry);
                    Assert.AreEqual(path, Assert.ContainsSingle(read.Entries).TextPath);
                    File.Delete(path);
                    return new WalkDirectoryReadResult(
                        [WalkPath.FromRawUnix(bytes, "vanishes"u8, RawUnixDirectoryEntryType.RegularFile)], read.Errors);
                };

                if (parallel)
                {
                    builder.BuildParallel().Run(() => entry =>
                    {
                        Assert.IsTrue(entry.IsDirectory);
                        return WalkState.Continue;
                    });
                }
                else
                {
                    Assert.IsTrue(Assert.ContainsSingle(builder.Build()).IsDirectory);
                }

                WalkException failure = Assert.ContainsSingle(errors);
                Assert.AreEqual(1, failure.Depth);
                Assert.AreSequenceEqual(bytes, failure.Path.AsUnixBytes().ToArray());
                IOException ioError = Assert.IsExactInstanceOfType<IOException>(failure.InnerException);
                Assert.AreEqual(2, Assert.IsExactInstanceOfType<Win32Exception>(ioError.InnerException).NativeErrorCode);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
        else
        {
            Assert.IsFalse(NativeFileSystemMetadata.TryGetRawUnixStatus("unused"u8, followLinks: true, out _));
        }
    }

    /// <summary>
    /// Verifies concurrent workers visit every buffered entry once and all complete across multiple roots.
    /// </summary>
    [TestMethod]
    public void ParallelBufferedRootsVisitEveryEntryExactlyOnce()
    {
        string root = Directory.CreateTempSubdirectory("scout-walk-").FullName;
        try
        {
            var expected = new HashSet<string>(StringComparer.Ordinal);
            string[] roots = [Path.Join(root, "a"), Path.Join(root, "b")];
            foreach (string path in roots)
            {
                Directory.CreateDirectory(path);
                expected.Add(path);
                File.WriteAllText(Path.Join(path, ".rgignore"), "ignored\n");
                for (int directoryIndex = 0; directoryIndex < 32; directoryIndex++)
                {
                    string directory = Path.Join(path, "d" + directoryIndex);
                    Directory.CreateDirectory(directory);
                    expected.Add(directory);
                    File.WriteAllText(Path.Join(directory, "ignored"), "needle");
                    for (int fileIndex = 0; fileIndex < 16; fileIndex++)
                    {
                        string file = Path.Join(directory, "f" + fileIndex);
                        File.WriteAllText(file, "needle");
                        expected.Add(file);
                    }
                }
            }

            var visited = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            int completed = 0;
            WalkBuilder.FromPaths(roots).GitGlobal(false).Threads(8).BuildParallel().RunWithCompletion(() =>
                (entry =>
                {
                    Assert.IsTrue(visited.TryAdd(entry.FullPath, 0), "Duplicate entry: " + entry.FullPath);
                    return WalkState.Continue;
                }, () => Interlocked.Increment(ref completed)));
            Assert.AreEqual(8, completed);
            Assert.IsTrue(expected.SetEquals(visited.Keys));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
