namespace Scout;

/// <summary>
/// Verifies the supported public file walker facade.
/// </summary>
[TestClass]
public sealed class FileWalkerApiTests
{
    /// <summary>
    /// Verifies default walking applies ripgrep-compatible ignore and hidden-file rules.
    /// </summary>
    [TestMethod]
    public void FileWalkerAppliesDefaultIgnoreRules()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "src"));
        Directory.CreateDirectory(Path.Join(root, "ignored"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "ignored/\n*.tmp\n");
        File.WriteAllText(Path.Join(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "main.cs"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "scratch.tmp"), string.Empty);
        File.WriteAllText(Path.Join(root, "ignored", "file.txt"), string.Empty);

        var walker = new FileWalker(new FileWalkerOptions { Sort = FileWalkSort.FileName });

        Assert.AreSequenceEqual<string>(["src", "src/main.cs"], Collect(root, walker));
    }

    /// <summary>
    /// Verifies walker options expose the expected public knobs.
    /// </summary>
    [TestMethod]
    public void FileWalkerOptionsControlFiltering()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Join(root, ".git"));
        Directory.CreateDirectory(Path.Join(root, "src"));
        Directory.CreateDirectory(Path.Join(root, "ignored"));
        File.WriteAllText(Path.Join(root, ".gitignore"), "ignored/\n");
        File.WriteAllText(Path.Join(root, ".hidden"), string.Empty);
        File.WriteAllText(Path.Join(root, "src", "main.cs"), string.Empty);
        File.WriteAllText(Path.Join(root, "ignored", "file.txt"), string.Empty);

        var options = new FileWalkerOptions
        {
            IgnoreHidden = false,
            ReadGitIgnoreFiles = false,
            Sort = FileWalkSort.FileName,
        };

        Assert.AreSequenceEqual<string>([".git", ".gitignore", ".hidden", "ignored", "ignored/file.txt", "src", "src/main.cs"], Collect(root, new FileWalker(options)));
    }

    /// <summary>
    /// Verifies file walker entries expose file metadata without leaking lower-level walker types.
    /// </summary>
    [TestMethod]
    public void FileWalkEntryExposesMetadata()
    {
        string root = CreateTempDirectory();
        string path = Path.Join(root, "file.txt");
        File.WriteAllText(path, "hello");
        var walker = new FileWalker();

        FileWalkEntry entry = Assert.ContainsSingle(walker.Enumerate(root));

        Assert.AreEqual(path, entry.FullPath);
        Assert.AreEqual("file.txt", entry.FileName);
        Assert.AreEqual(1, entry.Depth);
        Assert.IsTrue(entry.IsFile);
        Assert.IsFalse(entry.IsDirectory);
        Assert.IsFalse(entry.IsSymbolicLink);
        Assert.IsFalse(entry.IsStdin);
        Assert.AreEqual(5, entry.Length);
        Assert.AreEqual(path, entry.ToString());
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
        string path = Path.Join(Path.GetTempPath(), "scout-filewalker-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
