using System;
using System.IO;
using System.Text;

namespace Scout;

internal sealed class IgnoreRule
{
    private readonly string baseDirectory;
    private readonly string patternText;
    private readonly bool negated;
    private readonly bool directoryOnly;
    private readonly bool basenameOnly;
    private readonly Glob glob;

    private IgnoreRule(
        string baseDirectory,
        string patternText,
        bool negated,
        bool directoryOnly,
        bool rooted,
        bool asciiCaseInsensitive)
    {
        this.baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        this.patternText = patternText;
        this.negated = negated;
        this.directoryOnly = directoryOnly;
        basenameOnly = !rooted && !ContainsSeparator(patternText);
        glob = Glob.Parse(
            Encoding.UTF8.GetBytes(patternText),
            new GlobOptions(
                literalSeparator: true,
                asciiCaseInsensitive: asciiCaseInsensitive,
                matchBaseName: basenameOnly,
                allowUnclosedClass: true));
    }

    public static bool TryParse(string baseDirectory, string line, bool asciiCaseInsensitive, out IgnoreRule? rule)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        ArgumentNullException.ThrowIfNull(line);

        rule = null;
        string trimmed = TrimTrailingWhitespace(line);
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed[0] == '#')
        {
            return false;
        }

        bool negated = false;
        if (trimmed[0] == '!')
        {
            negated = true;
            trimmed = trimmed[1..];
        }
        else if (trimmed.Length >= 2 && trimmed[0] == '\\' && (trimmed[1] == '!' || trimmed[1] == '#'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        bool rooted = StartsWithSlash(trimmed);
        bool directoryOnly = trimmed.EndsWith('/');
        if (directoryOnly)
        {
            trimmed = trimmed[..^1];
        }

        trimmed = TrimLeadingSlash(trimmed);
        if (trimmed.Length == 0)
        {
            return false;
        }

        rule = new IgnoreRule(baseDirectory, NormalizePattern(trimmed), negated, directoryOnly, rooted, asciiCaseInsensitive);
        return true;
    }

    public IgnoreDecision Match(DirEntry entry)
    {
        if (directoryOnly && !entry.IsDirectory)
        {
            return IgnoreDecision.None;
        }

        string candidate;
        if (basenameOnly)
        {
            candidate = entry.FileName;
        }
        else
        {
            string? relative = GetRelativePath(entry.FullPath);
            if (relative is null)
            {
                return IgnoreDecision.None;
            }

            candidate = relative;
        }

        if (!glob.IsMatch(Encoding.UTF8.GetBytes(candidate)))
        {
            return IgnoreDecision.None;
        }

        return negated ? IgnoreDecision.Whitelist : IgnoreDecision.Ignore;
    }

    private string? GetRelativePath(string fullPath)
    {
        string relative = Path.GetRelativePath(baseDirectory, fullPath);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        return NormalizePattern(relative);
    }

    private static string TrimTrailingWhitespace(string text)
    {
        int end = text.Length;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
        {
            if (IsEscaped(text, end - 1))
            {
                break;
            }

            end--;
        }

        return text[..end];
    }

    private static bool IsEscaped(string text, int index)
    {
        int slashCount = 0;
        for (int current = index - 1; current >= 0 && text[current] == '\\'; current--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private static string TrimLeadingSlash(string text)
    {
        while (text.Length > 0 && (text[0] == '/' || text[0] == '\\'))
        {
            text = text[1..];
        }

        return text;
    }

    private static bool StartsWithSlash(string text)
    {
        return text.Length > 0 && (text[0] == '/' || text[0] == '\\');
    }

    private static string NormalizePattern(string text)
    {
        return text.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static bool ContainsSeparator(string text)
    {
        return text.Contains('/', StringComparison.Ordinal) || text.Contains('\\', StringComparison.Ordinal);
    }
}
