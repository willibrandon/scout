namespace Scout.IO.Ignore;

internal sealed class IgnoreStack
{
    private readonly IgnoreStack? parent;
    private readonly IgnoreRuleSet customRules;
    private readonly IgnoreRuleSet dotRules;
    private readonly IgnoreRuleSet gitRules;
    private readonly IgnoreRuleSet gitExcludeRules;
    private readonly IgnoreRuleSet globalGitRules;
    private readonly bool hasGit;
    private readonly bool requireGit;

    private IgnoreStack(
        IgnoreStack? parent,
        IgnoreRuleSet customRules,
        IgnoreRuleSet dotRules,
        IgnoreRuleSet gitRules,
        IgnoreRuleSet gitExcludeRules,
        IgnoreRuleSet globalGitRules,
        bool hasGit,
        bool requireGit)
    {
        this.parent = parent;
        this.customRules = customRules;
        this.dotRules = dotRules;
        this.gitRules = gitRules;
        this.gitExcludeRules = gitExcludeRules;
        this.globalGitRules = globalGitRules;
        this.hasGit = hasGit;
        this.requireGit = requireGit;
    }

    public static IgnoreStack Empty { get; } =
        Create(new IgnoreRuleSet());

    public static IgnoreStack Create(IgnoreRuleSet globalGitRules)
    {
        ArgumentNullException.ThrowIfNull(globalGitRules);

        return new(
            null,
            new IgnoreRuleSet(),
            new IgnoreRuleSet(),
            new IgnoreRuleSet(),
            new IgnoreRuleSet(),
            globalGitRules,
            hasGit: false,
            requireGit: true);
    }

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
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(customIgnoreFileNames);

        bool directoryHasGit = HasGitDirectory(directory);
        var gitRuleSet = new IgnoreRuleSet();
        var gitExcludeRuleSet = new IgnoreRuleSet();
        if (gitIgnore)
        {
            AddFileRules(directory, ".gitignore", gitRuleSet, ignoreCaseInsensitive, logger);
        }

        if (gitExclude)
        {
            AddGitExcludeRules(directory, gitExcludeRuleSet, ignoreCaseInsensitive, logger);
        }

        var dotRuleSet = new IgnoreRuleSet();
        if (dotIgnore)
        {
            AddFileRules(directory, ".ignore", dotRuleSet, ignoreCaseInsensitive, logger);
        }

        var customRuleSet = new IgnoreRuleSet();
        for (int index = 0; index < customIgnoreFileNames.Count; index++)
        {
            AddFileRules(directory, customIgnoreFileNames[index], customRuleSet, ignoreCaseInsensitive, logger);
        }

        if (customRuleSet.IsEmpty && dotRuleSet.IsEmpty && gitRuleSet.IsEmpty && gitExcludeRuleSet.IsEmpty && !directoryHasGit)
        {
            return this;
        }

        return new IgnoreStack(this, customRuleSet, dotRuleSet, gitRuleSet, gitExcludeRuleSet, globalGitRules, directoryHasGit, requireGit);
    }

    public IgnoreDecision Match(DirEntry entry)
    {
        return Match(entry, out _);
    }

    internal IgnoreDecision Match(DirEntry entry, out IgnoreRule? matchedRule)
    {
        bool anyGit = !requireGit || HasGitInStack();
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

        for (IgnoreStack? current = this; current is not null; current = current.parent)
        {
            if (customDecision == IgnoreDecision.None)
            {
                customDecision = current.customRules.Match(entry, out customRule);
            }

            if (dotDecision == IgnoreDecision.None)
            {
                dotDecision = current.dotRules.Match(entry, out dotRule);
            }

            if (anyGit && !sawGit)
            {
                if (gitDecision == IgnoreDecision.None)
                {
                    gitDecision = current.gitRules.Match(entry, out gitRule);
                }

                if (gitExcludeDecision == IgnoreDecision.None)
                {
                    gitExcludeDecision = current.gitExcludeRules.Match(entry, out gitExcludeRule);
                }
            }

            sawGit |= current.hasGit;
        }

        if (anyGit)
        {
            globalGitDecision = globalGitRules.Match(entry, out globalGitRule);
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
        for (IgnoreStack? current = this; current is not null; current = current.parent)
        {
            if (current.hasGit)
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

    private static bool HasGitDirectory(string directory)
    {
        return Directory.Exists(Path.Combine(directory, ".git"))
            || File.Exists(Path.Combine(directory, ".git"))
            || Directory.Exists(Path.Combine(directory, ".jj"));
    }

    private static void AddFileRules(string directory, string fileName, IgnoreRuleSet ruleSet, bool ignoreCaseInsensitive, DiagnosticLogger logger)
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
