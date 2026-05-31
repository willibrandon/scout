using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Scout;

/// <summary>
/// Provides byte-oriented search helpers corresponding to the single-byte
/// search surface of Rust's <c>memchr</c> crate.
/// </summary>
public static class MemchrSearch
{
    /// <summary>
    /// Finds the first occurrence of a byte.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the byte is absent.</returns>
    public static int Find(ReadOnlySpan<byte> haystack, byte needle)
    {
        if (haystack.IsEmpty)
        {
            return -1;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return FindVector512(haystack, needle);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return FindVector256(haystack, needle);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return FindSse2(haystack, needle);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return FindAdvSimd(haystack, needle);
        }

        return FindScalar(haystack, needle, start: 0);
    }

    /// <summary>
    /// Finds every occurrence of a byte in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte to find.</param>
    /// <returns>The zero-based indexes of every occurrence.</returns>
    public static IReadOnlyList<int> FindAll(ReadOnlySpan<byte> haystack, byte needle)
    {
        var matches = new List<int>();
        MemchrEnumerator enumerator = Enumerate(haystack, needle);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every occurrence of a byte in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte to find.</param>
    /// <returns>An allocation-free byte offset enumerator.</returns>
    public static MemchrEnumerator Enumerate(ReadOnlySpan<byte> haystack, byte needle)
    {
        return new MemchrEnumerator(haystack, needle, reverse: false);
    }

    /// <summary>
    /// Finds the last occurrence of a byte.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the byte is absent.</returns>
    public static int FindReverse(ReadOnlySpan<byte> haystack, byte needle)
    {
        if (haystack.IsEmpty)
        {
            return -1;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return FindReverseVector512(haystack, needle);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return FindReverseVector256(haystack, needle);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return FindReverseSse2(haystack, needle);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return FindReverseAdvSimd(haystack, needle);
        }

        return FindReverseScalar(haystack, needle, haystack.Length - 1);
    }

    /// <summary>
    /// Finds every occurrence of a byte in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte to find.</param>
    /// <returns>The zero-based indexes of every occurrence in descending order.</returns>
    public static IReadOnlyList<int> FindAllReverse(ReadOnlySpan<byte> haystack, byte needle)
    {
        var matches = new List<int>();
        MemchrEnumerator enumerator = EnumerateReverse(haystack, needle);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every occurrence of a byte in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte to find.</param>
    /// <returns>An allocation-free byte offset enumerator.</returns>
    public static MemchrEnumerator EnumerateReverse(ReadOnlySpan<byte> haystack, byte needle)
    {
        return new MemchrEnumerator(haystack, needle, reverse: true);
    }

    /// <summary>
    /// Finds the first occurrence of either of two bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when neither byte is present.</returns>
    public static int Find2(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        if (first == second)
        {
            return Find(haystack, first);
        }

        if (haystack.IsEmpty)
        {
            return -1;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return Find2Vector512(haystack, first, second);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return Find2Vector256(haystack, first, second);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find2Sse2(haystack, first, second);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find2AdvSimd(haystack, first, second);
        }

        return Find2Scalar(haystack, first, second, start: 0);
    }

    /// <summary>
    /// Finds every occurrence of either of two bytes in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <returns>The zero-based indexes of every occurrence.</returns>
    public static IReadOnlyList<int> Find2All(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        var matches = new List<int>();
        Memchr2Enumerator enumerator = Enumerate2(haystack, first, second);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every occurrence of either of two bytes in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <returns>An allocation-free byte offset enumerator.</returns>
    public static Memchr2Enumerator Enumerate2(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        return new Memchr2Enumerator(haystack, first, second, reverse: false);
    }

    /// <summary>
    /// Finds the last occurrence of either of two bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when neither byte is present.</returns>
    public static int Find2Reverse(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        if (first == second)
        {
            return FindReverse(haystack, first);
        }

        if (haystack.IsEmpty)
        {
            return -1;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return Find2ReverseVector512(haystack, first, second);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return Find2ReverseVector256(haystack, first, second);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find2ReverseSse2(haystack, first, second);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find2ReverseAdvSimd(haystack, first, second);
        }

        return Find2ReverseScalar(haystack, first, second, haystack.Length - 1);
    }

    /// <summary>
    /// Finds every occurrence of either of two bytes in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <returns>The zero-based indexes of every occurrence in descending order.</returns>
    public static IReadOnlyList<int> Find2AllReverse(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        var matches = new List<int>();
        Memchr2Enumerator enumerator = Enumerate2Reverse(haystack, first, second);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every occurrence of either of two bytes in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <returns>An allocation-free byte offset enumerator.</returns>
    public static Memchr2Enumerator Enumerate2Reverse(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        return new Memchr2Enumerator(haystack, first, second, reverse: true);
    }

    /// <summary>
    /// Finds the first occurrence of any of three bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <param name="third">The third byte to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when none of the bytes are present.</returns>
    public static int Find3(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        if (first == second)
        {
            return Find2(haystack, first, third);
        }

        if (first == third || second == third)
        {
            return Find2(haystack, first, second);
        }

        if (haystack.IsEmpty)
        {
            return -1;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return Find3Vector512(haystack, first, second, third);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return Find3Vector256(haystack, first, second, third);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find3Sse2(haystack, first, second, third);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find3AdvSimd(haystack, first, second, third);
        }

        return Find3Scalar(haystack, first, second, third, start: 0);
    }

    /// <summary>
    /// Finds every occurrence of any of three bytes in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <param name="third">The third byte to find.</param>
    /// <returns>The zero-based indexes of every occurrence.</returns>
    public static IReadOnlyList<int> Find3All(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        var matches = new List<int>();
        Memchr3Enumerator enumerator = Enumerate3(haystack, first, second, third);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every occurrence of any of three bytes in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <param name="third">The third byte to find.</param>
    /// <returns>An allocation-free byte offset enumerator.</returns>
    public static Memchr3Enumerator Enumerate3(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        return new Memchr3Enumerator(haystack, first, second, third, reverse: false);
    }

    /// <summary>
    /// Finds the last occurrence of any of three bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <param name="third">The third byte to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when none of the bytes are present.</returns>
    public static int Find3Reverse(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        if (first == second)
        {
            return Find2Reverse(haystack, first, third);
        }

        if (first == third || second == third)
        {
            return Find2Reverse(haystack, first, second);
        }

        if (haystack.IsEmpty)
        {
            return -1;
        }

        if (Avx512BW.IsSupported && haystack.Length >= Vector512<byte>.Count)
        {
            return Find3ReverseVector512(haystack, first, second, third);
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return Find3ReverseVector256(haystack, first, second, third);
        }

        if (Sse2.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find3ReverseSse2(haystack, first, second, third);
        }

        if (AdvSimd.IsSupported && haystack.Length >= Vector128<byte>.Count)
        {
            return Find3ReverseAdvSimd(haystack, first, second, third);
        }

        return Find3ReverseScalar(haystack, first, second, third, haystack.Length - 1);
    }

    /// <summary>
    /// Finds every occurrence of any of three bytes in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <param name="third">The third byte to find.</param>
    /// <returns>The zero-based indexes of every occurrence in descending order.</returns>
    public static IReadOnlyList<int> Find3AllReverse(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        var matches = new List<int>();
        Memchr3Enumerator enumerator = Enumerate3Reverse(haystack, first, second, third);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every occurrence of any of three bytes in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="first">The first byte to find.</param>
    /// <param name="second">The second byte to find.</param>
    /// <param name="third">The third byte to find.</param>
    /// <returns>An allocation-free byte offset enumerator.</returns>
    public static Memchr3Enumerator Enumerate3Reverse(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        return new Memchr3Enumerator(haystack, first, second, third, reverse: true);
    }

    private static int FindVector512(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector512.Create(needle);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector512<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong mask = Avx512BW.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector512<byte>.Count;
        }

        return FindScalar(haystack, needle, offset);
    }

    private static int Find2Vector512(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector512.Create(first);
        var secondVector = Vector512.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector512<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong mask =
                Avx512BW.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx512BW.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector512<byte>.Count;
        }

        return Find2Scalar(haystack, first, second, offset);
    }

    private static int Find3Vector512(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector512.Create(first);
        var secondVector = Vector512.Create(second);
        var thirdVector = Vector512.Create(third);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector512<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong mask =
                Avx512BW.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx512BW.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                Avx512BW.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector512<byte>.Count;
        }

        return Find3Scalar(haystack, first, second, third, offset);
    }

    private static int FindReverseVector512(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector512.Create(needle);
        int offset = haystack.Length;
        while (offset >= Vector512<byte>.Count)
        {
            offset -= Vector512<byte>.Count;
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong mask = Avx512BW.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return FindReverseScalar(haystack, needle, offset - 1);
    }

    private static int Find2ReverseVector512(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector512.Create(first);
        var secondVector = Vector512.Create(second);
        int offset = haystack.Length;
        while (offset >= Vector512<byte>.Count)
        {
            offset -= Vector512<byte>.Count;
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong mask =
                Avx512BW.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx512BW.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find2ReverseScalar(haystack, first, second, offset - 1);
    }

    private static int Find3ReverseVector512(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector512.Create(first);
        var secondVector = Vector512.Create(second);
        var thirdVector = Vector512.Create(third);
        int offset = haystack.Length;
        while (offset >= Vector512<byte>.Count)
        {
            offset -= Vector512<byte>.Count;
            var block = Vector512.LoadUnsafe(ref reference, (nuint)offset);
            ulong mask =
                Avx512BW.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx512BW.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                Avx512BW.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find3ReverseScalar(haystack, first, second, third, offset - 1);
    }

    private static int FindVector256(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector256.Create(needle);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector256<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = Avx2.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return FindScalar(haystack, needle, offset);
    }

    private static int Find2Vector256(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector256.Create(first);
        var secondVector = Vector256.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector256<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Avx2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx2.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return Find2Scalar(haystack, first, second, offset);
    }

    private static int Find3Vector256(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector256.Create(first);
        var secondVector = Vector256.Create(second);
        var thirdVector = Vector256.Create(third);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector256<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Avx2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx2.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                Avx2.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return Find3Scalar(haystack, first, second, third, offset);
    }

    private static int FindReverseVector256(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector256.Create(needle);
        int offset = haystack.Length;
        while (offset >= Vector256<byte>.Count)
        {
            offset -= Vector256<byte>.Count;
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = Avx2.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return FindReverseScalar(haystack, needle, offset - 1);
    }

    private static int Find2ReverseVector256(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector256.Create(first);
        var secondVector = Vector256.Create(second);
        int offset = haystack.Length;
        while (offset >= Vector256<byte>.Count)
        {
            offset -= Vector256<byte>.Count;
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Avx2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx2.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find2ReverseScalar(haystack, first, second, offset - 1);
    }

    private static int Find3ReverseVector256(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector256.Create(first);
        var secondVector = Vector256.Create(second);
        var thirdVector = Vector256.Create(third);
        int offset = haystack.Length;
        while (offset >= Vector256<byte>.Count)
        {
            offset -= Vector256<byte>.Count;
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Avx2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Avx2.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                Avx2.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find3ReverseScalar(haystack, first, second, third, offset - 1);
    }

    private static int FindSse2(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector128.Create(needle);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = Sse2.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalar(haystack, needle, offset);
    }

    private static int Find2Sse2(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Sse2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Sse2.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return Find2Scalar(haystack, first, second, offset);
    }

    private static int Find3Sse2(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        var thirdVector = Vector128.Create(third);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Sse2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Sse2.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                Sse2.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return Find3Scalar(haystack, first, second, third, offset);
    }

    private static int FindReverseSse2(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector128.Create(needle);
        int offset = haystack.Length;
        while (offset >= Vector128<byte>.Count)
        {
            offset -= Vector128<byte>.Count;
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = Sse2.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return FindReverseScalar(haystack, needle, offset - 1);
    }

    private static int Find2ReverseSse2(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        int offset = haystack.Length;
        while (offset >= Vector128<byte>.Count)
        {
            offset -= Vector128<byte>.Count;
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Sse2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Sse2.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find2ReverseScalar(haystack, first, second, offset - 1);
    }

    private static int Find3ReverseSse2(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        var thirdVector = Vector128.Create(third);
        int offset = haystack.Length;
        while (offset >= Vector128<byte>.Count)
        {
            offset -= Vector128<byte>.Count;
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                Sse2.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                Sse2.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                Sse2.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find3ReverseScalar(haystack, first, second, third, offset - 1);
    }

    private static int FindAdvSimd(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector128.Create(needle);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = AdvSimd.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalar(haystack, needle, offset);
    }

    private static int Find2AdvSimd(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                AdvSimd.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                AdvSimd.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return Find2Scalar(haystack, first, second, offset);
    }

    private static int Find3AdvSimd(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        var thirdVector = Vector128.Create(third);
        int offset = 0;
        int vectorEnd = haystack.Length - Vector128<byte>.Count;
        while (offset <= vectorEnd)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                AdvSimd.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                AdvSimd.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                AdvSimd.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return Find3Scalar(haystack, first, second, third, offset);
    }

    private static int FindReverseAdvSimd(ReadOnlySpan<byte> haystack, byte needle)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var needleVector = Vector128.Create(needle);
        int offset = haystack.Length;
        while (offset >= Vector128<byte>.Count)
        {
            offset -= Vector128<byte>.Count;
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = AdvSimd.CompareEqual(block, needleVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return FindReverseScalar(haystack, needle, offset - 1);
    }

    private static int Find2ReverseAdvSimd(ReadOnlySpan<byte> haystack, byte first, byte second)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        int offset = haystack.Length;
        while (offset >= Vector128<byte>.Count)
        {
            offset -= Vector128<byte>.Count;
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                AdvSimd.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                AdvSimd.CompareEqual(block, secondVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find2ReverseScalar(haystack, first, second, offset - 1);
    }

    private static int Find3ReverseAdvSimd(ReadOnlySpan<byte> haystack, byte first, byte second, byte third)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        var firstVector = Vector128.Create(first);
        var secondVector = Vector128.Create(second);
        var thirdVector = Vector128.Create(third);
        int offset = haystack.Length;
        while (offset >= Vector128<byte>.Count)
        {
            offset -= Vector128<byte>.Count;
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask =
                AdvSimd.CompareEqual(block, firstVector).ExtractMostSignificantBits() |
                AdvSimd.CompareEqual(block, secondVector).ExtractMostSignificantBits() |
                AdvSimd.CompareEqual(block, thirdVector).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.Log2(mask);
            }
        }

        return Find3ReverseScalar(haystack, first, second, third, offset - 1);
    }

    private static int FindScalar(ReadOnlySpan<byte> haystack, byte needle, int start)
    {
        for (int index = start; index < haystack.Length; index++)
        {
            if (haystack[index] == needle)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindReverseScalar(ReadOnlySpan<byte> haystack, byte needle, int start)
    {
        for (int index = start; index >= 0; index--)
        {
            if (haystack[index] == needle)
            {
                return index;
            }
        }

        return -1;
    }

    private static int Find2Scalar(ReadOnlySpan<byte> haystack, byte first, byte second, int start)
    {
        for (int index = start; index < haystack.Length; index++)
        {
            byte candidate = haystack[index];
            if (candidate == first || candidate == second)
            {
                return index;
            }
        }

        return -1;
    }

    private static int Find2ReverseScalar(ReadOnlySpan<byte> haystack, byte first, byte second, int start)
    {
        for (int index = start; index >= 0; index--)
        {
            byte candidate = haystack[index];
            if (candidate == first || candidate == second)
            {
                return index;
            }
        }

        return -1;
    }

    private static int Find3Scalar(ReadOnlySpan<byte> haystack, byte first, byte second, byte third, int start)
    {
        for (int index = start; index < haystack.Length; index++)
        {
            byte candidate = haystack[index];
            if (candidate == first || candidate == second || candidate == third)
            {
                return index;
            }
        }

        return -1;
    }

    private static int Find3ReverseScalar(ReadOnlySpan<byte> haystack, byte first, byte second, byte third, int start)
    {
        for (int index = start; index >= 0; index--)
        {
            byte candidate = haystack[index];
            if (candidate == first || candidate == second || candidate == third)
            {
                return index;
            }
        }

        return -1;
    }
}
