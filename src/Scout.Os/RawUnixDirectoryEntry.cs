
namespace Scout;

/// <summary>
/// Represents one raw Unix directory entry.
/// </summary>
public sealed class RawUnixDirectoryEntry
{
    internal RawUnixDirectoryEntry(byte[] name, byte[] fullPath)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fullPath);

        Name = name;
        FullPath = fullPath;
    }

    /// <summary>
    /// Gets the raw entry name bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Name { get; }

    /// <summary>
    /// Gets the raw full path bytes.
    /// </summary>
    public ReadOnlyMemory<byte> FullPath { get; }
}
