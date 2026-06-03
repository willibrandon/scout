
namespace Scout;

/// <summary>
/// Verifies search path display formatting.
/// </summary>
public sealed class SearchPathArgumentTests
{
    /// <summary>
    /// Verifies directory search display bytes preserve the root argument prefix.
    /// </summary>
    [Fact]
    public void DirectoryDisplayPathBytesPreserveRootArgument()
    {
        string fullRoot = Path.Combine(Path.GetTempPath(), "scout-root");
        string fullPath = Path.Combine(fullRoot, "src", "file.txt");
        var entry = new DirEntry(fullPath, 1, default, isDirectory: false, isSymbolicLink: false, isStdin: false, 0, default);

        byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(
            "root",
            fullRoot,
            entry,
            defaultRoot: false,
            pathSeparator: (byte)'/');

        Assert.Equal("root/src/file.txt"u8.ToArray(), displayPath);
    }

    /// <summary>
    /// Verifies default-root directory searches display paths relative to the root.
    /// </summary>
    [Fact]
    public void DefaultRootDirectoryDisplayPathBytesAreRelative()
    {
        string fullRoot = Path.Combine(Path.GetTempPath(), "scout-root");
        string fullPath = Path.Combine(fullRoot, "src", "file.txt");
        var entry = new DirEntry(fullPath, 1, default, isDirectory: false, isSymbolicLink: false, isStdin: false, 0, default);

        byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(
            ".",
            fullRoot,
            entry,
            defaultRoot: true,
            pathSeparator: (byte)'/');

        Assert.Equal("src/file.txt"u8.ToArray(), displayPath);
    }

    /// <summary>
    /// Verifies explicit current-directory roots keep ripgrep's leading dot slash.
    /// </summary>
    [Fact]
    public void ExplicitCurrentDirectoryDisplayPathBytesKeepDotSlash()
    {
        string fullRoot = Path.Combine(Path.GetTempPath(), "scout-root");
        string fullPath = Path.Combine(fullRoot, "src", "file.txt");
        var entry = new DirEntry(fullPath, 1, default, isDirectory: false, isSymbolicLink: false, isStdin: false, 0, default);

        byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(
            ".",
            fullRoot,
            entry,
            defaultRoot: false,
            pathSeparator: (byte)'/');

        Assert.Equal("./src/file.txt"u8.ToArray(), displayPath);
    }
}
