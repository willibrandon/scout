namespace Scout;

/// <summary>
/// Verifies the current PCRE2 build metadata exposed by <see cref="Pcre2Library" />.
/// </summary>
[TestClass]
public sealed class Pcre2LibraryTests
{
    /// <summary>
    /// Verifies managed PCRE2 spans retain pre-<c>\K</c> starts and reject unsupported reversed spans.
    /// </summary>
    [TestMethod]
    public void MatchConstructionHandlesResetStartsWithoutUnsignedUnderflow()
    {
        Pcre2Match match = Pcre2Regex.CreateMatch(
            start: 3,
            end: 6,
            patternStart: 0);

        Assert.AreEqual(3, match.Start);
        Assert.AreEqual(3, match.Length);
        Assert.AreEqual(0, match.PatternStart);
        Pcre2Exception exception = Assert.ThrowsExactly<Pcre2Exception>(
            () => Pcre2Regex.CreateMatch(start: 2, end: 1, patternStart: 0));
        Assert.Contains("start after its exclusive end", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the current PCRE2 runtime state matches the pinned non-PCRE2 reference build.
    /// </summary>
    [TestMethod]
    public void UnavailableRuntimeMatchesPinnedReference()
    {
        Assert.IsFalse(Pcre2Library.IsAvailable);
        Assert.IsFalse(Pcre2Library.IsJitAvailable);
        Assert.AreEqual("-pcre2", Pcre2Library.FeatureLabel);
        Assert.AreEqual("PCRE2 is not available in this build of scout.\n", Pcre2Library.VersionText);
        Assert.AreSequenceEqual("PCRE2 is not available in this build of scout.\n"u8.ToArray(), Pcre2Library.UnavailableVersionOutput.ToArray());
        Assert.AreSequenceEqual("PCRE2 is not available in this build of scout.\n"u8.ToArray(), Pcre2Library.GetVersionOutput());
    }
}
