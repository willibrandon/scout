using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Scout;

/// <summary>
/// Verifies the initial recursive walker port surface.
/// </summary>
public sealed class WalkTests
{
    /// <summary>
    /// Verifies recursive walking and maximum depth behavior.
    /// </summary>
    [Fact]
    public void WalkRecursesAndHonorsMaxDepth()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "a", "b", "c"));
        File.WriteAllText(Path.Combine(root, "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "b", "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "b", "c", "foo"), string.Empty);

        Assert.Equal(
            ["a", "a/b", "a/b/c", "a/b/c/foo", "a/b/foo", "a/foo", "foo"],
            Collect(root, new WalkBuilder(root)));

        Assert.Empty(Collect(root, new WalkBuilder(root).MaxDepth(0)));
        Assert.Equal(["a", "foo"], Collect(root, new WalkBuilder(root).MaxDepth(1)));
        Assert.Equal(["a", "a/b", "a/foo", "foo"], Collect(root, new WalkBuilder(root).MaxDepth(2)));
    }

    /// <summary>
    /// Verifies parallel walking yields the same filtered paths as the serial walker.
    /// </summary>
    [Fact]
    public void ParallelWalkMatchesSerialWalk()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        Directory.CreateDirectory(Path.Combine(root, "ignored"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored/\n*.tmp\n");
        File.WriteAllText(Path.Combine(root, "a", "one.txt"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "two.tmp"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "b", "three.txt"), string.Empty);
        File.WriteAllText(Path.Combine(root, "ignored", "hidden.txt"), string.Empty);
        File.WriteAllText(Path.Combine(root, "root.txt"), string.Empty);

        Assert.Equal(
            Collect(root, new WalkBuilder(root)),
            CollectParallel(root, new WalkBuilder(root).Threads(4)));
    }

    /// <summary>
    /// Verifies parallel walking creates one visitor per configured worker.
    /// </summary>
    [Fact]
    public void ParallelWalkCreatesVisitorPerWorker()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "file"), string.Empty);
        int visitors = 0;

        new WalkBuilder(root).Threads(4).BuildParallel().Run(() =>
        {
            Interlocked.Increment(ref visitors);
            return static _ => WalkState.Continue;
        });

        Assert.Equal(4, visitors);
    }

    /// <summary>
    /// Verifies parallel visitors can skip descending into a visited directory.
    /// </summary>
    [Fact]
    public void ParallelWalkSkipPreventsDescendants()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "keep"));
        Directory.CreateDirectory(Path.Combine(root, "skip"));
        File.WriteAllText(Path.Combine(root, "keep", "file"), string.Empty);
        File.WriteAllText(Path.Combine(root, "skip", "file"), string.Empty);

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
    [Fact]
    public void WalkHonorsMinDepth()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "a", "b", "c"));
        File.WriteAllText(Path.Combine(root, "a", "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "b", "foo"), string.Empty);

        Assert.Equal(
            ["a/b", "a/b/c", "a/b/foo", "a/foo"],
            Collect(root, new WalkBuilder(root).MinDepth(2)));
        Assert.Equal(
            ["a/b", "a/foo"],
            Collect(root, new WalkBuilder(root).MinDepth(2).MaxDepth(1)));
    }

    /// <summary>
    /// Verifies hidden entries are skipped by default and can be included.
    /// </summary>
    [Fact]
    public void HiddenEntriesAreSkippedByDefault()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Combine(root, ".git", "config"), string.Empty);
        File.WriteAllText(Path.Combine(root, "visible"), string.Empty);

        Assert.Equal(["visible"], Collect(root, new WalkBuilder(root)));
        Assert.Equal(
            [".git", ".git/config", ".hidden", "visible"],
            Collect(root, new WalkBuilder(root).Hidden(false)));
    }

    /// <summary>
    /// Verifies maximum file size skips only files, not directories.
    /// </summary>
    [Fact]
    public void MaxFileSizeSkipsLargeFiles()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "a"));
        WriteSizedFile(Path.Combine(root, "small"), 4);
        WriteSizedFile(Path.Combine(root, "large"), 8);
        WriteSizedFile(Path.Combine(root, "a", "nested"), 8);

        Assert.Equal(["a", "small"], Collect(root, new WalkBuilder(root).MaxFileSize(4)));
    }

    /// <summary>
    /// Verifies symbolic links are skipped by default and traversed when requested.
    /// </summary>
    [Fact]
    public void FollowLinksControlsSymlinkTraversal()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "a", "b", "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "real"), string.Empty);
        Assert.True(TryCreateDirectorySymlink(Path.Combine(root, "a", "b"), Path.Combine(root, "z")), "Required directory symlink could not be created.");
        Assert.True(TryCreateFileSymlink(Path.Combine(root, "real"), Path.Combine(root, "file-link")), "Required file symlink could not be created.");

        Assert.Equal(["a", "a/b", "a/b/foo", "real"], Collect(root, new WalkBuilder(root)));
        Assert.Equal(["a", "a/b", "a/b/foo", "file-link", "real", "z", "z/foo"], Collect(root, new WalkBuilder(root).FollowLinks(true)));
    }

    /// <summary>
    /// Verifies symbolic-link loops are not yielded or recursed when following links.
    /// </summary>
    [Fact]
    public void FollowLinksSkipsSymlinkLoops()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        Assert.True(TryCreateDirectorySymlink(Path.Combine(root, "a"), Path.Combine(root, "a", "b", "c")), "Required directory symlink could not be created.");

        Assert.Equal(["a", "a/b"], Collect(root, new WalkBuilder(root)));
        Assert.Equal(["a", "a/b"], Collect(root, new WalkBuilder(root).FollowLinks(true)));
    }

    /// <summary>
    /// Verifies file identities resolve symbolic links to their final target.
    /// </summary>
    [Fact]
    public void FileIdentityFollowsSymlinkTarget()
    {
        string root = CreateTempDirectory();
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        File.WriteAllText(target, string.Empty);
        Assert.True(TryCreateFileSymlink(target, link), "Required file symlink could not be created.");

        Assert.Equal(FileIdentity.FromPath(target), FileIdentity.FromPath(link));
        Assert.NotEqual(FileIdentity.FromPath(target), FileIdentity.FromPath(link, followLinks: false));
    }

    /// <summary>
    /// Verifies same-file-system traversal still descends through the root file system.
    /// </summary>
    [Fact]
    public void SameFileSystemAllowsSameDeviceTraversal()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "a", "b", "file"), string.Empty);

        Assert.Equal(["a", "a/b", "a/b/file"], Collect(root, new WalkBuilder(root).SameFileSystem(true)));
    }

    /// <summary>
    /// Verifies same-file-system traversal yields but does not descend into followed cross-device symlinks.
    /// </summary>
    [Fact]
    public void SameFileSystemSkipsDifferentDeviceSymlinkDescendants()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.True(OperatingSystem.IsWindows());
        }
        else
        {
            string root = CreateTempDirectory();
            string external = OperatingSystem.IsLinux() ? "/sys" : "/dev";
            Assert.True(Directory.Exists(external), "Required cross-device fixture does not exist: " + external);
            Assert.True(NativeFileSystemMetadata.TryGetDevice(root, out FileSystemDevice rootDevice), "Could not read root device.");
            Assert.True(NativeFileSystemMetadata.TryGetDevice(external, out FileSystemDevice externalDevice), "Could not read external device.");
            Assert.NotEqual(rootDevice, externalDevice);

            Directory.CreateDirectory(Path.Combine(root, "same_file"));
            Assert.True(TryCreateDirectorySymlink(external, Path.Combine(root, "same_file", "alink")), "Required cross-device directory symlink could not be created.");

            List<string> baseline = Collect(
                root,
                new WalkBuilder(root).Hidden(false).FollowLinks(true).MaxDepth(3));
            Assert.Contains(baseline, static path => path.StartsWith("same_file/alink/", StringComparison.Ordinal));

            List<string> paths = Collect(
                root,
                new WalkBuilder(root).Hidden(false).FollowLinks(true).SameFileSystem(true).MaxDepth(3));

            Assert.Contains("same_file/alink", paths);
            Assert.DoesNotContain(paths, static path => path.StartsWith("same_file/alink/", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Verifies ignore files apply to descendants and support negation.
    /// </summary>
    [Fact]
    public void IgnoreFilesApplyToDescendantsWithNegation()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n!important.log\n");
        File.WriteAllText(Path.Combine(root, "debug.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "important.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "logs", "debug.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "logs", "important.log"), string.Empty);

        Assert.Equal(["important.log", "logs", "logs/important.log"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies ignore-file globs opt into literal-separator matching.
    /// </summary>
    [Fact]
    public void IgnoreFileWildcardsDoNotCrossSeparators()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src", "nested"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "src/*.log\n");
        File.WriteAllText(Path.Combine(root, "src", "debug.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "nested", "debug.log"), string.Empty);

        Assert.Equal(["src", "src/nested", "src/nested/debug.log"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies ignore files treat unclosed character classes as literals.
    /// </summary>
    [Fact]
    public void IgnoreFilesAllowUnclosedCharacterClasses()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "[\n");
        File.WriteAllText(Path.Combine(root, "["), string.Empty);
        File.WriteAllText(Path.Combine(root, "keep"), string.Empty);

        Assert.Equal(["keep"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies directory-only ignore rules prune matching directories.
    /// </summary>
    [Fact]
    public void DirectoryOnlyIgnoreRulePrunesDirectories()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "target"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "target/\n");
        File.WriteAllText(Path.Combine(root, "target", "artifact"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "target"), string.Empty);

        Assert.Equal(["src", "src/target"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies nested ignore files override ancestor rules.
    /// </summary>
    [Fact]
    public void NestedIgnoreFilesOverrideAncestorRules()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "*.tmp\n");
        File.WriteAllText(Path.Combine(root, "src", ".ignore"), "!keep.tmp\n");
        File.WriteAllText(Path.Combine(root, "drop.tmp"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "drop.tmp"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "keep.tmp"), string.Empty);

        Assert.Equal(["src", "src/keep.tmp"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies parent ignore files apply when walking below the repository root.
    /// </summary>
    [Fact]
    public void ParentIgnoreFilesApplyToSubtreeRoots()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "foo\n");
        File.WriteAllText(Path.Combine(root, "src", "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "bar"), string.Empty);
        string subtree = Path.Combine(root, "src");

        Assert.Equal(["bar"], Collect(subtree, new WalkBuilder(subtree)));
        Assert.Equal(["bar", "foo"], Collect(subtree, new WalkBuilder(subtree).Parents(false)));
    }

    /// <summary>
    /// Verifies rooted patterns in parent ignore files remain anchored to their original directory.
    /// </summary>
    [Fact]
    public void ParentIgnoreRootedPatternsStayAnchoredToParent()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "src", "llvm"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "/llvm/\nfoo\n");
        File.WriteAllText(Path.Combine(root, "src", "foo"), string.Empty);
        string subtree = Path.Combine(root, "src");

        Assert.Equal(["llvm"], Collect(subtree, new WalkBuilder(subtree)));
    }

    /// <summary>
    /// Verifies standard ignore sources can be toggled independently.
    /// </summary>
    [Fact]
    public void StandardIgnoreSourcesCanBeDisabled()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "git-only\n");
        File.WriteAllText(Path.Combine(root, ".ignore"), "dot-only\n");
        File.WriteAllText(Path.Combine(root, ".rgignore"), "rg-only\n");
        File.WriteAllText(Path.Combine(root, "git-only"), string.Empty);
        File.WriteAllText(Path.Combine(root, "dot-only"), string.Empty);
        File.WriteAllText(Path.Combine(root, "rg-only"), string.Empty);
        File.WriteAllText(Path.Combine(root, "keep"), string.Empty);

        Assert.Equal(["git-only", "keep"], Collect(root, new WalkBuilder(root).GitIgnore(false)));
        Assert.Equal(["dot-only", "keep", "rg-only"], Collect(root, new WalkBuilder(root).Ignore(false)));
        Assert.Equal(
            [".git", ".gitignore", ".ignore", ".rgignore", "dot-only", "git-only", "keep", "rg-only"],
            Collect(root, new WalkBuilder(root).StandardFilters(false)));
    }

    /// <summary>
    /// Verifies git-specific ignore files require a repository marker by default.
    /// </summary>
    [Fact]
    public void GitIgnoreRequiresRepositoryByDefault()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".gitignore"), "foo\n");
        File.WriteAllText(Path.Combine(root, "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "bar"), string.Empty);

        Assert.Equal(["bar", "foo"], Collect(root, new WalkBuilder(root)));
        Assert.Equal(["bar"], Collect(root, new WalkBuilder(root).RequireGit(false)));
    }

    /// <summary>
    /// Verifies JJ repository markers enable gitignore semantics.
    /// </summary>
    [Fact]
    public void GitIgnoreAppliesInsideJjRepository()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".jj"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "foo\n");
        File.WriteAllText(Path.Combine(root, "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "bar"), string.Empty);

        Assert.Equal(["bar"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies git exclude files have lower precedence than .gitignore and .ignore files.
    /// </summary>
    [Fact]
    public void GitExcludeHasLowestGitPrecedence()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git", "info"));
        File.WriteAllText(Path.Combine(root, ".git", "info", "exclude"), "foo\nbar\nbaz\n");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "!foo\n");
        File.WriteAllText(Path.Combine(root, ".ignore"), "!bar\n");
        File.WriteAllText(Path.Combine(root, "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "bar"), string.Empty);
        File.WriteAllText(Path.Combine(root, "baz"), string.Empty);
        File.WriteAllText(Path.Combine(root, "keep"), string.Empty);

        Assert.Equal(["bar", "foo", "keep"], Collect(root, new WalkBuilder(root)));
        Assert.Equal(["bar", "baz", "foo", "keep"], Collect(root, new WalkBuilder(root).GitExclude(false)));
    }

    /// <summary>
    /// Verifies linked worktree <c>.git/info/exclude</c> discovery follows the upstream common-dir rules.
    /// </summary>
    [Fact]
    public void GitExcludeReadsLinkedWorktreeCommonDir()
    {
        string root = CreateTempDirectory();
        string gitDirectory = Path.Combine(root, ".git");
        string worktreeGitDirectory = Path.Combine(gitDirectory, "worktrees", "linked-worktree");
        string linkedWorktree = Path.Combine(root, "linked-worktree");
        string commonDirectoryFile = Path.Combine(worktreeGitDirectory, "commondir");
        Directory.CreateDirectory(Path.Combine(gitDirectory, "info"));
        Directory.CreateDirectory(worktreeGitDirectory);
        Directory.CreateDirectory(linkedWorktree);
        File.WriteAllText(Path.Combine(gitDirectory, "info", "exclude"), "ignore_me\n");
        File.WriteAllText(Path.Combine(linkedWorktree, ".git"), "gitdir: " + worktreeGitDirectory);
        File.WriteAllText(Path.Combine(linkedWorktree, "ignore_me"), string.Empty);
        File.WriteAllText(Path.Combine(linkedWorktree, "keep"), string.Empty);

        File.WriteAllText(commonDirectoryFile, "../..");
        Assert.Equal(["keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));

        File.WriteAllText(commonDirectoryFile, gitDirectory);
        Assert.Equal(["keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));

        File.Delete(commonDirectoryFile);
        Assert.Equal(["ignore_me", "keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));

        File.WriteAllText(Path.Combine(linkedWorktree, ".git"), "garbage");
        Assert.Equal(["ignore_me", "keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));
    }

    /// <summary>
    /// Verifies malformed linked-worktree <c>.git</c> files do not activate git exclude rules.
    /// </summary>
    [Fact]
    public void GitExcludeRequiresGitDirPrefixSpace()
    {
        string root = CreateTempDirectory();
        string gitDirectory = Path.Combine(root, ".git");
        string linkedWorktree = Path.Combine(root, "linked-worktree");
        Directory.CreateDirectory(Path.Combine(gitDirectory, "info"));
        Directory.CreateDirectory(linkedWorktree);
        File.WriteAllText(Path.Combine(gitDirectory, "info", "exclude"), "ignore_me\n");
        File.WriteAllText(Path.Combine(linkedWorktree, ".git"), "gitdir:" + gitDirectory);
        File.WriteAllText(Path.Combine(linkedWorktree, "ignore_me"), string.Empty);
        File.WriteAllText(Path.Combine(linkedWorktree, "keep"), string.Empty);

        Assert.Equal(["ignore_me", "keep"], Collect(linkedWorktree, new WalkBuilder(linkedWorktree)));
    }

    /// <summary>
    /// Verifies global gitignore files apply below repository ignore sources and can be disabled.
    /// </summary>
    [Fact]
    public void GlobalGitIgnoreRulesApplyAfterRepositoryIgnoreSources()
    {
        string root = CreateTempDirectory();
        string home = CreateTempDirectory();
        string xdgConfigHome = Path.Combine(home, "xdg");
        string globalOnly = "global-" + Guid.NewGuid().ToString("N") + ".log";
        string gitExcludeWhitelist = "whitelist-" + Guid.NewGuid().ToString("N") + ".log";
        Directory.CreateDirectory(Path.Combine(root, ".git", "info"));
        Directory.CreateDirectory(Path.Combine(xdgConfigHome, "git"));
        File.WriteAllText(Path.Combine(xdgConfigHome, "git", "ignore"), globalOnly + "\n" + gitExcludeWhitelist + "\n");
        File.WriteAllText(Path.Combine(root, ".git", "info", "exclude"), "!" + gitExcludeWhitelist + "\n");
        File.WriteAllText(Path.Combine(root, globalOnly), string.Empty);
        File.WriteAllText(Path.Combine(root, gitExcludeWhitelist), string.Empty);
        File.WriteAllText(Path.Combine(root, "keep"), string.Empty);

        string? originalHome = Environment.GetEnvironmentVariable("HOME");
        string? originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", home);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);

            Assert.Equal(["keep", gitExcludeWhitelist], Collect(root, new WalkBuilder(root)));
            Assert.Equal([globalOnly, "keep", gitExcludeWhitelist], Collect(root, new WalkBuilder(root).GitGlobal(false)));
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
    [Fact]
    public void ExplicitIgnoreFilesHaveLowestPrecedence()
    {
        string root = CreateTempDirectory();
        string ignoreFile = Path.Combine(root, ".not-an-ignore");
        Directory.CreateDirectory(Path.Combine(root, "a"));
        File.WriteAllText(ignoreFile, "foo\nbar\n");
        File.WriteAllText(Path.Combine(root, ".ignore"), "!bar\n");
        File.WriteAllText(Path.Combine(root, "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "bar"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "bar"), string.Empty);

        Assert.Equal(["a", "a/bar", "bar"], Collect(root, new WalkBuilder(root).AddIgnoreFile(ignoreFile)));
        Assert.Equal(
            [".ignore", ".not-an-ignore", "a"],
            Collect(root, new WalkBuilder(root).StandardFilters(false).AddIgnoreFile(ignoreFile)));
    }

    /// <summary>
    /// Verifies ignore files are case-sensitive by default and can be matched ASCII case-insensitively.
    /// </summary>
    [Fact]
    public void IgnoreFilesCanMatchCaseInsensitively()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.html\n");
        File.WriteAllText(Path.Combine(root, "lower.html"), string.Empty);
        File.WriteAllText(Path.Combine(root, "upper.HTML"), string.Empty);
        File.WriteAllText(Path.Combine(root, "short.htm"), string.Empty);
        File.WriteAllText(Path.Combine(root, "wide.HTM"), string.Empty);

        Assert.Equal(["short.htm", "upper.HTML", "wide.HTM"], Collect(root, new WalkBuilder(root)));
        Assert.Equal(
            ["short.htm", "wide.HTM"],
            Collect(root, new WalkBuilder(root).IgnoreCaseInsensitive(true)));
    }

    /// <summary>
    /// Verifies explicit ignore files honor case-insensitive matching.
    /// </summary>
    [Fact]
    public void ExplicitIgnoreFilesCanMatchCaseInsensitively()
    {
        string root = CreateTempDirectory();
        string ignoreRoot = CreateTempDirectory();
        string ignoreFile = Path.Combine(ignoreRoot, "ignore");
        File.WriteAllText(ignoreFile, "*.log\n");
        File.WriteAllText(Path.Combine(root, "trace.LOG"), string.Empty);
        File.WriteAllText(Path.Combine(root, "keep.txt"), string.Empty);

        Assert.Equal(
            ["keep.txt"],
            Collect(root, new WalkBuilder(root).IgnoreCaseInsensitive(true).AddIgnoreFile(ignoreFile)));
    }

    /// <summary>
    /// Verifies custom ignore files override standard ignore files and preserve insertion precedence.
    /// </summary>
    [Fact]
    public void CustomIgnoreFilesOverrideStandardIgnoreFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "foo\n");
        File.WriteAllText(Path.Combine(root, ".custom1"), "!foo\nbar\n");
        File.WriteAllText(Path.Combine(root, ".custom2"), "!bar\n");
        File.WriteAllText(Path.Combine(root, "foo"), string.Empty);
        File.WriteAllText(Path.Combine(root, "bar"), string.Empty);
        File.WriteAllText(Path.Combine(root, "keep"), string.Empty);

        Assert.Equal(
            ["bar", "foo", "keep"],
            Collect(root, new WalkBuilder(root).AddCustomIgnoreFileName(".custom1").AddCustomIgnoreFileName(".custom2")));
    }

    /// <summary>
    /// Verifies escaped leading comment and negation markers are literal patterns.
    /// </summary>
    [Fact]
    public void IgnoreFilesSupportEscapedLeadingMarkers()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "\\#literal\n\\!literal\n");
        File.WriteAllText(Path.Combine(root, "#literal"), string.Empty);
        File.WriteAllText(Path.Combine(root, "!literal"), string.Empty);
        File.WriteAllText(Path.Combine(root, "other"), string.Empty);

        Assert.Equal(["other"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies escaped trailing whitespace remains part of ignore patterns.
    /// </summary>
    [Fact]
    public void IgnoreFilesPreserveEscapedTrailingWhitespace()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "trimmed \nliteral\\ \n");
        File.WriteAllText(Path.Combine(root, "trimmed"), string.Empty);
        File.WriteAllText(Path.Combine(root, "literal "), string.Empty);
        File.WriteAllText(Path.Combine(root, "literal"), string.Empty);

        Assert.Equal(["literal"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies rooted and recursive gitignore patterns follow upstream matching semantics.
    /// </summary>
    [Fact]
    public void IgnoreFilesHonorRootedAndRecursivePatterns()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "child"));
        Directory.CreateDirectory(Path.Combine(root, "a", "x", "y"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "/root.log\n**/any.log\na/**/b\n");
        File.WriteAllText(Path.Combine(root, "root.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "any.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "child", "root.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "child", "any.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "b"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "x", "b"), string.Empty);
        File.WriteAllText(Path.Combine(root, "a", "x", "y", "b"), string.Empty);
        File.WriteAllText(Path.Combine(root, "keep"), string.Empty);

        Assert.Equal(["a", "a/x", "a/x/y", "child", "child/root.log", "keep"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies whitelisted hidden entries bypass hidden filtering.
    /// </summary>
    [Fact]
    public void WhitelistedHiddenEntriesBypassHiddenFiltering()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "!.visible-hidden\n");
        File.WriteAllText(Path.Combine(root, ".visible-hidden"), string.Empty);
        File.WriteAllText(Path.Combine(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Combine(root, "visible"), string.Empty);

        Assert.Equal([".visible-hidden", "visible"], Collect(root, new WalkBuilder(root)));
    }

    /// <summary>
    /// Verifies whitelist overrides include matching files and ignore unmatched files.
    /// </summary>
    [Fact]
    public void OverrideWhitelistIgnoresUnmatchedFiles()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "main.rs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "main.c"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("*.rs").Build();

        Assert.Equal(["src", "src/main.rs"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies negated overrides exclude matching paths.
    /// </summary>
    [Fact]
    public void NegatedOverrideIgnoresMatchingPath()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.rs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "drop.generated.rs"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("*.rs").Add("!*.generated.rs").Build();

        Assert.Equal(["keep.rs"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies override whitelists take precedence over ignore-file rules and hidden filtering.
    /// </summary>
    [Fact]
    public void OverrideWhitelistBeatsIgnoreAndHiddenFilters()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Combine(root, ".hidden.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "visible.log"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("*.log").Build();

        Assert.Equal([".hidden.log", "visible.log"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies directory-only overrides ignore matching directories.
    /// </summary>
    [Fact]
    public void DirectoryOnlyOverrideIgnoresMatchingDirectories()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "target"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "target", "artifact"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "target"), string.Empty);
        Override overrides = new OverrideBuilder(root).Add("!target/").Build();

        Assert.Equal(["src", "src/target"], Collect(root, new WalkBuilder(root).Overrides(overrides)));
    }

    /// <summary>
    /// Verifies selected file types whitelist matching files and ignore other files.
    /// </summary>
    [Fact]
    public void SelectedFileTypeFiltersFiles()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "main.rs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "main.c"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("rust", "*.rs")
            .Select("rust")
            .Build();

        Assert.Equal(["src", "src/main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies negated file types ignore matching files and leave other files alone.
    /// </summary>
    [Fact]
    public void NegatedFileTypeIgnoresMatchingFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "main.rs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "main.c"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("c", "*.c")
            .Negate("c")
            .Build();

        Assert.Equal(["main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies include definitions inherit globs from existing file types.
    /// </summary>
    [Fact]
    public void FileTypeIncludeDefinitionsUseExistingGlobs()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "index.html"), string.Empty);
        File.WriteAllText(Path.Combine(root, "lib.rs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "script.js"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .AddDefinition("html:*.html")
            .AddDefinition("rust:*.rs")
            .AddDefinition("combo:include:html,rust")
            .Select("combo")
            .Build();

        Assert.Equal(["index.html", "lib.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies default file types include the pinned container type.
    /// </summary>
    [Fact]
    public void DefaultFileTypesIncludeContainer()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "Dockerfile"), string.Empty);
        File.WriteAllText(Path.Combine(root, "dev.Containerfile"), string.Empty);
        File.WriteAllText(Path.Combine(root, "main.rs"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .AddDefaults()
            .Select("container")
            .Build();

        Assert.Equal(["Dockerfile", "dev.Containerfile"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies file type whitelists bypass hidden filtering.
    /// </summary>
    [Fact]
    public void FileTypeWhitelistBypassesHiddenFiltering()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".main.rs"), string.Empty);
        File.WriteAllText(Path.Combine(root, ".hidden.txt"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("rust", "*.rs")
            .Select("rust")
            .Build();

        Assert.Equal([".main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies file type ignores still apply after ignore-file whitelists.
    /// </summary>
    [Fact]
    public void FileTypesCanIgnoreIgnoreFileWhitelists()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.tmp\n!keep.tmp\n");
        File.WriteAllText(Path.Combine(root, "keep.tmp"), string.Empty);
        File.WriteAllText(Path.Combine(root, "main.rs"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .Add("rust", "*.rs")
            .Select("rust")
            .Build();

        Assert.Equal(["main.rs"], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    /// <summary>
    /// Verifies representative pinned upstream default file type patterns.
    /// </summary>
    /// <param name="fileType">The default file type to select.</param>
    /// <param name="fileName">The file name expected to match.</param>
    [Theory]
    [InlineData("bazel", "WORKSPACE.bazel")]
    [InlineData("dockercompose", "docker-compose.prod.yml")]
    [InlineData("license", "COPYING")]
    [InlineData("msbuild", "solution.slnf")]
    [InlineData("ruby", "Gemfile")]
    [InlineData("tf", "prod.tfvars.json")]
    [InlineData("typescript", "source.cts")]
    [InlineData("vim", ".vimrc")]
    [InlineData("zstd", "archive.zstd")]
    public void DefaultFileTypesIncludePinnedUpstreamPatterns(string fileType, string fileName)
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, fileName), string.Empty);
        File.WriteAllText(Path.Combine(root, "unmatched.nope"), string.Empty);
        FileTypeMatcher fileTypes = new FileTypeMatcherBuilder()
            .AddDefaults()
            .Select(fileType)
            .Build();

        Assert.Equal([fileName], Collect(root, new WalkBuilder(root).FileTypes(fileTypes)));
    }

    private static List<string> Collect(string root, WalkBuilder builder)
    {
        List<string> paths = [];
        foreach (DirEntry entry in builder.SortByPath().Build())
        {
            string relative = Path.GetRelativePath(root, entry.FullPath);
            if (relative == ".")
            {
                continue;
            }

            paths.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
        }

        paths.Sort(StringComparer.Ordinal);
        return paths;
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
        string path = Path.Combine(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteSizedFile(string path, int size)
    {
        using FileStream stream = File.Create(path);
        stream.SetLength(size);
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
