using System;
using System.Collections.Generic;
using System.IO;

namespace Scout;

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
        IReadOnlyList<string> customIgnoreFileNames)
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
                customIgnoreFileNames);
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
        IReadOnlyList<string> customIgnoreFileNames)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(customIgnoreFileNames);

        bool directoryHasGit = HasGitDirectory(directory);
        var gitRuleSet = new IgnoreRuleSet();
        var gitExcludeRuleSet = new IgnoreRuleSet();
        if (gitIgnore)
        {
            AddFileRules(directory, ".gitignore", gitRuleSet, ignoreCaseInsensitive);
        }

        if (gitExclude)
        {
            AddGitExcludeRules(directory, gitExcludeRuleSet, ignoreCaseInsensitive);
        }

        var dotRuleSet = new IgnoreRuleSet();
        if (dotIgnore)
        {
            AddFileRules(directory, ".ignore", dotRuleSet, ignoreCaseInsensitive);
        }

        var customRuleSet = new IgnoreRuleSet();
        for (int index = 0; index < customIgnoreFileNames.Count; index++)
        {
            AddFileRules(directory, customIgnoreFileNames[index], customRuleSet, ignoreCaseInsensitive);
        }

        if (customRuleSet.IsEmpty && dotRuleSet.IsEmpty && gitRuleSet.IsEmpty && gitExcludeRuleSet.IsEmpty && !directoryHasGit)
        {
            return this;
        }

        return new IgnoreStack(this, customRuleSet, dotRuleSet, gitRuleSet, gitExcludeRuleSet, globalGitRules, directoryHasGit, requireGit);
    }

    public IgnoreDecision Match(DirEntry entry)
    {
        bool anyGit = !requireGit || HasGitInStack();
        bool sawGit = false;
        IgnoreDecision customDecision = IgnoreDecision.None;
        IgnoreDecision dotDecision = IgnoreDecision.None;
        IgnoreDecision gitDecision = IgnoreDecision.None;
        IgnoreDecision gitExcludeDecision = IgnoreDecision.None;
        IgnoreDecision globalGitDecision = IgnoreDecision.None;

        for (IgnoreStack? current = this; current is not null; current = current.parent)
        {
            if (customDecision == IgnoreDecision.None)
            {
                customDecision = current.customRules.Match(entry);
            }

            if (dotDecision == IgnoreDecision.None)
            {
                dotDecision = current.dotRules.Match(entry);
            }

            if (anyGit && !sawGit)
            {
                if (gitDecision == IgnoreDecision.None)
                {
                    gitDecision = current.gitRules.Match(entry);
                }

                if (gitExcludeDecision == IgnoreDecision.None)
                {
                    gitExcludeDecision = current.gitExcludeRules.Match(entry);
                }
            }

            sawGit |= current.hasGit;
        }

        if (anyGit)
        {
            globalGitDecision = globalGitRules.Match(entry);
        }

        return FirstDecision(customDecision, dotDecision, gitDecision, gitExcludeDecision, globalGitDecision);
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
        IgnoreDecision dotDecision,
        IgnoreDecision gitDecision,
        IgnoreDecision gitExcludeDecision,
        IgnoreDecision globalGitDecision)
    {
        if (customDecision != IgnoreDecision.None)
        {
            return customDecision;
        }

        if (dotDecision != IgnoreDecision.None)
        {
            return dotDecision;
        }

        if (gitDecision != IgnoreDecision.None)
        {
            return gitDecision;
        }

        if (gitExcludeDecision != IgnoreDecision.None)
        {
            return gitExcludeDecision;
        }

        return globalGitDecision;
    }

    private static bool HasGitDirectory(string directory)
    {
        return Directory.Exists(Path.Combine(directory, ".git"))
            || File.Exists(Path.Combine(directory, ".git"))
            || Directory.Exists(Path.Combine(directory, ".jj"));
    }

    private static void AddFileRules(string directory, string fileName, IgnoreRuleSet ruleSet, bool ignoreCaseInsensitive)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        ruleSet.AddFile(directory, path, ignoreCaseInsensitive);
    }

    private static void AddGitExcludeRules(string directory, IgnoreRuleSet ruleSet, bool ignoreCaseInsensitive)
    {
        string? path = ResolveGitExcludePath(directory);
        if (path is not null && File.Exists(path))
        {
            ruleSet.AddFile(directory, path, ignoreCaseInsensitive);
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

        const string prefix = "gitdir:";
        if (line is null || !line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string path = line[prefix.Length..].Trim();
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
