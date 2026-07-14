namespace Scout;

/// <summary>
/// Verifies recursive standard-search target planning.
/// </summary>
public sealed class StandardSearchTargetOperationsTests
{
    /// <summary>
    /// Verifies regex candidate prechecks do not require directory metadata to have been resolved eagerly.
    /// </summary>
    [Fact]
    public void RegexCandidatePrecheckAcceptsLazyDirectoryEntryMetadata()
    {
        byte[][] pattern = ["foo.*bar"u8.ToArray()];
        RegexSearchPlan? regexPlan = LiteralLineSearcher.CreateRegexSearchPlan(
            pattern,
            asciiCaseInsensitive: false,
            compileAutomata: true);
        var entry = new DirEntry(
            "lazy-entry",
            depth: 1,
            attributes: default,
            isDirectory: false,
            isSymbolicLink: false,
            isStdin: false,
            length: null,
            identity: default,
            deferMetadata: true);

        Assert.Null(entry.KnownLength);
        Assert.True(StandardSearchTargetOperations.CanUseDirectoryEntryRegexCandidatePrecheck(
            entry,
            pattern,
            new CliLowArgs(),
            regexPlan,
            out RegexCandidateLineAccelerator? accelerator));
        Assert.NotNull(accelerator);
    }
}
