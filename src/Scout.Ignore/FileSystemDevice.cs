using System.Globalization;

namespace Scout;

internal readonly struct FileSystemDevice : IEquatable<FileSystemDevice>
{
    private readonly string key;

    private FileSystemDevice(string key)
    {
        this.key = key;
    }

    public bool IsEmpty => Key.Length == 0;

    private string Key => key ?? string.Empty;

    public static FileSystemDevice FromKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new FileSystemDevice(key);
    }

    public static FileSystemDevice FromUInt64(ulong value)
    {
        return new FileSystemDevice(value.ToString(CultureInfo.InvariantCulture));
    }

    public bool Equals(FileSystemDevice other)
    {
        return StringComparer.Ordinal.Equals(Key, other.Key);
    }

    public override bool Equals(object? obj)
    {
        return obj is FileSystemDevice other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Key);
    }
}
