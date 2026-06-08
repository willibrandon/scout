using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Scout;

/// <summary>
/// Counts bytes using the SIMD tiers required by Scout's byte-oriented search core.
/// </summary>
public static class ByteCounter
{
    /// <summary>
    /// Counts the number of occurrences of <paramref name="needle" /> in <paramref name="haystack" />.
    /// </summary>
    /// <param name="haystack">The bytes to scan.</param>
    /// <param name="needle">The byte to count.</param>
    /// <returns>The number of matching bytes.</returns>
    public static long Count(ReadOnlySpan<byte> haystack, byte needle)
    {
        if (haystack.IsEmpty)
        {
            return 0;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return CountVector512(haystack, needle);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return CountVector256(haystack, needle);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return CountSse2(haystack, needle);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return CountAdvSimd(haystack, needle);
        }

        return CountScalar(haystack, needle, start: 0);
    }

    /// <summary>
    /// Counts one byte while locating the first occurrence of another byte in the same scan.
    /// </summary>
    /// <param name="haystack">The bytes to scan.</param>
    /// <param name="countNeedle">The byte to count.</param>
    /// <param name="findNeedle">The byte whose first offset should be found.</param>
    /// <param name="firstFound">The first offset of <paramref name="findNeedle" />, or -1 when absent.</param>
    /// <returns>The number of occurrences of <paramref name="countNeedle" />.</returns>
    public static long CountAndFindFirst(ReadOnlySpan<byte> haystack, byte countNeedle, byte findNeedle, out int firstFound)
    {
        if (haystack.IsEmpty)
        {
            firstFound = -1;
            return 0;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return CountAndFindFirstVector512(haystack, countNeedle, findNeedle, out firstFound);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return CountAndFindFirstVector256(haystack, countNeedle, findNeedle, out firstFound);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return CountAndFindFirstSse2(haystack, countNeedle, findNeedle, out firstFound);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return CountAndFindFirstAdvSimd(haystack, countNeedle, findNeedle, out firstFound);
        }

        return CountAndFindFirstScalarFromStart(haystack, countNeedle, findNeedle, start: 0, firstFound: out firstFound);
    }

    private static long CountVector512(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector512.Create(needle);
        long count = 0;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector512<byte>.Count);
        while (offset < vectorLimit)
        {
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong mask = Avx512BW.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            count += BitOperations.PopCount(mask);
            offset += Vector512<byte>.Count;
        }

        return count + CountScalar(haystack, needle, offset);
    }

    private static long CountAndFindFirstVector512(ReadOnlySpan<byte> haystack, byte countNeedle, byte findNeedle, out int firstFound)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var countVector = Vector512.Create(countNeedle);
        var findVector = Vector512.Create(findNeedle);
        firstFound = -1;
        long count = 0;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector512<byte>.Count);
        while (offset < vectorLimit)
        {
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong countMask = Avx512BW.CompareEqual(block, countVector).ExtractMostSignificantBits();
            count += BitOperations.PopCount(countMask);
            ulong findMask = Avx512BW.CompareEqual(block, findVector).ExtractMostSignificantBits();
            if (findMask != 0 && firstFound < 0)
            {
                firstFound = offset + BitOperations.TrailingZeroCount(findMask);
            }

            offset += Vector512<byte>.Count;
        }

        return count + CountAndFindFirstScalar(haystack, countNeedle, findNeedle, offset, ref firstFound);
    }

    private static long CountVector256(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector256.Create(needle);
        long count = 0;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector256<byte>.Count);
        while (offset < vectorLimit)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = Avx2.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            count += BitOperations.PopCount(mask);
            offset += Vector256<byte>.Count;
        }

        return count + CountScalar(haystack, needle, offset);
    }

    private static long CountAndFindFirstVector256(ReadOnlySpan<byte> haystack, byte countNeedle, byte findNeedle, out int firstFound)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var countVector = Vector256.Create(countNeedle);
        var findVector = Vector256.Create(findNeedle);
        firstFound = -1;
        long count = 0;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector256<byte>.Count);
        while (offset < vectorLimit)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint countMask = Avx2.CompareEqual(block, countVector).ExtractMostSignificantBits();
            count += BitOperations.PopCount(countMask);
            uint findMask = Avx2.CompareEqual(block, findVector).ExtractMostSignificantBits();
            if (findMask != 0 && firstFound < 0)
            {
                firstFound = offset + BitOperations.TrailingZeroCount(findMask);
            }

            offset += Vector256<byte>.Count;
        }

        return count + CountAndFindFirstScalar(haystack, countNeedle, findNeedle, offset, ref firstFound);
    }

    private static long CountSse2(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector128.Create(needle);
        long count = 0;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector128<byte>.Count);
        while (offset < vectorLimit)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = Sse2.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            count += BitOperations.PopCount(mask);
            offset += Vector128<byte>.Count;
        }

        return count + CountScalar(haystack, needle, offset);
    }

    private static long CountAndFindFirstSse2(ReadOnlySpan<byte> haystack, byte countNeedle, byte findNeedle, out int firstFound)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var countVector = Vector128.Create(countNeedle);
        var findVector = Vector128.Create(findNeedle);
        firstFound = -1;
        long count = 0;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector128<byte>.Count);
        while (offset < vectorLimit)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint countMask = Sse2.CompareEqual(block, countVector).ExtractMostSignificantBits();
            count += BitOperations.PopCount(countMask);
            uint findMask = Sse2.CompareEqual(block, findVector).ExtractMostSignificantBits();
            if (findMask != 0 && firstFound < 0)
            {
                firstFound = offset + BitOperations.TrailingZeroCount(findMask);
            }

            offset += Vector128<byte>.Count;
        }

        return count + CountAndFindFirstScalar(haystack, countNeedle, findNeedle, offset, ref firstFound);
    }

    private static long CountAdvSimd(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector128.Create(needle);
        Vector128<uint> counts = Vector128<uint>.Zero;
        Vector128<byte> laneCounts = Vector128<byte>.Zero;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector128<byte>.Count);
        int batchCount = 0;
        while (offset < vectorLimit)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var matches = Vector128.ShiftRightLogical(Vector128.Equals(block, needleVector), 7);
            laneCounts += matches;
            batchCount++;
            offset += Vector128<byte>.Count;
            if (batchCount == byte.MaxValue)
            {
                counts += WidenAdvSimdLaneCounts(laneCounts);
                laneCounts = Vector128<byte>.Zero;
                batchCount = 0;
            }
        }

        if (batchCount != 0)
        {
            counts += WidenAdvSimdLaneCounts(laneCounts);
        }

        long count =
            counts.GetElement(0) +
            counts.GetElement(1) +
            counts.GetElement(2) +
            counts.GetElement(3);
        return count + CountScalar(haystack, needle, offset);
    }

    private static long CountAndFindFirstAdvSimd(ReadOnlySpan<byte> haystack, byte countNeedle, byte findNeedle, out int firstFound)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var countVector = Vector128.Create(countNeedle);
        var findVector = Vector128.Create(findNeedle);
        Vector128<uint> counts = Vector128<uint>.Zero;
        Vector128<byte> laneCounts = Vector128<byte>.Zero;
        firstFound = -1;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector128<byte>.Count);
        int batchCount = 0;
        while (offset < vectorLimit)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var matches = Vector128.ShiftRightLogical(Vector128.Equals(block, countVector), 7);
            laneCounts += matches;
            uint findMask = AdvSimd.CompareEqual(block, findVector).ExtractMostSignificantBits();
            if (findMask != 0 && firstFound < 0)
            {
                firstFound = offset + BitOperations.TrailingZeroCount(findMask);
            }

            batchCount++;
            offset += Vector128<byte>.Count;
            if (batchCount == byte.MaxValue)
            {
                counts += WidenAdvSimdLaneCounts(laneCounts);
                laneCounts = Vector128<byte>.Zero;
                batchCount = 0;
            }
        }

        if (batchCount != 0)
        {
            counts += WidenAdvSimdLaneCounts(laneCounts);
        }

        long count =
            counts.GetElement(0) +
            counts.GetElement(1) +
            counts.GetElement(2) +
            counts.GetElement(3);
        return count + CountAndFindFirstScalar(haystack, countNeedle, findNeedle, offset, ref firstFound);
    }

    private static Vector128<uint> WidenAdvSimdLaneCounts(Vector128<byte> laneCounts)
    {
        Vector128<ushort> pairCounts = AdvSimd.AddPairwiseWidening(laneCounts);
        return AdvSimd.AddPairwiseWidening(pairCounts);
    }

    private static long CountScalar(ReadOnlySpan<byte> haystack, byte needle, int start)
    {
        long count = 0;
        for (int index = start; index < haystack.Length; index++)
        {
            if (haystack[index] == needle)
            {
                count++;
            }
        }

        return count;
    }

    private static long CountAndFindFirstScalarFromStart(ReadOnlySpan<byte> haystack, byte countNeedle, byte findNeedle, int start, out int firstFound)
    {
        firstFound = -1;
        return CountAndFindFirstScalar(haystack, countNeedle, findNeedle, start, ref firstFound);
    }

    private static long CountAndFindFirstScalar(ReadOnlySpan<byte> haystack, byte countNeedle, byte findNeedle, int start, ref int firstFound)
    {
        long count = 0;
        for (int index = start; index < haystack.Length; index++)
        {
            byte value = haystack[index];
            if (value == countNeedle)
            {
                count++;
            }

            if (value == findNeedle && firstFound < 0)
            {
                firstFound = index;
            }
        }

        return count;
    }
}
