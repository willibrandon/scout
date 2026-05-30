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
                "tests/multiline.rs",
                "overlap2",
                dir => dir.CreateFile("test", "xxx\nabc\ndefabc\ndefxxx\nxxx"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-U", "abc\ndef", "test")),
            new(
                "tests/multiline.rs",
                "dot_no_newline",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-U", "of this world.+detective work", "sherlock")),
            new(
                "tests/multiline.rs",
                "dot_all",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-U", "--multiline-dotall", "of this world.+detective work", "sherlock")),
            new(
                "tests/multiline.rs",
                "stdin",
                _ => { },
                DifferentialCase.ExactWithStandardInput(EncodingUtf8.GetBytes(Sherlock), "-n", "-U", @"of this world\p{Any}+?detective work")),
            new(
                "tests/feature.rs",
                "f7_stdin",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.ExactWithStandardInput(EncodingUtf8.GetBytes("Sherlock"), "--path-separator", "/", "-f-")),
            new(
                "tests/feature.rs",
                "f20_no_filename",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--no-filename", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "single_file",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "dir",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "line_numbers",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "columns",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--column", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "with_filename",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-H", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "inverted",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-v", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "inverted_line_numbers",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-v", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "case_insensitive",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-i", "sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "word",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-w", "as", "sherlock")),
            new(
                "tests/misc.rs",
                "line",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-x", "Watson|and exhibited clearly, with a label attached.", "sherlock")),
            new(
                "tests/misc.rs",
                "literal",
                dir => dir.CreateFile("file", "blib\n()\nblab\n"),
                DifferentialCase.Exact("--path-separator", "/", "-F", "()", "file")),
            new(
                "tests/misc.rs",
                "quiet",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-q", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "replace",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-r", "FooBar", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "count",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--count", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "count_matches",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--count-matches", "the", ".")),
            new(
                "tests/misc.rs",
                "files_with_matches",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--files-with-matches", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "files_without_match",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "foo");
                },
                DifferentialCase.Exact("--path-separator", "/", "--files-without-match", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "after_context",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-A", "1", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "after_context_line_numbers",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-A", "1", "-n", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "before_context",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-B", "1", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "before_context_line_numbers",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-B", "1", "-n", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "context",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-C", "1", "world|attached", "sherlock")),
            new(
                "tests/feature.rs",
                "f89_files_with_matches",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--null", "--files-with-matches", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f89_files_without_match",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "foo");
                },
                DifferentialCase.Exact("--path-separator", "/", "--null", "--files-without-match", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f89_count",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--null", "--count", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f89_files",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--null", "--files", ".")),
            new(
                "tests/feature.rs",
                "f89_match",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--null", "-C1", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f34_only_matching",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-o", "Sherlock", "sherlock")),
            new(
                "tests/feature.rs",
                "f34_only_matching_line_column",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-o", "--column", "-n", "Sherlock", "sherlock")),
            new(
                "tests/feature.rs",
                "f948_exit_code_no_match",
                dir => dir.CreateFile("sherlock", "Sherlock\nWatson\n"),
                DifferentialCase.Exact("--path-separator", "/", "Moriarty", ".")),
            new(
                "tests/feature.rs",
                "f948_exit_code_match",
                dir => dir.CreateFile("sherlock", "Sherlock\nWatson\n"),
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", ".")),
            new(
                "tests/regression.rs",
                "r105_part2",
                dir => dir.CreateFile("foo", "zztest"),
                DifferentialCase.Exact("--path-separator", "/", "--column", "test", ".")),
            new(
                "tests/regression.rs",
                "r105_part1",
                dir => dir.CreateFile("foo", "zztest"),
                DifferentialCase.Exact("--path-separator", "/", "--vimgrep", "test", "foo")),
            new(
                "tests/regression.rs",
                "r128",
                dir => dir.CreateFile("foo", "\n\n\n\nx"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "x", ".")),
            new(
                "tests/feature.rs",
                "f419_zero_as_shortcut_for_null",
                dir => dir.CreateFile("sherlock", "Sherlock\nSherlock\n"),
                DifferentialCase.Exact("--path-separator", "/", "-0", "-c", "Sherlock", "sherlock")),
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

    private const string Sherlock =
        "For the Doctor Watsons of this world, as opposed to the Sherlock\n" +
        "Holmeses, success in the province of detective work must always\n" +
        "be, to a very large extent, the result of luck. Sherlock Holmes\n" +
        "can extract a clew from a wisp of straw or a flake of cigar ash;\n" +
        "but Doctor Watson has to have it taken out for him and dusted,\n" +
        "and exhibited clearly, with a label attached.\n";
}
