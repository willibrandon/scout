using System;

namespace Scout;

/// <summary>
/// Verifies the regex adapter over Scout's matcher ABI.
/// </summary>
public sealed class RegexMatcherTests
{
    /// <summary>
    /// Verifies the adapter returns a by-value matcher span.
    /// </summary>
    [Fact]
    public void FindsFirstMatch()
    {
        var matcher = RegexMatcher.Compile(@"(?i)[[:alpha:]]+\d+"u8);

        MatcherMatch? match = matcher.Find("11ABC123 yy"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new MatcherMatch(2, 6), match.Value);
        Assert.True(matcher.IsMatch("ABC123"u8));
        Assert.False(matcher.IsMatch("ABC"u8));
    }

    /// <summary>
    /// Verifies generic struct-sink iteration reports non-overlapping matches without delegates.
    /// </summary>
    [Fact]
    public void IteratesMatchesThroughStructSink()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        var sink = new CollectingSink();

        int count = matcher.ForEachMatch("one two 3"u8, ref sink);

        Assert.Equal(3, count);
        Assert.Equal(3, sink.Count);
        Assert.Equal(0, sink.Starts[0]);
        Assert.Equal(4, sink.Starts[1]);
        Assert.Equal(8, sink.Starts[2]);
        Assert.Equal(3, sink.Lengths[0]);
        Assert.Equal(3, sink.Lengths[1]);
        Assert.Equal(1, sink.Lengths[2]);
    }

    /// <summary>
    /// Verifies a sink can stop iteration synchronously.
    /// </summary>
    [Fact]
    public void SinkCanStopIteration()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        var sink = new StoppingSink();

        int count = matcher.ForEachMatch("one two"u8, ref sink);

        Assert.Equal(0, count);
        Assert.Equal(new MatcherMatch(0, 3), sink.Match);
    }

    /// <summary>
    /// Verifies function-pointer iteration passes explicit stack-rooted state.
    /// </summary>
    [Fact]
    public unsafe void IteratesMatchesThroughFunctionPointer()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        int* state = stackalloc int[8];

        int count = matcher.ForEachMatch("one two 3"u8, &CollectCallback, state);

        Assert.Equal(3, count);
        Assert.Equal(3, state[0]);
        Assert.Equal(0, state[1]);
        Assert.Equal(3, state[2]);
        Assert.Equal(4, state[3]);
        Assert.Equal(3, state[4]);
        Assert.Equal(8, state[5]);
        Assert.Equal(1, state[6]);
        Assert.Equal(9, state[7]);
    }

    /// <summary>
    /// Verifies function-pointer iteration can stop synchronously.
    /// </summary>
    [Fact]
    public unsafe void FunctionPointerCanStopIteration()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        int* state = stackalloc int[4];

        int count = matcher.ForEachMatch("one two"u8, &StopCallback, state);

        Assert.Equal(0, count);
        Assert.Equal(0, state[0]);
        Assert.Equal(3, state[1]);
        Assert.Equal(7, state[2]);
    }

    private static unsafe bool CollectCallback(void* state, ReadOnlySpan<byte> haystack, MatcherMatch match)
    {
        int* values = (int*)state;
        int index = values[0];
        values[(index * 2) + 1] = match.Start;
        values[(index * 2) + 2] = match.Length;
        values[0] = index + 1;
        values[7] = haystack.Length;
        return true;
    }

    private static unsafe bool StopCallback(void* state, ReadOnlySpan<byte> haystack, MatcherMatch match)
    {
        int* values = (int*)state;
        values[0] = match.Start;
        values[1] = match.Length;
        values[2] = haystack.Length;
        return false;
    }
}
