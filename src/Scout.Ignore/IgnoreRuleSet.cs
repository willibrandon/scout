namespace Scout;

internal sealed class IgnoreRuleSet
{
    private readonly List<IgnoreRule> rules = [];
    private string? baseDirectory;
    private GlobSet? globSet;
    private bool[]? fileRuleEligibility;

    public bool IsEmpty => rules.Count == 0;

    internal int Count => rules.Count;

    public void Add(IgnoreRule rule)
    {
        baseDirectory ??= rule.BaseDirectory;
        rules.Add(rule);
        globSet = null;
        fileRuleEligibility = null;
    }

    public void AddFile(string baseDirectory, string path, bool asciiCaseInsensitive)
    {
        if (!TryAddFile(baseDirectory, path, asciiCaseInsensitive, out string? errorMessage))
        {
            throw new IOException(errorMessage);
        }
    }

    public bool TryAddFile(string baseDirectory, string path, bool asciiCaseInsensitive, out string? errorMessage)
    {
        if (Directory.Exists(path))
        {
            errorMessage = OperatingSystem.IsWindows()
                ? $"{path}: {OsErrorMessages.DirectoryAsFile}"
                : $"{path}: line 1: {OsErrorMessages.DirectoryAsFile}";
            return false;
        }

        try
        {
            AddFileLines(baseDirectory, path, asciiCaseInsensitive);
            errorMessage = null;
            return true;
        }
        catch (FileNotFoundException)
        {
            errorMessage = $"{path}: {OsErrorMessages.NoSuchFileOrDirectory}";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            errorMessage = $"{path}: {OsErrorMessages.NoSuchFileOrDirectory}";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = $"{path}: {OsErrorMessages.PermissionDenied}";
            return false;
        }
        catch (IOException exception)
        {
            errorMessage = $"{path}: {exception.Message}";
            return false;
        }
    }

    internal IgnoreGlobSetSummary GetGlobSetSummary(int startIndex)
    {
        var summary = new IgnoreGlobSetSummary(0, 0, 0, 0, 0, 0, 0);
        for (int index = startIndex; index < rules.Count; index++)
        {
            summary = summary.Add(rules[index].GetGlobSetSummary());
        }

        return summary;
    }

    private void AddFileLines(string baseDirectory, string path, bool asciiCaseInsensitive)
    {
        bool firstLine = true;
        foreach (string line in File.ReadLines(path))
        {
            string currentLine = firstLine ? line.TrimStart('\uFEFF') : line;
            firstLine = false;

            if (IgnoreRule.TryParse(baseDirectory, currentLine, path, asciiCaseInsensitive, out IgnoreRule? rule) && rule is not null)
            {
                Add(rule);
            }
        }
    }

    public IgnoreDecision Match(DirEntry entry)
    {
        return Match(entry, out _);
    }

    internal IgnoreDecision Match(DirEntry entry, out IgnoreRule? matchedRule)
    {
        matchedRule = null;
        if (rules.Count == 0)
        {
            return IgnoreDecision.None;
        }

        string? relativePath = GetRelativePath(entry.FullPath);
        if (relativePath is null)
        {
            return MatchSlow(entry, out matchedRule);
        }

        var candidate = GlobCandidate.FromBytes(System.Text.Encoding.UTF8.GetBytes(relativePath));
        ReadOnlySpan<bool> eligible = entry.IsDirectory ? [] : GetFileRuleEligibility();
        int matchedIndex = GetGlobSet().LastMatchingIndex(candidate, eligible);
        if (matchedIndex < 0)
        {
            return IgnoreDecision.None;
        }

        matchedRule = rules[matchedIndex];
        return matchedRule.IsWhitelist ? IgnoreDecision.Whitelist : IgnoreDecision.Ignore;
    }

    private IgnoreDecision MatchSlow(DirEntry entry, out IgnoreRule? matchedRule)
    {
        IgnoreDecision decision = IgnoreDecision.None;
        matchedRule = null;
        for (int index = 0; index < rules.Count; index++)
        {
            IgnoreRule rule = rules[index];
            IgnoreDecision current = rule.Match(entry);
            if (current != IgnoreDecision.None)
            {
                decision = current;
                matchedRule = rule;
            }
        }

        return decision;
    }

    private GlobSet GetGlobSet()
    {
        if (globSet is not null)
        {
            return globSet;
        }

        var globs = new Glob[rules.Count];
        for (int index = 0; index < rules.Count; index++)
        {
            globs[index] = rules[index].Glob;
        }

        globSet = GlobSet.Create(globs);
        return globSet;
    }

    private bool[] GetFileRuleEligibility()
    {
        if (fileRuleEligibility is not null)
        {
            return fileRuleEligibility;
        }

        fileRuleEligibility = new bool[rules.Count];
        for (int index = 0; index < rules.Count; index++)
        {
            fileRuleEligibility[index] = !rules[index].IsDirectoryOnly;
        }

        return fileRuleEligibility;
    }

    private string? GetRelativePath(string fullPath)
    {
        if (baseDirectory is null)
        {
            return null;
        }

        string relative = Path.GetRelativePath(baseDirectory, fullPath);
        if (relative == "." || PathUtil.IsRelativePathOutsideBase(relative))
        {
            return null;
        }

        return NormalizePattern(relative);
    }

    public IgnoreDecision MatchPathOrAnyParents(DirEntry entry)
    {
        if (baseDirectory is not null && !PathUtil.IsPathUnderBase(baseDirectory, entry.FullPath))
        {
            throw new ArgumentException("path is expected to be under the root", nameof(entry));
        }

        IgnoreDecision decision = Match(entry);
        if (decision != IgnoreDecision.None)
        {
            return decision;
        }

        string? parent = Path.GetDirectoryName(entry.FullPath);
        while (!string.IsNullOrEmpty(parent))
        {
            var parentEntry = new DirEntry(
                parent,
                entry.Depth,
                FileAttributes.Directory,
                isDirectory: true,
                isSymbolicLink: false,
                isStdin: false,
                length: null,
                identity: default);

            decision = Match(parentEntry);
            if (decision != IgnoreDecision.None)
            {
                return decision;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return IgnoreDecision.None;
    }

    private static string NormalizePattern(string text)
    {
        return text.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
