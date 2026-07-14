namespace Scout.IO.Ignore;

/// <summary>
/// Represents the ordered ignore rules inherited by one directory.
/// </summary>
/// <param name="parent">The parent directory's ignore stack.</param>
/// <param name="customRules">The custom ignore rules introduced by this directory.</param>
/// <param name="dotRules">The <c>.ignore</c> rules introduced by this directory.</param>
/// <param name="gitRules">The <c>.gitignore</c> rules introduced by this directory.</param>
/// <param name="gitExcludeRules">The Git exclude rules introduced by this directory.</param>
/// <param name="globalGitRules">The configured global Git ignore rules.</param>
/// <param name="hasGit">Whether this directory contains a Git or JJ repository marker.</param>
/// <param name="requireGit">Whether Git-specific rules require a repository marker.</param>
internal sealed class IgnoreStack(
    IgnoreStack? parent,
    IgnoreRuleSet customRules,
    IgnoreRuleSet dotRules,
    IgnoreRuleSet gitRules,
    IgnoreRuleSet gitExcludeRules,
    IgnoreRuleSet globalGitRules,
    bool hasGit,
    bool requireGit)
{
    private static readonly IgnoreRuleSet s_emptyRules = new();
    private readonly IgnoreStack? _parent = parent;
    private readonly IgnoreRuleSet _customRules = customRules;
    private readonly IgnoreRuleSet _dotRules = dotRules;
    private readonly IgnoreRuleSet _gitRules = gitRules;
    private readonly IgnoreRuleSet _gitExcludeRules = gitExcludeRules;
    private readonly IgnoreRuleSet _globalGitRules = globalGitRules;
    private readonly bool _hasGit = hasGit;
    private readonly bool _requireGit = requireGit;

    /// <summary>
    /// Gets an empty ignore stack suitable for standard-filter-free traversal.
    /// </summary>
    public static IgnoreStack Empty { get; } =
        Create(s_emptyRules, requireGit: true);

    /// <summary>
    /// Creates a root ignore stack.
    /// </summary>
    /// <param name="globalGitRules">The configured global Git ignore rules.</param>
    /// <param name="requireGit">Whether Git-specific rules require a repository marker.</param>
    /// <returns>The root ignore stack.</returns>
    public static IgnoreStack Create(IgnoreRuleSet globalGitRules, bool requireGit)
    {
        ArgumentNullException.ThrowIfNull(globalGitRules);

        return new(
            null,
            s_emptyRules,
            s_emptyRules,
            s_emptyRules,
            s_emptyRules,
            globalGitRules,
            hasGit: false,
            requireGit);
    }

    /// <summary>
    /// Adds ignore rules inherited from directories above a traversal root.
    /// </summary>
    /// <param name="path">The traversal root path.</param>
    /// <param name="dotIgnore">Whether <c>.ignore</c> files are enabled.</param>
    /// <param name="gitIgnore">Whether <c>.gitignore</c> files are enabled.</param>
    /// <param name="gitExclude">Whether Git exclude files are enabled.</param>
    /// <param name="requireGit">Whether Git-specific rules require a repository marker.</param>
    /// <param name="ignoreCaseInsensitive">Whether ignore patterns use ASCII-insensitive matching.</param>
    /// <param name="customIgnoreFileNames">The custom ignore file names to load.</param>
    /// <param name="logger">The diagnostic logger.</param>
    /// <returns>The inherited ignore stack.</returns>
    public IgnoreStack AddParents(
        string path,
        bool dotIgnore,
        bool gitIgnore,
        bool gitExclude,
        bool requireGit,
        bool ignoreCaseInsensitive,
        IReadOnlyList<string> customIgnoreFileNames,
        DiagnosticLogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(customIgnoreFileNames);

        string fullPath = Path.GetFullPath(path);
        string? directory = Directory.Exists(fullPath)
            ? Directory.GetParent(Path.TrimEndingDirectorySeparator(fullPath))?.FullName
            : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return this;
        }

        List<string> directories = [];
        for (string? current = directory; !string.IsNullOrEmpty(current); current = Directory.GetParent(current)?.FullName)
        {
            directories.Add(current);
        }

        IgnoreStack stack = this;
        for (int index = directories.Count - 1; index >= 0; index--)
        {
            stack = stack.AddDirectory(
                directories[index],
                dotIgnore,
                gitIgnore,
                gitExclude,
                requireGit,
                ignoreCaseInsensitive,
                customIgnoreFileNames,
                logger);
        }

        return stack;
    }

    /// <summary>
    /// Adds ignore rules for a directory whose entries have not been enumerated.
    /// </summary>
    /// <param name="directory">The directory containing the ignore rules.</param>
    /// <param name="dotIgnore">Whether <c>.ignore</c> files are enabled.</param>
    /// <param name="gitIgnore">Whether <c>.gitignore</c> files are enabled.</param>
    /// <param name="gitExclude">Whether Git exclude files are enabled.</param>
    /// <param name="requireGit">Whether Git-specific rules require a repository marker.</param>
    /// <param name="ignoreCaseInsensitive">Whether ignore patterns use ASCII-insensitive matching.</param>
    /// <param name="customIgnoreFileNames">The custom ignore file names to load.</param>
    /// <param name="logger">The diagnostic logger.</param>
    /// <returns>The ignore stack for children of <paramref name="directory" />.</returns>
    public IgnoreStack AddDirectory(
        string directory,
        bool dotIgnore,
        bool gitIgnore,
        bool gitExclude,
        bool requireGit,
        bool ignoreCaseInsensitive,
        IReadOnlyList<string> customIgnoreFileNames,
        DiagnosticLogger logger)
    {
        return AddDirectoryCore(
            directory,
            entries: default,
            entriesKnown: false,
            dotIgnore,
            gitIgnore,
            gitExclude,
            requireGit,
            ignoreCaseInsensitive,
            customIgnoreFileNames,
            logger);
    }

    /// <summary>
    /// Adds ignore rules discovered among a directory's already-enumerated entries.
    /// </summary>
    /// <param name="directory">The directory containing the ignore rules.</param>
    /// <param name="entries">The entries already enumerated from the directory.</param>
    /// <param name="dotIgnore">Whether <c>.ignore</c> files are enabled.</param>
    /// <param name="gitIgnore">Whether <c>.gitignore</c> files are enabled.</param>
    /// <param name="gitExclude">Whether Git exclude files are enabled.</param>
    /// <param name="requireGit">Whether Git-specific rules require a repository marker.</param>
    /// <param name="ignoreCaseInsensitive">Whether ignore patterns use ASCII-insensitive matching.</param>
    /// <param name="customIgnoreFileNames">The custom ignore file names to load.</param>
    /// <param name="logger">The diagnostic logger.</param>
    /// <returns>The ignore stack for children of <paramref name="directory" />.</returns>
    internal IgnoreStack AddDirectory(
        string directory,
        ReadOnlySpan<WalkPath> entries,
        bool dotIgnore,
        bool gitIgnore,
        bool gitExclude,
        bool requireGit,
        bool ignoreCaseInsensitive,
        IReadOnlyList<string> customIgnoreFileNames,
        DiagnosticLogger logger)
    {
        return AddDirectoryCore(
            directory,
            entries,
            entriesKnown: true,
            dotIgnore,
            gitIgnore,
            gitExclude,
            requireGit,
            ignoreCaseInsensitive,
            customIgnoreFileNames,
            logger);
    }

    private IgnoreStack AddDirectoryCore(
        string directory,
        ReadOnlySpan<WalkPath> entries,
        bool entriesKnown,
        bool dotIgnore,
        bool gitIgnore,
        bool gitExclude,
        bool requireGit,
        bool ignoreCaseInsensitive,
        IReadOnlyList<string> customIgnoreFileNames,
        DiagnosticLogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(customIgnoreFileNames);

        bool hasGitEntry = !entriesKnown;
        bool hasJjEntry = !entriesKnown;
        bool hasGitIgnore = !entriesKnown;
        bool hasDotIgnore = !entriesKnown;
        Span<bool> hasCustomIgnore = customIgnoreFileNames.Count <= 64
            ? stackalloc bool[customIgnoreFileNames.Count]
            : new bool[customIgnoreFileNames.Count];
        if (!entriesKnown)
        {
            hasCustomIgnore.Fill(true);
        }
        else
        {
            for (int nameIndex = 0; nameIndex < customIgnoreFileNames.Count; nameIndex++)
            {
                string customName = customIgnoreFileNames[nameIndex];
                hasCustomIgnore[nameIndex] = customName.Contains(Path.DirectorySeparatorChar) ||
                    customName.Contains(Path.AltDirectorySeparatorChar);
            }

            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                WalkPath entry = entries[entryIndex];
                if (entry.IsRawUnixPath)
                {
                    continue;
                }

                ReadOnlySpan<char> fileName = Path.GetFileName(entry.TextPath.AsSpan());
                hasGitEntry |= fileName.SequenceEqual(".git");
                hasJjEntry |= fileName.SequenceEqual(".jj");
                hasGitIgnore |= fileName.SequenceEqual(".gitignore");
                hasDotIgnore |= fileName.SequenceEqual(".ignore");
                for (int nameIndex = 0; nameIndex < customIgnoreFileNames.Count; nameIndex++)
                {
                    hasCustomIgnore[nameIndex] |= fileName.Equals(
                        customIgnoreFileNames[nameIndex],
                        StringComparison.Ordinal);
                }
            }
        }

        bool checkVcsDirectory = requireGit && (gitIgnore || gitExclude);
        bool directoryHasGit = checkVcsDirectory &&
            HasGitDirectory(directory, hasGitEntry, hasJjEntry);
        IgnoreRuleSet? gitRuleSet = null;
        IgnoreRuleSet? gitExcludeRuleSet = null;
        if (gitIgnore && hasGitIgnore)
        {
            gitRuleSet = new IgnoreRuleSet();
            AddFileRules(
                directory,
                ".gitignore",
                gitRuleSet,
                ignoreCaseInsensitive,
                logger);
        }

        if (gitExclude && hasGitEntry)
        {
            gitExcludeRuleSet = new IgnoreRuleSet();
            AddGitExcludeRules(directory, gitExcludeRuleSet, ignoreCaseInsensitive, logger);
        }

        IgnoreRuleSet? dotRuleSet = null;
        if (dotIgnore && hasDotIgnore)
        {
            dotRuleSet = new IgnoreRuleSet();
            AddFileRules(
                directory,
                ".ignore",
                dotRuleSet,
                ignoreCaseInsensitive,
                logger);
        }

        IgnoreRuleSet? customRuleSet = null;
        for (int index = 0; index < customIgnoreFileNames.Count; index++)
        {
            if (!hasCustomIgnore[index])
            {
                continue;
            }

            customRuleSet ??= new IgnoreRuleSet();
            AddFileRules(
                directory,
                customIgnoreFileNames[index],
                customRuleSet,
                ignoreCaseInsensitive,
                logger);
        }

        if (IsEmpty(customRuleSet) &&
            IsEmpty(dotRuleSet) &&
            IsEmpty(gitRuleSet) &&
            IsEmpty(gitExcludeRuleSet) &&
            !directoryHasGit)
        {
            return this;
        }

        return new IgnoreStack(
            this,
            OrEmpty(customRuleSet),
            OrEmpty(dotRuleSet),
            OrEmpty(gitRuleSet),
            OrEmpty(gitExcludeRuleSet),
            _globalGitRules,
            directoryHasGit,
            requireGit);
    }

    /// <summary>
    /// Matches a directory entry against the inherited ignore rules.
    /// </summary>
    /// <param name="entry">The directory entry to match.</param>
    /// <returns>The highest-precedence ignore decision.</returns>
    public IgnoreDecision Match(DirEntry entry)
    {
        return Match(entry, out _);
    }

    /// <summary>
    /// Matches a directory entry and returns the rule responsible for the decision.
    /// </summary>
    /// <param name="entry">The directory entry to match.</param>
    /// <param name="matchedRule">The matching rule, when one exists.</param>
    /// <returns>The highest-precedence ignore decision.</returns>
    internal IgnoreDecision Match(DirEntry entry, out IgnoreRule? matchedRule)
    {
        bool anyGit = !_requireGit || HasGitInStack();
        bool sawGit = false;
        matchedRule = null;
        IgnoreDecision customDecision = IgnoreDecision.None;
        IgnoreDecision dotDecision = IgnoreDecision.None;
        IgnoreDecision gitDecision = IgnoreDecision.None;
        IgnoreDecision gitExcludeDecision = IgnoreDecision.None;
        IgnoreDecision globalGitDecision = IgnoreDecision.None;
        IgnoreRule? customRule = null;
        IgnoreRule? dotRule = null;
        IgnoreRule? gitRule = null;
        IgnoreRule? gitExcludeRule = null;
        IgnoreRule? globalGitRule = null;

        for (IgnoreStack? current = this; current is not null; current = current._parent)
        {
            if (customDecision == IgnoreDecision.None)
            {
                customDecision = current._customRules.Match(entry, out customRule);
            }

            if (dotDecision == IgnoreDecision.None)
            {
                dotDecision = current._dotRules.Match(entry, out dotRule);
            }

            if (anyGit && !sawGit)
            {
                if (gitDecision == IgnoreDecision.None)
                {
                    gitDecision = current._gitRules.Match(entry, out gitRule);
                }

                if (gitExcludeDecision == IgnoreDecision.None)
                {
                    gitExcludeDecision = current._gitExcludeRules.Match(entry, out gitExcludeRule);
                }
            }

            sawGit |= current._hasGit;
        }

        if (anyGit)
        {
            globalGitDecision = _globalGitRules.Match(entry, out globalGitRule);
        }

        return FirstDecision(
            customDecision,
            customRule,
            dotDecision,
            dotRule,
            gitDecision,
            gitRule,
            gitExcludeDecision,
            gitExcludeRule,
            globalGitDecision,
            globalGitRule,
            out matchedRule);
    }

    private bool HasGitInStack()
    {
        for (IgnoreStack? current = this; current is not null; current = current._parent)
        {
            if (current._hasGit)
            {
                return true;
            }
        }

        return false;
    }

    private static IgnoreDecision FirstDecision(
        IgnoreDecision customDecision,
        IgnoreRule? customRule,
        IgnoreDecision dotDecision,
        IgnoreRule? dotRule,
        IgnoreDecision gitDecision,
        IgnoreRule? gitRule,
        IgnoreDecision gitExcludeDecision,
        IgnoreRule? gitExcludeRule,
        IgnoreDecision globalGitDecision,
        IgnoreRule? globalGitRule,
        out IgnoreRule? matchedRule)
    {
        if (customDecision != IgnoreDecision.None)
        {
            matchedRule = customRule;
            return customDecision;
        }

        if (dotDecision != IgnoreDecision.None)
        {
            matchedRule = dotRule;
            return dotDecision;
        }

        if (gitDecision != IgnoreDecision.None)
        {
            matchedRule = gitRule;
            return gitDecision;
        }

        if (gitExcludeDecision != IgnoreDecision.None)
        {
            matchedRule = gitExcludeRule;
            return gitExcludeDecision;
        }

        matchedRule = globalGitRule;
        return globalGitDecision;
    }

    private static bool IsEmpty(IgnoreRuleSet? ruleSet)
    {
        return ruleSet is null || ruleSet.IsEmpty;
    }

    private static IgnoreRuleSet OrEmpty(IgnoreRuleSet? ruleSet)
    {
        return IsEmpty(ruleSet) ? s_emptyRules : ruleSet!;
    }

    private static bool HasGitDirectory(string directory, bool hasGitEntry, bool hasJjEntry)
    {
        if (hasGitEntry)
        {
            string dotGit = Path.Combine(directory, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
            {
                return true;
            }
        }

        return hasJjEntry && Directory.Exists(Path.Combine(directory, ".jj"));
    }

    private static void AddFileRules(
        string directory,
        string fileName,
        IgnoreRuleSet ruleSet,
        bool ignoreCaseInsensitive,
        DiagnosticLogger logger)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        int startIndex = ruleSet.Count;
        IgnoreDiagnosticLogging.LogOpenedIgnoreFile(logger, path);
        ruleSet.AddFile(directory, path, ignoreCaseInsensitive);
        IgnoreDiagnosticLogging.LogBuiltGlobSet(logger, ruleSet.GetGlobSetSummary(startIndex));
    }

    private static void AddGitExcludeRules(string directory, IgnoreRuleSet ruleSet, bool ignoreCaseInsensitive, DiagnosticLogger logger)
    {
        string? path = ResolveGitExcludePath(directory);
        if (path is not null && File.Exists(path))
        {
            int startIndex = ruleSet.Count;
            IgnoreDiagnosticLogging.LogOpenedIgnoreFile(logger, path);
            ruleSet.AddFile(directory, path, ignoreCaseInsensitive);
            IgnoreDiagnosticLogging.LogBuiltGlobSet(logger, ruleSet.GetGlobSetSummary(startIndex));
        }
    }

    private static string? ResolveGitExcludePath(string directory)
    {
        string dotGit = Path.Combine(directory, ".git");
        if (Directory.Exists(dotGit))
        {
            return Path.Combine(dotGit, "info", "exclude");
        }

        if (!File.Exists(dotGit))
        {
            return null;
        }

        string? gitDir = TryReadGitDirFile(directory, dotGit);
        if (gitDir is null)
        {
            return null;
        }

        string commonDir = ResolveCommonGitDirectory(gitDir);
        return Path.Combine(commonDir, "info", "exclude");
    }

    private static string? TryReadGitDirFile(string directory, string dotGit)
    {
        string? line;
        try
        {
            using StreamReader reader = File.OpenText(dotGit);
            line = reader.ReadLine();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        const string prefix = "gitdir: ";
        if (line is null || !line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string path = line[prefix.Length..];
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        string relativeToGitFile = Path.GetFullPath(Path.Combine(directory, path));
        if (Directory.Exists(relativeToGitFile) || File.Exists(relativeToGitFile))
        {
            return relativeToGitFile;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }

    private static string ResolveCommonGitDirectory(string gitDir)
    {
        string commonDirPath = Path.Combine(gitDir, "commondir");
        string? line;
        try
        {
            using StreamReader reader = File.OpenText(commonDirPath);
            line = reader.ReadLine();
        }
        catch (IOException)
        {
            return gitDir;
        }
        catch (UnauthorizedAccessException)
        {
            return gitDir;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return gitDir;
        }

        string path = line.Trim();
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(gitDir, path));
    }
}
