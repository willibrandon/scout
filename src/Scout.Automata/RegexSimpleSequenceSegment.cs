namespace Scout;

internal readonly struct RegexSimpleSequenceSegment
{
    private readonly bool[] byteLookup;

    public RegexSimpleSequenceSegment(
        RegexSyntaxKind kind,
        byte[] value,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        int minimum,
        int? maximum,
        bool lazy)
    {
        Kind = kind;
        Value = value;
        CaseInsensitive = caseInsensitive;
        MultiLine = multiLine;
        DotMatchesNewline = dotMatchesNewline;
        Crlf = crlf;
        LineTerminator = lineTerminator;
        Minimum = minimum;
        Maximum = maximum;
        Lazy = lazy;
        MatcherKind = GetMatcherKind(kind, value, caseInsensitive, multiLine, dotMatchesNewline, crlf, lineTerminator);
        Literal = value.Length == 1 ? value[0] : (byte)0;
        byteLookup = MatcherKind == RegexSimpleSequenceByteMatcherKind.Lookup
            ? BuildByteLookup(kind, value, caseInsensitive, multiLine, dotMatchesNewline, crlf, lineTerminator)
            : [];
    }

    public RegexSyntaxKind Kind { get; }

    public byte[] Value { get; }

    public bool CaseInsensitive { get; }

    public bool MultiLine { get; }

    public bool DotMatchesNewline { get; }

    public bool Crlf { get; }

    public byte LineTerminator { get; }

    public int Minimum { get; }

    public int? Maximum { get; }

    public bool Lazy { get; }

    public RegexSimpleSequenceByteMatcherKind MatcherKind { get; }

    public byte Literal { get; }

    public bool AtomMatches(byte value)
    {
        return MatcherKind switch
        {
            RegexSimpleSequenceByteMatcherKind.Literal => value == Literal,
            RegexSimpleSequenceByteMatcherKind.AsciiUppercase => IsAsciiUppercase(value),
            RegexSimpleSequenceByteMatcherKind.AsciiLowercase => IsAsciiLowercase(value),
            RegexSimpleSequenceByteMatcherKind.AsciiLetter => IsAsciiLetter(value),
            RegexSimpleSequenceByteMatcherKind.AsciiDigit => IsAsciiDigit(value),
            RegexSimpleSequenceByteMatcherKind.AsciiIdentifierStart => IsAsciiIdentifierStart(value),
            RegexSimpleSequenceByteMatcherKind.AsciiAlphanumeric => IsAsciiAlphanumeric(value),
            RegexSimpleSequenceByteMatcherKind.AsciiWord => IsAsciiWord(value),
            RegexSimpleSequenceByteMatcherKind.RegexWhitespace => IsRegexWhitespace(value),
            RegexSimpleSequenceByteMatcherKind.Any => true,
            _ => byteLookup[value],
        };
    }

    private static RegexSimpleSequenceByteMatcherKind GetMatcherKind(
        RegexSyntaxKind kind,
        byte[] value,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator)
    {
        return kind switch
        {
            RegexSyntaxKind.Literal when value.Length == 1 && !caseInsensitive => RegexSimpleSequenceByteMatcherKind.Literal,
            RegexSyntaxKind.DigitClass => RegexSimpleSequenceByteMatcherKind.AsciiDigit,
            RegexSyntaxKind.WordClass => RegexSimpleSequenceByteMatcherKind.AsciiWord,
            RegexSyntaxKind.WhitespaceClass => RegexSimpleSequenceByteMatcherKind.RegexWhitespace,
            RegexSyntaxKind.AnyClass => RegexSimpleSequenceByteMatcherKind.Any,
            RegexSyntaxKind.Dot when dotMatchesNewline => RegexSimpleSequenceByteMatcherKind.Any,
            RegexSyntaxKind.CharacterClass when !caseInsensitive &&
                !multiLine &&
                !crlf &&
                lineTerminator == (byte)'\n' &&
                IsAsciiUppercaseClass(value) => RegexSimpleSequenceByteMatcherKind.AsciiUppercase,
            RegexSyntaxKind.CharacterClass when !caseInsensitive &&
                !multiLine &&
                !crlf &&
                lineTerminator == (byte)'\n' &&
                IsAsciiLowercaseClass(value) => RegexSimpleSequenceByteMatcherKind.AsciiLowercase,
            RegexSyntaxKind.CharacterClass when !caseInsensitive &&
                !multiLine &&
                !crlf &&
                lineTerminator == (byte)'\n' &&
                IsAsciiLetterClass(value) => RegexSimpleSequenceByteMatcherKind.AsciiLetter,
            RegexSyntaxKind.CharacterClass when !caseInsensitive &&
                !multiLine &&
                !crlf &&
                lineTerminator == (byte)'\n' &&
                IsAsciiIdentifierStartClass(value) => RegexSimpleSequenceByteMatcherKind.AsciiIdentifierStart,
            RegexSyntaxKind.CharacterClass when !caseInsensitive &&
                !multiLine &&
                !crlf &&
                lineTerminator == (byte)'\n' &&
                IsAsciiAlphanumericClass(value) => RegexSimpleSequenceByteMatcherKind.AsciiAlphanumeric,
            RegexSyntaxKind.CharacterClass when !caseInsensitive &&
                !multiLine &&
                !crlf &&
                lineTerminator == (byte)'\n' &&
                IsAsciiWordClass(value) => RegexSimpleSequenceByteMatcherKind.AsciiWord,
            _ => RegexSimpleSequenceByteMatcherKind.Lookup,
        };
    }

    private static bool IsAsciiUppercaseClass(ReadOnlySpan<byte> value)
    {
        return value.Length == 3 &&
            value[0] == (byte)'A' &&
            value[1] == (byte)'-' &&
            value[2] == (byte)'Z';
    }

    private static bool IsAsciiLowercaseClass(ReadOnlySpan<byte> value)
    {
        return value.Length == 3 &&
            value[0] == (byte)'a' &&
            value[1] == (byte)'-' &&
            value[2] == (byte)'z';
    }

    private static bool IsAsciiLetterClass(ReadOnlySpan<byte> value)
    {
        return value.SequenceEqual("A-Za-z"u8) || value.SequenceEqual("a-zA-Z"u8);
    }

    private static bool IsAsciiIdentifierStartClass(ReadOnlySpan<byte> value)
    {
        return CharacterClassMatchesExactly(value, IsAsciiIdentifierStart);
    }

    private static bool IsAsciiAlphanumericClass(ReadOnlySpan<byte> value)
    {
        return CharacterClassMatchesExactly(value, IsAsciiAlphanumeric);
    }

    private static bool IsAsciiWordClass(ReadOnlySpan<byte> value)
    {
        return CharacterClassMatchesExactly(value, IsAsciiWord);
    }

    public static bool IsAsciiUppercase(byte value)
    {
        return (uint)(value - (byte)'A') <= 25;
    }

    public static bool IsAsciiLowercase(byte value)
    {
        return (uint)(value - (byte)'a') <= 25;
    }

    public static bool IsAsciiLetter(byte value)
    {
        return (uint)((value | 0x20) - (byte)'a') <= 25;
    }

    public static bool IsAsciiDigit(byte value)
    {
        return (uint)(value - (byte)'0') <= 9;
    }

    public static bool IsAsciiIdentifierStart(byte value)
    {
        return IsAsciiLetter(value) || value == (byte)'_';
    }

    public static bool IsAsciiAlphanumeric(byte value)
    {
        return IsAsciiLetter(value) || IsAsciiDigit(value);
    }

    public static bool IsAsciiWord(byte value)
    {
        return IsAsciiAlphanumeric(value) || value == (byte)'_';
    }

    public static bool IsRegexWhitespace(byte value)
    {
        return value is (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r' or (byte)' ';
    }

    private static bool[] BuildByteLookup(
        RegexSyntaxKind kind,
        byte[] value,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator)
    {
        bool[] lookup = new bool[256];
        for (int candidate = 0; candidate <= 0xFF; candidate++)
        {
            lookup[candidate] = RegexByteClass.AtomMatches(
                (byte)candidate,
                kind,
                value,
                caseInsensitive,
                multiLine,
                dotMatchesNewline,
                crlf,
                lineTerminator);
        }

        return lookup;
    }

    private static bool CharacterClassMatchesExactly(ReadOnlySpan<byte> expression, Func<byte, bool> predicate)
    {
        Span<bool> classMatches = stackalloc bool[256];
        if (!TryBuildSimpleAsciiClass(expression, classMatches))
        {
            return false;
        }

        for (int candidate = 0; candidate <= 0xFF; candidate++)
        {
            byte value = (byte)candidate;
            if (classMatches[value] != predicate(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBuildSimpleAsciiClass(ReadOnlySpan<byte> expression, Span<bool> matches)
    {
        if (expression.IsEmpty || expression[0] == (byte)'^')
        {
            return false;
        }

        for (int index = 0; index < expression.Length; index++)
        {
            byte start = expression[index];
            if (start is (byte)'\\' or (byte)'[' or (byte)']' || start > 0x7F)
            {
                return false;
            }

            if (index + 2 < expression.Length &&
                expression[index + 1] == (byte)'-' &&
                expression[index + 2] is not ((byte)'\\' or (byte)'[' or (byte)']') &&
                expression[index + 2] <= 0x7F)
            {
                byte end = expression[index + 2];
                if (start > end)
                {
                    return false;
                }

                for (int value = start; value <= end; value++)
                {
                    matches[value] = true;
                }

                index += 2;
                continue;
            }

            matches[start] = true;
        }

        return true;
    }
}
