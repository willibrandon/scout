using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies named special escapes decode inside character classes on every engine path.
/// </summary>
[TestClass]
public sealed class ByteRegexClassEscapeTests
{
    /// <summary>
    /// Verifies a class holding one special escape matches that byte and not the escape letter.
    /// </summary>
    /// <param name="escape">The escape as written in the pattern.</param>
    /// <param name="value">The byte the escape names.</param>
    [TestMethod]
    [DataRow(@"\a", (byte)0x07)]
    [DataRow(@"\f", (byte)0x0C)]
    [DataRow(@"\t", (byte)0x09)]
    [DataRow(@"\n", (byte)0x0A)]
    [DataRow(@"\r", (byte)0x0D)]
    [DataRow(@"\v", (byte)0x0B)]
    public void SingleEscapeClassMatchesOnlyItsByte(string escape, byte value)
    {
        ArgumentNullException.ThrowIfNull(escape);
        byte letter = (byte)escape[1];
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            ByteRegex regex = Compile($"[{escape}]", mode);
            AssertSingleMatch(regex, [(byte)'x', value, (byte)'y'], 1, 1, mode);
            Assert.IsFalse(regex.IsMatch([letter]), $"[{escape}] matched the letter in {mode}.");
            Assert.AreEqual(2, regex.Count([value, letter, value]), $"[{escape}] count in {mode}.");

            ByteRegex intersected = Compile($"[{escape}&&{escape}]", mode);
            AssertSingleMatch(intersected, [letter, value], 1, 1, mode);

            ByteRegex prefixed = Compile($"L[{escape}]", mode);
            AssertSingleMatch(prefixed, [(byte)'L', letter, (byte)'L', value], 2, 2, mode);
        }
    }

    /// <summary>
    /// Verifies a negated class excludes the escaped byte and still matches the escape letter.
    /// </summary>
    /// <param name="escape">The escape as written in the pattern.</param>
    /// <param name="value">The byte the escape names.</param>
    [TestMethod]
    [DataRow(@"\a", (byte)0x07)]
    [DataRow(@"\f", (byte)0x0C)]
    [DataRow(@"\t", (byte)0x09)]
    [DataRow(@"\n", (byte)0x0A)]
    [DataRow(@"\r", (byte)0x0D)]
    [DataRow(@"\v", (byte)0x0B)]
    public void NegatedEscapeClassExcludesOnlyItsByte(string escape, byte value)
    {
        ArgumentNullException.ThrowIfNull(escape);
        byte letter = (byte)escape[1];
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            ByteRegex regex = Compile($"[^{escape}]", mode);
            Assert.IsTrue(regex.IsMatch([letter]), $"[^{escape}] rejected the letter in {mode}.");
            Assert.IsFalse(regex.IsMatch([value]), $"[^{escape}] accepted its byte in {mode}.");
            AssertSingleMatch(regex, [value, value, letter], 2, 1, mode);

            ByteRegex repeated = Compile($"[^{escape}]+", mode);
            AssertSingleMatch(repeated, [value, letter, letter, value], 1, 2, mode);
        }
    }

    /// <summary>
    /// Verifies a repeated class consumes a run of the escaped byte.
    /// </summary>
    /// <param name="escape">The escape as written in the pattern.</param>
    /// <param name="value">The byte the escape names.</param>
    [TestMethod]
    [DataRow(@"\a", (byte)0x07)]
    [DataRow(@"\f", (byte)0x0C)]
    [DataRow(@"\t", (byte)0x09)]
    [DataRow(@"\n", (byte)0x0A)]
    [DataRow(@"\r", (byte)0x0D)]
    [DataRow(@"\v", (byte)0x0B)]
    public void RepeatedEscapeClassConsumesRuns(string escape, byte value)
    {
        ArgumentNullException.ThrowIfNull(escape);
        byte letter = (byte)escape[1];
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            ByteRegex regex = Compile($"[{escape}]+", mode);
            AssertSingleMatch(regex, [letter, value, value, value, letter], 1, 3, mode);
            Assert.AreEqual(2, regex.Count([value, letter, value, value]), $"[{escape}]+ count in {mode}.");
            Assert.IsFalse(regex.IsMatch([letter, letter]), $"[{escape}]+ matched letters in {mode}.");
        }
    }

    /// <summary>
    /// Verifies ranges whose endpoints are special escapes cover exactly the bytes between them.
    /// </summary>
    /// <param name="pattern">The class pattern.</param>
    /// <param name="first">The first byte inside the range.</param>
    /// <param name="last">The last byte inside the range.</param>
    [TestMethod]
    [DataRow(@"[\a-\v]", (byte)0x07, (byte)0x0B)]
    [DataRow(@"[\t-\r]", (byte)0x09, (byte)0x0D)]
    [DataRow(@"[\a-\f]", (byte)0x07, (byte)0x0C)]
    [DataRow(@"[\v-\f]", (byte)0x0B, (byte)0x0C)]
    [DataRow(@"[\a-\v&&[^\t]]", (byte)0x07, (byte)0x0B)]
    public void EscapedRangeCoversExactlyItsBytes(string pattern, byte first, byte last)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        bool excludesTab = pattern.Contains("[^\\t]", StringComparison.Ordinal);
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            ByteRegex regex = Compile(pattern, mode);
            ByteRegex negated = Compile(pattern.Insert(1, "^"), mode);
            for (int value = 0; value < 256; value++)
            {
                bool inside = value >= first && value <= last && !(excludesTab && value == 0x09);
                Assert.AreEqual(inside, regex.IsMatch([(byte)value]), $"{pattern} on 0x{value:X2} in {mode}.");
                if (value < 0x80)
                {
                    // A lone byte at 0x80 or above is not a Unicode scalar, so a negated class never
                    // matches it on its own; only ASCII bytes exercise the negation.
                    Assert.AreEqual(!inside, negated.IsMatch([(byte)value]), $"negated {pattern} on 0x{value:X2} in {mode}.");
                }
            }

            AssertSingleMatch(Compile(pattern + "+", mode), [(byte)'q', first, last, (byte)'q'], 1, excludesTab && first == 0x09 ? 1 : 2, mode);
        }
    }

    private static ByteRegex Compile(string pattern, ByteRegexEngineMode mode)
    {
        return ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = mode });
    }

    private static void AssertSingleMatch(ByteRegex regex, byte[] input, int start, int length, ByteRegexEngineMode mode)
    {
        Assert.IsTrue(regex.IsMatch(input), $"IsMatch in {mode}.");
        ByteRegexMatch? match = regex.Find(input);
        Assert.IsNotNull(match, $"Find in {mode}.");
        Assert.AreEqual(start, match.Value.Start, $"Find start in {mode}.");
        Assert.AreEqual(length, match.Value.Length, $"Find length in {mode}.");
        ByteRegexCaptures? captures = regex.FindCaptures(input);
        Assert.IsNotNull(captures, $"FindCaptures in {mode}.");
        Assert.AreEqual(start, captures.Match.Start, $"FindCaptures start in {mode}.");
        Assert.AreEqual(length, captures.Match.Length, $"FindCaptures length in {mode}.");
        Assert.AreEqual(1, regex.Count(input), $"Count in {mode}.");
    }
}
