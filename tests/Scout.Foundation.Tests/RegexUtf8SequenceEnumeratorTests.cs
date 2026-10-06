using System.Buffers;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies ordered UTF-8 sequence decomposition for minimized Unicode-class compilation.
/// </summary>
[TestClass]
public sealed class RegexUtf8SequenceEnumeratorTests()
{
    /// <summary>
    /// Verifies sequences cover every valid boundary in lexical order without crossing the surrogate gap.
    /// </summary>
    [TestMethod]
    public void EmitsOrderedDisjointSequencesAcrossScalarBoundaries()
    {
        var enumerator = new RegexUtf8SequenceEnumerator(0x7F, 0x10FFFF);
        var sequences = new List<RegexUtf8ByteSequence>();

        while (enumerator.MoveNext(out RegexUtf8ByteSequence sequence))
        {
            sequences.Add(sequence);
        }

        Assert.IsNotEmpty(sequences);
        int previousEnd = 0x7E;
        for (int sequenceIndex = 0; sequenceIndex < sequences.Count; sequenceIndex++)
        {
            RegexUtf8ByteSequence sequence = sequences[sequenceIndex];
            Assert.IsInRange(1, 4, sequence.Length);
            for (int rangeIndex = 0; rangeIndex < sequence.Length; rangeIndex++)
            {
                RegexUtf8ByteRange range = sequence[rangeIndex];
                Assert.IsLessThanOrEqualTo(range.End, range.Start);
            }

            int start = DecodeBoundary(sequence, useEnd: false);
            int end = DecodeBoundary(sequence, useEnd: true);
            int expectedStart = previousEnd == 0xD7FF ? 0xE000 : previousEnd + 1;
            Assert.AreEqual(expectedStart, start);
            Assert.IsLessThanOrEqualTo(end, start);
            if (sequenceIndex > 0)
            {
                Assert.IsLessThan(0, CompareBoundaries(
                    sequences[sequenceIndex - 1],
                    leftUseEnd: true,
                    sequence,
                    rightUseEnd: false));
            }

            previousEnd = end;
        }

        Assert.AreEqual(0x10FFFF, previousEnd);
    }

    /// <summary>
    /// Verifies unaligned ranges remain contiguous while crossing UTF-8 and surrogate boundaries.
    /// </summary>
    /// <param name="start">The first valid scalar.</param>
    /// <param name="end">The last valid scalar.</param>
    [TestMethod]
    [DataRow(0x01, 0x7E)]
    [DataRow(0x81, 0x7FE)]
    [DataRow(0x801, 0xD7FE)]
    [DataRow(0xD7F0, 0xE010)]
    [DataRow(0xE001, 0xFFFE)]
    [DataRow(0x10001, 0x10FFFE)]
    [DataRow(0x81, 0x10FFFE)]
    public void EmitsUnalignedScalarRanges(int start, int end)
    {
        var enumerator = new RegexUtf8SequenceEnumerator(start, end);
        var sequences = new List<RegexUtf8ByteSequence>();

        while (enumerator.MoveNext(out RegexUtf8ByteSequence sequence))
        {
            sequences.Add(sequence);
        }

        Assert.IsNotEmpty(sequences);
        int previousEnd = start - 1;
        for (int sequenceIndex = 0; sequenceIndex < sequences.Count; sequenceIndex++)
        {
            RegexUtf8ByteSequence sequence = sequences[sequenceIndex];
            int sequenceStart = DecodeBoundary(sequence, useEnd: false);
            int sequenceEnd = DecodeBoundary(sequence, useEnd: true);
            int expectedStart = previousEnd == 0xD7FF ? 0xE000 : previousEnd + 1;

            Assert.AreEqual(expectedStart, sequenceStart);
            Assert.IsLessThanOrEqualTo(sequenceEnd, sequenceStart);
            previousEnd = sequenceEnd;
        }

        Assert.AreEqual(end, previousEnd);
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
        Assert.AreEqual(OperationStatus.Done, status);
        Assert.AreEqual(bytes.Length, consumed);
        return rune.Value;
    }
}
