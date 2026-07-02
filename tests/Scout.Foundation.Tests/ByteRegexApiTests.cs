using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade intended for package consumers.
/// </summary>
public sealed class ByteRegexApiTests
{
    /// <summary>
    /// Verifies byte regex matching exposes byte offsets and spans.
    /// </summary>
    [Fact]
    public void FindsFirstMatch()
    {
        var regex = ByteRegex.Compile(@"(?i)[[:alpha:]]+\d+");

        ByteRegexMatch? match = regex.Find("11ABC123 yy"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new ByteRegexMatch(2, 6), match.Value);
        Assert.True(match.Value.Value("11ABC123 yy"u8).SequenceEqual("ABC123"u8));
        Assert.True(regex.IsMatch("ABC123"u8));
        Assert.False(regex.IsMatch("ABC"u8));
    }

    /// <summary>
    /// Verifies capture groups are exposed without leaking automata types.
    /// </summary>
    [Fact]
    public void FindsCaptures()
    {
        var regex = ByteRegex.Compile(@"([[:alpha:]]+)(\d+)");

        ByteRegexCaptures? captures = regex.FindCaptures("11ABC123 yy"u8);

        Assert.NotNull(captures);
        Assert.Equal(3, captures.GroupCount);
        Assert.Equal(new ByteRegexMatch(2, 6), captures.Match);
        Assert.Equal(new ByteRegexMatch(2, 6), captures.GetGroup(0));
        Assert.Equal(new ByteRegexMatch(2, 3), captures.GetGroup(1));
        Assert.Equal(new ByteRegexMatch(5, 3), captures.GetGroup(2));
        Assert.Equal(3, captures.ParticipatingCount());
    }

    /// <summary>
    /// Verifies byte mode can search arbitrary non-UTF-8 input.
    /// </summary>
    [Fact]
    public void MatchesArbitraryBytesWhenUtf8BoundaryChecksAreDisabled()
    {
        byte[] pattern = [0xff, (byte)'.'];
        var regex = ByteRegex.Compile(
            pattern,
            new ByteRegexOptions
            {
                Utf8 = false,
                UnicodeClasses = false,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[] input = [0x00, 0xff, 0xfe, 0x41];

        Assert.Equal(new ByteRegexMatch(1, 2), regex.Find(input));
    }

    /// <summary>
    /// Verifies match iteration uses caller-owned state.
    /// </summary>
    [Fact]
    public void IteratesMatchesWithState()
    {
        var regex = ByteRegex.Compile(@"\w+");
        var matches = new List<ByteRegexMatch>();

        int count = regex.ForEachMatch(
            "one two 3"u8,
            ref matches,
            AddMatch);

        Assert.Equal(3, count);
        Assert.Equal([new ByteRegexMatch(0, 3), new ByteRegexMatch(4, 3), new ByteRegexMatch(8, 1)], matches);
    }

    /// <summary>
    /// Verifies syntax errors are surfaced as byte regex parse exceptions.
    /// </summary>
    [Fact]
    public void ConvertsSyntaxErrorsToParseException()
    {
        ByteRegexParseException exception = Assert.Throws<ByteRegexParseException>(() => ByteRegex.Compile("["));

        Assert.Contains("byte offset", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.Offset);
    }

    /// <summary>
    /// Verifies ordered set matching is available through the public facade.
    /// </summary>
    [Fact]
    public void FindsSetMatch()
    {
        var set = ByteRegexSet.Compile(["foo[0-9]+", "bar[a-z]+"]);

        ByteRegexSetMatch? match = set.Find("xx barzz foo42"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new ByteRegexSetMatch(1, new ByteRegexMatch(3, 5)), match.Value);
        Assert.True(set.IsMatch("foo42"u8));
        Assert.Equal(2, set.CountMatches("foo1 barz"u8));
    }

    private static bool AddMatch(ReadOnlySpan<byte> input, ByteRegexMatch match, ref List<ByteRegexMatch> state)
    {
        state.Add(match);
        return true;
    }
}
