using System;

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
    /// Verifies elapsed-only normalization is not enough for explicitly parallel text output.
    /// </summary>
    [Fact]
    public void MaskElapsedRejectsExplicitParallelTextOutput()
    {
        Assert.Throws<ArgumentException>(() => DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--threads=2", "needle", "."));
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
    public void DisabledJsonAndStatsAllowExactOutput()
    {
        var testCase = DifferentialCase.Exact("--json", "--no-json", "--stats", "--no-stats", "needle", ".");

        Assert.Equal(DifferentialComparisonMode.Exact, testCase.ComparisonMode);
    }
}
