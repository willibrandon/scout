using System.Text;

namespace Scout.IO.Ignore;

/// <summary>
/// Stores ordered ignore rules that share a base directory and matches them as a set.
/// </summary>
internal sealed class IgnoreRuleSet
{
    private readonly List<IgnoreRule> _rules = [];
    private string? _baseDirectory;
    private GlobSet? _globSet;
    private bool[]? _fileRuleEligibility;

    /// <summary>
    /// Gets a value indicating whether the set contains no rules.
    /// </summary>
    public bool IsEmpty => _rules.Count == 0;

    /// <summary>
    /// Gets the number of rules in the set.
    /// </summary>
    internal int Count => _rules.Count;

    /// <summary>
    /// Adds an ignore rule to the end of the set.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    public void Add(IgnoreRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        _baseDirectory ??= rule.BaseDirectory;
        _rules.Add(rule);
        _globSet = null;
        _fileRuleEligibility = null;
    }

    /// <summary>
    /// Adds rules parsed from an ignore file.
    /// </summary>
    /// <param name="baseDirectory">The directory relative to which patterns are matched.</param>
    /// <param name="path">The ignore file path.</param>
    /// <param name="asciiCaseInsensitive">Whether patterns use ASCII-insensitive matching.</param>
    public void AddFile(string baseDirectory, string path, bool asciiCaseInsensitive)
    {
        if (!TryAddFile(baseDirectory, path, asciiCaseInsensitive, out string? errorMessage))
        {
            throw new IOException(errorMessage);
        }
    }

    /// <summary>
    /// Tries to add rules parsed from an ignore file.
    /// </summary>
    /// <param name="baseDirectory">The directory relative to which patterns are matched.</param>
    /// <param name="path">The ignore file path.</param>
    /// <param name="asciiCaseInsensitive">Whether patterns use ASCII-insensitive matching.</param>
    /// <param name="errorMessage">The parsing or I/O error when the file cannot be added.</param>
    /// <returns><see langword="true" /> when the file was added successfully.</returns>
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

    /// <summary>
    /// Summarizes the glob strategies used by rules at and after an index.
    /// </summary>
    /// <param name="startIndex">The first rule index to include.</param>
    /// <returns>The glob strategy summary.</returns>
    internal IgnoreGlobSetSummary GetGlobSetSummary(int startIndex)
    {
        var summary = new IgnoreGlobSetSummary(0, 0, 0, 0, 0, 0, 0);
        for (int index = startIndex; index < _rules.Count; index++)
        {
            summary = summary.Add(_rules[index].GetGlobSetSummary());
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

    /// <summary>
    /// Matches a directory entry against the ordered rules.
    /// </summary>
    /// <param name="entry">The directory entry to match.</param>
    /// <returns>The last matching rule's decision.</returns>
    public IgnoreDecision Match(DirEntry entry)
    {
        return Match(entry, out _);
    }

    /// <summary>
    /// Matches a directory entry and returns the rule responsible for the decision.
    /// </summary>
    /// <param name="entry">The directory entry to match.</param>
    /// <param name="matchedRule">The last matching rule, when one exists.</param>
    /// <returns>The last matching rule's decision.</returns>
    internal IgnoreDecision Match(DirEntry entry, out IgnoreRule? matchedRule)
    {
        matchedRule = null;
        if (_rules.Count == 0)
        {
            return IgnoreDecision.None;
        }

        string? relativePath = GetRelativePath(entry.FullPath);
        if (relativePath is null)
        {
            return MatchSlow(entry, out matchedRule);
        }

        var candidate = GlobCandidate.FromBytes(Encoding.UTF8.GetBytes(relativePath));
        ReadOnlySpan<bool> eligible = entry.IsDirectory ? [] : GetFileRuleEligibility();
        int matchedIndex = GetGlobSet().LastMatchingIndex(candidate, eligible);
        if (matchedIndex < 0)
        {
            return IgnoreDecision.None;
        }

        matchedRule = _rules[matchedIndex];
        return matchedRule.IsWhitelist ? IgnoreDecision.Whitelist : IgnoreDecision.Ignore;
    }

    private IgnoreDecision MatchSlow(DirEntry entry, out IgnoreRule? matchedRule)
    {
        IgnoreDecision decision = IgnoreDecision.None;
        matchedRule = null;
        for (int index = 0; index < _rules.Count; index++)
        {
            IgnoreRule rule = _rules[index];
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
        if (_globSet is not null)
        {
            return _globSet;
        }

        var globs = new Glob[_rules.Count];
        for (int index = 0; index < _rules.Count; index++)
        {
            globs[index] = _rules[index].Glob;
        }

        _globSet = GlobSet.Create(globs);
        return _globSet;
    }

    private bool[] GetFileRuleEligibility()
    {
        if (_fileRuleEligibility is not null)
        {
            return _fileRuleEligibility;
        }

        _fileRuleEligibility = new bool[_rules.Count];
        for (int index = 0; index < _rules.Count; index++)
        {
            _fileRuleEligibility[index] = !_rules[index].IsDirectoryOnly;
        }

        return _fileRuleEligibility;
    }

    private string? GetRelativePath(string fullPath)
    {
        if (_baseDirectory is null)
        {
            return null;
        }

        if (!OperatingSystem.IsWindows() && TryGetRelativePathStart(fullPath, out int relativeStart))
        {
            return NormalizePattern(fullPath[relativeStart..]);
        }

        string relative = Path.GetRelativePath(_baseDirectory, fullPath);
        if (relative == "." || PathUtil.IsRelativePathOutsideBase(relative))
        {
            return null;
        }

        return NormalizePattern(relative);
    }

    private bool TryGetRelativePathStart(string fullPath, out int relativeStart)
    {
        string baseDirectory = _baseDirectory!;
        relativeStart = 0;
        if (fullPath.Length <= baseDirectory.Length ||
            !fullPath.StartsWith(baseDirectory, StringComparison.Ordinal))
        {
            return false;
        }

        if (Path.EndsInDirectorySeparator(baseDirectory))
        {
            relativeStart = baseDirectory.Length;
            return true;
        }

        char boundary = fullPath[baseDirectory.Length];
        if (boundary != Path.DirectorySeparatorChar &&
            boundary != Path.AltDirectorySeparatorChar)
        {
            return false;
        }

        relativeStart = baseDirectory.Length + 1;
        return relativeStart < fullPath.Length;
    }

    /// <summary>
    /// Matches a path and then each of its parents against the ordered rules.
    /// </summary>
    /// <param name="entry">The path entry to match.</param>
    /// <returns>The first decision found while walking toward the rule-set root.</returns>
    public IgnoreDecision MatchPathOrAnyParents(DirEntry entry)
    {
        if (_baseDirectory is not null && !PathUtil.IsPathUnderBase(_baseDirectory, entry.FullPath))
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
