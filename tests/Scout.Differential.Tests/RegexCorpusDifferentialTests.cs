using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scout;

/// <summary>
/// Runs selected regex crate corpus cases through Scout and pinned ripgrep.
/// </summary>
public sealed class RegexCorpusDifferentialTests
{
    private const int ExpectedDifferentialCaseCount = 70;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly (string RelativePath, string Name)[] DifferentialCases =
    [
        ("misc.toml", "ascii-literal"),
        ("misc.toml", "ascii-literal-not"),
        ("misc.toml", "prefix-literal-match"),
        ("misc.toml", "prefix-literal-match-ascii"),
        ("misc.toml", "prefix-literal-no-match"),
        ("misc.toml", "one-literal-edge"),
        ("misc.toml", "terminates"),
        ("misc.toml", "suffix-100"),
        ("misc.toml", "suffix-200"),
        ("misc.toml", "suffix-300"),
        ("misc.toml", "suffix-400"),
        ("misc.toml", "suffix-500"),
        ("misc.toml", "suffix-600"),
        ("flags.toml", "1"),
        ("flags.toml", "2"),
        ("flags.toml", "3"),
        ("flags.toml", "4"),
        ("flags.toml", "5"),
        ("flags.toml", "6"),
        ("flags.toml", "9"),
        ("flags.toml", "10"),
        ("flags.toml", "11"),
        ("iter.toml", "1"),
        ("iter.toml", "2"),
        ("iter.toml", "empty1"),
        ("iter.toml", "empty2"),
        ("iter.toml", "empty3"),
        ("iter.toml", "empty4"),
        ("iter.toml", "empty6"),
        ("iter.toml", "empty7"),
        ("iter.toml", "start1"),
        ("iter.toml", "start2"),
        ("empty.toml", "100"),
        ("empty.toml", "120"),
        ("empty.toml", "130"),
        ("empty.toml", "200"),
        ("empty.toml", "210"),
        ("empty.toml", "220"),
        ("empty.toml", "240"),
        ("empty.toml", "300"),
        ("empty.toml", "320"),
        ("empty.toml", "330"),
        ("empty.toml", "400"),
        ("empty.toml", "500"),
        ("empty.toml", "510"),
        ("empty.toml", "520"),
        ("crazy.toml", "ranges-not"),
        ("crazy.toml", "float1"),
        ("crazy.toml", "float3"),
        ("crazy.toml", "float4"),
        ("crazy.toml", "float5"),
        ("crazy.toml", "date1"),
        ("crazy.toml", "date2"),
        ("crazy.toml", "date3"),
        ("crazy.toml", "neg-class-letter"),
        ("crazy.toml", "neg-class-letter-comma"),
        ("crazy.toml", "neg-class-letter-space"),
        ("crazy.toml", "neg-class-comma"),
        ("crazy.toml", "neg-class-space"),
        ("crazy.toml", "neg-class-space-comma"),
        ("crazy.toml", "neg-class-comma-space"),
        ("crazy.toml", "neg-class-ascii"),
        ("crazy.toml", "lazy-many-many"),
        ("crazy.toml", "lazy-many-optional"),
        ("crazy.toml", "lazy-one-many-many"),
        ("crazy.toml", "lazy-one-many-optional"),
        ("crazy.toml", "greedy-many-many"),
        ("crazy.toml", "greedy-many-optional"),
        ("crazy.toml", "greedy-one-many-many"),
        ("crazy.toml", "greedy-one-many-optional"),
    ];

    /// <summary>
    /// Verifies regex corpus cases are compared through the CLI differential oracle.
    /// </summary>
    /// <param name="relativePath">The regex corpus TOML file.</param>
    /// <param name="name">The regex corpus case name.</param>
    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void CorpusCaseMatchesPinnedRipgrep(string relativePath, string name)
    {
        RegexCorpusCase corpusCase = RegexCorpusLoader.Load(relativePath, name);
        AssertSearchCompatible(corpusCase, relativePath);

        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "haystack");
            File.WriteAllBytes(path, corpusCase.Haystack);

            var differentialCase = DifferentialCase.Normalized(
                DifferentialComparisonMode.MaskElapsed,
                BuildArguments(corpusCase, path));
            DifferentialRunner.AssertMatchesPinned(differentialCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the rg-backed regex corpus differential catalog stays intentional.
    /// </summary>
    [Fact]
    public void CatalogDocumentsCurrentDifferentialCoverage()
    {
        Assert.Equal(ExpectedDifferentialCaseCount, DifferentialCases.Length);
    }

    /// <summary>
    /// Gets regex corpus cases that currently exercise supported CLI-facing regex behavior.
    /// </summary>
    /// <returns>The corpus case parameters.</returns>
    public static IEnumerable<object[]> CorpusCases()
    {
        for (int index = 0; index < DifferentialCases.Length; index++)
        {
            yield return [DifferentialCases[index].RelativePath, DifferentialCases[index].Name];
        }
    }

    private static void AssertSearchCompatible(RegexCorpusCase corpusCase, string relativePath)
    {
        Assert.True(corpusCase.Compiles, relativePath + "::" + corpusCase.Name + " is expected to compile.");
        Assert.False(corpusCase.Anchored, relativePath + "::" + corpusCase.Name + " uses anchored engine-only semantics.");
        Assert.Null(corpusCase.MatchLimit);
        Assert.Equal(0, corpusCase.BoundsStart);
        Assert.Equal(corpusCase.Haystack.Length, corpusCase.BoundsEnd);
        Assert.Equal((byte)'\n', corpusCase.LineTerminator);
        Assert.NotEmpty(corpusCase.Patterns);
    }

    private static string[] BuildArguments(RegexCorpusCase corpusCase, string path)
    {
        var arguments = new List<string>
        {
            "-U",
            "--json",
            "-o",
        };
        if (corpusCase.CaseInsensitive)
        {
            arguments.Add("-i");
        }

        for (int index = 0; index < corpusCase.Patterns.Count; index++)
        {
            arguments.Add("-e");
            arguments.Add(Utf8.GetString(corpusCase.Patterns[index]));
        }

        arguments.Add(path);
        return arguments.ToArray();
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-regex-corpus-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
