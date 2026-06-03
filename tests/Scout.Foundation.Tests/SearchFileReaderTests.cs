using System.Text;

namespace Scout;

/// <summary>
/// Verifies search-file reader mmap selection and decoding.
/// </summary>
public sealed class SearchFileReaderTests
{
    /// <summary>
    /// Verifies explicit no-mmap mode uses buffered reads and still applies search encoding.
    /// </summary>
    [Fact]
    public void ReadNeverUsesBufferedReaderAndDecodes()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "utf16.txt");
            File.WriteAllBytes(path, [0xFF, 0xFE, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0]);

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.Auto, SearchMmapMode.Never, allowMemoryMap: true);

            Assert.Equal(SearchFileReadKind.Buffered, result.Kind);
            Assert.Equal("needle\n"u8.ToArray(), result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies forced mmap mode uses the platform-compatible mmap strategy.
    /// </summary>
    [Fact]
    public void ReadAlwaysTryMmapUsesPlatformStrategy()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllText(path, "needle\n");

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.AlwaysTryMmap, allowMemoryMap: false);

            Assert.Equal(GetExpectedMemoryMappedKind(), result.Kind);
            Assert.Equal("needle\n"u8.ToArray(), result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies automatic mmap mode honors the caller's upstream path eligibility decision.
    /// </summary>
    [Fact]
    public void ReadAutoHonorsEligibility()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllText(path, "needle\n");

            SearchFileReadResult disallowed = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.Auto, allowMemoryMap: false);
            SearchFileReadResult allowed = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.Auto, allowMemoryMap: true);

            Assert.Equal(SearchFileReadKind.Buffered, disallowed.Kind);
            Assert.Equal(GetExpectedMemoryMappedKind(), allowed.Kind);
            Assert.Equal("needle\n"u8.ToArray(), disallowed.GetBytes());
            Assert.Equal("needle\n"u8.ToArray(), allowed.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies empty files fall back to buffered reads.
    /// </summary>
    [Fact]
    public void ReadEmptyFileUsesBufferedReader()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "empty.txt");
            File.WriteAllBytes(path, []);

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.AlwaysTryMmap, allowMemoryMap: true);

            Assert.Equal(SearchFileReadKind.Buffered, result.Kind);
            Assert.Empty(result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies buffered reads can reuse directory-walk length metadata.
    /// </summary>
    [Fact]
    public void ReadBufferedUsesKnownLength()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            byte[] expected = "needle\n"u8.ToArray();
            File.WriteAllBytes(path, expected);

            SearchFileReadResult result = SearchFileReader.Read(path, SearchEncodingKind.None, SearchMmapMode.Never, allowMemoryMap: true, knownLength: expected.Length);

            Assert.Equal(SearchFileReadKind.Buffered, result.Kind);
            Assert.Equal(expected, result.GetBytes());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies Unix raw-byte path reads bypass text path APIs.
    /// </summary>
    [Fact]
    public void ReadUnixPathUsesRawBytePath()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => SearchFileReader.ReadUnixPath("unused"u8, SearchEncodingKind.None));
        }
        else
        {
            string root = CreateTempDirectory();
            try
            {
                string path = Path.Combine(root, "input.txt");
                File.WriteAllText(path, "needle\n");
                byte[] pathBytes = Encoding.UTF8.GetBytes(path);

                SearchFileReadResult result = SearchFileReader.ReadUnixPath(pathBytes, SearchEncodingKind.None);

                Assert.Equal(SearchFileReadKind.Buffered, result.Kind);
                Assert.Equal("needle\n"u8.ToArray(), result.GetBytes());
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
        string root = Path.Combine(Path.GetTempPath(), $"scout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
