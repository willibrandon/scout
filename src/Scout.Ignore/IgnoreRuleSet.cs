using System;
using System.Collections.Generic;
using System.IO;

namespace Scout;

internal sealed class IgnoreRuleSet
{
    private readonly List<IgnoreRule> rules = [];
    private string? baseDirectory;

    public bool IsEmpty => rules.Count == 0;

    public void Add(IgnoreRule rule)
    {
        baseDirectory ??= rule.BaseDirectory;
        rules.Add(rule);
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
            errorMessage = $"{path}: line 1: Is a directory (os error 21)";
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
            errorMessage = $"{path}: No such file or directory (os error 2)";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            errorMessage = $"{path}: No such file or directory (os error 2)";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = $"{path}: Permission denied (os error 13)";
            return false;
        }
        catch (IOException exception)
        {
            errorMessage = $"{path}: {exception.Message}";
            return false;
        }
    }

    private void AddFileLines(string baseDirectory, string path, bool asciiCaseInsensitive)
    {
        bool firstLine = true;
        foreach (string line in File.ReadLines(path))
        {
            string currentLine = firstLine ? line.TrimStart('\uFEFF') : line;
            firstLine = false;

            if (IgnoreRule.TryParse(baseDirectory, currentLine, asciiCaseInsensitive, out IgnoreRule? rule) && rule is not null)
            {
                Add(rule);
            }
        }
    }

    public IgnoreDecision Match(DirEntry entry)
    {
        IgnoreDecision decision = IgnoreDecision.None;
        string? relativePath = null;
        bool relativePathComputed = false;
        for (int index = 0; index < rules.Count; index++)
        {
            IgnoreRule rule = rules[index];
            if (rule.NeedsRelativePath && !relativePathComputed)
            {
                relativePath = GetRelativePath(entry.FullPath);
                relativePathComputed = true;
            }

            if (rule.NeedsRelativePath && relativePath is null)
            {
                continue;
            }

            IgnoreDecision current = rule.Match(entry, relativePath);
            if (current != IgnoreDecision.None)
            {
                decision = current;
            }
        }

        return decision;
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
