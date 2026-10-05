using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies the initial recursive walker port surface.
/// </summary>
[TestClass]
public sealed class WalkTests
{
    /// <summary>
    /// Verifies recursive walking and maximum depth behavior.
    /// </summary>
    [TestMethod]
    public void WalkRecursesAndHonorsMaxDepth()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "a", "b", "c"));
        File.WriteAllText(Path.Join(root, "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "b", "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "b", "c", "foo"), string.Empty);

        Assert.AreSequenceEqual<string>(
            ["a", "a/b", "a/b/c", "a/b/c/foo", "a/b/foo", "a/foo", "foo"],
            Collect(root, new WalkBuilder(root)));

        Assert.IsEmpty(Collect(root, new WalkBuilder(root).MaxDepth(0)));
        Assert.AreSequenceEqual<string>(["a", "foo"], Collect(root, new WalkBuilder(root).MaxDepth(1)));
        Assert.AreSequenceEqual<string>(["a", "a/b", "a/foo", "foo"], Collect(root, new WalkBuilder(root).MaxDepth(2)));
    }

    /// <summary>
    /// Verifies parallel walking yields the same filtered paths as the serial walker.
    /// </summary>
    [TestMethod]
    public void ParallelWalkMatchesSerialWalk()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "a", "b"));
        Directory.CreateDirectory(Path.Join(root, "ignored"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "ignored/\n*.tmp\n");
        File.WriteAllText(Path.Join(root, "a", "one.txt"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "two.tmp"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "b", "three.txt"), string.Empty);
        File.WriteAllText(Path.Join(root, "ignored", "hidden.txt"), string.Empty);
        File.WriteAllText(Path.Join(root, "root.txt"), string.Empty);

        Assert.AreSequenceEqual(
            Collect(root, new WalkBuilder(root)),
            CollectParallel(root, new WalkBuilder(root).Threads(4)));
    }

    /// <summary>
    /// Verifies entries expose exact file metadata when native directory records permit a lazy status query.
    /// </summary>
    [TestMethod]
    public void WalkEntriesResolveLazyFileMetadata()
    {
        string root = CreateTempDirectory();
        string path = Path.Join(root, "file");
        File.WriteAllText(path, "hello");

        DirEntry entry = Assert.ContainsSingle(static entry => entry.Depth == 1, new WalkBuilder(root).Build());

        Assert.AreEqual(5, entry.Length);
        Assert.AreEqual(FileIdentity.FromPath(path, followLinks: false), entry.Identity);
    }

    /// <summary>
    /// Verifies parallel walking creates one visitor per configured worker.
    /// </summary>
    [TestMethod]
    public void ParallelWalkCreatesVisitorPerWorker()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "file"), string.Empty);
        int visitors = 0;

        new WalkBuilder(root).Threads(4).BuildParallel().Run(() =>
        {
            Interlocked.Increment(ref visitors);
            return static _ => WalkState.Continue;
        });

        Assert.AreEqual(4, visitors);
    }

    /// <summary>
    /// Verifies parallel walking completes every worker after exhausting the work queue.
    /// </summary>
    [TestMethod]
    public void ParallelWalkCompletesEveryWorkerAfterExhaustion()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "file"), string.Empty);
        int completions = 0;

        new WalkBuilder(root).Threads(4).BuildParallel().RunWithCompletion(() =>
        {
            Func<DirEntry, WalkState> visitor = static _ => WalkState.Continue;
            return (visitor, () => Interlocked.Increment(ref completions));
        });

        Assert.AreEqual(4, completions);
    }

    /// <summary>
    /// Verifies parallel walking completes every worker after an early quit.
    /// </summary>
    [TestMethod]
    public void ParallelWalkCompletesEveryWorkerAfterQuit()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "file"), string.Empty);
        int completions = 0;

        new WalkBuilder(root).Threads(4).BuildParallel().RunWithCompletion(() =>
        {
            Func<DirEntry, WalkState> visitor = static _ => WalkState.Quit;
            return (visitor, () => Interlocked.Increment(ref completions));
        });

        Assert.AreEqual(4, completions);
    }

    /// <summary>
    /// Verifies failures raised while completing a parallel worker propagate to the caller.
    /// </summary>
    [TestMethod]
    public void ParallelWalkPropagatesWorkerCompletionFailure()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "file"), string.Empty);

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new WalkBuilder(root).Threads(1).BuildParallel().RunWithCompletion(() =>
            {
                Func<DirEntry, WalkState> visitor = static _ => WalkState.Continue;
                return (visitor, static () => throw new InvalidOperationException("completion failed"));
            }));

        Assert.AreEqual("completion failed", error.Message);
    }

    /// <summary>
    /// Verifies parallel walking completes a worker after its visitor fails.
    /// </summary>
    [TestMethod]
    public void ParallelWalkCompletesWorkerAfterVisitorFailure()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "file"), string.Empty);
        int completions = 0;

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new WalkBuilder(root).Threads(1).BuildParallel().RunWithCompletion(() =>
            {
                Func<DirEntry, WalkState> visitor = static _ => throw new InvalidOperationException("visitor failed");
                return (visitor, () => Interlocked.Increment(ref completions));
            }));

        Assert.AreEqual("visitor failed", error.Message);
        Assert.AreEqual(1, completions);
    }

    /// <summary>
    /// Verifies a visitor-factory failure occurs before any parallel worker starts.
    /// </summary>
    [TestMethod]
    public void ParallelWalkCreatesAllVisitorsBeforeStartingWorkers()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "file"), string.Empty);
        int factories = 0;
        int visits = 0;

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new WalkBuilder(root).Threads(4).BuildParallel().RunWithCompletion(() =>
            {
                if (Interlocked.Increment(ref factories) == 2)
                {
                    throw new InvalidOperationException("factory failed");
                }

                Func<DirEntry, WalkState> visitor = _ =>
                {
                    Interlocked.Increment(ref visits);
                    return WalkState.Continue;
                };
                return (visitor, static () => { });
            }));

        Assert.AreEqual("factory failed", error.Message);
        Assert.AreEqual(2, factories);
        Assert.AreEqual(0, visits);
    }

    /// <summary>
    /// Verifies parallel visitors can skip descending into a visited directory.
    /// </summary>
    [TestMethod]
    public void ParallelWalkSkipPreventsDescendants()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "keep"));
        Directory.CreateDirectory(Path.Join(root, "skip"));
        File.WriteAllText(Path.Join(root, "keep", "file"), string.Empty);
        File.WriteAllText(Path.Join(root, "skip", "file"), string.Empty);

        List<string> paths = CollectParallel(
            root,
            new WalkBuilder(root).Threads(2),
            entry => entry.FileName == "skip" ? WalkState.Skip : WalkState.Continue);

        Assert.Contains("skip", paths);
        Assert.DoesNotContain("skip/file", paths);
        Assert.Contains("keep/file", paths);
    }

    /// <summary>
    /// Verifies minimum depth follows the upstream clamp behavior with maximum depth.
    /// </summary>
    [TestMethod]
    public void WalkHonorsMinDepth()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "a", "b", "c"));
        File.WriteAllText(Path.Join(root, "a", "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "b", "foo"), string.Empty);

        Assert.AreSequenceEqual<string>(
            ["a/b", "a/b/c", "a/b/foo", "a/foo"],
            Collect(root, new WalkBuilder(root).MinDepth(2)));
        Assert.AreSequenceEqual<string>(
            ["a/b", "a/foo"],
            Collect(root, new WalkBuilder(root).MinDepth(2).MaxDepth(1)));
    }

    /// <summary>
    /// Verifies hidden entries are skipped by default and can be included.
    /// </summary>
    [TestMethod]
    public void HiddenEntriesAreSkippedByDefault()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        File.WriteAllText(Path.Join(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Join(root, ".git", "config"), string.Empty);
        File.WriteAllText(Path.Join(root, "visible"), string.Empty);

        Assert.AreSequenceEqual<string>(["visible"], Collect(root, new WalkBuilder(root)));
        Assert.AreSequenceEqual<string>(
            [".git", ".git/config", ".hidden", "visible"],
            Collect(root, new WalkBuilder(root).Hidden(false)));
    }

    /// <summary>
    /// Verifies maximum file size skips only files, not directories.
    /// </summary>
    [TestMethod]
    public void MaxFileSizeSkipsLargeFiles()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "a"));
        WriteSizedFile(Path.Join(root, "small"), 4);
        WriteSizedFile(Path.Join(root, "large"), 8);
        WriteSizedFile(Path.Join(root, "a", "nested"), 8);

        Assert.AreSequenceEqual<string>(["a", "small"], Collect(root, new WalkBuilder(root).MaxFileSize(4)));
    }

    /// <summary>
    /// Verifies symbolic links are skipped by default and traversed when requested.
    /// </summary>
    [TestMethod]
    public void FollowLinksControlsSymlinkTraversal()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "a", "b"));
        File.WriteAllText(Path.Join(root, "a", "b", "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "real"), string.Empty);
        Assert.IsTrue(TryCreateDirectorySymlink(Path.Join(root, "a", "b"), Path.Join(root, "z")), "Required directory symlink could not be created.");
        Assert.IsTrue(TryCreateFileSymlink(Path.Join(root, "real"), Path.Join(root, "file-link")), "Required file symlink could not be created.");

        Assert.AreSequenceEqual<string>(["a", "a/b", "a/b/foo", "real"], Collect(root, new WalkBuilder(root)));
        Assert.AreSequenceEqual<string>(["a", "a/b", "a/b/foo", "file-link", "real", "z", "z/foo"], Collect(root, new WalkBuilder(root).FollowLinks(true)));
    }

    /// <summary>
    /// Verifies symbolic-link loops are not yielded or recursed when following links.
    /// </summary>
    [TestMethod]
    public void FollowLinksSkipsSymlinkLoops()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "a", "b"));
        Assert.IsTrue(TryCreateDirectorySymlink(Path.Join(root, "a"), Path.Join(root, "a", "b", "c")), "Required directory symlink could not be created.");

        string[] expected = ["a", "a/b"];
        Assert.AreSequenceEqual(expected, Collect(root, new WalkBuilder(root)));
        Assert.AreSequenceEqual(expected, Collect(root, new WalkBuilder(root).FollowLinks(true)));
        Assert.AreSequenceEqual(
            expected,
            CollectParallel(root, new WalkBuilder(root).FollowLinks(true).Threads(4)));
    }

    /// <summary>
    /// Verifies file identities resolve symbolic links to their final target.
    /// </summary>
    [TestMethod]
    public void FileIdentityFollowsSymlinkTarget()
    {
        string root = CreateTempDirectory();
        string target = Path.Join(root, "target");
        string link = Path.Join(root, "link");
        File.WriteAllText(target, string.Empty);
        Assert.IsTrue(TryCreateFileSymlink(target, link), "Required file symlink could not be created.");

        Assert.AreEqual(FileIdentity.FromPath(target), FileIdentity.FromPath(link));
        Assert.AreNotEqual(FileIdentity.FromPath(target), FileIdentity.FromPath(link, followLinks: false));
    }

    /// <summary>
    /// Verifies supported Unix architectures prefer their native <c>stat</c> layout without a second metadata probe.
    /// </summary>
    [TestMethod]
    public void UnixStatLayoutPrefersCurrentArchitecture()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.IsTrue(OperatingSystem.IsWindows());
        }
        else
        {
            UnixStatLayout layout = UnixStatLayout.ForCurrentPlatform[0];
            if (OperatingSystem.IsMacOS())
            {
                Assert.AreEqual(RuntimeInformation.ProcessArchitecture == Architecture.X64 ? 8 : 4, layout.ModeOffset);
            }
            else
            {
                Assert.IsTrue(OperatingSystem.IsLinux());
                int expectedModeOffset = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => 24,
                    Architecture.Arm64 => 16,
                    _ => 16,
                };
                Assert.AreEqual(expectedModeOffset, layout.ModeOffset);
            }
        }
    }

    /// <summary>
    /// Verifies native Unix status decoding preserves file, directory, and symbolic-link metadata.
    /// </summary>
    [TestMethod]
    public void NativeUnixStatusDecodesFilesystemEntries()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.IsTrue(OperatingSystem.IsWindows());
            Assert.IsFalse(NativeFileSystemMetadata.TryGetUnixStatus("unused", followLinks: false, out _));
        }
        else
        {
            string root = CreateTempDirectory();
            string directory = Path.Join(root, "directory");
            string file = Path.Join(root, "file");
            string link = Path.Join(root, "link");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(file, "needle"u8.ToArray());
            Assert.IsTrue(TryCreateFileSymlink(file, link), "Required file symlink could not be created.");

            Assert.IsTrue(NativeFileSystemMetadata.TryGetUnixStatus(directory, followLinks: false, out NativeUnixFileStatus directoryStatus));
            Assert.IsTrue(directoryStatus.IsDirectory);
            Assert.IsFalse(directoryStatus.IsSymbolicLink);
            Assert.IsNull(directoryStatus.Length);
            Assert.IsFalse(directoryStatus.Metadata.IsEmpty);

            Assert.IsTrue(NativeFileSystemMetadata.TryGetUnixStatus(file, followLinks: false, out NativeUnixFileStatus fileStatus));
            Assert.IsFalse(fileStatus.IsDirectory);
            Assert.IsFalse(fileStatus.IsSymbolicLink);
            Assert.AreEqual(6, fileStatus.Length);
            Assert.IsFalse(fileStatus.Metadata.IsEmpty);

            Assert.IsTrue(NativeFileSystemMetadata.TryGetUnixStatus(link, followLinks: false, out NativeUnixFileStatus linkStatus));
            Assert.IsFalse(linkStatus.IsDirectory);
            Assert.IsTrue(linkStatus.IsSymbolicLink);
            Assert.IsNull(linkStatus.Length);
            Assert.IsFalse(linkStatus.Metadata.IsEmpty);

            Assert.IsTrue(NativeFileSystemMetadata.TryGetUnixStatus(link, followLinks: true, out NativeUnixFileStatus targetStatus));
            Assert.IsFalse(targetStatus.IsDirectory);
            Assert.IsTrue(targetStatus.IsSymbolicLink);
            Assert.AreEqual(6, targetStatus.Length);
            Assert.AreEqual(fileStatus.Metadata, targetStatus.Metadata);
        }
    }

    /// <summary>
    /// Verifies raw Unix traversal stays byte-based after discovering an invalid UTF-8 directory.
    /// </summary>
    [TestMethod]
    public void WalkRecursesIntoRawUnixInvalidUtf8Directories()
    {
        if (!OperatingSystem.IsLinux())
        {
            AssertRawUnixInvalidUtf8DirectoryFixtureUnavailable();
        }
        else
        {
            string root = CreateTempDirectory();
            byte[] rootBytes = Encoding.UTF8.GetBytes(root);
            byte[] invalidDirectoryName = [(byte)'d', 0xff, (byte)'i', (byte)'r'];
            byte[] invalidDirectoryPath = JoinRawUnixPath(rootBytes, invalidDirectoryName);
            byte[] childPath = JoinRawUnixPath(invalidDirectoryPath, "needle.txt"u8);

            RawUnixDirectory.Create(invalidDirectoryPath);
            RawUnixFile.WriteAllBytes(childPath, "needle\n"u8);

            var rawPaths = new List<byte[]>();
            foreach (DirEntry entry in new WalkBuilder(root).Hidden(false).Build().Where(entry => entry.IsRawUnixPath))
            {
                rawPaths.Add(entry.UnixPathBytes.ToArray());
            }

            Assert.Contains(path => path.AsSpan().SequenceEqual(invalidDirectoryPath), rawPaths);
            Assert.Contains(path => path.AsSpan().SequenceEqual(childPath), rawPaths);
        }
    }

    /// <summary>
    /// Verifies a valid replacement character in a file name is yielded only once.
    /// </summary>
    [TestMethod]
    public void WalkDoesNotDuplicateValidUnicodeReplacementCharacterFileName()
    {
        string root = CreateTempDirectory();
        const string fileName = "valid\uFFFD.txt";
        File.WriteAllText(Path.Join(root, fileName), string.Empty);

        Assert.AreSequenceEqual<string>([fileName], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies same-file-system traversal still descends through the root file system.
    /// </summary>
    [TestMethod]
    public void SameFileSystemAllowsSameDeviceTraversal()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "a", "b"));
        File.WriteAllText(Path.Join(root, "a", "b", "file"), string.Empty);

        Assert.AreSequenceEqual<string>(["a", "a/b", "a/b/file"], Collect(root, new WalkBuilder(root).SameFileSystem(true)));
    }

    /// <summary>
    /// Verifies same-file-system traversal yields but does not descend into followed cross-device symlinks.
    /// </summary>
    [TestMethod]
    public void SameFileSystemSkipsDifferentDeviceSymlinkDescendants()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.IsTrue(OperatingSystem.IsWindows());
        }
        else
        {
            string root = CreateTempDirectory();
            string external = OperatingSystem.IsLinux() ? "/sys" : "/dev";
            Assert.IsTrue(Directory.Exists(external), "Required cross-device fixture does not exist: " + external);
            Assert.IsTrue(NativeFileSystemMetadata.TryGetDevice(root, out FileSystemDevice rootDevice), "Could not read root device.");
            Assert.IsTrue(NativeFileSystemMetadata.TryGetDevice(external, out FileSystemDevice externalDevice), "Could not read external device.");
            Assert.AreNotEqual(rootDevice, externalDevice);

            Directory.CreateDirectory(Path.Join(root, "same_file"));
            Assert.IsTrue(TryCreateDirectorySymlink(external, Path.Join(root, "same_file", "alink")), "Required cross-device directory symlink could not be created.");

            List<string> baseline = Collect(
                root,
                new WalkBuilder(root).Hidden(false).FollowLinks(true).MaxDepth(3));
            Assert.Contains(static path => path.StartsWith("same_file/alink/", StringComparison.Ordinal), baseline);

            List<string> paths = Collect(
                root,
                new WalkBuilder(root).Hidden(false).FollowLinks(true).SameFileSystem(true).MaxDepth(3));

            Assert.Contains("same_file/alink", paths);
            Assert.DoesNotContain(static path => path.StartsWith("same_file/alink/", StringComparison.Ordinal), paths);
        }
    }

    /// <summary>
    /// Verifies ignore files apply to descendants and support negation.
    /// </summary>
    [TestMethod]
    public void IgnoreFilesApplyToDescendantsWithNegation()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "logs"));
        File.WriteAllText(Path.Join(root, ".ignore"), "*.log\n!important.log\n");
        File.WriteAllText(Path.Join(root, "debug.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "important.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "logs", "debug.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "logs", "important.log"), string.Empty);

        Assert.AreSequenceEqual<string>(["important.log", "logs", "logs/important.log"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies ignore-file globs opt into literal-separator matching.
    /// </summary>
    [TestMethod]
    public void IgnoreFileWildcardsDoNotCrossSeparators()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "src", "nested"));
        File.WriteAllText(Path.Join(root, ".ignore"), "src/*.log\n");
        File.WriteAllText(Path.Join(root, "src", "debug.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "nested", "debug.log"), string.Empty);

        Assert.AreSequenceEqual<string>(["src", "src/nested", "src/nested/debug.log"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies ignore files treat unclosed character classes as literals.
    /// </summary>
    [TestMethod]
    public void IgnoreFilesAllowUnclosedCharacterClasses()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "[\n");
        File.WriteAllText(Path.Join(root, "["), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(["keep"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies directory-only ignore rules prune matching directories.
    /// </summary>
    [TestMethod]
    public void DirectoryOnlyIgnoreRulePrunesDirectories()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "target"));
        Directory.CreateDirectory(Path.Join(root, "src"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "target/\n");
        File.WriteAllText(Path.Join(root, "target", "artifact"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "target"), string.Empty);

        Assert.AreSequenceEqual<string>(["src", "src/target"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies a directory ignored by its parent is pruned before its own ignore files are parsed.
    /// </summary>
    [TestMethod]
    public void IgnoredDirectoryDoesNotLoadItsOwnIgnoreFiles()
    {
        string root = CreateTempDirectory();
        string ignored = Directory.CreateDirectory(Path.Join(root, "ignored")).FullName;
        File.WriteAllText(Path.Join(root, ".ignore"), "ignored/\n");
        File.WriteAllText(Path.Join(ignored, ".ignore"), "{a,b\n");
        File.WriteAllText(Path.Join(ignored, "unreachable"), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(["keep"], Collect(root, new WalkBuilder(root)));
        Assert.AreSequenceEqual<string>(["keep"], CollectParallel(root, new WalkBuilder(root).Threads(4)));
    }

    /// <summary>
    /// Verifies directories named like standard ignore files are traversed instead of parsed as files.
    /// </summary>
    /// <param name="ignoreFileName">The standard ignore file name to use for the directory.</param>
    [TestMethod]
    [DataRow(".ignore")]
    [DataRow(".gitignore")]
    public void IgnoreFileNameDirectoryDoesNotAbortTraversal(string ignoreFileName)
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ignoreFileName));
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        string[] expected = [ignoreFileName, "keep"];
        Assert.AreSequenceEqual(expected, Collect(root, new WalkBuilder(root).Hidden(false)));
        Assert.AreSequenceEqual(expected, CollectParallel(root, new WalkBuilder(root).Hidden(false).Threads(4)));
    }

    /// <summary>
    /// Verifies nested ignore files override ancestor rules.
    /// </summary>
    [TestMethod]
    public void NestedIgnoreFilesOverrideAncestorRules()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "src"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "*.tmp\n");
        File.WriteAllText(Path.Join(root, "src", ".ignore"), "!keep.tmp\n");
        File.WriteAllText(Path.Join(root, "drop.tmp"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "drop.tmp"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "keep.tmp"), string.Empty);

        Assert.AreSequenceEqual<string>(["src", "src/keep.tmp"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies parent ignore files apply when walking below the repository root.
    /// </summary>
    [TestMethod]
    public void ParentIgnoreFilesApplyToSubtreeRoots()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "src"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "foo\n");
        File.WriteAllText(Path.Join(root, "src", "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "bar"), string.Empty);
        string subtree = Path.Join(root, "src");

        Assert.AreSequenceEqual<string>(["bar"], Collect(subtree, new WalkBuilder(subtree)));
        Assert.AreSequenceEqual<string>(["bar", "foo"], Collect(subtree, new WalkBuilder(subtree).Parents(false)));
    }

    /// <summary>
    /// Verifies rooted patterns in parent ignore files remain anchored to their original directory.
    /// </summary>
    [TestMethod]
    public void ParentIgnoreRootedPatternsStayAnchoredToParent()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "src", "llvm"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "/llvm/\nfoo\n");
        File.WriteAllText(Path.Join(root, "src", "foo"), string.Empty);
        string subtree = Path.Join(root, "src");

        Assert.AreSequenceEqual<string>(["llvm"], Collect(subtree, new WalkBuilder(subtree)));
    }

    /// <summary>
    /// Verifies standard ignore sources can be toggled independently.
    /// </summary>
    [TestMethod]
    public void StandardIgnoreSourcesCanBeDisabled()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "git-only\n");
        File.WriteAllText(Path.Join(root, ".ignore"), "dot-only\n");
        File.WriteAllText(Path.Join(root, ".rgignore"), "rg-only\n");
        File.WriteAllText(Path.Join(root, ".scoutignore"), "scout-only\n");
        File.WriteAllText(Path.Join(root, "git-only"), string.Empty);
        File.WriteAllText(Path.Join(root, "dot-only"), string.Empty);
        File.WriteAllText(Path.Join(root, "rg-only"), string.Empty);
        File.WriteAllText(Path.Join(root, "scout-only"), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(["git-only", "keep"], Collect(root, new WalkBuilder(root).GitIgnore(false)));
        Assert.AreSequenceEqual<string>(["dot-only", "keep", "rg-only", "scout-only"], Collect(root, new WalkBuilder(root).Ignore(false)));
        Assert.AreSequenceEqual<string>(
            [".git", ".gitignore", ".ignore", ".rgignore", ".scoutignore", "dot-only", "git-only", "keep", "rg-only", "scout-only"],
            Collect(root, new WalkBuilder(root).StandardFilters(false)));
    }

    /// <summary>
    /// Verifies Scout-native ignore rules override matching .rgignore rules.
    /// </summary>
    [TestMethod]
    public void ScoutIgnoreOverridesRgIgnore()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        File.WriteAllText(Path.Join(root, ".rgignore"), "conflict\nrg-only\n");
        File.WriteAllText(Path.Join(root, ".scoutignore"), "!conflict\nscout-only\n");
        File.WriteAllText(Path.Join(root, "conflict"), string.Empty);
        File.WriteAllText(Path.Join(root, "rg-only"), string.Empty);
        File.WriteAllText(Path.Join(root, "scout-only"), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(["conflict", "keep"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies git-specific ignore files require a repository marker by default.
    /// </summary>
    [TestMethod]
    public void GitIgnoreRequiresRepositoryByDefault()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".gitignore"), "foo\n");
        File.WriteAllText(Path.Join(root, "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "bar"), string.Empty);

        Assert.AreSequenceEqual<string>(["bar", "foo"], Collect(root, new WalkBuilder(root)));
        Assert.AreSequenceEqual<string>(["bar"], Collect(root, new WalkBuilder(root).RequireGit(false)));
    }

    /// <summary>
    /// Verifies JJ repository markers enable gitignore semantics.
    /// </summary>
    [TestMethod]
    public void GitIgnoreAppliesInsideJjRepository()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".jj"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "foo\n");
        File.WriteAllText(Path.Join(root, "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "bar"), string.Empty);

        Assert.AreSequenceEqual<string>(["bar"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies git exclude files have lower precedence than .gitignore and .ignore files.
    /// </summary>
    [TestMethod]
    public void GitExcludeHasLowestGitPrecedence()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git", "info"));
        File.WriteAllText(Path.Join(root, ".git", "info", "exclude"), "foo\nbar\nbaz\n");
        File.WriteAllText(Path.Join(root, ".gitignore"), "!foo\n");
        File.WriteAllText(Path.Join(root, ".ignore"), "!bar\n");
        File.WriteAllText(Path.Join(root, "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "bar"), string.Empty);
        File.WriteAllText(Path.Join(root, "baz"), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(["bar", "foo", "keep"], Collect(root, new WalkBuilder(root)));
        Assert.AreSequenceEqual<string>(["bar", "baz", "foo", "keep"], Collect(root, new WalkBuilder(root).GitExclude(false)));
    }

    /// <summary>
    /// Verifies linked worktree <c>.git/info/exclude</c> discovery follows the upstream common-dir rules.
    /// </summary>
    [TestMethod]
    public void GitExcludeReadsLinkedWorktreeCommonDir()
    {
        string root = CreateTempDirectory();
        string gitDirectory = Path.Join(root, ".git");
        string worktreeGitDirectory = Path.Join(gitDirectory, "worktrees", "linked-worktree");
        string linkedWorktree = Path.Join(root, "linked-worktree");
        string commonDirectoryFile = Path.Join(worktreeGitDirectory, "commondir");
        Directory.CreateDirectory(Path.Join(gitDirectory, "info"));
        Directory.CreateDirectory(worktreeGitDirectory);
        Directory.CreateDirectory(linkedWorktree);
        File.WriteAllText(Path.Join(gitDirectory, "info", "exclude"), "ignore_me\n");
        File.WriteAllText(Path.Join(linkedWorktree, ".git"), "gitdir: " + worktreeGitDirectory);
        File.WriteAllText(Path.Join(linkedWorktree, "ignore_me"), string.Empty);
        File.WriteAllText(Path.Join(linkedWorktree, "keep"), string.Empty);

        File.WriteAllText(commonDirectoryFile, "../..");
        Assert.AreSequenceEqual<string>(["keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));

        File.WriteAllText(commonDirectoryFile, gitDirectory);
        Assert.AreSequenceEqual<string>(["keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));

        File.Delete(commonDirectoryFile);
        Assert.AreSequenceEqual<string>(["ignore_me", "keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));

        File.WriteAllText(Path.Join(linkedWorktree, ".git"), "garbage");
        Assert.AreSequenceEqual<string>(["ignore_me", "keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));
    }

    /// <summary>
    /// Verifies malformed linked-worktree <c>.git</c> files do not activate git exclude rules.
    /// </summary>
    [TestMethod]
    public void GitExcludeRequiresGitDirPrefixSpace()
    {
        string root = CreateTempDirectory();
        string gitDirectory = Path.Join(root, ".git");
        string linkedWorktree = Path.Join(root, "linked-worktree");
        Directory.CreateDirectory(Path.Join(gitDirectory, "info"));
        Directory.CreateDirectory(linkedWorktree);
        File.WriteAllText(Path.Join(gitDirectory, "info", "exclude"), "ignore_me\n");
        File.WriteAllText(Path.Join(linkedWorktree, ".git"), "gitdir:" + gitDirectory);
        File.WriteAllText(Path.Join(linkedWorktree, "ignore_me"), string.Empty);
        File.WriteAllText(Path.Join(linkedWorktree, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(["ignore_me", "keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));
    }

    /// <summary>
    /// Verifies global gitignore files apply below repository ignore sources and can be disabled.
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    public void GlobalGitIgnoreRulesApplyAfterRepositoryIgnoreSources()
    {
        string root = CreateTempDirectory();
        string home = CreateTempDirectory();
        string xdgConfigHome = Path.Join(home, "xdg");
        string globalOnly = "global-" + Guid.NewGuid().ToString("N") + ".log";
        string gitExcludeWhitelist = "whitelist-" + Guid.NewGuid().ToString("N") + ".log";
        Directory.CreateDirectory(Path.Join(root, ".git", "info"));
        Directory.CreateDirectory(Path.Join(xdgConfigHome, "git"));
        File.WriteAllText(Path.Join(xdgConfigHome, "git", "ignore"), globalOnly + "\n" + gitExcludeWhitelist + "\n");
        File.WriteAllText(Path.Join(root, ".git", "info", "exclude"), "!" + gitExcludeWhitelist + "\n");
        File.WriteAllText(Path.Join(root, globalOnly), string.Empty);
        File.WriteAllText(Path.Join(root, gitExcludeWhitelist), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        string? originalHome = Environment.GetEnvironmentVariable("HOME");
        string? originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", home);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);

            Assert.AreSequenceEqual<string>(["keep", gitExcludeWhitelist], Collect(root, new WalkBuilder(root)));
            Assert.AreSequenceEqual<string>([globalOnly, "keep", gitExcludeWhitelist], Collect(root, new WalkBuilder(root).GitGlobal(false)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", originalHome);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", originalXdgConfigHome);
        }
    }

    /// <summary>
    /// Verifies explicit ignore files apply below all directory-local ignore sources.
    /// </summary>
    [TestMethod]
    public void ExplicitIgnoreFilesHaveLowestPrecedence()
    {
        string root = CreateTempDirectory();
        string ignoreFile = Path.Join(root, ".not-an-ignore");
        Directory.CreateDirectory(Path.Join(root, "a"));
        File.WriteAllText(ignoreFile, "foo\nbar\n");
        File.WriteAllText(Path.Join(root, ".ignore"), "!bar\n");
        File.WriteAllText(Path.Join(root, "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "bar"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "bar"), string.Empty);

        Assert.AreSequenceEqual<string>(["a", "a/bar", "bar"], Collect(root, new WalkBuilder(root).AddIgnoreFile(ignoreFile)));
        Assert.AreSequenceEqual<string>(
            [".ignore", ".not-an-ignore", "a"],
            Collect(root, new WalkBuilder(root).StandardFilters(false).AddIgnoreFile(ignoreFile)));
    }

    /// <summary>
    /// Verifies ignore files are case-sensitive by default and can be matched ASCII case-insensitively.
    /// </summary>
    [TestMethod]
    public void IgnoreFilesCanMatchCaseInsensitively()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "*.html\n");
        File.WriteAllText(Path.Join(root, "lower.html"), string.Empty);
        File.WriteAllText(Path.Join(root, "upper.HTML"), string.Empty);
        File.WriteAllText(Path.Join(root, "short.htm"), string.Empty);
        File.WriteAllText(Path.Join(root, "wide.HTM"), string.Empty);

        Assert.AreSequenceEqual<string>(["short.htm", "upper.HTML", "wide.HTM"], Collect(root, new WalkBuilder(root)));
        Assert.AreSequenceEqual<string>(
            ["short.htm", "wide.HTM"],
            Collect(root, new WalkBuilder(root).IgnoreCaseInsensitive(true)));
    }

    /// <summary>
    /// Verifies explicit ignore files honor case-insensitive matching.
    /// </summary>
    [TestMethod]
    public void ExplicitIgnoreFilesCanMatchCaseInsensitively()
    {
        string root = CreateTempDirectory();
        string ignoreRoot = CreateTempDirectory();
        string ignoreFile = Path.Join(ignoreRoot, "ignore");
        File.WriteAllText(ignoreFile, "*.log\n");
        File.WriteAllText(Path.Join(root, "trace.LOG"), string.Empty);
        File.WriteAllText(Path.Join(root, "keep.txt"), string.Empty);

        Assert.AreSequenceEqual<string>(
            ["keep.txt"],
            Collect(root, new WalkBuilder(root).IgnoreCaseInsensitive(true).AddIgnoreFile(ignoreFile)));
    }

    /// <summary>
    /// Verifies custom ignore files override standard ignore files and preserve insertion precedence.
    /// </summary>
    [TestMethod]
    public void CustomIgnoreFilesOverrideStandardIgnoreFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "foo\n");
        File.WriteAllText(Path.Join(root, ".custom1"), "!foo\nbar\n");
        File.WriteAllText(Path.Join(root, ".custom2"), "!bar\n");
        File.WriteAllText(Path.Join(root, "foo"), string.Empty);
        File.WriteAllText(Path.Join(root, "bar"), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(
            ["bar", "foo", "keep"],
            Collect(root, new WalkBuilder(root).AddCustomIgnoreFileName(".custom1").AddCustomIgnoreFileName(".custom2")));
    }

    /// <summary>
    /// Verifies escaped leading comment and negation markers are literal patterns.
    /// </summary>
    [TestMethod]
    public void IgnoreFilesSupportEscapedLeadingMarkers()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "\\#literal\n\\!literal\n");
        File.WriteAllText(Path.Join(root, "#literal"), string.Empty);
        File.WriteAllText(Path.Join(root, "!literal"), string.Empty);
        File.WriteAllText(Path.Join(root, "other"), string.Empty);

        Assert.AreSequenceEqual<string>(["other"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies escaped trailing whitespace remains part of ignore patterns.
    /// </summary>
    [TestMethod]
    public void IgnoreFilesPreserveEscapedTrailingWhitespace()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "trimmed \nliteral\\ \n");
        File.WriteAllText(Path.Join(root, "trimmed"), string.Empty);
        File.WriteAllText(Path.Join(root, "literal "), string.Empty);
        File.WriteAllText(Path.Join(root, "literal"), string.Empty);

        Assert.AreSequenceEqual<string>(["literal"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies rooted and recursive gitignore patterns follow upstream matching semantics.
    /// </summary>
    [TestMethod]
    public void IgnoreFilesHonorRootedAndRecursivePatterns()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "child"));
        Directory.CreateDirectory(Path.Join(root, "a", "x", "y"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "/root.log\n**/any.log\na/**/b\n");
        File.WriteAllText(Path.Join(root, "root.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "any.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "child", "root.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "child", "any.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "b"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "x", "b"), string.Empty);
        File.WriteAllText(Path.Join(root, "a", "x", "y", "b"), string.Empty);
        File.WriteAllText(Path.Join(root, "keep"), string.Empty);

        Assert.AreSequenceEqual<string>(["a", "a/x", "a/x/y", "child", "child/root.log", "keep"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies whitelisted hidden entries bypass hidden filtering.
    /// </summary>
    [TestMethod]
    public void WhitelistedHiddenEntriesBypassHiddenFiltering()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "!.visible-hidden\n");
        File.WriteAllText(Path.Join(root, ".visible-hidden"), string.Empty);
        File.WriteAllText(Path.Join(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Join(root, "visible"), string.Empty);

        Assert.AreSequenceEqual<string>([".visible-hidden", "visible"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies whitelist overrides include matching files and ignore unmatched files.
    /// </summary>
    [TestMethod]
    public void OverrideWhitelistIgnoresUnmatchedFiles()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "src"));
        File.WriteAllText(Path.Join(root, "src", "main.rs"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "main.c"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("*.rs").Build();

        Assert.AreSequenceEqual<string>(["src", "src/main.rs"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies negated overrides exclude matching paths.
    /// </summary>
    [TestMethod]
    public void NegatedOverrideIgnoresMatchingPath()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "keep.rs"), string.Empty);
        File.WriteAllText(Path.Join(root, "drop.generated.rs"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("*.rs").Add("!*.generated.rs").Build();

        Assert.AreSequenceEqual<string>(["keep.rs"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies override whitelists take precedence over ignore-file rules and hidden filtering.
    /// </summary>
    [TestMethod]
    public void OverrideWhitelistBeatsIgnoreAndHiddenFilters()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Join(root, ".hidden.log"), string.Empty);
        File.WriteAllText(Path.Join(root, "visible.log"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("*.log").Build();

        Assert.AreSequenceEqual<string>([".hidden.log", "visible.log"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies directory-only overrides ignore matching directories.
    /// </summary>
    [TestMethod]
    public void DirectoryOnlyOverrideIgnoresMatchingDirectories()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "target"));
        Directory.CreateDirectory(Path.Join(root, "src"));
        File.WriteAllText(Path.Join(root, "target", "artifact"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "target"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("!target/").Build();

        Assert.AreSequenceEqual<string>(["src", "src/target"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies selected file types whitelist matching files and ignore other files.
    /// </summary>
    [TestMethod]
    public void SelectedFileTypeFiltersFiles()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, "src"));
        File.WriteAllText(Path.Join(root, "src", "main.rs"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "main.c"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("rust", "*.rs")
            .Select("rust")
            .Build();

        Assert.AreSequenceEqual<string>(["src", "src/main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies negated file types ignore matching files and leave other files alone.
    /// </summary>
    [TestMethod]
    public void NegatedFileTypeIgnoresMatchingFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "main.rs"), string.Empty);
        File.WriteAllText(Path.Join(root, "main.c"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("c", "*.c")
            .Negate("c")
            .Build();

        Assert.AreSequenceEqual<string>(["main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies include definitions inherit globs from existing file types.
    /// </summary>
    [TestMethod]
    public void FileTypeIncludeDefinitionsUseExistingGlobs()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "index.html"), string.Empty);
        File.WriteAllText(Path.Join(root, "lib.rs"), string.Empty);
        File.WriteAllText(Path.Join(root, "script.js"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .AddDefinition("html:*.html")
            .AddDefinition("rust:*.rs")
            .AddDefinition("combo:include:html,rust")
            .Select("combo")
            .Build();

        Assert.AreSequenceEqual<string>(["index.html", "lib.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies default file types include the pinned container type.
    /// </summary>
    [TestMethod]
    public void DefaultFileTypesIncludeContainer()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, "Dockerfile"), string.Empty);
        File.WriteAllText(Path.Join(root, "dev.Containerfile"), string.Empty);
        File.WriteAllText(Path.Join(root, "main.rs"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .AddDefaults()
            .Select("container")
            .Build();

        Assert.AreSequenceEqual<string>(["Dockerfile", "dev.Containerfile"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies file type whitelists bypass hidden filtering.
    /// </summary>
    [TestMethod]
    public void FileTypeWhitelistBypassesHiddenFiltering()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".main.rs"), string.Empty);
        File.WriteAllText(Path.Join(root, ".hidden.txt"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("rust", "*.rs")
            .Select("rust")
            .Build();

        Assert.AreSequenceEqual<string>([".main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies file type ignores still apply after ignore-file whitelists.
    /// </summary>
    [TestMethod]
    public void FileTypesCanIgnoreIgnoreFileWhitelists()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, ".ignore"), "*.tmp\n!keep.tmp\n");
        File.WriteAllText(Path.Join(root, "keep.tmp"), string.Empty);
        File.WriteAllText(Path.Join(root, "main.rs"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("rust", "*.rs")
            .Select("rust")
            .Build();

        Assert.AreSequenceEqual<string>(["main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies representative pinned upstream default file type patterns.
    /// </summary>
    /// <param name="fileType">The default file type to select.</param>
    /// <param name="fileName">The file name expected to match.</param>
    [TestMethod]
    [DataRow("bazel", "WORKSPACE.bazel")]
    [DataRow("dockercompose", "docker-compose.prod.yml")]
    [DataRow("license", "COPYING")]
    [DataRow("msbuild", "solution.slnf")]
    [DataRow("ruby", "Gemfile")]
    [DataRow("tf", "prod.tfvars.json")]
    [DataRow("typescript", "source.cts")]
    [DataRow("vim", ".vimrc")]
    [DataRow("zstd", "archive.zstd")]
    public void DefaultFileTypesIncludePinnedUpstreamPatterns(string fileType, string fileName)
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Join(root, fileName), string.Empty);
        File.WriteAllText(Path.Join(root, "unmatched.nope"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .AddDefaults()
            .Select(fileType)
            .Build();

        Assert.AreSequenceEqual<string>([fileName], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    private static List<string> Collect(string root, WalkBuilder builder)
    {
        return builder.SortByPath().Build()
            .Select(entry => Path.GetRelativePath(root, entry.FullPath))
            .Where(static relative => relative != ".")
            .Select(static relative => relative.Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(static relative => relative, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> CollectParallel(string root, WalkBuilder builder)
    {
        return CollectParallel(root, builder, static _ => WalkState.Continue);
    }

    private static List<string> CollectParallel(string root, WalkBuilder builder, Func<DirEntry, WalkState> visitor)
    {
        var paths = new ConcurrentBag<string>();
        builder.SortByPath().BuildParallel().Run(() => entry =>
        {
            string relative = Path.GetRelativePath(root, entry.FullPath);
            if (relative != ".")
            {
                paths.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
            }

            return visitor(entry);
        });

        var sorted = new List<string>(paths);
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Join(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteSizedFile(string path, int size)
    {
        using FileStream stream = File.Create(path);
        stream.SetLength(size);
    }

    private static void AssertRawUnixInvalidUtf8DirectoryFixtureUnavailable()
    {
        if (OperatingSystem.IsMacOS())
        {
            string root = CreateTempDirectory();
            try
            {
                byte[] rootBytes = Encoding.UTF8.GetBytes(root);
                byte[] invalidDirectoryName = [(byte)'d', 0xff, (byte)'i', (byte)'r'];
                byte[] invalidDirectoryPath = JoinRawUnixPath(rootBytes, invalidDirectoryName);

                Assert.ThrowsExactly<IOException>(() => RawUnixDirectory.Create(invalidDirectoryPath));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
        else
        {
            Assert.ThrowsExactly<PlatformNotSupportedException>(() => RawUnixDirectory.Create("unused"u8));
        }
    }

    private static byte[] JoinRawUnixPath(ReadOnlySpan<byte> parent, ReadOnlySpan<byte> name)
    {
        byte[] path = new byte[parent.Length + 1 + name.Length];
        parent.CopyTo(path);
        path[parent.Length] = (byte)'/';
        name.CopyTo(path.AsSpan(parent.Length + 1));
        return path;
    }

    private static bool TryCreateDirectorySymlink(string target, string link)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateFileSymlink(string target, string link)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
