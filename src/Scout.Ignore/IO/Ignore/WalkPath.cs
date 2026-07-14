using System.Text;

namespace Scout.IO.Ignore;

/// <summary>
/// Carries one walk path together with byte-preserving Unix path data and directory-entry type metadata when available.
/// </summary>
internal readonly struct WalkPath(
    string textPath,
    byte[]? unixPathBytes,
    byte[]? unixFileNameBytes,
    RawUnixDirectoryEntryType unixFileType)
{
    private static readonly UTF8Encoding Utf8Lossy = new(encoderShouldEmitUTF8Identifier: false);
    private readonly byte[]? _unixPathBytes = unixPathBytes;
    private readonly byte[]? _unixFileNameBytes = unixFileNameBytes;
    private readonly RawUnixDirectoryEntryType _unixFileType = unixFileType;

    /// <summary>
    /// Gets the text representation of the path.
    /// </summary>
    public string TextPath { get; } = textPath;

    /// <summary>
    /// Gets the byte-preserving Unix path when the text representation is lossy.
    /// </summary>
    public ReadOnlySpan<byte> UnixPathBytes => _unixPathBytes.AsSpan();

    /// <summary>
    /// Gets the byte-preserving Unix file name when the text representation is lossy.
    /// </summary>
    public ReadOnlySpan<byte> UnixFileNameBytes => _unixFileNameBytes.AsSpan();

    /// <summary>
    /// Gets a value indicating whether this path retains raw Unix bytes.
    /// </summary>
    public bool IsRawUnixPath => _unixPathBytes is not null;

    /// <summary>
    /// Gets a value indicating whether the Unix directory entry reported a regular file.
    /// </summary>
    public bool IsKnownUnixRegularFile => _unixFileType == RawUnixDirectoryEntryType.RegularFile;

    /// <summary>
    /// Gets a value indicating whether the Unix directory entry reported a directory.
    /// </summary>
    public bool IsKnownUnixDirectory => _unixFileType == RawUnixDirectoryEntryType.Directory;

    /// <summary>
    /// Gets a value indicating whether the Unix directory entry reported a symbolic link.
    /// </summary>
    public bool IsKnownUnixSymbolicLink => _unixFileType == RawUnixDirectoryEntryType.SymbolicLink;

    /// <summary>
    /// Gets a value indicating whether the Unix directory entry reported a type that can be used without a status call.
    /// </summary>
    public bool HasUsableUnixFileType => _unixFileType is
        RawUnixDirectoryEntryType.RegularFile or
        RawUnixDirectoryEntryType.Directory or
        RawUnixDirectoryEntryType.SymbolicLink;

    /// <summary>
    /// Creates a path from a text representation.
    /// </summary>
    /// <param name="textPath">The text path.</param>
    /// <param name="unixFileType">The optional native Unix directory-entry type.</param>
    /// <returns>The walk path.</returns>
    public static WalkPath FromText(
        string textPath,
        RawUnixDirectoryEntryType unixFileType = RawUnixDirectoryEntryType.Unknown)
    {
        ArgumentException.ThrowIfNullOrEmpty(textPath);
        return new WalkPath(textPath, unixPathBytes: null, unixFileNameBytes: null, unixFileType);
    }

    /// <summary>
    /// Creates a byte-preserving Unix path.
    /// </summary>
    /// <param name="unixPathBytes">The raw Unix path bytes.</param>
    /// <param name="unixFileNameBytes">The raw Unix file name bytes.</param>
    /// <param name="unixFileType">The optional native Unix directory-entry type.</param>
    /// <returns>The walk path.</returns>
    public static WalkPath FromRawUnix(
        ReadOnlySpan<byte> unixPathBytes,
        ReadOnlySpan<byte> unixFileNameBytes,
        RawUnixDirectoryEntryType unixFileType = RawUnixDirectoryEntryType.Unknown)
    {
        if (unixPathBytes.IsEmpty)
        {
            throw new ArgumentException("Path cannot be empty.", nameof(unixPathBytes));
        }

        return new WalkPath(
            Utf8Lossy.GetString(unixPathBytes),
            unixPathBytes.ToArray(),
            unixFileNameBytes.ToArray(),
            unixFileType);
    }
}
