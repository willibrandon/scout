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
    private const int ExpectedDifferentialCaseCount = 191;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly (string RelativePath, int Count)[] ExpectedDifferentialFileCounts =
    [
        ("bytes.toml", 17),
        ("crazy.toml", 47),
        ("crlf.toml", 4),
        ("empty.toml", 19),
        ("flags.toml", 9),
        ("iter.toml", 19),
        ("misc.toml", 13),
        ("multiline.toml", 1),
        ("no-unicode.toml", 15),
        ("regex-lite.toml", 5),
        ("regression.toml", 13),
        ("set.toml", 29),
    ];

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
        ("iter.toml", "empty5"),
        ("iter.toml", "empty6"),
        ("iter.toml", "empty7"),
        ("iter.toml", "empty8"),
        ("iter.toml", "empty9"),
        ("iter.toml", "empty10"),
        ("iter.toml", "empty11"),
        ("iter.toml", "nonempty-followedby-empty"),
        ("iter.toml", "nonempty-followedby-oneempty"),
        ("iter.toml", "nonempty-followedby-onemixed"),
        ("iter.toml", "nonempty-followedby-twomixed"),
        ("iter.toml", "start1"),
        ("iter.toml", "start2"),
        ("empty.toml", "100"),
        ("empty.toml", "110"),
        ("empty.toml", "120"),
        ("empty.toml", "130"),
        ("empty.toml", "200"),
        ("empty.toml", "210"),
        ("empty.toml", "220"),
        ("empty.toml", "230"),
        ("empty.toml", "240"),
        ("empty.toml", "300"),
        ("empty.toml", "310"),
        ("empty.toml", "320"),
        ("empty.toml", "330"),
        ("empty.toml", "400"),
        ("empty.toml", "500"),
        ("empty.toml", "510"),
        ("empty.toml", "520"),
        ("empty.toml", "600"),
        ("empty.toml", "610"),
        ("crazy.toml", "ranges"),
        ("crazy.toml", "ranges-not"),
        ("crazy.toml", "float1"),
        ("crazy.toml", "float3"),
        ("crazy.toml", "float4"),
        ("crazy.toml", "float5"),
        ("crazy.toml", "email"),
        ("crazy.toml", "email-not"),
        ("crazy.toml", "email-big"),
        ("crazy.toml", "date1"),
        ("crazy.toml", "date2"),
        ("crazy.toml", "date3"),
        ("crazy.toml", "start-end-empty"),
        ("crazy.toml", "start-end-empty-rev"),
        ("crazy.toml", "start-end-empty-many-1"),
        ("crazy.toml", "start-end-empty-many-2"),
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
        ("crazy.toml", "lazy-range-min-many"),
        ("crazy.toml", "lazy-range-many"),
        ("crazy.toml", "greedy-many-many"),
        ("crazy.toml", "greedy-many-optional"),
        ("crazy.toml", "greedy-one-many-many"),
        ("crazy.toml", "greedy-one-many-optional"),
        ("crazy.toml", "greedy-range-min-many"),
        ("crazy.toml", "greedy-range-many"),
        ("crazy.toml", "empty1"),
        ("crazy.toml", "empty2"),
        ("crazy.toml", "empty3"),
        ("crazy.toml", "empty4"),
        ("crazy.toml", "empty5"),
        ("crazy.toml", "empty6"),
        ("crazy.toml", "empty7"),
        ("crazy.toml", "empty8"),
        ("crazy.toml", "empty9"),
        ("crazy.toml", "empty10"),
        ("crazy.toml", "empty11"),
        ("crlf.toml", "basic"),
        ("crlf.toml", "start-end-non-empty"),
        ("crlf.toml", "start-end-empty"),
        ("crlf.toml", "dot-no-crlf"),
        ("bytes.toml", "word-boundary-ascii"),
        ("bytes.toml", "word-boundary-ascii-not"),
        ("bytes.toml", "perl-word-ascii"),
        ("bytes.toml", "perl-decimal-ascii"),
        ("bytes.toml", "perl-whitespace-ascii"),
        ("bytes.toml", "case-one-ascii"),
        ("bytes.toml", "case-one-unicode"),
        ("bytes.toml", "case-class-simple-ascii"),
        ("bytes.toml", "case-class-ascii"),
        ("bytes.toml", "dotstar-prefix-ascii"),
        ("bytes.toml", "dotstar-prefix-unicode"),
        ("bytes.toml", "invalid-utf8-anchor-100"),
        ("bytes.toml", "invalid-utf8-anchor-300"),
        ("bytes.toml", "negate-ascii"),
        ("bytes.toml", "null-bytes"),
        ("bytes.toml", "word-boundary-ascii-100"),
        ("bytes.toml", "word-boundary-ascii-200"),
        ("regression.toml", "negated-char-class-100"),
        ("regression.toml", "negated-char-class-200"),
        ("regression.toml", "ascii-word-underscore"),
        ("regression.toml", "alt-in-alt-100"),
        ("regression.toml", "alt-in-alt-200"),
        ("regression.toml", "leftmost-first-prefix"),
        ("regression.toml", "many-alternates"),
        ("regression.toml", "word-boundary-alone-100"),
        ("regression.toml", "word-boundary-alone-200"),
        ("regression.toml", "partial-anchor"),
        ("regression.toml", "partial-anchor-alternate-begin"),
        ("regression.toml", "partial-anchor-alternate-end"),
        ("regression.toml", "lits-unambiguous-100"),
        ("multiline.toml", "repeat8"),
        ("no-unicode.toml", "invalid-utf8-literal1"),
        ("no-unicode.toml", "mixed"),
        ("no-unicode.toml", "case1"),
        ("no-unicode.toml", "case2"),
        ("no-unicode.toml", "case4"),
        ("no-unicode.toml", "negate2"),
        ("no-unicode.toml", "dotstar-prefix1"),
        ("no-unicode.toml", "dotstar-prefix2"),
        ("no-unicode.toml", "null-bytes1"),
        ("no-unicode.toml", "word-ascii"),
        ("no-unicode.toml", "decimal-ascii"),
        ("no-unicode.toml", "space-ascii"),
        ("no-unicode.toml", "iter1-bytes"),
        ("no-unicode.toml", "iter2-bytes"),
        ("no-unicode.toml", "unanchored-invalid-utf8-match-100"),
        ("regex-lite.toml", "perl-class-decimal"),
        ("regex-lite.toml", "perl-class-space"),
        ("regex-lite.toml", "perl-class-word"),
        ("regex-lite.toml", "word-boundary"),
        ("regex-lite.toml", "case-insensitive-is-ascii-only"),
        ("set.toml", "basic30"),
        ("set.toml", "basic40"),
        ("set.toml", "basic10-leftmost-first"),
        ("set.toml", "basic60-leftmost-first"),
        ("set.toml", "basic61-leftmost-first"),
        ("set.toml", "basic71"),
        ("set.toml", "basic80"),
        ("set.toml", "basic81"),
        ("set.toml", "basic82"),
        ("set.toml", "basic91"),
        ("set.toml", "basic110"),
        ("set.toml", "basic111"),
        ("set.toml", "basic120"),
        ("set.toml", "basic121"),
        ("set.toml", "basic122"),
        ("set.toml", "basic130"),
        ("set.toml", "empty10-leftmost-first"),
        ("set.toml", "empty11-leftmost-first"),
        ("set.toml", "empty20-leftmost-first"),
        ("set.toml", "empty21-leftmost-first"),
        ("set.toml", "empty30-leftmost-first"),
        ("set.toml", "empty31-leftmost-first"),
        ("set.toml", "empty40"),
        ("set.toml", "nomatch10"),
        ("set.toml", "nomatch20"),
        ("set.toml", "nomatch40"),
        ("set.toml", "caps-110"),
        ("set.toml", "caps-120"),
        ("set.toml", "caps-121"),
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
                BuildArguments(corpusCase, relativePath, path));
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
        string[] keys = CorpusCaseKeys();
        var upstream = new SortedSet<string>(RegexCorpusLoader.EnumerateAllCaseKeys(), StringComparer.Ordinal);
        var differential = new SortedSet<string>(keys, StringComparer.Ordinal);

        Assert.Equal(ExpectedDifferentialCaseCount, keys.Length);
        Assert.Equal(keys.Length, differential.Count);
        Assert.Equal(ExpectedDifferentialFileCounts, CountByRelativePath(differential));
        Assert.Empty(Difference(differential, upstream));

        for (int index = 0; index < DifferentialCases.Length; index++)
        {
            (string relativePath, string name) = DifferentialCases[index];
            AssertSearchCompatible(RegexCorpusLoader.Load(relativePath, name), relativePath);
        }
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

    private static string[] CorpusCaseKeys()
    {
        var keys = new List<string>();
        for (int index = 0; index < DifferentialCases.Length; index++)
        {
            keys.Add(DifferentialCases[index].RelativePath + "|" + DifferentialCases[index].Name);
        }

        return keys.ToArray();
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

    private static (string RelativePath, int Count)[] CountByRelativePath(IEnumerable<string> keys)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            int separator = key.IndexOf('|', StringComparison.Ordinal);
            string relativePath = separator < 0 ? key : key[..separator];
            counts.TryGetValue(relativePath, out int count);
            counts[relativePath] = count + 1;
        }

        var result = new (string RelativePath, int Count)[counts.Count];
        int index = 0;
        foreach (KeyValuePair<string, int> pair in counts)
        {
            result[index] = (pair.Key, pair.Value);
            index++;
        }

        return result;
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

    private static string[] BuildArguments(RegexCorpusCase corpusCase, string relativePath, string path)
    {
        var arguments = new List<string>
        {
            "-U",
            "--json",
            "-o",
        };
        if (string.Equals(relativePath, "bytes.toml", StringComparison.Ordinal) ||
            string.Equals(relativePath, "regex-lite.toml", StringComparison.Ordinal) ||
            string.Equals(relativePath, "no-unicode.toml", StringComparison.Ordinal))
        {
            arguments.Add("--no-unicode");
        }

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
