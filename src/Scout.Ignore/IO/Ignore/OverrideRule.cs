using System.Text;

namespace Scout.IO.Ignore;

internal sealed class OverrideRule
{
    private readonly string baseDirectory;
    private readonly bool directoryOnly;
    private readonly bool basenameOnly;
    private readonly Glob glob;

    private OverrideRule(string baseDirectory, string patternText, IgnoreDecision decision, bool directoryOnly, bool asciiCaseInsensitive)
    {
        this.baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        Decision = decision;
        this.directoryOnly = directoryOnly;
        basenameOnly = !ContainsSeparator(patternText);
        glob = Glob.Parse(
            Encoding.UTF8.GetBytes(patternText),
            new GlobOptions(literalSeparator: true, asciiCaseInsensitive: asciiCaseInsensitive, matchBaseName: basenameOnly));
    }

    public IgnoreDecision Decision { get; }

    public static bool TryParse(string baseDirectory, string line, bool asciiCaseInsensitive, out OverrideRule? rule)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        ArgumentNullException.ThrowIfNull(line);

        rule = null;
        string trimmed = TrimTrailingWhitespace(line);
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return false;
        }

        IgnoreDecision decision = IgnoreDecision.Whitelist;
        if (trimmed[0] == '!')
        {
            decision = IgnoreDecision.Ignore;
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

        rule = new OverrideRule(baseDirectory, NormalizePattern(trimmed), decision, directoryOnly, asciiCaseInsensitive);
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

        return glob.IsMatch(Encoding.UTF8.GetBytes(candidate)) ? Decision : IgnoreDecision.None;
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
            end--;
        }

        return text[..end];
    }

    private static string TrimLeadingSlash(string text)
    {
        while (text.Length > 0 && (text[0] == '/' || text[0] == '\\'))
        {
            text = text[1..];
        }

        return text;
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
