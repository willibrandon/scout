namespace Scout.IO.Ignore;

/// <summary>
/// Recursively enumerates filesystem entries using ripgrep-compatible ignore semantics.
/// </summary>
public sealed class FileWalker
{
    private readonly FileWalkerOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileWalker" /> class with default ripgrep-compatible options.
    /// </summary>
    public FileWalker()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileWalker" /> class.
    /// </summary>
    /// <param name="options">The walker options, or <see langword="null" /> for defaults.</param>
    public FileWalker(FileWalkerOptions? options)
    {
        this.options = (options ?? new FileWalkerOptions()).Clone();
    }

    /// <summary>
    /// Enumerates entries below a root path.
    /// </summary>
    /// <param name="root">The root path to traverse.</param>
    /// <returns>The entries yielded by the walker.</returns>
    public IEnumerable<FileWalkEntry> Enumerate(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        return EnumerateCore([root]);
    }

    /// <summary>
    /// Enumerates entries below one or more root paths.
    /// </summary>
    /// <param name="roots">The root paths to traverse.</param>
    /// <returns>The entries yielded by the walker.</returns>
    public IEnumerable<FileWalkEntry> Enumerate(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var copiedRoots = new List<string>();
        foreach (string root in roots)
        {
            ArgumentException.ThrowIfNullOrEmpty(root);
            copiedRoots.Add(root);
        }

        return EnumerateCore(copiedRoots);
    }

    private IEnumerable<FileWalkEntry> EnumerateCore(List<string> roots)
    {
        if (roots.Count == 0)
        {
            yield break;
        }

        WalkBuilder builder = CreateBuilder(roots);
        foreach (DirEntry entry in builder.Build())
        {
            yield return FileWalkEntry.FromDirEntry(entry);
        }
    }

    private WalkBuilder CreateBuilder(List<string> roots)
    {
        WalkBuilder builder = new(roots[0]);
        for (int index = 1; index < roots.Count; index++)
        {
            builder.Add(roots[index]);
        }

        ApplyOptions(builder);
        return builder;
    }

    private void ApplyOptions(WalkBuilder builder)
    {
        builder
            .MinDepth(options.MinDepth)
            .MaxDepth(options.MaxDepth)
            .MaxFileSize(options.MaxFileSize)
            .Hidden(options.IgnoreHidden)
            .FollowLinks(options.FollowSymbolicLinks)
            .SameFileSystem(options.SameFileSystem)
            .Parents(options.ReadParentIgnoreFiles)
            .Ignore(options.ReadIgnoreFiles)
            .GitIgnore(options.ReadGitIgnoreFiles)
            .GitExclude(options.ReadGitExcludeFiles)
            .GitGlobal(options.ReadGlobalGitIgnore)
            .RequireGit(options.RequireGitRepository)
            .IgnoreCaseInsensitive(options.CaseInsensitiveIgnoreRules)
            .Overrides(options.Overrides)
            .FileTypes(options.FileTypes)
            .Threads(options.Threads)
            .Diagnostics(options.Diagnostics);

        switch (options.Sort)
        {
            case FileWalkSort.None:
                break;
            case FileWalkSort.FullPath:
                builder.SortByPath();
                break;
            case FileWalkSort.FileName:
                builder.SortByFileName();
                break;
            default:
                throw new InvalidOperationException($"Unsupported file walk sort value: {options.Sort}.");
        }

        for (int index = 0; index < options.CustomIgnoreFileNames.Count; index++)
        {
            builder.AddCustomIgnoreFileName(options.CustomIgnoreFileNames[index]);
        }

        for (int index = 0; index < options.IgnoreFiles.Count; index++)
        {
            builder.AddIgnoreFile(options.IgnoreFiles[index]);
        }
    }
}
