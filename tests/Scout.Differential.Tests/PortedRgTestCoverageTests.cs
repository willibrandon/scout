using System.Text.RegularExpressions;

namespace Scout;

/// <summary>
/// Verifies the ported ripgrep test catalog tracks upstream rgtest coverage.
/// </summary>
[TestClass]
public sealed partial class PortedRgTestCoverageTests
{
    private static readonly string UpstreamTestsRoot = Path.Join(FindRepositoryRoot(), "upstream", "ripgrep-e89fff89", "tests");

    private static readonly string[] ExpectedUnportedRgTests = [];

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
    [TestMethod]
    public void CatalogDocumentsCurrentUpstreamRgtestGaps()
    {
        SortedSet<string> upstream = ReadUpstreamRgTests();
        SortedSet<string> catalog = ReadCatalog();

        Assert.AreSequenceEqual(ExpectedUnportedRgTests, Difference(upstream, catalog));
        Assert.AreSequenceEqual(ExpectedCatalogSplitRgTests, Difference(catalog, upstream));
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
        string path = Path.Join(FindRepositoryRoot(), "tests", "Scout.Differential.Tests", "PortedRgTests.catalog");
        string nativePcre2Path = Path.Join(FindRepositoryRoot(), "tests", "Scout.Differential.Tests", "NativePcre2RgTests.catalog");
        string nativeInvalidUtf8Path = Path.Join(FindRepositoryRoot(), "tests", "Scout.Differential.Tests", "NativeInvalidUtf8RgTests.catalog");
        var tests = new SortedSet<string>(StringComparer.Ordinal);
        ReadCatalogFile(path, tests);
        ReadCatalogFile(nativePcre2Path, tests);
        ReadCatalogFile(nativeInvalidUtf8Path, tests);
        return tests;
    }

    private static void ReadCatalogFile(string path, SortedSet<string> tests)
    {
        foreach (string name in File.ReadLines(path).Select(static line => line.Trim()).Where(static line => line.Length > 0))
        {
            tests.Add(name);
        }
    }

    private static string[] Difference(SortedSet<string> left, SortedSet<string> right)
    {
        return left.Except(right, StringComparer.Ordinal).ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Scout.slnx")))
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
