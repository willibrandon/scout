using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Scout;

/// <summary>
/// Recursively yields directory entries from paths configured by <see cref="WalkBuilder" />.
/// </summary>
public sealed class Walk : IEnumerable<DirEntry>
{
    private readonly string[] paths;
    private readonly int? minDepth;
    private readonly int? maxDepth;
    private readonly long? maxFileSize;
    private readonly bool hidden;
    private readonly bool followLinks;
    private readonly bool sameFileSystem;
    private readonly bool parents;
    private readonly bool dotIgnore;
    private readonly bool gitIgnore;
    private readonly bool gitExclude;
    private readonly bool gitGlobal;
    private readonly bool requireGit;
    private readonly bool ignoreCaseInsensitive;
    private readonly WalkSort sort;
    private readonly Override overrides;
    private readonly FileTypeMatcher fileTypes;
    private readonly IgnoreRuleSet explicitIgnoreRules;
    private readonly string[] customIgnoreFileNames;

    internal Walk(
        string[] paths,
        int? minDepth,
        int? maxDepth,
        long? maxFileSize,
        bool hidden,
        bool followLinks,
        bool sameFileSystem,
        bool parents,
        bool dotIgnore,
        bool gitIgnore,
        bool gitExclude,
        bool gitGlobal,
        bool requireGit,
        bool ignoreCaseInsensitive,
        WalkSort sort,
        Override overrides,
        FileTypeMatcher fileTypes,
        IgnoreRuleSet explicitIgnoreRules,
        string[] customIgnoreFileNames)
    {
        this.paths = paths;
        this.minDepth = minDepth;
        this.maxDepth = maxDepth;
        this.maxFileSize = maxFileSize;
        this.hidden = hidden;
        this.followLinks = followLinks;
        this.sameFileSystem = sameFileSystem;
        this.parents = parents;
        this.dotIgnore = dotIgnore;
        this.gitIgnore = gitIgnore;
        this.gitExclude = gitExclude;
        this.gitGlobal = gitGlobal;
        this.requireGit = requireGit;
        this.ignoreCaseInsensitive = ignoreCaseInsensitive;
        this.sort = sort;
        this.overrides = overrides;
        this.fileTypes = fileTypes;
        this.explicitIgnoreRules = explicitIgnoreRules;
        this.customIgnoreFileNames = customIgnoreFileNames;
    }

    /// <inheritdoc />
    public IEnumerator<DirEntry> GetEnumerator()
    {
        foreach (WalkWorkItem item in CreateInitialWorkItems())
        {
            if (item.Path == "-")
            {
                yield return DirEntry.Stdin();
                continue;
            }

            foreach (DirEntry entry in Enumerate(
                item.Path,
                item.Depth,
                item.Ancestors,
                item.IgnoreStack,
                item.RootDevice,
                item.IsRoot))
            {
                yield return entry;
            }
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private IEnumerable<DirEntry> Enumerate(
        string path,
        int depth,
        HashSet<FileIdentity> ancestors,
        IgnoreStack ignoreStack,
        FileSystemDevice rootDevice,
        bool isRoot)
    {
        if (!TryEvaluateEntry(path, depth, ancestors, ignoreStack, rootDevice, isRoot, out WalkEntryState state))
        {
            yield break;
        }

        DirEntry entry = state.Entry;
        if (state.ShouldYield)
        {
            yield return entry;
        }

        if (!state.ShouldRecurse)
        {
            yield break;
        }

        bool added = !entry.Identity.IsEmpty && ancestors.Add(entry.Identity);
        try
        {
            foreach (string childPath in EnumerateChildren(entry))
            {
                foreach (DirEntry childEntry in Enumerate(childPath, depth + 1, ancestors, state.ChildIgnoreStack, rootDevice, isRoot: false))
                {
                    yield return childEntry;
                }
            }
        }
        finally
        {
            if (added)
            {
                ancestors.Remove(entry.Identity);
            }
        }
    }

    internal IEnumerable<WalkWorkItem> CreateInitialWorkItems()
    {
        for (int index = 0; index < paths.Length; index++)
        {
            string path = paths[index];
            if (path == "-")
            {
                yield return new WalkWorkItem(path, depth: 0, [], IgnoreStack.Empty, default, isRoot: true);
                continue;
            }

            string fullPath = Path.GetFullPath(path);
            IgnoreRuleSet globalGitRules = gitGlobal
                ? GlobalGitIgnore.Load(Directory.GetCurrentDirectory(), ignoreCaseInsensitive)
                : new IgnoreRuleSet();
            var rootIgnoreStack = IgnoreStack.Create(globalGitRules);
            IgnoreStack ignoreStack = parents
                ? rootIgnoreStack.AddParents(
                    fullPath,
                    dotIgnore,
                    gitIgnore,
                    gitExclude,
                    requireGit,
                    ignoreCaseInsensitive,
                    customIgnoreFileNames)
                : rootIgnoreStack;
            FileSystemDevice rootDevice = GetRootDevice(fullPath);
            yield return new WalkWorkItem(fullPath, depth: 0, [], ignoreStack, rootDevice, isRoot: true);
        }
    }

    internal bool TryEvaluateEntry(
        string path,
        int depth,
        HashSet<FileIdentity> ancestors,
        IgnoreStack ignoreStack,
        FileSystemDevice rootDevice,
        bool isRoot,
        out WalkEntryState state)
    {
        DirEntry entry = CreateEntry(path, depth);
        IgnoreStack childIgnoreStack = entry.IsDirectory
            ? ignoreStack.AddDirectory(
                entry.FullPath,
                dotIgnore,
                gitIgnore,
                gitExclude,
                requireGit,
                ignoreCaseInsensitive,
                customIgnoreFileNames)
            : ignoreStack;
        IgnoreDecision overrideDecision = isRoot ? IgnoreDecision.None : overrides.Match(entry);
        if (overrideDecision == IgnoreDecision.Ignore)
        {
            state = default;
            return false;
        }

        IgnoreDecision ignoreDecision = overrideDecision == IgnoreDecision.Whitelist || isRoot
            ? overrideDecision
            : ignoreStack.Match(entry);
        if (!isRoot && ignoreDecision == IgnoreDecision.None)
        {
            ignoreDecision = explicitIgnoreRules.Match(entry);
        }

        if (ignoreDecision == IgnoreDecision.Ignore)
        {
            state = default;
            return false;
        }

        if (overrideDecision != IgnoreDecision.Whitelist)
        {
            IgnoreDecision typeDecision = fileTypes.Match(entry);
            if (typeDecision == IgnoreDecision.Ignore)
            {
                state = default;
                return false;
            }

            if (typeDecision == IgnoreDecision.Whitelist)
            {
                ignoreDecision = IgnoreDecision.Whitelist;
            }
        }

        if (!isRoot && ignoreDecision != IgnoreDecision.Whitelist && hidden && PathUtil.IsHidden(entry))
        {
            state = default;
            return false;
        }

        if (entry.IsDirectory && followLinks && entry.IsSymbolicLink && ancestors.Contains(entry.Identity))
        {
            state = default;
            return false;
        }

        state = new WalkEntryState(entry, childIgnoreStack, ShouldYield(entry, isRoot), ShouldRecurse(entry, rootDevice, isRoot));
        return true;
    }

    private bool ShouldYield(DirEntry entry, bool isRoot)
    {
        if (!isRoot && entry.IsSymbolicLink && !followLinks)
        {
            return false;
        }

        if (minDepth.HasValue && entry.Depth < minDepth.Value)
        {
            return false;
        }

        if (maxFileSize.HasValue && entry.IsFile && entry.Length > maxFileSize.Value)
        {
            return false;
        }

        return true;
    }

    private bool ShouldRecurse(DirEntry entry, FileSystemDevice rootDevice, bool isRoot)
    {
        if (!entry.IsDirectory)
        {
            return false;
        }

        if (maxDepth.HasValue && entry.Depth >= maxDepth.Value)
        {
            return false;
        }

        if (sameFileSystem && !isRoot && !rootDevice.IsEmpty && !IsSameFileSystem(rootDevice, entry))
        {
            return false;
        }

        return followLinks || !entry.IsSymbolicLink || entry.Depth == 0;
    }

    private FileSystemDevice GetRootDevice(string path)
    {
        if (!sameFileSystem)
        {
            return default;
        }

        return NativeFileSystemMetadata.TryGetDevice(path, out FileSystemDevice device) ? device : default;
    }

    private static bool IsSameFileSystem(FileSystemDevice rootDevice, DirEntry entry)
    {
        if (!NativeFileSystemMetadata.TryGetDevice(entry.FullPath, out FileSystemDevice device))
        {
            return true;
        }

        return rootDevice.Equals(device);
    }

    private DirEntry CreateEntry(string path, int depth)
    {
        FileAttributes attributes = File.GetAttributes(path);
        bool isSymbolicLink = (attributes & FileAttributes.ReparsePoint) != 0;
        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
        long? length = null;
        string resolvedFullPath = path;

        if (followLinks)
        {
            string? target = TryResolveLinkTarget(path);
            if (target is not null)
            {
                isSymbolicLink = true;
                resolvedFullPath = target;
                if (Directory.Exists(target))
                {
                    isDirectory = true;
                    length = null;
                }
                else if (File.Exists(target))
                {
                    isDirectory = false;
                    length = new FileInfo(target).Length;
                }
            }
        }

        if (followLinks && !isDirectory && TryCanEnumerateDirectory(resolvedFullPath))
        {
            isDirectory = true;
            length = null;
        }

        if (!isDirectory && length is null)
        {
            length = new FileInfo(path).Length;
        }

        var identity = FileIdentity.FromPath(path, followLinks);
        return new DirEntry(path, depth, attributes, isDirectory, isSymbolicLink, isStdin: false, length, identity, resolvedFullPath);
    }

    internal string[] EnumerateChildren(DirEntry entry)
    {
        string[] children = Directory.GetFileSystemEntries(entry.ResolvedFullPath);
        Sort(children);
        if (entry.ResolvedFullPath == entry.FullPath)
        {
            return children;
        }

        string[] paths = new string[children.Length];
        for (int index = 0; index < children.Length; index++)
        {
            paths[index] = Path.Combine(entry.FullPath, Path.GetFileName(children[index]));
        }

        return paths;
    }

    private static bool TryCanEnumerateDirectory(string path)
    {
        try
        {
            _ = new DirectoryInfo(path).GetFileSystemInfos();
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
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

    private void Sort(FileSystemInfo[] children)
    {
        if (sort == WalkSort.None)
        {
            return;
        }

        Array.Sort(children, Compare);
    }

    private int Compare(FileSystemInfo? left, FileSystemInfo? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        if (right is null)
        {
            return 1;
        }

        return sort == WalkSort.ByFileName
            ? StringComparer.Ordinal.Compare(left.Name, right.Name)
            : StringComparer.Ordinal.Compare(left.FullName, right.FullName);
    }

    private void Sort(string[] children)
    {
        if (sort == WalkSort.None)
        {
            return;
        }

        Array.Sort(children, Compare);
    }

    private int Compare(string? left, string? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        if (right is null)
        {
            return 1;
        }

        return sort == WalkSort.ByFileName
            ? StringComparer.Ordinal.Compare(Path.GetFileName(left), Path.GetFileName(right))
            : StringComparer.Ordinal.Compare(left, right);
    }

    private static FileSystemInfo CreateInfo(string path)
    {
        if (Directory.Exists(path) && !File.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        return new FileInfo(path);
    }

    private static string? TryResolveLinkTarget(string path)
    {
        FileSystemInfo? target = CreateInfo(path).ResolveLinkTarget(returnFinalTarget: true);
        if (target is not null)
        {
            return target.FullName;
        }

        if (!NativeFileSystemMetadata.TryReadLinkTarget(path, out string linkTarget))
        {
            return null;
        }

        string resolved = Path.IsPathRooted(linkTarget)
            ? linkTarget
            : Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, linkTarget);
        return Path.GetFullPath(resolved);
    }
}
