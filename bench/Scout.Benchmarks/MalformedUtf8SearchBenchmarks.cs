using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Compares direct malformed UTF-8 matching with an expanded-copy and offset-map reference.
/// </summary>
[MemoryDiagnoser]
public class MalformedUtf8SearchBenchmarks
{
    private byte[] _haystack = [];
    private ByteRegex _directRegex = null!;
    private ByteRegex _expandedRegex = null!;

    /// <summary>
    /// Gets or sets the synthetic input size in bytes.
    /// </summary>
    [Params(1 * 1024 * 1024, 8 * 1024 * 1024)]
    public int Size { get; set; }

    /// <summary>
    /// Creates deterministic mixed valid and malformed UTF-8 input and reusable regexes.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _haystack = CreateHaystack(Size);
        _directRegex = ByteRegex.Compile(
            @"\u{FFFD}+",
            new ByteRegexOptions { MatchInvalidUtf8 = true });
        _expandedRegex = ByteRegex.Compile(@"\u{FFFD}+");
    }

    /// <summary>
    /// Expands malformed bytes to encoded U+FFFD, builds an original-offset map, and finds the first match.
    /// </summary>
    /// <returns>The mapped original match span packed into one value.</returns>
    [Benchmark(Baseline = true)]
    public long ExpandedCopyAndOffsetMap()
    {
        byte[] expanded = GC.AllocateUninitializedArray<byte>(checked(_haystack.Length * 3));
        int[] originalOffsets = GC.AllocateUninitializedArray<int>(checked(expanded.Length + 1));
        int expandedLength = ExpandInvalidUtf8(_haystack, expanded, originalOffsets);
        ByteRegexMatch? match = _expandedRegex.Find(expanded.AsSpan(0, expandedLength));
        if (!match.HasValue)
        {
            return -1;
        }

        int originalStart = originalOffsets[match.Value.Start];
        int originalEnd = originalOffsets[match.Value.End];
        return Pack(originalStart, originalEnd - originalStart);
    }

    /// <summary>
    /// Finds malformed bytes directly in the original input.
    /// </summary>
    /// <returns>The original match span packed into one value.</returns>
    [Benchmark]
    public long DirectOriginalBytes()
    {
        ByteRegexMatch? match = _directRegex.Find(_haystack);
        return match.HasValue ? Pack(match.Value.Start, match.Value.Length) : -1;
    }

    private static byte[] CreateHaystack(int size)
    {
        byte[] bytes = GC.AllocateUninitializedArray<byte>(size);
        uint state = 0x56;
        for (int index = 0; index < bytes.Length; index++)
        {
            state = unchecked((state * 1_664_525) + 1_013_904_223);
            bytes[index] = (byte)('a' + ((state >> 24) % 26));
        }

        const int blockStride = 64 * 1024;
        const int malformedBlockLength = 16 * 1024;
        for (int blockStart = blockStride / 2;
             blockStart + malformedBlockLength < bytes.Length;
             blockStart += blockStride)
        {
            bytes.AsSpan(blockStart, malformedBlockLength).Fill(0xFF);
            for (int index = blockStart; index + 3 < blockStart + malformedBlockLength; index += 257)
            {
                bytes[index] = 0xEF;
                bytes[index + 1] = 0xBF;
                bytes[index + 2] = 0xBD;
            }
        }

        return bytes;
    }

    private static int ExpandInvalidUtf8(
        ReadOnlySpan<byte> input,
        Span<byte> output,
        Span<int> originalOffsets)
    {
        int read = 0;
        int written = 0;
        originalOffsets[0] = 0;
        while (read < input.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf8(input[read..], out _, out int consumed);
            if (status == OperationStatus.Done)
            {
                input.Slice(read, consumed).CopyTo(output[written..]);
                for (int index = 0; index < consumed; index++)
                {
                    originalOffsets[written + index] = read + index;
                }

                read += consumed;
                written += consumed;
                originalOffsets[written] = read;
                continue;
            }

            output[written] = 0xEF;
            output[written + 1] = 0xBF;
            output[written + 2] = 0xBD;
            originalOffsets[written] = read;
            originalOffsets[written + 1] = read;
            originalOffsets[written + 2] = read;
            read++;
            written += 3;
            originalOffsets[written] = read;
        }

        return written;
    }

    private static long Pack(int start, int length)
    {
        return ((long)start << 32) | (uint)length;
    }
}
