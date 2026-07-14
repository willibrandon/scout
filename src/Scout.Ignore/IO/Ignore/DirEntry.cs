using System.Text;

namespace Scout.IO.Ignore;

/// <summary>
/// Represents one entry yielded by Scout's recursive directory walker.
/// </summary>
public sealed class DirEntry
{
    private static readonly UTF8Encoding Utf8Lossy = new(encoderShouldEmitUTF8Identifier: false);
    private readonly byte[]? _unixPathBytes;
    private readonly byte[]? _unixFileNameBytes;
    private readonly long? _length;
    private readonly FileIdentity _identity;
    private readonly bool _deferMetadata;
    private DirEntryMetadata? _deferredMetadata;

    internal DirEntry(
        string fullPath,
        int depth,
        FileAttributes attributes,
        bool isDirectory,
        bool isSymbolicLink,
        bool isStdin,
        long? length,
        FileIdentity identity,
        string? resolvedFullPath = null,
        byte[]? unixPathBytes = null,
        byte[]? unixFileNameBytes = null,
        bool deferMetadata = false)
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
        _length = length;
        _identity = identity;
        _unixPathBytes = unixPathBytes;
        _unixFileNameBytes = unixFileNameBytes;
        _deferMetadata = deferMetadata;
    }

    /// <summary>
    /// Gets the full path for this directory entry.
    /// </summary>
    public string FullPath { get; }

    internal string ResolvedFullPath { get; }

    /// <summary>
    /// Gets the file name for this directory entry.
    /// </summary>
    public string FileName => IsStdin
        ? "<stdin>"
        : _unixFileNameBytes is not null
            ? Utf8Lossy.GetString(_unixFileNameBytes)
            : Path.GetFileName(FullPath);

    internal bool IsRawUnixPath => _unixPathBytes is not null;

    internal ReadOnlySpan<byte> UnixPathBytes => _unixPathBytes.AsSpan();

    internal ReadOnlySpan<byte> UnixFileNameBytes => _unixFileNameBytes.AsSpan();

    /// <summary>
    /// Gets the file length only when traversal already resolved it without an additional metadata query.
    /// </summary>
    internal long? KnownLength => _deferMetadata
        ? Volatile.Read(ref _deferredMetadata)?.Length
        : _length;

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
    /// Gets the file length when this entry is a file. On Unix, first access may query
    /// the filesystem when the native directory record supplied only the entry type.
    /// </summary>
    public long? Length => _deferMetadata ? GetDeferredMetadata().Length : _length;

    /// <summary>
    /// Gets the best available same-file identity. On Unix, first access may query
    /// the filesystem when the native directory record supplied only the entry type.
    /// </summary>
    public FileIdentity Identity => _deferMetadata ? GetDeferredMetadata().Identity : _identity;

    internal static DirEntry Stdin()
    {
        return new DirEntry("<stdin>", 0, default, isDirectory: false, isSymbolicLink: false, isStdin: true, null, default);
    }

    private DirEntryMetadata GetDeferredMetadata()
    {
        DirEntryMetadata? metadata = Volatile.Read(ref _deferredMetadata);
        if (metadata is not null)
        {
            return metadata;
        }

        metadata = ResolveMetadata();
        return Interlocked.CompareExchange(ref _deferredMetadata, metadata, null) ?? metadata;
    }

    private DirEntryMetadata ResolveMetadata()
    {
        NativeUnixFileStatus status;
        bool found = IsRawUnixPath
            ? NativeFileSystemMetadata.TryGetRawUnixStatus(UnixPathBytes, followLinks: false, out status)
            : NativeFileSystemMetadata.TryGetUnixStatus(FullPath, followLinks: false, out status);
        if (!found)
        {
            return new DirEntryMetadata(_length, _identity);
        }

        FileIdentity identity = IsRawUnixPath
            ? FileIdentity.FromRawUnixPath(UnixPathBytes, status.Metadata)
            : FileIdentity.FromPathMetadata(FullPath, status.Metadata);
        return new DirEntryMetadata(status.Length, identity);
    }
}
