using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Scout;

/// <summary>
/// Verifies the ported ripgrep test catalog tracks upstream rgtest coverage.
/// </summary>
public sealed partial class PortedRgTestCoverageTests
{
    private const string UpstreamTestsRoot = "/Users/brandon/src/ripgrep/tests";

    private static readonly string[] ExpectedUnportedRgTests =
    [
        "tests/feature.rs|f1155_auto_hybrid_regex", // PCRE2/hybrid-engine only.
        "tests/feature.rs|f362_u64_to_narrow_usize_overflow", // 32-bit target only.
        "tests/json.rs|notutf8", // Invalid UTF-8 filename case skipped by upstream on macOS/APFS.
        "tests/json.rs|r1412_look_behind_match_missing", // PCRE2 lookbehind only.
        "tests/regression.rs|r1401_look_ahead_only_matching_1", // PCRE2 lookahead only.
        "tests/regression.rs|r1401_look_ahead_only_matching_2", // PCRE2 lookahead only.
        "tests/regression.rs|r1412_look_behind_no_replacement", // PCRE2 lookbehind only.
        "tests/regression.rs|r1573", // PCRE2 lookahead only.
        "tests/regression.rs|r210", // Invalid UTF-8 filename case skipped on APFS.
        "tests/regression.rs|r3139_multiline_lookahead_files_with_matches", // PCRE2 lookahead only.
    ];

    private static readonly string[] ExpectedCatalogSplitRgTests =
    [
        "tests/feature.rs|f740_passthru_count_override", // Split from upstream f740_passthru.
        "tests/feature.rs|f740_passthru_file_patterns", // Split from upstream f740_passthru.
        "tests/feature.rs|f740_passthru_multiple_e", // Split from upstream f740_passthru.
        "tests/feature.rs|f740_passthru_only_matching", // Split from upstream f740_passthru.
        "tests/feature.rs|f740_passthru_replace", // Split from upstream f740_passthru.
        "tests/feature.rs|f740_passthru_single", // Split from upstream f740_passthru.
    ];

    /// <summary>
    /// Verifies every upstream rgtest is either ported or explicitly documented as blocked.
    /// </summary>
    [Fact]
    public void CatalogDocumentsCurrentUpstreamRgtestGaps()
    {
        SortedSet<string> upstream = ReadUpstreamRgTests();
        SortedSet<string> catalog = ReadCatalog();

        Assert.Equal(ExpectedUnportedRgTests, Difference(upstream, catalog));
        Assert.Equal(ExpectedCatalogSplitRgTests, Difference(catalog, upstream));
    }

    private static SortedSet<string> ReadUpstreamRgTests()
    {
        var tests = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(UpstreamTestsRoot, "*.rs"))
        {
            string sourceFile = "tests/" + Path.GetFileName(path);
            string text = File.ReadAllText(path);
            foreach (Match match in RgTestPattern().Matches(text))
            {
                tests.Add(sourceFile + "|" + match.Groups[1].Value);
            }
        }

        return tests;
    }

    private static SortedSet<string> ReadCatalog()
    {
        string path = Path.Combine(FindRepositoryRoot(), "tests", "Scout.Differential.Tests", "PortedRgTests.catalog");
        var tests = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                tests.Add(trimmed);
            }
        }

        return tests;
    }

    private static string[] Difference(SortedSet<string> left, SortedSet<string> right)
    {
        var difference = new List<string>();
        foreach (string value in left)
        {
            if (!right.Contains(value))
            {
                difference.Add(value);
            }
        }

        return difference.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }

    [GeneratedRegex(@"rgtest!\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex RgTestPattern();
}
