using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexAsciiCaseInsensitiveFinder
{
    private const int VeryRareAnchorScore = 250;

    private readonly byte[] needle;
    private readonly int anchorIndex;
    private readonly byte anchor;
    private readonly byte anchorAlternate;
    private readonly bool hasAnchorAlternate;
    private readonly int blockAnchorIndex = -1;
    private readonly byte blockFirst;
    private readonly byte blockSecond;
    private readonly bool blockFirstHasAlternate;
    private readonly bool blockSecondHasAlternate;
    private readonly Vector128<byte> blockFirstVector128;
    private readonly Vector128<byte> blockSecondVector128;
    private readonly Vector128<byte> blockFirstAlternateVector128;
    private readonly Vector128<byte> blockSecondAlternateVector128;
    private readonly Vector256<byte> blockFirstVector256;
    private readonly Vector256<byte> blockSecondVector256;
    private readonly Vector256<byte> blockFirstAlternateVector256;
    private readonly Vector256<byte> blockSecondAlternateVector256;

    public RegexAsciiCaseInsensitiveFinder(ReadOnlySpan<byte> needle)
    {
        this.needle = NormalizeAsciiCase(needle);
        if (this.needle.Length == 0)
        {
            return;
        }

        anchorIndex = SelectAnchorIndex(this.needle);
        anchor = this.needle[anchorIndex];
        hasAnchorAlternate = IsAsciiCased(anchor);
        anchorAlternate = hasAnchorAlternate ? ToggleAsciiCase(anchor) : anchor;
        if (this.needle.Length >= 2 && ShouldUseBlockAnchor(this.needle, anchorIndex))
        {
            blockAnchorIndex = SelectBlockAnchorIndex(this.needle);
            blockFirst = this.needle[blockAnchorIndex];
            blockSecond = this.needle[blockAnchorIndex + 1];
            blockFirstHasAlternate = IsAsciiCased(blockFirst);
            blockSecondHasAlternate = IsAsciiCased(blockSecond);
            byte blockFirstAlternate = blockFirstHasAlternate ? ToggleAsciiCase(blockFirst) : blockFirst;
            byte blockSecondAlternate = blockSecondHasAlternate ? ToggleAsciiCase(blockSecond) : blockSecond;
            blockFirstVector128 = Vector128.Create(blockFirst);
            blockSecondVector128 = Vector128.Create(blockSecond);
            blockFirstAlternateVector128 = Vector128.Create(blockFirstAlternate);
            blockSecondAlternateVector128 = Vector128.Create(blockSecondAlternate);
            blockFirstVector256 = Vector256.Create(blockFirst);
            blockSecondVector256 = Vector256.Create(blockSecond);
            blockFirstAlternateVector256 = Vector256.Create(blockFirstAlternate);
            blockSecondAlternateVector256 = Vector256.Create(blockSecondAlternate);
        }
    }

    public int Find(ReadOnlySpan<byte> haystack)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        if (needle.Length > haystack.Length)
        {
            return -1;
        }

        if (blockAnchorIndex >= 0)
        {
            return FindBlockAnchored(haystack);
        }

        int anchorPosition = anchorIndex;
        int lastAnchorPosition = haystack.Length - (needle.Length - anchorIndex);
        while (anchorPosition <= lastAnchorPosition)
        {
            int offset = hasAnchorAlternate
                ? haystack[anchorPosition..].IndexOfAny(anchor, anchorAlternate)
                : haystack[anchorPosition..].IndexOf(anchor);
            if (offset < 0)
            {
                return -1;
            }

            anchorPosition += offset;
            int matchStart = anchorPosition - anchorIndex;
            if (MatchesAt(haystack, matchStart))
            {
                return matchStart;
            }

            anchorPosition++;
        }

        return -1;
    }

    private int FindBlockAnchored(ReadOnlySpan<byte> haystack)
    {
        int blockPosition = blockAnchorIndex;
        int lastBlockPosition = haystack.Length - (needle.Length - blockAnchorIndex);
        while (blockPosition <= lastBlockPosition)
        {
            int found = FindBlockPosition(haystack, blockPosition, lastBlockPosition);
            if (found < 0)
            {
                return -1;
            }

            int matchStart = found - blockAnchorIndex;
            if (MatchesAt(haystack, matchStart))
            {
                return matchStart;
            }

            blockPosition = found + 1;
        }

        return -1;
    }

    private int FindBlockPosition(ReadOnlySpan<byte> haystack, int startAt, int lastStart)
    {
        if (startAt > lastStart)
        {
            return -1;
        }

        if (Avx2.IsSupported && lastStart - startAt + 1 > Vector256<byte>.Count)
        {
            return FindBlockVector256(haystack, startAt, lastStart);
        }

        if (Sse2.IsSupported && lastStart - startAt + 1 > Vector128<byte>.Count)
        {
            return FindBlockVector128(haystack, startAt, lastStart);
        }

        return FindBlockScalar(haystack, startAt, lastStart);
    }

    private int FindBlockVector256(ReadOnlySpan<byte> haystack, int startAt, int lastStart)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = Math.Min(haystack.Length - Vector256<byte>.Count - 1, lastStart - Vector256<byte>.Count + 1);
        while (offset <= vectorEnd)
        {
            var current = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector256<byte> firstMatches = Avx2.CompareEqual(current, blockFirstVector256);
            if (blockFirstHasAlternate)
            {
                firstMatches = Avx2.Or(firstMatches, Avx2.CompareEqual(current, blockFirstAlternateVector256));
            }

            Vector256<byte> secondMatches = Avx2.CompareEqual(next, blockSecondVector256);
            if (blockSecondHasAlternate)
            {
                secondMatches = Avx2.Or(secondMatches, Avx2.CompareEqual(next, blockSecondAlternateVector256));
            }

            uint mask = Avx2.And(firstMatches, secondMatches).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return FindBlockScalar(haystack, offset, lastStart);
    }

    private int FindBlockVector128(ReadOnlySpan<byte> haystack, int startAt, int lastStart)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = Math.Min(haystack.Length - Vector128<byte>.Count - 1, lastStart - Vector128<byte>.Count + 1);
        while (offset <= vectorEnd)
        {
            var current = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector128<byte> firstMatches = Sse2.CompareEqual(current, blockFirstVector128);
            if (blockFirstHasAlternate)
            {
                firstMatches = Sse2.Or(firstMatches, Sse2.CompareEqual(current, blockFirstAlternateVector128));
            }

            Vector128<byte> secondMatches = Sse2.CompareEqual(next, blockSecondVector128);
            if (blockSecondHasAlternate)
            {
                secondMatches = Sse2.Or(secondMatches, Sse2.CompareEqual(next, blockSecondAlternateVector128));
            }

            uint mask = Sse2.And(firstMatches, secondMatches).ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return FindBlockScalar(haystack, offset, lastStart);
    }

    private int FindBlockScalar(ReadOnlySpan<byte> haystack, int startAt, int lastStart)
    {
        for (int index = startAt; index <= lastStart; index++)
        {
            if (FoldAscii(haystack[index]) == blockFirst &&
                FoldAscii(haystack[index + 1]) == blockSecond)
            {
                return index;
            }
        }

        return -1;
    }

    private bool MatchesAt(ReadOnlySpan<byte> haystack, int position)
    {
        if (needle.Length > haystack.Length - position)
        {
            return false;
        }

        for (int index = 0; index < needle.Length; index++)
        {
            if (FoldAscii(haystack[position + index]) != needle[index])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] NormalizeAsciiCase(ReadOnlySpan<byte> value)
    {
        byte[] normalized = value.ToArray();
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = FoldAscii(normalized[index]);
        }

        return normalized;
    }

    internal static int SelectAnchorIndex(ReadOnlySpan<byte> value)
    {
        int bestIndex = 0;
        int bestScore = AnchorScore(value[0]);
        for (int index = 1; index < value.Length; index++)
        {
            int score = AnchorScore(value[index]);
            if (score > bestScore)
            {
                bestIndex = index;
                bestScore = score;
            }
        }

        return bestIndex;
    }

    private static int SelectBlockAnchorIndex(ReadOnlySpan<byte> value)
    {
        int byteAnchorIndex = SelectAnchorIndex(value);
        return byteAnchorIndex == 0 ? 0 : byteAnchorIndex - 1;
    }

    private static bool ShouldUseBlockAnchor(ReadOnlySpan<byte> value, int byteAnchorIndex)
    {
        return AnchorScore(value[byteAnchorIndex]) < VeryRareAnchorScore;
    }

    private static int AnchorScore(byte value)
    {
        byte folded = FoldAscii(value);
        if (folded is >= (byte)'0' and <= (byte)'9')
        {
            return 180;
        }

        if (folded is < (byte)'a' or > (byte)'z')
        {
            return folded == (byte)' ' ? 10 : 220;
        }

        return folded switch
        {
            (byte)'q' or (byte)'z' => 260,
            (byte)'x' or (byte)'j' => 250,
            (byte)'k' => 240,
            (byte)'v' => 230,
            (byte)'b' or (byte)'p' => 220,
            (byte)'g' => 210,
            (byte)'w' => 200,
            (byte)'y' => 190,
            (byte)'f' => 180,
            (byte)'m' => 170,
            (byte)'c' => 160,
            (byte)'u' => 150,
            (byte)'l' => 140,
            (byte)'d' => 130,
            (byte)'r' => 120,
            (byte)'h' => 110,
            (byte)'s' => 100,
            (byte)'n' => 90,
            (byte)'i' => 80,
            (byte)'o' => 70,
            (byte)'a' => 60,
            (byte)'t' => 50,
            (byte)'e' => 40,
            _ => 30,
        };
    }

    internal static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }

    internal static bool IsAsciiCased(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    internal static byte ToggleAsciiCase(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z'
            ? (byte)(value - 0x20)
            : (byte)(value + 0x20);
    }
}
