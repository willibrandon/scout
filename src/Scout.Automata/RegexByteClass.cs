using System.Buffers;
using System.Text;

namespace Scout;

internal static class RegexByteClass
{
    private static readonly bool[] AsciiCaseFoldNeedsUnicodeScalar = CreateAsciiCaseFoldNeedsUnicodeScalar();

    public static bool AtomMatches(
        byte value,
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator)
    {
        return kind switch
        {
            RegexSyntaxKind.Literal => expression.Length > 0 && ByteEquals(value, expression[0], caseInsensitive),
            RegexSyntaxKind.Dot => dotMatchesNewline || (crlf
                ? value != (byte)'\n' && value != (byte)'\r'
                : value != lineTerminator),
            RegexSyntaxKind.AnyClass => true,
            RegexSyntaxKind.CharacterClass => ClassMatches(value, expression, caseInsensitive, multiLine, crlf, lineTerminator),
            RegexSyntaxKind.ByteClass => ByteClassMatches(value, expression),
            RegexSyntaxKind.DigitClass => IsAsciiDigitByte(value),
            RegexSyntaxKind.NotDigitClass => !IsAsciiDigitByte(value),
            RegexSyntaxKind.WordClass => IsAsciiWordByte(value),
            RegexSyntaxKind.NotWordClass => !IsAsciiWordByte(value),
            RegexSyntaxKind.WhitespaceClass => IsRegexWhitespaceByte(value),
            RegexSyntaxKind.NotWhitespaceClass => !IsRegexWhitespaceByte(value),
            _ => false,
        };
    }

    public static bool TryGetAtomMatchLength(
        ReadOnlySpan<byte> haystack,
        int position,
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        bool utf8,
        bool unicodeClasses,
        out int length)
    {
        length = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        bool requiresUtf8ScalarMatch = RequiresUtf8ScalarMatch(kind, expression, utf8, caseInsensitive, unicodeClasses);
        bool canUseAsciiScalarFastPath = CanUseAsciiScalarFastPath(kind, expression);
        return TryGetAtomMatchLength(
            haystack,
            position,
            kind,
            expression,
            caseInsensitive,
            multiLine,
            dotMatchesNewline,
            crlf,
            lineTerminator,
            unicodeClasses,
            requiresUtf8ScalarMatch,
            canUseAsciiScalarFastPath,
            out length);
    }

    public static bool TryGetAtomMatchLength(
        ReadOnlySpan<byte> haystack,
        int position,
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        bool unicodeClasses,
        bool requiresUtf8ScalarMatch,
        bool canUseAsciiScalarFastPath,
        out int length)
    {
        length = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        if (requiresUtf8ScalarMatch)
        {
            if (haystack[position] <= 0x7F &&
                canUseAsciiScalarFastPath)
            {
                if (!AtomMatches(
                    haystack[position],
                    kind,
                    expression,
                    caseInsensitive,
                    multiLine,
                    dotMatchesNewline,
                    crlf,
                    lineTerminator))
                {
                    return false;
                }

                length = 1;
                return true;
            }

            if (!IsUtf8Boundary(haystack, position) ||
                !TryDecodeUtf8Scalar(haystack, position, out Rune rune, out length) ||
                !AtomMatches(
                    rune,
                    kind,
                    expression,
                    caseInsensitive,
                    multiLine,
                    dotMatchesNewline,
                    crlf,
                    lineTerminator,
                    unicodeClasses))
            {
                length = 0;
                return false;
            }

            return true;
        }

        if (!AtomMatches(
            haystack[position],
            kind,
            expression,
            caseInsensitive,
            multiLine,
            dotMatchesNewline,
            crlf,
            lineTerminator))
        {
            return false;
        }

        length = 1;
        return true;
    }

    internal static bool CanUseAsciiScalarFastPath(RegexSyntaxKind kind, ReadOnlySpan<byte> expression)
    {
        return kind switch
        {
            RegexSyntaxKind.Literal => expression.Length == 1 && expression[0] <= 0x7F,
            RegexSyntaxKind.CharacterClass => IsAscii(expression) && !ContainsUnicodePropertyClassToken(expression),
            RegexSyntaxKind.UnicodePropertyClass or RegexSyntaxKind.NotUnicodePropertyClass => false,
            _ => true,
        };
    }

    private static bool IsAscii(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool ContainsUnicodePropertyClassToken(ReadOnlySpan<byte> expression)
    {
        int index = IsNegatedClass(expression) ? 1 : 0;
        while (index < expression.Length)
        {
            if (!TryReadClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out _, out _))
            {
                return false;
            }

            if (tokenKind is RegexSyntaxKind.UnicodePropertyClass or RegexSyntaxKind.NotUnicodePropertyClass)
            {
                return true;
            }
        }

        return false;
    }

    public static bool PredicateMatches(
        ReadOnlySpan<byte> haystack,
        int position,
        RegexSyntaxKind kind,
        bool multiLine,
        bool crlf,
        byte lineTerminator,
        bool utf8,
        bool unicodeClasses)
    {
        bool useUnicodeWord = unicodeClasses;
        if ((utf8 || useUnicodeWord) && IsBoundaryPredicate(kind) && !IsUtf8Boundary(haystack, position))
        {
            return false;
        }

        return kind switch
        {
            RegexSyntaxKind.StartAnchor => IsStartAnchorMatch(haystack, position, multiLine, crlf, lineTerminator),
            RegexSyntaxKind.EndAnchor => IsEndAnchorMatch(haystack, position, multiLine, crlf, lineTerminator),
            RegexSyntaxKind.AbsoluteStartAnchor => position == 0,
            RegexSyntaxKind.AbsoluteEndAnchor => position == haystack.Length,
            RegexSyntaxKind.WordBoundary => IsRegexWordBoundary(haystack, position, useUnicodeWord),
            RegexSyntaxKind.NotWordBoundary => IsRegexNotWordBoundary(haystack, position, useUnicodeWord),
            RegexSyntaxKind.WordStartBoundary => IsRegexWordStartBoundary(haystack, position, useUnicodeWord),
            RegexSyntaxKind.WordEndBoundary => IsRegexWordEndBoundary(haystack, position, useUnicodeWord),
            RegexSyntaxKind.WordStartHalfBoundary => IsRegexWordStartHalfBoundary(haystack, position, useUnicodeWord),
            RegexSyntaxKind.WordEndHalfBoundary => IsRegexWordEndHalfBoundary(haystack, position, useUnicodeWord),
            _ => false,
        };
    }

    public static bool IsUtf8Boundary(ReadOnlySpan<byte> bytes, int position)
    {
        if (position <= 0 || position >= bytes.Length)
        {
            return true;
        }

        int firstCandidate = Math.Max(0, position - 3);
        for (int index = firstCandidate; index < position; index++)
        {
            if (TryGetUtf8ScalarLength(bytes, index, out int length) &&
                length > 1 &&
                position < index + length)
            {
                return false;
            }
        }

        return true;
    }

    public static bool RequiresUtf8ScalarMatch(RegexSyntaxKind kind, ReadOnlySpan<byte> expression, bool utf8, bool caseInsensitive, bool unicodeClasses)
    {
        bool codepointMode = utf8 || unicodeClasses;
        return kind switch
        {
            RegexSyntaxKind.Dot
                or RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.UnicodePropertyClass
                or RegexSyntaxKind.NotUnicodePropertyClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.NotWhitespaceClass => codepointMode,
            RegexSyntaxKind.DigitClass
                or RegexSyntaxKind.WordClass
                or RegexSyntaxKind.WhitespaceClass => unicodeClasses,
            RegexSyntaxKind.Literal => expression.Length > 1 ||
                unicodeClasses && caseInsensitive && LiteralCaseFoldMayNeedUnicodeScalar(expression),
            RegexSyntaxKind.CharacterClass => codepointMode && IsNegatedClass(expression) ||
                unicodeClasses && (ContainsScalarClassToken(expression) || caseInsensitive && ClassCaseFoldMayNeedUnicodeScalar(expression)),
            _ => false,
        };
    }

    private static bool LiteralCaseFoldMayNeedUnicodeScalar(ReadOnlySpan<byte> expression)
    {
        if (expression.Length != 1)
        {
            return true;
        }

        return LiteralByteCaseFoldMayNeedUnicodeScalar(expression[0]);
    }

    private static bool LiteralByteCaseFoldMayNeedUnicodeScalar(byte literal)
    {
        return literal > 0x7F || AsciiCaseFoldNeedsUnicodeScalar[literal];
    }

    private static bool ClassCaseFoldMayNeedUnicodeScalar(ReadOnlySpan<byte> expression)
    {
        int index = IsNegatedClass(expression) ? 1 : 0;
        while (index < expression.Length)
        {
            if (!TryReadClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out byte literal, out bool tokenNegated))
            {
                return false;
            }

            if (!tokenNegated &&
                tokenKind == RegexSyntaxKind.Literal &&
                index + 1 < expression.Length &&
                expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (TryReadClassToken(expression, ref rangeEndIndex, out RegexSyntaxKind rangeEndKind, out byte rangeEndLiteral, out bool rangeEndNegated) &&
                    !rangeEndNegated &&
                    rangeEndKind == RegexSyntaxKind.Literal)
                {
                    if (ClassRangeCaseFoldMayNeedUnicodeScalar(literal, rangeEndLiteral))
                    {
                        return true;
                    }

                    index = rangeEndIndex;
                    continue;
                }
            }

            if (tokenKind == RegexSyntaxKind.Literal && LiteralByteCaseFoldMayNeedUnicodeScalar(literal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ClassRangeCaseFoldMayNeedUnicodeScalar(byte start, byte end)
    {
        byte foldedStart = FoldMaybe(start, caseInsensitive: true);
        byte foldedEnd = FoldMaybe(end, caseInsensitive: true);
        for (byte value = (byte)'a'; value <= (byte)'z'; value++)
        {
            if (foldedStart <= value &&
                value <= foldedEnd &&
                AsciiCaseFoldNeedsUnicodeScalar[value])
            {
                return true;
            }
        }

        return false;
    }

    private static bool[] CreateAsciiCaseFoldNeedsUnicodeScalar()
    {
        bool[] result = new bool[128];
        for (int value = 0; value < result.Length; value++)
        {
            List<Rune> equivalents = [];
            RegexUnicodeTables.AddSimpleCaseFoldEquivalents(new Rune(value), equivalents);
            for (int index = 0; index < equivalents.Count; index++)
            {
                if (!equivalents[index].IsAscii)
                {
                    result[value] = true;
                    break;
                }
            }
        }

        return result;
    }

    private static bool AtomMatches(
        Rune value,
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        bool unicodeClasses)
    {
        return kind switch
        {
            RegexSyntaxKind.Literal => RuneEqualsLiteral(value, expression, caseInsensitive, unicodeClasses),
            RegexSyntaxKind.Dot => dotMatchesNewline || !IsLineTerminator(value, crlf, lineTerminator),
            RegexSyntaxKind.AnyClass => true,
            RegexSyntaxKind.CharacterClass => ClassMatches(value, expression, caseInsensitive, multiLine, crlf, lineTerminator, unicodeClasses),
            RegexSyntaxKind.DigitClass => IsRegexDigitRune(value, unicodeClasses),
            RegexSyntaxKind.NotDigitClass => !IsRegexDigitRune(value, unicodeClasses),
            RegexSyntaxKind.WordClass => IsRegexWordRune(value, unicodeClasses),
            RegexSyntaxKind.NotWordClass => !IsRegexWordRune(value, unicodeClasses),
            RegexSyntaxKind.WhitespaceClass => IsRegexWhitespaceRune(value, unicodeClasses),
            RegexSyntaxKind.NotWhitespaceClass => !IsRegexWhitespaceRune(value, unicodeClasses),
            RegexSyntaxKind.UnicodePropertyClass => unicodeClasses && IsUnicodePropertyRune(value, expression, caseInsensitive),
            RegexSyntaxKind.NotUnicodePropertyClass => unicodeClasses && !IsUnicodePropertyRune(value, expression, caseInsensitive),
            _ => false,
        };
    }

    private static bool TryDecodeUtf8Scalar(ReadOnlySpan<byte> bytes, int position, out Rune rune, out int length)
    {
        OperationStatus status = Rune.DecodeFromUtf8(bytes[position..], out rune, out length);
        return status == OperationStatus.Done;
    }

    internal static bool TryGetUtf8ScalarLength(ReadOnlySpan<byte> bytes, int position, out int length)
    {
        length = 0;
        if (position >= bytes.Length)
        {
            return false;
        }

        byte first = bytes[position];
        if (first <= 0x7F)
        {
            length = 1;
            return true;
        }

        if (first is >= 0xC2 and <= 0xDF)
        {
            return TryRequireUtf8Continuations(bytes, position, 2, out length);
        }

        if (first == 0xE0)
        {
            return position + 1 < bytes.Length &&
                bytes[position + 1] is >= 0xA0 and <= 0xBF &&
                TryRequireUtf8Continuations(bytes, position, 3, out length);
        }

        if (first is >= 0xE1 and <= 0xEC or >= 0xEE and <= 0xEF)
        {
            return TryRequireUtf8Continuations(bytes, position, 3, out length);
        }

        if (first == 0xED)
        {
            return position + 1 < bytes.Length &&
                bytes[position + 1] is >= 0x80 and <= 0x9F &&
                TryRequireUtf8Continuations(bytes, position, 3, out length);
        }

        if (first == 0xF0)
        {
            return position + 1 < bytes.Length &&
                bytes[position + 1] is >= 0x90 and <= 0xBF &&
                TryRequireUtf8Continuations(bytes, position, 4, out length);
        }

        if (first is >= 0xF1 and <= 0xF3)
        {
            return TryRequireUtf8Continuations(bytes, position, 4, out length);
        }

        if (first == 0xF4)
        {
            return position + 1 < bytes.Length &&
                bytes[position + 1] is >= 0x80 and <= 0x8F &&
                TryRequireUtf8Continuations(bytes, position, 4, out length);
        }

        return false;
    }

    private static bool TryRequireUtf8Continuations(ReadOnlySpan<byte> bytes, int position, int requiredLength, out int length)
    {
        length = 0;
        if (position + requiredLength > bytes.Length)
        {
            return false;
        }

        for (int index = position + 1; index < position + requiredLength; index++)
        {
            if (!IsUtf8Continuation(bytes[index]))
            {
                return false;
            }
        }

        length = requiredLength;
        return true;
    }

    private static bool IsUtf8Continuation(byte value)
    {
        return value is >= 0x80 and <= 0xBF;
    }

    internal static bool IsNegatedClass(ReadOnlySpan<byte> expression)
    {
        return !expression.IsEmpty && expression[0] == (byte)'^';
    }

    private static bool ContainsScalarClassToken(ReadOnlySpan<byte> expression)
    {
        int index = IsNegatedClass(expression) ? 1 : 0;
        while (index < expression.Length)
        {
            if (TryReadScalarEscape(expression, index, out int scalarNextIndex, out int scalar))
            {
                if (scalar > 0x7F)
                {
                    return true;
                }

                index = scalarNextIndex;
                continue;
            }

            if (!TryReadClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out _, out _))
            {
                return false;
            }

            if (tokenKind is RegexSyntaxKind.DigitClass
                or RegexSyntaxKind.WordClass
                or RegexSyntaxKind.WhitespaceClass
                or RegexSyntaxKind.LetterClass
                or RegexSyntaxKind.AlphanumericClass
                or RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.UnicodePropertyClass
                or RegexSyntaxKind.NotUnicodePropertyClass)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadScalarEscape(ReadOnlySpan<byte> expression, int index, out int nextIndex, out int scalar)
    {
        nextIndex = index;
        scalar = 0;
        if (index + 1 >= expression.Length ||
            expression[index] != (byte)'\\' ||
            expression[index + 1] is not ((byte)'x' or (byte)'u'))
        {
            return false;
        }

        byte escaped = expression[index + 1];
        int scan = index + 2;
        if (escaped == (byte)'x' &&
            scan + 1 < expression.Length &&
            TryReadHexByte(expression[scan], expression[scan + 1], out byte byteValue))
        {
            nextIndex = scan + 2;
            scalar = byteValue;
            return true;
        }

        return TryReadBracedHexScalar(expression, ref scan, out scalar, out nextIndex);
    }

    private static bool TryReadBracedHexScalar(ReadOnlySpan<byte> expression, ref int index, out int scalar, out int nextIndex)
    {
        scalar = 0;
        nextIndex = index;
        if (index >= expression.Length || expression[index] != (byte)'{')
        {
            return false;
        }

        int scan = index + 1;
        int parsed = 0;
        int digits = 0;
        while (scan < expression.Length && expression[scan] != (byte)'}')
        {
            if (!TryGetHexValue(expression[scan], out int digit))
            {
                return false;
            }

            parsed = (parsed * 16) + digit;
            if (parsed > 0x10FFFF)
            {
                return false;
            }

            digits++;
            scan++;
        }

        if (digits == 0 || scan >= expression.Length || expression[scan] != (byte)'}' || !Rune.IsValid(parsed))
        {
            return false;
        }

        nextIndex = scan + 1;
        scalar = parsed;
        return true;
    }

    private static bool ContainsLiteralClassToken(ReadOnlySpan<byte> expression)
    {
        int index = IsNegatedClass(expression) ? 1 : 0;
        while (index < expression.Length)
        {
            if (!TryReadClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out _, out _))
            {
                return false;
            }

            if (tokenKind == RegexSyntaxKind.Literal)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBoundaryPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
    }

    private static bool IsStartAnchorMatch(ReadOnlySpan<byte> haystack, int position, bool multiLine, bool crlf, byte lineTerminator)
    {
        if (position == 0)
        {
            return true;
        }

        if (!multiLine)
        {
            return false;
        }

        byte previous = haystack[position - 1];
        if (!crlf)
        {
            return previous == lineTerminator;
        }

        if (previous == (byte)'\n')
        {
            return true;
        }

        return previous == (byte)'\r' &&
            (position >= haystack.Length || haystack[position] != (byte)'\n');
    }

    private static bool IsEndAnchorMatch(ReadOnlySpan<byte> haystack, int position, bool multiLine, bool crlf, byte lineTerminator)
    {
        if (position == haystack.Length)
        {
            return true;
        }

        if (!multiLine)
        {
            return false;
        }

        byte current = haystack[position];
        if (!crlf)
        {
            return current == lineTerminator;
        }

        if (current == (byte)'\n')
        {
            return position == 0 || haystack[position - 1] != (byte)'\r';
        }

        return current == (byte)'\r';
    }

    private static bool ClassMatches(byte value, ReadOnlySpan<byte> expression, bool caseInsensitive, bool multiLine, bool crlf, byte lineTerminator)
    {
        bool negated = IsNegatedClass(expression);
        ReadOnlySpan<byte> body = negated ? expression[1..] : expression;
        bool matched = ClassIntersectionMatches(value, body, caseInsensitive);
        return negated ? !matched : matched;
    }

    private static bool ClassIntersectionMatches(byte value, ReadOnlySpan<byte> expression, bool caseInsensitive)
    {
        if (!TryFindClassIntersectionOperator(expression, out int operatorIndex))
        {
            return ClassUnionMatches(value, expression, caseInsensitive);
        }

        return ClassUnionMatches(value, expression[..operatorIndex], caseInsensitive) &&
            ClassIntersectionMatches(value, expression[(operatorIndex + 2)..], caseInsensitive);
    }

    private static bool ClassUnionMatches(byte value, ReadOnlySpan<byte> expression, bool caseInsensitive)
    {
        int index = 0;
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

        return matched;
    }

    private static bool ClassMatches(
        Rune value,
        ReadOnlySpan<byte> expression,
        bool caseInsensitive,
        bool multiLine,
        bool crlf,
        byte lineTerminator,
        bool unicodeClasses)
    {
        bool negated = IsNegatedClass(expression);
        ReadOnlySpan<byte> body = negated ? expression[1..] : expression;
        bool matched = ClassIntersectionMatches(value, body, caseInsensitive, unicodeClasses);
        return negated ? !matched : matched;
    }

    private static bool ClassIntersectionMatches(Rune value, ReadOnlySpan<byte> expression, bool caseInsensitive, bool unicodeClasses)
    {
        if (!TryFindClassIntersectionOperator(expression, out int operatorIndex))
        {
            return ClassUnionMatches(value, expression, caseInsensitive, unicodeClasses);
        }

        return ClassUnionMatches(value, expression[..operatorIndex], caseInsensitive, unicodeClasses) &&
            ClassIntersectionMatches(value, expression[(operatorIndex + 2)..], caseInsensitive, unicodeClasses);
    }

    private static bool ClassUnionMatches(Rune value, ReadOnlySpan<byte> expression, bool caseInsensitive, bool unicodeClasses)
    {
        int index = 0;
        bool matched = false;
        while (index < expression.Length)
        {
            if (!TryReadClassScalarToken(expression, ref index, out RegexSyntaxKind tokenKind, out int literal, out bool tokenNegated))
            {
                break;
            }

            if (!tokenNegated &&
                tokenKind == RegexSyntaxKind.Literal &&
                index + 1 < expression.Length &&
                expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (!TryReadClassScalarToken(expression, ref rangeEndIndex, out RegexSyntaxKind rangeEndKind, out int rangeEndLiteral, out bool rangeEndNegated) ||
                    rangeEndNegated ||
                    rangeEndKind != RegexSyntaxKind.Literal)
                {
                    matched |= ClassTokenMatches(value, tokenKind, literal, tokenNegated, caseInsensitive, unicodeClasses);
                    continue;
                }

                index = rangeEndIndex;
                matched |= ClassRangeMatches(value, literal, rangeEndLiteral, caseInsensitive, unicodeClasses);
                continue;
            }

            matched |= ClassTokenMatches(value, tokenKind, literal, tokenNegated, caseInsensitive, unicodeClasses);
        }

        return matched;
    }

    internal static bool TryFindClassIntersectionOperator(ReadOnlySpan<byte> expression, out int operatorIndex)
    {
        for (int index = 0; index + 1 < expression.Length; index++)
        {
            if (expression[index] == (byte)'\\')
            {
                index++;
                continue;
            }

            if (TryParsePosixClass(expression, index, out _, out _, out int nextIndex))
            {
                index = nextIndex - 1;
                continue;
            }

            if (expression[index] == (byte)'&' &&
                expression[index + 1] == (byte)'&' &&
                index > 0 &&
                index + 2 < expression.Length)
            {
                operatorIndex = index;
                return true;
            }
        }

        operatorIndex = -1;
        return false;
    }

    internal static bool TryReadClassToken(
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
                case (byte)'x':
                case (byte)'u':
                    int hexIndex = index + 2;
                    if (TryReadEscapedHexByte(expression, ref hexIndex, escaped, out literal))
                    {
                        index = hexIndex;
                        return true;
                    }

                    literal = escaped;
                    index += 2;
                    return true;
                case (byte)'p':
                    if (TryReadUnicodePropertyClassToken(
                        expression,
                        index + 2,
                        negated: false,
                        out tokenKind,
                        out literal,
                        out tokenNegated,
                        out int propertyNextIndex))
                    {
                        index = propertyNextIndex;
                        return true;
                    }

                    literal = escaped;
                    index += 2;
                    return true;
                case (byte)'P':
                    if (TryReadUnicodePropertyClassToken(
                        expression,
                        index + 2,
                        negated: true,
                        out tokenKind,
                        out literal,
                        out tokenNegated,
                        out int negatedPropertyNextIndex))
                    {
                        index = negatedPropertyNextIndex;
                        return true;
                    }

                    literal = escaped;
                    index += 2;
                    return true;
                case (byte)'t':
                    literal = (byte)'\t';
                    index += 2;
                    return true;
                case (byte)'n':
                    literal = (byte)'\n';
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

    private static bool TryReadClassScalarToken(
        ReadOnlySpan<byte> expression,
        ref int index,
        out RegexSyntaxKind tokenKind,
        out int literal,
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
                case (byte)'x':
                case (byte)'u':
                    if (TryReadScalarEscape(expression, index, out int scalarNextIndex, out int scalar))
                    {
                        literal = scalar;
                        index = scalarNextIndex;
                        return true;
                    }

                    literal = escaped;
                    index += 2;
                    return true;
                case (byte)'p':
                    if (TryReadUnicodePropertyClassToken(
                        expression,
                        index + 2,
                        negated: false,
                        out tokenKind,
                        out byte propertyLiteral,
                        out tokenNegated,
                        out int propertyNextIndex))
                    {
                        literal = propertyLiteral;
                        index = propertyNextIndex;
                        return true;
                    }

                    literal = escaped;
                    index += 2;
                    return true;
                case (byte)'P':
                    if (TryReadUnicodePropertyClassToken(
                        expression,
                        index + 2,
                        negated: true,
                        out tokenKind,
                        out byte negatedPropertyLiteral,
                        out tokenNegated,
                        out int negatedPropertyNextIndex))
                    {
                        literal = negatedPropertyLiteral;
                        index = negatedPropertyNextIndex;
                        return true;
                    }

                    literal = escaped;
                    index += 2;
                    return true;
                case (byte)'t':
                    literal = '\t';
                    index += 2;
                    return true;
                case (byte)'n':
                    literal = '\n';
                    index += 2;
                    return true;
                case (byte)'r':
                    literal = '\r';
                    index += 2;
                    return true;
                case (byte)'f':
                    literal = '\f';
                    index += 2;
                    return true;
                default:
                    literal = escaped;
                    index += 2;
                    return true;
            }
        }

        if (TryDecodeUtf8Scalar(expression, index, out Rune rune, out int length))
        {
            literal = rune.Value;
            index += length;
            return true;
        }

        literal = expression[index];
        index++;
        return true;
    }

    internal static bool TryReadEscapedHexByte(ReadOnlySpan<byte> expression, ref int index, byte escaped, out byte value)
    {
        value = 0;
        if (escaped == (byte)'x')
        {
            if (index + 1 < expression.Length &&
                TryReadHexByte(expression[index], expression[index + 1], out value))
            {
                index += 2;
                return true;
            }

            return TryReadBracedHexByte(expression, ref index, out value);
        }

        return escaped == (byte)'u' && TryReadBracedHexByte(expression, ref index, out value);
    }

    private static bool ByteClassMatches(byte value, ReadOnlySpan<byte> expression)
    {
        for (int index = 0; index + 1 < expression.Length; index += 2)
        {
            if (expression[index] <= value && value <= expression[index + 1])
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadBracedHexByte(ReadOnlySpan<byte> expression, ref int index, out byte value)
    {
        value = 0;
        if (index >= expression.Length || expression[index] != (byte)'{')
        {
            return false;
        }

        int scan = index + 1;
        int parsed = 0;
        int digits = 0;
        while (scan < expression.Length && expression[scan] != (byte)'}')
        {
            if (!TryGetHexValue(expression[scan], out int digit))
            {
                return false;
            }

            parsed = (parsed * 16) + digit;
            if (parsed > byte.MaxValue)
            {
                return false;
            }

            digits++;
            scan++;
        }

        if (digits == 0 || scan >= expression.Length || expression[scan] != (byte)'}')
        {
            return false;
        }

        value = (byte)parsed;
        index = scan + 1;
        return true;
    }

    private static bool TryReadHexByte(byte high, byte low, out byte value)
    {
        value = 0;
        if (!TryGetHexValue(high, out int highValue) ||
            !TryGetHexValue(low, out int lowValue))
        {
            return false;
        }

        value = (byte)((highValue << 4) | lowValue);
        return true;
    }

    private static bool TryGetHexValue(byte value, out int digit)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            digit = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            digit = value - (byte)'A' + 10;
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            digit = value - (byte)'a' + 10;
            return true;
        }

        digit = 0;
        return false;
    }

    private static bool TryReadUnicodePropertyClassToken(
        ReadOnlySpan<byte> expression,
        int index,
        bool negated,
        out RegexSyntaxKind tokenKind,
        out byte literal,
        out bool tokenNegated,
        out int nextIndex)
    {
        tokenKind = RegexSyntaxKind.Literal;
        literal = 0;
        tokenNegated = false;
        nextIndex = index;
        if (index >= expression.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> name;
        if (expression[index] == (byte)'{')
        {
            int nameStart = index + 1;
            int nameEnd = nameStart;
            while (nameEnd < expression.Length && expression[nameEnd] != (byte)'}')
            {
                nameEnd++;
            }

            if (nameEnd >= expression.Length || nameEnd == nameStart)
            {
                return false;
            }

            name = expression[nameStart..nameEnd];
            nextIndex = nameEnd + 1;
        }
        else
        {
            name = expression.Slice(index, 1);
            nextIndex = index + 1;
        }

        if (RegexUnicodePropertyNames.NameEquals(name, "any"))
        {
            tokenKind = RegexSyntaxKind.AnyClass;
            tokenNegated = negated;
            return true;
        }

        if (!RegexUnicodePropertyNames.TryGetKind(name, out RegexUnicodePropertyKind propertyKind))
        {
            return false;
        }

        tokenKind = negated ? RegexSyntaxKind.NotUnicodePropertyClass : RegexSyntaxKind.UnicodePropertyClass;
        literal = (byte)propertyKind;
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
            RegexSyntaxKind.AnyClass => true,
            RegexSyntaxKind.UnicodePropertyClass or RegexSyntaxKind.NotUnicodePropertyClass => false,
            _ => ByteEquals(value, literal, caseInsensitive),
        };
        return tokenNegated ? !matched : matched;
    }

    private static bool ClassTokenMatches(
        Rune value,
        RegexSyntaxKind tokenKind,
        int literal,
        bool tokenNegated,
        bool caseInsensitive,
        bool unicodeClasses)
    {
        bool matched = tokenKind switch
        {
            RegexSyntaxKind.DigitClass => IsRegexDigitRune(value, unicodeClasses),
            RegexSyntaxKind.WordClass => IsRegexWordRune(value, unicodeClasses),
            RegexSyntaxKind.WhitespaceClass => IsRegexWhitespaceRune(value, unicodeClasses),
            RegexSyntaxKind.LetterClass => IsRegexAlphabeticRune(value, unicodeClasses),
            RegexSyntaxKind.AlphanumericClass => IsRegexAlphabeticRune(value, unicodeClasses) || IsRegexDigitRune(value, unicodeClasses),
            RegexSyntaxKind.AnyClass => true,
            RegexSyntaxKind.UnicodePropertyClass => unicodeClasses && IsUnicodePropertyRune(value, (RegexUnicodePropertyKind)literal, caseInsensitive),
            RegexSyntaxKind.NotUnicodePropertyClass => unicodeClasses && !IsUnicodePropertyRune(value, (RegexUnicodePropertyKind)literal, caseInsensitive),
            _ => RuneEqualsLiteral(value, literal, caseInsensitive, unicodeClasses),
        };
        return tokenNegated ? !matched : matched;
    }

    private static bool ClassRangeMatches(Rune value, int start, int end, bool caseInsensitive, bool unicodeClasses)
    {
        if (start <= value.Value && value.Value <= end)
        {
            return true;
        }

        if (!caseInsensitive || !unicodeClasses)
        {
            return false;
        }

        if (start is >= 0 and <= 0x7F &&
            end is >= 0 and <= 0x7F &&
            TryFoldUnicodeScalarToAscii(value, out byte folded))
        {
            byte foldedStart = FoldMaybe((byte)start, caseInsensitive);
            byte foldedEnd = FoldMaybe((byte)end, caseInsensitive);
            return foldedStart <= folded && folded <= foldedEnd;
        }

        return false;
    }

    private static bool RuneEqualsLiteral(Rune value, byte literal, bool caseInsensitive, bool unicodeClasses)
    {
        ReadOnlySpan<byte> literalSpan = stackalloc byte[] { literal };
        return RuneEqualsLiteral(value, literalSpan, caseInsensitive, unicodeClasses);
    }

    private static bool RuneEqualsLiteral(Rune value, int literal, bool caseInsensitive, bool unicodeClasses)
    {
        if (!Rune.IsValid(literal))
        {
            return false;
        }

        var literalRune = new Rune(literal);
        if (!caseInsensitive || !unicodeClasses)
        {
            return value.Value == literalRune.Value;
        }

        return RegexUnicodeTables.IsSimpleCaseFold(value, literalRune);
    }

    private static bool RuneEqualsLiteral(Rune value, ReadOnlySpan<byte> literal, bool caseInsensitive, bool unicodeClasses)
    {
        if (!TryDecodeLiteralRune(literal, out Rune literalRune))
        {
            return false;
        }

        if (!caseInsensitive || !unicodeClasses)
        {
            return value.Value == literalRune.Value;
        }

        return RegexUnicodeTables.IsSimpleCaseFold(value, literalRune);
    }

    private static bool TryDecodeLiteralRune(ReadOnlySpan<byte> literal, out Rune rune)
    {
        if (literal.Length == 1 && literal[0] <= 0x7F)
        {
            rune = new Rune(literal[0]);
            return true;
        }

        return Rune.DecodeFromUtf8(literal, out rune, out int consumed) == OperationStatus.Done &&
            consumed == literal.Length;
    }

    private static bool IsRegexWordBoundary(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        bool leftIsWord = IsRegexWordBefore(haystack, position, unicodeWord);
        bool rightIsWord = IsRegexWordAt(haystack, position, unicodeWord);
        return leftIsWord != rightIsWord;
    }

    private static bool IsRegexNotWordBoundary(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        return IsRegexWordContextValid(haystack, position, unicodeWord) &&
            !IsRegexWordBoundary(haystack, position, unicodeWord);
    }

    private static bool IsRegexWordContextValid(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        return !unicodeWord ||
            IsValidRegexWordContextBefore(haystack, position) &&
            IsValidRegexWordContextAt(haystack, position);
    }

    private static bool IsRegexWordStartBoundary(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        bool leftIsWord = IsRegexWordBefore(haystack, position, unicodeWord);
        bool rightIsWord = IsRegexWordAt(haystack, position, unicodeWord);
        return !leftIsWord && rightIsWord;
    }

    private static bool IsRegexWordEndBoundary(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        bool leftIsWord = IsRegexWordBefore(haystack, position, unicodeWord);
        bool rightIsWord = IsRegexWordAt(haystack, position, unicodeWord);
        return leftIsWord && !rightIsWord;
    }

    private static bool IsRegexWordStartHalfBoundary(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        bool leftIsWord = IsRegexWordBefore(haystack, position, unicodeWord);
        return !leftIsWord;
    }

    private static bool IsRegexWordEndHalfBoundary(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        bool rightIsWord = IsRegexWordAt(haystack, position, unicodeWord);
        return !rightIsWord;
    }

    private static bool IsRegexWordBefore(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        if (position <= 0)
        {
            return false;
        }

        if (!unicodeWord)
        {
            return IsAsciiWordByte(haystack[position - 1]);
        }

        int firstCandidate = Math.Max(0, position - 4);
        for (int index = firstCandidate; index < position; index++)
        {
            if (TryDecodeUtf8Scalar(haystack, index, out Rune rune, out int length) &&
                index + length == position)
            {
                return IsRegexWordRune(rune, unicodeClasses: true);
            }
        }

        return false;
    }

    private static bool IsRegexWordAt(ReadOnlySpan<byte> haystack, int position, bool unicodeWord)
    {
        if (position >= haystack.Length)
        {
            return false;
        }

        if (!unicodeWord)
        {
            return IsAsciiWordByte(haystack[position]);
        }

        return TryDecodeUtf8Scalar(haystack, position, out Rune rune, out _) &&
            IsRegexWordRune(rune, unicodeClasses: true);
    }

    private static bool IsValidRegexWordContextBefore(ReadOnlySpan<byte> haystack, int position)
    {
        if (position <= 0)
        {
            return true;
        }

        int firstCandidate = Math.Max(0, position - 4);
        for (int index = firstCandidate; index < position; index++)
        {
            if (TryDecodeUtf8Scalar(haystack, index, out _, out int length) &&
                index + length == position)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidRegexWordContextAt(ReadOnlySpan<byte> haystack, int position)
    {
        return position >= haystack.Length ||
            TryDecodeUtf8Scalar(haystack, position, out _, out _);
    }

    private static bool IsRegexDigitRune(Rune value, bool unicodeClasses)
    {
        if (!unicodeClasses)
        {
            return value.IsAscii && IsAsciiDigitByte((byte)value.Value);
        }

        return value.IsAscii
            ? IsAsciiDigitByte((byte)value.Value)
            : RegexUnicodeTables.IsDecimalNumber(value);
    }

    private static bool IsRegexWordRune(Rune value, bool unicodeClasses)
    {
        if (!unicodeClasses)
        {
            return value.IsAscii && IsAsciiWordByte((byte)value.Value);
        }

        if (value.IsAscii)
        {
            return IsAsciiWordByte((byte)value.Value);
        }

        return RegexUnicodeTables.IsPerlWord(value);
    }

    private static bool IsRegexWhitespaceRune(Rune value, bool unicodeClasses)
    {
        if (!unicodeClasses)
        {
            return value.IsAscii && IsRegexWhitespaceByte((byte)value.Value);
        }

        return value.IsAscii
            ? IsRegexWhitespaceByte((byte)value.Value)
            : RegexUnicodeTables.IsPerlSpace(value);
    }

    private static bool IsRegexAlphabeticRune(Rune value, bool unicodeClasses)
    {
        if (!unicodeClasses)
        {
            return value.IsAscii && IsAsciiAlphaByte((byte)value.Value);
        }

        return RegexUnicodeTables.IsAlphabetic(value);
    }

    private static bool IsUnicodePropertyRune(Rune value, ReadOnlySpan<byte> expression, bool caseInsensitive)
    {
        return expression.Length == 1 && IsUnicodePropertyRune(value, (RegexUnicodePropertyKind)expression[0], caseInsensitive);
    }

    private static bool IsUnicodePropertyRune(Rune value, RegexUnicodePropertyKind kind, bool caseInsensitive)
    {
        if (caseInsensitive &&
            kind is RegexUnicodePropertyKind.CasedLetter
                or RegexUnicodePropertyKind.LowercaseLetter
                or RegexUnicodePropertyKind.TitlecaseLetter
                or RegexUnicodePropertyKind.UppercaseLetter)
        {
            return RegexUnicodeTables.IsGeneralCategory(RegexUnicodePropertyKind.CasedLetter, value);
        }

        return RegexUnicodeTables.IsGeneralCategory(kind, value) ||
            RegexUnicodeTables.IsBooleanProperty(kind, value) ||
            RegexUnicodeTables.IsBreakProperty(kind, value) ||
            RegexUnicodeTables.IsScript(kind, value) ||
            RegexUnicodeTables.IsScriptExtension(kind, value);
    }

    private static bool TryFoldUnicodeScalarToAscii(Rune value, out byte folded)
    {
        for (byte candidate = (byte)'a'; candidate <= (byte)'z'; candidate++)
        {
            if (RegexUnicodeTables.IsSimpleCaseFold(value, new Rune(candidate)))
            {
                folded = candidate;
                return true;
            }
        }

        folded = 0;
        return false;
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
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static bool IsLineTerminator(byte value, bool crlf, byte lineTerminator)
    {
        return crlf
            ? value is (byte)'\n' or (byte)'\r'
            : value == lineTerminator;
    }

    private static bool IsLineTerminator(Rune value, bool crlf, byte lineTerminator)
    {
        return value.IsAscii && IsLineTerminator((byte)value.Value, crlf, lineTerminator);
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
