
namespace Scout;

/// <summary>
/// Verifies differential cases opt into normalization when upstream output is intentionally unstable.
/// </summary>
public sealed class DifferentialCasePolicyTests
{
    /// <summary>
    /// Verifies JSON comparisons cannot use exact byte matching because JSON summaries contain elapsed time.
    /// </summary>
    [Fact]
    public void ExactRejectsJsonOutput()
    {
        Assert.Throws<ArgumentException>(() => DifferentialCase.Exact("--json", "needle", "."));
    }

    /// <summary>
    /// Verifies stats comparisons cannot sort without also masking wall-clock fields.
    /// </summary>
    [Fact]
    public void SortLinesRejectsStatsOutput()
    {
        Assert.Throws<ArgumentException>(() => DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--stats", "needle", "."));
    }

    /// <summary>
    /// Verifies serial stats comparisons may mask elapsed fields without sorting path output.
    /// </summary>
    [Fact]
    public void MaskElapsedAllowsSerialStatsOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "-j1", "--stats", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.MaskElapsed, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies sorted stats comparisons may mask only elapsed fields because sorting forces serial output.
    /// </summary>
    [Fact]
    public void MaskElapsedAllowsSortedExplicitParallelStatsOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "-j2", "--sort=path", "--stats", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.MaskElapsed, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies explicitly parallel stats comparisons must sort path-ordered output and mask elapsed fields.
    /// </summary>
    [Fact]
    public void MaskElapsedRejectsExplicitParallelStatsOutput()
    {
        Assert.Throws<ArgumentException>(() => DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "-j2", "--stats", "needle", "."));
    }

    /// <summary>
    /// Verifies exact comparisons cannot opt into ripgrep's parallel walker without also normalizing path order.
    /// </summary>
    [Fact]
    public void ExactRejectsExplicitParallelOutput()
    {
        Assert.Throws<ArgumentException>(() => DifferentialCase.Exact("-j2", "needle", "."));
    }

    /// <summary>
    /// Verifies default directory traversal resolves to path-order normalization because ripgrep's walker can emit files in different orders.
    /// </summary>
    [Fact]
    public void ExactNormalizesDefaultDirectoryOutput()
    {
        var testCase = DifferentialCase.Exact("needle", ".");

        Assert.Equal(DifferentialComparisonMode.SortLines, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies file-list mode resolves to path-order normalization because it uses ripgrep's walker.
    /// </summary>
    [Fact]
    public void ExactNormalizesDefaultFileListOutput()
    {
        var testCase = DifferentialCase.Exact("--files", ".");

        Assert.Equal(DifferentialComparisonMode.SortLines, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies sorted traversal remains exact because ripgrep serializes sorted walks.
    /// </summary>
    [Fact]
    public void ExactAllowsSortedDirectoryOutput()
    {
        var testCase = DifferentialCase.Exact("--sort=path", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies ripgrep's legacy sorted-files switch is treated as sorted traversal.
    /// </summary>
    [Fact]
    public void ExactAllowsSortFilesDirectoryOutput()
    {
        var testCase = DifferentialCase.Exact("--sort-files", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies sorted traversal remains exact even when a parallel thread count is requested.
    /// </summary>
    [Fact]
    public void ExactAllowsSortedExplicitParallelOutput()
    {
        var testCase = DifferentialCase.Exact("-j2", "--sort=path", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies sorted-files traversal remains exact even when a parallel thread count is requested.
    /// </summary>
    [Fact]
    public void ExactAllowsSortFilesExplicitParallelOutput()
    {
        var testCase = DifferentialCase.Exact("-j2", "--sort-files", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies runtime directory paths also resolve to path-order normalization once the fixture exists.
    /// </summary>
    [Fact]
    public void ExactNormalizesRuntimeDirectoryOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "scout-diff-policy-" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "haystack");
        Directory.CreateDirectory(directory);
        try
        {
            var testCase = DifferentialCase.Exact("needle", "haystack");

            Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
            Assert.Equal(DifferentialComparisonMode.SortLines, testCase.GetComparisonMode(testCase.Arguments, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies multiple explicit search paths resolve to path-order normalization because ripgrep can search them in parallel.
    /// </summary>
    [Fact]
    public void ExactNormalizesMultipleExplicitPaths()
    {
        var testCase = DifferentialCase.Exact("needle", "first", "second");

        Assert.Equal(DifferentialComparisonMode.SortLines, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies elapsed-field comparisons over multiple explicit paths also sort path output.
    /// </summary>
    [Fact]
    public void MaskElapsedNormalizesMultipleExplicitPathStatsOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--stats", "needle", "first", "second");

        Assert.Equal(DifferentialComparisonMode.SortLinesAndMaskElapsed, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies forced serial multi-path searches keep exact comparisons available.
    /// </summary>
    [Fact]
    public void ExactAllowsForcedSerialMultipleExplicitPaths()
    {
        var testCase = DifferentialCase.Exact("-j1", "needle", "first", "second");

        Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies elapsed-only normalization is not enough for explicitly parallel text output.
    /// </summary>
    [Fact]
    public void MaskElapsedRejectsExplicitParallelTextOutput()
    {
        Assert.Throws<ArgumentException>(() => DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--threads=2", "needle", "."));
    }

    /// <summary>
    /// Verifies default directory stats comparisons mask elapsed fields and sort path output.
    /// </summary>
    [Fact]
    public void MaskElapsedNormalizesDefaultDirectoryStatsOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--stats", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.SortLinesAndMaskElapsed, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies sorted-files stats output masks elapsed fields without sorting path output again.
    /// </summary>
    [Fact]
    public void MaskElapsedAllowsSortFilesDirectoryStatsOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--sort-files", "--stats", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.MaskElapsed, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies default directory JSON comparisons sort path output and mask elapsed summary fields.
    /// </summary>
    [Fact]
    public void MaskElapsedNormalizesDefaultDirectoryJsonOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--json", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.SortLinesAndMaskElapsed, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies sorted comparisons may opt into ripgrep's parallel walker.
    /// </summary>
    [Fact]
    public void SortLinesAllowsExplicitParallelOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "--threads", "2", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.SortLines, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies forced serial search keeps exact comparisons available.
    /// </summary>
    [Fact]
    public void ExactAllowsForcedSerialOutput()
    {
        var testCase = DifferentialCase.Exact("-j1", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies explicitly parallel JSON and stats comparisons can opt into both required normalizers.
    /// </summary>
    [Fact]
    public void SortLinesAndMaskElapsedAllowsExplicitParallelJsonStatsOutput()
    {
        var testCase = DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "-j2", "--json", "--stats", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.SortLinesAndMaskElapsed, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies disabled JSON and stats flags do not force elapsed masking.
    /// </summary>
    [Fact]
    public void DisabledJsonAndStatsDoNotForceElapsedMasking()
    {
        var testCase = DifferentialCase.Exact("--json", "--no-json", "--stats", "--no-stats", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.SortLines, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies elapsed-field policy follows parsed mode precedence instead of raw token presence.
    /// </summary>
    [Fact]
    public void JsonResetByFilesModeDoesNotForceElapsedMasking()
    {
        var testCase = DifferentialCase.Exact("--json", "--files", ".");

        Assert.Equal(DifferentialComparisonMode.SortLines, testCase.ComparisonMode);
    }

    /// <summary>
    /// Verifies file-list mode reset by JSON still requires elapsed masking.
    /// </summary>
    [Fact]
    public void FilesResetByJsonModeRequiresElapsedMasking()
    {
        Assert.Throws<ArgumentException>(() => DifferentialCase.Exact("--files", "--json", "needle", "."));
    }
}
