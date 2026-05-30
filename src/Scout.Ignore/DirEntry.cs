using System;
using System.IO;

namespace Scout;

/// <summary>
/// Represents one entry yielded by Scout's recursive directory walker.
/// </summary>
public sealed class DirEntry
{
    internal DirEntry(
        string fullPath,
        int depth,
        FileAttributes attributes,
        bool isDirectory,
        bool isSymbolicLink,
        bool isStdin,
        long? length,
        FileIdentity identity,
        string? resolvedFullPath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        FullPath = fullPath;
        ResolvedFullPath = resolvedFullPath ?? fullPath;
        Depth = depth;
        Attributes = attributes;
        IsDirectory = isDirectory;
        IsSymbolicLink = isSymbolicLink;
        IsStdin = isStdin;
        Length = length;
        Identity = identity;
    }

    /// <summary>
    /// Gets the full path for this directory entry.
    /// </summary>
    public string FullPath { get; }

    internal string ResolvedFullPath { get; }

    /// <summary>
    /// Gets the file name for this directory entry.
    /// </summary>
    public string FileName => IsStdin ? "<stdin>" : Path.GetFileName(FullPath);

    /// <summary>
    /// Gets the entry depth relative to the walk root.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Gets the filesystem attributes captured for this entry.
    /// </summary>
    public FileAttributes Attributes { get; }

    /// <summary>
    /// Gets a value indicating whether this entry is a directory.
    /// </summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// Gets a value indicating whether this entry is a regular file.
    /// </summary>
    public bool IsFile => !IsDirectory && !IsStdin;

    /// <summary>
    /// Gets a value indicating whether this entry path is a symbolic link.
    /// </summary>
    public bool IsSymbolicLink { get; }

    /// <summary>
    /// Gets a value indicating whether this entry represents standard input.
    /// </summary>
    public bool IsStdin { get; }

    /// <summary>
    /// Gets the file length when this entry is a file.
    /// </summary>
    public long? Length { get; }

    /// <summary>
    /// Gets the best available same-file identity for this entry.
    /// </summary>
    public FileIdentity Identity { get; }

    internal static DirEntry Stdin()
    {
        return new DirEntry("<stdin>", 0, default, isDirectory: false, isSymbolicLink: false, isStdin: true, null, default);
    }
}
