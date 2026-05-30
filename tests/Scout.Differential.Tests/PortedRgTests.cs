namespace Scout;

/// <summary>
/// Ports upstream ripgrep <c>tests/*.rs</c> cases into the Scout differential harness.
/// </summary>
public sealed class PortedRgTests
{
    /// <summary>
    /// Verifies an initial set of ported upstream <c>rgtest!</c> cases against pinned ripgrep.
    /// </summary>
    [Fact]
    public void PortedRgtestCasesMatchPinnedRipgrep()
    {
        PortedRgTestCase[] cases =
        [
            new(
                "tests/regression.rs",
                "r64",
                dir =>
                {
                    dir.CreateFile("dir/abc", string.Empty);
                    dir.CreateFile("foo/abc", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--files", "foo")),
            new(
                "tests/regression.rs",
                "r93",
                dir => dir.CreateFile("foo", "192.168.1.1"),
                DifferentialCase.Exact("--path-separator", "/", @"(\d{1,3}\.){3}\d{1,3}", ".")),
            new(
                "tests/multiline.rs",
                "overlap1",
                dir => dir.CreateFile("test", "xxx\nabc\ndefxxxabc\ndefxxx\nxxx"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-U", "abc\ndef", ".")),
            new(
                "tests/feature.rs",
                "f948_exit_code_no_match",
                dir => dir.CreateFile("sherlock", "Sherlock\nWatson\n"),
                DifferentialCase.Exact("--path-separator", "/", "Moriarty", ".")),
            new(
                "tests/feature.rs",
                "f411_search_stats",
                dir => dir.CreateFile("sherlock", "needle\nmiss\nneedle\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "-j1", "--stats", "needle", ".")),
        ];

        for (int index = 0; index < cases.Length; index++)
        {
            PortedRgTestCase testCase = cases[index];
            using var directory = RgTestDirectory.Create(testCase.SourceFile.Replace('/', '-') + "-" + testCase.Name);
            testCase.Arrange(directory);
            DifferentialRunner.AssertMatchesPinned(testCase.Command, directory.RootPath);
        }
    }
}
