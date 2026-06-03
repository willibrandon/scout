
namespace Scout;

/// <summary>
/// Verifies global gitignore path discovery.
/// </summary>
public sealed class GlobalGitIgnoreTests
{
    /// <summary>
    /// Verifies upstream <c>core.excludesFile</c> parsing cases.
    /// </summary>
    /// <param name="config">The git config text.</param>
    /// <param name="expected">The expected parsed path.</param>
    [Theory]
    [InlineData("[core]\nexcludesFile = /foo/bar", "/foo/bar")]
    [InlineData("[core]\nexcludesFile = ~/foo/bar", "/home/scout/foo/bar")]
    [InlineData("[core]\nexcludesFile = \"~/foo/bar\"", "/home/scout/foo/bar")]
    public void ParseExcludesFileMatchesUpstream(string config, string expected)
    {
        Assert.Equal(expected, GlobalGitIgnore.ParseExcludesFile(config, "/home/scout"));
    }

    /// <summary>
    /// Verifies invalid or unrelated upstream <c>core.excludesFile</c> cases do not produce a path.
    /// </summary>
    /// <param name="config">The git config text.</param>
    [Theory]
    [InlineData("[core]\nexcludeFile = /foo/bar")]
    [InlineData("[core]\nexcludesFile = \" \"~/foo/bar \" \"")]
    public void ParseExcludesFileRejectsUpstreamNonMatches(string config)
    {
        Assert.Null(GlobalGitIgnore.ParseExcludesFile(config, "/home/scout"));
    }

    /// <summary>
    /// Verifies the home git config takes precedence and expands tildes like upstream.
    /// </summary>
    [Fact]
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

        Assert.Equal("/home/scout/ignore", path);
    }

    /// <summary>
    /// Verifies XDG git config is used before the default XDG ignore path.
    /// </summary>
    [Fact]
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

        Assert.Equal("/configured/ignore", path);
    }

    /// <summary>
    /// Verifies the default global ignore path uses XDG when no git config file provides a value.
    /// </summary>
    [Fact]
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

        Assert.Equal("/xdg/git/ignore", path);
    }
}
