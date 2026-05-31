
using System;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies byte-oriented glob behavior.
/// </summary>
public sealed class GlobTests
{
    /// <summary>
    /// Verifies literal matching preserves arbitrary bytes.
    /// </summary>
    [Fact]
    public void LiteralMatchesArbitraryBytes()
    {
        var glob = Glob.Parse([0x66, 0xff, 0x6f]);

        Assert.True(glob.IsMatch([0x66, 0xff, 0x6f]));
        Assert.False(glob.IsMatch([0x66, 0xef, 0x6f]));
    }

    /// <summary>
    /// Verifies glob metacharacters are escaped with upstream bracket classes.
    /// </summary>
    [Fact]
    public void EscapeWrapsGlobMetacharacters()
    {
        Assert.Equal("foo"u8.ToArray(), Glob.Escape("foo"u8));
        Assert.Equal("foo[*]"u8.ToArray(), Glob.Escape("foo*"u8));
        Assert.Equal("[[][]]"u8.ToArray(), Glob.Escape("[]"u8));
        Assert.Equal("[*][?]"u8.ToArray(), Glob.Escape("*?"u8));
        Assert.Equal("src/[*][*]/[*].rs"u8.ToArray(), Glob.Escape("src/**/*.rs"u8));
        Assert.Equal("bar[[]ab[]]baz"u8.ToArray(), Glob.Escape("bar[ab]baz"u8));
        Assert.Equal("bar[[]!![]]!baz"u8.ToArray(), Glob.Escape("bar[!!]!baz"u8));
        Assert.Equal("foo[{]bar[}]"u8.ToArray(), Glob.Escape("foo{bar}"u8));
    }

    /// <summary>
    /// Verifies single-star wildcards cross separators with globset defaults.
    /// </summary>
    [Fact]
    public void StarCrossesSeparatorByDefault()
    {
        var glob = Glob.Parse("src/*.cs"u8.ToArray());

        Assert.True(glob.IsMatch("src/App.cs"u8));
        Assert.True(glob.IsMatch("src/App/Program.cs"u8));
    }

    /// <summary>
    /// Verifies literal-separator mode prevents wildcards from crossing separators.
    /// </summary>
    [Fact]
    public void LiteralSeparatorBlocksWildcardSeparatorMatches()
    {
        var glob = Glob.Parse("src/*.cs"u8.ToArray(), new GlobOptions(literalSeparator: true));

        Assert.True(glob.IsMatch("src/App.cs"u8));
        Assert.False(glob.IsMatch("src/App/Program.cs"u8));
    }

    /// <summary>
    /// Verifies double-star wildcards cross separators.
    /// </summary>
    [Fact]
    public void DoubleStarCrossesSeparator()
    {
        var glob = Glob.Parse("src/**.cs"u8.ToArray());

        Assert.True(glob.IsMatch("src/App/Program.cs"u8));
    }

    /// <summary>
    /// Verifies double-star followed by a separator can match zero directory levels.
    /// </summary>
    [Fact]
    public void DoubleStarSlashCanMatchZeroDirectories()
    {
        var glob = Glob.Parse("**/foo"u8.ToArray());

        Assert.True(glob.IsMatch("foo"u8));
        Assert.True(glob.IsMatch("src/foo"u8));
    }

    /// <summary>
    /// Verifies non-component double stars are ordinary stars.
    /// </summary>
    [Fact]
    public void NonComponentDoubleStarsAreOrdinaryStars()
    {
        var middle = Glob.Parse("a**b"u8.ToArray(), new GlobOptions(literalSeparator: true));
        var prefix = Glob.Parse("**a"u8.ToArray(), new GlobOptions(literalSeparator: true));
        var suffix = Glob.Parse("a**"u8.ToArray(), new GlobOptions(literalSeparator: true));
        var recursive = Glob.Parse("a/**"u8.ToArray(), new GlobOptions(literalSeparator: true));

        Assert.False(middle.IsMatch("a/x/b"u8));
        Assert.False(prefix.IsMatch("x/a"u8));
        Assert.False(suffix.IsMatch("a/x"u8));
        Assert.True(recursive.IsMatch("a/x/b"u8));
    }

    /// <summary>
    /// Verifies question mark matches one non-separator byte.
    /// </summary>
    [Fact]
    public void QuestionMatchesOneByte()
    {
        var glob = Glob.Parse("file?.txt"u8.ToArray());

        Assert.True(glob.IsMatch("file1.txt"u8));
        Assert.False(glob.IsMatch("file12.txt"u8));
    }

    /// <summary>
    /// Verifies character classes include ranges and negation.
    /// </summary>
    [Fact]
    public void CharacterClassSupportsRangesAndNegation()
    {
        Assert.True(Glob.Parse("file[0-9].txt"u8.ToArray()).IsMatch("file7.txt"u8));
        Assert.True(Glob.Parse("file[!0-9].txt"u8.ToArray()).IsMatch("filex.txt"u8));
        Assert.False(Glob.Parse("file[!0-9].txt"u8.ToArray()).IsMatch("file7.txt"u8));
    }

    /// <summary>
    /// Verifies character classes accept upstream's leading bracket and hyphen edge cases.
    /// </summary>
    [Fact]
    public void CharacterClassSupportsBracketAndHyphenLiterals()
    {
        Assert.True(Glob.Parse("[]]"u8.ToArray()).IsMatch("]"u8));
        Assert.True(Glob.Parse("[!]]"u8.ToArray()).IsMatch("x"u8));
        Assert.False(Glob.Parse("[!]]"u8.ToArray()).IsMatch("]"u8));
        Assert.True(Glob.Parse("[a-]"u8.ToArray()).IsMatch("-"u8));
        Assert.True(Glob.Parse("[-a-z]"u8.ToArray()).IsMatch("-"u8));
        Assert.True(Glob.Parse("[]-z]"u8.ToArray()).IsMatch("^"u8));
    }

    /// <summary>
    /// Verifies backslashes inside character classes are literal bytes.
    /// </summary>
    [Fact]
    public void CharacterClassTreatsBackslashAsLiteral()
    {
        var backslashThenBracket = Glob.Parse("[\\]]"u8.ToArray());
        var backslashOrHyphen = Glob.Parse("[\\-]"u8.ToArray());

        Assert.True(backslashThenBracket.IsMatch("\\]"u8));
        Assert.False(backslashThenBracket.IsMatch("]"u8));
        Assert.True(backslashOrHyphen.IsMatch("\\"u8));
        Assert.True(backslashOrHyphen.IsMatch("-"u8));
        Assert.False(backslashOrHyphen.IsMatch("]"u8));
    }

    /// <summary>
    /// Verifies malformed glob syntax reports upstream parse error kinds.
    /// </summary>
    [Fact]
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
    [Fact]
    public void AllowUnclosedClassTreatsClassAsLiteral()
    {
        var options = new GlobOptions(allowUnclosedClass: true);

        Assert.True(Glob.Parse("["u8.ToArray(), options).IsMatch("["u8));
        Assert.True(Glob.Parse("[abc"u8.ToArray(), options).IsMatch("[abc"u8));
        Assert.True(Glob.Parse("[]"u8.ToArray(), options).IsMatch("[]"u8));
        Assert.True(Glob.Parse("[!]"u8.ToArray(), options).IsMatch("[!]"u8));
    }

    /// <summary>
    /// Verifies brace alternatives match any listed alternative.
    /// </summary>
    [Fact]
    public void BraceAlternativesMatchListedPatterns()
    {
        var glob = Glob.Parse("*.{cs,fs}"u8.ToArray());

        Assert.True(glob.IsMatch("Program.cs"u8));
        Assert.True(glob.IsMatch("Program.fs"u8));
        Assert.False(glob.IsMatch("Program.vb"u8));
    }

    /// <summary>
    /// Verifies empty brace alternatives match upstream defaults and opt-in behavior.
    /// </summary>
    [Fact]
    public void EmptyBraceAlternativesRequireOptInWhenMixedWithNonEmptyAlternatives()
    {
        Assert.True(Glob.Parse("{}"u8.ToArray()).IsMatch(ReadOnlySpan<byte>.Empty));
        Assert.True(Glob.Parse("{,}"u8.ToArray()).IsMatch(ReadOnlySpan<byte>.Empty));
        Assert.False(Glob.Parse("foo{,.txt}"u8.ToArray()).IsMatch("foo"u8));
        Assert.True(Glob.Parse("foo{,.txt}"u8.ToArray()).IsMatch("foo.txt"u8));

        var emptyAlternates = Glob.Parse("foo{,.txt}"u8.ToArray(), new GlobOptions(emptyAlternates: true));

        Assert.True(emptyAlternates.IsMatch("foo"u8));
        Assert.True(emptyAlternates.IsMatch("foo.txt"u8));
    }

    /// <summary>
    /// Verifies backslash escapes glob metacharacters.
    /// </summary>
    [Fact]
    public void BackslashEscapesMetacharacters()
    {
        var glob = Glob.Parse("literal\\*.txt"u8.ToArray());

        Assert.True(glob.IsMatch("literal*.txt"u8));
        Assert.False(glob.IsMatch("literal-test.txt"u8));
    }

    /// <summary>
    /// Verifies ASCII case-insensitive glob options fold only ASCII case.
    /// </summary>
    [Fact]
    public void AsciiCaseInsensitiveMatchesAsciiCase()
    {
        var glob = Glob.Parse("SRC/*.CS"u8.ToArray(), new GlobOptions(asciiCaseInsensitive: true));

        Assert.True(glob.IsMatch("src/app.cs"u8));
        Assert.False(Glob.Parse([0xc0], new GlobOptions(asciiCaseInsensitive: true)).IsMatch([0xe0]));
    }

    /// <summary>
    /// Verifies Windows separators can include slash and backslash.
    /// </summary>
    [Fact]
    public void WindowsOptionsTreatBackslashAsSeparator()
    {
        var glob = Glob.Parse("src\\*.cs"u8.ToArray(), GlobOptions.WindowsLiteralSeparator);

        Assert.True(glob.IsMatch("src\\App.cs"u8));
        Assert.False(glob.IsMatch("src\\App\\Program.cs"u8));
    }

    /// <summary>
    /// Verifies representative upstream globset match cases.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="path">The candidate path.</param>
    /// <param name="option">The upstream builder option set.</param>
    [Theory]
    [InlineData("a*b*c", "a___b___c", "default")]
    [InlineData("abc*abc*abc", "abcabcabcabcabcabcabc", "default")]
    [InlineData("some/**/needle.txt", "some/needle.txt", "default")]
    [InlineData("some/**/needle.txt", "some/one/two/needle.txt", "default")]
    [InlineData("some/**/**/needle.txt", "some/other/needle.txt", "default")]
    [InlineData("**", ".asdf", "default")]
    [InlineData("**", "/x/.asdf", "default")]
    [InlineData("**/test", "test", "default")]
    [InlineData("/**/test", "/test", "default")]
    [InlineData("**/.*", "abc/.abc", "default")]
    [InlineData(".*/**", ".abc/abc", "default")]
    [InlineData("test/**", "test/", "default")]
    [InlineData("test/**", "test/one/two", "default")]
    [InlineData("some/*/needle.txt", "some/one/needle.txt", "default")]
    [InlineData("*some/path/to/hello.txt", "a/bigger/some/path/to/hello.txt", "default")]
    [InlineData("_[[]_[]]_[?]_[*]_!_", "_[_]_?_*_!_", "default")]
    [InlineData("{**/src/**,foo}", "abc/src/bar", "default")]
    [InlineData("{[}],foo}", "}", "default")]
    [InlineData("{a,b{c,d}}", "bd", "default")]
    [InlineData("foo{,.txt}", "foo", "empty-alternates")]
    [InlineData("aBcDeFg", "ABCDEFG", "case-insensitive")]
    [InlineData("abc/def", "abc/def", "literal-separator")]
    [InlineData("abc[/]def", "abc/def", "literal-separator")]
    [InlineData("\\[", "[", "backslash-escapes")]
    [InlineData("\\?", "?", "backslash-escapes")]
    [InlineData("\\*", "*", "backslash-escapes")]
    [InlineData("\\[a-z]", "\\a", "no-backslash-escapes")]
    [InlineData("\\?", "\\a", "no-backslash-escapes")]
    [InlineData("\\*", "\\\\", "no-backslash-escapes")]
    public void UpstreamMatchMatrixMatches(string pattern, string path, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));
        var set = GlobSet.Create([glob]);

        Assert.True(glob.IsMatch(Bytes(path)));
        Assert.True(set.IsMatch(Bytes(path)));
    }

    /// <summary>
    /// Verifies representative upstream globset non-match cases.
    /// </summary>
    /// <param name="pattern">The upstream glob pattern.</param>
    /// <param name="path">The candidate path.</param>
    /// <param name="option">The upstream builder option set.</param>
    [Theory]
    [InlineData("a*b*c", "abcd", "default")]
    [InlineData("abc*abc*abc", "abcabcabcabcabcabcabca", "default")]
    [InlineData("some/**/needle.txt", "some/other/notthis.txt", "default")]
    [InlineData("/**/test", "test", "default")]
    [InlineData("/**/test", "/one/notthis", "default")]
    [InlineData("**/.*", "ab.c", "default")]
    [InlineData("**/.*", "abc/ab.c", "default")]
    [InlineData(".*/**", ".abc", "default")]
    [InlineData("foo/**", "foo", "default")]
    [InlineData("*hello.txt", "hello.txt-and-then-some", "default")]
    [InlineData("*some/path/to/hello.txt", "some/other/path/to/hello.txt", "default")]
    [InlineData("a", "foo/a", "default")]
    [InlineData("./foo", "foo", "default")]
    [InlineData("**/foo", "foofoo", "default")]
    [InlineData("**/foo/bar", "foofoo/bar", "default")]
    [InlineData("/*.c", "mozilla-sha1/sha1.c", "default")]
    [InlineData("*.c", "mozilla-sha1/sha1.c", "literal-separator")]
    [InlineData("**/m4/ltoptions.m4", "csharp/src/packages/repositories.config", "literal-separator")]
    [InlineData("some/*/needle.txt", "some/one/two/needle.txt", "literal-separator")]
    [InlineData("abc?def", "abc/def", "literal-separator")]
    [InlineData("abc*def", "abc/def", "literal-separator")]
    [InlineData("foo{,.txt}", "foo", "default")]
    public void UpstreamNonMatchMatrixDoesNotMatch(string pattern, string path, string option)
    {
        var glob = Glob.Parse(Bytes(pattern), GetOptions(option));
        var set = GlobSet.Create([glob]);

        Assert.False(glob.IsMatch(Bytes(path)));
        Assert.False(set.IsMatch(Bytes(path)));
    }

    private static void AssertParseError(
        byte[] pattern,
        GlobParseErrorKind expectedKind,
        byte? expectedRangeStart = null,
        byte? expectedRangeEnd = null)
    {
        GlobParseException exception = Assert.Throws<GlobParseException>(() => Glob.Parse(pattern));

        Assert.Equal(expectedKind, exception.ErrorKind);
        Assert.Equal(pattern, exception.GlobPattern.ToArray());
        Assert.Equal(expectedRangeStart, exception.RangeStart);
        Assert.Equal(expectedRangeEnd, exception.RangeEnd);
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
