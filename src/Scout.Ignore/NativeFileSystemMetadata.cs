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
    private const uint UnixFileTypeMask = 0xF000;
    private const uint UnixFifo = 0x1000;
    private const uint UnixCharacterDevice = 0x2000;
    private const uint UnixDirectory = 0x4000;
    private const uint UnixRegularFile = 0x8000;
    private const uint UnixSymbolicLink = 0xA000;
    private const uint UnixSocket = 0xC000;

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

    public static bool TryGetUnixStatus(string path, bool followLinks, out NativeUnixFileStatus status)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            status = default;
            return false;
        }

        byte* linkStatus = stackalloc byte[StatBufferSize];
        if (LStat(path, linkStatus) != 0)
        {
            status = default;
            return false;
        }

        byte* effectiveStatus = linkStatus;
        if (followLinks)
        {
            byte* targetStatus = stackalloc byte[StatBufferSize];
            if (Stat(path, targetStatus) == 0)
            {
                effectiveStatus = targetStatus;
            }
        }

        uint? linkExpectedFileType = TryGetExpectedTextFileType(path, followLinks: false);
        uint? effectiveExpectedFileType = followLinks
            ? TryGetExpectedTextFileType(path, followLinks: true)
            : linkExpectedFileType;
        return TryCreateUnixStatus(linkStatus, effectiveStatus, linkExpectedFileType, effectiveExpectedFileType, out status);
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

            return TryCreateUnixStatus(linkStatus, effectiveStatus, null, null, out status);
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

        uint? expectedFileType = TryGetExpectedTextFileType(path, followLinks);
        if (!TrySelectUnixStatLayout(status, expectedFileType, out UnixStatLayout layout))
        {
            metadata = default;
            return false;
        }

        metadata = ReadMetadata(status, layout);
        return true;
    }

    private static bool TryCreateUnixStatus(
        byte* linkStatus,
        byte* effectiveStatus,
        uint? linkExpectedFileType,
        uint? effectiveExpectedFileType,
        out NativeUnixFileStatus status)
    {
        if (!TryReadUnixFileType(linkStatus, linkExpectedFileType, out uint linkFileType, out _) ||
            !TryReadUnixFileType(effectiveStatus, effectiveExpectedFileType, out uint effectiveFileType, out UnixStatLayout effectiveLayout))
        {
            status = default;
            return false;
        }

        FileAttributes attributes = BuildAttributes(effectiveFileType, linkFileType);
        long? length = effectiveFileType == UnixRegularFile ? ReadSize(effectiveStatus, effectiveLayout) : null;
        FileSystemMetadata metadata = ReadMetadata(effectiveStatus, effectiveLayout);
        status = new NativeUnixFileStatus(
            attributes,
            effectiveFileType == UnixDirectory,
            linkFileType == UnixSymbolicLink,
            length,
            metadata);
        return true;
    }

    private static FileSystemMetadata ReadMetadata(byte* status, UnixStatLayout layout)
    {
        ulong device = ReadDevice(status);
        ulong fileId = layout.InodeSize == sizeof(uint)
            ? BitConverter.ToUInt32(new ReadOnlySpan<byte>(status + layout.InodeOffset, sizeof(uint)))
            : BitConverter.ToUInt64(new ReadOnlySpan<byte>(status + layout.InodeOffset, sizeof(ulong)));
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

    private static bool TrySelectUnixStatLayout(byte* status, uint? expectedFileType, out UnixStatLayout layout)
    {
        if (TryReadUnixFileType(status, expectedFileType, out _, out layout))
        {
            return true;
        }

        layout = default;
        return false;
    }

    private static bool TryReadUnixFileType(byte* status, uint? expectedFileType, out uint fileType, out UnixStatLayout layout)
    {
        ReadOnlySpan<UnixStatLayout> layouts = UnixStatLayout.ForCurrentPlatform;
        if (expectedFileType.HasValue)
        {
            for (int index = 0; index < layouts.Length; index++)
            {
                uint expectedCandidate = ReadFileType(status, layouts[index]);
                if (expectedCandidate == expectedFileType.Value)
                {
                    fileType = expectedCandidate;
                    layout = layouts[index];
                    return true;
                }
            }
        }

        for (int index = 0; index < layouts.Length; index++)
        {
            uint candidate = ReadFileType(status, layouts[index]);
            if (candidate is UnixRegularFile or UnixDirectory or UnixSymbolicLink or UnixFifo or UnixCharacterDevice or UnixSocket)
            {
                fileType = candidate;
                layout = layouts[index];
                return true;
            }
        }

        fileType = 0;
        layout = default;
        return false;
    }

    private static uint ReadFileType(byte* status, UnixStatLayout layout)
    {
        uint mode = BitConverter.ToUInt32(new ReadOnlySpan<byte>(status + layout.ModeOffset, sizeof(uint)));
        return mode & UnixFileTypeMask;
    }

    private static uint? TryGetExpectedTextFileType(string path, bool followLinks)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if (!followLinks && (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return UnixSymbolicLink;
            }

            if ((attributes & FileAttributes.Directory) != 0 || (followLinks && Directory.Exists(path)))
            {
                return UnixDirectory;
            }

            return File.Exists(path) ? UnixRegularFile : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long ReadSize(byte* status, UnixStatLayout layout)
    {
        return BitConverter.ToInt64(new ReadOnlySpan<byte>(status + layout.SizeOffset, sizeof(long)));
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
