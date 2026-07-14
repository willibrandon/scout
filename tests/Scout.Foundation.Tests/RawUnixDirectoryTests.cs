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
    /// Verifies adjacent short directory-entry records preserve complete names.
    /// </summary>
    [Fact]
    public void EnumeratePreservesAdjacentShortEntryNames()
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
                string[] names =
                [
                    "a",
                    "bb",
                    "ccc",
                    "dddd",
                    "eeeee",
                    "ffffff",
                    "ggggggg",
                    "hhhhhhhh",
                    "iiiiiiiii",
                    "jjjjjjjjjj",
                    "kkkkkkkkkkk",
                ];
                for (int index = 0; index < names.Length; index++)
                {
                    File.WriteAllText(Path.Combine(root, names[index]), "needle");
                }

                RawUnixDirectoryEntry[] entries = RawUnixDirectory.Enumerate(Encoding.UTF8.GetBytes(root));
                Assert.Equal(names.Length, entries.Length);
                for (int index = 0; index < entries.Length; index++)
                {
                    Assert.DoesNotContain((byte)0, entries[index].Name.ToArray());
                }

                for (int index = 0; index < names.Length; index++)
                {
                    byte[] expectedName = Encoding.UTF8.GetBytes(names[index]);
                    RawUnixDirectoryEntry entry = Find(entries, expectedName);
                    Assert.Equal(expectedName, entry.Name.ToArray());
                    Assert.Equal(Encoding.UTF8.GetBytes(Path.Combine(root, names[index])), entry.FullPath.ToArray());
                    Assert.Equal(RawUnixDirectoryEntryType.RegularFile, entry.FileType);
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies enumeration retains native regular-file, directory, and symbolic-link types.
    /// </summary>
    [Fact]
    public void EnumeratePreservesNativeFileTypes()
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
                string file = Path.Combine(root, "file");
                string directory = Path.Combine(root, "directory");
                string link = Path.Combine(root, "link");
                File.WriteAllText(file, "needle");
                Directory.CreateDirectory(directory);
                File.CreateSymbolicLink(link, file);

                RawUnixDirectoryEntry[] entries = RawUnixDirectory.Enumerate(Encoding.UTF8.GetBytes(root));
                Assert.Equal(RawUnixDirectoryEntryType.RegularFile, Find(entries, "file"u8).FileType);
                Assert.Equal(RawUnixDirectoryEntryType.Directory, Find(entries, "directory"u8).FileType);
                Assert.Equal(RawUnixDirectoryEntryType.SymbolicLink, Find(entries, "link"u8).FileType);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies native directory read errors are not mistaken for end-of-directory.
    /// </summary>
    [Fact]
    public void ReadDirectoryFailureIsNotTreatedAsEndOfDirectory()
    {
        RawUnixDirectory.ThrowIfReadDirectoryFailed(error: 0);

        Assert.Throws<IOException>(() => RawUnixDirectory.ThrowIfReadDirectoryFailed(error: 5));
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

    private static RawUnixDirectoryEntry Find(
        ReadOnlySpan<RawUnixDirectoryEntry> entries,
        ReadOnlySpan<byte> name)
    {
        for (int index = 0; index < entries.Length; index++)
        {
            if (entries[index].Name.Span.SequenceEqual(name))
            {
                return entries[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"Raw directory entry '{Encoding.UTF8.GetString(name)}' was not found.");
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
