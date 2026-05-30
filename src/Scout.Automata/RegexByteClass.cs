using System;

namespace Scout;

internal static class RegexByteClass
{
    public static bool AtomMatches(
        byte value,
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        bool caseInsensitive,
        bool dotMatchesNewline)
    {
        return kind switch
        {
            RegexSyntaxKind.Literal => expression.Length > 0 && ByteEquals(value, expression[0], caseInsensitive),
            RegexSyntaxKind.Dot => dotMatchesNewline || value != (byte)'\n',
            RegexSyntaxKind.AnyClass => true,
            RegexSyntaxKind.CharacterClass => ClassMatches(value, expression, caseInsensitive),
            RegexSyntaxKind.DigitClass => IsAsciiDigitByte(value),
            RegexSyntaxKind.NotDigitClass => value != (byte)'\n' && !IsAsciiDigitByte(value),
            RegexSyntaxKind.WordClass => IsAsciiWordByte(value),
            RegexSyntaxKind.NotWordClass => value != (byte)'\n' && !IsAsciiWordByte(value),
            RegexSyntaxKind.WhitespaceClass => value != (byte)'\n' && IsRegexWhitespaceByte(value),
            RegexSyntaxKind.NotWhitespaceClass => value != (byte)'\n' && !IsRegexWhitespaceByte(value),
            _ => false,
        };
    }

    public static bool PredicateMatches(ReadOnlySpan<byte> haystack, int position, RegexSyntaxKind kind, bool multiLine)
    {
        return kind switch
        {
            RegexSyntaxKind.StartAnchor => position == 0 || multiLine && position > 0 && haystack[position - 1] == (byte)'\n',
            RegexSyntaxKind.EndAnchor => position == haystack.Length || multiLine && position < haystack.Length && haystack[position] == (byte)'\n',
            RegexSyntaxKind.WordBoundary => IsRegexWordBoundary(haystack, position),
            RegexSyntaxKind.NotWordBoundary => !IsRegexWordBoundary(haystack, position),
            RegexSyntaxKind.WordStartBoundary => IsRegexWordStartBoundary(haystack, position),
            RegexSyntaxKind.WordEndBoundary => IsRegexWordEndBoundary(haystack, position),
            _ => false,
        };
    }

    private static bool ClassMatches(byte value, ReadOnlySpan<byte> expression, bool caseInsensitive)
    {
        if (value == (byte)'\n')
        {
            return false;
        }

        bool negated = !expression.IsEmpty && expression[0] == (byte)'^';
        int index = negated ? 1 : 0;
        bool matched = false;
        while (index < expression.Length)
        {
            if (!TryReadClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out byte literal, out bool tokenNegated))
            {
                break;
            }

            if (!tokenNegated &&
                tokenKind == RegexSyntaxKind.Literal &&
                index + 1 < expression.Length &&
                expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (!TryReadClassToken(expression, ref rangeEndIndex, out RegexSyntaxKind rangeEndKind, out byte rangeEndLiteral, out bool rangeEndNegated) ||
                    rangeEndNegated ||
                    rangeEndKind != RegexSyntaxKind.Literal)
                {
                    matched |= ClassTokenMatches(value, tokenKind, literal, tokenNegated, caseInsensitive);
                    continue;
                }

                index = rangeEndIndex;
                byte foldedValue = FoldMaybe(value, caseInsensitive);
                byte foldedStart = FoldMaybe(literal, caseInsensitive);
                byte foldedEnd = FoldMaybe(rangeEndLiteral, caseInsensitive);
                matched |= foldedStart <= foldedValue && foldedValue <= foldedEnd;
                continue;
            }

            matched |= ClassTokenMatches(value, tokenKind, literal, tokenNegated, caseInsensitive);
        }

        return negated ? !matched : matched;
    }

    private static bool TryReadClassToken(
        ReadOnlySpan<byte> expression,
        ref int index,
        out RegexSyntaxKind tokenKind,
        out byte literal,
        out bool tokenNegated)
    {
        tokenKind = RegexSyntaxKind.Literal;
        literal = 0;
        tokenNegated = false;
        if (index >= expression.Length)
        {
            return false;
        }

        if (TryParsePosixClass(expression, index, out tokenKind, out tokenNegated, out int nextIndex))
        {
            index = nextIndex;
            return true;
        }

        if (expression[index] == (byte)'\\' && index + 1 < expression.Length)
        {
            byte escaped = expression[index + 1];
            switch (escaped)
            {
                case (byte)'d':
                    tokenKind = RegexSyntaxKind.DigitClass;
                    index += 2;
                    return true;
                case (byte)'D':
                    tokenKind = RegexSyntaxKind.DigitClass;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'w':
                    tokenKind = RegexSyntaxKind.WordClass;
                    index += 2;
                    return true;
                case (byte)'W':
                    tokenKind = RegexSyntaxKind.WordClass;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'s':
                    tokenKind = RegexSyntaxKind.WhitespaceClass;
                    index += 2;
                    return true;
                case (byte)'S':
                    tokenKind = RegexSyntaxKind.WhitespaceClass;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'t':
                    literal = (byte)'\t';
                    index += 2;
                    return true;
                case (byte)'r':
                    literal = (byte)'\r';
                    index += 2;
                    return true;
                case (byte)'f':
                    literal = (byte)'\f';
                    index += 2;
                    return true;
                default:
                    literal = escaped;
                    index += 2;
                    return true;
            }
        }

        literal = expression[index];
        index++;
        return true;
    }

    private static bool TryParsePosixClass(
        ReadOnlySpan<byte> expression,
        int index,
        out RegexSyntaxKind tokenKind,
        out bool tokenNegated,
        out int nextIndex)
    {
        tokenKind = RegexSyntaxKind.Literal;
        tokenNegated = false;
        nextIndex = index;
        if (index + 4 >= expression.Length ||
            expression[index] != (byte)'[' ||
            expression[index + 1] != (byte)':')
        {
            return false;
        }

        int nameStart = index + 2;
        if (nameStart < expression.Length && expression[nameStart] == (byte)'^')
        {
            tokenNegated = true;
            nameStart++;
        }

        int nameEnd = nameStart;
        while (nameEnd + 1 < expression.Length &&
            !(expression[nameEnd] == (byte)':' && expression[nameEnd + 1] == (byte)']'))
        {
            nameEnd++;
        }

        if (nameEnd == nameStart || nameEnd + 1 >= expression.Length)
        {
            return false;
        }

        if (!TryGetPosixClassKind(expression[nameStart..nameEnd], out tokenKind))
        {
            return false;
        }

        nextIndex = nameEnd + 2;
        return true;
    }

    private static bool TryGetPosixClassKind(ReadOnlySpan<byte> name, out RegexSyntaxKind tokenKind)
    {
        tokenKind = RegexSyntaxKind.Literal;
        if (name.SequenceEqual("digit"u8))
        {
            tokenKind = RegexSyntaxKind.DigitClass;
            return true;
        }

        if (name.SequenceEqual("word"u8))
        {
            tokenKind = RegexSyntaxKind.WordClass;
            return true;
        }

        if (name.SequenceEqual("space"u8))
        {
            tokenKind = RegexSyntaxKind.WhitespaceClass;
            return true;
        }

        if (name.SequenceEqual("alpha"u8))
        {
            tokenKind = RegexSyntaxKind.LetterClass;
            return true;
        }

        if (name.SequenceEqual("alnum"u8))
        {
            tokenKind = RegexSyntaxKind.AlphanumericClass;
            return true;
        }

        return false;
    }

    private static bool ClassTokenMatches(byte value, RegexSyntaxKind tokenKind, byte literal, bool tokenNegated, bool caseInsensitive)
    {
        bool matched = tokenKind switch
        {
            RegexSyntaxKind.DigitClass => IsAsciiDigitByte(value),
            RegexSyntaxKind.WordClass => IsAsciiWordByte(value),
            RegexSyntaxKind.WhitespaceClass => IsRegexWhitespaceByte(value),
            RegexSyntaxKind.LetterClass => IsAsciiAlphaByte(value),
            RegexSyntaxKind.AlphanumericClass => IsAsciiAlphaByte(value) || IsAsciiDigitByte(value),
            _ => ByteEquals(value, literal, caseInsensitive),
        };
        return tokenNegated ? !matched : matched;
    }

    private static bool IsRegexWordBoundary(ReadOnlySpan<byte> haystack, int position)
    {
        bool leftIsWord = position > 0 && IsAsciiWordByte(haystack[position - 1]);
        bool rightIsWord = position < haystack.Length && IsAsciiWordByte(haystack[position]);
        return leftIsWord != rightIsWord;
    }

    private static bool IsRegexWordStartBoundary(ReadOnlySpan<byte> haystack, int position)
    {
        bool leftIsWord = position > 0 && IsAsciiWordByte(haystack[position - 1]);
        bool rightIsWord = position < haystack.Length && IsAsciiWordByte(haystack[position]);
        return !leftIsWord && rightIsWord;
    }

    private static bool IsRegexWordEndBoundary(ReadOnlySpan<byte> haystack, int position)
    {
        bool leftIsWord = position > 0 && IsAsciiWordByte(haystack[position - 1]);
        bool rightIsWord = position < haystack.Length && IsAsciiWordByte(haystack[position]);
        return leftIsWord && !rightIsWord;
    }

    private static bool IsAsciiAlphaByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_'
            || (value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z');
    }

    private static bool IsAsciiDigitByte(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsRegexWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static bool ByteEquals(byte left, byte right, bool caseInsensitive)
    {
        return FoldMaybe(left, caseInsensitive) == FoldMaybe(right, caseInsensitive);
    }

    private static byte FoldMaybe(byte value, bool caseInsensitive)
    {
        return caseInsensitive && value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }
}
