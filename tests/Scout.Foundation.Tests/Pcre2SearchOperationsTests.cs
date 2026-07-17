namespace Scout;

/// <summary>
/// Verifies PCRE2 search planning behavior.
/// </summary>
public sealed class Pcre2SearchOperationsTests
{
    /// <summary>
    /// Verifies max-count is allowed by the native PCRE2 search path.
    /// </summary>
    [Fact]
    public void AllowsMaxCount()
    {
        CliLowArgs lowArgs = ParseLowArgs("--pcre2", "--max-count", "2", "needle", "haystack.txt");

        Assert.Equal(2UL, lowArgs.MaxCount);
        Assert.True(Pcre2SearchOperations.CanRun(lowArgs));
    }

    /// <summary>
    /// Verifies line-oriented inverted searches are allowed by the native PCRE2 path.
    /// </summary>
    [Fact]
    public void AllowsLineOrientedInvertMatch()
    {
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--invert-match", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--invert-match", "--count-matches", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--invert-match", "--json", "--only-matching", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--invert-match", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--only-matching", "needle", "haystack.txt")));
    }

    /// <summary>
    /// Verifies line-oriented context output can use the native PCRE2 search path.
    /// </summary>
    [Fact]
    public void AllowsLineOrientedContextOutput()
    {
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--fixed-strings", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--fixed-strings", "--json", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "--json", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "--count", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--passthru", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--only-matching", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--replace", "x", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--passthru", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--replace", "x", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--only-matching", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--only-matching", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--count", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--count-matches", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--files-with-matches", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--files-without-match", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--count", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--files-with-matches", "--passthru", "needle", "haystack.txt")));
    }

    /// <summary>
    /// Verifies line-oriented vimgrep output can use the native PCRE2 search path.
    /// </summary>
    [Fact]
    public void AllowsLineOrientedVimgrepOutput()
    {
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--only-matching", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--invert-match", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--passthru", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--count", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--json", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--multiline", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--multiline", "--only-matching", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--multiline", "--replace", "x", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--only-matching", "--count", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--invert-match", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--vimgrep", "--multiline", "--invert-match", "needle", "haystack.txt")));
    }

    /// <summary>
    /// Verifies multiline context output can use the native PCRE2 search path.
    /// </summary>
    [Fact]
    public void AllowsMultilineContextOutput()
    {
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--fixed-strings", "--multiline", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--passthru", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "--multiline", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "--multiline", "--count", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "--multiline", "--only-matching", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--multiline", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "--json", "--multiline", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--null-data", "--json", "--multiline", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--multiline", "--passthru", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--multiline", "--invert-match", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--multiline", "--replace", "x", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--multiline", "--replace", "x", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--only-matching", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--replace", "x", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--invert-match", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--vimgrep", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--vimgrep", "--only-matching", "--context", "1", "needle")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--vimgrep", "--replace", "x", "--context", "1", "needle")));
    }

    /// <summary>
    /// Verifies stats are supported by the native PCRE2 search path.
    /// </summary>
    [Fact]
    public void AllowsStats()
    {
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--stats", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--max-count", "2", "--stats", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--stats", "needle", "haystack.txt")));
    }

    /// <summary>
    /// Verifies Unicode PCRE2 searches tolerate invalid UTF-8 subject bytes like ripgrep.
    /// </summary>
    [Fact]
    public void UnicodeCompileOptionsAllowInvalidUtf8Subjects()
    {
        Pcre2CompileOptions unicodeOptions = Pcre2SearchOperations.GetPcre2CompileOptions(
            ParseLowArgs("--pcre2", "needle", "haystack.txt"),
            ["needle"u8.ToArray()]);
        Pcre2CompileOptions byteOptions = Pcre2SearchOperations.GetPcre2CompileOptions(
            ParseLowArgs("--pcre2", "--no-pcre2-unicode", "needle", "haystack.txt"),
            ["needle"u8.ToArray()]);

        Assert.True((unicodeOptions & Pcre2CompileOptions.Utf) != 0);
        Assert.True((unicodeOptions & Pcre2CompileOptions.UnicodeProperties) != 0);
        Assert.True((unicodeOptions & Pcre2CompileOptions.MatchInvalidUtf) != 0);
        Assert.False((byteOptions & Pcre2CompileOptions.Utf) != 0);
        Assert.False((byteOptions & Pcre2CompileOptions.UnicodeProperties) != 0);
        Assert.False((byteOptions & Pcre2CompileOptions.MatchInvalidUtf) != 0);
    }

    /// <summary>
    /// Verifies capture replay receives the same LF and CRLF-normalized subject as the original
    /// line-oriented PCRE2 search.
    /// </summary>
    /// <param name="crlf">Whether CRLF-aware output is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureReplayUsesExactMatchLineNormalization(bool crlf)
    {
        ReadOnlyMemory<byte> lineTerminator = crlf
            ? "\r\n"u8.ToArray()
            : "\n"u8.ToArray();

        Assert.Equal(
            "foo"u8.ToArray(),
            Pcre2SearchOperations.GetPcre2MatchLine("foo\n"u8, lineTerminator).ToArray());
        Assert.Equal(
            "foo"u8.ToArray(),
            Pcre2SearchOperations.GetPcre2MatchLine("foo\r\n"u8, lineTerminator).ToArray());
    }

    private static CliLowArgs ParseLowArgs(params string[] arguments)
    {
        var osArguments = new OsString[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index] = OsString.FromText(arguments[index]);
        }

        CliParseResult result = CliParser.Parse(osArguments);
        Assert.Equal(CliParseStatus.Ok, result.Status);
        return result.LowArgs!;
    }
}
