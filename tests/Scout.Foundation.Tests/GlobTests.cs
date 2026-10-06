
using System.Text;

namespace Scout;

/// <summary>
/// Verifies byte-oriented glob behavior.
/// </summary>
[TestClass]
public sealed class GlobTests
{
    /// <summary>
    /// Verifies literal matching preserves arbitrary bytes.
    /// </summary>
    [TestMethod]
    public void LiteralMatchesArbitraryBytes()
    {
        var glob = Glob.Parse([0x66, 0xff, 0x6f]);

        Assert.IsTrue(glob.IsMatch([0x66, 0xff, 0x6f]));
        Assert.IsFalse(glob.IsMatch([0x66, 0xef, 0x6f]));
    }

    /// <summary>
    /// Verifies glob metacharacters are escaped with upstream bracket classes.
    /// </summary>
    [TestMethod]
    public void EscapeWrapsGlobMetacharacters()
    {
        Assert.AreSequenceEqual("foo"u8.ToArray(), Glob.Escape("foo"u8));
        Assert.AreSequenceEqual("foo[*]"u8.ToArray(), Glob.Escape("foo*"u8));
        Assert.AreSequenceEqual("[[][]]"u8.ToArray(), Glob.Escape("[]"u8));
        Assert.AreSequenceEqual("[*][?]"u8.ToArray(), Glob.Escape("*?"u8));
        Assert.AreSequenceEqual("src/[*][*]/[*].rs"u8.ToArray(), Glob.Escape("src/**/*.rs"u8));
        Assert.AreSequenceEqual("bar[[]ab[]]baz"u8.ToArray(), Glob.Escape("bar[ab]baz"u8));
        Assert.AreSequenceEqual("bar[[]!![]]!baz"u8.ToArray(), Glob.Escape("bar[!!]!baz"u8));
        Assert.AreSequenceEqual("foo[{]bar[}]"u8.ToArray(), Glob.Escape("foo{bar}"u8));
    }

    /// <summary>
    /// Verifies single-star wildcards cross separators with globset defaults.
    /// </summary>
    [TestMethod]
    public void StarCrossesSeparatorByDefault()
    {
        var glob = Glob.Parse("src/*.cs"u8.ToArray());

        Assert.IsTrue(glob.IsMatch("src/App.cs"u8));
        Assert.IsTrue(glob.IsMatch("src/App/Program.cs"u8));
    }

    /// <summary>
    /// Verifies literal-separator mode prevents wildcards from crossing separators.
    /// </summary>
    [TestMethod]
    public void LiteralSeparatorBlocksWildcardSeparatorMatches()
    {
        var glob = Glob.Parse("src/*.cs"u8.ToArray(), new GlobOptions(literalSeparator: true));

        Assert.IsTrue(glob.IsMatch("src/App.cs"u8));
        Assert.IsFalse(glob.IsMatch("src/App/Program.cs"u8));
    }

    /// <summary>
    /// Verifies double-star wildcards cross separators.
    /// </summary>
    [TestMethod]
    public void DoubleStarCrossesSeparator()
    {
        var glob = Glob.Parse("src/**.cs"u8.ToArray());

        Assert.IsTrue(glob.IsMatch("src/App/Program.cs"u8));
    }

    /// <summary>
    /// Verifies double-star followed by a separator can match zero directory levels.
    /// </summary>
    [TestMethod]
    public void DoubleStarSlashCanMatchZeroDirectories()
    {
        var glob = Glob.Parse("**/foo"u8.ToArray());

        Assert.IsTrue(glob.IsMatch("foo"u8));
        Assert.IsTrue(glob.IsMatch("src/foo"u8));
    }

    /// <summary>
    /// Verifies non-component double stars are ordinary stars.
    /// </summary>
    [TestMethod]
    public void NonComponentDoubleStarsAreOrdinaryStars()
    {
        var middle = Glob.Parse("a**b"u8.ToArray(), new GlobOptions(literalSeparator: true));
        var prefix = Glob.Parse("**a"u8.ToArray(), new GlobOptions(literalSeparator: true));
        var suffix = Glob.Parse("a**"u8.ToArray(), new GlobOptions(literalSeparator: true));
        var recursive = Glob.Parse("a/**"u8.ToArray(), new GlobOptions(literalSeparator: true));

        Assert.IsFalse(middle.IsMatch("a/x/b"u8));
        Assert.IsFalse(prefix.IsMatch("x/a"u8));
        Assert.IsFalse(suffix.IsMatch("a/x"u8));
        Assert.IsTrue(recursive.IsMatch("a/x/b"u8));
    }

    /// <summary>
    /// Verifies question mark matches one non-separator byte.
    /// </summary>
    [TestMethod]
    public void QuestionMatchesOneByte()
    {
        var glob = Glob.Parse("file?.txt"u8.ToArray());

        Assert.IsTrue(glob.IsMatch("file1.txt"u8));
        Assert.IsFalse(glob.IsMatch("file12.txt"u8));
    }

    /// <summary>
    /// Verifies character classes include ranges and negation.
    /// </summary>
    [TestMethod]
    public void CharacterClassSupportsRangesAndNegation()
    {
        Assert.IsTrue(Glob.Parse("file[0-9].txt"u8.ToArray()).IsMatch("file7.txt"u8));
        Assert.IsTrue(Glob.Parse("file[!0-9].txt"u8.ToArray()).IsMatch("filex.txt"u8));
        Assert.IsFalse(Glob.Parse("file[!0-9].txt"u8.ToArray()).IsMatch("file7.txt"u8));
    }

    /// <summary>
    /// Verifies character classes accept upstream's leading bracket and hyphen edge cases.
    /// </summary>
    [TestMethod]
    public void CharacterClassSupportsBracketAndHyphenLiterals()
    {
        Assert.IsTrue(Glob.Parse("[]]"u8.ToArray()).IsMatch("]"u8));
        Assert.IsTrue(Glob.Parse("[!]]"u8.ToArray()).IsMatch("x"u8));
        Assert.IsFalse(Glob.Parse("[!]]"u8.ToArray()).IsMatch("]"u8));
        Assert.IsTrue(Glob.Parse("[a-]"u8.ToArray()).IsMatch("-"u8));
        Assert.IsTrue(Glob.Parse("[-a-z]"u8.ToArray()).IsMatch("-"u8));
        Assert.IsTrue(Glob.Parse("[]-z]"u8.ToArray()).IsMatch("^"u8));
    }

    /// <summary>
    /// Verifies backslashes inside character classes are literal bytes.
    /// </summary>
    [TestMethod]
    public void CharacterClassTreatsBackslashAsLiteral()
    {
        var backslashThenBracket = Glob.Parse("[\\]]"u8.ToArray());
        var backslashOrHyphen = Glob.Parse("[\\-]"u8.ToArray());

        Assert.IsTrue(backslashThenBracket.IsMatch("\\]"u8));
        Assert.IsFalse(backslashThenBracket.IsMatch("]"u8));
        Assert.IsTrue(backslashOrHyphen.IsMatch("\\"u8));
        Assert.IsTrue(backslashOrHyphen.IsMatch("-"u8));
        Assert.IsFalse(backslashOrHyphen.IsMatch("]"u8));
    }

    /// <summary>
    /// Verifies malformed glob syntax reports upstream parse error kinds.
    /// </summary>
    [TestMethod]
    public void ParseReportsMalformedPatterns()
    {
        AssertParseError("["u8.ToArray(), GlobParseErrorKind.UnclosedClass);
        AssertParseError("[]"u8.ToArray(), GlobParseErrorKind.UnclosedClass);
        AssertParseError("[!"u8.ToArray(), GlobParseErrorKind.UnclosedClass);
        AssertParseError("[!]"u8.ToArray(), GlobParseErrorKind.UnclosedClass);
        AssertParseError("[z-a]"u8.ToArray(), GlobParseErrorKind.InvalidRange, (byte)'z', (byte)'a');
        AssertParseError("[z--]"u8.ToArray(), GlobParseErrorKind.InvalidRange, (byte)'z', (byte)'-');
        AssertParseError("{a,b"u8.ToArray(), GlobParseErrorKind.UnclosedAlternates);
        AssertParseError("{a,{b,c}"u8.ToArray(), GlobParseErrorKind.UnclosedAlternates);
        AssertParseError("a,b}"u8.ToArray(), GlobParseErrorKind.UnopenedAlternates);
        AssertParseError("{a,b}}"u8.ToArray(), GlobParseErrorKind.UnopenedAlternates);
        AssertParseError("abc\\"u8.ToArray(), GlobParseErrorKind.DanglingEscape);
    }

    /// <summary>
    /// Verifies unclosed character classes can opt into literal treatment.
    /// </summary>
    [TestMethod]
    public void AllowUnclosedClassTreatsClassAsLiteral()
    {
        var options = new GlobOptions(allowUnclosedClass: true);

        Assert.IsTrue(Glob.Parse("["u8.ToArray(), options).IsMatch("["u8));
        Assert.IsTrue(Glob.Parse("[abc"u8.ToArray(), options).IsMatch("[abc"u8));
        Assert.IsTrue(Glob.Parse("[]"u8.ToArray(), options).IsMatch("[]"u8));
        Assert.IsTrue(Glob.Parse("[!]"u8.ToArray(), options).IsMatch("[!]"u8));
    }

    /// <summary>
    /// Verifies brace alternatives match any listed alternative.
    /// </summary>
    [TestMethod]
    public void BraceAlternativesMatchListedPatterns()
    {
        var glob = Glob.Parse("*.{cs,fs}"u8.ToArray());

        Assert.IsTrue(glob.IsMatch("Program.cs"u8));
        Assert.IsTrue(glob.IsMatch("Program.fs"u8));
        Assert.IsFalse(glob.IsMatch("Program.vb"u8));
    }

    /// <summary>
    /// Verifies empty brace alternatives match upstream defaults and opt-in behavior.
    /// </summary>
    [TestMethod]
    public void EmptyBraceAlternativesRequireOptInWhenMixedWithNonEmptyAlternatives()
    {
        Assert.IsTrue(Glob.Parse("{}"u8.ToArray()).IsMatch(ReadOnlySpan<byte>.Empty));
        Assert.IsTrue(Glob.Parse("{,}"u8.ToArray()).IsMatch(ReadOnlySpan<byte>.Empty));
        Assert.IsFalse(Glob.Parse("foo{,.txt}"u8.ToArray()).IsMatch("foo"u8));
        Assert.IsTrue(Glob.Parse("foo{,.txt}"u8.ToArray()).IsMatch("foo.txt"u8));

        var emptyAlternates = Glob.Parse("foo{,.txt}"u8.ToArray(), new GlobOptions(emptyAlternates: true));

        Assert.IsTrue(emptyAlternates.IsMatch("foo"u8));
        Assert.IsTrue(emptyAlternates.IsMatch("foo.txt"u8));
    }

    /// <summary>
    /// Verifies backslash escapes glob metacharacters.
    /// </summary>
    [TestMethod]
    public void BackslashEscapesMetacharacters()
    {
        var glob = Glob.Parse("literal\\*.txt"u8.ToArray());

        Assert.IsTrue(glob.IsMatch("literal*.txt"u8));
        Assert.IsFalse(glob.IsMatch("literal-test.txt"u8));
    }

    /// <summary>
    /// Verifies ASCII case-insensitive glob options fold only ASCII case.
    /// </summary>
    [TestMethod]
    public void AsciiCaseInsensitiveMatchesAsciiCase()
    {
        var glob = Glob.Parse("SRC/*.CS"u8.ToArray(), new GlobOptions(asciiCaseInsensitive: true));

        Assert.IsTrue(glob.IsMatch("src/app.cs"u8));
        Assert.IsFalse(Glob.Parse([0xc0], new GlobOptions(asciiCaseInsensitive: true)).IsMatch([0xe0]));
    }

    /// <summary>
    /// Verifies Windows separators can include slash and backslash.
    /// </summary>
    [TestMethod]
    public void WindowsOptionsTreatBackslashAsSeparator()
    {
        var glob = Glob.Parse("src\\*.cs"u8.ToArray(), GlobOptions.WindowsLiteralSeparator);

        Assert.IsTrue(glob.IsMatch("src\\App.cs"u8));
        Assert.IsFalse(glob.IsMatch("src\\App\\Program.cs"u8));
    }

    /// <summary>
    /// Verifies representative upstream globset match cases.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="path">The candidate path.</param>
    /// <param name="option">The upstream builder option set.</param>
    [TestMethod]
    [DataRow("a*b*c", "a___b___c", "default")]
    [DataRow("abc*abc*abc", "abcabcabcabcabcabcabc", "default")]
    [DataRow("some/**/needle.txt", "some/needle.txt", "default")]
    [DataRow("some/**/needle.txt", "some/one/two/needle.txt", "default")]
    [DataRow("some/**/**/needle.txt", "some/other/needle.txt", "default")]
    [DataRow("**", ".asdf", "default")]
    [DataRow("**", "/x/.asdf", "default")]
    [DataRow("**/test", "test", "default")]
    [DataRow("/**/test", "/test", "default")]
    [DataRow("**/.*", "abc/.abc", "default")]
    [DataRow(".*/**", ".abc/abc", "default")]
    [DataRow("test/**", "test/", "default")]
    [DataRow("test/**", "test/one/two", "default")]
    [DataRow("some/*/needle.txt", "some/one/needle.txt", "default")]
    [DataRow("*some/path/to/hello.txt", "a/bigger/some/path/to/hello.txt", "default")]
    [DataRow("_[[]_[]]_[?]_[*]_!_", "_[_]_?_*_!_", "default")]
    [DataRow("{**/src/**,foo}", "abc/src/bar", "default")]
    [DataRow("{[}],foo}", "}", "default")]
    [DataRow("{a,b{c,d}}", "bd", "default")]
    [DataRow("foo{,.txt}", "foo", "empty-alternates")]
    [DataRow("aBcDeFg", "ABCDEFG", "case-insensitive")]
    [DataRow("abc/def", "abc/def", "literal-separator")]
    [DataRow("abc[/]def", "abc/def", "literal-separator")]
    [DataRow("\\[", "[", "backslash-escapes")]
    [DataRow("\\?", "?", "backslash-escapes")]
    [DataRow("\\*", "*", "backslash-escapes")]
    [DataRow("\\[a-z]", "\\a", "no-backslash-escapes")]
    [DataRow("\\?", "\\a", "no-backslash-escapes")]
    [DataRow("\\*", "\\\\", "no-backslash-escapes")]
    public void UpstreamMatchMatrixMatches(string pattern, string path, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));
        var set = GlobSet.Create([glob]);

        Assert.IsTrue(glob.IsMatch(Bytes(path)));
        Assert.IsTrue(set.IsMatch(Bytes(path)));
    }

    /// <summary>
    /// Verifies representative upstream globset non-match cases.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="path">The candidate path.</param>
    /// <param name="option">The upstream builder option set.</param>
    [TestMethod]
    [DataRow("a*b*c", "abcd", "default")]
    [DataRow("abc*abc*abc", "abcabcabcabcabcabcabca", "default")]
    [DataRow("some/**/needle.txt", "some/other/notthis.txt", "default")]
    [DataRow("/**/test", "test", "default")]
    [DataRow("/**/test", "/one/notthis", "default")]
    [DataRow("**/.*", "ab.c", "default")]
    [DataRow("**/.*", "abc/ab.c", "default")]
    [DataRow(".*/**", ".abc", "default")]
    [DataRow("foo/**", "foo", "default")]
    [DataRow("*hello.txt", "hello.txt-and-then-some", "default")]
    [DataRow("*some/path/to/hello.txt", "some/other/path/to/hello.txt", "default")]
    [DataRow("a", "foo/a", "default")]
    [DataRow("./foo", "foo", "default")]
    [DataRow("**/foo", "foofoo", "default")]
    [DataRow("**/foo/bar", "foofoo/bar", "default")]
    [DataRow("/*.c", "mozilla-sha1/sha1.c", "default")]
    [DataRow("*.c", "mozilla-sha1/sha1.c", "literal-separator")]
    [DataRow("**/m4/ltoptions.m4", "csharp/src/packages/repositories.config", "literal-separator")]
    [DataRow("some/*/needle.txt", "some/one/two/needle.txt", "literal-separator")]
    [DataRow("abc?def", "abc/def", "literal-separator")]
    [DataRow("abc*def", "abc/def", "literal-separator")]
    [DataRow("foo{,.txt}", "foo", "default")]
    public void UpstreamNonMatchMatrixDoesNotMatch(string pattern, string path, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));
        var set = GlobSet.Create([glob]);

        Assert.IsFalse(glob.IsMatch(Bytes(path)));
        Assert.IsFalse(set.IsMatch(Bytes(path)));
    }

    /// <summary>
    /// Verifies literal candidate extraction follows upstream globset strategy cases.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="expected">The expected extracted literal, or <see langword="null" />.</param>
    /// <param name="option">The upstream builder option set.</param>
    [TestMethod]
    [DataRow("foo", "foo", "default")]
    [DataRow("/foo", "/foo", "default")]
    [DataRow("/foo/", "/foo/", "default")]
    [DataRow("/foo/bar", "/foo/bar", "default")]
    [DataRow("*.foo", null, "default")]
    [DataRow("foo/bar", "foo/bar", "default")]
    [DataRow("**/foo/bar", null, "default")]
    [DataRow("foo", null, "case-insensitive")]
    public void LiteralStrategyMatchesUpstreamExtraction(string pattern, string? expected, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));

        AssertStrategyResult(glob.TryGetLiteral(out byte[] actual), actual, expected);
    }

    /// <summary>
    /// Verifies extension-only candidate extraction follows upstream globset strategy cases.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="expected">The expected extracted extension, or <see langword="null" />.</param>
    /// <param name="option">The upstream builder option set.</param>
    [TestMethod]
    [DataRow("**/*.rs", ".rs", "default")]
    [DataRow("**/*.rs.bak", null, "default")]
    [DataRow("*.rs", ".rs", "default")]
    [DataRow("a*.rs", null, "default")]
    [DataRow("/*.c", null, "default")]
    [DataRow("*.c", null, "literal-separator")]
    [DataRow("*.c", ".c", "default")]
    public void ExtensionOnlyStrategyMatchesUpstreamExtraction(string pattern, string? expected, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));

        AssertStrategyResult(glob.TryGetExtensionOnly(out byte[] actual), actual, expected);
    }

    /// <summary>
    /// Verifies required-extension candidate extraction follows upstream globset strategy cases.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="expected">The expected extracted extension, or <see langword="null" />.</param>
    /// <param name="option">The upstream builder option set.</param>
    [TestMethod]
    [DataRow("*.rs", ".rs", "default")]
    [DataRow("/foo/bar/*.rs", ".rs", "default")]
    [DataRow("/foo/bar/.rs", ".rs", "default")]
    [DataRow(".rs", ".rs", "default")]
    [DataRow("./rs", null, "default")]
    [DataRow("foo", null, "default")]
    [DataRow(".foo/", null, "default")]
    [DataRow("foo/", null, "default")]
    public void RequiredExtensionStrategyMatchesUpstreamExtraction(string pattern, string? expected, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));

        AssertStrategyResult(glob.TryGetRequiredExtension(out byte[] actual), actual, expected);
    }

    /// <summary>
    /// Verifies fixed-prefix candidate extraction preserves necessary path filters.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="expected">The expected extracted prefix, or <see langword="null" />.</param>
    /// <param name="option">The upstream builder option set.</param>
    [TestMethod]
    [DataRow("/foo", "/foo", "default")]
    [DataRow("/foo/*", "/foo/", "default")]
    [DataRow("**/foo", null, "default")]
    [DataRow("foo/**", "foo/", "default")]
    [DataRow("foo/*", "foo/", "literal-separator")]
    [DataRow("a*.rs", "a", "default")]
    public void FixedPrefixStrategyExtractsNecessaryCandidatePrefix(string pattern, string? expected, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));

        AssertStrategyResult(glob.TryGetFixedPrefix(out byte[] actual), actual, expected);
    }

    /// <summary>
    /// Verifies fixed-suffix candidate extraction preserves necessary path filters.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="expected">The expected extracted suffix, or <see langword="null" />.</param>
    /// <param name="option">The upstream builder option set.</param>
    [TestMethod]
    [DataRow("**/foo/bar", "/foo/bar", "default")]
    [DataRow("*/foo/bar", "/foo/bar", "default")]
    [DataRow("*/foo/bar", "/foo/bar", "literal-separator")]
    [DataRow("foo/bar", null, "default")]
    [DataRow("*.foo", ".foo", "default")]
    [DataRow("*.foo", ".foo", "literal-separator")]
    [DataRow("**/*_test", "_test", "default")]
    [DataRow("a*.rs", ".rs", "default")]
    public void FixedSuffixStrategyExtractsNecessaryCandidateSuffix(string pattern, string? expected, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));

        AssertStrategyResult(glob.TryGetFixedSuffix(out byte[] actual), actual, expected);
    }

    /// <summary>
    /// Verifies recursive component suffix extraction preserves the exact whole-path candidate.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="expectedSuffix">The expected path-suffix candidate, or <see langword="null" />.</param>
    /// <param name="expectedExactLiteral">The expected exact whole-path candidate, or <see langword="null" />.</param>
    [TestMethod]
    [DataRow("**/foo/bar", "/foo/bar", "foo/bar")]
    [DataRow("**/foo", "/foo", "foo")]
    [DataRow("foo", null, null)]
    [DataRow("*/foo", null, null)]
    public void ComponentSuffixStrategyMatchesRecursiveExtraction(string pattern, string? expectedSuffix, string? expectedExactLiteral)
    {
        var glob = Glob.Parse(Bytes(pattern));
        bool actualResult = glob.TryGetComponentSuffix(out byte[] actualSuffix, out byte[] actualExactLiteral);
        bool expectedResult = expectedSuffix is not null;

        Assert.AreEqual(expectedResult, actualResult);
        AssertStrategyResult(actualResult, actualSuffix, expectedSuffix);
        AssertStrategyResult(actualResult, actualExactLiteral, expectedExactLiteral);
    }

    private static void AssertParseError(
        byte[] pattern,
        GlobParseErrorKind expectedKind,
        byte? expectedRangeStart = null,
        byte? expectedRangeEnd = null)
    {
        GlobParseException exception = Assert.ThrowsExactly<GlobParseException>(() => Glob.Parse(pattern));

        Assert.AreEqual(expectedKind, exception.ErrorKind);
        Assert.AreSequenceEqual(pattern, exception.GlobPattern.ToArray());
        Assert.AreEqual(expectedRangeStart, exception.RangeStart);
        Assert.AreEqual(expectedRangeEnd, exception.RangeEnd);
    }

    private static void AssertStrategyResult(bool actualResult, byte[] actual, string? expected)
    {
        Assert.AreEqual(expected is not null, actualResult);
        if (expected is null)
        {
            Assert.IsEmpty(actual);
            return;
        }

        Assert.AreSequenceEqual(Bytes(expected), actual);
    }

    private static GlobOptions GetOptions(string option)
    {
        return option switch
        {
            "default" => GlobOptions.Unix,
            "literal-separator" => GlobOptions.UnixLiteralSeparator,
            "case-insensitive" => new GlobOptions(asciiCaseInsensitive: true),
            "backslash-escapes" => new GlobOptions(backslashEscapes: true),
            "no-backslash-escapes" => new GlobOptions(backslashEscapes: false),
            "empty-alternates" => new GlobOptions(backslashEscapes: true, emptyAlternates: true),
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, "Unknown glob option."),
        };
    }

    private static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }
}
