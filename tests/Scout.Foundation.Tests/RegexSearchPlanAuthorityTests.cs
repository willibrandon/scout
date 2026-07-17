namespace Scout;

/// <summary>
/// Verifies plan-owning search APIs derive match semantics from the compiled plan.
/// </summary>
public sealed class RegexSearchPlanAuthorityTests
{
    /// <summary>
    /// Verifies an unrelated empty pattern list cannot suppress a non-empty authoritative plan.
    /// </summary>
    [Fact]
    public void EmptyNeedlesDoNotSuppressAuthoritativePlan()
    {
        byte[][] patterns = [@"\bfoo\b"u8.ToArray()];
        var plan = RegexSearchPlan.Create(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            "foo\nbar\n"u8,
            Array.Empty<byte[]>(),
            plan,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal("foo\n"u8.ToArray(), sink.Line.ToArray());
    }
}
