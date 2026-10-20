namespace Scout;

/// <summary>
/// Verifies bell and vertical tab escapes inside character classes from issue 88 through the command-line pipeline.
/// </summary>
[TestClass]
public sealed class Issue88ClassEscapeDifferentialTests
{
    /// <summary>
    /// Verifies every class shape that reaches a dedicated fast path selects the same records as ripgrep.
    /// </summary>
    /// <param name="pattern">The pattern whose character class names a special escape.</param>
    [TestMethod]
    [DataRow(@"[\a]")]
    [DataRow(@"[\a]+")]
    [DataRow(@"[\v]")]
    [DataRow(@"[\v]+")]
    [DataRow(@"[\f]+")]
    [DataRow(@"[\t]+")]
    [DataRow(@"[^\a]")]
    [DataRow(@"[^\v]+")]
    [DataRow(@"[\a-\v]+")]
    [DataRow(@"[\a\v]+")]
    [DataRow(@"L[\a]")]
    [DataRow(@"[\a&&\a]")]
    [DataRow(@"[\a-\v&&[^\t]]+")]
    [DataRow(@"x[\v]")]
    public void ClassEscapesMatchPinnedRipgrep(string pattern)
    {
        using var directory = RgTestDirectory.Create("issue-88-class-escapes");
        directory.CreateFile(
            "haystack.txt",
            "bell\u0007here\nvtab\u000bhere\nformfeed\u000chere\ntab\there\nplain line\nLetter L\u0007\nletters a v f t\n");
        string haystack = Path.Join(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-n", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--count-matches", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-o", "--column", pattern, haystack));
    }
}
