using System.Text;
using Scout.IO.Globbing;
using Scout.IO.Ignore;

namespace Scout;

/// <summary>
/// Verifies release glob and file-type behavior through exported APIs.
/// </summary>
public sealed class UpstreamGlobRegressionTests
{
    /// <summary>
    /// Verifies MatchesAll requires every pattern, including duplicates and mixed strategies.
    /// </summary>
    [Fact]
    public void MatchesAllChecksEveryGlob()
    {
        Assert.True(GlobSet.Create([]).MatchesAll("anything"u8));
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
            foreach (string candidate in new[]
            {
                "src/App.cs", "src/Other.cs", "abc", "def", "foo/abc", "foo/a.c", "foo/a.rs",
                "a/main.rs", "main.rs", "a/lib.rs", "ab/c/def", "ab/cd", "ab/x", "a", "a/b/c/main.rs", "foo.rs", "as", "", "..",
            })
            {
                byte[] path = Encoding.UTF8.GetBytes(candidate);
                bool expected = globs.All(glob => glob.IsMatch(path));
                Assert.Equal(expected, set.MatchesAll(path));
                Assert.Equal(expected, set.MatchesAll(GlobCandidate.FromBytes(path)));
            }
        }

        var insensitive = GlobSet.Create([
            new GlobBuilder("c*"u8.ToArray()).WithAsciiCaseInsensitive(true).Build(),
            Glob.Parse("*{rs,c}"u8.ToArray()),
        ]);
        Assert.True(insensitive.MatchesAll("c/main.rs"u8));
        Assert.True(insensitive.MatchesAll("C/main.c"u8));
        Assert.False(insensitive.MatchesAll("Ca"u8));
        Assert.False(insensitive.MatchesAll("foo.c"u8));
    }

    /// <summary>
    /// Verifies owned basename extraction preserves bytes and upstream empty-component behavior.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <param name="basename">The expected basename.</param>
    [Theory]
    [InlineData("foo/bar", "bar")]
    [InlineData("foo", "foo")]
    [InlineData("foo/..", "")]
    [InlineData("", "")]
    public void OwnedBasenameMatchesUpstream(string path, string basename)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(path);
        var candidate = new GlobCandidate(bytes);
        Array.Fill(bytes, (byte)'X');
        Assert.Equal(Encoding.UTF8.GetBytes(basename), candidate.BaseName.ToArray());
        Assert.Equal(Encoding.UTF8.GetBytes(path), candidate.Path.ToArray());
    }

    /// <summary>
    /// Verifies new definitions select real files through the walker and support negation and clearing.
    /// </summary>
    /// <param name="type">The type name or alias.</param>
    /// <param name="file">A matching file name.</param>
    [Theory]
    [InlineData("hurl", "request.hurl")]
    [InlineData("mojo", "main.mojo")]
    [InlineData("pkgbuild", "PKGBUILD")]
    [InlineData("proto", "message.proto")]
    [InlineData("protobuf", "message.proto")]
    [InlineData("rocq", "proof.v")]
    public void NewFileTypesParticipateInNormalSelections(string type, string file)
    {
        using var fixture = new DirectoryFixture();
        string expected = fixture.Write(file);
        fixture.Write("unrelated.txt");
        FileTypeMatcherBuilder builder = new FileTypeMatcherBuilder().AddDefaults();
        Assert.Contains(builder.Build().Definitions, definition => definition.Name == type);
        FileTypeMatcher selected = builder.Select(type).Build();
        Assert.Equal(expected, Assert.Single(new WalkBuilder(fixture.Root).GitGlobal(false).FileTypes(selected).Build(), static entry => entry.IsFile).FullPath);
        FileTypeMatcher negated = new FileTypeMatcherBuilder().AddDefaults().Negate(type).Build();
        Assert.DoesNotContain(new WalkBuilder(fixture.Root).GitGlobal(false).FileTypes(negated).Build(), entry => entry.FullPath == expected);
        FileTypeMatcher cleared = new FileTypeMatcherBuilder().AddDefaults().Clear(type).Add(type, "*.txt").Select(type).Build();
        Assert.EndsWith("unrelated.txt", Assert.Single(new WalkBuilder(fixture.Root).GitGlobal(false).FileTypes(cleared).Build(), static entry => entry.IsFile).FullPath, StringComparison.Ordinal);
    }
}
