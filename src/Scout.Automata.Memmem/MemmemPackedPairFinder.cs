using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal readonly struct MemmemPackedPairFinder
{
    private const int MinimumNeedleLength = 3;
    private const int MinimumHaystackLength = 256;

    private readonly byte first;
    private readonly byte second;
    private readonly int firstIndex;
    private readonly int secondIndex;
    private readonly int maxIndex;

    private MemmemPackedPairFinder(byte first, byte second, int firstIndex, int secondIndex)
    {
        this.first = first;
        this.second = second;
        this.firstIndex = firstIndex;
        this.secondIndex = secondIndex;
        maxIndex = Math.Max(firstIndex, secondIndex);
    }

    public static bool TryCreate(ReadOnlySpan<byte> needle, out MemmemPackedPairFinder finder)
    {
        finder = default;
        if (needle.Length < MinimumNeedleLength)
        {
            return false;
        }

        int firstIndex = 0;
        int secondIndex = 1;
        byte first = needle[firstIndex];
        byte second = needle[secondIndex];
        int firstRank = Rank(first);
        int secondRank = Rank(second);
        if (secondRank < firstRank)
        {
            (first, second) = (second, first);
            (firstIndex, secondIndex) = (secondIndex, firstIndex);
            (firstRank, secondRank) = (secondRank, firstRank);
        }

        int length = Math.Min(needle.Length, byte.MaxValue + 1);
        for (int index = 2; index < length; index++)
        {
            byte candidate = needle[index];
            int candidateRank = Rank(candidate);
            if (candidateRank < firstRank)
            {
                second = first;
                secondIndex = firstIndex;
                secondRank = firstRank;
                first = candidate;
                firstIndex = index;
                firstRank = candidateRank;
            }
            else if (candidateRank < secondRank)
            {
                second = candidate;
                secondIndex = index;
                secondRank = candidateRank;
            }
        }

        finder = new MemmemPackedPairFinder(first, second, firstIndex, secondIndex);
        return true;
    }

    public int Find(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length > haystack.Length)
        {
            return -1;
        }

        if (haystack.Length < MinimumHaystackLength)
        {
            return MemmemSearch.FindWithoutPackedPair(haystack, needle);
        }

        if (!IsAdjacentPair() &&
            Avx512BW.IsSupported &&
            haystack.Length >= MinimumLengthForVector(Vector512<byte>.Count, needle.Length))
        {
            return FindVector512(haystack, needle);
        }

        if (Avx2.IsSupported && haystack.Length >= MinimumLengthForVector(Vector256<byte>.Count, needle.Length))
        {
            return FindVector256(haystack, needle);
        }

        if (Sse2.IsSupported && haystack.Length >= MinimumLengthForVector(Vector128<byte>.Count, needle.Length))
        {
            return FindSse2(haystack, needle);
        }

        if (AdvSimd.IsSupported && haystack.Length >= MinimumLengthForVector(Vector128<byte>.Count, needle.Length))
        {
            return FindAdvSimd(haystack, needle);
        }

        if (IsAdjacentPair() && BitConverter.IsLittleEndian)
        {
            return FindAdjacentPairScalar64(haystack, needle);
        }

        return MemmemSearch.FindWithoutPackedPair(haystack, needle);
    }

    private int MinimumLengthForVector(int vectorLength, int needleLength)
    {
        return Math.Max(needleLength, maxIndex + vectorLength);
    }

    private bool IsAdjacentPair()
    {
        return AreAdjacentPair(firstIndex, secondIndex);
    }

    private static bool AreAdjacentPair(int left, int right)
    {
        return Math.Abs(left - right) == 1;
    }

    private int FindAdjacentPairScalar64(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int pairStart = Math.Min(firstIndex, secondIndex);
        byte low = firstIndex < secondIndex ? first : second;
        byte high = firstIndex < secondIndex ? second : first;
        ulong lowPattern = RepeatByte(low);
        ulong highPattern = RepeatByte(high);
        int offset = 0;
        int scalarEnd = haystack.Length - sizeof(ulong);
        int lastStart = haystack.Length - needle.Length;
        while (offset <= scalarEnd)
        {
            ulong block = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref reference, offset));
            ulong lowMatches = ZeroByteMask(block ^ lowPattern);
            ulong highMatches = ZeroByteMask((block >> 8) ^ highPattern);
            ulong mask = lowMatches & highMatches & 0x0080_8080_8080_8080UL;
            while (mask != 0)
            {
                int pairOffset = offset + (BitOperations.TrailingZeroCount(mask) >> 3);
                int candidate = pairOffset - pairStart;
                if (candidate < 0)
                {
                    mask &= mask - 1;
                    continue;
                }

                if (candidate > lastStart)
                {
                    return -1;
                }

                if (haystack.Slice(candidate, needle.Length).SequenceEqual(needle))
                {
                    return candidate;
                }

                mask &= mask - 1;
            }

            offset += sizeof(ulong) - 1;
        }

        return FindScalarPair(haystack, needle, Math.Max(0, offset - pairStart));
    }

    private int FindVector512(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector512.Create(first);
        var secondVector = Vector512.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - MinimumLengthForVector(Vector512<byte>.Count, needle.Length);
        int lastStart = haystack.Length - needle.Length;
        int unrolledEnd = vectorEnd - Vector512<byte>.Count;
        while (offset <= unrolledEnd)
        {
            if (TryFindVector512Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset,
                    lastStart,
                    out int candidate) ||
                TryFindVector512Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset + Vector512<byte>.Count,
                    lastStart,
                    out candidate))
            {
                return candidate;
            }

            offset += Vector512<byte>.Count * 2;
        }

        while (offset <= vectorEnd)
        {
            if (TryFindVector512Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset,
                    lastStart,
                    out int candidate))
            {
                return candidate;
            }

            offset += Vector512<byte>.Count;
        }

        return FindScalarPair(haystack, needle, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryFindVector512Chunk(
        ref byte reference,
        Vector512<byte> firstVector,
        Vector512<byte> secondVector,
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        int offset,
        int lastStart,
        out int match)
    {
        var firstBlock = Vector512.LoadUnsafe(ref reference, (nuint)(offset + firstIndex));
        var secondBlock = Vector512.LoadUnsafe(ref reference, (nuint)(offset + secondIndex));
        ulong mask =
            (Avx512BW.CompareEqual(firstBlock, firstVector) &
             Avx512BW.CompareEqual(secondBlock, secondVector)).ExtractMostSignificantBits();
        while (mask != 0)
        {
            int candidate = offset + BitOperations.TrailingZeroCount(mask);
            if (candidate > lastStart)
            {
                match = -1;
                return true;
            }

            if (haystack.Slice(candidate, needle.Length).SequenceEqual(needle))
            {
                match = candidate;
                return true;
            }

            mask &= mask - 1;
        }

        match = -1;
        return false;
    }

    private int FindVector256(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector256.Create(first);
        var secondVector = Vector256.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - MinimumLengthForVector(Vector256<byte>.Count, needle.Length);
        int lastStart = haystack.Length - needle.Length;
        int unrolledEnd = vectorEnd - Vector256<byte>.Count * 3;
        while (offset <= unrolledEnd)
        {
            if (TryFindVector256Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset,
                    lastStart,
                    out int candidate) ||
                TryFindVector256Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset + Vector256<byte>.Count,
                    lastStart,
                    out candidate) ||
                TryFindVector256Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset + Vector256<byte>.Count * 2,
                    lastStart,
                    out candidate) ||
                TryFindVector256Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset + Vector256<byte>.Count * 3,
                    lastStart,
                    out candidate))
            {
                return candidate;
            }

            offset += Vector256<byte>.Count * 4;
        }

        while (offset <= vectorEnd)
        {
            if (TryFindVector256Chunk(
                    ref reference,
                    firstVector,
                    secondVector,
                    haystack,
                    needle,
                    offset,
                    lastStart,
                    out int candidate))
            {
                return candidate;
            }

            offset += Vector256<byte>.Count;
        }

        return FindScalarPair(haystack, needle, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryFindVector256Chunk(
        ref byte reference,
        Vector256<byte> firstVector,
        Vector256<byte> secondVector,
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        int offset,
        int lastStart,
        out int match)
    {
        var firstBlock = Vector256.LoadUnsafe(ref reference, (nuint)(offset + firstIndex));
        var secondBlock = Vector256.LoadUnsafe(ref reference, (nuint)(offset + secondIndex));
        uint mask =
            (Avx2.CompareEqual(firstBlock, firstVector) &
             Avx2.CompareEqual(secondBlock, secondVector)).ExtractMostSignificantBits();
        while (mask != 0)
        {
            int candidate = offset + BitOperations.TrailingZeroCount(mask);
            if (candidate > lastStart)
            {
                match = -1;
                return true;
            }

            if (haystack.Slice(candidate, needle.Length).SequenceEqual(needle))
            {
                match = candidate;
                return true;
            }

            mask &= mask - 1;
        }

        match = -1;
        return false;
    }

    private int FindSse2(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - MinimumLengthForVector(Vector128<byte>.Count, needle.Length);
        int lastStart = haystack.Length - needle.Length;
        while (offset <= vectorEnd)
        {
            var firstBlock = Vector128.LoadUnsafe(ref reference, (nuint)(offset + firstIndex));
            var secondBlock = Vector128.LoadUnsafe(ref reference, (nuint)(offset + secondIndex));
            uint mask =
                (Sse2.CompareEqual(firstBlock, firstVector) &
                 Sse2.CompareEqual(secondBlock, secondVector)).ExtractMostSignificantBits();
            while (mask != 0)
            {
                int candidate = offset + BitOperations.TrailingZeroCount(mask);
                if (candidate > lastStart)
                {
                    return -1;
                }

                if (haystack.Slice(candidate, needle.Length).SequenceEqual(needle))
                {
                    return candidate;
                }

                mask &= mask - 1;
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalarPair(haystack, needle, offset);
    }

    private int FindAdvSimd(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - MinimumLengthForVector(Vector128<byte>.Count, needle.Length);
        int lastStart = haystack.Length - needle.Length;
        while (offset <= vectorEnd)
        {
            var firstBlock = Vector128.LoadUnsafe(ref reference, (nuint)(offset + firstIndex));
            var secondBlock = Vector128.LoadUnsafe(ref reference, (nuint)(offset + secondIndex));
            uint mask =
                (AdvSimd.CompareEqual(firstBlock, firstVector) &
                 AdvSimd.CompareEqual(secondBlock, secondVector)).ExtractMostSignificantBits();
            while (mask != 0)
            {
                int candidate = offset + BitOperations.TrailingZeroCount(mask);
                if (candidate > lastStart)
                {
                    return -1;
                }

                if (haystack.Slice(candidate, needle.Length).SequenceEqual(needle))
                {
                    return candidate;
                }

                mask &= mask - 1;
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalarPair(haystack, needle, offset);
    }

    private int FindScalarPair(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int start)
    {
        int lastStart = haystack.Length - needle.Length;
        for (int candidate = start; candidate <= lastStart; candidate++)
        {
            if (haystack[candidate + firstIndex] == first &&
                haystack[candidate + secondIndex] == second &&
                haystack.Slice(candidate, needle.Length).SequenceEqual(needle))
            {
                return candidate;
            }
        }

        return -1;
    }

    private static ulong RepeatByte(byte value)
    {
        return value * 0x0101_0101_0101_0101UL;
    }

    private static ulong ZeroByteMask(ulong value)
    {
        return (value - 0x0101_0101_0101_0101UL) & ~value & 0x8080_8080_8080_8080UL;
    }

    private static int Rank(byte value)
    {
        return DefaultRank[value];
    }

    private static ReadOnlySpan<byte> DefaultRank =>
    [
        55, 52, 51, 50, 49, 48, 47, 46, 45, 103, 242, 66, 67, 229, 44, 43,
        42, 41, 40, 39, 38, 37, 36, 35, 34, 33, 56, 32, 31, 30, 29, 28,
        255, 148, 164, 149, 136, 160, 155, 173, 221, 222, 134, 122, 232, 202, 215, 224,
        208, 220, 204, 187, 183, 179, 177, 168, 178, 200, 226, 195, 154, 184, 174, 126,
        120, 191, 157, 194, 170, 189, 162, 161, 150, 193, 142, 137, 171, 176, 185, 167,
        186, 112, 175, 192, 188, 156, 140, 143, 123, 133, 128, 147, 138, 146, 114, 223,
        151, 249, 216, 238, 236, 253, 227, 218, 230, 247, 135, 180, 241, 233, 246, 244,
        231, 139, 245, 243, 251, 235, 201, 196, 240, 214, 152, 182, 205, 181, 127, 27,
        212, 211, 210, 213, 228, 197, 169, 159, 131, 172, 105, 80, 98, 96, 97, 81,
        207, 145, 116, 115, 144, 130, 153, 121, 107, 132, 109, 110, 124, 111, 82, 108,
        118, 141, 113, 129, 119, 125, 165, 117, 92, 106, 83, 72, 99, 93, 65, 79,
        166, 237, 163, 199, 190, 225, 209, 203, 198, 217, 219, 206, 234, 248, 158, 239,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    ];
}
