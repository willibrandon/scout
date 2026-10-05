namespace Scout;

/// <summary>
/// Covers the multiline line-boundary literal regex fast path.
/// </summary>
[TestClass]
public sealed class RegexLineBoundaryLiteralEngineTests
{
    /// <summary>
    /// Ensures a leading inline multiline flag can drive the fast path used by Rebar.
    /// </summary>
    [TestMethod]
    public void CompileUsesLineBoundaryLiteralEngineForLeadingInlineMultilineAlternation()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);

        Assert.AreEqual(RegexEngineKind.LineBoundaryLiteral, regex.EngineKind);
    }

    /// <summary>
    /// Ensures root multiline options select the same fast path.
    /// </summary>
    [TestMethod]
    public void CompileUsesLineBoundaryLiteralEngineForRootMultilineAlternation()
    {
        var regex = RegexAutomaton.Compile(
            "^Sherlock Holmes|Sherlock Holmes$"u8,
            caseInsensitive: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.AreEqual(RegexEngineKind.LineBoundaryLiteral, regex.EngineKind);
    }

    /// <summary>
    /// Finds literal occurrences adjacent to either accepted line boundary.
    /// </summary>
    [TestMethod]
    public void LineBoundaryLiteralFindsStartOrEndLineBoundary()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);
        byte[] haystack = "Sherlock Holmes walks\nDr. Watson meets Sherlock Holmes\nx Sherlock Holmes y\nSherlock Holmes"u8.ToArray();

        Assert.AreEqual(new RegexMatch(0, "Sherlock Holmes"u8.Length), regex.Find(haystack));

        int secondStart = "Sherlock Holmes walks\nDr. Watson meets "u8.Length;
        Assert.AreEqual(new RegexMatch(secondStart, "Sherlock Holmes"u8.Length), regex.Find(haystack, startAt: 1));

        int lastStart = haystack.Length - "Sherlock Holmes"u8.Length;
        Assert.AreEqual(new RegexMatch(lastStart, "Sherlock Holmes"u8.Length), regex.Find(haystack, secondStart + 1));
    }

    /// <summary>
    /// Counts a literal once when it satisfies both line-start and line-end alternatives.
    /// </summary>
    [TestMethod]
    public void LineBoundaryLiteralCountsEachLiteralOccurrenceOnce()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);
        byte[] haystack = "Sherlock Holmes\nDr. Watson meets Sherlock Holmes\nx Sherlock Holmes y\nSherlock Holmes"u8.ToArray();

        Assert.AreEqual(3, regex.CountMatches(haystack));
        Assert.AreEqual(3 * "Sherlock Holmes"u8.Length, regex.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Rejects literal occurrences that are not adjacent to either line boundary.
    /// </summary>
    [TestMethod]
    public void LineBoundaryLiteralDoesNotMatchInteriorLiteral()
    {
        RegexAutomaton regex = Compile("(?m)^Sherlock Holmes|Sherlock Holmes$"u8);

        Assert.IsNull(regex.Find("x Sherlock Holmes y"u8));
        Assert.IsNull(regex.MatchAt("x Sherlock Holmes y"u8, startAt: 2));
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
