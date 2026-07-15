namespace Scout;

/// <summary>
/// Verifies zero-copy mapped search-file ownership and lifetime behavior.
/// </summary>
public sealed class MemoryMappedSearchFileTests
{
    /// <summary>
    /// Verifies a non-empty file is exposed directly until its mapping is disposed.
    /// </summary>
    [Fact]
    public void TryOpenExposesMappedBytesUntilDisposed()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllBytes(path, "alpha\nneedle\n"u8.ToArray());

            Assert.True(MemoryMappedSearchFile.TryOpen(path, out MemoryMappedSearchFile? mappedSearchFile));
            Assert.NotNull(mappedSearchFile);
            Assert.True(mappedSearchFile.Bytes.SequenceEqual("alpha\nneedle\n"u8));

            mappedSearchFile.Dispose();

            Assert.Throws<ObjectDisposedException>(() => mappedSearchFile.Bytes.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an empty file declines mapping so the buffered empty-input path remains authoritative.
    /// </summary>
    [Fact]
    public void TryOpenDeclinesEmptyFile()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "empty.txt");
            File.WriteAllBytes(path, []);

            MemoryMappedSearchFile? mappedSearchFile = null;
            try
            {
                Assert.False(MemoryMappedSearchFile.TryOpen(path, out mappedSearchFile));
                Assert.Null(mappedSearchFile);
            }
            finally
            {
                mappedSearchFile?.Dispose();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies bounded views can advance without retaining the preceding mapped pages.
    /// </summary>
    [Fact]
    public void TryMapViewReplacesCurrentView()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllBytes(path, "0123456789"u8.ToArray());

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                Assert.Equal(10, mappedSearchFile.Length);
                Assert.True(mappedSearchFile.TryMapView(offset: 0, maximumLength: 4));
                Assert.True(mappedSearchFile.Bytes.SequenceEqual("0123"u8));
                Assert.True(mappedSearchFile.TryMapView(offset: 4, maximumLength: 4));
                Assert.True(mappedSearchFile.Bytes.SequenceEqual("4567"u8));
                Assert.True(mappedSearchFile.TryMapView(offset: 8, maximumLength: 4));
                Assert.True(mappedSearchFile.Bytes.SequenceEqual("89"u8));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"scout-mmap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
