using System;
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

    private static long CountAdvSimd(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector128.Create(needle);
        long count = 0;
        int offset = 0;
        int vectorLimit = haystack.Length - (haystack.Length % Vector128<byte>.Count);
        while (offset < vectorLimit)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = AdvSimd.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            count += BitOperations.PopCount(mask);
            offset += Vector128<byte>.Count;
        }

        return count + CountScalar(haystack, needle, offset);
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
}
