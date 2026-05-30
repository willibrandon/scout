using System;
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Verifies the regex corpus harness tracks the pinned regex crate corpus.
/// </summary>
public sealed class RegexCorpusCoverageTests
{
    private const int ExpectedUpstreamCaseCount = 839;
    private const int ExpectedSupportedCaseCount = 361;

    private static readonly (string RelativePath, int Count)[] ExpectedUpstreamFileCounts =
    [
        ("anchored.toml", 12),
        ("bytes.toml", 26),
        ("crazy.toml", 52),
        ("crlf.toml", 15),
        ("earliest.toml", 7),
        ("empty.toml", 19),
        ("expensive.toml", 2),
        ("flags.toml", 11),
        ("iter.toml", 22),
        ("leftmost-all.toml", 3),
        ("line-terminator.toml", 10),
        ("misc.toml", 16),
        ("multiline.toml", 140),
        ("no-unicode.toml", 23),
        ("overlapping.toml", 23),
        ("regex-lite.toml", 9),
        ("regression.toml", 86),
        ("set.toml", 52),
        ("substring.toml", 4),
        ("unicode.toml", 84),
        ("utf8.toml", 28),
        ("word-boundary-special.toml", 92),
        ("word-boundary.toml", 103),
    ];

    private static readonly (string RelativePath, int Count)[] ExpectedSupportedFileCounts =
    [
        ("anchored.toml", 9),
        ("bytes.toml", 9),
        ("crazy.toml", 52),
        ("crlf.toml", 15),
        ("empty.toml", 19),
        ("flags.toml", 11),
        ("iter.toml", 22),
        ("line-terminator.toml", 10),
        ("misc.toml", 16),
        ("multiline.toml", 140),
        ("no-unicode.toml", 15),
        ("regex-lite.toml", 5),
        ("regression.toml", 18),
        ("set.toml", 18),
        ("substring.toml", 2),
    ];

    /// <summary>
    /// Verifies supported regex corpus cases are unique and pinned to upstream case inventory.
    /// </summary>
    [Fact]
    public void CatalogDocumentsCurrentRegexCorpusCoverage()
    {
        string[] supportedKeys = RegexCorpusTests.CorpusCaseKeys();
        var upstream = new SortedSet<string>(RegexCorpusLoader.EnumerateAllCaseKeys(), StringComparer.Ordinal);
        var supported = new SortedSet<string>(supportedKeys, StringComparer.Ordinal);

        Assert.Equal(ExpectedSupportedCaseCount, supportedKeys.Length);
        Assert.Equal(supportedKeys.Length, supported.Count);
        Assert.Equal(ExpectedUpstreamCaseCount, upstream.Count);
        Assert.Equal(ExpectedUpstreamFileCounts, CountByRelativePath(upstream));
        Assert.Equal(ExpectedSupportedFileCounts, CountByRelativePath(supported));
        Assert.Empty(Difference(supported, upstream));
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
}
