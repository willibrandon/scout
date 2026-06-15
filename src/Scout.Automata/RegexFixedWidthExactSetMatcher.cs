using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexFixedWidthExactSetMatcher
{
    private const int MaxWidth = 8;
    private const int MaxExpandedValues = 256;
    private const int MaxVectorPairValues = 16;

    private readonly ulong[] values;
    private readonly int width;
    private readonly bool useVectorPairs;
    private readonly Vector128<byte>[] firstPairVectors128;
    private readonly Vector128<byte>[] secondPairVectors128;
    private readonly Vector256<byte>[] firstPairVectors256;
    private readonly Vector256<byte>[] secondPairVectors256;

    private RegexFixedWidthExactSetMatcher(ulong[] values, int width)
    {
        this.values = values;
        this.width = width;
        useVectorPairs = width == 2 && values.Length <= MaxVectorPairValues;
        if (useVectorPairs)
        {
            firstPairVectors128 = new Vector128<byte>[values.Length];
            secondPairVectors128 = new Vector128<byte>[values.Length];
            firstPairVectors256 = new Vector256<byte>[values.Length];
            secondPairVectors256 = new Vector256<byte>[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                byte first = (byte)values[index];
                byte second = (byte)(values[index] >> 8);
                firstPairVectors128[index] = Vector128.Create(first);
                secondPairVectors128[index] = Vector128.Create(second);
                firstPairVectors256[index] = Vector256.Create(first);
                secondPairVectors256[index] = Vector256.Create(second);
            }
        }
        else
        {
            firstPairVectors128 = [];
            secondPairVectors128 = [];
            firstPairVectors256 = [];
            secondPairVectors256 = [];
        }
    }

    public int Width => width;

    public static bool TryCreate(
        RegexFixedWidthAtom[][] alternatives,
        int width,
        out RegexFixedWidthExactSetMatcher? matcher)
    {
        matcher = null;
        if (width <= 0 || width > MaxWidth)
        {
            return false;
        }

        var expanded = new List<ulong>();
        for (int index = 0; index < alternatives.Length; index++)
        {
            if (!TryAppendExpandedAlternative(alternatives[index], width, expanded))
            {
                return false;
            }
        }

        if (expanded.Count == 0 || expanded.Count > MaxExpandedValues)
        {
            return false;
        }

        expanded.Sort();
        int write = 0;
        for (int read = 0; read < expanded.Count; read++)
        {
            if (write == 0 || expanded[read] != expanded[write - 1])
            {
                expanded[write++] = expanded[read];
            }
        }

        expanded.RemoveRange(write, expanded.Count - write);
        matcher = new RegexFixedWidthExactSetMatcher(expanded.ToArray(), width);
        return true;
    }

    public bool Matches(ReadOnlySpan<byte> haystack, int start)
    {
        if (start > haystack.Length - width)
        {
            return false;
        }

        ulong packed = Pack(haystack, start, width);
        if (values.Length <= 8)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == packed)
                {
                    return true;
                }
            }

            return false;
        }

        return Array.BinarySearch(values, packed) >= 0;
    }

    public int Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (useVectorPairs && start <= haystack.Length - 2)
        {
            if (Avx2.IsSupported && haystack.Length - start > Vector256<byte>.Count)
            {
                return FindTwoByteVector256(haystack, start);
            }

            if (Sse2.IsSupported && haystack.Length - start > Vector128<byte>.Count)
            {
                return FindTwoByteVector128(haystack, start);
            }
        }

        return FindScalar(haystack, start);
    }

    private int FindScalar(ReadOnlySpan<byte> haystack, int start)
    {
        int end = haystack.Length - width;
        for (int index = start; index <= end; index++)
        {
            if (Matches(haystack, index))
            {
                return index;
            }
        }

        return -1;
    }

    private int FindTwoByteVector256(ReadOnlySpan<byte> haystack, int start)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = start;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            uint mask = AnyPairEqual256(current, next).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private int FindTwoByteVector128(ReadOnlySpan<byte> haystack, int start)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = start;
        int vectorEnd = haystack.Length - Vector128<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            uint mask = AnyPairEqual128(current, next).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private Vector256<byte> AnyPairEqual256(Vector256<byte> current, Vector256<byte> next)
    {
        Vector256<byte> matches = Vector256<byte>.Zero;
        for (int index = 0; index < firstPairVectors256.Length; index++)
        {
            matches = Avx2.Or(matches, Avx2.And(
                Avx2.CompareEqual(current, firstPairVectors256[index]),
                Avx2.CompareEqual(next, secondPairVectors256[index])));
        }

        return matches;
    }

    private Vector128<byte> AnyPairEqual128(Vector128<byte> current, Vector128<byte> next)
    {
        Vector128<byte> matches = Vector128<byte>.Zero;
        for (int index = 0; index < firstPairVectors128.Length; index++)
        {
            matches = Sse2.Or(matches, Sse2.And(
                Sse2.CompareEqual(current, firstPairVectors128[index]),
                Sse2.CompareEqual(next, secondPairVectors128[index])));
        }

        return matches;
    }

    private static bool TryAppendExpandedAlternative(
        RegexFixedWidthAtom[] alternative,
        int width,
        List<ulong> expanded)
    {
        var values = new List<ulong> { 0 };
        Span<byte> bytes = stackalloc byte[256];
        for (int position = 0; position < width; position++)
        {
            alternative[position].CopyMatchingBytes(bytes, out int byteCount);
            if (byteCount == 0 ||
                values.Count > MaxExpandedValues / byteCount)
            {
                return false;
            }

            int existingCount = values.Count;
            for (int valueIndex = 0; valueIndex < existingCount; valueIndex++)
            {
                ulong prefix = values[valueIndex];
                values[valueIndex] = prefix | ((ulong)bytes[0] << (position * 8));
                for (int byteIndex = 1; byteIndex < byteCount; byteIndex++)
                {
                    values.Add(prefix | ((ulong)bytes[byteIndex] << (position * 8)));
                }
            }
        }

        if (expanded.Count > MaxExpandedValues - values.Count)
        {
            return false;
        }

        expanded.AddRange(values);
        return true;
    }

    private static ulong Pack(ReadOnlySpan<byte> haystack, int start, int width)
    {
        ulong packed = 0;
        for (int index = 0; index < width; index++)
        {
            packed |= (ulong)haystack[start + index] << (index * 8);
        }

        return packed;
    }
}
