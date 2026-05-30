
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Verifies ordered glob-set behavior.
/// </summary>
public sealed class GlobSetTests
{
    /// <summary>
    /// Verifies the glob builder applies upstream-style options.
    /// </summary>
    [Fact]
    public void GlobBuilderAppliesOptions()
    {
        Glob literalSeparator = Glob.Builder("*.rs"u8.ToArray())
            .WithLiteralSeparator(true)
            .Build();
        Glob insensitive = Glob.Builder("SRC/*.RS"u8.ToArray())
            .WithAsciiCaseInsensitive(true)
            .Build();
        Glob emptyAlternates = Glob.Builder("foo{,.txt}"u8.ToArray())
            .WithEmptyAlternates(true)
            .Build();

        Assert.True(literalSeparator.IsMatch("foo.rs"u8));
        Assert.False(literalSeparator.IsMatch("foo/bar.rs"u8));
        Assert.True(insensitive.IsMatch("src/main.rs"u8));
        Assert.True(emptyAlternates.IsMatch("foo"u8));
    }

    /// <summary>
    /// Verifies the glob-set builder preserves insertion order and can be reused.
    /// </summary>
    [Fact]
    public void GlobSetBuilderBuildsOrderedReusableSets()
    {
        GlobSetBuilder builder = GlobSet.Builder()
            .Add(Glob.Parse("*.rs"u8.ToArray()))
            .Add(Glob.Parse("src/**"u8.ToArray()));

        GlobSet first = builder.Build();
        builder.Add(Glob.Parse("*.md"u8.ToArray()));
        GlobSet second = builder.Build();

        Assert.Equal(2, first.Count);
        Assert.Equal(3, second.Count);
        Assert.False(first.IsEmpty);
        Assert.Equal([0, 1], first.MatchingIndexes("src/lib.rs"u8));
        Assert.Equal([2], second.MatchingIndexes("README.md"u8));
    }

    /// <summary>
    /// Verifies prepared candidates expose normalized path components.
    /// </summary>
    [Fact]
    public void GlobCandidateExposesPathComponents()
    {
        var candidate = GlobCandidate.FromBytes("src/lib.rs"u8);
        var dotFile = GlobCandidate.FromBytes(".rs"u8);
        var parent = GlobCandidate.FromBytes("src/.."u8);

        Assert.Equal("src/lib.rs"u8.ToArray(), candidate.Path.ToArray());
        Assert.Equal("lib.rs"u8.ToArray(), candidate.BaseName.ToArray());
        Assert.Equal(".rs"u8.ToArray(), candidate.Extension.ToArray());
        Assert.Equal(".rs"u8.ToArray(), dotFile.Extension.ToArray());
        Assert.Empty(parent.BaseName.ToArray());
        Assert.Empty(parent.Extension.ToArray());
    }

    /// <summary>
    /// Verifies glob-set candidate overloads match the byte-path overloads.
    /// </summary>
    [Fact]
    public void GlobSetCandidateOverloadsMatchPreparedPath()
    {
        GlobSet set = GlobSet.Builder()
            .Add(Glob.Parse("src/**"u8.ToArray()))
            .Add(Glob.Parse("*.rs"u8.ToArray()))
            .Add(Glob.Parse("lib*"u8.ToArray(), new GlobOptions(matchBaseName: true)))
            .Build();
        var candidate = GlobCandidate.FromBytes("src/lib.rs"u8);
        var matches = new List<int> { 99 };

        set.MatchingIndexesInto(candidate, matches);

        Assert.True(set.IsMatch(candidate));
        Assert.True(set.MatchesAll(candidate));
        Assert.Equal([0, 1, 2], set.MatchingIndexes(candidate));
        Assert.Equal([0, 1, 2], matches);
    }

    /// <summary>
    /// Verifies glob sets report matching indexes in insertion order.
    /// </summary>
    [Fact]
    public void MatchingIndexesReportsInsertionOrder()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("*.cs"u8.ToArray()),
                Glob.Parse("src/**"u8.ToArray()),
                Glob.Parse("*.md"u8.ToArray()),
            ]);

        Assert.Equal([0, 1], set.MatchingIndexes("src/App.cs"u8));
        Assert.True(set.IsMatch("README.md"u8));
        Assert.False(set.IsMatch("README.txt"u8));
    }

    /// <summary>
    /// Verifies matching into an existing list clears and fills it in insertion order.
    /// </summary>
    [Fact]
    public void MatchingIndexesIntoClearsAndReportsInsertionOrder()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("*.cs"u8.ToArray()),
                Glob.Parse("src/**"u8.ToArray()),
                Glob.Parse("*.md"u8.ToArray()),
            ]);
        var matches = new List<int> { 99 };

        set.MatchingIndexesInto("src/App.cs"u8, matches);

        Assert.Equal([0, 1], matches);
    }

    /// <summary>
    /// Verifies matching into an existing list clears it for empty sets.
    /// </summary>
    [Fact]
    public void MatchingIndexesIntoClearsForEmptySet()
    {
        var set = GlobSet.Create([]);
        var matches = new List<int> { 99 };

        set.MatchingIndexesInto("src/App.cs"u8, matches);

        Assert.Empty(matches);
        Assert.True(set.IsEmpty);
    }

    /// <summary>
    /// Verifies all-match checks follow upstream empty-set and every-pattern semantics.
    /// </summary>
    [Fact]
    public void MatchesAllRequiresEveryGlob()
    {
        Assert.True(GlobSet.Create([]).MatchesAll("anything"u8));

        var set = GlobSet.Create(
            [
                Glob.Parse("src/**"u8.ToArray()),
                Glob.Parse("*.cs"u8.ToArray()),
            ]);

        Assert.True(set.MatchesAll("src/App.cs"u8));
        Assert.False(set.MatchesAll("src/App.txt"u8));
        Assert.False(set.MatchesAll("tests/App.cs"u8));
    }

    /// <summary>
    /// Verifies glob-set strategies preserve matches across literal, basename, extension, prefix, suffix, and fallback candidates.
    /// </summary>
    [Fact]
    public void StrategiesPreserveMatchingIndexes()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("literal"u8.ToArray()),
                Glob.Parse("README.md"u8.ToArray(), new GlobOptions(matchBaseName: true)),
                Glob.Parse("*.cs"u8.ToArray()),
                Glob.Parse("src/**/test?.[ch]"u8.ToArray()),
                Glob.Parse("name*"u8.ToArray(), new GlobOptions(matchBaseName: true)),
                Glob.Parse("*tail"u8.ToArray()),
                Glob.Parse("{foo,bar}"u8.ToArray()),
                Glob.Parse("literal\\*.txt"u8.ToArray()),
            ]);

        Assert.Equal([0], set.MatchingIndexes("literal"u8));
        Assert.Equal([1], set.MatchingIndexes("docs/README.md"u8));
        Assert.Equal([2], set.MatchingIndexes("Program.cs"u8));
        Assert.Equal([3], set.MatchingIndexes("src/unit/test1.c"u8));
        Assert.Equal([4], set.MatchingIndexes("nested/name123"u8));
        Assert.Equal([5], set.MatchingIndexes("prefix-tail"u8));
        Assert.Equal([6], set.MatchingIndexes("foo"u8));
        Assert.Equal([7], set.MatchingIndexes("literal*.txt"u8));
    }

    /// <summary>
    /// Verifies broad regex fallback candidates cannot bypass final glob verification.
    /// </summary>
    [Fact]
    public void FallbackCandidatesAreVerifiedByGlobMatcher()
    {
        var set = GlobSet.Create([Glob.Parse("{foo,bar}"u8.ToArray())]);

        Assert.True(set.IsMatch("foo"u8));
        Assert.False(set.IsMatch("baz"u8));
        Assert.Empty(set.MatchingIndexes("baz"u8));
    }

    /// <summary>
    /// Verifies required-extension candidates still require the full glob to match.
    /// </summary>
    [Fact]
    public void RequiredExtensionCandidatesAreVerifiedByGlobMatcher()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("src/**/test*.rs"u8.ToArray()),
                Glob.Parse("*.tar.gz"u8.ToArray()),
            ]);
        var matchingCandidate = GlobCandidate.FromBytes("src/unit/test_one.rs"u8);
        var extensionOnlyCandidate = GlobCandidate.FromBytes("src/unit/prod.rs"u8);

        Assert.Equal([0], set.MatchingIndexes("src/unit/test_one.rs"u8));
        Assert.Equal([0], set.MatchingIndexes(matchingCandidate));
        Assert.Empty(set.MatchingIndexes("src/unit/prod.rs"u8));
        Assert.Empty(set.MatchingIndexes(extensionOnlyCandidate));
        Assert.Equal([1], set.MatchingIndexes("archive.tar.gz"u8));
        Assert.Empty(set.MatchingIndexes("archive.gz"u8));
    }

    /// <summary>
    /// Verifies recursive-prefix component suffixes match exact paths and component boundaries.
    /// </summary>
    [Fact]
    public void ComponentSuffixCandidatesRespectPathComponents()
    {
        var set = GlobSet.Create([Glob.Parse("**/foo/bar"u8.ToArray())]);

        Assert.Equal([0], set.MatchingIndexes("foo/bar"u8));
        Assert.Equal([0], set.MatchingIndexes("src/foo/bar"u8));
        Assert.Empty(set.MatchingIndexes("src/prefixfoo/bar"u8));
        Assert.Empty(set.MatchingIndexes("src/foo/bar/baz"u8));
    }

    /// <summary>
    /// Verifies mixed case-sensitive and case-insensitive prefix candidates remain parity-preserving.
    /// </summary>
    [Fact]
    public void PrefixCandidatesRespectGlobCaseMode()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("SRC/*.CS"u8.ToArray()),
                Glob.Parse("SRC/*.CS"u8.ToArray(), new GlobOptions(asciiCaseInsensitive: true)),
            ]);

        Assert.Equal([1], set.MatchingIndexes("src/App.cs"u8));
        Assert.Equal([0, 1], set.MatchingIndexes("SRC/App.CS"u8));
    }
}
