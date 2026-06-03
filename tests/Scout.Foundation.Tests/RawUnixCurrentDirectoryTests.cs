using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies raw Unix current-directory access.
/// </summary>
public sealed unsafe partial class RawUnixCurrentDirectoryTests
{
    /// <summary>
    /// Verifies the current directory is exposed as platform bytes.
    /// </summary>
    [Fact]
    public void GetReturnsCurrentDirectoryBytes()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            Assert.Throws<PlatformNotSupportedException>(RawUnixCurrentDirectory.Get);
        }
        else
        {
            string original = Directory.GetCurrentDirectory();
            string root = CreateTempDirectory();
            try
            {
                Directory.SetCurrentDirectory(root);

                byte[] actual = RawUnixCurrentDirectory.Get();

                Assert.Equal(Encoding.UTF8.GetBytes(Directory.GetCurrentDirectory()), actual);
                Assert.DoesNotContain((byte)0, actual);
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies Linux current-directory bytes are not decoded through UTF-8.
    /// </summary>
    [Fact]
    public void GetPreservesInvalidUtf8CurrentDirectoryOnLinux()
    {
        if (OperatingSystem.IsLinux())
        {
            string root = CreateTempDirectory();
            byte[] rootBytes = Encoding.UTF8.GetBytes(root);
            byte[] invalidName = [(byte)'c', 0xff, (byte)'w', (byte)'d'];
            byte[] invalidPath = JoinRawUnixPath(rootBytes, invalidName);
            int previousDirectory = OpenCurrentDirectory();

            try
            {
                RawUnixDirectory.Create(invalidPath);
                Assert.Equal(0, ChangeDirectory(invalidPath));

                byte[] actual = RawUnixCurrentDirectory.Get();

                Assert.Equal(invalidPath, actual);
            }
            finally
            {
                Assert.Equal(0, ChangeDirectory(previousDirectory));
                _ = Close(previousDirectory);
                _ = RemoveDirectory(invalidPath);
                Directory.Delete(root, recursive: true);
            }
        }
        else
        {
            Assert.True(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows());
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] JoinRawUnixPath(ReadOnlySpan<byte> parent, ReadOnlySpan<byte> name)
    {
        byte[] path = new byte[parent.Length + 1 + name.Length];
        parent.CopyTo(path);
        path[parent.Length] = (byte)'/';
        name.CopyTo(path.AsSpan(parent.Length + 1));
        return path;
    }

    private static int OpenCurrentDirectory()
    {
        byte[] currentDirectory = [(byte)'.', 0];
        fixed (byte* currentDirectoryPointer = currentDirectory)
        {
            int fileDescriptor = Open(currentDirectoryPointer, flags: 0);
            Assert.True(fileDescriptor >= 0, "Failed to open the current directory file descriptor.");
            return fileDescriptor;
        }
    }

    private static int ChangeDirectory(ReadOnlySpan<byte> path)
    {
        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            return ChDir(pathPointer);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    private static partial int Open(byte* path, int flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "chdir", SetLastError = true)]
    private static partial int ChDir(byte* path);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "fchdir", SetLastError = true)]
    private static partial int ChangeDirectory(int fileDescriptor);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fileDescriptor);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "rmdir", SetLastError = true)]
    private static partial int RemoveDirectory(byte* path);

    private static int RemoveDirectory(ReadOnlySpan<byte> path)
    {
        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            return RemoveDirectory(pathPointer);
        }
    }
}
