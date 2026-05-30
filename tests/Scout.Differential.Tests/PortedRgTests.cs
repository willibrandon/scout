using System;
using System.IO;
using System.Text;

namespace Scout;

internal static class PortedRgTests
{
    private static readonly PortedRgTestCase[] Cases =
        [
            new(
                "tests/regression.rs",
                "r16",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "ghi/");
                    dir.CreateDirectory("ghi");
                    dir.CreateDirectory("def/ghi");
                    dir.CreateFile("ghi/toplevel.txt", "xyz");
                    dir.CreateFile("def/ghi/subdir.txt", "xyz");
                },
                DifferentialCase.Exact("--path-separator", "/", "xyz", ".")),
            new(
                "tests/regression.rs",
                "r25",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "/llvm/");
                    dir.CreateDirectory("src/llvm");
                    dir.CreateFile("src/llvm/foo", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", "."),
                DifferentialCase.ExactInDirectory("src", "--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r30",
                dir =>
                {
                    dir.CreateFile(".gitignore", "vendor/**\n!vendor/manifest");
                    dir.CreateDirectory("vendor");
                    dir.CreateFile("vendor/manifest", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r49",
                dir =>
                {
                    dir.CreateFile(".gitignore", "foo/bar");
                    dir.CreateDirectory("test/foo/bar");
                    dir.CreateFile("test/foo/bar/baz", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "xyz", ".")),
            new(
                "tests/regression.rs",
                "r50",
                dir =>
                {
                    dir.CreateFile(".gitignore", "XXX/YYY/");
                    dir.CreateDirectory("abc/def/XXX/YYY");
                    dir.CreateDirectory("ghi/XXX/YYY");
                    dir.CreateFile("abc/def/XXX/YYY/bar", "test");
                    dir.CreateFile("ghi/XXX/YYY/bar", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "xyz", ".")),
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
                "r65",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "a/");
                    dir.CreateDirectory("a");
                    dir.CreateFile("a/foo", "xyz");
                    dir.CreateFile("a/bar", "xyz");
                },
                DifferentialCase.Exact("--path-separator", "/", "xyz", ".")),
            new(
                "tests/regression.rs",
                "r67",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "/*\n!/dir");
                    dir.CreateDirectory("dir");
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/bar", "test");
                    dir.CreateFile("dir/bar", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r87",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "foo\n**no-vcs**");
                    dir.CreateFile("foo", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r90",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "!.foo");
                    dir.CreateFile(".foo", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r93",
                dir => dir.CreateFile("foo", "192.168.1.1"),
                DifferentialCase.Exact("--path-separator", "/", @"(\d{1,3}\.){3}\d{1,3}", ".")),
            new(
                "tests/regression.rs",
                "r99",
                dir =>
                {
                    dir.CreateFile("foo1", "test");
                    dir.CreateFile("foo2", "zzz");
                    dir.CreateFile("bar", "test");
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "-j1", "--heading", "test", ".")),
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
                "notutf8_file",
                dir => dir.CreateBytes("foo", [(byte)'q', (byte)'u', (byte)'u', (byte)'x', 0xFF, (byte)'b', (byte)'a', (byte)'z']),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", @"(?-u)\xFF")),
            new(
                "tests/json.rs",
                "crlf",
                dir => dir.CreateFile("sherlock", SherlockCrlf),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", "--crlf", @"Sherlock$", "sherlock")),
            new(
                "tests/json.rs",
                "r1095_missing_crlf",
                dir => dir.CreateFile("foo", "test\r\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", "test"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--json", "test", "--crlf")),
            new(
                "tests/json.rs",
                "r1095_crlf_empty_match",
                dir => dir.CreateFile("foo", "test\r\n\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "-U", "--json", "\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "-U", "--json", "\n", "--crlf")),
            new(
                "tests/feature.rs",
                "f1_sjis",
                dir => dir.CreateBytes("foo", [(byte)0x84, (byte)'Y', (byte)0x84, (byte)'u', (byte)0x84, (byte)0x82, (byte)0x84, (byte)'|', (byte)0x84, (byte)0x80, (byte)0x84, (byte)'{', (byte)' ', (byte)0x84, (byte)'V', (byte)0x84, (byte)0x80, (byte)0x84, (byte)'|', (byte)0x84, (byte)'}', (byte)0x84, (byte)0x83]),
                DifferentialCase.Exact("--path-separator", "/", "-Esjis", CyrillicSherlockHolmes)),
            new(
                "tests/feature.rs",
                "f1_utf16_auto",
                CreateUtf16Sherlock,
                DifferentialCase.Exact("--path-separator", "/", CyrillicSherlockHolmes)),
            new(
                "tests/feature.rs",
                "f1_utf16_explicit",
                CreateUtf16Sherlock,
                DifferentialCase.Exact("--path-separator", "/", "-Eutf-16le", CyrillicSherlockHolmes)),
            new(
                "tests/feature.rs",
                "f1_eucjp",
                dir => dir.CreateBytes("foo", [(byte)0xA7, (byte)0xBA, (byte)0xA7, (byte)0xD6, (byte)0xA7, (byte)0xE2, (byte)0xA7, (byte)0xDD, (byte)0xA7, (byte)0xE0, (byte)0xA7, (byte)0xDC, (byte)' ', (byte)0xA7, (byte)0xB7, (byte)0xA7, (byte)0xE0, (byte)0xA7, (byte)0xDD, (byte)0xA7, (byte)0xDE, (byte)0xA7, (byte)0xE3]),
                DifferentialCase.Exact("--path-separator", "/", "-Eeuc-jp", CyrillicSherlockHolmes)),
            new(
                "tests/feature.rs",
                "f1_unknown_encoding",
                _ => { },
                DifferentialCase.Exact("--path-separator", "/", "-Efoobar")),
            new(
                "tests/feature.rs",
                "f1_replacement_encoding",
                _ => { },
                DifferentialCase.Exact("--path-separator", "/", "-Ecsiso2022kr")),
            new(
                "tests/feature.rs",
                "f7",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("pat", "Sherlock\nHolmes");
                },
                DifferentialCase.Exact("--path-separator", "/", "-fpat", "sherlock")),
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
                "with_heading",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--with-filename", "--heading", "Sherlock", "sherlock")),
            new(
                "tests/misc.rs",
                "with_heading_default",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("foo", "Sherlock Holmes lives on Baker Street.");
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "-j1", "--heading", "Sherlock", ".")),
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
                "file_types",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "Sherlock");
                    dir.CreateFile("file.rs", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "-t", "rust", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "file_types_all",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "-t", "all", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "file_types_negate",
                dir =>
                {
                    dir.CreateFile("file.py", "Sherlock");
                    dir.CreateFile("file.rs", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "-T", "rust", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "file_types_negate_all",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "-T", "all", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "file_type_clear",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "Sherlock");
                    dir.CreateFile("file.rs", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "--type-clear", "rust", "-t", "rust", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "file_type_add",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "Sherlock");
                    dir.CreateFile("file.rs", "Sherlock");
                    dir.CreateFile("file.wat", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "--type-add", "wat:*.wat", "-t", "wat", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "file_type_add_compose",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "Sherlock");
                    dir.CreateFile("file.rs", "Sherlock");
                    dir.CreateFile("file.wat", "Sherlock");
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--type-add", "wat:*.wat", "--type-add", "combo:include:wat,py", "-t", "combo", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "glob",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.py", "Sherlock");
                    dir.CreateFile("file.rs", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "-g", "*.rs", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "glob_negate",
                dir =>
                {
                    dir.CreateFile("file.py", "Sherlock");
                    dir.CreateFile("file.rs", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "-g", "!*.rs", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "glob_case_insensitive",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.HTML", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "--iglob", "*.html", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "glob_case_sensitive",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file1.HTML", "Sherlock");
                    dir.CreateFile("file2.html", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "--glob", "*.html", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "glob_always_case_insensitive",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("file.HTML", "Sherlock");
                },
                DifferentialCase.Exact("--path-separator", "/", "--glob-case-insensitive", "--glob", "*.html", "Sherlock", ".")),
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
                "max_filesize_parse_error_length",
                _ => { },
                DifferentialCase.Exact("--path-separator", "/", "--max-filesize", "44444444444444444444")),
            new(
                "tests/misc.rs",
                "max_filesize_parse_error_suffix",
                _ => { },
                DifferentialCase.Exact("--path-separator", "/", "--max-filesize", "45k")),
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
                "max_filesize_suffix_overflow",
                dir => dir.CreateSize("foo", 1_000_000),
                DifferentialCase.Exact("--path-separator", "/", "--max-filesize", "34359738368G", "--files")),
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
                "ignore_git_parent",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "sherlock\n");
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/sherlock", Sherlock);
                },
                DifferentialCase.ExactInDirectory("foo", "--path-separator", "/", "Sherlock")),
            new(
                "tests/misc.rs",
                "ignore_git_parent_stop",
                dir =>
                {
                    dir.CreateFile(".gitignore", "sherlock\n");
                    dir.CreateDirectory("foo/.git");
                    dir.CreateDirectory("foo/bar");
                    dir.CreateFile("foo/bar/sherlock", Sherlock);
                },
                DifferentialCase.ExactInDirectory("foo/bar", "--path-separator", "/", "Sherlock")),
            new(
                "tests/misc.rs",
                "ignore_git_parent_stop_file",
                dir =>
                {
                    dir.CreateFile(".gitignore", "sherlock\n");
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/.git", string.Empty);
                    dir.CreateDirectory("foo/bar");
                    dir.CreateFile("foo/bar/sherlock", Sherlock);
                },
                DifferentialCase.ExactInDirectory("foo/bar", "--path-separator", "/", "Sherlock")),
            new(
                "tests/misc.rs",
                "ignore_ripgrep_parent_no_stop",
                dir =>
                {
                    dir.CreateFile(".rgignore", "sherlock\n");
                    dir.CreateDirectory("foo/.git");
                    dir.CreateDirectory("foo/bar");
                    dir.CreateFile("foo/bar/sherlock", Sherlock);
                },
                DifferentialCase.ExactInDirectory("foo/bar", "--path-separator", "/", "Sherlock")),
            new(
                "tests/misc.rs",
                "no_parent_ignore_git",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "sherlock\n");
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/.gitignore", "watson\n");
                    dir.CreateFile("foo/sherlock", Sherlock);
                    dir.CreateFile("foo/watson", Sherlock);
                },
                DifferentialCase.ExactInDirectory("foo", "--path-separator", "/", "--no-ignore-parent", "Sherlock")),
            new(
                "tests/misc.rs",
                "symlink_nofollow",
                dir =>
                {
                    dir.CreateDirectory("foo");
                    dir.CreateDirectory("foo/bar");
                    dir.LinkDirectory("foo/baz", "foo/bar/baz");
                    dir.CreateDirectory("foo/baz");
                    dir.CreateFile("foo/baz/sherlock", Sherlock);
                },
                DifferentialCase.ExactInDirectory("foo/bar", "--path-separator", "/", "Sherlock")),
            new(
                "tests/misc.rs",
                "symlink_follow",
                dir =>
                {
                    dir.CreateDirectory("foo");
                    dir.CreateDirectory("foo/bar");
                    dir.CreateDirectory("foo/baz");
                    dir.CreateFile("foo/baz/sherlock", Sherlock);
                    dir.LinkDirectory("foo/baz", "foo/bar/baz");
                },
                DifferentialCase.ExactInDirectory("foo/bar", "--path-separator", "/", "-L", "Sherlock")),
            new(
                "tests/misc.rs",
                "unrestricted1",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile(".gitignore", "sherlock\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-u", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "unrestricted2",
                dir => dir.CreateFile(".sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-uu", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "unrestricted3",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("hay", "foo\0bar\nfoo\0baz\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-uuu", "foo", ".")),
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
                "preprocessing",
                dir => CreateUpstreamDataFile(dir, "sherlock.xz"),
                DifferentialCase.Exact("--path-separator", "/", "--pre", "xzcat", "Sherlock", "sherlock.xz")),
            new(
                "tests/misc.rs",
                "preprocessing_glob",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    CreateUpstreamDataFile(dir, "sherlock.xz");
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "--pre", "xzcat", "--pre-glob", "*.xz", "Sherlock", ".")),
            new(
                "tests/misc.rs",
                "compressed_gzip",
                dir => CreateUpstreamDataFile(dir, "sherlock.gz"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.gz")),
            new(
                "tests/misc.rs",
                "compressed_bzip2",
                dir => CreateUpstreamDataFile(dir, "sherlock.bz2"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.bz2")),
            new(
                "tests/misc.rs",
                "compressed_xz",
                dir => CreateUpstreamDataFile(dir, "sherlock.xz"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.xz")),
            new(
                "tests/misc.rs",
                "compressed_lz4",
                dir => CreateUpstreamDataFile(dir, "sherlock.lz4"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.lz4")),
            new(
                "tests/misc.rs",
                "compressed_lzma",
                dir => CreateUpstreamDataFile(dir, "sherlock.lzma"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.lzma")),
            new(
                "tests/misc.rs",
                "compressed_brotli",
                dir => CreateUpstreamDataFile(dir, "sherlock.br"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.br")),
            new(
                "tests/misc.rs",
                "compressed_zstd",
                dir => CreateUpstreamDataFile(dir, "sherlock.zst"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.zst")),
            new(
                "tests/misc.rs",
                "compressed_uncompress",
                dir => CreateUpstreamDataFile(dir, "sherlock.Z"),
                DifferentialCase.Exact("--path-separator", "/", "-z", "Sherlock", "sherlock.Z")),
            new(
                "tests/misc.rs",
                "compressed_failing_gzip",
                dir => dir.CreateFile("sherlock.gz", Sherlock),
                DifferentialCase.Normalized(DifferentialComparisonMode.NonEmptyStderr, "--path-separator", "/", "-z", "Sherlock", "sherlock.gz")),
            new(
                "tests/misc.rs",
                "binary_convert",
                dir => dir.CreateFile("file", "foo\0bar\nfoo\0baz\n"),
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "foo", "file")),
            new(
                "tests/misc.rs",
                "binary_convert_mmap",
                dir => dir.CreateFile("file", "foo\0bar\nfoo\0baz\n"),
                DifferentialCase.Exact("--path-separator", "/", "--mmap", "foo", "file")),
            new(
                "tests/misc.rs",
                "binary_quit",
                dir => dir.CreateFile("file", "foo\0bar\nfoo\0baz\n"),
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "foo", "-gfile")),
            new(
                "tests/misc.rs",
                "binary_quit_mmap",
                dir => dir.CreateFile("file", "foo\0bar\nfoo\0baz\n"),
                DifferentialCase.Exact("--path-separator", "/", "--mmap", "foo", "-gfile")),
            new(
                "tests/misc.rs",
                "binary_search_mmap",
                dir => dir.CreateFile("file", "foo\0bar\nfoo\0baz\n"),
                DifferentialCase.Exact("--path-separator", "/", "-a", "--mmap", "foo", "file")),
            new(
                "tests/misc.rs",
                "binary_search_no_mmap",
                dir => dir.CreateFile("file", "foo\0bar\nfoo\0baz\n"),
                DifferentialCase.Exact("--path-separator", "/", "-a", "--no-mmap", "foo", "file")),
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
                "type_list",
                _ => { },
                DifferentialCase.Exact("--path-separator", "/", "--type-list")),
            new(
                "tests/misc.rs",
                "sort_files",
                CreateSortFixture,
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "test", ".")),
            new(
                "tests/misc.rs",
                "sort_accessed",
                CreateSortFixture,
                DifferentialCase.Exact("--path-separator", "/", "--sort", "accessed", "test", ".")),
            new(
                "tests/misc.rs",
                "sortr_accessed",
                CreateSortFixture,
                DifferentialCase.Exact("--path-separator", "/", "--sortr", "accessed", "test", ".")),
            new(
                "tests/feature.rs",
                "f45_relative_cwd",
                dir =>
                {
                    dir.CreateFile(".not-an-ignore", "foo\n/bar");
                    dir.CreateDirectory("bar");
                    dir.CreateDirectory("baz/bar");
                    dir.CreateDirectory("baz/baz/bar");
                    dir.CreateFile("bar/test", "test");
                    dir.CreateFile("baz/bar/test", "test");
                    dir.CreateFile("baz/baz/bar/test", "test");
                    dir.CreateFile("baz/foo", "test");
                    dir.CreateFile("baz/test", "test");
                    dir.CreateFile("foo", "test");
                    dir.CreateFile("test", "test");
                },
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "-l", "test"),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--path-separator", "/", "-l", "test", "--ignore-file", ".not-an-ignore"),
                DifferentialCase.NormalizedInDirectory(DifferentialComparisonMode.SortLines, "baz", "--path-separator", "/", "--ignore-file", "../.not-an-ignore", "-l", "test")),
            new(
                "tests/feature.rs",
                "f45_precedence_with_others",
                dir =>
                {
                    dir.CreateFile(".not-an-ignore", "*.log");
                    dir.CreateFile(".ignore", "!imp.log");
                    dir.CreateFile("imp.log", "test");
                    dir.CreateFile("wat.log", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--ignore-file", ".not-an-ignore", "test")),
            new(
                "tests/feature.rs",
                "f45_precedence_internal",
                dir =>
                {
                    dir.CreateFile(".not-an-ignore1", "*.log");
                    dir.CreateFile(".not-an-ignore2", "!imp.log");
                    dir.CreateFile("imp.log", "test");
                    dir.CreateFile("wat.log", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--ignore-file", ".not-an-ignore1", "--ignore-file", ".not-an-ignore2", "test")),
            new(
                "tests/feature.rs",
                "f68_no_ignore_vcs",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "foo");
                    dir.CreateFile(".ignore", "bar");
                    dir.CreateFile("foo", "test");
                    dir.CreateFile("bar", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--no-ignore-vcs", "test")),
            new(
                "tests/feature.rs",
                "f70_smart_case",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-S", "sherlock")),
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
                "f196_persistent_config",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile(".ripgreprc", "--ignore-case");
                },
                DifferentialCase.Exact("--path-separator", "/", "sherlock", "sherlock"),
                DifferentialCase.ExactWithConfig(".ripgreprc", "--path-separator", "/", "sherlock", "sherlock")),
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
                "f362_dfa_size_limit",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "--dfa-size-limit", "10", @"For\s", "sherlock")),
            new(
                "tests/feature.rs",
                "f362_exceeds_regex_size_limit",
                _ => { },
                DifferentialCase.Exact("--path-separator", "/", "--regex-size-limit", "10K", @"[0-9]\w+")),
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
                "tests/feature.rs",
                "f948_exit_code_error",
                dir => dir.CreateFile("sherlock", "Sherlock\nWatson\n"),
                DifferentialCase.Exact("--path-separator", "/", "*", ".")),
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
                "r127",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "foo/sherlock\n");
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/sherlock", Sherlock);
                    dir.CreateFile("foo/watson", Sherlock);
                },
                DifferentialCase.Exact("--path-separator", "/", "Sherlock", ".")),
            new(
                "tests/regression.rs",
                "r128",
                dir => dir.CreateFile("foo", "\n\n\n\nx"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "x", ".")),
            new(
                "tests/regression.rs",
                "r137",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.LinkFile("sherlock", "sym1");
                    dir.LinkFile("sherlock", "sym2");
                },
                DifferentialCase.Exact("--path-separator", "/", "-j1", "Sherlock", "./", "sym1", "sym2")),
            new(
                "tests/regression.rs",
                "r156",
                dir => dir.CreateFile(
                    "testcase.txt",
                    "#parse('widgets/foo_bar_macros.vm')\n" +
                    "#parse ( 'widgets/mobile/foo_bar_macros.vm' )\n" +
                    "#parse (\"widgets/foobarhiddenformfields.vm\")\n" +
                    "#parse ( \"widgets/foo_bar_legal.vm\" )\n" +
                    "#include( 'widgets/foo_bar_tips.vm' )\n" +
                    "#include('widgets/mobile/foo_bar_macros.vm')\n" +
                    "#include (\"widgets/mobile/foo_bar_resetpw.vm\")\n" +
                    "#parse('widgets/foo-bar-macros.vm')\n" +
                    "#parse ( 'widgets/mobile/foo-bar-macros.vm' )\n" +
                    "#parse (\"widgets/foo-bar-hiddenformfields.vm\")\n" +
                    "#parse ( \"widgets/foo-bar-legal.vm\" )\n" +
                    "#include( 'widgets/foo-bar-tips.vm' )\n" +
                    "#include('widgets/mobile/foo-bar-macros.vm')\n" +
                    "#include (\"widgets/mobile/foo-bar-resetpw.vm\")\n"),
                DifferentialCase.Exact("--path-separator", "/", "-N", @"#(?:parse|include)\s*\(\s*(?:""|')[./A-Za-z_-]+(?:""|')", "testcase.txt")),
            new(
                "tests/regression.rs",
                "r184",
                dir =>
                {
                    dir.CreateFile(".gitignore", ".*");
                    dir.CreateDirectory("foo/bar");
                    dir.CreateFile("foo/bar/baz", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", "."),
                DifferentialCase.ExactInDirectory("foo/bar", "--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r199",
                dir => dir.CreateFile("foo", "tEsT"),
                DifferentialCase.Exact("--path-separator", "/", "--smart-case", @"\btest\b", ".")),
            new(
                "tests/regression.rs",
                "r206",
                dir =>
                {
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/bar.txt", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", "-g", "*.txt", ".")),
            new(
                "tests/regression.rs",
                "r228",
                dir => dir.CreateDirectory("foo"),
                DifferentialCase.Exact("--path-separator", "/", "--ignore-file", "foo", "test", ".")),
            new(
                "tests/regression.rs",
                "r229",
                dir => dir.CreateFile("foo", "economie"),
                DifferentialCase.Exact("--path-separator", "/", "-S", "[E]conomie", ".")),
            new(
                "tests/regression.rs",
                "r251",
                dir => dir.CreateFile("foo", "привет\nПривет\nПрИвЕт"),
                DifferentialCase.Exact("--path-separator", "/", "-i", "привет", ".")),
            new(
                "tests/regression.rs",
                "r256",
                dir =>
                {
                    dir.CreateDirectory("bar");
                    dir.CreateFile("bar/baz", "test");
                    dir.LinkDirectory("bar", "foo");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", "foo")),
            new(
                "tests/regression.rs",
                "r256_j1",
                dir =>
                {
                    dir.CreateDirectory("bar");
                    dir.CreateFile("bar/baz", "test");
                    dir.LinkDirectory("bar", "foo");
                },
                DifferentialCase.Exact("--path-separator", "/", "-j1", "test", "foo")),
            new(
                "tests/regression.rs",
                "r270",
                dir => dir.CreateFile("foo", "-test"),
                DifferentialCase.Exact("--path-separator", "/", "-e", "-test", ".")),
            new(
                "tests/regression.rs",
                "r279",
                dir => dir.CreateFile("foo", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-q", "test", ".")),
            new(
                "tests/regression.rs",
                "r391",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile("lock", string.Empty);
                    dir.CreateFile("bar.py", string.Empty);
                    dir.CreateFile(".git/packed-refs", string.Empty);
                    dir.CreateFile(".git/description", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--no-ignore", "--hidden", "--follow", "--files", "--glob", "!{.git,node_modules,plugged}/**", "--glob", "*.{js,json,php,md,styl,scss,sass,pug,html,config,py,cpp,c,go,hs}", ".")),
            new(
                "tests/regression.rs",
                "r405",
                dir =>
                {
                    dir.CreateDirectory("foo/bar");
                    dir.CreateDirectory("bar/foo");
                    dir.CreateFile("foo/bar/file1.txt", "test");
                    dir.CreateFile("bar/foo/file2.txt", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "-g", "!/foo/**", "test", ".")),
            new(
                "tests/regression.rs",
                "r451_only_matching_as_in_issue",
                dir => dir.CreateFile("digits.txt", "1 2 3\n"),
                DifferentialCase.Exact("--path-separator", "/", "--only-matching", @"[0-9]+", "digits.txt")),
            new(
                "tests/regression.rs",
                "r451_only_matching",
                dir => dir.CreateFile("digits.txt", "1 2 3\n123\n"),
                DifferentialCase.Exact("--path-separator", "/", "--only-matching", "--column", @"[0-9]", "digits.txt")),
            new(
                "tests/regression.rs",
                "r483_matching_no_stdout",
                dir => dir.CreateFile("file.py", string.Empty),
                DifferentialCase.Exact("--path-separator", "/", "--quiet", "--files", "--glob", "*.py", ".")),
            new(
                "tests/regression.rs",
                "r483_non_matching_exit_code",
                dir => dir.CreateFile("file.rs", string.Empty),
                DifferentialCase.Exact("--path-separator", "/", "--quiet", "--files", "--glob", "*.py", ".")),
            new(
                "tests/regression.rs",
                "r493",
                dir => dir.CreateFile("input.txt", "peshwaship 're seminomata"),
                DifferentialCase.Exact("--path-separator", "/", "-o", @"\b 're \b", "input.txt")),
            new(
                "tests/regression.rs",
                "r506_word_not_parenthesized",
                dir => dir.CreateFile("wb.txt", "min minimum amin\nmax maximum amax"),
                DifferentialCase.Exact("--path-separator", "/", "-w", "-o", "min|max", "wb.txt")),
            new(
                "tests/regression.rs",
                "r553_switch",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-i", "sherlock", "."),
                DifferentialCase.Exact("--path-separator", "/", "-i", "-i", "sherlock", ".")),
            new(
                "tests/regression.rs",
                "r553_flag",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-C", "1", @"world|attached", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "-C", "1", @"world|attached", "sherlock", "-C", "0")),
            new(
                "tests/regression.rs",
                "r568_leading_hyphen_option_args",
                dir => dir.CreateFile("file", "foo bar -baz\n"),
                DifferentialCase.Exact("--path-separator", "/", "-e-baz", "-e", "-baz", "file"),
                DifferentialCase.Exact("--path-separator", "/", "-rni", "bar", "file"),
                DifferentialCase.Exact("--path-separator", "/", "-r", "-n", "-i", "bar", "file")),
            new(
                "tests/regression.rs",
                "r693_context_in_contextless_mode",
                dir =>
                {
                    dir.CreateFile("foo", "xyz\n");
                    dir.CreateFile("bar", "xyz\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-C1", "-c", "--sort-files", "xyz", ".")),
            new(
                "tests/regression.rs",
                "r807",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", ".a/b");
                    dir.CreateDirectory(".a/b");
                    dir.CreateDirectory(".a/c");
                    dir.CreateFile(".a/b/file", "test");
                    dir.CreateFile(".a/c/file", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--hidden", "test", ".")),
            new(
                "tests/regression.rs",
                "r2711",
                dir =>
                {
                    dir.CreateDirectory("a/b");
                    dir.CreateFile("a/.ignore", ".foo");
                    dir.CreateFile("a/b/.foo", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--hidden", "--files"),
                DifferentialCase.Exact("--path-separator", "/", "--hidden", "--files", "./"),
                DifferentialCase.Exact("--path-separator", "/", "--hidden", "--files", "a"),
                DifferentialCase.Exact("--path-separator", "/", "--hidden", "--files", "a/b"),
                DifferentialCase.Exact("--path-separator", "/", "--hidden", "--files", "./a"),
                DifferentialCase.ExactInDirectory("a", "--path-separator", "/", "--hidden", "--files"),
                DifferentialCase.ExactInDirectory("a/b", "--path-separator", "/", "--hidden", "--files")),
            new(
                "tests/regression.rs",
                "r829_original",
                dir =>
                {
                    dir.CreateDirectory("a/b");
                    dir.CreateFile(".ignore", "/a/b");
                    dir.CreateFile("a/b/test.txt", "Sample text");
                },
                DifferentialCase.Exact("--path-separator", "/", "Sample", "."),
                DifferentialCase.Exact("--path-separator", "/", "Sample", "a"),
                DifferentialCase.ExactInDirectory("a", "--path-separator", "/", "Sample", ".")),
            new(
                "tests/regression.rs",
                "r829_2731",
                dir =>
                {
                    dir.CreateDirectory("some_dir/build");
                    dir.CreateFile("some_dir/build/foo", "string");
                    dir.CreateFile(".ignore", "build/\n!/some_dir/build/");
                },
                DifferentialCase.Exact("--path-separator", "/", "-l", "string", "."),
                DifferentialCase.Exact("--path-separator", "/", "-l", "string", "some_dir"),
                DifferentialCase.Exact("--path-separator", "/", "-l", "string", "./some_dir"),
                DifferentialCase.Exact("--path-separator", "/", "-l", "string", "some_dir/build"),
                DifferentialCase.Exact("--path-separator", "/", "-l", "string", "./some_dir/build")),
            new(
                "tests/regression.rs",
                "r829_2747",
                dir =>
                {
                    dir.CreateDirectory("a/c/b");
                    dir.CreateDirectory("a/src/f/b");
                    dir.CreateFile("a/c/b/foo", string.Empty);
                    dir.CreateFile("a/src/f/b/foo", string.Empty);
                    dir.CreateFile(".ignore", "/a/*/b");
                },
                DifferentialCase.Exact("--path-separator", "/", "--files"),
                DifferentialCase.Exact("--path-separator", "/", "--files", "a/src"),
                DifferentialCase.ExactInDirectory("a/src", "--path-separator", "/", "--files")),
            new(
                "tests/regression.rs",
                "r829_2778",
                dir =>
                {
                    dir.CreateDirectory("parent/subdir");
                    dir.CreateFile(".ignore", "/parent/*.txt");
                    dir.CreateFile("parent/ignore-me.txt", string.Empty);
                    dir.CreateFile("parent/subdir/dont-ignore-me.txt", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--files"),
                DifferentialCase.ExactInDirectory("parent", "--path-separator", "/", "--files")),
            new(
                "tests/regression.rs",
                "r829_2836",
                dir =>
                {
                    dir.CreateDirectory("testdir/sub/sub2");
                    dir.CreateFile(".ignore", "/testdir/sub/sub2/\n");
                    dir.CreateFile("testdir/sub/sub2/foo", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--files"),
                DifferentialCase.ExactInDirectory("testdir", "--path-separator", "/", "--files")),
            new(
                "tests/regression.rs",
                "r829_2933",
                dir =>
                {
                    dir.CreateDirectory("testdir/sub/sub2");
                    dir.CreateFile(".ignore", "/testdir/sub/sub2/");
                    dir.CreateFile("testdir/sub/sub2/testfile", "needle");
                },
                DifferentialCase.ExactInDirectory("testdir", "--path-separator", "/", "--files-with-matches", "needle", ".")),
            new(
                "tests/regression.rs",
                "r900",
                dir =>
                {
                    dir.CreateFile("sherlock", Sherlock);
                    dir.CreateFile("pat", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "-fpat", "sherlock")),
            new(
                "tests/regression.rs",
                "r1064",
                dir => dir.CreateFile("input", "abc"),
                DifferentialCase.Exact("--path-separator", "/", "a(.*c)", ".")),
            new(
                "tests/regression.rs",
                "r1098",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "a**b");
                    dir.CreateFile("afoob", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r1130",
                dir => dir.CreateFile("foo", "test"),
                DifferentialCase.Exact("--path-separator", "/", "--files-with-matches", "test", "foo"),
                DifferentialCase.Exact("--path-separator", "/", "--files-without-match", "nada", "foo")),
            new(
                "tests/regression.rs",
                "r1159_invalid_flag",
                _ => { },
                DifferentialCase.Exact("--path-separator", "/", "--wat")),
            new(
                "tests/regression.rs",
                "r1159_exit_status",
                dir => dir.CreateFile("foo", "test"),
                DifferentialCase.Exact("--path-separator", "/", "test", "foo"),
                DifferentialCase.Exact("--path-separator", "/", "-q", "test", "foo"),
                DifferentialCase.Exact("--path-separator", "/", "test", "foo", "no-file"),
                DifferentialCase.Exact("--path-separator", "/", "-q", "test", "foo", "no-file"),
                DifferentialCase.Exact("--path-separator", "/", "nada", "foo"),
                DifferentialCase.Exact("--path-separator", "/", "-q", "nada", "foo"),
                DifferentialCase.Exact("--path-separator", "/", "nada", "foo", "no-file"),
                DifferentialCase.Exact("--path-separator", "/", "-q", "nada", "foo", "no-file")),
            new(
                "tests/regression.rs",
                "r1163",
                dir => dir.CreateFile("bom.txt", "\uFEFFtest123\ntest123"),
                DifferentialCase.Exact("--path-separator", "/", "^test123", ".")),
            new(
                "tests/regression.rs",
                "r1164",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "myfile");
                    dir.CreateFile("MYFILE", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "--ignore-file-case-insensitive", "test", "."),
                DifferentialCase.Exact("--path-separator", "/", "--ignore-file-case-insensitive", "--no-ignore-file-case-insensitive", "test", ".")),
            new(
                "tests/regression.rs",
                "r1173",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "**");
                    dir.CreateFile("foo", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r1174",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "**/**/*");
                    dir.CreateDirectory("a");
                    dir.CreateFile("a/foo", "test");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", ".")),
            new(
                "tests/regression.rs",
                "r1176_literal_file",
                dir =>
                {
                    dir.CreateFile("patterns", "foo(bar\n");
                    dir.CreateFile("test", "foo(bar");
                },
                DifferentialCase.Exact("--path-separator", "/", "-F", "-f", "patterns", "test")),
            new(
                "tests/regression.rs",
                "r1176_line_regex",
                dir =>
                {
                    dir.CreateFile("patterns", "foo\n");
                    dir.CreateFile("test", "foobar\nfoo\nbarfoo\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-x", "-f", "patterns", "test")),
            new(
                "tests/regression.rs",
                "r1203_reverse_suffix_literal",
                dir => dir.CreateFile("test", "153.230000\n"),
                DifferentialCase.Exact("--path-separator", "/", @"\d\d\d00", "test"),
                DifferentialCase.Exact("--path-separator", "/", @"\d\d\d000", "test")),
            new(
                "tests/regression.rs",
                "r1223_no_dir_check_for_default_path",
                dir =>
                {
                    dir.CreateDirectory("-");
                    dir.CreateFile("a.json", "{}");
                    dir.CreateFile("a.txt", "some text");
                },
                DifferentialCase.ExactWithStandardInput(EncodingUtf8.GetBytes("a.json\na.txt"), "--path-separator", "/", "a")),
            new(
                "tests/regression.rs",
                "r1259_drop_last_byte_nonl",
                dir =>
                {
                    dir.CreateFile("patterns-nonl", "[foo]");
                    dir.CreateFile("patterns-nl", "[foo]\n");
                    dir.CreateFile("test", "fz");
                },
                DifferentialCase.Exact("--path-separator", "/", "-f", "patterns-nonl", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-f", "patterns-nl", "test")),
            new(
                "tests/regression.rs",
                "r1311_multi_line_term_replace",
                dir => dir.CreateFile("input", "hello\nworld\n"),
                DifferentialCase.Exact("--path-separator", "/", "-U", "-r?", "-n", "\n", "input")),
            new(
                "tests/regression.rs",
                "r1319",
                dir => dir.CreateFile("input", "CCAGCTACTCGGGAGGCTGAGGCTGGAGGATCGCTTGAGTCCAGGAGTTC"),
                DifferentialCase.Exact("--path-separator", "/", "TTGAGTCCAGGAG[ATCG]{2}C", ".")),
            new(
                "tests/regression.rs",
                "r1334_invert_empty_patterns",
                dir =>
                {
                    dir.CreateFile("zero-patterns", string.Empty);
                    dir.CreateFile("one-pattern", "\n");
                    dir.CreateFile("haystack", "one\ntwo\nthree\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-f", "zero-patterns", "haystack"),
                DifferentialCase.Exact("--path-separator", "/", "-f", "one-pattern", "haystack"),
                DifferentialCase.Exact("--path-separator", "/", "-vf", "zero-patterns", "haystack"),
                DifferentialCase.Exact("--path-separator", "/", "-vf", "one-pattern", "haystack")),
            new(
                "tests/regression.rs",
                "r1334_crazy_literals",
                dir =>
                {
                    StringBuilder patterns = new();
                    for (int index = 0; index < 40; index++)
                    {
                        patterns.Append("1.208.0.0/12\n");
                    }

                    dir.CreateFile("patterns", patterns.ToString());
                    dir.CreateFile("corpus", "1.208.0.0/12\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-Ff", "patterns", "corpus")),
            new(
                "tests/regression.rs",
                "r1380",
                dir => dir.CreateFile("foo", "a\nb\nc\nd\ne\nd\ne\nd\ne\nd\ne\n"),
                DifferentialCase.Exact("--path-separator", "/", "-A2", "-m1", "d", "foo")),
            new(
                "tests/regression.rs",
                "r1389_bad_symlinks_no_biscuit",
                dir =>
                {
                    dir.CreateDirectory("mydir");
                    dir.CreateFile("mydir/file.txt", "test");
                    dir.LinkDirectory("mydir", "mylink");
                },
                DifferentialCase.Exact("--path-separator", "/", "test", "--no-ignore", "--sort", "path", "mylink")),
            new(
                "tests/regression.rs",
                "r1537",
                dir => dir.CreateFile("foo", "abc;de,fg"),
                DifferentialCase.Exact("--path-separator", "/", ";(.*,){1}", ".")),
            new(
                "tests/regression.rs",
                "r1559",
                dir => dir.CreateFile(
                    "foo",
                    "type A struct {\n" +
                    "\tTaskID int `json:\"taskID\"`\n" +
                    "}\n\n" +
                    "type B struct {\n" +
                    "\tObjectID string `json:\"objectID\"`\n" +
                    "\tTaskID   int    `json:\"taskID\"`\n" +
                    "}\n"),
                DifferentialCase.Exact("--path-separator", "/", "TaskID +int", ".")),
            new(
                "tests/regression.rs",
                "r1638",
                dir => dir.CreateBytes("foo", [0xEF, 0xBB, 0xBF, (byte)'x']),
                DifferentialCase.Exact("--path-separator", "/", "--column", "x", ".")),
            new(
                "tests/regression.rs",
                "r1739_replacement_lineterm_match",
                dir => dir.CreateFile("test", "a\n"),
                DifferentialCase.Exact("--path-separator", "/", "-r${0}f", ".*", "test")),
            new(
                "tests/regression.rs",
                "f1757",
                dir =>
                {
                    dir.CreateDirectory("rust/target");
                    dir.CreateFile(".ignore", "rust/target\nrust1/target/onemore");
                    dir.CreateFile("rust/source.rs", "needle");
                    dir.CreateFile("rust/target/rustdoc-output.html", "needle");
                    dir.CreateDirectory("rust1/target/onemore");
                    dir.CreateFile("rust1/source.rs", "needle");
                    dir.CreateFile("rust1/target/onemore/rustdoc-output.html", "needle");
                },
                DifferentialCase.Exact("--path-separator", "/", "--files-with-matches", "needle", "rust"),
                DifferentialCase.Exact("--path-separator", "/", "--files-with-matches", "needle", "./rust"),
                DifferentialCase.Exact("--path-separator", "/", "--files-with-matches", "needle", "rust1"),
                DifferentialCase.Exact("--path-separator", "/", "--files-with-matches", "needle", "./rust1")),
            new(
                "tests/regression.rs",
                "r1765",
                dir => dir.CreateFile("test", "\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.NonEmptyStdout, "--path-separator", "/", "x?", "--crlf", "--color", "always")),
            new(
                "tests/regression.rs",
                "r1866",
                dir => dir.CreateFile("test", "foobar\nfoobar\nfoo quux"),
                DifferentialCase.Exact("--path-separator", "/", "--multiline", "--vimgrep", @"foobar\nfoobar\nfoo|quux", "test")),
            new(
                "tests/regression.rs",
                "r1868_context_passthru_override",
                dir => dir.CreateFile("test", "foo\nbar\nbaz\nquux\n"),
                DifferentialCase.Exact("--path-separator", "/", "-C1", "bar", "test"),
                DifferentialCase.Exact("--path-separator", "/", "--passthru", "bar", "test"),
                DifferentialCase.Exact("--path-separator", "/", "--passthru", "-C1", "bar", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-C1", "--passthru", "bar", "test"),
                DifferentialCase.Exact("--path-separator", "/", "--passthru", "-B1", "bar", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-B1", "--passthru", "bar", "test"),
                DifferentialCase.Exact("--path-separator", "/", "--passthru", "-A1", "bar", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-A1", "--passthru", "bar", "test")),
            new(
                "tests/regression.rs",
                "r1878",
                dir => dir.CreateFile("test", "a\nbaz\nabc\n"),
                DifferentialCase.Exact("--path-separator", "/", "-U", "--no-mmap", "^baz", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-U", "--mmap", "^baz", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-U", "--no-mmap", "(?-m)^baz", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-U", "--mmap", "(?-m)^baz", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-U", "--no-mmap", @"\Abaz", "test"),
                DifferentialCase.Exact("--path-separator", "/", "-U", "--mmap", @"\Abaz", "test")),
            new(
                "tests/regression.rs",
                "r1891",
                dir => dir.CreateFile("test", "\n##\n"),
                DifferentialCase.Exact("--path-separator", "/", "-won", string.Empty, "test")),
            new(
                "tests/regression.rs",
                "r2094",
                dir => dir.CreateFile("haystack", "a\nb\nc\na\nb\nc"),
                DifferentialCase.Exact("--path-separator", "/", "--no-line-number", "--no-filename", "--multiline", "--max-count=1", "--passthru", "--replace=B", "b", "haystack")),
            new(
                "tests/regression.rs",
                "r2198",
                dir =>
                {
                    dir.CreateFile(".ignore", "a");
                    dir.CreateFile(".rgignore", "b");
                    dir.CreateFile("a", string.Empty);
                    dir.CreateFile("b", string.Empty);
                    dir.CreateFile("c", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--files", "--sort", "path"),
                DifferentialCase.Exact("--path-separator", "/", "--files", "--sort", "path", "--no-ignore-dot")),
            new(
                "tests/regression.rs",
                "r2236",
                dir =>
                {
                    dir.CreateFile(".ignore", @"foo\/");
                    dir.CreateDirectory("foo");
                    dir.CreateFile("foo/bar", "test\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "test")),
            new(
                "tests/regression.rs",
                "r2574",
                dir => dir.CreateFile("haystack", "some.domain.com\nsome.domain.com/x\n"),
                DifferentialCase.Exact("--path-separator", "/", "--no-filename", "--no-unicode", "-w", "-o", @"(\w+\.)*domain\.(\w+)")),
            new(
                "tests/regression.rs",
                "r2658_null_data_line_regexp",
                dir => dir.CreateFile("haystack", "foo\0bar\0quux\0"),
                DifferentialCase.Exact("--path-separator", "/", "--null-data", "--line-regexp", "bar")),
            new(
                "tests/regression.rs",
                "r2770_gitignore_error",
                dir =>
                {
                    dir.CreateFile(".git", string.Empty);
                    dir.CreateFile(".gitignore", "**/bar/*");
                    dir.CreateDirectory("foo/bar");
                    dir.CreateFile("foo/bar/baz", "quux");
                },
                DifferentialCase.Exact("--path-separator", "/", "-l", "quux"),
                DifferentialCase.ExactInDirectory("foo", "--path-separator", "/", "-l", "quux")),
            new(
                "tests/regression.rs",
                "r2944_incorrect_bytes_searched",
                dir => dir.CreateFile("haystack", "foo1\nfoo2\nfoo3\nfoo4\nfoo5\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "--stats", "-m2", "foo", ".")),
            new(
                "tests/regression.rs",
                "r2990_trip_over_trailing_dot",
                dir =>
                {
                    dir.CreateDirectory("asdf");
                    dir.CreateDirectory("asdf.");
                    dir.CreateFile("asdf/foo", string.Empty);
                    dir.CreateFile("asdf./foo", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--files", "-g", "!asdf/"),
                DifferentialCase.Exact("--path-separator", "/", "--files", "-g", "!asdf./")),
            new(
                "tests/regression.rs",
                "r3067_gitignore_error",
                dir =>
                {
                    dir.CreateFile(".git", string.Empty);
                    dir.CreateFile(".gitignore", "foobar/debug");
                    dir.CreateDirectory("foobar/some/debug");
                    dir.CreateDirectory("foobar/debug");
                    dir.CreateFile("foobar/some/debug/flag", "baz");
                    dir.CreateFile("foobar/debug/flag2", "baz");
                },
                DifferentialCase.Exact("--path-separator", "/", "baz")),
            new(
                "tests/regression.rs",
                "r3108_files_without_match_quiet_exit",
                dir =>
                {
                    dir.CreateFile("yes-match", "abc");
                    dir.CreateFile("non-match", "xyz");
                },
                DifferentialCase.Exact("--path-separator", "/", "-q", "abc", "non-match"),
                DifferentialCase.Exact("--path-separator", "/", "-q", "abc", "yes-match"),
                DifferentialCase.Exact("--path-separator", "/", "--files-with-matches", "-q", "abc", "non-match")),
            new(
                "tests/regression.rs",
                "r3173_hidden_whitelist_only_dot",
                dir =>
                {
                    dir.CreateDirectory("subdir");
                    dir.CreateFile("subdir/.foo.txt", "text");
                    dir.CreateFile(".ignore", "!.foo.txt");
                },
                DifferentialCase.Exact("--path-separator", "/", "--files"),
                DifferentialCase.Exact("--path-separator", "/", "--files", "."),
                DifferentialCase.Exact("--path-separator", "/", "--files", "./"),
                DifferentialCase.ExactInDirectory("subdir", "--path-separator", "/", "--files"),
                DifferentialCase.ExactInDirectory("subdir", "--path-separator", "/", "--files", "."),
                DifferentialCase.ExactInDirectory("subdir", "--path-separator", "/", "--files", "./")),
            new(
                "tests/feature.rs",
                "f419_zero_as_shortcut_for_null",
                dir => dir.CreateFile("sherlock", "Sherlock\nSherlock\n"),
                DifferentialCase.Exact("--path-separator", "/", "-0", "-c", "Sherlock", "sherlock")),
            new(
                "tests/feature.rs",
                "f740_passthru",
                dir =>
                {
                    dir.CreateFile("file", "\nfoo\nbar\nfoobar\n\nbaz\n");
                    dir.CreateFile("patterns", "foo\nbar\n");
                },
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "foo", "file"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-e", "foo", "-e", "bar", "file"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-f", "patterns", "file"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-c", "foo", "file"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-o", "foo", "file"),
                DifferentialCase.Exact("--path-separator", "/", "-n", "--passthru", "-r", "wat", "foo", "file")),
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
                "f917_trim_multi_standard",
                dir => dir.CreateFile("haystack", TrimmedLongHaystack),
                DifferentialCase.Exact("--path-separator", "/", "--multiline", "--trim", "-r$0", "--no-filename", @"a\n?bc")),
            new(
                "tests/feature.rs",
                "f917_trim_max_columns_normal",
                dir => dir.CreateFile("haystack", "     0123456789abcdefghijklmnopqrstuvwxyz"),
                DifferentialCase.Exact("--path-separator", "/", "--trim", "--max-columns-preview", "-M8", "--no-filename", "abc", ".")),
            new(
                "tests/feature.rs",
                "f917_trim_max_columns_matches",
                dir => dir.CreateFile("haystack", TrimmedLongHaystack),
                DifferentialCase.Exact("--path-separator", "/", "--trim", "--max-columns-preview", "-M8", "--color=always", "--colors=path:none", "--no-filename", "abc", ".")),
            new(
                "tests/feature.rs",
                "f917_trim_max_columns_multi_standard",
                dir => dir.CreateFile("haystack", TrimmedLongHaystack),
                DifferentialCase.Exact("--path-separator", "/", "--multiline", "--trim", "--max-columns-preview", "-M8", "--color=always", "--colors=path:none", "--no-filename", @"a\n?bc")),
            new(
                "tests/feature.rs",
                "f917_trim_max_columns_multi_only_matching",
                dir => dir.CreateFile("haystack", TrimmedLongHaystack),
                DifferentialCase.Exact("--path-separator", "/", "--multiline", "--trim", "--max-columns-preview", "-M8", "--only-matching", "--no-filename", @".*a\n?bc.*")),
            new(
                "tests/feature.rs",
                "f917_trim_max_columns_multi_per_match",
                dir => dir.CreateFile("haystack", TrimmedLongHaystack),
                DifferentialCase.Exact("--path-separator", "/", "--multiline", "--trim", "--max-columns-preview", "-M8", "--vimgrep", "--no-filename", @".*a\n?bc.*")),
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
                "f1138_no_ignore_dot",
                dir =>
                {
                    dir.CreateDirectory(".git");
                    dir.CreateFile(".gitignore", "foo");
                    dir.CreateFile(".ignore", "bar");
                    dir.CreateFile(".fzf-ignore", "quux");
                    dir.CreateFile("foo", string.Empty);
                    dir.CreateFile("bar", string.Empty);
                    dir.CreateFile("quux", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--no-ignore-dot"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--no-ignore-dot", "--ignore-file", ".fzf-ignore")),
            new(
                "tests/feature.rs",
                "f1207_auto_encoding",
                dir => dir.CreateBytes("foo", [(byte)0xFF, (byte)0xFE, 0, (byte)'b']),
                DifferentialCase.Exact("--path-separator", "/", "-a", @"\x00", "foo")),
            new(
                "tests/feature.rs",
                "f1207_ignore_encoding",
                dir => dir.CreateBytes("foo", [(byte)0xFF, (byte)0xFE, 0, (byte)'b']),
                DifferentialCase.Exact("--path-separator", "/", "--encoding", "none", "-a", @"\x00", "foo")),
            new(
                "tests/feature.rs",
                "f1414_no_require_git",
                dir =>
                {
                    dir.CreateFile(".gitignore", "foo");
                    dir.CreateFile("foo", string.Empty);
                    dir.CreateFile("bar", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--no-require-git"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--no-require-git", "--require-git")),
            new(
                "tests/feature.rs",
                "f1420_no_ignore_exclude",
                dir =>
                {
                    dir.CreateDirectory(".git/info");
                    dir.CreateFile(".git/info/exclude", "foo");
                    dir.CreateFile("bar", string.Empty);
                    dir.CreateFile("foo", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--no-ignore-exclude")),
            new(
                "tests/feature.rs",
                "f1466_no_ignore_files",
                dir =>
                {
                    dir.CreateFile(".myignore", "bar");
                    dir.CreateFile("bar", string.Empty);
                    dir.CreateFile("foo", string.Empty);
                },
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--ignore-file", ".myignore"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--ignore-file", ".myignore", "--no-ignore-files"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--ignore-file", ".myignore", "--no-ignore-files", "--ignore-files"),
                DifferentialCase.Exact("--path-separator", "/", "--sort", "path", "--files", "--ignore-file", ".myignore", "-u")),
            new(
                "tests/feature.rs",
                "f2361_sort_nested_files",
                dir => dir.CreateDirectory("dir"),
                DifferentialCase.ExactWithSetup(
                    dir => CreateNestedSortFiles(dir, "1"),
                    "--path-separator",
                    "/",
                    "--sort",
                    "accessed",
                    "--files"),
                DifferentialCase.ExactWithSetup(
                    dir => CreateNestedSortFiles(dir, "2"),
                    "--path-separator",
                    "/",
                    "--sort",
                    "accessed",
                    "--files")),
            new(
                "tests/feature.rs",
                "f1404_nothing_searched_warning",
                CreateNothingSearchedFixture,
                DifferentialCase.Exact("--path-separator", "/", "needle")),
            new(
                "tests/feature.rs",
                "f1404_nothing_searched_ignored",
                CreateNothingSearchedFixture,
                DifferentialCase.Exact("--path-separator", "/", "--no-messages", "needle")),
            new(
                "tests/feature.rs",
                "f1842_field_context_separator",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "-A1", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-context-separator", "!", "-n", "-A1", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-context-separator", "!!", "-n", "-A1", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-context-separator", @"\x7F", "-n", "-A1", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-context-separator", string.Empty, "-n", "-A1", "Doctor Watsons", "sherlock")),
            new(
                "tests/feature.rs",
                "f1842_field_match_separator",
                dir => dir.CreateFile("sherlock", Sherlock),
                DifferentialCase.Exact("--path-separator", "/", "-n", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-match-separator", "!", "-n", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-match-separator", "!!", "-n", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-match-separator", @"\x7F", "-n", "Doctor Watsons", "sherlock"),
                DifferentialCase.Exact("--path-separator", "/", "--field-match-separator", string.Empty, "-n", "Doctor Watsons", "sherlock")),
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
                "no_unicode",
                dir => dir.CreateFile("test", "\u03b4"),
                DifferentialCase.Exact("--path-separator", "/", "-i", "--no-unicode", "\u0394", "test")),
            new(
                "tests/feature.rs",
                "stop_on_nonmatch",
                dir => dir.CreateFile("test", "line1\nline2\nline3\nline4\nline5"),
                DifferentialCase.Exact("--path-separator", "/", "--stop-on-nonmatch", "[235]")),
            new(
                "tests/feature.rs",
                "f411_search_stats",
                dir => dir.CreateFile("sherlock", "needle\nmiss\nneedle\n"),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--path-separator", "/", "-j1", "--stats", "needle", ".")),
            new(
                "tests/binary.rs",
                "after_match1_implicit",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "Project Gutenberg EBook", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_implicit_binary",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "--binary", "Project Gutenberg EBook", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_implicit_text",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "--text", "Project Gutenberg EBook", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_stdin",
                _ => { },
                DifferentialCase.ExactWithStandardInput(File.ReadAllBytes(UpstreamSherlockNulPath), "--path-separator", "/", "--no-mmap", "-n", "Project Gutenberg EBook")),
            new(
                "tests/binary.rs",
                "after_match1_explicit",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "Project Gutenberg EBook", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_explicit_text",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "--text", "Project Gutenberg EBook", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_explicit_count",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-c", "Project Gutenberg EBook", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_implicit_path",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-l", "Project Gutenberg EBook", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_implicit_quiet",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-q", "Project Gutenberg EBook", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_implicit_count",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-c", "Project Gutenberg EBook", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match1_implicit_count_binary",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-c", "--binary", "Project Gutenberg EBook", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match2_implicit",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "Project Gutenberg EBook|a medical student", "-g", "hay")),
            new(
                "tests/binary.rs",
                "after_match2_implicit_text",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "--text", "Project Gutenberg EBook|a medical student", "-g", "hay")),
            new(
                "tests/binary.rs",
                "before_match1_implicit",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "Heaven", "-g", "hay")),
            new(
                "tests/binary.rs",
                "before_match1_explicit",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "Heaven", "hay")),
            new(
                "tests/binary.rs",
                "before_match1_implicit_binary",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "--binary", "Heaven", "-g", "hay")),
            new(
                "tests/binary.rs",
                "before_match1_implicit_text",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "--text", "Heaven", "-g", "hay")),
            new(
                "tests/binary.rs",
                "before_match2_implicit",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "a medical student", "-g", "hay")),
            new(
                "tests/binary.rs",
                "before_match2_explicit",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "a medical student", "hay")),
            new(
                "tests/binary.rs",
                "before_match2_implicit_text",
                CreateSherlockNul,
                DifferentialCase.Exact("--path-separator", "/", "--no-mmap", "-n", "--text", "a medical student", "-g", "hay")),
            new(
                "tests/binary.rs",
                "matching_files_inconsistent_with_count",
                CreateBinaryCountInconsistency,
                DifferentialCase.Exact("--path-separator", "/", "--sort=path", "-l", "cat"),
                DifferentialCase.Exact("--path-separator", "/", "--sort=path", "-c", "cat"),
                DifferentialCase.Exact("--path-separator", "/", "--sort=path", "-c", "cat", "--binary"),
                DifferentialCase.Exact("--path-separator", "/", "--sort=path", "-c", "cat", "--text")),
        ];

    internal static void Run(string sourceFile, string name)
    {
        PortedRgTestCase testCase = Find(sourceFile, name);
        using var directory = RgTestDirectory.Create(testCase.SourceFile.Replace('/', '-') + "-" + testCase.Name);
        testCase.Arrange(directory);
        for (int index = 0; index < testCase.Commands.Length; index++)
        {
            DifferentialCase command = testCase.Commands[index];
            command.BeforeRun?.Invoke(directory);
            using RgTestDirectory scoutDirectory = directory.Clone(testCase.Name + "-scout");
            using RgTestDirectory pinnedDirectory = directory.Clone(testCase.Name + "-pinned");
            string scoutWorkingDirectory = GetWorkingDirectory(scoutDirectory, command);
            string pinnedWorkingDirectory = GetWorkingDirectory(pinnedDirectory, command);
            DifferentialRunner.AssertMatchesPinned(command, scoutWorkingDirectory, pinnedWorkingDirectory);
        }
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

    private static string GetWorkingDirectory(RgTestDirectory directory, DifferentialCase command)
    {
        return command.RelativeWorkingDirectory is null
            ? directory.RootPath
            : Path.Combine(directory.RootPath, command.RelativeWorkingDirectory);
    }

    private static void CreateSherlockNul(RgTestDirectory dir)
    {
        dir.CreateBytes("hay", File.ReadAllBytes(UpstreamSherlockNulPath));
    }

    private static void CreateUpstreamDataFile(RgTestDirectory dir, string fileName)
    {
        dir.CreateBytes(fileName, File.ReadAllBytes(Path.Combine(UpstreamDataDirectory, fileName)));
    }

    private static void CreateUtf16Sherlock(RgTestDirectory dir)
    {
        dir.CreateBytes("foo", [(byte)0xFF, (byte)0xFE, (byte)'(', (byte)0x04, (byte)'5', (byte)0x04, (byte)'@', (byte)0x04, (byte)';', (byte)0x04, (byte)'>', (byte)0x04, (byte)':', (byte)0x04, (byte)' ', (byte)0x00, (byte)'%', (byte)0x04, (byte)'>', (byte)0x04, (byte)';', (byte)0x04, (byte)'<', (byte)0x04, (byte)'A', (byte)0x04]);
    }

    private static void CreateBinaryCountInconsistency(RgTestDirectory dir)
    {
        var file1 = new StringBuilder();
        file1.Append("cat here\n");
        for (int index = 0; index < 150_000; index++)
        {
            file1.Append("padding line\n");
        }

        file1.Append('\0');
        dir.CreateFile("file1.txt", file1.ToString());
        dir.CreateFile("file2.txt", "cat here");
    }

    private static void CreateSortFixture(RgTestDirectory dir)
    {
        dir.CreateFile("a", "test");
        dir.CreateDirectory("dir");
        dir.CreateFile("dir/c", "test");
        dir.CreateFile("b", "test");
        dir.CreateFile("dir/d", "test");
        SetAccessTimes(dir, "a", "dir/c", "b", "dir/d");
    }

    private static void CreateNestedSortFiles(RgTestDirectory dir, string contents)
    {
        dir.CreateFile("foo", contents);
        dir.CreateDirectory("dir");
        dir.CreateFile("dir/bar", contents);
        SetAccessTimes(dir, "foo", "dir/bar");
    }

    private static void SetAccessTimes(RgTestDirectory dir, params string[] relativePaths)
    {
        DateTime baseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int index = 0; index < relativePaths.Length; index++)
        {
            dir.SetLastAccessTimeUtc(relativePaths[index], baseTime.AddSeconds(index));
        }
    }

    private static void CreateNothingSearchedFixture(RgTestDirectory dir)
    {
        dir.CreateFile(".ignore", "ignored-dir/**");
        dir.CreateDirectory("ignored-dir");
        dir.CreateFile("ignored-dir/foo", "needle");
    }

    private const string UpstreamDataDirectory = "/Users/brandon/src/ripgrep/tests/data";
    private const string UpstreamSherlockNulPath = "/Users/brandon/src/ripgrep/tests/data/sherlock-nul.txt";
    private const string CyrillicSherlockHolmes = "\u0428\u0435\u0440\u043B\u043E\u043A \u0425\u043E\u043B\u043C\u0441";

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

    private const string TrimmedLongHaystack = "     0123456789abcdefghijklmnopqrstuvwxyz";
}
