
namespace Scout;

internal static class GlobalGitIgnore
{
    public static IgnoreRuleSet Load(string baseDirectory, bool asciiCaseInsensitive)
    {
        var rules = new IgnoreRuleSet();
        string? path = ResolveFilePath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return rules;
        }

        rules.AddFile(baseDirectory, path, asciiCaseInsensitive);
        return rules;
    }

    internal static string? ResolveFilePath()
    {
        return ResolveFilePath(ProcessEnvironment.GetVariable, File.Exists, File.ReadAllText);
    }

    internal static string? ResolveFilePath(
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        Func<string, string> readAllText)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(readAllText);

        string? home = GetHomeDirectory(getEnvironmentVariable);
        string? homeConfig = home is null ? null : CombinePath(home, ".gitconfig");
        string? fromHomeConfig = ResolveExcludesFile(homeConfig, home, fileExists, readAllText);
        if (fromHomeConfig is not null)
        {
            return fromHomeConfig;
        }

        string? xdgConfigHome = GetXdgConfigHome(getEnvironmentVariable, home);
        string? xdgConfig = xdgConfigHome is null ? null : CombinePath(xdgConfigHome, "git", "config");
        string? fromXdgConfig = ResolveExcludesFile(xdgConfig, home, fileExists, readAllText);
        if (fromXdgConfig is not null)
        {
            return fromXdgConfig;
        }

        return xdgConfigHome is null ? null : CombinePath(xdgConfigHome, "git", "ignore");
    }

    internal static string? ParseExcludesFile(string text, string? homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(text);

        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            int separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            if (!string.Equals(key, "excludesFile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1].Trim();
            }

            if (value.Length == 0 || ContainsWhitespace(value))
            {
                continue;
            }

            return ExpandTilde(value, homeDirectory);
        }

        return null;
    }

    private static string? ResolveExcludesFile(
        string? configPath,
        string? homeDirectory,
        Func<string, bool> fileExists,
        Func<string, string> readAllText)
    {
        if (string.IsNullOrEmpty(configPath) || !fileExists(configPath))
        {
            return null;
        }

        try
        {
            return ParseExcludesFile(readAllText(configPath), homeDirectory);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? GetHomeDirectory(Func<string, string?> getEnvironmentVariable)
    {
        string? home = getEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            return home;
        }

        string? userProfile = getEnvironmentVariable("USERPROFILE");
        return string.IsNullOrEmpty(userProfile) ? null : userProfile;
    }

    private static string? GetXdgConfigHome(Func<string, string?> getEnvironmentVariable, string? homeDirectory)
    {
        string? xdgConfigHome = getEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfigHome))
        {
            return xdgConfigHome;
        }

        return homeDirectory is null ? null : CombinePath(homeDirectory, ".config");
    }

    private static string CombinePath(string root, params string[] segments)
    {
        char separator = root.Contains('/') && !root.Contains('\\')
            ? '/'
            : Path.DirectorySeparatorChar;
        string path = root.TrimEnd('/', '\\');
        for (int index = 0; index < segments.Length; index++)
        {
            path += separator + segments[index];
        }

        return path;
    }

    private static string ExpandTilde(string path, string? homeDirectory)
    {
        return string.IsNullOrEmpty(homeDirectory)
            ? path
            : path.Replace("~", homeDirectory, StringComparison.Ordinal);
    }

    private static bool ContainsWhitespace(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}
