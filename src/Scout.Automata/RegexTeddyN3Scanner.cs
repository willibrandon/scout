using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Scout;

/// <summary>
/// Finds candidate offsets for literal prefixes by applying three-byte Teddy fingerprints.
/// </summary>
internal sealed class RegexTeddyN3Scanner(
    Vector128<byte>[] lowMasks128,
    Vector128<byte>[] highMasks128,
    Vector256<byte>[] lowMasks256,
    Vector256<byte>[] highMasks256)
{
    private const int BucketCount = 8;
    private const int MaskLength = 3;

    private readonly Vector128<byte>[] _lowMasks128 = lowMasks128;
    private readonly Vector128<byte>[] _highMasks128 = highMasks128;
    private readonly Vector256<byte>[] _lowMasks256 = lowMasks256;
    private readonly Vector256<byte>[] _highMasks256 = highMasks256;

    /// <summary>
    /// Attempts to create a scanner when every literal supplies a three-byte fingerprint.
    /// </summary>
    /// <param name="needles">The literal prefixes to fingerprint.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters compare case-insensitively.</param>
    /// <param name="scanner">Receives the scanner when all prefixes are supported.</param>
    /// <returns><see langword="true" /> when the scanner was created.</returns>
    internal static bool TryCreate(
        byte[][] needles,
        bool asciiCaseInsensitive,
        out RegexTeddyN3Scanner? scanner)
    {
        for (int index = 0; index < needles.Length; index++)
        {
            if (needles[index].Length < MaskLength)
            {
                scanner = null;
                return false;
            }
        }

        var lowMasks128 = new Vector128<byte>[MaskLength];
        var highMasks128 = new Vector128<byte>[MaskLength];
        var lowMasks256 = new Vector256<byte>[MaskLength];
        var highMasks256 = new Vector256<byte>[MaskLength];
        BuildMasks(
            needles,
            asciiCaseInsensitive,
            lowMasks128,
            highMasks128,
            lowMasks256,
            highMasks256);
        scanner = new RegexTeddyN3Scanner(
            lowMasks128,
            highMasks128,
            lowMasks256,
            highMasks256);
        return true;
    }

    /// <summary>
    /// Finds the next offset whose first three bytes match a Teddy fingerprint.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first byte offset to inspect.</param>
    /// <returns>The candidate offset, or <c>-1</c> when none remains.</returns>
    internal int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        startAt = Math.Clamp(startAt, 0, haystack.Length);
        if (startAt > haystack.Length - MaskLength)
        {
            return -1;
        }

        if (Avx2.IsSupported &&
            haystack.Length - startAt >= Vector256<byte>.Count + MaskLength - 1)
        {
            return FindCandidateVector256(haystack, startAt);
        }

        if ((Ssse3.IsSupported || AdvSimd.Arm64.IsSupported) &&
            haystack.Length - startAt >= Vector128<byte>.Count + MaskLength - 1)
        {
            return FindCandidateVector128(haystack, startAt);
        }

        return FindCandidateScalar(haystack, startAt);
    }

    private int FindCandidateVector256(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - MaskLength + 1;
        while (offset <= vectorEnd)
        {
            Vector256<byte> candidates = CandidateVector256(ref reference, offset);
            uint emptyLanes = Avx2.CompareEqual(candidates, Vector256<byte>.Zero)
                .ExtractMostSignificantBits();
            uint candidateLanes = ~emptyLanes;
            if (candidateLanes != 0)
            {
                return offset + BitOperations.TrailingZeroCount(candidateLanes);
            }

            offset += Vector256<byte>.Count;
        }

        return FindCandidateScalar(haystack, offset);
    }

    private int FindCandidateVector128(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector128<byte>.Count - MaskLength + 1;
        while (offset <= vectorEnd)
        {
            Vector128<byte> candidates = Ssse3.IsSupported
                ? CandidateVector128Ssse3(ref reference, offset)
                : CandidateVector128AdvSimd(ref reference, offset);
            uint emptyLanes = Vector128.Equals(candidates, Vector128<byte>.Zero)
                .ExtractMostSignificantBits();
            uint candidateLanes = ~emptyLanes & ushort.MaxValue;
            if (candidateLanes != 0)
            {
                return offset + BitOperations.TrailingZeroCount(candidateLanes);
            }

            offset += Vector128<byte>.Count;
        }

        return FindCandidateScalar(haystack, offset);
    }

    private int FindCandidateScalar(ReadOnlySpan<byte> haystack, int startAt)
    {
        for (int position = startAt; position <= haystack.Length - MaskLength; position++)
        {
            byte candidates = CandidateByteScalar(haystack[position], byteIndex: 0);
            candidates &= CandidateByteScalar(haystack[position + 1], byteIndex: 1);
            candidates &= CandidateByteScalar(haystack[position + 2], byteIndex: 2);
            if (candidates != 0)
            {
                return position;
            }
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector256<byte> CandidateVector256(ref byte reference, int offset)
    {
        Vector256<byte> first = CandidateByteVector256(
            Vector256.LoadUnsafe(ref reference, (nuint)offset),
            byteIndex: 0);
        Vector256<byte> second = CandidateByteVector256(
            Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1)),
            byteIndex: 1);
        Vector256<byte> third = CandidateByteVector256(
            Vector256.LoadUnsafe(ref reference, (nuint)(offset + 2)),
            byteIndex: 2);
        return first & second & third;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector128<byte> CandidateVector128Ssse3(ref byte reference, int offset)
    {
        Vector128<byte> first = CandidateByteVector128Ssse3(
            Vector128.LoadUnsafe(ref reference, (nuint)offset),
            byteIndex: 0);
        Vector128<byte> second = CandidateByteVector128Ssse3(
            Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1)),
            byteIndex: 1);
        Vector128<byte> third = CandidateByteVector128Ssse3(
            Vector128.LoadUnsafe(ref reference, (nuint)(offset + 2)),
            byteIndex: 2);
        return first & second & third;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector128<byte> CandidateVector128AdvSimd(ref byte reference, int offset)
    {
        Vector128<byte> first = CandidateByteVector128AdvSimd(
            Vector128.LoadUnsafe(ref reference, (nuint)offset),
            byteIndex: 0);
        Vector128<byte> second = CandidateByteVector128AdvSimd(
            Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1)),
            byteIndex: 1);
        Vector128<byte> third = CandidateByteVector128AdvSimd(
            Vector128.LoadUnsafe(ref reference, (nuint)(offset + 2)),
            byteIndex: 2);
        return first & second & third;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector256<byte> CandidateByteVector256(Vector256<byte> input, int byteIndex)
    {
        var nibbleMask = Vector256.Create((byte)0x0F);
        Vector256<byte> lowNibbles = input & nibbleMask;
        Vector256<byte> highNibbles = (input >>> 4) & nibbleMask;
        return Avx2.Shuffle(_lowMasks256[byteIndex], lowNibbles) &
            Avx2.Shuffle(_highMasks256[byteIndex], highNibbles);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector128<byte> CandidateByteVector128Ssse3(Vector128<byte> input, int byteIndex)
    {
        var nibbleMask = Vector128.Create((byte)0x0F);
        Vector128<byte> lowNibbles = input & nibbleMask;
        Vector128<byte> highNibbles = (input >>> 4) & nibbleMask;
        return Ssse3.Shuffle(_lowMasks128[byteIndex], lowNibbles) &
            Ssse3.Shuffle(_highMasks128[byteIndex], highNibbles);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector128<byte> CandidateByteVector128AdvSimd(Vector128<byte> input, int byteIndex)
    {
        var nibbleMask = Vector128.Create((byte)0x0F);
        Vector128<byte> lowNibbles = input & nibbleMask;
        Vector128<byte> highNibbles = (input >>> 4) & nibbleMask;
        return AdvSimd.Arm64.VectorTableLookup(_lowMasks128[byteIndex], lowNibbles) &
            AdvSimd.Arm64.VectorTableLookup(_highMasks128[byteIndex], highNibbles);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte CandidateByteScalar(byte value, int byteIndex)
    {
        return (byte)(
            _lowMasks128[byteIndex].GetElement(value & 0x0F) &
            _highMasks128[byteIndex].GetElement(value >> 4));
    }

    private static void BuildMasks(
        byte[][] needles,
        bool asciiCaseInsensitive,
        Vector128<byte>[] lowMasks128,
        Vector128<byte>[] highMasks128,
        Vector256<byte>[] lowMasks256,
        Vector256<byte>[] highMasks256)
    {
        byte[] low = new byte[16];
        byte[] high = new byte[16];
        for (int byteIndex = 0; byteIndex < MaskLength; byteIndex++)
        {
            Array.Clear(low);
            Array.Clear(high);
            for (int patternIndex = 0; patternIndex < needles.Length; patternIndex++)
            {
                int bucket = patternIndex % BucketCount;
                byte value = needles[patternIndex][byteIndex];
                AddMaskByte(low, high, bucket, value);
                if (asciiCaseInsensitive && IsAsciiCased(value))
                {
                    AddMaskByte(low, high, bucket, ToggleAsciiCase(value));
                }
            }

            ref byte lowReference = ref MemoryMarshal.GetArrayDataReference(low);
            ref byte highReference = ref MemoryMarshal.GetArrayDataReference(high);
            lowMasks128[byteIndex] = Vector128.LoadUnsafe(ref lowReference);
            highMasks128[byteIndex] = Vector128.LoadUnsafe(ref highReference);
            lowMasks256[byteIndex] = Vector256.Create(
                lowMasks128[byteIndex],
                lowMasks128[byteIndex]);
            highMasks256[byteIndex] = Vector256.Create(
                highMasks128[byteIndex],
                highMasks128[byteIndex]);
        }
    }

    private static void AddMaskByte(Span<byte> low, Span<byte> high, int bucket, byte value)
    {
        byte bit = (byte)(1 << bucket);
        low[value & 0x0F] |= bit;
        high[value >> 4] |= bit;
    }

    private static bool IsAsciiCased(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    private static byte ToggleAsciiCase(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z'
            ? (byte)(value - 32)
            : (byte)(value + 32);
    }
}
