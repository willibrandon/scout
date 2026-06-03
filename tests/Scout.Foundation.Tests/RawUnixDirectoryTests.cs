using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies raw Unix directory enumeration.
/// </summary>
public sealed unsafe partial class RawUnixDirectoryTests
{
    /// <summary>
    /// Verifies directory entries preserve raw name and full path bytes.
    /// </summary>
    [Fact]
    public void EnumeratePreservesNameAndFullPathBytes()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            Assert.Throws<PlatformNotSupportedException>(() => RawUnixDirectory.Enumerate("unused"u8));
        }
        else
        {
            string root = CreateTempDirectory();
            try
            {
                string file = Path.Combine(root, "alpha.txt");
                File.WriteAllText(file, "needle");

                RawUnixDirectoryEntry[] entries = RawUnixDirectory.Enumerate(Encoding.UTF8.GetBytes(root));
                byte[] name = "alpha.txt"u8.ToArray();
                byte[] fullPath = Encoding.UTF8.GetBytes(file);
                bool found = false;
                for (int index = 0; index < entries.Length; index++)
                {
                    if (entries[index].Name.Span.SequenceEqual(name) &&
                        entries[index].FullPath.Span.SequenceEqual(fullPath))
                    {
                        found = true;
                        break;
                    }
                }

                Assert.True(found);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies raw Unix symlink target reads preserve invalid UTF-8 bytes.
    /// </summary>
    [Fact]
    public void ReadLinkTargetPreservesRawUnixBytes()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            Assert.False(NativeFileSystemMetadata.TryReadRawUnixLinkTarget("unused"u8, out byte[] unsupportedTarget));
            Assert.Empty(unsupportedTarget);
        }
        else
        {
            string root = CreateTempDirectory();
            try
            {
                byte[] linkPath = JoinRawUnixPath(Encoding.UTF8.GetBytes(root), "link"u8);
                byte[] target = [(byte)'t', (byte)'a', 0xff, (byte)'g', (byte)'e', (byte)'t'];

                Assert.True(TryCreateRawUnixSymlink(target, linkPath));
                Assert.True(NativeFileSystemMetadata.TryReadRawUnixLinkTarget(linkPath, out byte[] actual));
                Assert.Equal(target, actual);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
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

    private static bool TryCreateRawUnixSymlink(ReadOnlySpan<byte> target, ReadOnlySpan<byte> linkPath)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        byte[] terminatedTarget = new byte[target.Length + 1];
        target.CopyTo(terminatedTarget);
        byte[] terminatedLinkPath = new byte[linkPath.Length + 1];
        linkPath.CopyTo(terminatedLinkPath);
        fixed (byte* targetPointer = terminatedTarget)
        fixed (byte* linkPathPointer = terminatedLinkPath)
        {
            return Symlink(targetPointer, linkPathPointer) == 0;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "symlink", SetLastError = true)]
    private static partial int Symlink(byte* target, byte* linkPath);
}
