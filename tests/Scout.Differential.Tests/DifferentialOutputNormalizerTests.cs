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
    /// Verifies normalized stdout preserves arbitrary non-UTF-8 bytes.
    /// </summary>
    [Fact]
    public void SortLinesPreservesNonUtf8Bytes()
    {
        byte[] input =
        [
            (byte)'b', (byte)'.', (byte)'t', (byte)'x', (byte)'t', (byte)':', 0xFF, (byte)'\n',
            (byte)'a', (byte)'.', (byte)'t', (byte)'x', (byte)'t', (byte)':', 0xFE, (byte)'\n',
        ];

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(input, DifferentialComparisonMode.SortLines);
        byte[] expected =
        [
            (byte)'a', (byte)'.', (byte)'t', (byte)'x', (byte)'t', (byte)':', 0xFE, (byte)'\n',
            (byte)'b', (byte)'.', (byte)'t', (byte)'x', (byte)'t', (byte)':', 0xFF, (byte)'\n',
        ];

        Assert.Equal(expected, normalized);
    }

    /// <summary>
    /// Verifies heading output is sorted by file block instead of individual payload lines.
    /// </summary>
    [Fact]
    public void SortLinesSortsHeadingBlocks()
    {
        string input =
            "b.txt\n" +
            "2:second\n" +
            "1:first\n" +
            "\n" +
            "a.txt\n" +
            "1:first\n";

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(Utf8.GetBytes(input), DifferentialComparisonMode.SortLines);

        Assert.Equal(
            "a.txt\n" +
            "1:first\n" +
            "\n" +
            "b.txt\n" +
            "2:second\n" +
            "1:first\n",
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
    /// Verifies NUL-terminated file lists are sorted as records instead of newline-delimited text.
    /// </summary>
    [Fact]
    public void SortLinesSortsNullTerminatedFileLists()
    {
        string input = "b.txt\0a.txt\0";

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(Utf8.GetBytes(input), DifferentialComparisonMode.SortLines);

        Assert.Equal("a.txt\0b.txt\0", Utf8.GetString(normalized));
    }

    /// <summary>
    /// Verifies NUL path terminators still sort payload lines by file path.
    /// </summary>
    [Fact]
    public void SortLinesSortsNullTerminatedPathPayloadLines()
    {
        string input =
            "b.txt\0" + "2\n" +
            "a.txt\0" + "1\n";

        byte[] normalized = DifferentialOutputNormalizer.NormalizeStdout(Utf8.GetBytes(input), DifferentialComparisonMode.SortLines);

        Assert.Equal(
            "a.txt\0" + "1\n" +
            "b.txt\0" + "2\n",
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

    /// <summary>
    /// Verifies stderr-only upstream assertions can require non-empty stderr without pinning tool-specific text.
    /// </summary>
    [Fact]
    public void NonEmptyStderrMasksNonEmptyErrorText()
    {
        Assert.Equal("<non-empty stderr>", DifferentialOutputNormalizer.NormalizeStderr("gzip failed", DifferentialComparisonMode.NonEmptyStderr));
        Assert.Equal(string.Empty, DifferentialOutputNormalizer.NormalizeStderr(string.Empty, DifferentialComparisonMode.NonEmptyStderr));
    }
}
