using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies whitespace inside counted repetitions and after an opening parenthesis parses the way regex-syntax parses it.
/// </summary>
[TestClass]
public sealed class RegexRepetitionWhitespaceTests
{
    /// <summary>
    /// Verifies whitespace regex-syntax accepts around repetition counts and group prefixes is accepted here.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="input">The text to search.</param>
    /// <param name="start">The expected match start.</param>
    /// <param name="length">The expected match length.</param>
    [TestMethod]
    [DataRow("a{ 2 , 3 }", "aaaa", 0, 3)]
    [DataRow("a{ 2 }", "aaaa", 0, 2)]
    [DataRow("a{2 }", "aaaa", 0, 2)]
    [DataRow("a{2 ,}", "aaaa", 0, 4)]
    [DataRow("a{ 2,3 }", "aaaa", 0, 3)]
    [DataRow("a{ 2}", "aaaa", 0, 2)]
    [DataRow("a{2 , 3}", "aaaa", 0, 3)]
    [DataRow("(?x)a{2, }", "aaaa", 0, 4)]
    [DataRow("(?x)a{ 2 # two\n , 3 }", "aaaa", 0, 3)]
    [DataRow("(?x)( ?:a)b", "xab", 1, 2)]
    [DataRow("(?x)( ?i:a)b", "xAb", 1, 2)]
    [DataRow("(?x)( # note\n ?:a)b", "xab", 1, 2)]
    [DataRow("(?x)( ?:a)b", "xab", 1, 2)]
    [DataRow("(?x)a b", "xab", 1, 2)]
    [DataRow("( ?:a)", " :a", 0, 3)]
    [DataRow("( ?:a)", ":a", 0, 2)]
    public void AllowedWhitespaceParses(string pattern, string input, int start, int length)
    {
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(input);
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = mode });
            ByteRegexMatch? match = regex.Find(haystack);
            Assert.IsNotNull(match, $"{pattern} in {mode}.");
            Assert.AreEqual(start, match.Value.Start, $"{pattern} start in {mode}.");
            Assert.AreEqual(length, match.Value.Length, $"{pattern} length in {mode}.");
        }
    }

    /// <summary>
    /// Verifies a named capture still binds when whitespace separates it from the opening parenthesis.
    /// </summary>
    [TestMethod]
    public void NamedCaptureAfterExtendedWhitespaceBinds()
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile("(?x)( ?P<name>a)b", new ByteRegexOptions { EngineMode = mode });
            ByteRegexCaptures? captures = regex.FindCaptures("xab"u8);
            Assert.IsNotNull(captures, $"captures in {mode}.");
            ByteRegexMatch? group = captures.GetGroup(1);
            Assert.IsNotNull(group, $"group in {mode}.");
            Assert.AreEqual(1, group.Value.Start, $"group start in {mode}.");
            Assert.AreEqual(1, group.Value.Length, $"group length in {mode}.");
        }
    }

    /// <summary>
    /// Verifies whitespace regex-syntax rejects is still rejected with the matching message.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="message">The expected error text.</param>
    [TestMethod]
    [DataRow("a{2, }", "repetition quantifier expects a valid decimal")]
    [DataRow("a{ }", "repetition quantifier expects a valid decimal")]
    [DataRow("a{2,x}", "repetition quantifier expects a valid decimal")]
    [DataRow("a{ ,3}", "repetition quantifier expects a valid decimal")]
    [DataRow("a{1 2}", "unclosed repetition quantifier")]
    [DataRow("a{2 ", "unclosed repetition quantifier")]
    [DataRow("(?x)(? :a)", "unrecognized inline flag")]
    [DataRow("(?x)( ? :a)", "unrecognized inline flag")]
    [DataRow("(?x)( ?P <n>a)", "unrecognized inline flag")]
    public void RejectedWhitespaceFails(string pattern, string message)
    {
        ByteRegexParseException exception = Assert.ThrowsExactly<ByteRegexParseException>(() => ByteRegex.Compile(pattern));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }
}
