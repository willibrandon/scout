
namespace Scout;

/// <summary>
/// Represents one raw Unix directory entry.
/// </summary>
public sealed class RawUnixDirectoryEntry
{
    private readonly byte[] _name;
    private readonly byte[] _parentPath;
    private byte[]? _fullPath;

    internal RawUnixDirectoryEntry(
        byte[] name,
        byte[] parentPath,
        RawUnixDirectoryEntryType fileType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(parentPath);

        _name = name;
        _parentPath = parentPath;
        FileType = fileType;
    }

    /// <summary>
    /// Gets the raw entry name bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Name => _name;

    /// <summary>
    /// Gets the raw full path bytes.
    /// </summary>
    public ReadOnlyMemory<byte> FullPath
    {
        get
        {
            byte[]? fullPath = Volatile.Read(ref _fullPath);
            if (fullPath is not null)
            {
                return fullPath;
            }

            fullPath = RawUnixDirectory.Join(_parentPath, _name);
            return Interlocked.CompareExchange(ref _fullPath, fullPath, null) ?? fullPath;
        }
    }

    /// <summary>
    /// Gets the native directory-entry type byte.
    /// </summary>
    internal RawUnixDirectoryEntryType FileType { get; }
}
