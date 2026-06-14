namespace Scout;

/// <summary>
/// Covers the multiline line-boundary literal regex fast path.
/// </summary>
public sealed class RegexLineBoundaryLiteralEngineTests
{
    /// <summary>
    /// Ensures a leading inline multiline flag can drive the fast path used by Rebar.
    /// </summary>
    [Fact]
    public void CompileUsesLineBoundaryLiteralEngineForLeadingInlineMultilineAlternation()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);

        Assert.Equal(RegexEngineKind.LineBoundaryLiteral, regex.EngineKind);
    }

    /// <summary>
    /// Ensures root multiline options select the same fast path.
    /// </summary>
    [Fact]
    public void CompileUsesLineBoundaryLiteralEngineForRootMultilineAlternation()
    {
        var regex = RegexAutomaton.Compile(
            "^Sherlock Holmes|Sherlock Holmes$"u8,
            caseInsensitive: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.LineBoundaryLiteral, regex.EngineKind);
    }

    /// <summary>
    /// Finds literal occurrences adjacent to either accepted line boundary.
    /// </summary>
    [Fact]
    public void LineBoundaryLiteralFindsStartOrEndLineBoundary()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);
        byte[] haystack = "Sherlock Holmes walks\nDr. Watson meets Sherlock Holmes\nx Sherlock Holmes y\nSherlock Holmes"u8.ToArray();

        Assert.Equal(new RegexMatch(0, "Sherlock Holmes"u8.Length), regex.Find(haystack));

        int secondStart = "Sherlock Holmes walks\nDr. Watson meets "u8.Length;
        Assert.Equal(new RegexMatch(secondStart, "Sherlock Holmes"u8.Length), regex.Find(haystack, startAt: 1));

        int lastStart = haystack.Length - "Sherlock Holmes"u8.Length;
        Assert.Equal(new RegexMatch(lastStart, "Sherlock Holmes"u8.Length), regex.Find(haystack, secondStart + 1));
    }

    /// <summary>
    /// Counts a literal once when it satisfies both line-start and line-end alternatives.
    /// </summary>
    [Fact]
    public void LineBoundaryLiteralCountsEachLiteralOccurrenceOnce()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);
        byte[] haystack = "Sherlock Holmes\nDr. Watson meets Sherlock Holmes\nx Sherlock Holmes y\nSherlock Holmes"u8.ToArray();

        Assert.Equal(3, regex.CountMatches(haystack));
        Assert.Equal(3 * "Sherlock Holmes"u8.Length, regex.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Rejects literal occurrences that are not adjacent to either line boundary.
    /// </summary>
    [Fact]
    public void LineBoundaryLiteralDoesNotMatchInteriorLiteral()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);

        Assert.Null(regex.Find("x Sherlock Holmes y"u8));
        Assert.Null(regex.MatchAt("x Sherlock Holmes y"u8, startAt: 2));
    }

    private static RegexAutomaton Compile(ReadOnlySpan<byte> pattern)
    {
        return RegexAutomaton.Compile(
            pattern,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
    }
}
