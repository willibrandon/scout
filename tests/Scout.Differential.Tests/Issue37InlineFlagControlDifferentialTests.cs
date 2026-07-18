namespace Scout;

/// <summary>
/// Verifies the scoped and unscoped multiline forms from issue 37 through the command-line pipeline.
/// </summary>
public sealed class Issue37InlineFlagControlDifferentialTests
{
    /// <summary>
    /// Verifies a leading unscoped multiline flag and its scoped control select the same records as ripgrep.
    /// </summary>
    [Fact]
    public void ScopedAndUnscopedMultilineControlsMatchPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("issue-37-inline-multiline-control");
        directory.CreateFile(
            "haystack.txt",
            "Scout one\nnot Scout\nScout two\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");
        string[] patterns = ["(?m)^Scout.*$", "(?m:^Scout.*$)"];

        for (int index = 0; index < patterns.Length; index++)
        {
            DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
                "--count-matches",
                patterns[index],
                haystack));
            DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
                "-n",
                patterns[index],
                haystack));
        }
    }
}
