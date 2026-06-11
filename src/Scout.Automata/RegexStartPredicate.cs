using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexStartPredicate
{
    private const int MaxPredicateLength = 64;
    private const int MaxFirstByteVariants = 192;

    private readonly bool[][] allowedBytes;

    private RegexStartPredicate(List<byte[]> allowedBytes)
    {
        this.allowedBytes = new bool[allowedBytes.Count][];
        for (int index = 0; index < allowedBytes.Count; index++)
        {
            bool[] lookup = new bool[256];
            byte[] bytes = allowedBytes[index];
            for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
            {
                lookup[bytes[byteIndex]] = true;
            }

            this.allowedBytes[index] = lookup;
        }
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexStartPredicate? predicate)
    {
        var allowed = new List<byte[]>();
        bool caseFoldMayNeedUnicodeScalars = options.CaseInsensitive && options.UnicodeClasses;
        if (!TryAppend(root, options, allowed, caseFoldMayNeedUnicodeScalars, out _) ||
            allowed.Count == 0)
        {
            return TryCreateFirstByte(root, options, out predicate);
        }

        predicate = new RegexStartPredicate(allowed);
        return true;
    }

    public bool CanStartAt(ReadOnlySpan<byte> haystack, int start)
    {
        if (start < 0 || start > haystack.Length - allowedBytes.Length)
        {
            return false;
        }

        for (int index = 0; index < allowedBytes.Length; index++)
        {
            if (!allowedBytes[index][haystack[start + index]])
            {
                return false;
            }
        }

        return true;
    }

    internal bool TryAddFirstBytes(bool[] bytes)
    {
        if (allowedBytes.Length == 0)
        {
            return false;
        }

        bool[] first = allowedBytes[0];
        for (int index = 0; index <= byte.MaxValue; index++)
        {
            if (first[index])
            {
                bytes[index] = true;
            }
        }

        return true;
    }

    private static bool TryCreateFirstByte(RegexSyntaxNode root, RegexCompileOptions options, out RegexStartPredicate? predicate)
    {
        var bytes = new List<byte>();
        if (!TryCollectFirstBytes(root, options, bytes, out bool canMatchEmpty) ||
            canMatchEmpty ||
            bytes.Count == 0 ||
            bytes.Count > MaxFirstByteVariants)
        {
            predicate = null;
            return false;
        }

        predicate = new RegexStartPredicate([bytes.ToArray()]);
        return true;
    }

    private static bool TryCollectFirstBytes(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte> bytes,
        out bool canMatchEmpty)
    {
        canMatchEmpty = false;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.StartAnchor:
            case RegexSyntaxKind.EndAnchor:
            case RegexSyntaxKind.AbsoluteStartAnchor:
            case RegexSyntaxKind.AbsoluteEndAnchor:
            case RegexSyntaxKind.WordBoundary:
            case RegexSyntaxKind.NotWordBoundary:
            case RegexSyntaxKind.WordStartBoundary:
            case RegexSyntaxKind.WordEndBoundary:
            case RegexSyntaxKind.WordStartHalfBoundary:
            case RegexSyntaxKind.WordEndHalfBoundary:
            case RegexSyntaxKind.InlineFlags:
                canMatchEmpty = true;
                return true;
            case RegexSyntaxKind.Literal:
                return TryAddLiteralFirstBytes(((RegexAtomNode)node).Value.Span, options, bytes, out canMatchEmpty);
            case RegexSyntaxKind.CharacterClass:
                return TryAddCharacterClassFirstBytes(((RegexAtomNode)node).Value.Span, options, bytes, out canMatchEmpty);
            case RegexSyntaxKind.DigitClass:
                AddFirstByteVariants(bytes, options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddDecimalNumberPrefixBytes)
                    : ByteRangeVariants((byte)'0', (byte)'9'));
                return true;
            case RegexSyntaxKind.WordClass:
                AddFirstByteVariants(bytes, options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlWordPrefixBytes)
                    : WordByteVariants());
                return true;
            case RegexSyntaxKind.WhitespaceClass:
                AddFirstByteVariants(bytes, options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlSpacePrefixBytes)
                    : WhitespaceByteVariants());
                return true;
            case RegexSyntaxKind.LetterClass:
                AddFirstByteVariants(bytes, options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddAlphabeticPrefixBytes)
                    : LetterByteVariants());
                return true;
            case RegexSyntaxKind.AlphanumericClass:
                if (options.UnicodeClasses)
                {
                    List<byte[]> variants = [];
                    RegexUnicodeTables.AddAlphabeticPrefixBytes(variants);
                    RegexUnicodeTables.AddDecimalNumberPrefixBytes(variants);
                    AddFirstByteVariants(bytes, variants.ToArray());
                }
                else
                {
                    AddFirstByteVariants(bytes, AlphanumericByteVariants());
                }

                return true;
            case RegexSyntaxKind.Sequence:
                return TryCollectSequenceFirstBytes((RegexSequenceNode)node, options, bytes, out canMatchEmpty);
            case RegexSyntaxKind.Alternation:
                return TryCollectAlternationFirstBytes((RegexAlternationNode)node, options, bytes, out canMatchEmpty);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryCollectFirstBytes(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    bytes,
                    out canMatchEmpty);
            case RegexSyntaxKind.Repetition:
                return TryCollectRepetitionFirstBytes((RegexRepetitionNode)node, options, bytes, out canMatchEmpty);
            default:
                return false;
        }
    }

    private static bool TryCollectSequenceFirstBytes(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<byte> bytes,
        out bool canMatchEmpty)
    {
        canMatchEmpty = true;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryCollectFirstBytes(child, currentOptions, bytes, out bool childCanMatchEmpty))
            {
                return false;
            }

            if (!childCanMatchEmpty)
            {
                canMatchEmpty = false;
                return true;
            }
        }

        return true;
    }

    private static bool TryCollectAlternationFirstBytes(
        RegexAlternationNode node,
        RegexCompileOptions options,
        List<byte> bytes,
        out bool canMatchEmpty)
    {
        canMatchEmpty = false;
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (!TryCollectFirstBytes(node.Alternatives[index], options, bytes, out bool alternativeCanMatchEmpty))
            {
                return false;
            }

            canMatchEmpty |= alternativeCanMatchEmpty;
        }

        return true;
    }

    private static bool TryCollectRepetitionFirstBytes(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<byte> bytes,
        out bool canMatchEmpty)
    {
        canMatchEmpty = true;
        if (node.Maximum == 0)
        {
            return true;
        }

        if (!TryCollectFirstBytes(node.Child, options, bytes, out bool childCanMatchEmpty))
        {
            return false;
        }

        canMatchEmpty = node.Minimum == 0 || childCanMatchEmpty;
        return true;
    }

    private static bool TryAddLiteralFirstBytes(
        ReadOnlySpan<byte> literal,
        RegexCompileOptions options,
        List<byte> bytes,
        out bool canMatchEmpty)
    {
        canMatchEmpty = literal.Length == 0;
        if (literal.Length == 0)
        {
            return true;
        }

        if (options.CaseInsensitive && options.UnicodeClasses)
        {
            return TryAddUnicodeLiteralFirstBytes(literal, bytes);
        }

        AddLiteralFirstByte(bytes, literal[0], options.CaseInsensitive);
        return true;
    }

    private static bool TryAddUnicodeLiteralFirstBytes(ReadOnlySpan<byte> literal, List<byte> bytes)
    {
        OperationStatus status = Rune.DecodeFromUtf8(literal, out Rune rune, out int consumed);
        if (status != OperationStatus.Done || consumed <= 0)
        {
            AddLiteralFirstByte(bytes, literal[0], caseInsensitive: true);
            return true;
        }

        List<Rune> equivalents = [];
        RegexUnicodeTables.AddSimpleCaseFoldEquivalents(rune, equivalents);
        for (int index = 0; index < equivalents.Count; index++)
        {
            AddRuneFirstByte(bytes, equivalents[index]);
        }

        return true;
    }

    private static bool TryAddCharacterClassFirstBytes(
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        List<byte> bytes,
        out bool canMatchEmpty)
    {
        canMatchEmpty = false;
        if (expression.Length == 0 || expression[0] == (byte)'^')
        {
            return false;
        }

        int index = 0;
        while (index < expression.Length)
        {
            if (expression[index] == (byte)'[' &&
                index + 1 < expression.Length &&
                expression[index + 1] == (byte)':')
            {
                return false;
            }

            if (!TryReadFirstByteClassToken(
                    expression,
                    options,
                    ref index,
                    out byte[][] tokenVariants,
                    out byte? rangeLiteral,
                    out bool sealedToken))
            {
                return false;
            }

            if (index < expression.Length - 1 && expression[index] == (byte)'-')
            {
                if (!rangeLiteral.HasValue || sealedToken)
                {
                    return false;
                }

                index++;
                if (!TryReadFirstByteClassToken(
                        expression,
                        options,
                        ref index,
                        out _,
                        out byte? rangeEnd,
                        out bool sealedRangeEnd) ||
                    !rangeEnd.HasValue ||
                    sealedRangeEnd ||
                    rangeEnd.Value < rangeLiteral.Value)
                {
                    return false;
                }

                for (int value = rangeLiteral.Value; value <= rangeEnd.Value; value++)
                {
                    AddClassLiteralFirstByte(bytes, (byte)value, options);
                }
            }
            else
            {
                AddFirstByteVariants(bytes, tokenVariants);
            }
        }

        return true;
    }

    private static bool TryReadFirstByteClassToken(
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        ref int index,
        out byte[][] variants,
        out byte? rangeLiteral,
        out bool sealedToken)
    {
        variants = [];
        rangeLiteral = null;
        sealedToken = false;
        if (index >= expression.Length)
        {
            return false;
        }

        byte value = expression[index++];
        if (value != (byte)'\\')
        {
            var bytes = new List<byte>();
            AddClassLiteralFirstByte(bytes, value, options);
            variants = ToSingleByteVariants(bytes);
            rangeLiteral = value;
            return true;
        }

        if (index >= expression.Length)
        {
            return false;
        }

        byte escaped = expression[index++];
        switch (escaped)
        {
            case (byte)'d':
                variants = options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddDecimalNumberPrefixBytes)
                    : ByteRangeVariants((byte)'0', (byte)'9');
                sealedToken = options.UnicodeClasses;
                return true;
            case (byte)'w':
                variants = options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlWordPrefixBytes)
                    : WordByteVariants();
                sealedToken = options.UnicodeClasses;
                return true;
            case (byte)'s':
                variants = options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlSpacePrefixBytes)
                    : WhitespaceByteVariants();
                sealedToken = options.UnicodeClasses;
                return true;
            case (byte)'n':
                variants = [[(byte)'\n']];
                rangeLiteral = (byte)'\n';
                return true;
            case (byte)'t':
                variants = [[(byte)'\t']];
                rangeLiteral = (byte)'\t';
                return true;
            case (byte)'r':
                variants = [[(byte)'\r']];
                rangeLiteral = (byte)'\r';
                return true;
            case (byte)'f':
                variants = [[(byte)'\f']];
                rangeLiteral = (byte)'\f';
                return true;
            case (byte)'D':
            case (byte)'W':
            case (byte)'S':
            case (byte)'p':
            case (byte)'P':
                return false;
            default:
                var bytes = new List<byte>();
                AddClassLiteralFirstByte(bytes, escaped, options);
                variants = ToSingleByteVariants(bytes);
                rangeLiteral = escaped;
                return true;
        }
    }

    private static void AddFirstByteVariants(List<byte> bytes, byte[][] variants)
    {
        for (int index = 0; index < variants.Length; index++)
        {
            if (variants[index].Length > 0)
            {
                AddDistinct(bytes, variants[index][0]);
            }
        }
    }

    private static void AddLiteralFirstByte(List<byte> bytes, byte value, bool caseInsensitive)
    {
        AddDistinct(bytes, value);
        if (caseInsensitive && IsAsciiCased(value))
        {
            AddDistinct(bytes, (byte)char.ToLowerInvariant((char)value));
            AddDistinct(bytes, (byte)char.ToUpperInvariant((char)value));
        }
    }

    private static void AddClassLiteralFirstByte(List<byte> bytes, byte value, RegexCompileOptions options)
    {
        if (options.CaseInsensitive && options.UnicodeClasses)
        {
            Span<byte> literal = [value];
            TryAddUnicodeLiteralFirstBytes(literal, bytes);
            return;
        }

        AddLiteralFirstByte(bytes, value, options.CaseInsensitive);
    }

    private static void AddRuneFirstByte(List<byte> bytes, Rune rune)
    {
        if (rune.IsAscii)
        {
            AddDistinct(bytes, (byte)rune.Value);
            return;
        }

        Span<byte> encoded = stackalloc byte[4];
        int written = rune.EncodeToUtf8(encoded);
        if (written > 0)
        {
            AddDistinct(bytes, encoded[0]);
        }
    }

    private static byte[][] ToSingleByteVariants(List<byte> bytes)
    {
        byte[][] variants = new byte[bytes.Count][];
        for (int index = 0; index < bytes.Count; index++)
        {
            variants[index] = [bytes[index]];
        }

        return variants;
    }

    private static byte[][] UnicodePrefixVariants(Action<List<byte[]>> addPrefixes)
    {
        List<byte[]> prefixes = [];
        addPrefixes(prefixes);
        return prefixes.ToArray();
    }

    private static byte[][] ByteRangeVariants(byte start, byte end)
    {
        byte[][] variants = new byte[end - start + 1][];
        for (int index = 0; index < variants.Length; index++)
        {
            variants[index] = [(byte)(start + index)];
        }

        return variants;
    }

    private static byte[][] WordByteVariants()
    {
        byte[][] variants = new byte[63][];
        int index = 0;
        FillRangeVariants(variants, ref index, (byte)'0', (byte)'9');
        FillRangeVariants(variants, ref index, (byte)'A', (byte)'Z');
        FillRangeVariants(variants, ref index, (byte)'a', (byte)'z');
        variants[index] = [(byte)'_'];
        return variants;
    }

    private static byte[][] LetterByteVariants()
    {
        byte[][] variants = new byte[52][];
        int index = 0;
        FillRangeVariants(variants, ref index, (byte)'A', (byte)'Z');
        FillRangeVariants(variants, ref index, (byte)'a', (byte)'z');
        return variants;
    }

    private static byte[][] AlphanumericByteVariants()
    {
        byte[][] variants = new byte[62][];
        int index = 0;
        FillRangeVariants(variants, ref index, (byte)'0', (byte)'9');
        FillRangeVariants(variants, ref index, (byte)'A', (byte)'Z');
        FillRangeVariants(variants, ref index, (byte)'a', (byte)'z');
        return variants;
    }

    private static byte[][] WhitespaceByteVariants()
    {
        return [[(byte)' '], [(byte)'\t'], [(byte)'\n'], [(byte)'\r'], [(byte)'\f'], [0x0b]];
    }

    private static void FillRangeVariants(byte[][] variants, ref int index, byte start, byte end)
    {
        for (int value = start; value <= end; value++)
        {
            variants[index++] = [(byte)value];
        }
    }

    private static bool TryAppend(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte[]> allowed,
        bool caseFoldMayNeedUnicodeScalars,
        out bool canContinue)
    {
        canContinue = false;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.StartAnchor:
            case RegexSyntaxKind.EndAnchor:
            case RegexSyntaxKind.AbsoluteStartAnchor:
            case RegexSyntaxKind.AbsoluteEndAnchor:
            case RegexSyntaxKind.WordBoundary:
            case RegexSyntaxKind.NotWordBoundary:
            case RegexSyntaxKind.WordStartBoundary:
            case RegexSyntaxKind.WordEndBoundary:
            case RegexSyntaxKind.WordStartHalfBoundary:
            case RegexSyntaxKind.WordEndHalfBoundary:
                canContinue = true;
                return true;
            case RegexSyntaxKind.Literal:
                return TryAppendLiteral(((RegexAtomNode)node).Value.Span, options, allowed, caseFoldMayNeedUnicodeScalars, out canContinue);
            case RegexSyntaxKind.CharacterClass:
                if (options.CaseInsensitive && options.UnicodeClasses)
                {
                    return false;
                }

                return TryAppendCharacterClass(((RegexAtomNode)node).Value.Span, options, allowed, out canContinue);
            case RegexSyntaxKind.DigitClass:
                return !options.UnicodeClasses && TryAppendByteSet(allowed, DigitBytes(), out canContinue);
            case RegexSyntaxKind.WordClass:
                return !options.UnicodeClasses && TryAppendByteSet(allowed, WordBytes(), out canContinue);
            case RegexSyntaxKind.WhitespaceClass:
                return !options.UnicodeClasses && TryAppendByteSet(allowed, WhitespaceBytes(), out canContinue);
            case RegexSyntaxKind.Sequence:
                return TryAppendSequence((RegexSequenceNode)node, options, allowed, caseFoldMayNeedUnicodeScalars, out canContinue);
            case RegexSyntaxKind.Alternation:
                return TryAppendAlternation((RegexAlternationNode)node, options, allowed, caseFoldMayNeedUnicodeScalars, out canContinue);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAppend(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), allowed, caseFoldMayNeedUnicodeScalars, out canContinue);
            case RegexSyntaxKind.Repetition:
                return TryAppendRepetition((RegexRepetitionNode)node, options, allowed, caseFoldMayNeedUnicodeScalars, out canContinue);
            default:
                return false;
        }
    }

    private static bool TryAppendSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<byte[]> allowed,
        bool caseFoldMayNeedUnicodeScalars,
        out bool canContinue)
    {
        int originalCount = allowed.Count;
        canContinue = true;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryAppend(child, currentOptions, allowed, caseFoldMayNeedUnicodeScalars, out bool childCanContinue))
            {
                canContinue = false;
                return allowed.Count > originalCount;
            }

            if (!childCanContinue)
            {
                canContinue = false;
                return true;
            }
        }

        return allowed.Count > originalCount;
    }

    private static bool TryAppendAlternation(
        RegexAlternationNode node,
        RegexCompileOptions options,
        List<byte[]> allowed,
        bool caseFoldMayNeedUnicodeScalars,
        out bool canContinue)
    {
        canContinue = false;
        if (node.Alternatives.Count == 0)
        {
            return false;
        }

        var merged = new List<List<byte>>();
        bool allAlternativesCanContinue = true;
        bool allAlternativesSameLength = true;
        int firstAlternativeLength = -1;
        int commonLength = int.MaxValue;
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            var alternativeAllowed = new List<byte[]>();
            if (!TryAppend(
                    node.Alternatives[index],
                    options,
                    alternativeAllowed,
                    caseFoldMayNeedUnicodeScalars,
                    out bool alternativeCanContinue) ||
                alternativeAllowed.Count == 0)
            {
                return false;
            }

            allAlternativesCanContinue &= alternativeCanContinue;
            if (index == 0)
            {
                firstAlternativeLength = alternativeAllowed.Count;
            }
            else
            {
                allAlternativesSameLength &= alternativeAllowed.Count == firstAlternativeLength;
            }

            commonLength = Math.Min(commonLength, alternativeAllowed.Count);
            if (index == 0)
            {
                for (int allowedIndex = 0; allowedIndex < alternativeAllowed.Count; allowedIndex++)
                {
                    merged.Add(new List<byte>(alternativeAllowed[allowedIndex]));
                }

                continue;
            }

            if (merged.Count > commonLength)
            {
                merged.RemoveRange(commonLength, merged.Count - commonLength);
            }

            for (int allowedIndex = 0; allowedIndex < commonLength; allowedIndex++)
            {
                byte[] bytes = alternativeAllowed[allowedIndex];
                for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
                {
                    AddDistinct(merged[allowedIndex], bytes[byteIndex]);
                }
            }
        }

        if (commonLength == 0 || commonLength == int.MaxValue)
        {
            return false;
        }

        int originalCount = allowed.Count;
        for (int index = 0; index < commonLength; index++)
        {
            if (!TryAppendByteSet(allowed, merged[index].ToArray(), out _))
            {
                canContinue = false;
                return allowed.Count > originalCount;
            }
        }

        canContinue = allAlternativesCanContinue && allAlternativesSameLength;
        return allowed.Count > originalCount;
    }

    private static bool TryAppendRepetition(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<byte[]> allowed,
        bool caseFoldMayNeedUnicodeScalars,
        out bool canContinue)
    {
        canContinue = false;
        if (node.Minimum == 0)
        {
            return false;
        }

        for (int count = 0; count < node.Minimum; count++)
        {
            if (!TryAppend(node.Child, options, allowed, caseFoldMayNeedUnicodeScalars, out bool childCanContinue))
            {
                return false;
            }

            if (!childCanContinue)
            {
                return true;
            }
        }

        canContinue = node.Maximum == node.Minimum;
        return true;
    }

    private static bool TryAppendLiteral(
        ReadOnlySpan<byte> literal,
        RegexCompileOptions options,
        List<byte[]> allowed,
        bool caseFoldMayNeedUnicodeScalars,
        out bool canContinue)
    {
        canContinue = true;
        if (options.CaseInsensitive && options.UnicodeClasses)
        {
            return false;
        }

        for (int index = 0; index < literal.Length; index++)
        {
            byte value = literal[index];
            byte[] bytes = options.CaseInsensitive && IsAsciiCased(value)
                ? [(byte)char.ToLowerInvariant((char)value), (byte)char.ToUpperInvariant((char)value)]
                : [value];
            if (!TryAppendByteSet(allowed, bytes, out _))
            {
                return false;
            }
        }

        return literal.Length > 0;
    }

    private static bool TryAppendCharacterClass(
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        List<byte[]> allowed,
        out bool canContinue)
    {
        canContinue = false;
        if (expression.Length == 0 || expression[0] == (byte)'^')
        {
            return false;
        }

        var bytes = new List<byte>();
        int index = 0;
        while (index < expression.Length)
        {
            if (expression[index] == (byte)'[' &&
                index + 1 < expression.Length &&
                expression[index + 1] == (byte)':')
            {
                return false;
            }

            if (!TryReadClassToken(expression, options, ref index, out byte[] tokenBytes, out byte? rangeLiteral))
            {
                return false;
            }

            if (index < expression.Length - 1 && expression[index] == (byte)'-')
            {
                index++;
                if (!rangeLiteral.HasValue ||
                    !TryReadClassToken(expression, options, ref index, out _, out byte? rangeEnd) ||
                    !rangeEnd.HasValue ||
                    rangeEnd.Value < rangeLiteral.Value)
                {
                    return false;
                }

                for (int value = rangeLiteral.Value; value <= rangeEnd.Value; value++)
                {
                    AddDistinct(bytes, (byte)value, options.CaseInsensitive);
                }
            }
            else
            {
                for (int tokenIndex = 0; tokenIndex < tokenBytes.Length; tokenIndex++)
                {
                    AddDistinct(bytes, tokenBytes[tokenIndex], options.CaseInsensitive);
                }
            }
        }

        return bytes.Count > 0 && TryAppendByteSet(allowed, bytes.ToArray(), out canContinue);
    }

    private static bool TryReadClassToken(
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        ref int index,
        out byte[] bytes,
        out byte? rangeLiteral)
    {
        bytes = [];
        rangeLiteral = null;
        if (index >= expression.Length)
        {
            return false;
        }

        byte value = expression[index++];
        if (value != (byte)'\\')
        {
            bytes = [value];
            rangeLiteral = value;
            return true;
        }

        if (index >= expression.Length)
        {
            return false;
        }

        byte escaped = expression[index++];
        switch (escaped)
        {
            case (byte)'d':
                if (options.UnicodeClasses)
                {
                    return false;
                }

                bytes = DigitBytes();
                return true;
            case (byte)'w':
                if (options.UnicodeClasses)
                {
                    return false;
                }

                bytes = WordBytes();
                return true;
            case (byte)'s':
                if (options.UnicodeClasses)
                {
                    return false;
                }

                bytes = WhitespaceBytes();
                return true;
            case (byte)'n':
                bytes = [(byte)'\n'];
                rangeLiteral = (byte)'\n';
                return true;
            case (byte)'t':
                bytes = [(byte)'\t'];
                rangeLiteral = (byte)'\t';
                return true;
            case (byte)'r':
                bytes = [(byte)'\r'];
                rangeLiteral = (byte)'\r';
                return true;
            case (byte)'f':
                bytes = [(byte)'\f'];
                rangeLiteral = (byte)'\f';
                return true;
            case (byte)'D':
            case (byte)'W':
            case (byte)'S':
            case (byte)'p':
            case (byte)'P':
                return false;
            default:
                bytes = [escaped];
                rangeLiteral = escaped;
                return true;
        }
    }

    private static bool TryAppendByteSet(List<byte[]> allowed, byte[] bytes, out bool canContinue)
    {
        canContinue = true;
        if (allowed.Count >= MaxPredicateLength || bytes.Length == 0)
        {
            return false;
        }

        allowed.Add(bytes);
        return true;
    }

    private static byte[] DigitBytes()
    {
        return [(byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9'];
    }

    private static byte[] WordBytes()
    {
        var bytes = new List<byte>();
        for (byte value = (byte)'0'; value <= (byte)'9'; value++)
        {
            bytes.Add(value);
        }

        for (byte value = (byte)'A'; value <= (byte)'Z'; value++)
        {
            bytes.Add(value);
        }

        for (byte value = (byte)'a'; value <= (byte)'z'; value++)
        {
            bytes.Add(value);
        }

        bytes.Add((byte)'_');
        return bytes.ToArray();
    }

    private static byte[] WhitespaceBytes()
    {
        return [(byte)' ', (byte)'\t', (byte)'\n', (byte)'\r', (byte)'\f', 0x0b];
    }

    private static void AddDistinct(List<byte> bytes, byte value, bool caseInsensitive)
    {
        AddDistinct(bytes, value);
        if (caseInsensitive && IsAsciiCased(value))
        {
            AddDistinct(bytes, (byte)char.ToLowerInvariant((char)value));
            AddDistinct(bytes, (byte)char.ToUpperInvariant((char)value));
        }
    }

    private static void AddDistinct(List<byte> bytes, byte value)
    {
        if (!bytes.Contains(value))
        {
            bytes.Add(value);
        }
    }

    private static bool IsAsciiCased(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
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
}
