using System.IO;

namespace Scout;

internal readonly struct NativeUnixFileStatus
{
    public NativeUnixFileStatus(
        FileAttributes attributes,
        bool isDirectory,
        bool isSymbolicLink,
        long? length,
        FileSystemMetadata metadata)
    {
        Attributes = attributes;
        IsDirectory = isDirectory;
        IsSymbolicLink = isSymbolicLink;
        Length = length;
        Metadata = metadata;
    }

    public FileAttributes Attributes { get; }

    public bool IsDirectory { get; }

    public bool IsSymbolicLink { get; }

    public long? Length { get; }

    public FileSystemMetadata Metadata { get; }
}
