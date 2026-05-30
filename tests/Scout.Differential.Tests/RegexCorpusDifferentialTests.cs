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
    private const int ExpectedDifferentialCaseCount = 16;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly (string RelativePath, string Name)[] DifferentialCases =
    [
        ("misc.toml", "ascii-literal"),
        ("misc.toml", "ascii-literal-not"),
        ("misc.toml", "prefix-literal-match"),
        ("misc.toml", "suffix-600"),
        ("flags.toml", "1"),
        ("flags.toml", "2"),
        ("flags.toml", "3"),
        ("flags.toml", "9"),
        ("flags.toml", "10"),
        ("crazy.toml", "ranges-not"),
        ("crazy.toml", "float1"),
        ("crazy.toml", "float3"),
        ("crazy.toml", "float4"),
        ("crazy.toml", "date1"),
        ("crazy.toml", "neg-class-letter"),
        ("crazy.toml", "greedy-many-many"),
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
