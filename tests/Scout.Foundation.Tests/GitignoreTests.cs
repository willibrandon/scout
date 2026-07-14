
namespace Scout;

/// <summary>
/// Verifies gitignore matcher edge cases ported from upstream ignore tests.
/// </summary>
public sealed class GitignoreTests
{
    private const string RootSpec = "ROOT";
    private const string MatchedPathOrAnyParentsRules = """
        file_root_00
        file_root_01/
        file_root_02/*
        file_root_03/**

        /file_root_10
        /file_root_11/
        /file_root_12/*
        /file_root_13/**

        */file_root_20
        */file_root_21/
        */file_root_22/*
        */file_root_23/**

        **/file_root_30
        **/file_root_31/
        **/file_root_32/*
        **/file_root_33/**

        file_deep_00
        file_deep_01/
        file_deep_02/*
        file_deep_03/**

        /file_deep_10
        /file_deep_11/
        /file_deep_12/*
        /file_deep_13/**

        */file_deep_20
        */file_deep_21/
        */file_deep_22/*
        */file_deep_23/**

        **/file_deep_30
        **/file_deep_31/
        **/file_deep_32/*
        **/file_deep_33/**

        dir_root_00
        dir_root_01/
        dir_root_02/*
        dir_root_03/**

        /dir_root_10
        /dir_root_11/
        /dir_root_12/*
        /dir_root_13/**

        */dir_root_20
        */dir_root_21/
        */dir_root_22/*
        */dir_root_23/**

        **/dir_root_30
        **/dir_root_31/
        **/dir_root_32/*
        **/dir_root_33/**

        dir_deep_00
        dir_deep_01/
        dir_deep_02/*
        dir_deep_03/**

        /dir_deep_10
        /dir_deep_11/
        /dir_deep_12/*
        /dir_deep_13/**

        */dir_deep_20
        */dir_deep_21/
        */dir_deep_22/*
        */dir_deep_23/**

        **/dir_deep_30
        **/dir_deep_31/
        **/dir_deep_32/*
        **/dir_deep_33/**
        """;

    /// <summary>
    /// Verifies upstream gitignore positive match cases.
    /// </summary>
    /// <param name="rootSpec">The upstream matcher root spec.</param>
    /// <param name="gitignore">The gitignore pattern text.</param>
    /// <param name="pathSpec">The upstream candidate path spec.</param>
    /// <param name="isDirectory">Whether the candidate is a directory.</param>
    [Theory]
    [InlineData(RootSpec, "months", "months", false)]
    [InlineData(RootSpec, "*.lock", "Cargo.lock", false)]
    [InlineData(RootSpec, "*.rs", "src/main.rs", false)]
    [InlineData(RootSpec, "src/*.rs", "src/main.rs", false)]
    [InlineData(RootSpec, "/*.c", "cat-file.c", false)]
    [InlineData(RootSpec, "/src/*.rs", "src/main.rs", false)]
    [InlineData(RootSpec, "!src/main.rs\n*.rs", "src/main.rs", false)]
    [InlineData(RootSpec, "foo/", "foo", true)]
    [InlineData(RootSpec, "**/foo", "foo", false)]
    [InlineData(RootSpec, "**/foo", "src/foo", false)]
    [InlineData(RootSpec, "**/foo/**", "src/foo/bar", false)]
    [InlineData(RootSpec, "**/foo/**", "wat/src/foo/bar/baz", false)]
    [InlineData(RootSpec, "**/foo/bar", "foo/bar", false)]
    [InlineData(RootSpec, "**/foo/bar", "src/foo/bar", false)]
    [InlineData(RootSpec, "abc/**", "abc/x", false)]
    [InlineData(RootSpec, "abc/**", "abc/x/y", false)]
    [InlineData(RootSpec, "abc/**", "abc/x/y/z", false)]
    [InlineData(RootSpec, "a/**/b", "a/b", false)]
    [InlineData(RootSpec, "a/**/b", "a/x/b", false)]
    [InlineData(RootSpec, "a/**/b", "a/x/y/b", false)]
    [InlineData(RootSpec, "\\!xy", "!xy", false)]
    [InlineData(RootSpec, "\\#foo", "#foo", false)]
    [InlineData(RootSpec, "foo", "./foo", false)]
    [InlineData(RootSpec, "target", "grep/target", false)]
    [InlineData(RootSpec, "Cargo.lock", "./tabwriter-bin/Cargo.lock", false)]
    [InlineData(RootSpec, "/foo/bar/baz", "./foo/bar/baz", false)]
    [InlineData(RootSpec, "foo/", "xyz/foo", true)]
    [InlineData("./src", "/llvm/", "./src/llvm", true)]
    [InlineData(RootSpec, "node_modules/ ", "node_modules", true)]
    [InlineData(RootSpec, "**/", "foo/bar", true)]
    [InlineData(RootSpec, "path1/*", "path1/foo", false)]
    [InlineData(RootSpec, ".a/b", ".a/b", false)]
    [InlineData("./", ".a/b", ".a/b", false)]
    [InlineData(".", ".a/b", ".a/b", false)]
    [InlineData("./.", ".a/b", ".a/b", false)]
    [InlineData("././", ".a/b", ".a/b", false)]
    [InlineData("././.", ".a/b", ".a/b", false)]
    [InlineData(RootSpec, "\\[", "[", false)]
    [InlineData(RootSpec, "\\?", "?", false)]
    [InlineData(RootSpec, "\\*", "*", false)]
    [InlineData(RootSpec, "\\a", "a", false)]
    [InlineData(RootSpec, "s*.rs", "sfoo.rs", false)]
    [InlineData(RootSpec, "**", "foo.rs", false)]
    [InlineData(RootSpec, "**/**/*", "a/foo.rs", false)]
    public void UpstreamPositiveMatchedCasesMatch(string rootSpec, string gitignore, string pathSpec, bool isDirectory)
    {
        string workspace = CreateTempDirectory();
        IgnoreRuleSet rules = LoadRulesForText(GetRootPath(workspace, rootSpec), gitignore);

        Assert.Equal(IgnoreDecision.Ignore, rules.Match(CreateEntryForSpec(workspace, pathSpec, isDirectory)));
    }

    /// <summary>
    /// Verifies upstream gitignore negative match cases.
    /// </summary>
    /// <param name="rootSpec">The upstream matcher root spec.</param>
    /// <param name="gitignore">The gitignore pattern text.</param>
    /// <param name="pathSpec">The upstream candidate path spec.</param>
    /// <param name="isDirectory">Whether the candidate is a directory.</param>
    [Theory]
    [InlineData(RootSpec, "amonths", "months", false)]
    [InlineData(RootSpec, "monthsa", "months", false)]
    [InlineData(RootSpec, "/src/*.rs", "src/grep/src/main.rs", false)]
    [InlineData(RootSpec, "/*.c", "mozilla-sha1/sha1.c", false)]
    [InlineData(RootSpec, "*.rs\n!src/main.rs", "src/main.rs", false)]
    [InlineData(RootSpec, "foo/", "foo", false)]
    [InlineData(RootSpec, "**/foo/**", "wat/src/afoo/bar/baz", false)]
    [InlineData(RootSpec, "**/foo/**", "wat/src/fooa/bar/baz", false)]
    [InlineData(RootSpec, "**/foo/bar", "foo/src/bar", false)]
    [InlineData(RootSpec, "#foo", "#foo", false)]
    [InlineData(RootSpec, "\n\n\n", "foo", false)]
    [InlineData(RootSpec, "foo/**", "foo", true)]
    [InlineData("./third_party/protobuf", "m4/ltoptions.m4", "./third_party/protobuf/csharp/src/packages/repositories.config", false)]
    [InlineData(RootSpec, "!/bar", "foo/bar", false)]
    [InlineData(RootSpec, "*\n!**/", "foo", true)]
    [InlineData(RootSpec, "src/*.rs", "src/grep/src/main.rs", false)]
    [InlineData(RootSpec, "path1/*", "path2/path1/foo", false)]
    [InlineData(RootSpec, "s*.rs", "src/foo.rs", false)]
    public void UpstreamNegativeMatchedCasesDoNotMatch(string rootSpec, string gitignore, string pathSpec, bool isDirectory)
    {
        string workspace = CreateTempDirectory();
        IgnoreRuleSet rules = LoadRulesForText(GetRootPath(workspace, rootSpec), gitignore);

        Assert.NotEqual(IgnoreDecision.Ignore, rules.Match(CreateEntryForSpec(workspace, pathSpec, isDirectory)));
    }

    /// <summary>
    /// Verifies a leading byte-order mark is ignored on the first gitignore line.
    /// </summary>
    [Fact]
    public void GitignoreSkipsBomOnFirstLine()
    {
        string root = CreateTempDirectory();
        string ignoreFile = Path.Combine(root, ".gitignore");
        File.WriteAllText(ignoreFile, "\uFEFFignore/this/path\n");
        IgnoreRuleSet rules = LoadRules(root, ignoreFile);

        Assert.Equal(IgnoreDecision.Ignore, rules.Match(CreateEntry(root, "ignore/this/path", isDirectory: false)));
    }

    /// <summary>
    /// Verifies path-or-parent matching rejects absolute paths outside the matcher root like upstream.
    /// </summary>
    [Fact]
    public void MatchPathOrAnyParentsRejectsPathsOutsideRoot()
    {
        string workspace = CreateTempDirectory();
        string root = Path.Combine(workspace, "ROOT");
        string outside = Path.Combine(workspace, "outside", "some_file");
        IgnoreRuleSet rules = LoadRulesForText(root, "some_file\n");

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => rules.MatchPathOrAnyParents(CreateAbsoluteEntry(outside, isDirectory: false)));
        Assert.Contains("path is expected to be under the root", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies direct matching does not treat a sibling with a shared text prefix as a descendant.
    /// </summary>
    [Fact]
    public void DirectMatchRequiresDirectorySeparatorAfterRootPrefix()
    {
        string workspace = CreateTempDirectory();
        string root = Path.Combine(workspace, "root");
        string sibling = Path.Combine(workspace, "root-sibling", "target");
        IgnoreRuleSet rules = LoadRulesForText(root, "/target\n");

        Assert.Equal(IgnoreDecision.Ignore, rules.Match(CreateEntry(root, "target", isDirectory: false)));
        Assert.Equal(IgnoreDecision.None, rules.Match(CreateAbsoluteEntry(sibling, isDirectory: false)));
    }

    /// <summary>
    /// Verifies parent-like path names under the matcher root are still ordinary relative paths.
    /// </summary>
    [Fact]
    public void MatchPathOrAnyParentsAllowsDotDotPrefixedNamesUnderRoot()
    {
        string root = CreateTempDirectory();
        IgnoreRuleSet rules = LoadRulesForText(root, "/..foo\n/sub/..bar\n");

        AssertIgnored(rules, root, "..foo");
        AssertIgnored(rules, root, "sub/..bar");
    }

    /// <summary>
    /// Verifies upstream path-or-parent matching cases for files in the matcher root.
    /// </summary>
    [Fact]
    public void MatchPathOrAnyParentsFilesInRootMatchesUpstream()
    {
        string root = CreateTempDirectory();
        IgnoreRuleSet rules = LoadMatchedPathRules(root);

        AssertIgnored(rules, root, "file_root_00");
        AssertNone(rules, root, "file_root_01");
        AssertNone(rules, root, "file_root_02");
        AssertNone(rules, root, "file_root_03");

        AssertIgnored(rules, root, "file_root_10");
        AssertNone(rules, root, "file_root_11");
        AssertNone(rules, root, "file_root_12");
        AssertNone(rules, root, "file_root_13");

        AssertNone(rules, root, "file_root_20");
        AssertNone(rules, root, "file_root_21");
        AssertNone(rules, root, "file_root_22");
        AssertNone(rules, root, "file_root_23");

        AssertIgnored(rules, root, "file_root_30");
        AssertNone(rules, root, "file_root_31");
        AssertNone(rules, root, "file_root_32");
        AssertNone(rules, root, "file_root_33");
    }

    /// <summary>
    /// Verifies upstream path-or-parent matching cases for files below a parent directory.
    /// </summary>
    [Fact]
    public void MatchPathOrAnyParentsFilesInDeepDirectoryMatchesUpstream()
    {
        string root = CreateTempDirectory();
        IgnoreRuleSet rules = LoadMatchedPathRules(root);

        AssertIgnored(rules, root, "parent_dir/file_deep_00");
        AssertNone(rules, root, "parent_dir/file_deep_01");
        AssertNone(rules, root, "parent_dir/file_deep_02");
        AssertNone(rules, root, "parent_dir/file_deep_03");

        AssertNone(rules, root, "parent_dir/file_deep_10");
        AssertNone(rules, root, "parent_dir/file_deep_11");
        AssertNone(rules, root, "parent_dir/file_deep_12");
        AssertNone(rules, root, "parent_dir/file_deep_13");

        AssertIgnored(rules, root, "parent_dir/file_deep_20");
        AssertNone(rules, root, "parent_dir/file_deep_21");
        AssertNone(rules, root, "parent_dir/file_deep_22");
        AssertNone(rules, root, "parent_dir/file_deep_23");

        AssertIgnored(rules, root, "parent_dir/file_deep_30");
        AssertNone(rules, root, "parent_dir/file_deep_31");
        AssertNone(rules, root, "parent_dir/file_deep_32");
        AssertNone(rules, root, "parent_dir/file_deep_33");
    }

    /// <summary>
    /// Verifies upstream path-or-parent matching cases for directories in the matcher root.
    /// </summary>
    [Fact]
    public void MatchPathOrAnyParentsDirectoriesInRootMatchUpstream()
    {
        string root = CreateTempDirectory();
        IgnoreRuleSet rules = LoadMatchedPathRules(root);

        AssertDirectoryTreeIgnored(rules, root, "dir_root_00");
        AssertDirectoryTreeIgnored(rules, root, "dir_root_01");
        AssertNone(rules, root, "dir_root_02", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "dir_root_02");
        AssertNone(rules, root, "dir_root_03", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "dir_root_03");

        AssertDirectoryTreeIgnored(rules, root, "dir_root_10");
        AssertDirectoryTreeIgnored(rules, root, "dir_root_11");
        AssertNone(rules, root, "dir_root_12", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "dir_root_12");
        AssertNone(rules, root, "dir_root_13", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "dir_root_13");

        AssertDirectoryTreeNone(rules, root, "dir_root_20");
        AssertDirectoryTreeNone(rules, root, "dir_root_21");
        AssertDirectoryTreeNone(rules, root, "dir_root_22");
        AssertDirectoryTreeNone(rules, root, "dir_root_23");

        AssertDirectoryTreeIgnored(rules, root, "dir_root_30");
        AssertDirectoryTreeIgnored(rules, root, "dir_root_31");
        AssertNone(rules, root, "dir_root_32", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "dir_root_32");
        AssertNone(rules, root, "dir_root_33", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "dir_root_33");
    }

    /// <summary>
    /// Verifies upstream path-or-parent matching cases for directories below a parent directory.
    /// </summary>
    [Fact]
    public void MatchPathOrAnyParentsDirectoriesInDeepDirectoryMatchUpstream()
    {
        string root = CreateTempDirectory();
        IgnoreRuleSet rules = LoadMatchedPathRules(root);

        AssertDirectoryTreeIgnored(rules, root, "parent_dir/dir_deep_00");
        AssertDirectoryTreeIgnored(rules, root, "parent_dir/dir_deep_01");
        AssertDirectoryTreeNone(rules, root, "parent_dir/dir_deep_02");
        AssertDirectoryTreeNone(rules, root, "parent_dir/dir_deep_03");

        AssertDirectoryTreeNone(rules, root, "parent_dir/dir_deep_10");
        AssertDirectoryTreeNone(rules, root, "parent_dir/dir_deep_11");
        AssertDirectoryTreeNone(rules, root, "parent_dir/dir_deep_12");
        AssertDirectoryTreeNone(rules, root, "parent_dir/dir_deep_13");

        AssertDirectoryTreeIgnored(rules, root, "parent_dir/dir_deep_20");
        AssertDirectoryTreeIgnored(rules, root, "parent_dir/dir_deep_21");
        AssertNone(rules, root, "parent_dir/dir_deep_22", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "parent_dir/dir_deep_22");
        AssertNone(rules, root, "parent_dir/dir_deep_23", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "parent_dir/dir_deep_23");

        AssertDirectoryTreeIgnored(rules, root, "parent_dir/dir_deep_30");
        AssertDirectoryTreeIgnored(rules, root, "parent_dir/dir_deep_31");
        AssertNone(rules, root, "parent_dir/dir_deep_32", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "parent_dir/dir_deep_32");
        AssertNone(rules, root, "parent_dir/dir_deep_33", isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, "parent_dir/dir_deep_33");
    }

    private static void AssertDirectoryTreeIgnored(IgnoreRuleSet rules, string root, string directory)
    {
        AssertIgnored(rules, root, directory, isDirectory: true);
        AssertDirectoryChildrenIgnored(rules, root, directory);
    }

    private static void AssertDirectoryChildrenIgnored(IgnoreRuleSet rules, string root, string directory)
    {
        AssertIgnored(rules, root, directory + "/file");
        AssertIgnored(rules, root, directory + "/child_dir", isDirectory: true);
        AssertIgnored(rules, root, directory + "/child_dir/file");
    }

    private static void AssertDirectoryTreeNone(IgnoreRuleSet rules, string root, string directory)
    {
        AssertNone(rules, root, directory, isDirectory: true);
        AssertNone(rules, root, directory + "/file");
        AssertNone(rules, root, directory + "/child_dir", isDirectory: true);
        AssertNone(rules, root, directory + "/child_dir/file");
    }

    private static void AssertIgnored(IgnoreRuleSet rules, string root, string relativePath, bool isDirectory = false)
    {
        Assert.Equal(IgnoreDecision.Ignore, rules.MatchPathOrAnyParents(CreateEntry(root, relativePath, isDirectory)));
    }

    private static void AssertNone(IgnoreRuleSet rules, string root, string relativePath, bool isDirectory = false)
    {
        Assert.Equal(IgnoreDecision.None, rules.MatchPathOrAnyParents(CreateEntry(root, relativePath, isDirectory)));
    }

    private static IgnoreRuleSet LoadMatchedPathRules(string root)
    {
        string ignoreFile = Path.Combine(root, ".gitignore");
        File.WriteAllText(ignoreFile, MatchedPathOrAnyParentsRules);
        return LoadRules(root, ignoreFile);
    }

    private static IgnoreRuleSet LoadRulesForText(string root, string gitignore)
    {
        Directory.CreateDirectory(root);
        string ignoreFile = Path.Combine(root, ".gitignore");
        File.WriteAllText(ignoreFile, gitignore);
        return LoadRules(root, ignoreFile);
    }

    private static IgnoreRuleSet LoadRules(string root, string ignoreFile)
    {
        var rules = new IgnoreRuleSet();
        rules.AddFile(root, ignoreFile, asciiCaseInsensitive: false);
        return rules;
    }

    private static DirEntry CreateEntry(string root, string relativePath, bool isDirectory)
    {
        string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return CreateAbsoluteEntry(fullPath, isDirectory);
    }

    private static DirEntry CreateAbsoluteEntry(string fullPath, bool isDirectory)
    {
        return new DirEntry(
            fullPath,
            depth: 0,
            attributes: isDirectory ? FileAttributes.Directory : default,
            isDirectory: isDirectory,
            isSymbolicLink: false,
            isStdin: false,
            length: null,
            identity: default);
    }

    private static DirEntry CreateEntryForSpec(string workspace, string pathSpec, bool isDirectory)
    {
        return CreateEntry(workspace, NormalizePathSpec(pathSpec), isDirectory);
    }

    private static string GetRootPath(string workspace, string rootSpec)
    {
        if (rootSpec == RootSpec)
        {
            return workspace;
        }

        return Path.Combine(workspace, NormalizePathSpec(rootSpec));
    }

    private static string NormalizePathSpec(string pathSpec)
    {
        string normalized = pathSpec;
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized == ".")
        {
            return string.Empty;
        }

        return normalized;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
