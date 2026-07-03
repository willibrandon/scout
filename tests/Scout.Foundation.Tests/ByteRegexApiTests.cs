using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade intended for package consumers.
/// </summary>
public sealed class ByteRegexApiTests
{
    private const int ConcurrentSearchIterations = 64;
    private const int ConcurrentHaystackCount = 8;

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

    /// <summary>
    /// Verifies a compiled regex can be shared across concurrent lazy-DFA searches.
    /// </summary>
    [Fact]
    public void SharedRegexFindsFromMultipleThreads()
    {
        var regex = ByteRegex.Compile(
            @"func \w+",
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = true,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                ByteRegexMatch? match = regex.Find(haystack);
                AssertFunctionMatch(match, haystack);
                Assert.True(regex.IsMatch(haystack));

                var matches = new List<ByteRegexMatch>();
                int count = regex.ForEachMatch(haystack, ref matches, AddMatch);

                Assert.Equal(1, count);
                Assert.Single(matches);
                Assert.Equal(match!.Value, matches[0]);
            }
        });
    }

    /// <summary>
    /// Verifies a compiled regex can be shared across concurrent PikeVM searches.
    /// </summary>
    [Fact]
    public void SharedRegexPikeVmFallbackFindsFromMultipleThreads()
    {
        var regex = ByteRegex.Compile(
            @"func \w+",
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = true,
                DfaSizeLimit = 1,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                AssertFunctionMatch(regex.Find(haystack), haystack);
            }
        });
    }

    /// <summary>
    /// Verifies generic capture matching can be shared across concurrent searches.
    /// </summary>
    [Fact]
    public void SharedRegexCapturesFromMultipleThreads()
    {
        var regex = ByteRegex.Compile(
            @"func ([a-z_]+)",
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = false,
                EngineMode = ByteRegexEngineMode.AutomataOnly,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                ByteRegexCaptures? captures = regex.FindCaptures(haystack);

                Assert.NotNull(captures);
                AssertFunctionMatch(captures.Match, haystack);
                ByteRegexMatch? group = captures.GetGroup(1);
                Assert.True(group.HasValue);
                Assert.True(group.Value.Value(haystack).StartsWith("handler_"u8));
            }
        });
    }

    /// <summary>
    /// Verifies a compiled regex set can be shared across concurrent searches.
    /// </summary>
    [Fact]
    public void SharedRegexSetFindsFromMultipleThreads()
    {
        var set = ByteRegexSet.Compile(
            ["func \\w+", "return \\w+"],
            new ByteRegexOptions
            {
                MultiLine = true,
                UnicodeClasses = true,
                DfaSizeLimit = 1,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[][] haystacks = CreateConcurrentRegexHaystacks();

        Parallel.For(0, ConcurrentSearchIterations, _ =>
        {
            foreach (byte[] haystack in haystacks)
            {
                ByteRegexSetMatch? match = set.Find(haystack);

                Assert.True(match.HasValue);
                Assert.Equal(0, match.Value.PatternId);
                AssertFunctionMatch(match.Value.Match, haystack);
            }
        });
    }

    private static bool AddMatch(ReadOnlySpan<byte> input, ByteRegexMatch match, ref List<ByteRegexMatch> state)
    {
        state.Add(match);
        return true;
    }

    private static byte[][] CreateConcurrentRegexHaystacks()
    {
        return Enumerable.Range(0, ConcurrentHaystackCount)
            .Select(static index => Encoding.UTF8.GetBytes($"package mod{index};\nfunc handler_{index}() {{ return errors.New(\"boom\") }}\n"))
            .ToArray();
    }

    private static void AssertFunctionMatch(ByteRegexMatch? match, ReadOnlySpan<byte> haystack)
    {
        Assert.True(match.HasValue);
        AssertFunctionMatch(match.Value, haystack);
    }

    private static void AssertFunctionMatch(ByteRegexMatch match, ReadOnlySpan<byte> haystack)
    {
        ReadOnlySpan<byte> value = match.Value(haystack);
        Assert.True(value.StartsWith("func handler_"u8));
        Assert.Equal(haystack.IndexOf("func "u8), match.Start);
    }
}
