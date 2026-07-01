using System.Collections.ObjectModel;

namespace Scout.IO.Ignore;

/// <summary>
/// Configures <see cref="FileWalker" />.
/// </summary>
public sealed class FileWalkerOptions
{
    private int? minDepth = 1;
    private int? maxDepth;
    private long? maxFileSize;
    private int threads;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileWalkerOptions" /> class.
    /// </summary>
    public FileWalkerOptions()
    {
        CustomIgnoreFileNames = [];
        IgnoreFiles = [];
    }

    private FileWalkerOptions(FileWalkerOptions source)
    {
        minDepth = source.minDepth;
        maxDepth = source.maxDepth;
        maxFileSize = source.maxFileSize;
        threads = source.threads;
        IgnoreHidden = source.IgnoreHidden;
        FollowSymbolicLinks = source.FollowSymbolicLinks;
        SameFileSystem = source.SameFileSystem;
        ReadParentIgnoreFiles = source.ReadParentIgnoreFiles;
        ReadIgnoreFiles = source.ReadIgnoreFiles;
        ReadGitIgnoreFiles = source.ReadGitIgnoreFiles;
        ReadGitExcludeFiles = source.ReadGitExcludeFiles;
        ReadGlobalGitIgnore = source.ReadGlobalGitIgnore;
        RequireGitRepository = source.RequireGitRepository;
        CaseInsensitiveIgnoreRules = source.CaseInsensitiveIgnoreRules;
        Sort = source.Sort;
        Overrides = source.Overrides;
        FileTypes = source.FileTypes;
        Diagnostics = source.Diagnostics;
        CustomIgnoreFileNames = new Collection<string>(source.CustomIgnoreFileNames.ToArray());
        IgnoreFiles = new Collection<string>(source.IgnoreFiles.ToArray());
    }

    /// <summary>
    /// Gets or sets the minimum yielded depth, or <see langword="null" /> for no minimum.
    /// </summary>
    /// <remarks>The default is <c>1</c>, so enumerating a directory yields its descendants and not the root itself.</remarks>
    public int? MinDepth
    {
        get => minDepth;
        set
        {
            if (value.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value.Value);
            }

            minDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum recursion depth, or <see langword="null" /> for no maximum.
    /// </summary>
    public int? MaxDepth
    {
        get => maxDepth;
        set
        {
            if (value.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value.Value);
            }

            maxDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum yielded file size, or <see langword="null" /> for no maximum.
    /// </summary>
    public long? MaxFileSize
    {
        get => maxFileSize;
        set
        {
            if (value.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value.Value);
            }

            maxFileSize = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether hidden files and directories are ignored.
    /// </summary>
    public bool IgnoreHidden { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether symbolic links are followed.
    /// </summary>
    public bool FollowSymbolicLinks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether traversal stays on the root file system.
    /// </summary>
    public bool SameFileSystem { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether parent directories are searched for ignore files.
    /// </summary>
    public bool ReadParentIgnoreFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether <c>.ignore</c>, <c>.rgignore</c>, and <c>.scoutignore</c> files are read.
    /// </summary>
    public bool ReadIgnoreFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether <c>.gitignore</c> files are read.
    /// </summary>
    public bool ReadGitIgnoreFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether <c>.git/info/exclude</c> files are read.
    /// </summary>
    public bool ReadGitExcludeFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the configured global gitignore file is read.
    /// </summary>
    public bool ReadGlobalGitIgnore { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether git-specific ignore files require a git or JJ repository marker.
    /// </summary>
    public bool RequireGitRepository { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether ignore-file patterns match ASCII case-insensitively.
    /// </summary>
    public bool CaseInsensitiveIgnoreRules { get; set; }

    /// <summary>
    /// Gets or sets deterministic sorting for entries yielded from each directory.
    /// </summary>
    public FileWalkSort Sort { get; set; }

    /// <summary>
    /// Gets or sets explicit override globs.
    /// </summary>
    public Override Overrides { get; set; } = Override.Empty;

    /// <summary>
    /// Gets or sets file type filters.
    /// </summary>
    public FileTypeMatcher FileTypes { get; set; } = FileTypeMatcher.Empty;

    /// <summary>
    /// Gets or sets the diagnostic logger used while loading and applying ignore rules.
    /// </summary>
    public DiagnosticLogger Diagnostics { get; set; }

    /// <summary>
    /// Gets custom ignore file names to read in every directory.
    /// </summary>
    public Collection<string> CustomIgnoreFileNames { get; }

    /// <summary>
    /// Gets explicit ignore files whose rules have lower precedence than directory ignore files.
    /// </summary>
    public Collection<string> IgnoreFiles { get; }

    /// <summary>
    /// Gets or sets the worker count used by lower-level parallel traversal APIs.
    /// </summary>
    public int Threads
    {
        get => threads;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            threads = value;
        }
    }

    internal FileWalkerOptions Clone()
    {
        return new FileWalkerOptions(this);
    }
}
