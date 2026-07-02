
namespace Scout.IO.Ignore;

internal readonly struct FileSystemMetadata : IEquatable<FileSystemMetadata>
{
    public FileSystemMetadata(FileSystemDevice device, ulong fileId)
    {
        Device = device;
        FileId = fileId;
    }

    public FileSystemDevice Device { get; }

    public ulong FileId { get; }

    public bool IsEmpty => Device.IsEmpty && FileId == 0;

    public bool Equals(FileSystemMetadata other)
    {
        return Device.Equals(other.Device) && FileId == other.FileId;
    }

    public override bool Equals(object? obj)
    {
        return obj is FileSystemMetadata other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Device, FileId);
    }
}
