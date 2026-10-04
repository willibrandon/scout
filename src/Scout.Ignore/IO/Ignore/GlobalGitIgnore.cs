using System.Text;

namespace Scout.IO.Ignore;

internal static class GlobalGitIgnore
{
    private static readonly UTF8Encoding Utf8Strict = new(false, true);
    private static readonly Lazy<RegexAutomaton> ExcludesPattern = new(static () => RegexAutomaton.Compile(
        "(?im-u)^\\s*excludesfile\\s*=\\s*\"?\\s*(\\S+?)\\s*\"?\\s*$"u8,
        caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false,
        specializationMode: RegexSpecializationMode.General));

    public static IgnoreRuleSet Load(string baseDirectory, bool asciiCaseInsensitive)
    {
        return Load(baseDirectory, asciiCaseInsensitive, default);
    }

    public static IgnoreRuleSet Load(string baseDirectory, bool asciiCaseInsensitive, DiagnosticLogger logger)
    {
        var rules = new IgnoreRuleSet();
        OsString? path = ResolveOsFilePath(ProcessEnvironment.GetVariableOsString, ReadConfig);
        if (path is null)
        {
            return rules;
        }

        byte[]? contents = ReadConfig(path.Value);
        if (contents is null)
        {
            return rules;
        }

        string displayPath = DisplayPath(path.Value);
        IgnoreDiagnosticLogging.LogOpenedIgnoreFile(logger, displayPath);
        using var reader = new StringReader(Encoding.UTF8.GetString(contents));
        bool firstLine = true;
        while (reader.ReadLine() is { } line)
        {
            string currentLine = firstLine ? line.TrimStart('\uFEFF') : line;
            firstLine = false;
            if (IgnoreRule.TryParse(baseDirectory, currentLine, displayPath, asciiCaseInsensitive, out IgnoreRule? rule) && rule is not null)
            {
                rules.Add(rule);
            }
        }

        IgnoreDiagnosticLogging.LogBuiltGlobSet(logger, rules.GetGlobSetSummary(0));
        return rules;
    }

    internal static string? ResolveFilePath()
    {
        OsString? path = ResolveOsFilePath(ProcessEnvironment.GetVariableOsString, ReadConfig);
        return path is null ? null : DisplayPath(path.Value);
    }

    internal static string? ResolveFilePath(
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        Func<string, string> readAllText)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(readAllText);
        OsString? path = ResolveOsFilePath(
            name => getEnvironmentVariable(name) is { } value ? OsString.FromText(value) : null,
            path =>
            {
                string text = DisplayPath(path);
                try
                {
                    return fileExists(text) ? Encoding.UTF8.GetBytes(readAllText(text)) : null;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            });
        return path is null ? null : DisplayPath(path.Value);
    }

    internal static OsString? ResolveOsFilePath(
        Func<string, OsString?> getEnvironmentVariable,
        Func<OsString, byte[]?> readFile)
    {
        OsString? home = NonEmpty(getEnvironmentVariable("HOME")) ?? NonEmpty(getEnvironmentVariable("USERPROFILE"));
        OsString? xdg = NonEmpty(getEnvironmentVariable("XDG_CONFIG_HOME")) ?? JoinPath(home, ".config");
        OsString? system = NonEmpty(getEnvironmentVariable("GIT_CONFIG_SYSTEM")) ?? OsString.FromText("/etc/gitconfig");
        OsString?[] configs =
        [
            NonEmpty(getEnvironmentVariable("GIT_CONFIG_GLOBAL")),
            JoinPath(home, ".gitconfig"),
            JoinPath(xdg, "git/config"),
            system,
        ];
        string? homeText = home is null ? null : DisplayPath(home.Value);
        foreach (OsString? config in configs)
        {
            if (config is { } file && readFile(file) is { } contents && ParseExcludesFile(contents, homeText) is { } excludes)
            {
                return OsString.FromText(excludes);
            }
        }

        return JoinPath(xdg, "git/ignore");
    }

    internal static string? ParseExcludesFile(string text, string? homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseExcludesFile(Encoding.UTF8.GetBytes(text), homeDirectory);
    }

    internal static string? ParseExcludesFile(ReadOnlySpan<byte> contents, string? homeDirectory)
    {
        RegexMatch? capture = ExcludesPattern.Value.FindCaptures(contents)?.GetGroup(1);
        if (capture is not { } span)
        {
            return null;
        }

        try
        {
            string candidate = Utf8Strict.GetString(contents.Slice(span.Start, span.Length));
            return string.IsNullOrEmpty(homeDirectory) ? candidate : candidate.Replace("~", homeDirectory, StringComparison.Ordinal);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static byte[]? ReadConfig(OsString path)
    {
        try
        {
            if (path.IsWindowsText)
            {
                return File.ReadAllBytes(path.AsWindowsString());
            }

            using Microsoft.Win32.SafeHandles.SafeFileHandle handle = RawUnixFile.OpenRead(path.AsUnixBytes());
            using var stream = new FileStream(handle, FileAccess.Read);
            using var contents = new MemoryStream();
            stream.CopyTo(contents);
            return contents.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static OsString? NonEmpty(OsString? value)
    {
        return value is { } path && (path.IsUnixBytes ? !path.AsUnixBytes().IsEmpty : path.AsWindowsString().Length != 0)
            ? path : null;
    }

    private static OsString? JoinPath(OsString? root, string suffix)
    {
        if (root is not { } path)
        {
            return null;
        }

        if (path.IsWindowsText)
        {
            string text = path.AsWindowsString();
            char separator = text.Contains('/') && !text.Contains('\\') ? '/' : System.IO.Path.DirectorySeparatorChar;
            return OsString.FromWindowsString(text.TrimEnd('/', '\\') + separator + suffix.Replace('/', separator));
        }

        return OsString.FromUnixBytes(RawUnixDirectory.Join(path.AsUnixBytes(), Encoding.UTF8.GetBytes(suffix)));
    }

    private static string DisplayPath(OsString path)
    {
        return path.IsWindowsText ? path.AsWindowsString() : Encoding.UTF8.GetString(path.AsUnixBytes());
    }
}
