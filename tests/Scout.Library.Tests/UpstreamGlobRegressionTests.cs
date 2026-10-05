using System.Text;
using Scout.IO.Globbing;
using Scout.IO.Ignore;

namespace Scout;

/// <summary>
/// Verifies release glob and file-type behavior through exported APIs.
/// </summary>
[TestClass]
public sealed class UpstreamGlobRegressionTests
{
    private static readonly string[] CandidatePaths = [
                "src/App.cs", "src/Other.cs", "abc", "def", "foo/abc", "foo/a.c", "foo/a.rs",
                "a/main.rs", "main.rs", "a/lib.rs", "ab/c/def", "ab/cd", "ab/x", "a", "a/b/c/main.rs", "foo.rs", "as", "", "..",
];

    /// <summary>
    /// Verifies MatchesAll requires every pattern, including duplicates and mixed strategies.
    /// </summary>
    [TestMethod]
    public void MatchesAllChecksEveryGlob()
    {
        Assert.IsTrue(GlobSet.Create([]).MatchesAll("anything"u8));
        string[][] groups =
        [
            ["src/App.cs", "src/Other.cs"],
            ["*.cs", "*.txt"],
            ["src/*", "*.cs", "src/App.cs"],
            ["src/App.cs", "src/App.cs"],
            ["**/*.cs", "src/**", "**/App.?s"],
            ["abc", "def"],
            ["abc"],
            ["**/abc", "**/a.c"],
            ["**/a.c"],
            ["**/*.rs", "**/*.c"],
            ["**/*.rs", "*.rs"],
            ["*.rs", "**/m*.rs", "a/*.rs"],
            ["a*", "ab*", "ab/c*"],
            ["*.rs", "*s", "*/main.rs"],
        ];
        foreach (string[] patterns in groups)
        {
            Glob[] globs = patterns.Select(static pattern => Glob.Parse(Encoding.UTF8.GetBytes(pattern))).ToArray();
            var set = GlobSet.Create(globs);
            foreach (byte[] path in CandidatePaths.Select(candidate => Encoding.UTF8.GetBytes(candidate)))
            {
                bool expected = globs.All(glob => glob.IsMatch(path));
                Assert.AreEqual(expected, set.MatchesAll(path));
                Assert.AreEqual(expected, set.MatchesAll(GlobCandidate.FromBytes(path)));
            }
        }

        var insensitive = GlobSet.Create([
            new GlobBuilder("c*"u8.ToArray()).WithAsciiCaseInsensitive(true).Build(),
            Glob.Parse("*{rs,c}"u8.ToArray()),
        ]);
        Assert.IsTrue(insensitive.MatchesAll("c/main.rs"u8));
        Assert.IsTrue(insensitive.MatchesAll("C/main.c"u8));
        Assert.IsFalse(insensitive.MatchesAll("Ca"u8));
        Assert.IsFalse(insensitive.MatchesAll("foo.c"u8));
    }

    /// <summary>
    /// Verifies owned basename extraction preserves bytes and upstream empty-component behavior.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <param name="basename">The expected basename.</param>
    [TestMethod]
    [DataRow("foo/bar", "bar")]
    [DataRow("foo", "foo")]
    [DataRow("foo/..", "")]
    [DataRow("", "")]
    public void OwnedBasenameMatchesUpstream(string path, string basename)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(path);
        var candidate = new GlobCandidate(bytes);
        Array.Fill(bytes, (byte)'X');
        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes(basename), candidate.BaseName.ToArray());
        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes(path), candidate.Path.ToArray());
    }

    /// <summary>
    /// Verifies new definitions select real files through the walker and support negation and clearing.
    /// </summary>
    /// <param name="type">The type name or alias.</param>
    /// <param name="file">A matching file name.</param>
    [TestMethod]
    [DataRow("hurl", "request.hurl")]
    [DataRow("mojo", "main.mojo")]
    [DataRow("pkgbuild", "PKGBUILD")]
    [DataRow("proto", "message.proto")]
    [DataRow("protobuf", "message.proto")]
    [DataRow("rocq", "proof.v")]
    public void NewFileTypesParticipateInNormalSelections(string type, string file)
    {
        using var fixture = new DirectoryFixture();
        string expected = fixture.Write(file);
        fixture.Write("unrelated.txt");
        FileTypeMatcherBuilder builder = new FileTypeMatcherBuilder().AddDefaults();
        Assert.Contains(definition => definition.Name == type, builder.Build().Definitions);
        FileTypeMatcher selected = builder.Select(type).Build();
        Assert.AreEqual(expected, Assert.ContainsSingle(static entry => entry.IsFile, new WalkBuilder(fixture.Root).GitGlobal(false).FileTypes(selected).Build()).FullPath);
        FileTypeMatcher negated = new FileTypeMatcherBuilder().AddDefaults().Negate(type).Build();
        Assert.DoesNotContain(entry => entry.FullPath == expected, new WalkBuilder(fixture.Root).GitGlobal(false).FileTypes(negated).Build());
        FileTypeMatcher cleared = new FileTypeMatcherBuilder().AddDefaults().Clear(type).Add(type, "*.txt").Select(type).Build();
        Assert.EndsWith("unrelated.txt", Assert.ContainsSingle(static entry => entry.IsFile, new WalkBuilder(fixture.Root).GitGlobal(false).FileTypes(cleared).Build()).FullPath, StringComparison.Ordinal);
    }
}
