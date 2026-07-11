using System.Buffers;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies ordered UTF-8 sequence decomposition for minimized Unicode-class compilation.
/// </summary>
public sealed class RegexUtf8SequenceEnumeratorTests()
{
    /// <summary>
    /// Verifies sequences cover every valid boundary in lexical order without crossing the surrogate gap.
    /// </summary>
    [Fact]
    public void EmitsOrderedDisjointSequencesAcrossScalarBoundaries()
    {
        var enumerator = new RegexUtf8SequenceEnumerator(0x7F, 0x10FFFF);
        var sequences = new List<RegexUtf8ByteSequence>();

        while (enumerator.MoveNext(out RegexUtf8ByteSequence sequence))
        {
            sequences.Add(sequence);
        }

        Assert.NotEmpty(sequences);
        int previousEnd = 0x7E;
        for (int sequenceIndex = 0; sequenceIndex < sequences.Count; sequenceIndex++)
        {
            RegexUtf8ByteSequence sequence = sequences[sequenceIndex];
            Assert.InRange(sequence.Length, 1, 4);
            for (int rangeIndex = 0; rangeIndex < sequence.Length; rangeIndex++)
            {
                RegexUtf8ByteRange range = sequence[rangeIndex];
                Assert.True(range.Start <= range.End);
            }

            int start = DecodeBoundary(sequence, useEnd: false);
            int end = DecodeBoundary(sequence, useEnd: true);
            int expectedStart = previousEnd == 0xD7FF ? 0xE000 : previousEnd + 1;
            Assert.Equal(expectedStart, start);
            Assert.True(start <= end);
            if (sequenceIndex > 0)
            {
                Assert.True(CompareBoundaries(
                    sequences[sequenceIndex - 1],
                    leftUseEnd: true,
                    sequence,
                    rightUseEnd: false) < 0);
            }

            previousEnd = end;
        }

        Assert.Equal(0x10FFFF, previousEnd);
    }

    private static int CompareBoundaries(
        RegexUtf8ByteSequence left,
        bool leftUseEnd,
        RegexUtf8ByteSequence right,
        bool rightUseEnd)
    {
        int commonLength = Math.Min(left.Length, right.Length);
        for (int index = 0; index < commonLength; index++)
        {
            RegexUtf8ByteRange leftRange = left[index];
            RegexUtf8ByteRange rightRange = right[index];
            byte leftValue = leftUseEnd ? leftRange.End : leftRange.Start;
            byte rightValue = rightUseEnd ? rightRange.End : rightRange.Start;
            int comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int DecodeBoundary(RegexUtf8ByteSequence sequence, bool useEnd)
    {
        Span<byte> bytes = stackalloc byte[sequence.Length];
        for (int index = 0; index < sequence.Length; index++)
        {
            RegexUtf8ByteRange range = sequence[index];
            bytes[index] = useEnd ? range.End : range.Start;
        }

        OperationStatus status = Rune.DecodeFromUtf8(bytes, out Rune rune, out int consumed);
        Assert.Equal(OperationStatus.Done, status);
        Assert.Equal(bytes.Length, consumed);
        return rune.Value;
    }
}
