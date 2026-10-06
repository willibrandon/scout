namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible human-readable byte size parsing.
/// </summary>
[TestClass]
public sealed class CliHumanSizeParserTests
{
    /// <summary>
    /// Verifies bare numbers and uppercase binary suffixes parse to byte counts.
    /// </summary>
    /// <param name="text">The size text.</param>
    /// <param name="expected">The expected byte count.</param>
    [TestMethod]
    [DataRow("123", 123UL)]
    [DataRow("123K", 123UL * 1024UL)]
    [DataRow("123M", 123UL * 1024UL * 1024UL)]
    [DataRow("123G", 123UL * 1024UL * 1024UL * 1024UL)]
    public void TryParseAcceptsRipgrepSizeForms(string text, ulong expected)
    {
        bool parsed = CliHumanSizeParser.TryParse(text, out ulong size, out string error);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expected, size);
        Assert.IsEmpty(error);
    }

    /// <summary>
    /// Verifies invalid size formats match upstream diagnostics.
    /// </summary>
    /// <param name="text">The invalid size text.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("a")]
    [DataRow("123T")]
    [DataRow("123KB")]
    [DataRow("1k")]
    public void TryParseRejectsInvalidFormats(string text)
    {
        bool parsed = CliHumanSizeParser.TryParse(text, out ulong size, out string error);

        Assert.IsFalse(parsed);
        Assert.AreEqual(0UL, size);
        Assert.AreEqual(
            $"invalid format for size '{text}', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix",
            error);
    }

    /// <summary>
    /// Verifies integer overflow before suffix multiplication matches upstream diagnostics.
    /// </summary>
    [TestMethod]
    public void TryParseRejectsIntegerOverflow()
    {
        bool parsed = CliHumanSizeParser.TryParse("18446744073709551616", out ulong size, out string error);

        Assert.IsFalse(parsed);
        Assert.AreEqual(0UL, size);
        Assert.AreEqual("invalid integer found in size '18446744073709551616': number too large to fit in target type", error);
    }

    /// <summary>
    /// Verifies suffix multiplication overflow matches upstream diagnostics.
    /// </summary>
    [TestMethod]
    public void TryParseRejectsSuffixOverflow()
    {
        bool parsed = CliHumanSizeParser.TryParse("9999999999999999G", out ulong size, out string error);

        Assert.IsFalse(parsed);
        Assert.AreEqual(0UL, size);
        Assert.AreEqual("size too big in '9999999999999999G'", error);
    }
}
