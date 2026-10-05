
namespace Scout;

/// <summary>
/// Verifies the regex adapter over Scout's matcher ABI.
/// </summary>
[TestClass]
public sealed class RegexMatcherTests
{
    /// <summary>
    /// Verifies the adapter returns a by-value matcher span.
    /// </summary>
    [TestMethod]
    public void FindsFirstMatch()
    {
        var matcher = RegexMatcher.Compile(@"(?i)[[:alpha:]]+\d+"u8);

        MatcherMatch? match = matcher.Find("11ABC123 yy"u8);

        Assert.IsTrue(match.HasValue);
        Assert.AreEqual(new MatcherMatch(2, 6), match.Value);
        Assert.IsTrue(matcher.IsMatch("ABC123"u8));
        Assert.IsFalse(matcher.IsMatch("ABC"u8));
    }

    /// <summary>
    /// Verifies generic struct-sink iteration reports non-overlapping matches without delegates.
    /// </summary>
    [TestMethod]
    public void IteratesMatchesThroughStructSink()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        var sink = new CollectingSink();

        int count = matcher.ForEachMatch("one two 3"u8, ref sink);

        Assert.AreEqual(3, count);
        Assert.AreEqual(3, sink.Count);
        Assert.AreEqual(0, sink.Starts[0]);
        Assert.AreEqual(4, sink.Starts[1]);
        Assert.AreEqual(8, sink.Starts[2]);
        Assert.AreEqual(3, sink.Lengths[0]);
        Assert.AreEqual(3, sink.Lengths[1]);
        Assert.AreEqual(1, sink.Lengths[2]);
    }

    /// <summary>
    /// Verifies a sink can stop iteration synchronously.
    /// </summary>
    [TestMethod]
    public void SinkCanStopIteration()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        var sink = new StoppingSink();

        int count = matcher.ForEachMatch("one two"u8, ref sink);

        Assert.AreEqual(0, count);
        Assert.AreEqual(new MatcherMatch(0, 3), sink.Match);
    }

    /// <summary>
    /// Verifies function-pointer iteration passes explicit stack-rooted state.
    /// </summary>
    [TestMethod]
    public unsafe void IteratesMatchesThroughFunctionPointer()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        int* state = stackalloc int[8];

        int count = matcher.ForEachMatch("one two 3"u8, &CollectCallback, state);

        Assert.AreEqual(3, count);
        Assert.AreEqual(3, state[0]);
        Assert.AreEqual(0, state[1]);
        Assert.AreEqual(3, state[2]);
        Assert.AreEqual(4, state[3]);
        Assert.AreEqual(3, state[4]);
        Assert.AreEqual(8, state[5]);
        Assert.AreEqual(1, state[6]);
        Assert.AreEqual(9, state[7]);
    }

    /// <summary>
    /// Verifies function-pointer iteration can stop synchronously.
    /// </summary>
    [TestMethod]
    public unsafe void FunctionPointerCanStopIteration()
    {
        var matcher = RegexMatcher.Compile(@"\w+"u8);
        int* state = stackalloc int[4];

        int count = matcher.ForEachMatch("one two"u8, &StopCallback, state);

        Assert.AreEqual(0, count);
        Assert.AreEqual(0, state[0]);
        Assert.AreEqual(3, state[1]);
        Assert.AreEqual(7, state[2]);
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
