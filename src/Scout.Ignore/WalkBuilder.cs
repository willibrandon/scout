namespace Scout;

/// <summary>
/// Builds recursive directory walkers with ripgrep-style traversal options.
/// </summary>
public sealed class WalkBuilder
{
    private readonly List<string> paths = [];
    private int? minDepth;
    private int? maxDepth;
    private long? maxFileSize;
    private bool hidden = true;
    private bool followLinks;
    private bool sameFileSystem;
    private bool parents = true;
    private bool dotIgnore = true;
    private bool gitIgnore = true;
    private bool gitExclude = true;
    private bool gitGlobal = true;
    private bool requireGit = true;
    private bool ignoreCaseInsensitive;
    private bool defaultCustomIgnoreFiles = true;
    private int threads;
    private WalkSort sort;
    private Override overrides = Override.Empty;
    private FileTypeMatcher fileTypes = FileTypeMatcher.Empty;
    private readonly List<string> customIgnoreFileNames = [];
    private readonly IgnoreRuleSet explicitIgnoreRules = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WalkBuilder" /> class.
    /// </summary>
    /// <param name="path">The first path to traverse.</param>
    public WalkBuilder(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        paths.Add(path);
    }

    /// <summary>
    /// Adds another root path to traverse.
    /// </summary>
    /// <param name="path">The path to traverse.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder Add(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        paths.Add(path);
        return this;
    }

    /// <summary>
    /// Sets the minimum yielded depth.
    /// </summary>
    /// <param name="depth">The minimum depth, or <see langword="null" /> for no minimum.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder MinDepth(int? depth)
    {
        if (depth.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(depth.Value);
        }

        minDepth = depth;
        if (maxDepth.HasValue && minDepth.HasValue && minDepth.Value > maxDepth.Value)
        {
            minDepth = maxDepth;
        }

        return this;
    }

    /// <summary>
    /// Sets the maximum recursion depth.
    /// </summary>
    /// <param name="depth">The maximum depth, or <see langword="null" /> for no maximum.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder MaxDepth(int? depth)
    {
        if (depth.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(depth.Value);
        }

        maxDepth = depth;
        if (minDepth.HasValue && maxDepth.HasValue && maxDepth.Value < minDepth.Value)
        {
            maxDepth = minDepth;
        }

        return this;
    }

    /// <summary>
    /// Sets the maximum yielded file size.
    /// </summary>
    /// <param name="bytes">The maximum file size, or <see langword="null" /> for no maximum.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder MaxFileSize(long? bytes)
    {
        if (bytes.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes.Value);
        }

        maxFileSize = bytes;
        return this;
    }

    /// <summary>
    /// Enables or disables hidden file filtering.
    /// </summary>
    /// <param name="yes">Whether hidden files should be ignored.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder Hidden(bool yes)
    {
        hidden = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables symbolic-link traversal.
    /// </summary>
    /// <param name="yes">Whether symbolic links should be followed.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder FollowLinks(bool yes)
    {
        followLinks = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables pruning traversal at file system boundaries.
    /// </summary>
    /// <param name="yes">Whether child directories on other file systems should be skipped.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder SameFileSystem(bool yes)
    {
        sameFileSystem = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables reading ignore files from parent directories above each root path.
    /// </summary>
    /// <param name="yes">Whether parent ignore files should be read.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder Parents(bool yes)
    {
        parents = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables reading <c>.ignore</c> and default custom ignore files.
    /// </summary>
    /// <param name="yes">Whether dot ignore files should be read.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder Ignore(bool yes)
    {
        dotIgnore = yes;
        defaultCustomIgnoreFiles = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables reading <c>.gitignore</c> files.
    /// </summary>
    /// <param name="yes">Whether <c>.gitignore</c> files should be read.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder GitIgnore(bool yes)
    {
        gitIgnore = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables reading <c>.git/info/exclude</c> files.
    /// </summary>
    /// <param name="yes">Whether <c>.git/info/exclude</c> files should be read.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder GitExclude(bool yes)
    {
        gitExclude = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables reading the global gitignore file.
    /// </summary>
    /// <param name="yes">Whether the global gitignore file should be read.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder GitGlobal(bool yes)
    {
        gitGlobal = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables requiring a git or JJ repository before git-specific ignore rules apply.
    /// </summary>
    /// <param name="yes">Whether a repository marker is required.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder RequireGit(bool yes)
    {
        requireGit = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables case-insensitive ignore-file matching.
    /// </summary>
    /// <param name="yes">Whether ignore-file patterns should match ASCII case-insensitively.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder IgnoreCaseInsensitive(bool yes)
    {
        ignoreCaseInsensitive = yes;
        return this;
    }

    /// <summary>
    /// Enables or disables the standard traversal filters enabled by default.
    /// </summary>
    /// <param name="yes">Whether standard filters should be enabled.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder StandardFilters(bool yes)
    {
        hidden = yes;
        parents = yes;
        dotIgnore = yes;
        gitIgnore = yes;
        gitExclude = yes;
        gitGlobal = yes;
        defaultCustomIgnoreFiles = yes;
        return this;
    }

    /// <summary>
    /// Adds a custom ignore file name to read in every directory.
    /// </summary>
    /// <param name="fileName">The custom ignore file name.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder AddCustomIgnoreFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        customIgnoreFileNames.Add(fileName);
        return this;
    }

    /// <summary>
    /// Adds an explicit ignore file whose rules have lower precedence than directory ignore files.
    /// </summary>
    /// <param name="path">The ignore file path.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder AddIgnoreFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!TryAddIgnoreFile(path, out string? errorMessage))
        {
            throw new IOException(errorMessage);
        }

        return this;
    }

    /// <summary>
    /// Tries to add an explicit ignore file whose rules have lower precedence than directory ignore files.
    /// </summary>
    /// <param name="path">The ignore file path.</param>
    /// <param name="errorMessage">The ripgrep-style diagnostic message when the file could not be read.</param>
    /// <returns><see langword="true" /> when the ignore file was read successfully.</returns>
    public bool TryAddIgnoreFile(string path, out string? errorMessage)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return explicitIgnoreRules.TryAddFile(Directory.GetCurrentDirectory(), path, ignoreCaseInsensitive, out errorMessage);
    }

    /// <summary>
    /// Sets explicit override globs.
    /// </summary>
    /// <param name="overrideMatcher">The override matcher.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder Overrides(Override overrideMatcher)
    {
        ArgumentNullException.ThrowIfNull(overrideMatcher);
        overrides = overrideMatcher;
        return this;
    }

    /// <summary>
    /// Sets file type selections for this walker.
    /// </summary>
    /// <param name="fileTypeMatcher">The file type matcher.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder FileTypes(FileTypeMatcher fileTypeMatcher)
    {
        ArgumentNullException.ThrowIfNull(fileTypeMatcher);
        fileTypes = fileTypeMatcher;
        return this;
    }

    /// <summary>
    /// Sorts each directory's entries by full path.
    /// </summary>
    /// <returns>This builder.</returns>
    public WalkBuilder SortByPath()
    {
        sort = WalkSort.ByPath;
        return this;
    }

    /// <summary>
    /// Sorts each directory's entries by file name.
    /// </summary>
    /// <returns>This builder.</returns>
    public WalkBuilder SortByFileName()
    {
        sort = WalkSort.ByFileName;
        return this;
    }

    /// <summary>
    /// Sets the number of worker threads used by parallel traversal.
    /// </summary>
    /// <param name="count">The worker count, or <c>0</c> to choose automatically.</param>
    /// <returns>This builder.</returns>
    public WalkBuilder Threads(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        threads = count;
        return this;
    }

    /// <summary>
    /// Builds the configured directory walker.
    /// </summary>
    /// <returns>A directory walker.</returns>
    public Walk Build()
    {
        string[] copy = paths.ToArray();
        List<string> ignoreFileNames = [];
        if (defaultCustomIgnoreFiles)
        {
            ignoreFileNames.Add(".rgignore");
            ignoreFileNames.Add(".scoutignore");
        }

        ignoreFileNames.AddRange(customIgnoreFileNames);
        return new Walk(
            copy,
            minDepth,
            maxDepth,
            maxFileSize,
            hidden,
            followLinks,
            sameFileSystem,
            parents,
            dotIgnore,
            gitIgnore,
            gitExclude,
            gitGlobal,
            requireGit,
            ignoreCaseInsensitive,
            sort,
            overrides,
            fileTypes,
            explicitIgnoreRules,
            ignoreFileNames.ToArray());
    }

    /// <summary>
    /// Builds a parallel directory walker.
    /// </summary>
    /// <returns>A parallel directory walker.</returns>
    public WalkParallel BuildParallel()
    {
        return new WalkParallel(Build(), threads);
    }
}
