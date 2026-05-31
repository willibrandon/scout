using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Verifies the multi-regex pattern set surface.
/// </summary>
public sealed class PatternSetTests
{
    /// <summary>
    /// Verifies literal-only patterns use one multi-regex Aho-Corasick accelerator.
    /// </summary>
    [Fact]
    public void UsesLiteralMultiRegexAccelerator()
    {
        var set = PatternSet.Compile(
        [
            "abcd"u8.ToArray(),
            "ab"u8.ToArray(),
            @"[[:alpha:]]+\d+"u8.ToArray(),
        ]);

        Assert.True(set.UsesLiteralAccelerator);
        Assert.True(set.IsMatch("zzab"u8));
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(2, 4)), set.Find("zzabcd abc123"u8));
        Assert.Equal([0, 1, 2], set.MatchingPatternIds("zzabcd abc123"u8));
    }

    /// <summary>
    /// Verifies matching pattern identifiers are returned in insertion order.
    /// </summary>
    [Fact]
    public void ReturnsMatchingPatternIdsInInsertionOrder()
    {
        var set = PatternSet.Compile(
        [
            "foo"u8.ToArray(),
            @"[[:alpha:]]+\d+"u8.ToArray(),
            "bar"u8.ToArray(),
        ]);

        IReadOnlyList<int> matches = set.MatchingPatternIds("xxfoo bar abc123"u8);

        Assert.Equal([0, 1, 2], matches);
        Assert.True(set.IsMatch("abc123"u8));
        Assert.False(set.IsMatch("123"u8));
    }

    /// <summary>
    /// Verifies the selected match is the leftmost match across all patterns.
    /// </summary>
    [Fact]
    public void FindsLeftmostPatternMatch()
    {
        var set = PatternSet.Compile(
        [
            "bar"u8.ToArray(),
            "foo"u8.ToArray(),
        ]);

        PatternSetMatch? match = set.Find("xxfoo bar"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new PatternSetMatch(1, new RegexMatch(2, 3)), match.Value);
    }

    /// <summary>
    /// Verifies pattern order breaks ties at the same match offset.
    /// </summary>
    [Fact]
    public void UsesPatternOrderToBreakTies()
    {
        var first = PatternSet.Compile(
        [
            "ab"u8.ToArray(),
            "a"u8.ToArray(),
        ]);
        var second = PatternSet.Compile(
        [
            "a"u8.ToArray(),
            "ab"u8.ToArray(),
        ]);

        Assert.Equal(new PatternSetMatch(0, new RegexMatch(1, 2)), first.Find("zab"u8));
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(1, 1)), second.Find("zab"u8));
    }

    /// <summary>
    /// Verifies an empty set never matches.
    /// </summary>
    [Fact]
    public void EmptySetDoesNotMatch()
    {
        var set = PatternSet.Compile([]);

        Assert.Equal(0, set.Count);
        Assert.False(set.IsMatch("anything"u8));
        Assert.Null(set.Find("anything"u8));
        Assert.Empty(set.MatchingPatternIds("anything"u8));
    }
}
