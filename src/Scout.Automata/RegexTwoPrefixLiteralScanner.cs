using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexTwoPrefixLiteralScanner
{
    private const int PrefixBytes = 3;

    private readonly byte[][] literals;
    private readonly byte first0;
    private readonly byte first1;
    private readonly byte first2;
    private readonly byte second0;
    private readonly byte second1;
    private readonly byte second2;

    private RegexTwoPrefixLiteralScanner(IReadOnlyList<byte[]> literals)
    {
        this.literals = [literals[0].ToArray(), literals[1].ToArray()];
        first0 = this.literals[0][0];
        first1 = this.literals[0][1];
        first2 = this.literals[0][2];
        second0 = this.literals[1][0];
        second1 = this.literals[1][1];
        second2 = this.literals[1][2];
    }

    public static bool TryCreate(IReadOnlyList<byte[]> literals, out RegexTwoPrefixLiteralScanner? scanner)
    {
        scanner = null;
        if (literals.Count != 2 ||
            literals[0].Length < PrefixBytes ||
            literals[1].Length < PrefixBytes ||
            literals[0].AsSpan(0, PrefixBytes).SequenceEqual(literals[1].AsSpan(0, PrefixBytes)))
        {
            return false;
        }

        scanner = new RegexTwoPrefixLiteralScanner(literals);
        return true;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        if (Avx2.IsSupported && haystack.Length - searchAt >= Vector256<byte>.Count + PrefixBytes - 1)
        {
            return FindVector256(haystack, searchAt);
        }

        if (Sse2.IsSupported && haystack.Length - searchAt >= Vector128<byte>.Count + PrefixBytes - 1)
        {
            return FindVector128(haystack, searchAt);
        }

        return FindScalar(haystack, searchAt);
    }

    private RegexLiteralSetCandidate? FindVector256(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - (PrefixBytes - 1);
        var first0Vector = Vector256.Create(first0);
        var first1Vector = Vector256.Create(first1);
        var first2Vector = Vector256.Create(first2);
        var second0Vector = Vector256.Create(second0);
        var second1Vector = Vector256.Create(second1);
        var second2Vector = Vector256.Create(second2);
        while (offset <= vectorEnd)
        {
            var bytes0 = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var bytes1 = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            var bytes2 = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 2));
            uint mask = CandidateMask256(
                bytes0,
                bytes1,
                bytes2,
                first0Vector,
                first1Vector,
                first2Vector,
                second0Vector,
                second1Vector,
                second2Vector);
            while (mask != 0)
            {
                int candidateStart = offset + BitOperations.TrailingZeroCount(mask);
                if (TryVerifyAt(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
                {
                    return candidate;
                }

                mask &= mask - 1;
            }

            offset += Vector256<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private RegexLiteralSetCandidate? FindVector128(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector128<byte>.Count - (PrefixBytes - 1);
        var first0Vector = Vector128.Create(first0);
        var first1Vector = Vector128.Create(first1);
        var first2Vector = Vector128.Create(first2);
        var second0Vector = Vector128.Create(second0);
        var second1Vector = Vector128.Create(second1);
        var second2Vector = Vector128.Create(second2);
        while (offset <= vectorEnd)
        {
            var bytes0 = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var bytes1 = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            var bytes2 = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 2));
            uint mask = CandidateMask128(
                bytes0,
                bytes1,
                bytes2,
                first0Vector,
                first1Vector,
                first2Vector,
                second0Vector,
                second1Vector,
                second2Vector);
            while (mask != 0)
            {
                int candidateStart = offset + BitOperations.TrailingZeroCount(mask);
                if (TryVerifyAt(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
                {
                    return candidate;
                }

                mask &= mask - 1;
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private RegexLiteralSetCandidate? FindScalar(ReadOnlySpan<byte> haystack, int startAt)
    {
        for (int position = startAt; position <= haystack.Length - PrefixBytes; position++)
        {
            if (TryVerifyAt(haystack, position, out RegexLiteralSetCandidate candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryVerifyAt(ReadOnlySpan<byte> haystack, int start, out RegexLiteralSetCandidate candidate)
    {
        if (literals[0].Length <= haystack.Length - start &&
            haystack[start] == first0 &&
            haystack[start + 1] == first1 &&
            haystack[start + 2] == first2 &&
            haystack.Slice(start, literals[0].Length).SequenceEqual(literals[0]))
        {
            candidate = new RegexLiteralSetCandidate(0, new RegexMatch(start, literals[0].Length));
            return true;
        }

        if (literals[1].Length <= haystack.Length - start &&
            haystack[start] == second0 &&
            haystack[start + 1] == second1 &&
            haystack[start + 2] == second2 &&
            haystack.Slice(start, literals[1].Length).SequenceEqual(literals[1]))
        {
            candidate = new RegexLiteralSetCandidate(1, new RegexMatch(start, literals[1].Length));
            return true;
        }

        candidate = default;
        return false;
    }

    private static uint CandidateMask256(
        Vector256<byte> bytes0,
        Vector256<byte> bytes1,
        Vector256<byte> bytes2,
        Vector256<byte> first0,
        Vector256<byte> first1,
        Vector256<byte> first2,
        Vector256<byte> second0,
        Vector256<byte> second1,
        Vector256<byte> second2)
    {
        Vector256<byte> firstMatches = Avx2.And(
            Avx2.And(Avx2.CompareEqual(bytes0, first0), Avx2.CompareEqual(bytes1, first1)),
            Avx2.CompareEqual(bytes2, first2));
        Vector256<byte> secondMatches = Avx2.And(
            Avx2.And(Avx2.CompareEqual(bytes0, second0), Avx2.CompareEqual(bytes1, second1)),
            Avx2.CompareEqual(bytes2, second2));
        return Avx2.Or(firstMatches, secondMatches).ExtractMostSignificantBits();
    }

    private static uint CandidateMask128(
        Vector128<byte> bytes0,
        Vector128<byte> bytes1,
        Vector128<byte> bytes2,
        Vector128<byte> first0,
        Vector128<byte> first1,
        Vector128<byte> first2,
        Vector128<byte> second0,
        Vector128<byte> second1,
        Vector128<byte> second2)
    {
        Vector128<byte> firstMatches = Sse2.And(
            Sse2.And(Sse2.CompareEqual(bytes0, first0), Sse2.CompareEqual(bytes1, first1)),
            Sse2.CompareEqual(bytes2, first2));
        Vector128<byte> secondMatches = Sse2.And(
            Sse2.And(Sse2.CompareEqual(bytes0, second0), Sse2.CompareEqual(bytes1, second1)),
            Sse2.CompareEqual(bytes2, second2));
        return (uint)Sse2.Or(firstMatches, secondMatches).ExtractMostSignificantBits();
    }
}
