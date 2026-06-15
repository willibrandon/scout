using System.Buffers;
using System.Text;

namespace Scout;

internal static class RegexUtf8ByteCompiler
{
    private const int MaxScalar = 0x10FFFF;
    private const int SurrogateStart = 0xD800;
    private const int SurrogateEnd = 0xDFFF;
    private const long RangeSequenceScalarThreshold = 4096;
    private static readonly Lazy<RegexUtf8ByteTrie> UnicodeDigitTrie = new(() => CreateTableTrie(RegexUnicodeTables.DecimalNumberRanges, reversed: false));
    private static readonly Lazy<RegexUtf8ByteTrie> ReversedUnicodeDigitTrie = new(() => CreateTableTrie(RegexUnicodeTables.DecimalNumberRanges, reversed: true));
    private static readonly Lazy<RegexUtf8ByteTrie> UnicodeWhitespaceTrie = new(() => CreateTableTrie(RegexUnicodeTables.PerlSpaceRanges, reversed: false));
    private static readonly Lazy<RegexUtf8ByteTrie> ReversedUnicodeWhitespaceTrie = new(() => CreateTableTrie(RegexUnicodeTables.PerlSpaceRanges, reversed: true));
    private static readonly Lazy<RegexUtf8ByteTrie> UnicodeLetterTrie = new(() => CreateTableTrie(RegexUnicodeTables.GetGeneralCategoryRanges(RegexUnicodePropertyKind.Letter), reversed: false));
    private static readonly Lazy<RegexUtf8ByteTrie> ReversedUnicodeLetterTrie = new(() => CreateTableTrie(RegexUnicodeTables.GetGeneralCategoryRanges(RegexUnicodePropertyKind.Letter), reversed: true));
    private static readonly Lazy<RegexUtf8ByteTrie> UnicodeAlphabeticTrie = new(() => CreateTableTrie(RegexUnicodeTables.AlphabeticRanges, reversed: false));
    private static readonly Lazy<RegexUtf8ByteTrie> ReversedUnicodeAlphabeticTrie = new(() => CreateTableTrie(RegexUnicodeTables.AlphabeticRanges, reversed: true));

    public static string CreateCacheKey(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        bool reversed)
    {
        return $"{(int)kind}|{(options.CaseInsensitive ? 1 : 0)}|{(options.DotMatchesNewline ? 1 : 0)}|{(options.Crlf ? 1 : 0)}|{options.LineTerminator}|{(options.Utf8 ? 1 : 0)}|{(options.UnicodeClasses ? 1 : 0)}|{(reversed ? 1 : 0)}|{Convert.ToHexString(expression)}";
    }

    public static bool TryCreate(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        bool reversed,
        out RegexUtf8ByteTrie? trie)
    {
        trie = null;
        if (TryGetSharedTrie(kind, expression, options, reversed, out trie))
        {
            return true;
        }

        if (!RegexByteClass.RequiresUtf8ScalarMatch(kind, expression, options.Utf8, options.CaseInsensitive, options.UnicodeClasses) ||
            !TryBuildScalarRanges(kind, expression, options, out List<RegexScalarRange> ranges))
        {
            return false;
        }

        Normalize(ranges);
        if (ranges.Count == 0)
        {
            return false;
        }

        var created = new RegexUtf8ByteTrie();
        for (int rangeIndex = 0; rangeIndex < ranges.Count; rangeIndex++)
        {
            RegexScalarRange range = ranges[rangeIndex];
            for (int scalar = range.Start; scalar <= range.End; scalar++)
            {
                if (Rune.IsValid(scalar))
                {
                    created.AddScalar(scalar, reversed);
                }
            }
        }

        trie = created.IsEmpty ? null : created;
        return trie is not null;
    }

    public static bool TryGetSharedTrie(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        bool reversed,
        out RegexUtf8ByteTrie? trie)
    {
        trie = null;
        if (!options.UnicodeClasses)
        {
            return false;
        }

        if (expression.IsEmpty)
        {
            trie = kind switch
            {
                RegexSyntaxKind.DigitClass => reversed ? ReversedUnicodeDigitTrie.Value : UnicodeDigitTrie.Value,
                RegexSyntaxKind.WhitespaceClass => reversed ? ReversedUnicodeWhitespaceTrie.Value : UnicodeWhitespaceTrie.Value,
                RegexSyntaxKind.LetterClass => reversed ? ReversedUnicodeAlphabeticTrie.Value : UnicodeAlphabeticTrie.Value,
                _ => null,
            };
            return trie is not null;
        }

        trie = kind == RegexSyntaxKind.UnicodePropertyClass &&
            expression.Length == 1 &&
            expression[0] == (byte)RegexUnicodePropertyKind.Letter
            ? reversed ? ReversedUnicodeLetterTrie.Value : UnicodeLetterTrie.Value
            : null;
        return trie is not null;
    }

    public static bool TryCompileCompact(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        bool reversed,
        int next,
        RegexAddByteClassState addByteClass,
        RegexAddSplitState addSplit,
        out int start)
    {
        start = -1;
        if (!RegexByteClass.RequiresUtf8ScalarMatch(kind, expression, options.Utf8, options.CaseInsensitive, options.UnicodeClasses) ||
            !TryBuildScalarRanges(kind, expression, options, out List<RegexScalarRange> ranges))
        {
            return false;
        }

        Normalize(ranges);
        if (!TryGetAsciiRangesWhenAllNonAsciiScalarsMatch(ranges, out byte[] asciiRanges))
        {
            return false;
        }

        start = CompileAnyValidUtf8Scalar(asciiRanges, reversed, next, addByteClass, addSplit);
        return true;
    }

    public static bool TryCompileRangeSequences(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        bool reversed,
        int next,
        RegexAddByteClassState addByteClass,
        RegexAddSplitState addSplit,
        out int start)
    {
        start = -1;
        if (!RegexByteClass.RequiresUtf8ScalarMatch(kind, expression, options.Utf8, options.CaseInsensitive, options.UnicodeClasses) ||
            !TryBuildScalarRanges(kind, expression, options, out List<RegexScalarRange> ranges))
        {
            return false;
        }

        Normalize(ranges);
        if (EstimateScalarCount(ranges) <= RangeSequenceScalarThreshold)
        {
            return false;
        }

        List<int> branches = [];
        for (int index = 0; index < ranges.Count; index++)
        {
            AddUtf8RangeBranches(branches, ranges[index].Start, ranges[index].End, reversed, next, addByteClass);
        }

        if (branches.Count == 0)
        {
            return false;
        }

        start = branches[^1];
        for (int index = branches.Count - 2; index >= 0; index--)
        {
            start = addSplit(branches[index], start);
        }

        return true;
    }

    public static bool TryGetUtf8ByteLengthRange(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        out int minimumBytes,
        out int maximumBytes)
    {
        minimumBytes = 0;
        maximumBytes = 0;
        if (!TryBuildScalarRanges(kind, expression, options, out List<RegexScalarRange> ranges))
        {
            return false;
        }

        Normalize(ranges);
        if (ranges.Count == 0)
        {
            return false;
        }

        int minimum = 5;
        int maximum = 0;
        for (int index = 0; index < ranges.Count; index++)
        {
            AddUtf8ByteLengthRange(ranges[index], ref minimum, ref maximum);
        }

        if (maximum == 0)
        {
            return false;
        }

        minimumBytes = minimum;
        maximumBytes = maximum;
        return true;
    }

    private static bool TryBuildScalarRanges(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        out List<RegexScalarRange> ranges)
    {
        ranges = [];
        switch (kind)
        {
            case RegexSyntaxKind.Literal:
                return TryAddLiteralRanges(ranges, expression, options.CaseInsensitive, options.UnicodeClasses);
            case RegexSyntaxKind.Dot:
                AddAnyScalarRanges(ranges);
                if (!options.DotMatchesNewline)
                {
                    RemoveLineTerminators(ranges, options.Crlf, options.LineTerminator);
                }

                return true;
            case RegexSyntaxKind.AnyClass:
                AddAnyScalarRanges(ranges);
                return true;
            case RegexSyntaxKind.CharacterClass:
                return TryAddCharacterClassRanges(ranges, expression, options);
            case RegexSyntaxKind.DigitClass:
                AddDigitRanges(ranges, options.UnicodeClasses);
                return true;
            case RegexSyntaxKind.NotDigitClass:
                AddDigitRanges(ranges, options.UnicodeClasses);
                ComplementInPlace(ranges);
                return true;
            case RegexSyntaxKind.WordClass:
                AddWordRanges(ranges, options.UnicodeClasses);
                return true;
            case RegexSyntaxKind.NotWordClass:
                AddWordRanges(ranges, options.UnicodeClasses);
                ComplementInPlace(ranges);
                return true;
            case RegexSyntaxKind.WhitespaceClass:
                AddWhitespaceRanges(ranges, options.UnicodeClasses);
                return true;
            case RegexSyntaxKind.NotWhitespaceClass:
                AddWhitespaceRanges(ranges, options.UnicodeClasses);
                ComplementInPlace(ranges);
                return true;
            case RegexSyntaxKind.UnicodePropertyClass:
                return options.UnicodeClasses &&
                    TryAddUnicodePropertyRanges(ranges, expression, options.CaseInsensitive, negated: false);
            case RegexSyntaxKind.NotUnicodePropertyClass:
                return options.UnicodeClasses &&
                    TryAddUnicodePropertyRanges(ranges, expression, options.CaseInsensitive, negated: true);
            default:
                ranges = [];
                return false;
        }
    }

    private static bool TryAddCharacterClassRanges(
        List<RegexScalarRange> ranges,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options)
    {
        bool negated = RegexByteClass.IsNegatedClass(expression);
        ReadOnlySpan<byte> body = negated ? expression[1..] : expression;
        if (!TryAddCharacterClassIntersectionRanges(ranges, body, options))
        {
            return false;
        }

        if (negated)
        {
            ComplementInPlace(ranges);
        }

        return true;
    }

    private static bool TryAddCharacterClassIntersectionRanges(
        List<RegexScalarRange> ranges,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options)
    {
        if (!RegexByteClass.TryFindClassIntersectionOperator(expression, out int operatorIndex))
        {
            return TryAddCharacterClassUnionRanges(ranges, expression, options);
        }

        if (!TryAddCharacterClassUnionRanges(ranges, expression[..operatorIndex], options))
        {
            return false;
        }

        ReadOnlySpan<byte> remaining = expression[(operatorIndex + 2)..];
        while (true)
        {
            ReadOnlySpan<byte> segment;
            if (RegexByteClass.TryFindClassIntersectionOperator(remaining, out operatorIndex))
            {
                segment = remaining[..operatorIndex];
                remaining = remaining[(operatorIndex + 2)..];
            }
            else
            {
                segment = remaining;
                remaining = [];
            }

            var segmentRanges = new List<RegexScalarRange>();
            if (!TryAddCharacterClassUnionRanges(segmentRanges, segment, options))
            {
                return false;
            }

            IntersectInPlace(ranges, segmentRanges);
            if (remaining.IsEmpty)
            {
                return true;
            }
        }
    }

    private static bool TryAddCharacterClassUnionRanges(
        List<RegexScalarRange> ranges,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options)
    {
        int index = 0;
        while (index < expression.Length)
        {
            int tokenStart = index;
            if (!TryReadScalarClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out int literal, out bool tokenNegated))
            {
                return false;
            }

            if (!tokenNegated &&
                tokenKind == RegexSyntaxKind.Literal &&
                index + 1 < expression.Length &&
                expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (TryReadScalarClassToken(expression, ref rangeEndIndex, out RegexSyntaxKind rangeEndKind, out int rangeEndLiteral, out bool rangeEndNegated) &&
                    !rangeEndNegated &&
                    rangeEndKind == RegexSyntaxKind.Literal)
                {
                    AddClassRangeRanges(ranges, literal, rangeEndLiteral, options.CaseInsensitive, options.UnicodeClasses);
                    index = rangeEndIndex;
                    continue;
                }
            }

            if (index == tokenStart)
            {
                return false;
            }

            if (!TryAddClassTokenRanges(ranges, tokenKind, literal, tokenNegated, options))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAddClassTokenRanges(
        List<RegexScalarRange> ranges,
        RegexSyntaxKind tokenKind,
        int literal,
        bool tokenNegated,
        RegexCompileOptions options)
    {
        int startCount = ranges.Count;
        bool result = tokenKind switch
        {
            RegexSyntaxKind.DigitClass => AddDigitRanges(ranges, options.UnicodeClasses),
            RegexSyntaxKind.WordClass => AddWordRanges(ranges, options.UnicodeClasses),
            RegexSyntaxKind.WhitespaceClass => AddWhitespaceRanges(ranges, options.UnicodeClasses),
            RegexSyntaxKind.LetterClass => AddLetterRanges(ranges, options.UnicodeClasses),
            RegexSyntaxKind.AlphanumericClass => AddAlphanumericRanges(ranges, options.UnicodeClasses),
            RegexSyntaxKind.AnyClass => AddAnyScalarRanges(ranges),
            RegexSyntaxKind.UnicodePropertyClass => options.UnicodeClasses &&
                TryAddUnicodePropertyRanges(ranges, stackalloc byte[] { (byte)literal }, options.CaseInsensitive, negated: false),
            RegexSyntaxKind.NotUnicodePropertyClass => options.UnicodeClasses &&
                TryAddUnicodePropertyRanges(ranges, stackalloc byte[] { (byte)literal }, options.CaseInsensitive, negated: true),
            RegexSyntaxKind.Literal => TryAddLiteralScalarRanges(ranges, literal, options.CaseInsensitive, options.UnicodeClasses),
            _ => false,
        };
        if (!result)
        {
            return false;
        }

        if (tokenNegated)
        {
            List<RegexScalarRange> tokenRanges = ranges.GetRange(startCount, ranges.Count - startCount);
            ranges.RemoveRange(startCount, ranges.Count - startCount);
            ComplementInPlace(tokenRanges);
            ranges.AddRange(tokenRanges);
        }

        return true;
    }

    private static bool TryReadScalarClassToken(
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

        if (expression[index] == (byte)'\\' &&
            index + 1 < expression.Length &&
            expression[index + 1] is (byte)'x' or (byte)'u')
        {
            byte escaped = expression[index + 1];
            int scalarIndex = index + 2;
            if (TryReadEscapedHexScalar(expression, ref scalarIndex, escaped, out int scalar))
            {
                index = scalarIndex;
                literal = scalar;
                return true;
            }
        }

        if (!RegexByteClass.TryReadClassToken(expression, ref index, out tokenKind, out byte byteLiteral, out tokenNegated))
        {
            return false;
        }

        literal = byteLiteral;
        return true;
    }

    private static bool TryAddLiteralRanges(List<RegexScalarRange> ranges, ReadOnlySpan<byte> literal, bool caseInsensitive, bool unicodeClasses)
    {
        if (!TryDecodeLiteralRune(literal, out Rune rune))
        {
            return false;
        }

        return TryAddLiteralScalarRanges(ranges, rune.Value, caseInsensitive, unicodeClasses);
    }

    private static bool TryAddLiteralScalarRanges(List<RegexScalarRange> ranges, int scalar, bool caseInsensitive, bool unicodeClasses)
    {
        if (!Rune.IsValid(scalar))
        {
            return false;
        }

        if (!caseInsensitive || !unicodeClasses)
        {
            AddRange(ranges, scalar, scalar);
            return true;
        }

        List<Rune> equivalents = [];
        RegexUnicodeTables.AddSimpleCaseFoldEquivalents(new Rune(scalar), equivalents);
        for (int index = 0; index < equivalents.Count; index++)
        {
            AddRange(ranges, equivalents[index].Value, equivalents[index].Value);
        }

        return true;
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

    private static void AddClassRangeRanges(
        List<RegexScalarRange> ranges,
        int start,
        int end,
        bool caseInsensitive,
        bool unicodeClasses)
    {
        if (start <= byte.MaxValue && end <= byte.MaxValue)
        {
            byte foldedStart = FoldMaybe((byte)start, caseInsensitive);
            byte foldedEnd = FoldMaybe((byte)end, caseInsensitive);
            for (int value = 0; value <= 0x7F; value++)
            {
                byte foldedValue = FoldMaybe((byte)value, caseInsensitive);
                if (foldedStart <= foldedValue && foldedValue <= foldedEnd)
                {
                    AddRange(ranges, value, value);
                }
            }

            if (!caseInsensitive || !unicodeClasses)
            {
                return;
            }

            for (byte candidate = (byte)'a'; candidate <= (byte)'z'; candidate++)
            {
                if (foldedStart > candidate || candidate > foldedEnd)
                {
                    continue;
                }

                List<Rune> equivalents = [];
                RegexUnicodeTables.AddSimpleCaseFoldEquivalents(new Rune(candidate), equivalents);
                for (int index = 0; index < equivalents.Count; index++)
                {
                    AddRange(ranges, equivalents[index].Value, equivalents[index].Value);
                }
            }

            return;
        }

        AddRange(ranges, start, end);
        if (!caseInsensitive || !unicodeClasses)
        {
            return;
        }

        int asciiEnd = Math.Min(end, (byte)'z');
        for (int candidate = Math.Max(start, (byte)'a'); candidate <= asciiEnd; candidate++)
        {
            List<Rune> equivalents = [];
            RegexUnicodeTables.AddSimpleCaseFoldEquivalents(new Rune(candidate), equivalents);
            for (int index = 0; index < equivalents.Count; index++)
            {
                AddRange(ranges, equivalents[index].Value, equivalents[index].Value);
            }
        }
    }

    private static bool TryReadEscapedHexScalar(ReadOnlySpan<byte> expression, ref int index, byte escaped, out int scalar)
    {
        scalar = 0;
        if (escaped == (byte)'x' &&
            index + 1 < expression.Length &&
            TryReadHexByte(expression[index], expression[index + 1], out byte byteValue))
        {
            index += 2;
            scalar = byteValue;
            return true;
        }

        return (escaped == (byte)'x' || escaped == (byte)'u') &&
            TryReadBracedHexScalar(expression, ref index, out scalar);
    }

    private static bool TryReadBracedHexScalar(ReadOnlySpan<byte> expression, ref int index, out int scalar)
    {
        scalar = 0;
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
            if (parsed > MaxScalar)
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

        index = scan + 1;
        scalar = parsed;
        return true;
    }

    private static bool TryReadHexByte(byte high, byte low, out byte value)
    {
        value = 0;
        if (!TryGetHexValue(high, out int highValue) || !TryGetHexValue(low, out int lowValue))
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

    private static bool TryAddUnicodePropertyRanges(
        List<RegexScalarRange> ranges,
        ReadOnlySpan<byte> expression,
        bool caseInsensitive,
        bool negated)
    {
        if (expression.Length != 1)
        {
            return false;
        }

        var kind = (RegexUnicodePropertyKind)expression[0];
        if (caseInsensitive &&
            kind is RegexUnicodePropertyKind.CasedLetter
                or RegexUnicodePropertyKind.LowercaseLetter
                or RegexUnicodePropertyKind.TitlecaseLetter
                or RegexUnicodePropertyKind.UppercaseLetter)
        {
            AddTableRanges(ranges, RegexUnicodeTables.GetGeneralCategoryRanges(RegexUnicodePropertyKind.CasedLetter));
        }
        else
        {
            AddTableRanges(ranges, RegexUnicodeTables.GetGeneralCategoryRanges(kind));
            AddTableRanges(ranges, RegexUnicodeTables.GetBooleanPropertyRanges(kind));
            AddTableRanges(ranges, RegexUnicodeTables.GetBreakPropertyRanges(kind));
            AddTableRanges(ranges, RegexUnicodeTables.GetScriptRanges(kind));
            AddTableRanges(ranges, RegexUnicodeTables.GetScriptExtensionRanges(kind));
        }

        if (negated)
        {
            ComplementInPlace(ranges);
        }

        return true;
    }

    private static bool AddDigitRanges(List<RegexScalarRange> ranges, bool unicodeClasses)
    {
        if (unicodeClasses)
        {
            AddTableRanges(ranges, RegexUnicodeTables.DecimalNumberRanges);
        }
        else
        {
            AddRange(ranges, '0', '9');
        }

        return true;
    }

    private static bool AddWordRanges(List<RegexScalarRange> ranges, bool unicodeClasses)
    {
        if (unicodeClasses)
        {
            AddTableRanges(ranges, RegexUnicodeTables.PerlWordRanges);
        }
        else
        {
            AddRange(ranges, '0', '9');
            AddRange(ranges, 'A', 'Z');
            AddRange(ranges, '_', '_');
            AddRange(ranges, 'a', 'z');
        }

        return true;
    }

    private static bool AddWhitespaceRanges(List<RegexScalarRange> ranges, bool unicodeClasses)
    {
        if (unicodeClasses)
        {
            AddTableRanges(ranges, RegexUnicodeTables.PerlSpaceRanges);
        }
        else
        {
            AddRange(ranges, '\t', '\r');
            AddRange(ranges, ' ', ' ');
        }

        return true;
    }

    private static bool AddLetterRanges(List<RegexScalarRange> ranges, bool unicodeClasses)
    {
        if (unicodeClasses)
        {
            AddTableRanges(ranges, RegexUnicodeTables.AlphabeticRanges);
        }
        else
        {
            AddRange(ranges, 'A', 'Z');
            AddRange(ranges, 'a', 'z');
        }

        return true;
    }

    private static bool AddAlphanumericRanges(List<RegexScalarRange> ranges, bool unicodeClasses)
    {
        AddLetterRanges(ranges, unicodeClasses);
        AddDigitRanges(ranges, unicodeClasses);
        return true;
    }

    private static bool AddAnyScalarRanges(List<RegexScalarRange> ranges)
    {
        AddRange(ranges, 0, MaxScalar);
        return true;
    }

    private static void AddTableRanges(List<RegexScalarRange> ranges, ReadOnlySpan<int> table)
    {
        for (int index = 0; index + 1 < table.Length; index += 2)
        {
            AddRange(ranges, table[index], table[index + 1]);
        }
    }

    private static RegexUtf8ByteTrie CreateTableTrie(ReadOnlySpan<int> table, bool reversed)
    {
        var trie = new RegexUtf8ByteTrie();
        for (int index = 0; index + 1 < table.Length; index += 2)
        {
            for (int scalar = table[index]; scalar <= table[index + 1]; scalar++)
            {
                if (Rune.IsValid(scalar))
                {
                    trie.AddScalar(scalar, reversed);
                }
            }
        }

        return trie;
    }

    private static void RemoveLineTerminators(List<RegexScalarRange> ranges, bool crlf, byte lineTerminator)
    {
        var excluded = new List<RegexScalarRange>();
        if (crlf)
        {
            AddRange(excluded, '\n', '\n');
            AddRange(excluded, '\r', '\r');
        }
        else
        {
            AddRange(excluded, lineTerminator, lineTerminator);
        }

        Normalize(ranges);
        Normalize(excluded);
        ranges.Clear();
        AddComplementAgainst(ranges, excluded, source: AllValidScalarRanges());
    }

    private static bool TryGetAsciiRangesWhenAllNonAsciiScalarsMatch(List<RegexScalarRange> ranges, out byte[] asciiRanges)
    {
        asciiRanges = [];
        if (!ContainsRange(ranges, 0x80, SurrogateStart - 1) ||
            !ContainsRange(ranges, SurrogateEnd + 1, MaxScalar))
        {
            return false;
        }

        List<byte> ascii = [];
        for (int value = 0; value <= 0x7F; value++)
        {
            if (ContainsScalar(ranges, value))
            {
                ascii.Add((byte)value);
            }
        }

        asciiRanges = ascii.Count == 0 ? [] : ToByteRanges(ascii);
        return true;
    }

    private static void AddUtf8RangeBranches(
        List<int> branches,
        int start,
        int end,
        bool reversed,
        int next,
        RegexAddByteClassState addByteClass)
    {
        AddUtf8RangeBranchesForLength(branches, Math.Max(start, 0), Math.Min(end, 0x7F), reversed, next, addByteClass);
        AddUtf8RangeBranchesForLength(branches, Math.Max(start, 0x80), Math.Min(end, 0x7FF), reversed, next, addByteClass);
        AddUtf8RangeBranchesForLength(branches, Math.Max(start, 0x800), Math.Min(end, SurrogateStart - 1), reversed, next, addByteClass);
        AddUtf8RangeBranchesForLength(branches, Math.Max(start, SurrogateEnd + 1), Math.Min(end, 0xFFFF), reversed, next, addByteClass);
        AddUtf8RangeBranchesForLength(branches, Math.Max(start, 0x10000), Math.Min(end, MaxScalar), reversed, next, addByteClass);
    }

    private static void AddUtf8RangeBranchesForLength(
        List<int> branches,
        int start,
        int end,
        bool reversed,
        int next,
        RegexAddByteClassState addByteClass)
    {
        if (start > end)
        {
            return;
        }

        byte[] lower = EncodeScalar(start);
        byte[] upper = EncodeScalar(end);
        if (lower.Length != upper.Length)
        {
            throw new InvalidOperationException("UTF-8 range decomposition crossed an encoding length boundary.");
        }

        var prefix = new List<(byte Start, byte End)>(lower.Length);
        AddUtf8RangeBranchesCore(branches, lower, upper, position: 0, prefix, reversed, next, addByteClass);
    }

    private static void AddUtf8RangeBranchesCore(
        List<int> branches,
        byte[] lower,
        byte[] upper,
        int position,
        List<(byte Start, byte End)> prefix,
        bool reversed,
        int next,
        RegexAddByteClassState addByteClass)
    {
        if (position == lower.Length)
        {
            AddUtf8SequenceBranch(branches, prefix.ToArray(), reversed, next, addByteClass);
            return;
        }

        if (lower[position] == upper[position])
        {
            prefix.Add((lower[position], lower[position]));
            AddUtf8RangeBranchesCore(branches, lower, upper, position + 1, prefix, reversed, next, addByteClass);
            prefix.RemoveAt(prefix.Count - 1);
            return;
        }

        byte lowerByte = lower[position];
        byte upperByte = upper[position];

        prefix.Add((lowerByte, lowerByte));
        AddUtf8RangeBranchesCore(
            branches,
            lower,
            FillSuffix(upper, position + 1, 0xBF),
            position + 1,
            prefix,
            reversed,
            next,
            addByteClass);
        prefix.RemoveAt(prefix.Count - 1);

        if (lowerByte + 1 <= upperByte - 1)
        {
            prefix.Add(((byte)(lowerByte + 1), (byte)(upperByte - 1)));
            for (int index = position + 1; index < lower.Length; index++)
            {
                prefix.Add((0x80, 0xBF));
            }

            AddUtf8SequenceBranch(branches, prefix.ToArray(), reversed, next, addByteClass);
            prefix.RemoveRange(prefix.Count - (lower.Length - position), lower.Length - position);
        }

        prefix.Add((upperByte, upperByte));
        AddUtf8RangeBranchesCore(
            branches,
            FillSuffix(lower, position + 1, 0x80),
            upper,
            position + 1,
            prefix,
            reversed,
            next,
            addByteClass);
        prefix.RemoveAt(prefix.Count - 1);
    }

    private static byte[] FillSuffix(byte[] source, int start, byte value)
    {
        byte[] copy = (byte[])source.Clone();
        for (int index = start; index < copy.Length; index++)
        {
            copy[index] = value;
        }

        return copy;
    }

    private static byte[] EncodeScalar(int scalar)
    {
        Span<byte> buffer = stackalloc byte[4];
        int length = new Rune(scalar).EncodeToUtf8(buffer);
        return buffer[..length].ToArray();
    }

    private static int CompileAnyValidUtf8Scalar(
        ReadOnlySpan<byte> asciiRanges,
        bool reversed,
        int next,
        RegexAddByteClassState addByteClass,
        RegexAddSplitState addSplit)
    {
        List<int> branches = [];
        if (!asciiRanges.IsEmpty)
        {
            branches.Add(addByteClass(asciiRanges, next));
        }

        AddUtf8SequenceBranch(branches, [(0xC2, 0xDF), (0x80, 0xBF)], reversed, next, addByteClass);
        AddUtf8SequenceBranch(branches, [(0xE0, 0xE0), (0xA0, 0xBF), (0x80, 0xBF)], reversed, next, addByteClass);
        AddUtf8SequenceBranch(branches, [(0xE1, 0xEC), (0x80, 0xBF), (0x80, 0xBF)], reversed, next, addByteClass);
        AddUtf8SequenceBranch(branches, [(0xED, 0xED), (0x80, 0x9F), (0x80, 0xBF)], reversed, next, addByteClass);
        AddUtf8SequenceBranch(branches, [(0xEE, 0xEF), (0x80, 0xBF), (0x80, 0xBF)], reversed, next, addByteClass);
        AddUtf8SequenceBranch(branches, [(0xF0, 0xF0), (0x90, 0xBF), (0x80, 0xBF), (0x80, 0xBF)], reversed, next, addByteClass);
        AddUtf8SequenceBranch(branches, [(0xF1, 0xF3), (0x80, 0xBF), (0x80, 0xBF), (0x80, 0xBF)], reversed, next, addByteClass);
        AddUtf8SequenceBranch(branches, [(0xF4, 0xF4), (0x80, 0x8F), (0x80, 0xBF), (0x80, 0xBF)], reversed, next, addByteClass);

        int start = branches[^1];
        for (int index = branches.Count - 2; index >= 0; index--)
        {
            start = addSplit(branches[index], start);
        }

        return start;
    }

    private static void AddUtf8SequenceBranch(
        List<int> branches,
        ReadOnlySpan<(byte Start, byte End)> ranges,
        bool reversed,
        int next,
        RegexAddByteClassState addByteClass)
    {
        int start = next;
        if (reversed)
        {
            for (int index = 0; index < ranges.Length; index++)
            {
                start = addByteClass([ranges[index].Start, ranges[index].End], start);
            }
        }
        else
        {
            for (int index = ranges.Length - 1; index >= 0; index--)
            {
                start = addByteClass([ranges[index].Start, ranges[index].End], start);
            }
        }

        branches.Add(start);
    }

    private static void AddUtf8ByteLengthRange(RegexScalarRange range, ref int minimum, ref int maximum)
    {
        AddUtf8ByteLengthRangeForEncoding(range, 0, 0x7F, length: 1, ref minimum, ref maximum);
        AddUtf8ByteLengthRangeForEncoding(range, 0x80, 0x7FF, length: 2, ref minimum, ref maximum);
        AddUtf8ByteLengthRangeForEncoding(range, 0x800, 0xFFFF, length: 3, ref minimum, ref maximum);
        AddUtf8ByteLengthRangeForEncoding(range, 0x10000, MaxScalar, length: 4, ref minimum, ref maximum);
    }

    private static void AddUtf8ByteLengthRangeForEncoding(
        RegexScalarRange range,
        int start,
        int end,
        int length,
        ref int minimum,
        ref int maximum)
    {
        if (range.Start > end || range.End < start)
        {
            return;
        }

        minimum = Math.Min(minimum, length);
        maximum = Math.Max(maximum, length);
    }

    private static bool ContainsRange(List<RegexScalarRange> ranges, int start, int end)
    {
        int cursor = start;
        for (int index = 0; index < ranges.Count; index++)
        {
            RegexScalarRange range = ranges[index];
            if (range.End < cursor)
            {
                continue;
            }

            if (range.Start > cursor)
            {
                return false;
            }

            cursor = range.End + 1;
            if (cursor > end)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsScalar(List<RegexScalarRange> ranges, int scalar)
    {
        for (int index = 0; index < ranges.Count; index++)
        {
            RegexScalarRange range = ranges[index];
            if (scalar < range.Start)
            {
                return false;
            }

            if (scalar <= range.End)
            {
                return true;
            }
        }

        return false;
    }

    private static long EstimateScalarCount(List<RegexScalarRange> ranges)
    {
        long count = 0;
        for (int index = 0; index < ranges.Count; index++)
        {
            count += (long)ranges[index].End - ranges[index].Start + 1;
            if (count > RangeSequenceScalarThreshold)
            {
                return count;
            }
        }

        return count;
    }

    private static byte[] ToByteRanges(List<byte> values)
    {
        List<byte> ranges = [];
        byte start = values[0];
        byte previous = start;
        for (int index = 1; index < values.Count; index++)
        {
            byte value = values[index];
            if (value == previous + 1)
            {
                previous = value;
                continue;
            }

            ranges.Add(start);
            ranges.Add(previous);
            start = value;
            previous = value;
        }

        ranges.Add(start);
        ranges.Add(previous);
        return ranges.ToArray();
    }

    private static void ComplementInPlace(List<RegexScalarRange> ranges)
    {
        Normalize(ranges);
        RegexScalarRange[] source = ranges.ToArray();
        ranges.Clear();
        AddComplementAgainst(ranges, source, AllValidScalarRanges());
    }

    private static void IntersectInPlace(List<RegexScalarRange> ranges, List<RegexScalarRange> other)
    {
        Normalize(ranges);
        Normalize(other);
        var result = new List<RegexScalarRange>();
        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < ranges.Count && rightIndex < other.Count)
        {
            RegexScalarRange left = ranges[leftIndex];
            RegexScalarRange right = other[rightIndex];
            int start = Math.Max(left.Start, right.Start);
            int end = Math.Min(left.End, right.End);
            if (start <= end)
            {
                AddRange(result, start, end);
            }

            if (left.End < right.End)
            {
                leftIndex++;
            }
            else
            {
                rightIndex++;
            }
        }

        ranges.Clear();
        ranges.AddRange(result);
    }

    private static void AddComplementAgainst(
        List<RegexScalarRange> destination,
        IReadOnlyList<RegexScalarRange> excluded,
        RegexScalarRange[] source)
    {
        int excludedIndex = 0;
        for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
        {
            int cursor = source[sourceIndex].Start;
            int sourceEnd = source[sourceIndex].End;
            while (excludedIndex < excluded.Count && excluded[excludedIndex].End < cursor)
            {
                excludedIndex++;
            }

            int localExcludedIndex = excludedIndex;
            while (localExcludedIndex < excluded.Count && excluded[localExcludedIndex].Start <= sourceEnd)
            {
                RegexScalarRange removed = excluded[localExcludedIndex];
                if (cursor < removed.Start)
                {
                    AddRange(destination, cursor, removed.Start - 1);
                }

                cursor = Math.Max(cursor, removed.End + 1);
                if (cursor > sourceEnd)
                {
                    break;
                }

                localExcludedIndex++;
            }

            if (cursor <= sourceEnd)
            {
                AddRange(destination, cursor, sourceEnd);
            }
        }
    }

    private static RegexScalarRange[] AllValidScalarRanges()
    {
        return
        [
            new RegexScalarRange(0, SurrogateStart - 1),
            new RegexScalarRange(SurrogateEnd + 1, MaxScalar),
        ];
    }

    private static void AddRange(List<RegexScalarRange> ranges, int start, int end)
    {
        if (end < 0 || start > MaxScalar || start > end)
        {
            return;
        }

        start = Math.Max(0, start);
        end = Math.Min(MaxScalar, end);
        if (start < SurrogateStart && end >= SurrogateStart)
        {
            AddRange(ranges, start, SurrogateStart - 1);
            AddRange(ranges, SurrogateEnd + 1, end);
            return;
        }

        if (start >= SurrogateStart && start <= SurrogateEnd)
        {
            start = SurrogateEnd + 1;
        }

        if (end >= SurrogateStart && end <= SurrogateEnd)
        {
            end = SurrogateStart - 1;
        }

        if (start <= end)
        {
            ranges.Add(new RegexScalarRange(start, end));
        }
    }

    private static void Normalize(List<RegexScalarRange> ranges)
    {
        if (ranges.Count <= 1)
        {
            return;
        }

        ranges.Sort(static (left, right) => left.Start == right.Start
            ? left.End.CompareTo(right.End)
            : left.Start.CompareTo(right.Start));
        int write = 0;
        for (int read = 1; read < ranges.Count; read++)
        {
            RegexScalarRange current = ranges[read];
            RegexScalarRange previous = ranges[write];
            if (current.Start <= previous.End + 1)
            {
                ranges[write] = new RegexScalarRange(previous.Start, Math.Max(previous.End, current.End));
                continue;
            }

            write++;
            ranges[write] = current;
        }

        ranges.RemoveRange(write + 1, ranges.Count - write - 1);
    }

    private static byte FoldMaybe(byte value, bool caseInsensitive)
    {
        return caseInsensitive && value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }

}
