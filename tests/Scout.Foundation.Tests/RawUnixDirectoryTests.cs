using System;
using System.IO;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies raw Unix directory enumeration.
/// </summary>
public sealed class RawUnixDirectoryTests
{
    /// <summary>
    /// Verifies directory entries preserve raw name and full path bytes.
    /// </summary>
    [Fact]
    public void EnumeratePreservesNameAndFullPathBytes()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            return;
        }

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

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
