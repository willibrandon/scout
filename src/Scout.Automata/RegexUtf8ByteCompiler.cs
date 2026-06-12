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
        if (!options.UnicodeClasses || !expression.IsEmpty)
        {
            return false;
        }

        trie = kind switch
        {
            RegexSyntaxKind.DigitClass => reversed ? ReversedUnicodeDigitTrie.Value : UnicodeDigitTrie.Value,
            RegexSyntaxKind.WhitespaceClass => reversed ? ReversedUnicodeWhitespaceTrie.Value : UnicodeWhitespaceTrie.Value,
            _ => null,
        };
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
        int index = negated ? 1 : 0;
        while (index < expression.Length)
        {
            int tokenStart = index;
            if (!RegexByteClass.TryReadClassToken(expression, ref index, out RegexSyntaxKind tokenKind, out byte literal, out bool tokenNegated))
            {
                return false;
            }

            if (!tokenNegated &&
                tokenKind == RegexSyntaxKind.Literal &&
                index + 1 < expression.Length &&
                expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (RegexByteClass.TryReadClassToken(expression, ref rangeEndIndex, out RegexSyntaxKind rangeEndKind, out byte rangeEndLiteral, out bool rangeEndNegated) &&
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

        if (negated)
        {
            ComplementInPlace(ranges);
        }

        return true;
    }

    private static bool TryAddClassTokenRanges(
        List<RegexScalarRange> ranges,
        RegexSyntaxKind tokenKind,
        byte literal,
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
                TryAddUnicodePropertyRanges(ranges, stackalloc byte[] { literal }, options.CaseInsensitive, negated: false),
            RegexSyntaxKind.NotUnicodePropertyClass => options.UnicodeClasses &&
                TryAddUnicodePropertyRanges(ranges, stackalloc byte[] { literal }, options.CaseInsensitive, negated: true),
            RegexSyntaxKind.Literal => TryAddLiteralRanges(ranges, stackalloc byte[] { literal }, options.CaseInsensitive, options.UnicodeClasses),
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

    private static bool TryAddLiteralRanges(List<RegexScalarRange> ranges, ReadOnlySpan<byte> literal, bool caseInsensitive, bool unicodeClasses)
    {
        if (!TryDecodeLiteralRune(literal, out Rune rune))
        {
            return false;
        }

        if (!caseInsensitive || !unicodeClasses)
        {
            AddRange(ranges, rune.Value, rune.Value);
            return true;
        }

        List<Rune> equivalents = [];
        RegexUnicodeTables.AddSimpleCaseFoldEquivalents(rune, equivalents);
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
        byte start,
        byte end,
        bool caseInsensitive,
        bool unicodeClasses)
    {
        byte foldedStart = FoldMaybe(start, caseInsensitive);
        byte foldedEnd = FoldMaybe(end, caseInsensitive);
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
