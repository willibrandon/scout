namespace Scout;

/// <summary>
/// Verifies plan-owning search APIs derive match semantics from the compiled plan.
/// </summary>
[TestClass]
public sealed class RegexSearchPlanAuthorityTests
{
    /// <summary>
    /// Verifies an unrelated empty pattern list cannot suppress a non-empty authoritative plan.
    /// </summary>
    [TestMethod]
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

        Assert.IsTrue(matched);
        Assert.AreEqual(1UL, sink.MatchedLines);
        Assert.AreEqual(1, sink.LineNumber);
        Assert.AreSequenceEqual("foo\n"u8.ToArray(), sink.Line.ToArray());
    }
}
