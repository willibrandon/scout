using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies raw Unix directory enumeration.
/// </summary>
[TestClass]
public sealed unsafe partial class RawUnixDirectoryTests
{
    /// <summary>
    /// Verifies a terminal native read error retains entries read before the failure.
    /// </summary>
    [TestMethod]
    public void BufferedEnumerationRetainsEntriesBeforeReadFailure()
    {
        byte[] record = new byte[64];
        int nameOffset = OperatingSystem.IsMacOS() ? 21 : 19;
        BitConverter.TryWriteBytes(record.AsSpan(16), (ushort)record.Length);
        if (OperatingSystem.IsMacOS())
        {
            BitConverter.TryWriteBytes(record.AsSpan(18), (ushort)5);
            record[20] = (byte)RawUnixDirectoryEntryType.RegularFile;
        }
        else
        {
            record[18] = (byte)RawUnixDirectoryEntryType.RegularFile;
        }

        "entry"u8.CopyTo(record.AsSpan(nameOffset));
        fixed (byte* pointer = record)
        {
            nint address = (nint)pointer;
            int reads = 0;
            (RawUnixDirectoryEntry[] entries, IOException? error) = RawUnixDirectory.EnumerateOpenDirectory(
                0,
                "/root"u8,
                _ =>
                {
                    Marshal.SetLastPInvokeError(reads++ == 0 ? 0 : 5);
                    return reads == 1 ? address : 0;
                });
            RawUnixDirectoryEntry entry = Assert.ContainsSingle(entries);
            Assert.AreSequenceEqual("entry"u8.ToArray(), entry.Name.ToArray());
            Assert.AreSequenceEqual("/root/entry"u8.ToArray(), entry.FullPath.ToArray());
            Assert.IsNotNull(error);
            System.ComponentModel.Win32Exception cause = Assert.IsExactInstanceOfType<System.ComponentModel.Win32Exception>(error.InnerException);
            Assert.AreEqual(5, cause.NativeErrorCode);
        }
    }

    /// <summary>
    /// Verifies directory entries preserve raw name and full path bytes.
    /// </summary>
    [TestMethod]
    public void EnumeratePreservesNameAndFullPathBytes()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            Assert.ThrowsExactly<PlatformNotSupportedException>(() => RawUnixDirectory.Enumerate("unused"u8));
        }
        else
        {
            string root = CreateTempDirectory();
            try
            {
                string file = Path.Join(root, "alpha.txt");
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

                Assert.IsTrue(found);
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
    [TestMethod]
    public void EnumeratePreservesAdjacentShortEntryNames()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            Assert.ThrowsExactly<PlatformNotSupportedException>(() => RawUnixDirectory.Enumerate("unused"u8));
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
                    File.WriteAllText(Path.Join(root, names[index]), "needle");
                }

                RawUnixDirectoryEntry[] entries = RawUnixDirectory.Enumerate(Encoding.UTF8.GetBytes(root));
                Assert.HasCount(names.Length, entries);
                for (int index = 0; index < entries.Length; index++)
                {
                    Assert.DoesNotContain((byte)0, entries[index].Name.ToArray());
                }

                for (int index = 0; index < names.Length; index++)
                {
                    byte[] expectedName = Encoding.UTF8.GetBytes(names[index]);
                    RawUnixDirectoryEntry entry = Find(entries, expectedName);
                    Assert.AreSequenceEqual(expectedName, entry.Name.ToArray());
                    Assert.AreSequenceEqual(Encoding.UTF8.GetBytes(Path.Join(root, names[index])), entry.FullPath.ToArray());
                    Assert.AreEqual(RawUnixDirectoryEntryType.RegularFile, entry.FileType);
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
    [TestMethod]
    public void EnumeratePreservesNativeFileTypes()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            Assert.ThrowsExactly<PlatformNotSupportedException>(() => RawUnixDirectory.Enumerate("unused"u8));
        }
        else
        {
            string root = CreateTempDirectory();
            try
            {
                string file = Path.Join(root, "file");
                string directory = Path.Join(root, "directory");
                string link = Path.Join(root, "link");
                File.WriteAllText(file, "needle");
                Directory.CreateDirectory(directory);
                File.CreateSymbolicLink(link, file);

                RawUnixDirectoryEntry[] entries = RawUnixDirectory.Enumerate(Encoding.UTF8.GetBytes(root));
                Assert.AreEqual(RawUnixDirectoryEntryType.RegularFile, Find(entries, "file"u8).FileType);
                Assert.AreEqual(RawUnixDirectoryEntryType.Directory, Find(entries, "directory"u8).FileType);
                Assert.AreEqual(RawUnixDirectoryEntryType.SymbolicLink, Find(entries, "link"u8).FileType);
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
    [TestMethod]
    public void ReadDirectoryFailureIsNotTreatedAsEndOfDirectory()
    {
        RawUnixDirectory.ThrowIfReadDirectoryFailed(error: 0);

        Assert.ThrowsExactly<IOException>(() => RawUnixDirectory.ThrowIfReadDirectoryFailed(error: 5));
    }

    /// <summary>
    /// Verifies raw Unix symlink target reads preserve invalid UTF-8 bytes.
    /// </summary>
    [TestMethod]
    public void ReadLinkTargetPreservesRawUnixBytes()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            Assert.IsFalse(NativeFileSystemMetadata.TryReadRawUnixLinkTarget("unused"u8, out byte[] unsupportedTarget));
            Assert.IsEmpty(unsupportedTarget);
        }
        else
        {
            string root = CreateTempDirectory();
            try
            {
                byte[] linkPath = JoinRawUnixPath(Encoding.UTF8.GetBytes(root), "link"u8);
                byte[] target = [(byte)'t', (byte)'a', 0xff, (byte)'g', (byte)'e', (byte)'t'];

                Assert.IsTrue(TryCreateRawUnixSymlink(target, linkPath));
                Assert.IsTrue(NativeFileSystemMetadata.TryReadRawUnixLinkTarget(linkPath, out byte[] actual));
                Assert.AreSequenceEqual(target, actual);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Join(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
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

        throw new AssertFailedException($"Raw directory entry '{Encoding.UTF8.GetString(name)}' was not found.");
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
