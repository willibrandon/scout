using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Implements ripgrep-style heuristic detection for whether stdin should be searched implicitly.
/// </summary>
public static unsafe partial class StandardInputProbe
{
    private const int StandardInputFileDescriptor = 0;
    private const int StandardInputHandle = -10;
    private const int StatBufferSize = 512;
    private const uint UnixFileTypeMask = 0xF000;
    private const uint UnixFifo = 0x1000;
    private const uint UnixRegularFile = 0x8000;
    private const uint UnixSocket = 0xC000;
    private const uint WindowsFileTypeDisk = 0x0001;
    private const uint WindowsFileTypePipe = 0x0003;

    /// <summary>
    /// Returns true when stdin appears to be a regular file, FIFO/pipe or socket.
    /// </summary>
    /// <returns><see langword="true" /> when stdin should be searched implicitly.</returns>
    public static bool IsReadable()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsReadableWindows();
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return IsReadableUnix();
        }

        return false;
    }

    private static bool IsReadableUnix()
    {
        if (IsATty(StandardInputFileDescriptor) == 1)
        {
            return false;
        }

        byte* status = stackalloc byte[StatBufferSize];
        if (FStat(StandardInputFileDescriptor, status) != 0)
        {
            return false;
        }

        return TryReadUnixFileType(status, out uint fileType) &&
            fileType is UnixRegularFile or UnixFifo or UnixSocket;
    }

    private static bool TryReadUnixFileType(byte* status, out uint fileType)
    {
        ReadOnlySpan<int> modeOffsets = UnixModeOffsetsForCurrentPlatform;
        for (int index = 0; index < modeOffsets.Length; index++)
        {
            uint mode = BitConverter.ToUInt32(new ReadOnlySpan<byte>(status + modeOffsets[index], sizeof(uint)));
            uint candidate = mode & UnixFileTypeMask;
            if (candidate is UnixRegularFile or UnixFifo or UnixSocket or 0x2000 or 0x4000 or 0xA000)
            {
                fileType = candidate;
                return true;
            }
        }

        fileType = 0;
        return false;
    }

    private static ReadOnlySpan<int> UnixModeOffsetsForCurrentPlatform =>
        OperatingSystem.IsMacOS()
            ? MacOSModeOffsets
            : [16, 24];

    private static ReadOnlySpan<int> MacOSModeOffsets =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? [8, 4]
            : [4, 8];

    private static bool IsReadableWindows()
    {
        IntPtr handle = GetStdHandle(StandardInputHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return false;
        }

        uint fileType = GetFileType(handle);
        return fileType is WindowsFileTypeDisk or WindowsFileTypePipe;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "isatty", SetLastError = true)]
    private static partial int IsATty(int fileDescriptor);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int FStat(int fileDescriptor, byte* status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(IntPtr handle);
}
