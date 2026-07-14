namespace Scout;

/// <summary>
/// Identifies the native file type reported by a Unix directory entry.
/// </summary>
internal enum RawUnixDirectoryEntryType : byte
{
    /// <summary>
    /// The directory entry did not report a usable file type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The entry is a directory.
    /// </summary>
    Directory = 4,

    /// <summary>
    /// The entry is a regular file.
    /// </summary>
    RegularFile = 8,

    /// <summary>
    /// The entry is a symbolic link.
    /// </summary>
    SymbolicLink = 10,
}
