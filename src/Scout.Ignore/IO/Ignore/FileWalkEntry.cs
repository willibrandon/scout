namespace Scout.IO.Ignore;

/// <summary>
/// Represents one filesystem entry yielded by <see cref="FileWalker" />.
/// </summary>
public readonly struct FileWalkEntry : IEquatable<FileWalkEntry>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileWalkEntry" /> struct.
    /// </summary>
    /// <param name="fullPath">The full path for the entry.</param>
    /// <param name="fileName">The file name for the entry.</param>
    /// <param name="depth">The entry depth relative to the walk root.</param>
    /// <param name="attributes">The filesystem attributes captured for the entry.</param>
    /// <param name="isDirectory">Whether the entry is a directory.</param>
    /// <param name="isFile">Whether the entry is a regular file.</param>
    /// <param name="isSymbolicLink">Whether the entry path is a symbolic link.</param>
    /// <param name="isStdin">Whether the entry represents standard input.</param>
    /// <param name="length">The file length when known.</param>
    public FileWalkEntry(
        string fullPath,
        string fileName,
        int depth,
        FileAttributes attributes,
        bool isDirectory,
        bool isFile,
        bool isSymbolicLink,
        bool isStdin,
        long? length)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        FullPath = fullPath;
        FileName = fileName;
        Depth = depth;
        Attributes = attributes;
        IsDirectory = isDirectory;
        IsFile = isFile;
        IsSymbolicLink = isSymbolicLink;
        IsStdin = isStdin;
        Length = length;
    }

    /// <summary>
    /// Gets the full path for this entry.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Gets the file name for this entry.
    /// </summary>
    public string FileName { get; }

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
    public bool IsFile { get; }

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
    /// Tests whether two entries are equal.
    /// </summary>
    /// <param name="left">The left entry.</param>
    /// <param name="right">The right entry.</param>
    /// <returns><see langword="true" /> when the entries have the same values.</returns>
    public static bool operator ==(FileWalkEntry left, FileWalkEntry right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Tests whether two entries differ.
    /// </summary>
    /// <param name="left">The left entry.</param>
    /// <param name="right">The right entry.</param>
    /// <returns><see langword="true" /> when the entries differ.</returns>
    public static bool operator !=(FileWalkEntry left, FileWalkEntry right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(FileWalkEntry other)
    {
        return string.Equals(FullPath, other.FullPath, StringComparison.Ordinal) &&
            string.Equals(FileName, other.FileName, StringComparison.Ordinal) &&
            Depth == other.Depth &&
            Attributes == other.Attributes &&
            IsDirectory == other.IsDirectory &&
            IsFile == other.IsFile &&
            IsSymbolicLink == other.IsSymbolicLink &&
            IsStdin == other.IsStdin &&
            Length == other.Length;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is FileWalkEntry entry && Equals(entry);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FullPath, StringComparer.Ordinal);
        hash.Add(FileName, StringComparer.Ordinal);
        hash.Add(Depth);
        hash.Add(Attributes);
        hash.Add(IsDirectory);
        hash.Add(IsFile);
        hash.Add(IsSymbolicLink);
        hash.Add(IsStdin);
        hash.Add(Length);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FullPath;
    }

    internal static FileWalkEntry FromDirEntry(DirEntry entry)
    {
        return new FileWalkEntry(
            entry.FullPath,
            entry.FileName,
            entry.Depth,
            entry.Attributes,
            entry.IsDirectory,
            entry.IsFile,
            entry.IsSymbolicLink,
            entry.IsStdin,
            entry.Length);
    }
}
