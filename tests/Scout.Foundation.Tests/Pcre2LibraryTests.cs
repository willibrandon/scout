namespace Scout;

/// <summary>
/// Verifies the current PCRE2 build metadata exposed by <see cref="Pcre2Library" />.
/// </summary>
public sealed class Pcre2LibraryTests
{
    /// <summary>
    /// Verifies managed PCRE2 spans retain pre-<c>\K</c> starts and reject unsupported reversed spans.
    /// </summary>
    [Fact]
    public void MatchConstructionHandlesResetStartsWithoutUnsignedUnderflow()
    {
        Pcre2Match match = Pcre2Regex.CreateMatch(
            start: 3,
            end: 6,
            patternStart: 0);

        Assert.Equal(3, match.Start);
        Assert.Equal(3, match.Length);
        Assert.Equal(0, match.PatternStart);
        Pcre2Exception exception = Assert.Throws<Pcre2Exception>(
            () => Pcre2Regex.CreateMatch(start: 2, end: 1, patternStart: 0));
        Assert.Contains("start after its exclusive end", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the current PCRE2 runtime state matches the pinned non-PCRE2 reference build.
    /// </summary>
    [Fact]
    public void UnavailableRuntimeMatchesPinnedReference()
    {
        Assert.False(Pcre2Library.IsAvailable);
        Assert.False(Pcre2Library.IsJitAvailable);
        Assert.Equal("-pcre2", Pcre2Library.FeatureLabel);
        Assert.Equal("PCRE2 is not available in this build of scout.\n", Pcre2Library.VersionText);
        Assert.Equal("PCRE2 is not available in this build of scout", Pcre2Library.UnavailableErrorMessage);
        Assert.Equal("PCRE2 is not available in this build of scout.\n"u8.ToArray(), Pcre2Library.UnavailableVersionOutput.ToArray());
        Assert.Equal("PCRE2 is not available in this build of scout.\n"u8.ToArray(), Pcre2Library.GetVersionOutput());
    }
}
