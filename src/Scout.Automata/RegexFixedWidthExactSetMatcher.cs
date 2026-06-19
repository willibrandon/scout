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
    private const int MaxVectorTripleValues = 16;

    private readonly ulong[] values;
    private readonly int width;
    private readonly bool useVectorPair3Plus1;
    private readonly bool useVectorPairs;
    private readonly bool useVectorTriples;
    private readonly Vector128<byte> groupedPairFirstVector128;
    private readonly Vector128<byte> groupedPairSecond0Vector128;
    private readonly Vector128<byte> groupedPairSecond1Vector128;
    private readonly Vector128<byte> groupedPairSecond2Vector128;
    private readonly Vector128<byte> singletonPairFirstVector128;
    private readonly Vector128<byte> singletonPairSecondVector128;
    private readonly Vector256<byte> groupedPairFirstVector256;
    private readonly Vector256<byte> groupedPairSecond0Vector256;
    private readonly Vector256<byte> groupedPairSecond1Vector256;
    private readonly Vector256<byte> groupedPairSecond2Vector256;
    private readonly Vector256<byte> singletonPairFirstVector256;
    private readonly Vector256<byte> singletonPairSecondVector256;
    private readonly Vector128<byte>[] firstPairVectors128;
    private readonly Vector128<byte>[] secondPairVectors128;
    private readonly Vector256<byte>[] firstPairVectors256;
    private readonly Vector256<byte>[] secondPairVectors256;
    private readonly Vector128<byte>[] firstTripleVectors128;
    private readonly Vector128<byte>[] secondTripleVectors128;
    private readonly Vector128<byte>[] thirdTripleVectors128;
    private readonly Vector256<byte>[] firstTripleVectors256;
    private readonly Vector256<byte>[] secondTripleVectors256;
    private readonly Vector256<byte>[] thirdTripleVectors256;

    private RegexFixedWidthExactSetMatcher(ulong[] values, int width)
    {
        this.values = values;
        this.width = width;
        byte groupedPairFirst = 0;
        byte groupedPairSecond0 = 0;
        byte groupedPairSecond1 = 0;
        byte groupedPairSecond2 = 0;
        byte singletonPairFirst = 0;
        byte singletonPairSecond = 0;
        useVectorPair3Plus1 = width == 2 &&
            TryGetGrouped3Plus1PairValues(
                values,
                out groupedPairFirst,
                out groupedPairSecond0,
                out groupedPairSecond1,
                out groupedPairSecond2,
                out singletonPairFirst,
                out singletonPairSecond);
        useVectorPairs = width == 2 && !useVectorPair3Plus1 && values.Length <= MaxVectorPairValues;
        useVectorTriples = width == 3 && values.Length <= MaxVectorTripleValues;
        if (useVectorPair3Plus1)
        {
            groupedPairFirstVector128 = Vector128.Create(groupedPairFirst);
            groupedPairSecond0Vector128 = Vector128.Create(groupedPairSecond0);
            groupedPairSecond1Vector128 = Vector128.Create(groupedPairSecond1);
            groupedPairSecond2Vector128 = Vector128.Create(groupedPairSecond2);
            singletonPairFirstVector128 = Vector128.Create(singletonPairFirst);
            singletonPairSecondVector128 = Vector128.Create(singletonPairSecond);
            groupedPairFirstVector256 = Vector256.Create(groupedPairFirst);
            groupedPairSecond0Vector256 = Vector256.Create(groupedPairSecond0);
            groupedPairSecond1Vector256 = Vector256.Create(groupedPairSecond1);
            groupedPairSecond2Vector256 = Vector256.Create(groupedPairSecond2);
            singletonPairFirstVector256 = Vector256.Create(singletonPairFirst);
            singletonPairSecondVector256 = Vector256.Create(singletonPairSecond);
            firstPairVectors128 = [];
            secondPairVectors128 = [];
            firstPairVectors256 = [];
            secondPairVectors256 = [];
            firstTripleVectors128 = [];
            secondTripleVectors128 = [];
            thirdTripleVectors128 = [];
            firstTripleVectors256 = [];
            secondTripleVectors256 = [];
            thirdTripleVectors256 = [];
        }
        else if (useVectorPairs)
        {
            groupedPairFirstVector128 = default;
            groupedPairSecond0Vector128 = default;
            groupedPairSecond1Vector128 = default;
            groupedPairSecond2Vector128 = default;
            singletonPairFirstVector128 = default;
            singletonPairSecondVector128 = default;
            groupedPairFirstVector256 = default;
            groupedPairSecond0Vector256 = default;
            groupedPairSecond1Vector256 = default;
            groupedPairSecond2Vector256 = default;
            singletonPairFirstVector256 = default;
            singletonPairSecondVector256 = default;
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

            firstTripleVectors128 = [];
            secondTripleVectors128 = [];
            thirdTripleVectors128 = [];
            firstTripleVectors256 = [];
            secondTripleVectors256 = [];
            thirdTripleVectors256 = [];
        }
        else if (useVectorTriples)
        {
            groupedPairFirstVector128 = default;
            groupedPairSecond0Vector128 = default;
            groupedPairSecond1Vector128 = default;
            groupedPairSecond2Vector128 = default;
            singletonPairFirstVector128 = default;
            singletonPairSecondVector128 = default;
            groupedPairFirstVector256 = default;
            groupedPairSecond0Vector256 = default;
            groupedPairSecond1Vector256 = default;
            groupedPairSecond2Vector256 = default;
            singletonPairFirstVector256 = default;
            singletonPairSecondVector256 = default;
            firstPairVectors128 = [];
            secondPairVectors128 = [];
            firstPairVectors256 = [];
            secondPairVectors256 = [];
            firstTripleVectors128 = new Vector128<byte>[values.Length];
            secondTripleVectors128 = new Vector128<byte>[values.Length];
            thirdTripleVectors128 = new Vector128<byte>[values.Length];
            firstTripleVectors256 = new Vector256<byte>[values.Length];
            secondTripleVectors256 = new Vector256<byte>[values.Length];
            thirdTripleVectors256 = new Vector256<byte>[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                byte first = (byte)values[index];
                byte second = (byte)(values[index] >> 8);
                byte third = (byte)(values[index] >> 16);
                firstTripleVectors128[index] = Vector128.Create(first);
                secondTripleVectors128[index] = Vector128.Create(second);
                thirdTripleVectors128[index] = Vector128.Create(third);
                firstTripleVectors256[index] = Vector256.Create(first);
                secondTripleVectors256[index] = Vector256.Create(second);
                thirdTripleVectors256[index] = Vector256.Create(third);
            }
        }
        else
        {
            groupedPairFirstVector128 = default;
            groupedPairSecond0Vector128 = default;
            groupedPairSecond1Vector128 = default;
            groupedPairSecond2Vector128 = default;
            singletonPairFirstVector128 = default;
            singletonPairSecondVector128 = default;
            groupedPairFirstVector256 = default;
            groupedPairSecond0Vector256 = default;
            groupedPairSecond1Vector256 = default;
            groupedPairSecond2Vector256 = default;
            singletonPairFirstVector256 = default;
            singletonPairSecondVector256 = default;
            firstPairVectors128 = [];
            secondPairVectors128 = [];
            firstPairVectors256 = [];
            secondPairVectors256 = [];
            firstTripleVectors128 = [];
            secondTripleVectors128 = [];
            thirdTripleVectors128 = [];
            firstTripleVectors256 = [];
            secondTripleVectors256 = [];
            thirdTripleVectors256 = [];
        }
    }

    public int Width => width;

    public bool CanFindVectorized => useVectorPair3Plus1 || useVectorPairs || useVectorTriples;

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
        if (useVectorPair3Plus1 && start <= haystack.Length - 2)
        {
            if (Avx2.IsSupported && haystack.Length - start > Vector256<byte>.Count)
            {
                return FindTwoByteGrouped3Plus1Vector256(haystack, start);
            }

            if (Sse2.IsSupported && haystack.Length - start > Vector128<byte>.Count)
            {
                return FindTwoByteGrouped3Plus1Vector128(haystack, start);
            }
        }

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

        if (useVectorTriples && start <= haystack.Length - 3)
        {
            if (Avx2.IsSupported && haystack.Length - start > Vector256<byte>.Count + 1)
            {
                return FindThreeByteVector256(haystack, start);
            }

            if (Sse2.IsSupported && haystack.Length - start > Vector128<byte>.Count + 1)
            {
                return FindThreeByteVector128(haystack, start);
            }
        }

        return FindScalar(haystack, start);
    }

    private int FindTwoByteGrouped3Plus1Vector256(ReadOnlySpan<byte> haystack, int start)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = start;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            uint mask = AnyGrouped3Plus1PairEqual256(current, next).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private int FindTwoByteGrouped3Plus1Vector128(ReadOnlySpan<byte> haystack, int start)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = start;
        int vectorEnd = haystack.Length - Vector128<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            uint mask = AnyGrouped3Plus1PairEqual128(current, next).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalar(haystack, offset);
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

    private int FindThreeByteVector256(ReadOnlySpan<byte> haystack, int start)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = start;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - 2;
        while (offset <= vectorEnd)
        {
            var current = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            var third = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 2));
            uint mask = AnyTripleEqual256(current, next, third).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private int FindThreeByteVector128(ReadOnlySpan<byte> haystack, int start)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = start;
        int vectorEnd = haystack.Length - Vector128<byte>.Count - 2;
        while (offset <= vectorEnd)
        {
            var current = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            var third = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 2));
            uint mask = AnyTripleEqual128(current, next, third).ExtractMostSignificantBits();
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

    private Vector256<byte> AnyGrouped3Plus1PairEqual256(Vector256<byte> current, Vector256<byte> next)
    {
        Vector256<byte> groupedSecondMatches = Avx2.Or(
            Avx2.Or(
                Avx2.CompareEqual(next, groupedPairSecond0Vector256),
                Avx2.CompareEqual(next, groupedPairSecond1Vector256)),
            Avx2.CompareEqual(next, groupedPairSecond2Vector256));
        Vector256<byte> groupedMatches = Avx2.And(
            Avx2.CompareEqual(current, groupedPairFirstVector256),
            groupedSecondMatches);
        Vector256<byte> singletonMatches = Avx2.And(
            Avx2.CompareEqual(current, singletonPairFirstVector256),
            Avx2.CompareEqual(next, singletonPairSecondVector256));
        return Avx2.Or(groupedMatches, singletonMatches);
    }

    private Vector128<byte> AnyGrouped3Plus1PairEqual128(Vector128<byte> current, Vector128<byte> next)
    {
        Vector128<byte> groupedSecondMatches = Sse2.Or(
            Sse2.Or(
                Sse2.CompareEqual(next, groupedPairSecond0Vector128),
                Sse2.CompareEqual(next, groupedPairSecond1Vector128)),
            Sse2.CompareEqual(next, groupedPairSecond2Vector128));
        Vector128<byte> groupedMatches = Sse2.And(
            Sse2.CompareEqual(current, groupedPairFirstVector128),
            groupedSecondMatches);
        Vector128<byte> singletonMatches = Sse2.And(
            Sse2.CompareEqual(current, singletonPairFirstVector128),
            Sse2.CompareEqual(next, singletonPairSecondVector128));
        return Sse2.Or(groupedMatches, singletonMatches);
    }

    private Vector256<byte> AnyTripleEqual256(Vector256<byte> current, Vector256<byte> next, Vector256<byte> third)
    {
        Vector256<byte> matches = Vector256<byte>.Zero;
        for (int index = 0; index < firstTripleVectors256.Length; index++)
        {
            Vector256<byte> firstSecondMatches = Avx2.And(
                Avx2.CompareEqual(current, firstTripleVectors256[index]),
                Avx2.CompareEqual(next, secondTripleVectors256[index]));
            matches = Avx2.Or(matches, Avx2.And(
                firstSecondMatches,
                Avx2.CompareEqual(third, thirdTripleVectors256[index])));
        }

        return matches;
    }

    private Vector128<byte> AnyTripleEqual128(Vector128<byte> current, Vector128<byte> next, Vector128<byte> third)
    {
        Vector128<byte> matches = Vector128<byte>.Zero;
        for (int index = 0; index < firstTripleVectors128.Length; index++)
        {
            Vector128<byte> firstSecondMatches = Sse2.And(
                Sse2.CompareEqual(current, firstTripleVectors128[index]),
                Sse2.CompareEqual(next, secondTripleVectors128[index]));
            matches = Sse2.Or(matches, Sse2.And(
                firstSecondMatches,
                Sse2.CompareEqual(third, thirdTripleVectors128[index])));
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

    private static bool TryGetGrouped3Plus1PairValues(
        ulong[] values,
        out byte groupedFirst,
        out byte groupedSecond0,
        out byte groupedSecond1,
        out byte groupedSecond2,
        out byte singletonFirst,
        out byte singletonSecond)
    {
        groupedFirst = 0;
        groupedSecond0 = 0;
        groupedSecond1 = 0;
        groupedSecond2 = 0;
        singletonFirst = 0;
        singletonSecond = 0;
        if (values.Length != 4)
        {
            return false;
        }

        byte first0 = (byte)values[0];
        byte first1 = (byte)values[1];
        byte first2 = (byte)values[2];
        byte first3 = (byte)values[3];
        byte distinct0 = first0;
        byte distinct1 = 0;
        int distinctCount = 1;
        if (first1 != distinct0)
        {
            distinct1 = first1;
            distinctCount = 2;
        }

        if (first2 != distinct0 && (distinctCount == 1 || first2 != distinct1))
        {
            if (distinctCount == 2)
            {
                return false;
            }

            distinct1 = first2;
            distinctCount = 2;
        }

        if (first3 != distinct0 && (distinctCount == 1 || first3 != distinct1))
        {
            return false;
        }

        if (distinctCount != 2)
        {
            return false;
        }

        int count0 = 0;
        count0 += first0 == distinct0 ? 1 : 0;
        count0 += first1 == distinct0 ? 1 : 0;
        count0 += first2 == distinct0 ? 1 : 0;
        count0 += first3 == distinct0 ? 1 : 0;
        if (count0 != 1 && count0 != 3)
        {
            return false;
        }

        groupedFirst = count0 == 3 ? distinct0 : distinct1;
        singletonFirst = count0 == 1 ? distinct0 : distinct1;

        int groupWrite = 0;
        for (int index = 0; index < values.Length; index++)
        {
            byte first = (byte)values[index];
            byte second = (byte)(values[index] >> 8);
            if (first == groupedFirst)
            {
                if (groupWrite == 0)
                {
                    groupedSecond0 = second;
                }
                else if (groupWrite == 1)
                {
                    groupedSecond1 = second;
                }
                else
                {
                    groupedSecond2 = second;
                }

                groupWrite++;
            }
            else
            {
                singletonSecond = second;
            }
        }

        return groupWrite == 3;
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
