using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

internal static unsafe class NativeFileSystemMetadata
{
    private const int StatBufferSize = 512;
    private const int ReadLinkBufferSize = 4096;
    private const int UnixDeviceOffset = 0;
    private const int UnixInodeOffset = 8;

    public static bool TryGet(string path, bool followLinks, out FileSystemMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return TryGetUnix(path, followLinks, out metadata);
        }

        metadata = default;
        return false;
    }

    public static bool TryGetDevice(string path, out FileSystemDevice device)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (TryGet(path, followLinks: true, out FileSystemMetadata metadata))
        {
            device = metadata.Device;
            return !device.IsEmpty;
        }

        if (OperatingSystem.IsWindows())
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(root))
            {
                device = FileSystemDevice.FromKey(root.ToUpperInvariant());
                return true;
            }
        }

        device = default;
        return false;
    }

    public static bool TryReadLinkTarget(string path, out string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            byte* buffer = stackalloc byte[ReadLinkBufferSize];
            nint length = ReadLink(path, buffer, (nuint)ReadLinkBufferSize);
            if (length >= 0)
            {
                target = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, (int)length));
                return true;
            }
        }

        target = string.Empty;
        return false;
    }

    private static bool TryGetUnix(string path, bool followLinks, out FileSystemMetadata metadata)
    {
        byte* status = stackalloc byte[StatBufferSize];
        int result = followLinks ? Stat(path, status) : LStat(path, status);
        if (result != 0)
        {
            metadata = default;
            return false;
        }

        ulong device = ReadDevice(status);
        ulong fileId = BitConverter.ToUInt64(new ReadOnlySpan<byte>(status + UnixInodeOffset, sizeof(ulong)));
        metadata = new FileSystemMetadata(FileSystemDevice.FromUInt64(device), fileId);
        return true;
    }

    private static ulong ReadDevice(byte* status)
    {
        if (OperatingSystem.IsMacOS())
        {
            return BitConverter.ToUInt32(new ReadOnlySpan<byte>(status + UnixDeviceOffset, sizeof(uint)));
        }

        return BitConverter.ToUInt64(new ReadOnlySpan<byte>(status + UnixDeviceOffset, sizeof(ulong)));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(
        "libc",
        EntryPoint = "stat",
        ExactSpelling = true,
        SetLastError = true,
        CharSet = CharSet.Ansi,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true)]
    private static extern int Stat(string path, byte* status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(
        "libc",
        EntryPoint = "lstat",
        ExactSpelling = true,
        SetLastError = true,
        CharSet = CharSet.Ansi,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true)]
    private static extern int LStat(string path, byte* status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(
        "libc",
        EntryPoint = "readlink",
        ExactSpelling = true,
        SetLastError = true,
        CharSet = CharSet.Ansi,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true)]
    private static extern nint ReadLink(string path, byte* buffer, nuint bufferLength);
}
