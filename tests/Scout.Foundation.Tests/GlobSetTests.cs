

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
    /// Verifies last-match lookup preserves insertion-order precedence and eligibility filters.
    /// </summary>
    [Fact]
    public void LastMatchingIndexPreservesOrderAndEligibility()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("src/*.rs"u8.ToArray(), GlobOptions.UnixLiteralSeparator),
                Glob.Parse("*.rs"u8.ToArray()),
                Glob.Parse("src/**"u8.ToArray(), GlobOptions.UnixLiteralSeparator),
            ]);
        var candidate = GlobCandidate.FromBytes("src/main.rs"u8);

        Assert.Equal(2, set.LastMatchingIndex(candidate, []));
        Assert.Equal(1, set.LastMatchingIndex(candidate, [true, true, false]));
        Assert.Equal(-1, set.LastMatchingIndex(candidate, [false, false, false]));
        Assert.Throws<ArgumentException>(() => set.LastMatchingIndex(candidate, [true]));
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
    /// Verifies broad recursive globs remain eligible for final glob verification.
    /// </summary>
    [Fact]
    public void BroadRecursiveFallbackCandidatesAreVerifiedByGlobMatcher()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("**/**/*"u8.ToArray(), GlobOptions.UnixLiteralSeparator),
                Glob.Parse("**/dir_root_32/*"u8.ToArray(), GlobOptions.UnixLiteralSeparator),
            ]);

        Assert.Equal([0], set.MatchingIndexes("a/foo.rs"u8));
        Assert.Equal([0, 1], set.MatchingIndexes("dir_root_32/file"u8));
        Assert.Equal([0], set.MatchingIndexes("dir_root_32/child/file"u8));
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

    /// <summary>
    /// Verifies large extension-suffix sets preserve indexes and per-glob case semantics.
    /// </summary>
    [Fact]
    public void ExtensionSuffixAutomatonPreservesIndexesAndCaseModes()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("*.CS"u8.ToArray()),
                Glob.Parse("*.cs"u8.ToArray(), new GlobOptions(asciiCaseInsensitive: true)),
                Glob.Parse("*.rs"u8.ToArray()),
                Glob.Parse("*.fs"u8.ToArray()),
                Glob.Parse("*.vb"u8.ToArray()),
                Glob.Parse("*.cpp"u8.ToArray()),
                Glob.Parse("*.h"u8.ToArray()),
                Glob.Parse("*.java"u8.ToArray()),
            ]);

        Assert.Equal([1], set.MatchingIndexes("src/App.cs"u8));
        Assert.Equal([0, 1], set.MatchingIndexes("src/App.CS"u8));
        Assert.Equal([2], set.MatchingIndexes("src/lib.rs"u8));
        Assert.Empty(set.MatchingIndexes("src/readme.md"u8));
    }

    /// <summary>
    /// Verifies required-literal extraction finds the mandatory runs in Linux's general root ignore patterns.
    /// </summary>
    [Fact]
    public void RequiredLiteralExtractionFindsLinuxRootPatternRuns()
    {
        var dotFile = Glob.Parse(".*"u8.ToArray());
        var asn1 = Glob.Parse("*.asn1.[ch]"u8.ToArray());
        var numberedC = Glob.Parse("*.c.[012]*.*"u8.ToArray());
        var objectVariant = Glob.Parse("*.o.*"u8.ToArray());
        var generatedTable = Glob.Parse("*.tab.[ch]"u8.ToArray());

        Assert.True(dotFile.TryGetRequiredLiteral(out byte[] dotFileLiteral));
        Assert.True(asn1.TryGetRequiredLiteral(out byte[] asn1Literal));
        Assert.True(numberedC.TryGetRequiredLiteral(out byte[] numberedCLiteral));
        Assert.True(objectVariant.TryGetRequiredLiteral(out byte[] objectVariantLiteral));
        Assert.True(generatedTable.TryGetRequiredLiteral(out byte[] generatedTableLiteral));
        Assert.Equal("."u8.ToArray(), dotFileLiteral);
        Assert.Equal(".asn1."u8.ToArray(), asn1Literal);
        Assert.Equal(".c."u8.ToArray(), numberedCLiteral);
        Assert.Equal(".o."u8.ToArray(), objectVariantLiteral);
        Assert.Equal(".tab."u8.ToArray(), generatedTableLiteral);
    }

    /// <summary>
    /// Verifies required-literal extraction skips brace alternatives and classes while retaining escaped and unclosed literals.
    /// </summary>
    [Fact]
    public void RequiredLiteralExtractionIsConservativeAroundGlobSyntax()
    {
        var alternatives = Glob.Parse("*left{alternative,longer}right*.[ch]"u8.ToArray());
        var classes = Glob.Parse("*prefix[abc]suffix*.[ch]"u8.ToArray());
        var escapes = Glob.Parse("*\\[identifier*.[ch]"u8.ToArray());
        var unclosedClass = Glob.Parse(
            "*token[rest"u8.ToArray(),
            new GlobOptions(allowUnclosedClass: true));
        var recursivePrefix = Glob.Parse(
            "**/dir_root_32/*"u8.ToArray(),
            GlobOptions.UnixLiteralSeparator);

        Assert.True(alternatives.TryGetRequiredLiteral(out byte[] alternativeLiteral));
        Assert.True(classes.TryGetRequiredLiteral(out byte[] classLiteral));
        Assert.True(escapes.TryGetRequiredLiteral(out byte[] escapeLiteral));
        Assert.True(unclosedClass.TryGetRequiredLiteral(out byte[] unclosedClassLiteral));
        Assert.True(recursivePrefix.TryGetRequiredLiteral(out byte[] recursivePrefixLiteral));
        Assert.Equal("right"u8.ToArray(), alternativeLiteral);
        Assert.Equal("prefix"u8.ToArray(), classLiteral);
        Assert.Equal("[identifier"u8.ToArray(), escapeLiteral);
        Assert.Equal("token[rest"u8.ToArray(), unclosedClassLiteral);
        Assert.Equal("dir_root_32/"u8.ToArray(), recursivePrefixLiteral);
    }

    /// <summary>
    /// Verifies required-literal candidates remain subject to exact glob verification.
    /// </summary>
    [Fact]
    public void RequiredLiteralCandidatesAreVerifiedByGlobMatcher()
    {
        var basenameSet = GlobSet.Create(
            [
                Glob.Parse(
                    "*.o.*"u8.ToArray(),
                    new GlobOptions(literalSeparator: true, matchBaseName: true)),
            ]);
        var shapeSet = GlobSet.Create(
            [Glob.Parse("*left?required*.[ch]"u8.ToArray())]);

        Assert.Empty(basenameSet.MatchingIndexes("dir.o.value/file.c"u8));
        Assert.Empty(shapeSet.MatchingIndexes("leftrequired.c"u8));
        Assert.Equal([0], shapeSet.MatchingIndexes("left-required.c"u8));
    }

    /// <summary>
    /// Verifies merged suffix and required-literal patterns retain their distinct indexes and end requirements.
    /// </summary>
    [Fact]
    public void SuffixAndRequiredLiteralCandidatesShareMatcherWithoutLosingSemantics()
    {
        var set = GlobSet.Create(
            [
                Glob.Parse("*tail"u8.ToArray()),
                Glob.Parse("*tail*.[ch]"u8.ToArray()),
            ]);

        Assert.Equal([1], set.MatchingIndexes("tail-value.c"u8));
        Assert.Equal([0], set.MatchingIndexes("value.c-tail"u8));
        Assert.Empty(set.MatchingIndexes("value.c-tail-extra"u8));
    }

    /// <summary>
    /// Verifies required-literal candidates preserve alternatives, classes, escapes, and mixed ASCII case modes.
    /// </summary>
    [Fact]
    public void RequiredLiteralCandidatesPreserveGeneralGlobSemantics()
    {
        var alternatives = GlobSet.Create(
            [Glob.Parse("*left{alpha,beta}right*.[ch]"u8.ToArray())]);
        var classes = GlobSet.Create(
            [Glob.Parse("*prefix[ab]suffix*.[ch]"u8.ToArray())]);
        var escapes = GlobSet.Create(
            [Glob.Parse("*\\[identifier*.[ch]"u8.ToArray())]);
        var mixedCase = GlobSet.Create(
            [
                Glob.Parse("*Required*.[ch]"u8.ToArray()),
                Glob.Parse(
                    "*Required*.[ch]"u8.ToArray(),
                    new GlobOptions(asciiCaseInsensitive: true)),
            ]);

        Assert.Equal([0], alternatives.MatchingIndexes("xxleftbetarightyy.h"u8));
        Assert.Empty(alternatives.MatchingIndexes("xxleftgammarightyy.h"u8));
        Assert.Equal([0], classes.MatchingIndexes("xxprefixbsuffixyy.c"u8));
        Assert.Empty(classes.MatchingIndexes("xxprefixcsuffixyy.c"u8));
        Assert.Equal([0], escapes.MatchingIndexes("xx[identifier-tail.h"u8));
        Assert.Empty(escapes.MatchingIndexes("xxidentifier-tail.h"u8));
        Assert.Equal([1], mixedCase.MatchingIndexes("xxrequiredyy.h"u8));
        Assert.Equal([0, 1], mixedCase.MatchingIndexes("xxRequiredyy.h"u8));
    }
}
