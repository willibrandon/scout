
namespace Scout;

/// <summary>
/// Verifies global gitignore path discovery.
/// </summary>
[TestClass]
public sealed class GlobalGitIgnoreTests
{
    /// <summary>
    /// Verifies global, home, XDG and system configuration precedence and fallthrough.
    /// </summary>
    [TestMethod]
    public void ConfigurationCandidatesFallThroughInReleaseOrder()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = "/home/scout",
            ["XDG_CONFIG_HOME"] = "/xdg",
            ["GIT_CONFIG_GLOBAL"] = "/global-config",
            ["GIT_CONFIG_SYSTEM"] = "/system-config",
        };
        string[] candidates = ["/global-config", "/home/scout/.gitconfig", "/xdg/git/config", "/system-config"];
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string candidate in candidates)
        {
            files[candidate] = "excludesFile = " + candidate + ".ignore\n";
        }

        foreach (string candidate in candidates)
        {
            Assert.AreEqual(candidate + ".ignore", GlobalGitIgnore.ResolveFilePath(
                name => environment.GetValueOrDefault(name), files.ContainsKey, path => files[path]));
            files[candidate] = "[core]\neditor = vim\n";
        }

        Assert.AreEqual("/xdg/git/ignore", GlobalGitIgnore.ResolveFilePath(
            name => environment.GetValueOrDefault(name), files.ContainsKey, path => files[path]));
        environment["GIT_CONFIG_SYSTEM"] = string.Empty;
        files["/etc/gitconfig"] = "excludesFile = /system-default-ignore\n";
        Assert.AreEqual("/system-default-ignore", GlobalGitIgnore.ResolveFilePath(
            name => environment.GetValueOrDefault(name), files.ContainsKey, path => files[path]));
    }

    /// <summary>
    /// Verifies unreadable global configuration falls through instead of overriding later sources.
    /// </summary>
    [TestMethod]
    public void UnreadableGlobalConfigFallsThrough()
    {
        Assert.AreEqual("/home/scout/ignore", GlobalGitIgnore.ResolveFilePath(
            name => name == "HOME" ? "/home/scout" : name == "GIT_CONFIG_GLOBAL" ? "/unreadable" : null,
            path => path is "/unreadable" or "/home/scout/.gitconfig",
            path => path == "/unreadable" ? throw new UnauthorizedAccessException() : "excludesFile = ~/ignore\n"));
    }

    /// <summary>
    /// Verifies the first byte-regex match must decode as UTF-8, including after unrelated invalid bytes.
    /// </summary>
    [TestMethod]
    public void ConfigExtractionUsesBytesAndValidatesOnlyTheCandidate()
    {
        Assert.AreEqual("/valid", GlobalGitIgnore.ParseExcludesFile([0xFF, .. "\nExcludesFile = \" /valid \"\n"u8], null));
        byte[] config = [.. "excludesFile = /bad"u8, 0xFF, .. "\nexcludesFile = /valid\n"u8];
        Assert.IsNull(GlobalGitIgnore.ParseExcludesFile(config, null));
    }

    /// <summary>
    /// Verifies a raw environment path is preserved while selecting a UTF-8 excludes-file value.
    /// </summary>
    [TestMethod]
    public void ConfigEnvironmentPathsPreserveUnixBytes()
    {
        var path = OsString.FromUnixBytes([.. "/config-"u8, 0xFF]);
        bool read = false;
        OsString? result = GlobalGitIgnore.ResolveOsFilePath(
            name => name == "GIT_CONFIG_GLOBAL" ? path : null,
            candidate =>
            {
                if (candidate.Equals(path))
                {
                    read = true;
                    return "excludesFile = /ignore\n"u8.ToArray();
                }

                return null;
            });
        Assert.IsTrue(read);
        Assert.IsTrue(result!.Value.TryGetText(out string text));
        Assert.AreEqual("/ignore", text);
    }

    /// <summary>
    /// Verifies upstream <c>core.excludesFile</c> parsing cases.
    /// </summary>
    /// <param name="config">The git config text.</param>
    /// <param name="expected">The expected parsed path.</param>
    [TestMethod]
    [DataRow("[core]\nexcludesFile = /foo/bar", "/foo/bar")]
    [DataRow("[core]\nexcludesFile = ~/foo/bar", "/home/scout/foo/bar")]
    [DataRow("[core]\nexcludesFile = \"~/foo/bar\"", "/home/scout/foo/bar")]
    public void ParseExcludesFileMatchesUpstream(string config, string expected)
    {
        Assert.AreEqual(expected, GlobalGitIgnore.ParseExcludesFile(config, "/home/scout"));
    }

    /// <summary>
    /// Verifies invalid or unrelated upstream <c>core.excludesFile</c> cases do not produce a path.
    /// </summary>
    /// <param name="config">The git config text.</param>
    [TestMethod]
    [DataRow("[core]\nexcludeFile = /foo/bar")]
    [DataRow("[core]\nexcludesFile = \" \"~/foo/bar \" \"")]
    public void ParseExcludesFileRejectsUpstreamNonMatches(string config)
    {
        Assert.IsNull(GlobalGitIgnore.ParseExcludesFile(config, "/home/scout"));
    }

    /// <summary>
    /// Verifies the home git config takes precedence and expands tildes like upstream.
    /// </summary>
    [TestMethod]
    public void HomeGitConfigExcludesFileTakesPrecedence()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = "/home/scout",
            ["XDG_CONFIG_HOME"] = "/xdg",
        };
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/home/scout/.gitconfig"] = "[core]\nexcludesFile = ~/ignore\n",
            ["/xdg/git/config"] = "[core]\nexcludesFile = /xdg/ignore\n",
        };

        string? path = GlobalGitIgnore.ResolveFilePath(
            name => environment.TryGetValue(name, out string? value) ? value : null,
            files.ContainsKey,
            path => files[path]);

        Assert.AreEqual("/home/scout/ignore", path);
    }

    /// <summary>
    /// Verifies XDG git config is used before the default XDG ignore path.
    /// </summary>
    [TestMethod]
    public void XdgGitConfigOverridesDefaultIgnorePath()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = "/home/scout",
            ["XDG_CONFIG_HOME"] = "/xdg",
        };
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/xdg/git/config"] = "[core]\nexcludesFile = \"/configured/ignore\"\n",
        };

        string? path = GlobalGitIgnore.ResolveFilePath(
            name => environment.TryGetValue(name, out string? value) ? value : null,
            files.ContainsKey,
            path => files[path]);

        Assert.AreEqual("/configured/ignore", path);
    }

    /// <summary>
    /// Verifies the default global ignore path uses XDG when no git config file provides a value.
    /// </summary>
    [TestMethod]
    public void DefaultGlobalIgnorePathUsesXdgConfigHome()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = "/home/scout",
            ["XDG_CONFIG_HOME"] = "/xdg",
        };
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        string? path = GlobalGitIgnore.ResolveFilePath(
            name => environment.TryGetValue(name, out string? value) ? value : null,
            files.ContainsKey,
            path => files[path]);

        Assert.AreEqual("/xdg/git/ignore", path);
    }
}
