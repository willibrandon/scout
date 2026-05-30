using System;
using System.IO;

namespace Scout;

/// <summary>
/// Represents a stable identity used for same-file and symlink-loop checks.
/// </summary>
public readonly struct FileIdentity : IEquatable<FileIdentity>
{
    private readonly FileSystemMetadata metadata;
    private readonly string normalizedPath;

    private FileIdentity(string normalizedPath)
    {
        metadata = default;
        this.normalizedPath = normalizedPath;
    }

    private FileIdentity(FileSystemMetadata metadata, string normalizedPath)
    {
        this.metadata = metadata;
        this.normalizedPath = normalizedPath;
    }

    /// <summary>
    /// Gets the normalized path backing this identity.
    /// </summary>
    public string NormalizedPath => normalizedPath ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether this identity is empty.
    /// </summary>
    public bool IsEmpty => metadata.IsEmpty && NormalizedPath.Length == 0;

    internal FileSystemDevice Device => metadata.Device;

    /// <summary>
    /// Creates a file identity from a path.
    /// </summary>
    /// <param name="path">The path to identify.</param>
    /// <param name="followLinks">Whether symbolic links should resolve to their final target.</param>
    /// <returns>A file identity.</returns>
    public static FileIdentity FromPath(string path, bool followLinks = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath = Path.GetFullPath(path);
        if (followLinks)
        {
            fullPath = ResolveFinalTarget(fullPath);
        }

        if (NativeFileSystemMetadata.TryGet(path, followLinks, out FileSystemMetadata metadata))
        {
            return new FileIdentity(metadata, Normalize(fullPath));
        }

        return new FileIdentity(Normalize(fullPath));
    }

    internal static FileIdentity FromRawUnixPath(ReadOnlySpan<byte> path, FileSystemMetadata metadata)
    {
        if (!metadata.IsEmpty)
        {
            return new FileIdentity(metadata, Convert.ToBase64String(path));
        }

        return new FileIdentity(Convert.ToBase64String(path));
    }

    /// <inheritdoc />
    public bool Equals(FileIdentity other)
    {
        if (!metadata.IsEmpty && !other.metadata.IsEmpty)
        {
            return metadata.Equals(other.metadata);
        }

        return Comparer.Equals(NormalizedPath, other.NormalizedPath);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is FileIdentity other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (!metadata.IsEmpty)
        {
            return metadata.GetHashCode();
        }

        return Comparer.GetHashCode(NormalizedPath);
    }

    /// <summary>
    /// Compares two identities for equality.
    /// </summary>
    /// <param name="left">The left identity.</param>
    /// <param name="right">The right identity.</param>
    /// <returns><see langword="true" /> when the identities are equal.</returns>
    public static bool operator ==(FileIdentity left, FileIdentity right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two identities for inequality.
    /// </summary>
    /// <param name="left">The left identity.</param>
    /// <param name="right">The right identity.</param>
    /// <returns><see langword="true" /> when the identities are not equal.</returns>
    public static bool operator !=(FileIdentity left, FileIdentity right)
    {
        return !left.Equals(right);
    }

    private static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string Normalize(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static string ResolveFinalTarget(string path)
    {
        FileSystemInfo info = CreateInfo(path);
        if (!IsSymbolicLink(info))
        {
            return info.FullName;
        }

        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
        return target?.FullName ?? info.FullName;
    }

    private static FileSystemInfo CreateInfo(string path)
    {
        if (Directory.Exists(path) && !File.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        return new FileInfo(path);
    }

    private static bool IsSymbolicLink(FileSystemInfo info)
    {
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }
}
