using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexAsciiWordBoundaryEngine
{
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
                match = new RegexMatch(start, length);
                return true;
            }
        }

        match = default;
        return false;
    }

    private void CountOrSumAscii(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
    {
        total = 0;
        int position = SkipPartialAsciiWord(haystack, Math.Clamp(startAt, 0, haystack.Length));
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
