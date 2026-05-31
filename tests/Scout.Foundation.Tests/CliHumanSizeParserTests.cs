namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible human-readable byte size parsing.
/// </summary>
public sealed class CliHumanSizeParserTests
{
    /// <summary>
    /// Verifies bare numbers and uppercase binary suffixes parse to byte counts.
    /// </summary>
    /// <param name="text">The size text.</param>
    /// <param name="expected">The expected byte count.</param>
    [Theory]
    [InlineData("123", 123UL)]
    [InlineData("123K", 123UL * 1024UL)]
    [InlineData("123M", 123UL * 1024UL * 1024UL)]
    [InlineData("123G", 123UL * 1024UL * 1024UL * 1024UL)]
    public void TryParseAcceptsRipgrepSizeForms(string text, ulong expected)
    {
        bool parsed = CliHumanSizeParser.TryParse(text, out ulong size, out string error);

        Assert.True(parsed);
        Assert.Equal(expected, size);
        Assert.Empty(error);
    }

    /// <summary>
    /// Verifies invalid size formats match upstream diagnostics.
    /// </summary>
    /// <param name="text">The invalid size text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("123T")]
    [InlineData("123KB")]
    [InlineData("1k")]
    public void TryParseRejectsInvalidFormats(string text)
    {
        bool parsed = CliHumanSizeParser.TryParse(text, out ulong size, out string error);

        Assert.False(parsed);
        Assert.Equal(0UL, size);
        Assert.Equal(
            $"invalid format for size '{text}', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix",
            error);
    }

    /// <summary>
    /// Verifies integer overflow before suffix multiplication matches upstream diagnostics.
    /// </summary>
    [Fact]
    public void TryParseRejectsIntegerOverflow()
    {
        bool parsed = CliHumanSizeParser.TryParse("18446744073709551616", out ulong size, out string error);

        Assert.False(parsed);
        Assert.Equal(0UL, size);
        Assert.Equal("invalid integer found in size '18446744073709551616': number too large to fit in target type", error);
    }

    /// <summary>
    /// Verifies suffix multiplication overflow matches upstream diagnostics.
    /// </summary>
    [Fact]
    public void TryParseRejectsSuffixOverflow()
    {
        bool parsed = CliHumanSizeParser.TryParse("9999999999999999G", out ulong size, out string error);

        Assert.False(parsed);
        Assert.Equal(0UL, size);
        Assert.Equal("size too big in '9999999999999999G'", error);
    }
}
