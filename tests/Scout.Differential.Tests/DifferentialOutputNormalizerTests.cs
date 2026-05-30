using System.Text;

namespace Scout;

/// <summary>
/// Verifies normalization used by differential comparisons against pinned ripgrep.
/// </summary>
public sealed class DifferentialOutputNormalizerTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Verifies nondeterministic multi-file text output is sorted by file while preserving per-file order.
    /// </summary>
    [Fact]
    public void SortLinesSortsByFileAndPreservesPerFileOrder()
    {
        string input =
            "b.txt:2:second\n" +
            "b.txt:1:first\n" +
            "a.txt:1:first\n";

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(Utf8.GetBytes(input), DifferentialComparisonMode.SortLines);

        Assert.Equal(
            "a.txt:1:first\n" +
            "b.txt:2:second\n" +
            "b.txt:1:first\n",
            Utf8.GetString(normalized));
    }

    /// <summary>
    /// Verifies JSON output is sorted by path while preserving each file's begin/match/end order.
    /// </summary>
    [Fact]
    public void SortLinesAndMaskElapsedSortsJsonByPathAndPreservesPerFileOrder()
    {
        string input =
            "{\"type\":\"begin\",\"data\":{\"path\":{\"text\":\"b.txt\"}}}\n" +
            "{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"b.txt\"},\"lines\":{\"text\":\"needle\\n\"}}}\n" +
            "{\"type\":\"end\",\"data\":{\"path\":{\"text\":\"b.txt\"},\"stats\":{\"elapsed\":{\"human\":\"0.123456s\",\"nanos\":123456,\"secs\":0}}}}\n" +
            "{\"type\":\"begin\",\"data\":{\"path\":{\"text\":\"a.txt\"}}}\n" +
            "{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"a.txt\"},\"lines\":{\"text\":\"needle\\n\"}}}\n" +
            "{\"type\":\"end\",\"data\":{\"path\":{\"text\":\"a.txt\"},\"stats\":{\"elapsed\":{\"human\":\"0.234567s\",\"nanos\":234567,\"secs\":0}}}}\n" +
            "{\"type\":\"summary\",\"data\":{\"elapsed_total\":{\"human\":\"0.777777s\",\"nanos\":777777,\"secs\":0}}}\n";

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(Utf8.GetBytes(input), DifferentialComparisonMode.SortLinesAndMaskElapsed);

        Assert.Equal(
            "{\"type\":\"begin\",\"data\":{\"path\":{\"text\":\"a.txt\"}}}\n" +
            "{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"a.txt\"},\"lines\":{\"text\":\"needle\\n\"}}}\n" +
            "{\"type\":\"end\",\"data\":{\"path\":{\"text\":\"a.txt\"},\"stats\":{\"elapsed\":{\"human\":\"0.000000s\",\"nanos\":0,\"secs\":0}}}}\n" +
            "{\"type\":\"begin\",\"data\":{\"path\":{\"text\":\"b.txt\"}}}\n" +
            "{\"type\":\"match\",\"data\":{\"path\":{\"text\":\"b.txt\"},\"lines\":{\"text\":\"needle\\n\"}}}\n" +
            "{\"type\":\"end\",\"data\":{\"path\":{\"text\":\"b.txt\"},\"stats\":{\"elapsed\":{\"human\":\"0.000000s\",\"nanos\":0,\"secs\":0}}}}\n" +
            "{\"type\":\"summary\",\"data\":{\"elapsed_total\":{\"human\":\"0.000000s\",\"nanos\":0,\"secs\":0}}}\n",
            Utf8.GetString(normalized));
    }

    /// <summary>
    /// Verifies JSON bytes paths are also used as sort keys.
    /// </summary>
    [Fact]
    public void SortLinesSortsJsonBytesPaths()
    {
        string input =
            "{\"type\":\"begin\",\"data\":{\"path\":{\"bytes\":\"Yg==\"}}}\n" +
            "{\"type\":\"begin\",\"data\":{\"path\":{\"bytes\":\"YQ==\"}}}\n";

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(Utf8.GetBytes(input), DifferentialComparisonMode.SortLines);

        Assert.Equal(
            "{\"type\":\"begin\",\"data\":{\"path\":{\"bytes\":\"YQ==\"}}}\n" +
            "{\"type\":\"begin\",\"data\":{\"path\":{\"bytes\":\"Yg==\"}}}\n",
            Utf8.GetString(normalized));
    }

    /// <summary>
    /// Verifies stats timing lines are masked.
    /// </summary>
    [Fact]
    public void MaskElapsedMasksStatsWallClockLines()
    {
        string input =
            "2 matches\n" +
            "0.123456 seconds spent searching\n" +
            "0.234567 seconds total\n";

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(Utf8.GetBytes(input), DifferentialComparisonMode.MaskElapsed);

        Assert.Equal(
            "2 matches\n" +
            "0.000000 seconds spent searching\n" +
            "0.000000 seconds total\n",
            Utf8.GetString(normalized));
    }
}
