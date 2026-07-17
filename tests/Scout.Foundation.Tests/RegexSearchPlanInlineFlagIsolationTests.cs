using System.Text;

namespace Scout;

/// <summary>
/// Verifies each source pattern in an authoritative search plan has an independent inline-flag scope.
/// </summary>
public sealed class RegexSearchPlanInlineFlagIsolationTests
{
    /// <summary>
    /// Verifies unscoped inline flags cannot affect a later source-pattern branch.
    /// </summary>
    [Fact]
    public void CombinedPatternBranchesIsolateSupportedInlineFlags()
    {
        AssertBranchIsolation(
            "(?i)a",
            "(?i:a)",
            "B",
            "A|b",
            new RegexMatch(0, 1));
        AssertBranchIsolation(
            "(?-m)^a$",
            "(?-m:^a$)",
            "^b$",
            "x\na\nb\n",
            new RegexMatch(4, 1));
        AssertBranchIsolation(
            "(?s)a.b",
            "(?s:a.b)",
            "c.d",
            "a\nb|c\nd",
            new RegexMatch(0, 3));
        AssertBranchIsolation(
            "(?U)a.*b",
            "(?U:a.*b)",
            "c.*d",
            "a1b2b c1d2d",
            new RegexMatch(0, 3),
            new RegexMatch(6, 5));
        AssertBranchIsolation(
            @"(?-u)\w",
            @"(?-u:\w)",
            @"\w",
            "é",
            new RegexMatch(0, 2));
        AssertBranchIsolation(
            "(?R)^a$",
            "(?R:^a$)",
            "^b$",
            "a\r\nb\r\n",
            new RegexMatch(0, 1));
        AssertBranchIsolation(
            "(?x)a b",
            "(?x:a b)",
            "c d",
            "ab|cd",
            new RegexMatch(0, 2));
    }

    private static void AssertBranchIsolation(
        string unscopedFirstPattern,
        string scopedFirstPattern,
        string secondPattern,
        string haystack,
        params RegexMatch[] expected)
    {
        RegexSearchPlan unscopedPlan = CreatePlan(
            unscopedFirstPattern,
            secondPattern);
        RegexSearchPlan scopedPlan = CreatePlan(
            scopedFirstPattern,
            secondPattern);
        byte[] bytes = Encoding.UTF8.GetBytes(haystack);

        Assert.Equal(expected, FindAll(unscopedPlan, bytes));
        Assert.Equal(expected, FindAll(scopedPlan, bytes));
        Assert.Equal(scopedPlan.Matcher.CountMatches(bytes), unscopedPlan.Matcher.CountMatches(bytes));
        Assert.Equal(scopedPlan.Matcher.SumMatchSpans(bytes), unscopedPlan.Matcher.SumMatchSpans(bytes));
    }

    private static RegexSearchPlan CreatePlan(
        string firstPattern,
        string secondPattern)
    {
        return RegexSearchPlan.Create(
            [Encoding.UTF8.GetBytes(firstPattern), Encoding.UTF8.GetBytes(secondPattern)],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                multiline: true));
    }

    private static RegexMatch[] FindAll(
        RegexSearchPlan plan,
        ReadOnlySpan<byte> haystack)
    {
        var matches = new List<RegexMatch>();
        int startAt = 0;
        while (startAt <= haystack.Length)
        {
            RegexMatch? match = plan.Matcher.Find(haystack, startAt);
            if (!match.HasValue)
            {
                break;
            }

            matches.Add(match.Value);
            startAt = match.Value.Length == 0
                ? match.Value.Start + 1
                : match.Value.End;
        }

        return matches.ToArray();
    }
}
