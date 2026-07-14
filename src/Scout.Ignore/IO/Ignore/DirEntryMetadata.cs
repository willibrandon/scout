namespace Scout.IO.Ignore;

/// <summary>
/// Holds lazily resolved metadata for a directory entry whose native directory record supplied its file type.
/// </summary>
internal sealed class DirEntryMetadata(long? length, FileIdentity identity)
{
    /// <summary>
    /// Gets the file length when the entry is a regular file.
    /// </summary>
    public long? Length { get; } = length;

    /// <summary>
    /// Gets the stable identity of the entry.
    /// </summary>
    public FileIdentity Identity { get; } = identity;
}
