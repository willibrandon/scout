using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Enumerates Unix directories from raw bytes without decoding entry names as UTF-8.
/// </summary>
public static unsafe partial class RawUnixDirectory
{
    private const int LinuxDirentNameOffset = 19;
    private const int MacOSDirentNameOffset = 21;
    private const int MacOSDirentNameLengthOffset = 18;
    private const int MacOSDirentFileTypeOffset = 20;
    private const int DirentRecordLengthOffset = 16;
    private const uint DirectoryMode = 0x1FF;

    /// <summary>
    /// Creates a Unix directory from raw path bytes.
    /// </summary>
    /// <param name="path">The non-NUL-terminated raw directory path bytes.</param>
    /// <exception cref="ArgumentException">The path is empty or contains a NUL byte.</exception>
    /// <exception cref="IOException">The directory cannot be created.</exception>
    /// <exception cref="PlatformNotSupportedException">The current platform is Windows.</exception>
    public static void Create(ReadOnlySpan<byte> path)
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException("Raw Unix directory creation is only available on Linux and macOS.");
        }

        ValidatePath(path);
        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            if (MkDir(pathPointer, DirectoryMode) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new IOException(new Win32Exception(error).Message);
            }
        }
    }

    /// <summary>
    /// Enumerates a raw Unix directory path.
    /// </summary>
    /// <param name="path">The non-NUL-terminated raw directory path bytes.</param>
    /// <returns>The raw directory entries.</returns>
    /// <exception cref="ArgumentException">The path is empty or contains a NUL byte.</exception>
    /// <exception cref="IOException">The path cannot be opened.</exception>
    /// <exception cref="PlatformNotSupportedException">The current platform is Windows.</exception>
    public static RawUnixDirectoryEntry[] Enumerate(ReadOnlySpan<byte> path)
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException("Raw Unix directory enumeration is only available on Linux and macOS.");
        }

        ValidatePath(path);
        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            nint directory = OpenDir(pathPointer);
            if (directory == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new IOException(new Win32Exception(error).Message);
            }

            try
            {
                return EnumerateOpenDirectory(directory, path);
            }
            finally
            {
                _ = CloseDir(directory);
            }
        }
    }

    private static void ValidatePath(ReadOnlySpan<byte> path)
    {
        if (path.IsEmpty)
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        if (path.Contains((byte)0))
        {
            throw new ArgumentException("Unix paths cannot contain a NUL byte.", nameof(path));
        }
    }

    private static RawUnixDirectoryEntry[] EnumerateOpenDirectory(nint directory, ReadOnlySpan<byte> parentPath)
    {
        var entries = new List<RawUnixDirectoryEntry>();
        byte[] ownedParentPath = parentPath.ToArray();
        while (true)
        {
            nint entryPointer = ReadDir(directory);
            if (entryPointer == 0)
            {
                ThrowIfReadDirectoryFailed(Marshal.GetLastPInvokeError());
                return entries.ToArray();
            }

            byte[] name = ReadName(entryPointer, out RawUnixDirectoryEntryType fileType);
            if (IsCurrentOrParent(name))
            {
                continue;
            }

            entries.Add(new RawUnixDirectoryEntry(name, ownedParentPath, fileType));
        }
    }

    /// <summary>
    /// Throws when a native directory read ended because of an error rather than end-of-directory.
    /// </summary>
    /// <param name="error">The captured native error code.</param>
    internal static void ThrowIfReadDirectoryFailed(int error)
    {
        if (error != 0)
        {
            throw new IOException(new Win32Exception(error).Message);
        }
    }

    private static byte[] ReadName(
        nint entryPointer,
        out RawUnixDirectoryEntryType fileType)
    {
        byte* entry = (byte*)entryPointer;
        if (OperatingSystem.IsMacOS())
        {
            int darwinNameLength = BitConverter.ToUInt16(
                new ReadOnlySpan<byte>(entry + MacOSDirentNameLengthOffset, sizeof(ushort)));
            fileType = (RawUnixDirectoryEntryType)entry[MacOSDirentFileTypeOffset];
            return new ReadOnlySpan<byte>(entry + MacOSDirentNameOffset, darwinNameLength).ToArray();
        }

        int recordLength = BitConverter.ToUInt16(new ReadOnlySpan<byte>(entry + DirentRecordLengthOffset, sizeof(ushort)));
        int maxNameLength = Math.Max(0, recordLength - LinuxDirentNameOffset);
        int nameLength = 0;
        while (nameLength < maxNameLength && entry[LinuxDirentNameOffset + nameLength] != 0)
        {
            nameLength++;
        }

        fileType = (RawUnixDirectoryEntryType)entry[LinuxDirentNameOffset - 1];
        return new ReadOnlySpan<byte>(entry + LinuxDirentNameOffset, nameLength).ToArray();
    }

    private static nint ReadDir(nint directory)
    {
        if (OperatingSystem.IsMacOS() &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return ReadDirMacOSInode64(directory);
        }

        return ReadDirDefault(directory);
    }

    private static nint OpenDir(byte* path)
    {
        if (OperatingSystem.IsMacOS() &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return OpenDirMacOSInode64(path);
        }

        return OpenDirDefault(path);
    }

    private static bool IsCurrentOrParent(ReadOnlySpan<byte> name)
    {
        return name is [(byte)'.'] or [(byte)'.', (byte)'.'];
    }

    /// <summary>
    /// Joins raw Unix parent-path and entry-name bytes.
    /// </summary>
    /// <param name="parentPath">The raw parent path.</param>
    /// <param name="name">The raw entry name.</param>
    /// <returns>The joined raw path.</returns>
    internal static byte[] Join(ReadOnlySpan<byte> parentPath, ReadOnlySpan<byte> name)
    {
        bool needsSeparator = parentPath[^1] != (byte)'/';
        byte[] path = new byte[parentPath.Length + (needsSeparator ? 1 : 0) + name.Length];
        parentPath.CopyTo(path);
        int offset = parentPath.Length;
        if (needsSeparator)
        {
            path[offset] = (byte)'/';
            offset++;
        }

        name.CopyTo(path.AsSpan(offset));
        return path;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "opendir", SetLastError = true)]
    private static partial nint OpenDirDefault(byte* path);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "opendir$INODE64", SetLastError = true)]
    private static partial nint OpenDirMacOSInode64(byte* path);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "mkdir", SetLastError = true)]
    private static partial int MkDir(byte* path, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "readdir", SetLastError = true)]
    private static partial nint ReadDirDefault(nint directory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)]
    private static partial nint ReadDirMacOSInode64(nint directory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "closedir", SetLastError = true)]
    private static partial int CloseDir(nint directory);
}
