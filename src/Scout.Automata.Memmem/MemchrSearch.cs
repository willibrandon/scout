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
        return haystack.LastIndexOf(needle);
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
        return haystack.IndexOfAny(first, second);
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
        for (int index = haystack.Length - 1; index >= 0; index--)
        {
            byte candidate = haystack[index];
            if (candidate == first || candidate == second)
            {
                return index;
            }
        }

        return -1;
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
        return haystack.IndexOfAny(first, second, third);
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
        for (int index = haystack.Length - 1; index >= 0; index--)
        {
            byte candidate = haystack[index];
            if (candidate == first || candidate == second || candidate == third)
            {
                return index;
            }
        }

        return -1;
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
}
