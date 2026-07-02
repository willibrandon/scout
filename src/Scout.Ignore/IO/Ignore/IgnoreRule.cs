using System.Text;

namespace Scout.IO.Ignore;

internal sealed class IgnoreRule
{
    private const int FastKindNone = 0;
    private const int FastKindExact = 1;
    private const int FastKindSuffix = 2;
    private const int FastKindPrefix = 3;
    private const int FastKindAll = 4;

    private readonly string baseDirectory;
    private readonly string patternText;
    private readonly string originalText;
    private readonly string? sourcePath;
    private readonly bool negated;
    private readonly bool directoryOnly;
    private readonly bool basenameOnly;
    private readonly bool asciiCaseInsensitive;
    private readonly int fastKind;
    private readonly string fastText;
    private readonly Glob glob;

    private IgnoreRule(
        string baseDirectory,
        string patternText,
        string originalText,
        string? sourcePath,
        bool negated,
        bool directoryOnly,
        bool rooted,
        bool asciiCaseInsensitive)
    {
        this.baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        this.patternText = patternText;
        this.originalText = originalText;
        this.sourcePath = sourcePath;
        this.negated = negated;
        this.directoryOnly = directoryOnly;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        basenameOnly = !rooted && !ContainsSeparator(patternText);
        fastKind = DetectFastKind(patternText, basenameOnly, out fastText);
        glob = Glob.Parse(
            Encoding.UTF8.GetBytes(patternText),
            new GlobOptions(
                literalSeparator: true,
                asciiCaseInsensitive: asciiCaseInsensitive,
                matchBaseName: basenameOnly,
                allowUnclosedClass: true));
    }

    internal string BaseDirectory => baseDirectory;

    internal string PatternText => patternText;

    internal string OriginalText => originalText;

    internal string? SourcePath => sourcePath;

    internal bool IsWhitelist => negated;

    internal bool IsDirectoryOnly => directoryOnly;

    internal bool IsBasenameOnly => basenameOnly;

    internal int FastKind => fastKind;

    internal string FastText => fastText;

    internal bool NeedsRelativePath => !basenameOnly;

    internal Glob Glob => glob;

    internal IgnoreGlobSetSummary GetGlobSetSummary()
    {
        if (fastKind == FastKindExact)
        {
            return basenameOnly
                ? new IgnoreGlobSetSummary(0, 1, 0, 0, 0, 0, 0)
                : new IgnoreGlobSetSummary(1, 0, 0, 0, 0, 0, 0);
        }

        if (fastKind == FastKindPrefix)
        {
            return new IgnoreGlobSetSummary(0, 0, 0, 1, 0, 0, 0);
        }

        if (fastKind == FastKindSuffix)
        {
            return basenameOnly && fastText.Length > 0 && fastText[0] == '.'
                ? new IgnoreGlobSetSummary(0, 0, 1, 0, 0, 0, 0)
                : new IgnoreGlobSetSummary(0, 0, 0, 0, 1, 0, 0);
        }

        return new IgnoreGlobSetSummary(0, 0, 0, 0, 0, 0, 1);
    }

    public static bool TryParse(string baseDirectory, string line, bool asciiCaseInsensitive, out IgnoreRule? rule)
    {
        return TryParse(baseDirectory, line, sourcePath: null, asciiCaseInsensitive, out rule);
    }

    internal static bool TryParse(string baseDirectory, string line, string? sourcePath, bool asciiCaseInsensitive, out IgnoreRule? rule)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        ArgumentNullException.ThrowIfNull(line);

        rule = null;
        string original = line;
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

        rule = new IgnoreRule(baseDirectory, NormalizePattern(trimmed), original, sourcePath, negated, directoryOnly, rooted, asciiCaseInsensitive);
        return true;
    }

    public IgnoreDecision Match(DirEntry entry)
    {
        return Match(entry, relativePath: null);
    }

    internal IgnoreDecision Match(DirEntry entry, string? relativePath)
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
            relativePath ??= GetRelativePath(entry.FullPath);
            if (relativePath is null)
            {
                return IgnoreDecision.None;
            }

            candidate = relativePath;
        }

        if (!IsMatch(candidate))
        {
            return IgnoreDecision.None;
        }

        return negated ? IgnoreDecision.Whitelist : IgnoreDecision.Ignore;
    }

    private string? GetRelativePath(string fullPath)
    {
        string relative = Path.GetRelativePath(baseDirectory, fullPath);
        if (relative == "." || PathUtil.IsRelativePathOutsideBase(relative))
        {
            return null;
        }

        return NormalizePattern(relative);
    }

    private bool IsMatch(string candidate)
    {
        return fastKind switch
        {
            FastKindExact => EqualsPattern(candidate, patternText, asciiCaseInsensitive),
            FastKindSuffix => EndsWithPattern(candidate, fastText, asciiCaseInsensitive),
            FastKindPrefix => StartsWithPattern(candidate, fastText, asciiCaseInsensitive),
            FastKindAll => true,
            _ => glob.IsMatch(Encoding.UTF8.GetBytes(candidate)),
        };
    }

    private static int DetectFastKind(string pattern, bool basenameOnly, out string fastText)
    {
        fastText = pattern;
        if (pattern.Length == 0 || pattern.Contains('\\', StringComparison.Ordinal))
        {
            return FastKindNone;
        }

        if (pattern == "**")
        {
            return FastKindAll;
        }

        if (basenameOnly && pattern[0] == '*' && !ContainsGlobMeta(pattern.AsSpan(1)))
        {
            fastText = pattern[1..];
            return fastText.Length == 0 ? FastKindNone : FastKindSuffix;
        }

        if (basenameOnly && pattern[^1] == '*' && !ContainsGlobMeta(pattern.AsSpan(0, pattern.Length - 1)))
        {
            fastText = pattern[..^1];
            return fastText.Length == 0 ? FastKindNone : FastKindPrefix;
        }

        return ContainsGlobMeta(pattern) ? FastKindNone : FastKindExact;
    }

    private static bool EqualsPattern(string candidate, string pattern, bool asciiCaseInsensitive)
    {
        return asciiCaseInsensitive
            ? EqualsAsciiIgnoreCase(candidate, pattern)
            : string.Equals(candidate, pattern, StringComparison.Ordinal);
    }

    private static bool EndsWithPattern(string candidate, string suffix, bool asciiCaseInsensitive)
    {
        if (suffix.Length > candidate.Length)
        {
            return false;
        }

        return EqualsPattern(candidate[^suffix.Length..], suffix, asciiCaseInsensitive);
    }

    private static bool StartsWithPattern(string candidate, string prefix, bool asciiCaseInsensitive)
    {
        if (prefix.Length > candidate.Length)
        {
            return false;
        }

        return EqualsPattern(candidate[..prefix.Length], prefix, asciiCaseInsensitive);
    }

    private static bool EqualsAsciiIgnoreCase(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (FoldAscii(left[index]) != FoldAscii(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static char FoldAscii(char value)
    {
        return value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;
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

    private static bool ContainsGlobMeta(ReadOnlySpan<char> text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is '*' or '?' or '[')
            {
                return true;
            }
        }

        return false;
    }
}
