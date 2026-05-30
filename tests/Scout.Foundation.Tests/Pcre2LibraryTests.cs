namespace Scout;

/// <summary>
/// Verifies the current PCRE2 build metadata exposed by <see cref="Pcre2Library" />.
/// </summary>
public sealed class Pcre2LibraryTests
{
    /// <summary>
    /// Verifies the current PCRE2 runtime state matches the pinned non-PCRE2 reference build.
    /// </summary>
    [Fact]
    public void UnavailableRuntimeMatchesPinnedReference()
    {
        Assert.False(Pcre2Library.IsAvailable);
        Assert.Equal("PCRE2 is not available in this build of ripgrep", Pcre2Library.UnavailableErrorMessage);
        Assert.Equal("PCRE2 is not available in this build of ripgrep.\n"u8.ToArray(), Pcre2Library.UnavailableVersionOutput.ToArray());
    }
}
