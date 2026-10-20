namespace Scout;

/// <summary>
/// Verifies the repetition and extended mode group whitespace forms from issue 89 through the command-line pipeline.
/// </summary>
[TestClass]
public sealed class Issue89RepetitionWhitespaceDifferentialTests
{
    /// <summary>
    /// Verifies whitespace inside counted repetitions selects the same records as ripgrep.
    /// </summary>
    /// <param name="pattern">The pattern with whitespace around its counts.</param>
    [TestMethod]
    [DataRow("a{ 2 , 3 }")]
    [DataRow("a{ 2 }")]
    [DataRow("a{2 }")]
    [DataRow("a{2 ,}")]
    [DataRow("a{ 2,3 }")]
    [DataRow("a{ 2}")]
    [DataRow("a{2 , 3}")]
    [DataRow("(?x)a{2, }")]
    [DataRow("(?x)a{ 2 # two\n , 3 }")]
    public void RepetitionWhitespaceMatchesPinnedRipgrep(string pattern)
    {
        using var directory = RgTestDirectory.Create("issue-89-repetition-whitespace");
        directory.CreateFile("haystack.txt", "aaaa\naa\na\nbbb\naaa aa\n");
        string haystack = Path.Join(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-n", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--count-matches", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-o", "--column", pattern, haystack));
    }

    /// <summary>
    /// Verifies whitespace after an opening parenthesis selects the same records as ripgrep.
    /// </summary>
    /// <param name="pattern">The pattern with whitespace inside its group prefix.</param>
    [TestMethod]
    [DataRow("(?x)( ?:a)b")]
    [DataRow("(?x)( ?i:a)b")]
    [DataRow("(?x)( # note\n ?:a)b")]
    [DataRow("(?x)( ?:a)b")]
    [DataRow("(?x)a b")]
    [DataRow("( ?:a)")]
    public void GroupWhitespaceMatchesPinnedRipgrep(string pattern)
    {
        using var directory = RgTestDirectory.Create("issue-89-group-whitespace");
        directory.CreateFile("haystack.txt", "ab\nAb\n:a\n :a\na b\nxab\n");
        string haystack = Path.Join(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-n", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--count-matches", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-o", "--column", pattern, haystack));
    }

    /// <summary>
    /// Verifies a named capture that follows extended mode whitespace replaces the same way as ripgrep.
    /// </summary>
    [TestMethod]
    public void NamedCaptureAfterWhitespaceReplacesLikePinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("issue-89-named-capture");
        directory.CreateFile("haystack.txt", "ab\nxab\nb\n");
        string haystack = Path.Join(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-n", "-r", "[$name]", "(?x)( ?P<name>a)b", haystack));
    }
}
