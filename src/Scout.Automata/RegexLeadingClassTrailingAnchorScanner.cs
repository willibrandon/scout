using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexLeadingClassTrailingAnchorScanner
{
    private const int MaxTrailingBytes = 32;

    private readonly bool[] lastLiteralByteSet;
    private readonly bool[] trailingByteSet;
    private readonly Vector128<byte>[] lastLiteralVectors128;
    private readonly Vector128<byte>[] trailingVectors128;
    private readonly Vector256<byte>[] lastLiteralVectors256;
    private readonly Vector256<byte>[] trailingVectors256;

    private RegexLeadingClassTrailingAnchorScanner(
        byte[] lastLiteralBytes,
        byte[] trailingBytes,
        int minLiteralLength,
        int maxLiteralLength)
    {
        MinLiteralLength = minLiteralLength;
        MaxLiteralLength = maxLiteralLength;
        lastLiteralByteSet = BuildByteSet(lastLiteralBytes);
        trailingByteSet = BuildByteSet(trailingBytes);
        lastLiteralVectors128 = BuildVector128(lastLiteralBytes);
        trailingVectors128 = BuildVector128(trailingBytes);
        lastLiteralVectors256 = BuildVector256(lastLiteralBytes);
        trailingVectors256 = BuildVector256(trailingBytes);
    }

    public int MinLiteralLength { get; }

    public int MaxLiteralLength { get; }

    public static bool TryCreate(
        IReadOnlyList<RegexLeadingClassLiteralBranch> branches,
        RegexCompileOptions options,
        out RegexLeadingClassTrailingAnchorScanner? scanner)
    {
        scanner = null;
        if (branches.Count == 0)
        {
            return false;
        }

        bool[] lastLiteralByteSet = new bool[256];
        bool[] trailingByteSet = new bool[256];
        int minLiteralLength = int.MaxValue;
        int maxLiteralLength = 0;
        for (int index = 0; index < branches.Count; index++)
        {
            RegexLeadingClassLiteralBranch branch = branches[index];
            if (!branch.TrailingAtom.HasValue || branch.Literal.Length == 0)
            {
                return false;
            }

            lastLiteralByteSet[branch.Literal[^1]] = true;
            if (!TryAddAtomBytes(branch.TrailingAtom.Value, options, trailingByteSet))
            {
                return false;
            }

            minLiteralLength = Math.Min(minLiteralLength, branch.Literal.Length);
            maxLiteralLength = Math.Max(maxLiteralLength, branch.Literal.Length);
        }

        byte[] lastLiteralBytes = ToByteArray(lastLiteralByteSet);
        byte[] trailingBytes = ToByteArray(trailingByteSet);
        if (lastLiteralBytes.Length == 0 || trailingBytes.Length == 0)
        {
            return false;
        }

        scanner = new RegexLeadingClassTrailingAnchorScanner(
            lastLiteralBytes,
            trailingBytes,
            minLiteralLength,
            maxLiteralLength);
        return true;
    }

    public int Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        if (searchAt > haystack.Length - 2)
        {
            return -1;
        }

        if (Avx2.IsSupported && haystack.Length - searchAt > Vector256<byte>.Count)
        {
            return FindVector256(haystack, searchAt);
        }

        if (Sse2.IsSupported && haystack.Length - searchAt > Vector128<byte>.Count)
        {
            return FindVector128(haystack, searchAt);
        }

        return FindScalar(haystack, searchAt);
    }

    private int FindVector256(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector256<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector256.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector256<byte> matches = Avx2.And(
                AnyEqual256(current, lastLiteralVectors256),
                AnyEqual256(next, trailingVectors256));
            uint mask = matches.ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector256<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private int FindVector128(ReadOnlySpan<byte> haystack, int startAt)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int offset = startAt;
        int vectorEnd = haystack.Length - Vector128<byte>.Count - 1;
        while (offset <= vectorEnd)
        {
            var current = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            var next = Vector128.LoadUnsafe(ref reference, (nuint)(offset + 1));
            Vector128<byte> matches = Sse2.And(
                AnyEqual128(current, lastLiteralVectors128),
                AnyEqual128(next, trailingVectors128));
            uint mask = matches.ExtractMostSignificantBits();
            if (mask != 0)
            {
                return offset + BitOperations.TrailingZeroCount(mask);
            }

            offset += Vector128<byte>.Count;
        }

        return FindScalar(haystack, offset);
    }

    private int FindScalar(ReadOnlySpan<byte> haystack, int startAt)
    {
        for (int index = startAt; index < haystack.Length - 1; index++)
        {
            if (lastLiteralByteSet[haystack[index]] && trailingByteSet[haystack[index + 1]])
            {
                return index;
            }
        }

        return -1;
    }

    private static Vector256<byte> AnyEqual256(Vector256<byte> value, Vector256<byte>[] candidates)
    {
        Vector256<byte> matches = Vector256<byte>.Zero;
        for (int index = 0; index < candidates.Length; index++)
        {
            matches = Avx2.Or(matches, Avx2.CompareEqual(value, candidates[index]));
        }

        return matches;
    }

    private static Vector128<byte> AnyEqual128(Vector128<byte> value, Vector128<byte>[] candidates)
    {
        Vector128<byte> matches = Vector128<byte>.Zero;
        for (int index = 0; index < candidates.Length; index++)
        {
            matches = Sse2.Or(matches, Sse2.CompareEqual(value, candidates[index]));
        }

        return matches;
    }

    private static bool TryAddAtomBytes(RegexAtomSpec atom, RegexCompileOptions options, bool[] bytes)
    {
        int count = CountBytes(bytes);
        for (int value = 0; value <= 0xFF; value++)
        {
            if (!AtomMatches((byte)value, atom, options))
            {
                continue;
            }

            if (!bytes[value])
            {
                bytes[value] = true;
                count++;
                if (count > MaxTrailingBytes)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int CountBytes(bool[] bytes)
    {
        int count = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index])
            {
                count++;
            }
        }

        return count;
    }

    private static bool AtomMatches(byte value, RegexAtomSpec atom, RegexCompileOptions options)
    {
        return RegexByteClass.AtomMatches(
            value,
            atom.Kind,
            atom.Value,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator);
    }

    private static bool[] BuildByteSet(byte[] bytes)
    {
        bool[] set = new bool[256];
        for (int index = 0; index < bytes.Length; index++)
        {
            set[bytes[index]] = true;
        }

        return set;
    }

    private static Vector128<byte>[] BuildVector128(byte[] bytes)
    {
        var vectors = new Vector128<byte>[bytes.Length];
        for (int index = 0; index < bytes.Length; index++)
        {
            vectors[index] = Vector128.Create(bytes[index]);
        }

        return vectors;
    }

    private static Vector256<byte>[] BuildVector256(byte[] bytes)
    {
        var vectors = new Vector256<byte>[bytes.Length];
        for (int index = 0; index < bytes.Length; index++)
        {
            vectors[index] = Vector256.Create(bytes[index]);
        }

        return vectors;
    }

    private static byte[] ToByteArray(bool[] set)
    {
        List<byte> bytes = [];
        for (int value = 0; value <= 0xFF; value++)
        {
            if (set[value])
            {
                bytes.Add((byte)value);
            }
        }

        return bytes.ToArray();
    }
}
