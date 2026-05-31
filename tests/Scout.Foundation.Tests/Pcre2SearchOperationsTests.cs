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
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--passthru", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--only-matching", "--context", "1", "needle", "haystack.txt")));
        Assert.True(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--replace", "x", "--context", "1", "needle", "haystack.txt")));
    }

    /// <summary>
    /// Verifies max-count does not hide unsupported PCRE2 option combinations.
    /// </summary>
    [Fact]
    public void RejectsUnsupportedOptionCombinationsWithMaxCount()
    {
        Assert.False(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--max-count", "2", "--stats", "needle")));
        Assert.False(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--json", "--context", "1", "needle")));
        Assert.False(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--context", "1", "needle")));
        Assert.False(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--passthru", "needle")));
        Assert.False(Pcre2SearchOperations.CanRun(ParseLowArgs("--pcre2", "--multiline", "--invert-match", "needle")));
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
