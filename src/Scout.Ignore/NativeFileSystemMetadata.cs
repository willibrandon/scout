using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

internal static unsafe partial class NativeFileSystemMetadata
{
    private const int StatBufferSize = 512;
    private const int ReadLinkBufferSize = 4096;
    private const int UnixDeviceOffset = 0;
    private const int UnixInodeOffset = 8;
    private const int MacOSModeOffset = 4;
    private const int MacOSSizeOffset = 96;
    private const int LinuxSizeOffset = 48;
    private const uint UnixFileTypeMask = 0xF000;
    private const uint UnixDirectory = 0x4000;
    private const uint UnixRegularFile = 0x8000;
    private const uint UnixSymbolicLink = 0xA000;

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

    public static bool TryReadRawUnixLinkTarget(ReadOnlySpan<byte> path, out byte[] target)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            target = [];
            return false;
        }

        if (path.IsEmpty || path.Contains((byte)0))
        {
            target = [];
            return false;
        }

        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            byte* buffer = stackalloc byte[ReadLinkBufferSize];
            nint length = ReadLinkRaw(pathPointer, buffer, (nuint)ReadLinkBufferSize);
            if (length >= 0)
            {
                target = new ReadOnlySpan<byte>(buffer, (int)length).ToArray();
                return true;
            }
        }

        target = [];
        return false;
    }

    public static bool TryGetRawUnixStatus(ReadOnlySpan<byte> path, bool followLinks, out NativeUnixFileStatus status)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            status = default;
            return false;
        }

        if (path.IsEmpty || path.Contains((byte)0))
        {
            status = default;
            return false;
        }

        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            byte* linkStatus = stackalloc byte[StatBufferSize];
            if (LStatRaw(pathPointer, linkStatus) != 0)
            {
                status = default;
                return false;
            }

            byte* effectiveStatus = linkStatus;
            if (followLinks)
            {
                byte* targetStatus = stackalloc byte[StatBufferSize];
                if (StatRaw(pathPointer, targetStatus) == 0)
                {
                    effectiveStatus = targetStatus;
                }
            }

            if (!TryReadUnixFileType(linkStatus, out uint linkFileType) ||
                !TryReadUnixFileType(effectiveStatus, out uint effectiveFileType))
            {
                status = default;
                return false;
            }

            FileAttributes attributes = BuildAttributes(effectiveFileType, linkFileType);
            long? length = effectiveFileType == UnixRegularFile ? ReadSize(effectiveStatus) : null;
            FileSystemMetadata metadata = ReadMetadata(effectiveStatus);
            status = new NativeUnixFileStatus(
                attributes,
                effectiveFileType == UnixDirectory,
                linkFileType == UnixSymbolicLink,
                length,
                metadata);
            return true;
        }
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

        metadata = ReadMetadata(status);
        return true;
    }

    private static FileSystemMetadata ReadMetadata(byte* status)
    {
        ulong device = ReadDevice(status);
        ulong fileId = BitConverter.ToUInt64(new ReadOnlySpan<byte>(status + UnixInodeOffset, sizeof(ulong)));
        return new FileSystemMetadata(FileSystemDevice.FromUInt64(device), fileId);
    }

    private static ulong ReadDevice(byte* status)
    {
        if (OperatingSystem.IsMacOS())
        {
            return BitConverter.ToUInt32(new ReadOnlySpan<byte>(status + UnixDeviceOffset, sizeof(uint)));
        }

        return BitConverter.ToUInt64(new ReadOnlySpan<byte>(status + UnixDeviceOffset, sizeof(ulong)));
    }

    private static bool TryReadUnixFileType(byte* status, out uint fileType)
    {
        ReadOnlySpan<int> modeOffsets = OperatingSystem.IsMacOS()
            ? [MacOSModeOffset]
            : [16, 24];
        for (int index = 0; index < modeOffsets.Length; index++)
        {
            uint mode = BitConverter.ToUInt32(new ReadOnlySpan<byte>(status + modeOffsets[index], sizeof(uint)));
            uint candidate = mode & UnixFileTypeMask;
            if (candidate is UnixRegularFile or UnixDirectory or UnixSymbolicLink or 0x1000 or 0x2000 or 0xC000)
            {
                fileType = candidate;
                return true;
            }
        }

        fileType = 0;
        return false;
    }

    private static long ReadSize(byte* status)
    {
        int offset = OperatingSystem.IsMacOS() ? MacOSSizeOffset : LinuxSizeOffset;
        return BitConverter.ToInt64(new ReadOnlySpan<byte>(status + offset, sizeof(long)));
    }

    private static FileAttributes BuildAttributes(uint effectiveFileType, uint linkFileType)
    {
        FileAttributes attributes = default;
        if (effectiveFileType == UnixDirectory)
        {
            attributes |= FileAttributes.Directory;
        }

        if (linkFileType == UnixSymbolicLink)
        {
            attributes |= FileAttributes.ReparsePoint;
        }

        return attributes;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(
        "libc",
        EntryPoint = "stat",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Stat(string path, byte* status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(
        "libc",
        EntryPoint = "lstat",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStat(string path, byte* status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static partial int StatRaw(byte* path, byte* status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static partial int LStatRaw(byte* path, byte* status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(
        "libc",
        EntryPoint = "readlink",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint ReadLink(string path, byte* buffer, nuint bufferLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "readlink", SetLastError = true)]
    private static partial nint ReadLinkRaw(byte* path, byte* buffer, nuint bufferLength);
}
