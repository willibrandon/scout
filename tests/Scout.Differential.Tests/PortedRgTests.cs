namespace Scout;

internal static class PortedRgTests
{
    private static readonly PortedRgTestCase[] Cases =
        [
            new(
                "tests/regression.rs",
                "r64",
                dir =>
                {
                    dir.CreateFile("dir/abc", string.Empty);
                    dir.CreateFile("foo/abc", string.Empty);
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--files", "foo")),
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
                "only_matching",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-U", "--only-matching", @"Watson|Sherlock\p{Any}+?Holmes", "sherlock")),
            new(
                "tests/multiline.rs",
                "vimgrep",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-U", "--vimgrep", @"Watson|Sherlock\p{Any}+?Holmes", "sherlock")),
            new(
                "tests/multiline.rs",
                "stdin",
                _ => { },
                DifferentialCase.ExactWithStandardInput(EncodingUtf8.GetBytes(Sherlock), "-n", "-U", @"of this world\p{Any}+?detective work")),
            new(
                "tests/multiline.rs",
                "context",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-U", "-C1", @"detective work\p{Any}+?result of luck", "sherlock")),
            new(
                "tests/json.rs",
                "basic",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", "-B1", "Sherlock Holmes", "sherlock")),
            new(
                "tests/json.rs",
                "replacement",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", "-B1", "Sherlock Holmes", "-r", "John Watson", "sherlock")),
            new(
                "tests/json.rs",
                "quiet_stats",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", "--quiet", "--stats", "Sherlock Holmes", "sherlock")),
            new(
                "tests/json.rs",
                "crlf",
                dir => dir.CreateFile("sherlock", SherlockCrlf),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", "--crlf", @"Sherlock$", "sherlock")),
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
                "word_period",
                dir => dir.CreateFile("haystack", "..."),
                DifferentialCase.Exact("--path-separator", "/", "-ow", ".", "haystack")),
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
                "replace_groups",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-r", "$2, $1", "([A-Z][a-z]+) ([A-Z][a-z]+)", "sherlock")),
            new(
                "tests/misc.rs",
                "replace_named_groups",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-r", "$last, $first", "(?P<first>[A-Z][a-z]+) (?P<last>[A-Z][a-z]+)", "sherlock")),
            new(
                "tests/misc.rs",
                "replace_with_only_matching",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-o", "-r", "$1", @"of (\w+)", "sherlock")),
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
                "count_matches_inverted",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--count-matches", "--invert-match", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "count_matches_via_only",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--count", "--only-matching", "the", ".")),
            new(
                "tests/misc.rs",
                "include_zero",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--count", "--include-zero", "nada", ".")),
            new(
                "tests/misc.rs",
                "include_zero_override",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--count", "--include-zero", "--no-include-zero", "nada", ".")),
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
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--files-without-match", "Sherlock", ".")),
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
                "tests/misc.rs",
                "context_line_numbers",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-C", "1", "-n", "world|attached", "sherlock")),
            new(
                "tests/misc.rs",
                "byte_offset_only_matching",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-b", "-o", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "max_filesize_parse_no_suffix",
                dir =>
                {
                    dir.CreateSize("foo", 40);
                    dir.CreateSize("bar", 60);
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--max-filesize", "50", "--files", ".")),
            new(
                "tests/misc.rs",
                "max_filesize_parse_k_suffix",
                dir =>
                {
                    dir.CreateSize("foo", 3048);
                    dir.CreateSize("bar", 4100);
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--max-filesize", "4K", "--files", ".")),
            new(
                "tests/misc.rs",
                "max_filesize_parse_m_suffix",
                dir =>
                {
                    dir.CreateSize("foo", 1_000_000);
                    dir.CreateSize("bar", 1_400_000);
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--max-filesize", "1M", "--files", ".")),
            new(
                "tests/misc.rs",
                "ignore_hidden",
                dir => dir.CreateFile(".sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "no_ignore_hidden",
                dir => dir.CreateFile(".sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--hidden", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "ignore_git",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "sherlock\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "ignore_generic",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile(".ignore", "sherlock\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "ignore_ripgrep",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile(".rgignore", "sherlock\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "no_ignore",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile(".gitignore", "sherlock\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "--no-ignore", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "vimgrep",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--vimgrep", "Sherlock|Watson", ".")),
            new(
                "tests/misc.rs",
                "vimgrep_no_line",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--vimgrep", "-N", "Sherlock|Watson", ".")),
            new(
                "tests/misc.rs",
                "vimgrep_no_line_no_column",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--vimgrep", "-N", "--no-column", "Sherlock|Watson", ".")),
            new(
                "tests/misc.rs",
                "files",
                dir =>
                {
                    dir.CreateFile("file", string.Empty);
                    dir.CreateDirectory("dir");
                    dir.CreateFile("dir/file", string.Empty);
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--files", ".")),
            new(
                "tests/misc.rs",
                "sort_files",
                dir =>
                {
                    dir.CreateFile("a", "test");
                    dir.CreateDirectory("dir");
                    dir.CreateFile("dir/c", "test");
                    dir.CreateFile("b", "test");
                    dir.CreateFile("dir/d", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "test", ".")),
            new(
                "tests/feature.rs",
                "f109_max_depth",
                dir =>
                {
                    dir.CreateDirectory("one");
                    dir.CreateFile("one/pass", "far");
                    dir.CreateDirectory("one/too");
                    dir.CreateFile("one/too/many", "far");
                },
                DifferentialCase.Exact("--path-separator", "/", "--maxdepth", "2", "far", ".")),
            new(
                "tests/feature.rs",
                "f109_case_sensitive_part1",
                dir => dir.CreateFile("foo", "tEsT"),
                DifferentialCase.Exact("--path-separator", "/", "--smart-case", "--case-sensitive", "test", ".")),
            new(
                "tests/feature.rs",
                "f109_case_sensitive_part2",
                dir => dir.CreateFile("foo", "tEsT"),
                DifferentialCase.Exact("--path-separator", "/", "--ignore-case", "--case-sensitive", "test", ".")),
            new(
                "tests/feature.rs",
                "f129_matches",
                dir => dir.CreateFile("foo", "test\ntest abcdefghijklmnopqrstuvwxyz test"),
                DifferentialCase.Exact("--path-separator", "/", "-M26", "test", ".")),
            new(
                "tests/feature.rs",
                "f129_context",
                dir => dir.CreateFile("foo", "test\nabcdefghijklmnopqrstuvwxyz"),
                DifferentialCase.Exact("--path-separator", "/", "-M20", "-C1", "test", ".")),
            new(
                "tests/feature.rs",
                "f129_replace",
                dir => dir.CreateFile("foo", "test\ntest abcdefghijklmnopqrstuvwxyz test"),
                DifferentialCase.Exact("--path-separator", "/", "-M26", "-rfoo", "test", ".")),
            new(
                "tests/feature.rs",
                "f159_max_count",
                dir => dir.CreateFile("foo", "test\ntest"),
                DifferentialCase.Exact("--path-separator", "/", "-m1", "test", ".")),
            new(
                "tests/feature.rs",
                "f159_max_count_zero",
                dir => dir.CreateFile("foo", "test\ntest"),
                DifferentialCase.Exact("--path-separator", "/", "-m0", "test", ".")),
            new(
                "tests/feature.rs",
                "f243_column_line",
                dir => dir.CreateFile("foo", "test"),
                DifferentialCase.Exact("--path-separator", "/", "--column", "test", ".")),
            new(
                "tests/feature.rs",
                "f263_sort_files",
                dir =>
                {
                    dir.CreateFile("foo", "test");
                    dir.CreateFile("abc", "test");
                    dir.CreateFile("zoo", "test");
                    dir.CreateFile("bar", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--sort-files", "test", ".")),
            new(
                "tests/feature.rs",
                "f263_sort_files_reverse",
                dir =>
                {
                    dir.CreateFile("foo", "test");
                    dir.CreateFile("abc", "test");
                    dir.CreateFile("zoo", "test");
                    dir.CreateFile("bar", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--sortr=path", "test", ".")),
            new(
                "tests/feature.rs",
                "f275_pathsep",
                dir =>
                {
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/bar", "test");
                },
                DifferentialCase.Exact("--path-separator", "Z", "test", ".")),
            new(
                "tests/feature.rs",
                "f411_single_threaded_search_stats",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "-j1", "--stats", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f411_parallel_search_stats",
                dir =>
                {
                    dir.CreateFile("sherlock_1", Sherlock);
                    dir.CreateFile("sherlock_2", Sherlock);
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "--path-separator", "/", "-j2", "--stats", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f411_single_threaded_quiet_search_stats",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--quiet", "-j1", "--stats", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f411_parallel_quiet_search_stats",
                dir =>
                {
                    dir.CreateFile("sherlock_1", Sherlock);
                    dir.CreateFile("sherlock_2", Sherlock);
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "--path-separator", "/", "-j2", "--quiet", "--stats", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f416_crlf",
                dir => dir.CreateFile("sherlock", SherlockCrlf),
                DifferentialCase.Exact("--path-separator", "/", "--crlf", @"Sherlock$", "sherlock")),
            new(
                "tests/feature.rs",
                "f416_crlf_multiline",
                dir => dir.CreateFile("sherlock", SherlockCrlf),
                DifferentialCase.Exact("--path-separator", "/", "--crlf", "-U", @"Sherlock$", "sherlock")),
            new(
                "tests/feature.rs",
                "f416_crlf_only_matching",
                dir => dir.CreateFile("sherlock", SherlockCrlf),
                DifferentialCase.Exact("--path-separator", "/", "--crlf", "-o", @"Sherlock$", "sherlock")),
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
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--null", "--files-without-match", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f89_count",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--null", "--count", "Sherlock", ".")),
            new(
                "tests/feature.rs",
                "f89_files",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--null", "--files", ".")),
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
                "f740_passthru_single",
                dir =>
                {
                    dir.CreateFile("file", "\nfoo\nbar\nfoobar\n\nbaz\n");
                    dir.CreateFile("patterns", "foo\nbar\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "foo", "file")),
            new(
                "tests/feature.rs",
                "f740_passthru_multiple_e",
                dir =>
                {
                    dir.CreateFile("file", "\nfoo\nbar\nfoobar\n\nbaz\n");
                    dir.CreateFile("patterns", "foo\nbar\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-e", "foo", "-e", "bar", "file")),
            new(
                "tests/feature.rs",
                "f740_passthru_file_patterns",
                dir =>
                {
                    dir.CreateFile("file", "\nfoo\nbar\nfoobar\n\nbaz\n");
                    dir.CreateFile("patterns", "foo\nbar\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-f", "patterns", "file")),
            new(
                "tests/feature.rs",
                "f740_passthru_count_override",
                dir =>
                {
                    dir.CreateFile("file", "\nfoo\nbar\nfoobar\n\nbaz\n");
                    dir.CreateFile("patterns", "foo\nbar\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-c", "foo", "file")),
            new(
                "tests/feature.rs",
                "f740_passthru_only_matching",
                dir =>
                {
                    dir.CreateFile("file", "\nfoo\nbar\nfoobar\n\nbaz\n");
                    dir.CreateFile("patterns", "foo\nbar\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-o", "foo", "file")),
            new(
                "tests/feature.rs",
                "f740_passthru_replace",
                dir =>
                {
                    dir.CreateFile("file", "\nfoo\nbar\nfoobar\n\nbaz\n");
                    dir.CreateFile("patterns", "foo\nbar\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-r", "wat", "foo", "file")),
            new(
                "tests/feature.rs",
                "f917_trim",
                dir => dir.CreateFile("sherlock", TrimmedSherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-B1", "-A2", "--trim", "Holmeses", "sherlock")),
            new(
                "tests/feature.rs",
                "f917_trim_match",
                dir => dir.CreateFile("sherlock", TrimmedSherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-B1", "-A2", "--trim", @"\s+Holmeses", "sherlock")),
            new(
                "tests/feature.rs",
                "f917_trim_max_columns_normal",
                dir => dir.CreateFile("haystack", "     0123456789abcdefghijklmnopqrstuvwxyz"),
                DifferentialCase.Exact("--path-separator", "/", "--trim", "--max-columns-preview", "-M8", "--no-filename", "abc", ".")),
            new(
                "tests/feature.rs",
                "f993_null_data",
                dir => dir.CreateBytes("test", "foo\0bar\0\0\0baz\0"u8.ToArray()),
                DifferentialCase.Exact("--path-separator", "/", "--null-data", @".+", "test")),
            new(
                "tests/feature.rs",
                "f1078_max_columns_preview1",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-M46", "--max-columns-preview", "exhibited|dusted|has to have it", ".")),
            new(
                "tests/feature.rs",
                "f1078_max_columns_preview2",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-M43", "--max-columns-preview", "-rxxx", "exhibited|dusted|has to have it", ".")),
            new(
                "tests/feature.rs",
                "f2288_context_partial_override",
                dir => dir.CreateFile("test", "1\n2\n3\n4\n5\n6\n7\n8\n9\n"),
                DifferentialCase.Exact("--path-separator", "/", "-C1", "-A2", "5", "test")),
            new(
                "tests/feature.rs",
                "f2288_context_partial_override_rev",
                dir => dir.CreateFile("test", "1\n2\n3\n4\n5\n6\n7\n8\n9\n"),
                DifferentialCase.Exact("--path-separator", "/", "-A2", "-C1", "5", "test")),
            new(
                "tests/feature.rs",
                "no_context_sep",
                dir => dir.CreateFile("test", "foo\nctx\nbar\nctx\nfoo\nctx"),
                DifferentialCase.Exact("--path-separator", "/", "-A1", "--no-context-separator", "foo", "test")),
            new(
                "tests/feature.rs",
                "no_context_sep_overrides",
                dir => dir.CreateFile("test", "foo\nctx\nbar\nctx\nfoo\nctx"),
                DifferentialCase.Exact("--path-separator", "/", "-A1", "--context-separator", "AAA", "--no-context-separator", "foo", "test")),
            new(
                "tests/feature.rs",
                "no_context_sep_overridden",
                dir => dir.CreateFile("test", "foo\nctx\nbar\nctx\nfoo\nctx"),
                DifferentialCase.Exact("--path-separator", "/", "-A1", "--no-context-separator", "--context-separator", "AAA", "foo", "test")),
            new(
                "tests/feature.rs",
                "context_sep",
                dir => dir.CreateFile("test", "foo\nctx\nbar\nctx\nfoo\nctx"),
                DifferentialCase.Exact("--path-separator", "/", "-A1", "--context-separator", "AAA", "foo", "test")),
            new(
                "tests/feature.rs",
                "context_sep_default",
                dir => dir.CreateFile("test", "foo\nctx\nbar\nctx\nfoo\nctx"),
                DifferentialCase.Exact("--path-separator", "/", "-A1", "foo", "test")),
            new(
                "tests/feature.rs",
                "context_sep_empty",
                dir => dir.CreateFile("test", "foo\nctx\nbar\nctx\nfoo\nctx"),
                DifferentialCase.Exact("--path-separator", "/", "-A1", "--context-separator", string.Empty, "foo", "test")),
            new(
                "tests/feature.rs",
                "f411_search_stats",
                dir => dir.CreateFile("sherlock", "needle\nmiss\nneedle\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "-j1", "--stats", "needle", ".")),
        ];

    internal static void Run(string sourceFile, string name)
    {
        PortedRgTestCase testCase = Find(sourceFile, name);
        using var directory = RgTestDirectory.Create(testCase.SourceFile.Replace('/', '-') + "-" + testCase.Name);
        testCase.Arrange(directory);
        DifferentialRunner.AssertMatchesPinned(testCase.Command, directory.RootPath);
    }

    internal static void AssertCatalog((string SourceFile, string Name)[] expected)
    {
        Assert.Equal(expected.Length, Cases.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.NotNull(TryFind(expected[index].SourceFile, expected[index].Name));
        }

        for (int index = 0; index < Cases.Length; index++)
        {
            bool found = false;
            for (int expectedIndex = 0; expectedIndex < expected.Length; expectedIndex++)
            {
                if (IsCase(Cases[index], expected[expectedIndex].SourceFile, expected[expectedIndex].Name))
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, Cases[index].SourceFile + "::" + Cases[index].Name + " is missing from the generated test catalog.");
        }
    }

    private static PortedRgTestCase Find(string sourceFile, string name)
    {
        PortedRgTestCase? testCase = TryFind(sourceFile, name);
        if (testCase is null)
        {
            Assert.Fail("Ported ripgrep test case was not found: " + sourceFile + "::" + name);
        }

        return testCase;
    }

    private static PortedRgTestCase? TryFind(string sourceFile, string name)
    {
        for (int index = 0; index < Cases.Length; index++)
        {
            if (IsCase(Cases[index], sourceFile, name))
            {
                return Cases[index];
            }
        }

        return null;
    }

    private static bool IsCase(PortedRgTestCase testCase, string sourceFile, string name)
    {
        return string.Equals(testCase.SourceFile, sourceFile, StringComparison.Ordinal) &&
            string.Equals(testCase.Name, name, StringComparison.Ordinal);
    }

    private const string Sherlock =
        "For the Doctor Watsons of this world, as opposed to the Sherlock\n" +
        "Holmeses, success in the province of detective work must always\n" +
        "be, to a very large extent, the result of luck. Sherlock Holmes\n" +
        "can extract a clew from a wisp of straw or a flake of cigar ash;\n" +
        "but Doctor Watson has to have it taken out for him and dusted,\n" +
        "and exhibited clearly, with a label attached.\n";

    private const string SherlockCrlf =
        "For the Doctor Watsons of this world, as opposed to the Sherlock\r\n" +
        "Holmeses, success in the province of detective work must always\r\n" +
        "be, to a very large extent, the result of luck. Sherlock Holmes\r\n" +
        "can extract a clew from a wisp of straw or a flake of cigar ash;\r\n" +
        "but Doctor Watson has to have it taken out for him and dusted,\r\n" +
        "and exhibited clearly, with a label attached.\r\n";

    private const string TrimmedSherlock =
        "zzz\n" +
        "    For the Doctor Watsons of this world, as opposed to the Sherlock\n" +
        "  Holmeses, success in the province of detective work must always\n" +
        "\tbe, to a very large extent, the result of luck. Sherlock Holmes\n" +
        "     can extract a clew from a wisp of straw or a flake of cigar ash;\n" +
        "but Doctor Watson has to have it taken out for him and dusted,\n" +
        " and exhibited clearly, with a label attached.\n";
}
