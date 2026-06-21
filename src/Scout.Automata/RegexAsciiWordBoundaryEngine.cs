using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Scout;

internal sealed class RegexAsciiWordBoundaryEngine
{
    private static readonly SearchValues<byte> AsciiWordBytes = SearchValues.Create(
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz"u8);

    private readonly int minimum;
    private readonly bool unicodeWord;

    private RegexAsciiWordBoundaryEngine(int minimum, bool unicodeWord)
    {
        this.minimum = minimum;
        this.unicodeWord = unicodeWord;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexAsciiWordBoundaryEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !IsWordBoundary(sequence.Nodes[0]) ||
            !IsWordBoundary(sequence.Nodes[2]) ||
            UnwrapTransparentGroups(sequence.Nodes[1]) is not RegexRepetitionNode { Minimum: > 0, Maximum: null } repetition ||
            !IsWordAtom(repetition.Child, options))
        {
            return false;
        }

        engine = new RegexAsciiWordBoundaryEngine(repetition.Minimum, options.UnicodeClasses);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryFind(haystack, Math.Clamp(startAt, 0, haystack.Length), out RegexMatch match)
            ? match
            : null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        if (!unicodeWord)
        {
            return ContainsAsciiWordRun(haystack);
        }

        if (IsAllAscii(haystack))
        {
            return ContainsAsciiWordRun(haystack);
        }

        return ContainsUnicodeWordRun(haystack);
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        CountOrSum(haystack, startAt, sumSpans: false, out long total);
        return total;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        CountOrSum(haystack, startAt, sumSpans: true, out long total);
        return total;
    }

    public long CountMatchingLines(ReadOnlySpan<byte> haystack)
    {
        long total = 0;
        int position = 0;
        while (position < haystack.Length)
        {
            int lineEnd = FindLineEnd(haystack, position);
            int contentEnd = lineEnd;
            if (contentEnd > position && haystack[contentEnd - 1] == (byte)'\r')
            {
                contentEnd--;
            }

            if (LineIsMatch(haystack[position..contentEnd]))
            {
                total++;
            }

            if (lineEnd >= haystack.Length)
            {
                return total;
            }

            position = lineEnd + 1;
        }

        return total;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if (!unicodeWord)
        {
            return TryMatchAsciiAt(haystack, start, out length);
        }

        if ((uint)start >= (uint)haystack.Length ||
            unicodeWord && !RegexByteClass.IsUtf8Boundary(haystack, start) ||
            IsWordBefore(haystack, start, unicodeWord) ||
            !TryGetWordLength(haystack, start, unicodeWord, out _, out int firstLength))
        {
            return false;
        }

        int end = start + firstLength;
        int scalars = 1;
        while (TryGetWordLength(haystack, end, unicodeWord, out _, out int wordLength))
        {
            scalars++;
            end += wordLength;
        }

        length = end - start;
        return scalars >= minimum;
    }

    private bool TryFind(ReadOnlySpan<byte> haystack, int startAt, out RegexMatch match)
    {
        if (!unicodeWord)
        {
            return TryFindAscii(haystack, startAt, out match);
        }

        if (IsAllAscii(haystack))
        {
            return TryFindAscii(haystack, startAt, out match);
        }

        int position = SkipPartialWord(haystack, startAt);
        while (position < haystack.Length)
        {
            while (position < haystack.Length && !TryGetWordLength(haystack, position, unicodeWord, out _, out _))
            {
                position = AdvanceAfterNonWord(haystack, position);
            }

            int start = position;
            int scalars = 0;
            while (TryGetWordLength(haystack, position, unicodeWord, out _, out int wordLength))
            {
                scalars++;
                position += wordLength;
            }

            int length = position - start;
            if (scalars >= minimum)
            {
                match = new RegexMatch(start, length);
                return true;
            }
        }

        match = default;
        return false;
    }

    private void CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
    {
        if (!unicodeWord)
        {
            CountOrSumAscii(haystack, startAt, sumSpans, out total);
            return;
        }

        if (IsAllAscii(haystack))
        {
            CountOrSumAscii(haystack, startAt, sumSpans, out total);
            return;
        }

        total = 0;
        int position = SkipPartialWord(haystack, Math.Clamp(startAt, 0, haystack.Length));
        while (position < haystack.Length)
        {
            while (position < haystack.Length && !TryGetWordLength(haystack, position, unicodeWord, out _, out _))
            {
                position = AdvanceAfterNonWord(haystack, position);
            }

            int start = position;
            int scalars = 0;
            while (TryGetWordLength(haystack, position, unicodeWord, out _, out int wordLength))
            {
                scalars++;
                position += wordLength;
            }

            int length = position - start;
            if (scalars >= minimum)
            {
                total += sumSpans ? length : 1;
            }
        }
    }

    private bool TryMatchAsciiAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            start > 0 && IsAsciiWord(haystack[start - 1]) ||
            !IsAsciiWord(haystack[start]))
        {
            return false;
        }

        int end = start + 1;
        while (end < haystack.Length && IsAsciiWord(haystack[end]))
        {
            end++;
        }

        length = end - start;
        return length >= minimum;
    }

    private bool TryFindAscii(ReadOnlySpan<byte> haystack, int startAt, out RegexMatch match)
    {
        int position = SkipPartialAsciiWord(haystack, startAt);
        while (position < haystack.Length)
        {
            int relativeStart = haystack[position..].IndexOfAny(AsciiWordBytes);
            if (relativeStart < 0)
            {
                break;
            }

            int start = position + relativeStart;
            int relativeEnd = haystack[start..].IndexOfAnyExcept(AsciiWordBytes);
            int end = relativeEnd < 0 ? haystack.Length : start + relativeEnd;
            int length = end - start;
            if (length >= minimum)
            {
                match = new RegexMatch(start, length);
                return true;
            }

            position = end;
        }

        match = default;
        return false;
    }

    private bool ContainsAsciiWordRun(ReadOnlySpan<byte> haystack)
    {
        if (haystack.Length < minimum)
        {
            return false;
        }

        if (Avx2.IsSupported && haystack.Length >= Vector256<byte>.Count)
        {
            return ContainsAsciiWordRunAvx2(haystack);
        }

        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int runLength = 0;
        for (int position = 0; position < haystack.Length; position++)
        {
            if (IsAsciiWordFast(Unsafe.Add(ref reference, position)))
            {
                runLength++;
                if (runLength >= minimum)
                {
                    return true;
                }
            }
            else
            {
                runLength = 0;
            }
        }

        return false;
    }

    private bool ContainsAsciiWordRunAvx2(ReadOnlySpan<byte> haystack)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int position = 0;
        int length = haystack.Length;
        int vectorEnd = length - Vector256<byte>.Count;
        int runLength = 0;
        var caseFold = Vector256.Create((byte)0x20);
        var underscore = Vector256.Create((byte)'_');
        var beforeA = Vector256.Create((sbyte)('a' - 1));
        var afterZ = Vector256.Create((sbyte)('z' + 1));
        var beforeZero = Vector256.Create((sbyte)('0' - 1));
        var afterNine = Vector256.Create((sbyte)('9' + 1));
        while (position <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)position);
            Vector256<sbyte> folded = Avx2.Or(block, caseFold).AsSByte();
            Vector256<byte> letters = Avx2.And(
                Avx2.CompareGreaterThan(folded, beforeA),
                Avx2.CompareGreaterThan(afterZ, folded)).AsByte();
            Vector256<sbyte> signedBlock = block.AsSByte();
            Vector256<byte> digits = Avx2.And(
                Avx2.CompareGreaterThan(signedBlock, beforeZero),
                Avx2.CompareGreaterThan(afterNine, signedBlock)).AsByte();
            Vector256<byte> words = Avx2.Or(
                letters,
                Avx2.Or(digits, Avx2.CompareEqual(block, underscore)));
            if (AccumulateAsciiWordContainsMask(words.ExtractMostSignificantBits(), Vector256<byte>.Count, ref runLength))
            {
                return true;
            }

            position += Vector256<byte>.Count;
        }

        while (position < length)
        {
            if (IsAsciiWord(haystack[position]))
            {
                runLength++;
                if (runLength >= minimum)
                {
                    return true;
                }
            }
            else
            {
                runLength = 0;
            }

            position++;
        }

        return false;
    }

    private bool AccumulateAsciiWordContainsMask(uint mask, int width, ref int runLength)
    {
        uint fullMask = width == 32 ? uint.MaxValue : (1u << width) - 1u;
        mask &= fullMask;
        if (mask == fullMask)
        {
            runLength += width;
            return runLength >= minimum;
        }

        if (mask == 0)
        {
            runLength = 0;
            return false;
        }

        int prefixOnes = Math.Min(BitOperations.TrailingZeroCount(~mask), width);
        if (runLength > 0 && runLength + prefixOnes >= minimum)
        {
            return true;
        }

        if (BitOperations.PopCount(mask) < minimum)
        {
            runLength = CountSuffixOneBits(mask, width);
            return false;
        }

        int consumed = 0;
        while (mask != 0)
        {
            int zeros = BitOperations.TrailingZeroCount(mask);
            if (zeros > 0)
            {
                runLength = 0;
                consumed += zeros;
                mask >>= zeros;
            }

            int ones = BitOperations.TrailingZeroCount(~mask);
            runLength += ones;
            if (runLength >= minimum)
            {
                return true;
            }

            consumed += ones;
            mask >>= ones;
        }

        if (consumed < width)
        {
            runLength = 0;
        }

        return false;
    }

    private static int CountSuffixOneBits(uint mask, int width)
    {
        return BitOperations.LeadingZeroCount(~(mask << (32 - width)));
    }

    private bool ContainsUnicodeWordRun(ReadOnlySpan<byte> haystack)
    {
        if (haystack.Length < minimum)
        {
            return false;
        }

        int position = 0;
        int runLength = 0;
        while (position < haystack.Length)
        {
            byte first = haystack[position];
            if (first <= 0x7F)
            {
                if (IsAsciiWordFast(first))
                {
                    runLength++;
                    if (runLength >= minimum)
                    {
                        return true;
                    }
                }
                else
                {
                    runLength = 0;
                }

                position++;
                continue;
            }

            if (!RegexByteClass.IsUtf8Boundary(haystack, position) ||
                Rune.DecodeFromUtf8(haystack[position..], out Rune rune, out int scalarLength) != OperationStatus.Done)
            {
                runLength = 0;
                position++;
                continue;
            }

            if (RegexUnicodeTables.IsPerlWord(rune))
            {
                runLength++;
                if (runLength >= minimum)
                {
                    return true;
                }
            }
            else
            {
                runLength = 0;
            }

            position += scalarLength;
        }

        return false;
    }

    private void CountOrSumAscii(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
    {
        total = 0;
        int position = SkipPartialAsciiWord(haystack, Math.Clamp(startAt, 0, haystack.Length));
        if (Avx2.IsSupported && haystack.Length - position >= Vector256<byte>.Count)
        {
            CountOrSumAsciiAvx2(haystack, position, sumSpans, ref total);
            return;
        }

        while (position < haystack.Length)
        {
            while (position < haystack.Length && !IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int start = position;
            while (position < haystack.Length && IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int length = position - start;
            if (length >= minimum)
            {
                total += sumSpans ? length : 1;
            }
        }
    }

    private void CountOrSumAsciiAvx2(ReadOnlySpan<byte> haystack, int position, bool sumSpans, ref long total)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int length = haystack.Length;
        int vectorEnd = length - Vector256<byte>.Count;
        int runLength = 0;
        var caseFold = Vector256.Create((byte)0x20);
        var underscore = Vector256.Create((byte)'_');
        var beforeA = Vector256.Create((sbyte)('a' - 1));
        var afterZ = Vector256.Create((sbyte)('z' + 1));
        var beforeZero = Vector256.Create((sbyte)('0' - 1));
        var afterNine = Vector256.Create((sbyte)('9' + 1));
        while (position <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)position);
            Vector256<sbyte> folded = Avx2.Or(block, caseFold).AsSByte();
            Vector256<byte> letters = Avx2.And(
                Avx2.CompareGreaterThan(folded, beforeA),
                Avx2.CompareGreaterThan(afterZ, folded)).AsByte();
            Vector256<sbyte> signedBlock = block.AsSByte();
            Vector256<byte> digits = Avx2.And(
                Avx2.CompareGreaterThan(signedBlock, beforeZero),
                Avx2.CompareGreaterThan(afterNine, signedBlock)).AsByte();
            Vector256<byte> words = Avx2.Or(
                letters,
                Avx2.Or(digits, Avx2.CompareEqual(block, underscore)));
            uint mask = words.ExtractMostSignificantBits();
            AccumulateAsciiWordMask(mask, Vector256<byte>.Count, sumSpans, ref runLength, ref total);
            position += Vector256<byte>.Count;
        }

        while (position < length)
        {
            if (IsAsciiWord(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddAsciiWordRun(runLength, sumSpans, ref total);
                runLength = 0;
            }

            position++;
        }

        AddAsciiWordRun(runLength, sumSpans, ref total);
    }

    private bool LineIsMatch(ReadOnlySpan<byte> haystack)
    {
        if (!unicodeWord)
        {
            return ContainsAsciiWordRun(haystack);
        }

        if (IsAllAscii(haystack))
        {
            return ContainsAsciiWordRun(haystack);
        }

        return ContainsUnicodeWordRun(haystack);
    }

    private static int FindLineEnd(ReadOnlySpan<byte> haystack, int start)
    {
        int offset = haystack[start..].IndexOf((byte)'\n');
        return offset < 0 ? haystack.Length : start + offset;
    }

    private void AccumulateAsciiWordMask(uint mask, int width, bool sumSpans, ref int runLength, ref long total)
    {
        uint fullMask = width == 32 ? uint.MaxValue : (1u << width) - 1u;
        mask &= fullMask;
        if (mask == fullMask)
        {
            runLength += width;
            return;
        }

        if (mask == 0)
        {
            AddAsciiWordRun(runLength, sumSpans, ref total);
            runLength = 0;
            return;
        }

        int consumed = 0;
        while (mask != 0)
        {
            int zeros = BitOperations.TrailingZeroCount(mask);
            if (zeros > 0)
            {
                AddAsciiWordRun(runLength, sumSpans, ref total);
                runLength = 0;
                consumed += zeros;
                mask >>= zeros;
            }

            int ones = BitOperations.TrailingZeroCount(~mask);
            runLength += ones;
            consumed += ones;
            mask >>= ones;
        }

        if (consumed < width)
        {
            AddAsciiWordRun(runLength, sumSpans, ref total);
            runLength = 0;
        }
    }

    private void AddAsciiWordRun(int runLength, bool sumSpans, ref long total)
    {
        if (runLength >= minimum)
        {
            total += sumSpans ? runLength : 1;
        }
    }

    private static int SkipPartialAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        if (position > 0 &&
            position < haystack.Length &&
            IsAsciiWord(haystack[position - 1]) &&
            IsAsciiWord(haystack[position]))
        {
            do
            {
                position++;
            }
            while (position < haystack.Length && IsAsciiWord(haystack[position]));
        }

        return position;
    }

    private int SkipPartialWord(ReadOnlySpan<byte> haystack, int position)
    {
        position = SkipToUtf8Boundary(haystack, position);
        if (position > 0 &&
            position < haystack.Length &&
            IsWordBefore(haystack, position, unicodeWord) &&
            TryGetWordLength(haystack, position, unicodeWord, out _, out _))
        {
            do
            {
                position += WordLengthOrOne(haystack, position, unicodeWord);
            }
            while (position < haystack.Length && TryGetWordLength(haystack, position, unicodeWord, out _, out _));
        }

        return position;
    }

    private static bool IsWordBoundary(RegexSyntaxNode node)
    {
        return UnwrapTransparentGroups(node) is RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary };
    }

    private static bool IsWordAtom(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom)
        {
            return false;
        }

        if (atom.Kind == RegexSyntaxKind.WordClass)
        {
            return true;
        }

        if (options.UnicodeClasses)
        {
            return false;
        }

        if (atom.Kind != RegexSyntaxKind.CharacterClass)
        {
            return false;
        }

        ReadOnlySpan<byte> expression = atom.Value.Span;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bool matches = RegexByteClass.AtomMatches(
                (byte)value,
                RegexSyntaxKind.CharacterClass,
                expression,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
            if (matches != IsAsciiWord((byte)value))
            {
                return false;
            }
        }

        return true;
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }

    private static bool IsAsciiWord(byte value)
    {
        return RegexSimpleSequenceSegment.IsAsciiWord(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiWordFast(byte value)
    {
        byte folded = (byte)(value | 0x20);
        return (uint)(folded - 'a') <= 'z' - 'a' ||
            (uint)(value - '0') <= '9' - '0' ||
            value == (byte)'_';
    }

    private static bool IsAllAscii(ReadOnlySpan<byte> haystack)
    {
        return haystack.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F) < 0;
    }

    private static bool IsWordBefore(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        if (position <= 0)
        {
            return false;
        }

        if (!unicodeWord)
        {
            return IsAsciiWord(haystack[position - 1]);
        }

        int firstCandidate = Math.Max(0, position - 4);
        for (int index = firstCandidate; index < position; index++)
        {
            if (TryGetWordLength(haystack, index, unicodeWord, out bool matched, out int length) &&
                index + length == position)
            {
                return matched;
            }
        }

        return false;
    }

    private static bool TryGetWordLength(ReadOnlySpan<byte> haystack, int position, bool unicodeWord, out bool matched, out int length)
    {
        matched = false;
        length = 0;
        if ((uint)position >= (uint)haystack.Length)
        {
            return false;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            matched = IsAsciiWord(first);
            length = 1;
            return matched;
        }

        if (!unicodeWord)
        {
            return false;
        }

        if (!RegexByteClass.IsUtf8Boundary(haystack, position) ||
            Rune.DecodeFromUtf8(haystack[position..], out Rune rune, out length) != OperationStatus.Done)
        {
            length = 0;
            return false;
        }

        matched = RegexUnicodeTables.IsPerlWord(rune);
        return matched;
    }

    private static int WordLengthOrOne(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        return TryGetWordLength(haystack, position, unicodeWord, out _, out int length) && length > 0
            ? length
            : 1;
    }

    private static int AdvanceAfterNonWord(ReadOnlySpan<byte> haystack, int position)
    {
        if ((uint)position >= (uint)haystack.Length)
        {
            return position + 1;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            return position + 1;
        }

        return RegexByteClass.IsUtf8Boundary(haystack, position) &&
            Rune.DecodeFromUtf8(haystack[position..], out _, out int length) == OperationStatus.Done
            ? position + length
            : position + 1;
    }

    private static int SkipToUtf8Boundary(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length && !RegexByteClass.IsUtf8Boundary(haystack, position))
        {
            position++;
        }

        return position;
    }
}
