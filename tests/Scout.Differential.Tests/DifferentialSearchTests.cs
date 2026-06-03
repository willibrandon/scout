
namespace Scout;

/// <summary>
/// Verifies byte-for-byte search parity against the pinned ripgrep binary.
/// </summary>
public sealed class DifferentialSearchTests
{
    /// <summary>
    /// Verifies a baseline flag matrix against the pinned ripgrep binary.
    /// </summary>
    [Fact]
    public void BaselineSearchMatrixMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        try
        {
            string first = Path.Combine(root, "first.txt");
            string second = Path.Combine(root, "second.txt");
            string digits = Path.Combine(root, "digits.txt");
            File.WriteAllText(first, "needle\nmiss\nneedle again\n");
            File.WriteAllText(second, "alpha\nneedle second\n");
            File.WriteAllText(digits, "123\n456\n789");

            DifferentialCase[] cases =
            [
                DifferentialCase.Exact("needle", first),
                DifferentialCase.Exact("-nH", "needle", first),
                DifferentialCase.Exact("-nA1", "needle", first),
                DifferentialCase.Exact("-v", "needle", first),
                DifferentialCase.Exact("-c", "needle", first),
                DifferentialCase.Exact("--count-matches", "needle", first),
                DifferentialCase.Exact("-o", "needle", first),
                DifferentialCase.Exact("--sort=path", "-H", "needle", root),
                DifferentialCase.Exact("-ne", "needle", first),
                DifferentialCase.Exact("-m1", "needle", first),
                DifferentialCase.Exact("-n", "-U", @"(?m)(?:^\d+$\n?)+", digits),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "-H", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "-j2", "-H", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "-l", "needle", first, second),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--heading", "-n", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--stats", "needle", first),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "--stats", "-C1", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "-j2", "--stats", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "-j2", "--sort=path", "--stats", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "-j2", "--quiet", "--stats", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "--json", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "-j2", "--json", "needle", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--json", "--files", root),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "--files", "--json", "needle", root),
            ];

            for (int index = 0; index < cases.Length; index++)
            {
                DifferentialRunner.AssertMatchesPinned(cases[index]);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
