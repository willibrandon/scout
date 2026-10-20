using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Exercises match and captures enumeration through the exported regex API on every supported framework.
/// </summary>
[TestClass]
public sealed class ByteRegexEnumerationTests
{
    /// <summary>
    /// Verifies empty matches are stepped over the way Rust's find_iter steps over them.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="input">The text to search.</param>
    /// <param name="expected">The expected start:length pairs.</param>
    [TestMethod]
    [DataRow(@"\b", "ab cd", "0:0,2:0,3:0,5:0")]
    [DataRow("a*", "baab", "0:0,1:2,4:0")]
    [DataRow("", "ab", "0:0,1:0,2:0")]
    [DataRow("a|", "aab", "0:1,1:1,3:0")]
    [DataRow("a", "xaxa", "1:1,3:1")]
    public void EmptyMatchesFollowRustIteration(string pattern, string input, string expected)
    {
        byte[] haystack = Encoding.UTF8.GetBytes(input);
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = mode });
            Assert.AreEqual(expected, Spans(regex, haystack), $"matches for {pattern} in {mode}.");
            Assert.AreEqual(expected, CaptureSpans(regex, haystack), $"captures for {pattern} in {mode}.");
        }
    }

    /// <summary>
    /// Verifies both enumerators report the spans the callback and counting APIs report.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="input">The text to search.</param>
    [TestMethod]
    [DataRow("(a)(b)?", "ab a ab")]
    [DataRow("(x*)", "baab")]
    [DataRow(@"(\b)", "ab cd")]
    [DataRow("(a|)", "aab")]
    [DataRow("(?:)", "")]
    [DataRow("[0-9]+", "a1b22c333")]
    public void EnumeratorsAgreeWithCallbackIteration(string pattern, string input)
    {
        byte[] haystack = Encoding.UTF8.GetBytes(input);
        var reported = new StringBuilder();
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = mode });
            reported.Clear();
            int count = regex.ForEachMatch(haystack, ref reported, AppendSpan);

            Assert.AreEqual(reported.ToString(), Spans(regex, haystack), $"matches for {pattern} in {mode}.");
            Assert.AreEqual(reported.ToString(), CaptureSpans(regex, haystack), $"captures for {pattern} in {mode}.");
            Assert.AreEqual(count, regex.Count(haystack), $"count for {pattern} in {mode}.");
        }
    }

    /// <summary>
    /// Verifies each enumerated captures value carries the groups of its own match.
    /// </summary>
    [TestMethod]
    public void GroupsAreReportedPerMatch()
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile("(a)(b)?", new ByteRegexOptions { EngineMode = mode });
            int index = 0;
            foreach (ByteRegexCaptures captures in regex.EnumerateCaptures("ab a"u8))
            {
                Assert.AreEqual(3, captures.GroupCount, $"group count in {mode}.");
                if (index == 0)
                {
                    Assert.AreEqual(new ByteRegexMatch(0, 2), captures.Match, $"first match in {mode}.");
                    Assert.AreEqual(new ByteRegexMatch(0, 1), captures.GetGroup(1), $"first group in {mode}.");
                    Assert.AreEqual(new ByteRegexMatch(1, 1), captures.GetGroup(2), $"second group in {mode}.");
                }
                else
                {
                    Assert.AreEqual(new ByteRegexMatch(3, 1), captures.Match, $"second match in {mode}.");
                    Assert.AreEqual(new ByteRegexMatch(3, 1), captures.GetGroup(1), $"first group again in {mode}.");
                    Assert.IsNull(captures.GetGroup(2), $"absent group in {mode}.");
                }

                index++;
            }

            Assert.AreEqual(2, index, $"match count in {mode}.");
        }
    }

    /// <summary>
    /// Verifies the start offset skips earlier matches and out-of-range offsets behave like the single-match APIs.
    /// </summary>
    [TestMethod]
    public void StartOffsetIsHonored()
    {
        var regex = ByteRegex.Compile("a");
        byte[] haystack = "xaxa"u8.ToArray();

        Assert.AreEqual("3:1", Spans(regex, haystack, 2));
        Assert.AreEqual("3:1", CaptureSpans(regex, haystack, 2));
        Assert.AreEqual(string.Empty, Spans(regex, haystack, 10));
        Assert.AreEqual(string.Empty, CaptureSpans(regex, haystack, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => regex.EnumerateMatches([], -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => regex.EnumerateCaptures([], -1));
    }

    /// <summary>
    /// Verifies leaving an enumeration early does not disturb later enumerations of the same regex.
    /// </summary>
    [TestMethod]
    public void BreakingOutEarlyLeavesTheRegexReusable()
    {
        var regex = ByteRegex.Compile("[a-z]");
        byte[] haystack = "abc"u8.ToArray();
        foreach (ByteRegexMatch match in regex.EnumerateMatches(haystack))
        {
            Assert.AreEqual(new ByteRegexMatch(0, 1), match);
            break;
        }

        foreach (ByteRegexCaptures captures in regex.EnumerateCaptures(haystack))
        {
            Assert.AreEqual(new ByteRegexMatch(0, 1), captures.Match);
            break;
        }

        Assert.AreEqual("0:1,1:1,2:1", Spans(regex, haystack));
        Assert.AreEqual("0:1,1:1,2:1", CaptureSpans(regex, haystack));
    }

    /// <summary>
    /// Verifies set enumeration takes the leftmost match, breaks ties by pattern order, and counts like the set.
    /// </summary>
    /// <param name="input">The text to search.</param>
    /// <param name="expected">The expected start:length:pattern triples.</param>
    [TestMethod]
    [DataRow("ab a b", "0:2:0,3:1:1,5:1:2")]
    [DataRow("ba", "0:1:2,1:1:1")]
    [DataRow("", "")]
    public void SetEnumerationSelectsLeftmostByPatternOrder(string input, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        byte[] haystack = Encoding.UTF8.GetBytes(input);
        var set = ByteRegexSet.Compile(["ab", "a", "b"]);
        var spans = new StringBuilder();
        foreach (ByteRegexSetMatch match in set.EnumerateMatches(haystack))
        {
            Append(spans, match.Match);
            spans.Append(':').Append(match.PatternId);
        }

        Assert.AreEqual(expected, spans.ToString());
        Assert.AreEqual(expected.Length == 0 ? 0 : expected.Split(',').Length, set.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies set enumeration steps over empty matches the way the set counts them.
    /// </summary>
    [TestMethod]
    public void SetEnumerationStepsOverEmptyMatches()
    {
        var set = ByteRegexSet.Compile(["x*", "b"]);
        byte[] haystack = "ab"u8.ToArray();
        var spans = new StringBuilder();
        int count = 0;
        foreach (ByteRegexSetMatch match in set.EnumerateMatches(haystack))
        {
            Append(spans, match.Match);
            count++;
        }

        Assert.AreEqual("0:0,1:0,2:0", spans.ToString());
        Assert.AreEqual(count, set.CountMatches(haystack));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => set.EnumerateMatches([], -1));
    }

    /// <summary>
    /// Verifies set captures carry the whole-pattern group of the matching pattern, and that a set without such groups yields nothing.
    /// </summary>
    [TestMethod]
    public void SetCapturesEnumerationYieldsWholePatternGroups()
    {
        var set = ByteRegexSet.Compile(["(a)", "(b)"]);
        byte[] haystack = "ab"u8.ToArray();
        int index = 0;
        foreach (ByteRegexCaptures captures in set.EnumerateCaptures(haystack))
        {
            Assert.AreEqual(new ByteRegexMatch(index, 1), captures.Match);
            Assert.AreEqual(2, captures.ParticipatingCount());
            Assert.AreEqual(captures.Match, captures.GetGroup(captures.GroupCount - 1));
            index++;
        }

        Assert.AreEqual(2, index);

        var plain = ByteRegexSet.Compile(["a", "b"]);
        foreach (ByteRegexCaptures captures in plain.EnumerateCaptures(haystack))
        {
            Assert.Fail("A set without whole-pattern captures yielded " + captures.Match);
        }

        Assert.IsNull(plain.FindCaptures(haystack));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => set.EnumerateCaptures([], -1));
    }

    private static bool AppendSpan(ReadOnlySpan<byte> input, ByteRegexMatch match, ref StringBuilder state)
    {
        Append(state, match);
        return true;
    }

    private static string Spans(ByteRegex regex, byte[] haystack, int startAt = 0)
    {
        var spans = new StringBuilder();
        foreach (ByteRegexMatch match in regex.EnumerateMatches(haystack, startAt))
        {
            Append(spans, match);
        }

        return spans.ToString();
    }

    private static string CaptureSpans(ByteRegex regex, byte[] haystack, int startAt = 0)
    {
        var spans = new StringBuilder();
        foreach (ByteRegexCaptures captures in regex.EnumerateCaptures(haystack, startAt))
        {
            Append(spans, captures.Match);
        }

        return spans.ToString();
    }

    private static void Append(StringBuilder spans, ByteRegexMatch match)
    {
        if (spans.Length > 0)
        {
            spans.Append(',');
        }

        spans.Append(match.Start).Append(':').Append(match.Length);
    }
}
