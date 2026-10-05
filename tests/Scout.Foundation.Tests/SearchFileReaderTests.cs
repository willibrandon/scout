using System.Text;

namespace Scout;

/// <summary>
/// Verifies search-file reader mmap selection and decoding.
/// </summary>
[TestClass]
public sealed class SearchFileReaderTests
{
    /// <summary>
    /// Verifies explicit no-mmap mode uses buffered reads and still applies search encoding.
    /// </summary>
    [TestMethod]
    public void ReadNeverUsesBufferedReaderAndDecodes()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "utf16.txt");
            File.WriteAllBytes(path, [0xFF, 0xFE, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0]);

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.Auto, SearchMmapMode.Never, allowMemoryMap: true);

            Assert.AreEqual(SearchFileReadKind.Buffered, result.Kind);
            Assert.AreSequenceEqual("needle\n"u8.ToArray(), result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies forced mmap mode uses the platform-compatible mmap strategy.
    /// </summary>
    [TestMethod]
    public void ReadAlwaysTryMmapUsesPlatformStrategy()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            File.WriteAllText(path, "needle\n");

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.AlwaysTryMmap, allowMemoryMap: false);

            Assert.AreEqual(GetExpectedMemoryMappedKind(), result.Kind);
            Assert.AreSequenceEqual("needle\n"u8.ToArray(), result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies automatic mmap mode honors the caller's upstream path eligibility decision.
    /// </summary>
    [TestMethod]
    public void ReadAutoHonorsEligibility()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            File.WriteAllText(path, "needle\n");

            SearchFileReadResult disallowed = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.Auto, allowMemoryMap: false);
            SearchFileReadResult allowed = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.Auto, allowMemoryMap: true);

            Assert.AreEqual(SearchFileReadKind.Buffered, disallowed.Kind);
            Assert.AreEqual(GetExpectedMemoryMappedKind(), allowed.Kind);
            Assert.AreSequenceEqual("needle\n"u8.ToArray(), disallowed.GetBytes());
            Assert.AreSequenceEqual("needle\n"u8.ToArray(), allowed.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies empty files fall back to buffered reads.
    /// </summary>
    [TestMethod]
    public void ReadEmptyFileUsesBufferedReader()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "empty.txt");
            File.WriteAllBytes(path, []);

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.AlwaysTryMmap, allowMemoryMap: true);

            Assert.AreEqual(SearchFileReadKind.Buffered, result.Kind);
            Assert.IsEmpty(result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies buffered reads can reuse directory-walk length metadata.
    /// </summary>
    [TestMethod]
    public void ReadBufferedUsesKnownLength()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            byte[] expected = "needle\n"u8.ToArray();
            File.WriteAllBytes(path, expected);

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.Never, allowMemoryMap: true, knownLength: expected.Length);

            Assert.AreEqual(SearchFileReadKind.Buffered, result.Kind);
            Assert.AreSequenceEqual(expected, result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies Unix raw-byte path reads bypass text path APIs.
    /// </summary>
    [TestMethod]
    public void ReadUnixPathUsesRawBytePath()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.ThrowsExactly<PlatformNotSupportedException>(() => SearchFileReader.ReadUnixPath("unused"u8, SearchEncodingKind.None));
        }
        else
        {
            string root = CreateTempDirectory();
            try
            {
                string path = Path.Join(root, "input.txt");
                File.WriteAllText(path, "needle\n");
                byte[] pathBytes = Encoding.UTF8.GetBytes(path);

                SearchFileReadResult result = SearchFileReader.ReadUnixPath(pathBytes, SearchEncodingKind.None);

                Assert.AreEqual(SearchFileReadKind.Buffered, result.Kind);
                Assert.AreSequenceEqual("needle\n"u8.ToArray(), result.GetBytes());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static SearchFileReadKind GetExpectedMemoryMappedKind()
    {
        return OperatingSystem.IsMacOS()
            ? SearchFileReadKind.Buffered
            : SearchFileReadKind.MemoryMapped;
    }

    private static string CreateTempDirectory()
    {
        string root = Path.Join(Path.GetTempPath(), $"scout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
