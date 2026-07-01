namespace Scout;

/// <summary>
/// Verifies the supported public file walker facade.
/// </summary>
public sealed class FileWalkerApiTests
{
    /// <summary>
    /// Verifies default walking applies ripgrep-compatible ignore and hidden-file rules.
    /// </summary>
    [Fact]
    public void FileWalkerAppliesDefaultIgnoreRules()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "ignored"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored/\n*.tmp\n");
        File.WriteAllText(Path.Combine(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "main.cs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "scratch.tmp"), string.Empty);
        File.WriteAllText(Path.Combine(root, "ignored", "file.txt"), string.Empty);

        var walker = new FileWalker(new FileWalkerOptions { Sort = FileWalkSort.FileName });

        Assert.Equal(["src", "src/main.cs"], Collect(root, walker));
    }

    /// <summary>
    /// Verifies walker options expose the expected public knobs.
    /// </summary>
    [Fact]
    public void FileWalkerOptionsControlFiltering()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "ignored"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored/\n");
        File.WriteAllText(Path.Combine(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Combine(root, "src", "main.cs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "ignored", "file.txt"), string.Empty);

        var options = new FileWalkerOptions
        {
            IgnoreHidden = false,
            ReadGitIgnoreFiles = false,
            Sort = FileWalkSort.FileName,
        };

        Assert.Equal([".git", ".gitignore", ".hidden", "ignored", "ignored/file.txt", "src", "src/main.cs"], Collect(root, new FileWalker(options)));
    }

    /// <summary>
    /// Verifies file walker entries expose file metadata without leaking lower-level walker types.
    /// </summary>
    [Fact]
    public void FileWalkEntryExposesMetadata()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "file.txt");
        File.WriteAllText(path, "hello");
        var walker = new FileWalker();

        FileWalkEntry entry = Assert.Single(walker.Enumerate(root));

        Assert.Equal(path, entry.FullPath);
        Assert.Equal("file.txt", entry.FileName);
        Assert.Equal(1, entry.Depth);
        Assert.True(entry.IsFile);
        Assert.False(entry.IsDirectory);
        Assert.False(entry.IsSymbolicLink);
        Assert.False(entry.IsStdin);
        Assert.Equal(5, entry.Length);
        Assert.Equal(path, entry.ToString());
    }

    private static List<string> Collect(string root, FileWalker walker)
    {
        var paths = new List<string>();
        foreach (FileWalkEntry entry in walker.Enumerate(root))
        {
            paths.Add(ToRelativePath(root, entry.FullPath));
        }

        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-filewalker-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
