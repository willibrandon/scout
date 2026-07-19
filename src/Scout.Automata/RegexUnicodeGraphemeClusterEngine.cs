using System.Runtime.CompilerServices;

namespace Scout;

/// <summary>
/// Executes the canonical extended Unicode grapheme-cluster expression.
/// </summary>
internal sealed class RegexUnicodeGraphemeClusterEngine
{
    private RegexUnicodeGraphemeClusterEngine()
    {
    }

    /// <summary>
    /// Creates the structural grapheme-cluster engine when the parsed syntax is canonical.
    /// </summary>
    /// <param name="root">The parsed syntax root.</param>
    /// <param name="options">The effective compile options.</param>
    /// <param name="engine">Receives the structural engine.</param>
    /// <returns><see langword="true" /> when the syntax represents extended grapheme clusters.</returns>
    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexUnicodeGraphemeClusterEngine? engine)
    {
        engine = null;
        if (!options.UnicodeClasses || options.SwapGreed)
        {
            return false;
        }

        if (!TryNormalizeRoot(root, out RegexSyntaxNode normalized) ||
            !IsUnicodeGraphemeClusterPattern(normalized))
        {
            return false;
        }

        engine = new RegexUnicodeGraphemeClusterEngine();
        return true;
    }

    /// <summary>
    /// Finds the next extended grapheme cluster at or after an offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The next grapheme-cluster match, or <see langword="null" />.</returns>
    public static RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int position = Math.Clamp(startAt, 0, haystack.Length);
        while (position < haystack.Length)
        {
            if (TryMatchAt(haystack, position, out int length))
            {
                return new RegexMatch(position, length);
            }

            position++;
        }

        return null;
    }

    /// <summary>
    /// Matches one extended grapheme cluster at an exact offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The required match start.</param>
    /// <returns>The grapheme-cluster match, or <see langword="null" />.</returns>
    public static RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int position = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, position, out int length)
            ? new RegexMatch(position, length)
            : null;
    }

    /// <summary>
    /// Counts non-overlapping extended grapheme clusters at or after an offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The number of grapheme-cluster matches.</returns>
    public static long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    /// <summary>
    /// Sums the byte lengths of non-overlapping extended grapheme clusters.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The sum of matched byte lengths.</returns>
    public static long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    /// <summary>
    /// Attempts to measure one extended grapheme cluster at an exact offset.
    /// </summary>
    /// <param name="haystack">The bytes to inspect.</param>
    /// <param name="start">The required match start.</param>
    /// <param name="length">Receives the matched byte length.</param>
    /// <returns><see langword="true" /> when a grapheme cluster starts at the offset.</returns>
    public static bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            !TryDecodeClass(haystack, start, out int scalar, out int scalarLength, out RegexGraphemeClass kind))
        {
            return false;
        }

        int scalarEnd = start + scalarLength;
        if (kind == RegexGraphemeClass.Cr)
        {
            length = TryDecodeClass(
                    haystack,
                    scalarEnd,
                    out _,
                    out int nextLength,
                    out RegexGraphemeClass nextKind) &&
                nextKind == RegexGraphemeClass.Lf
                    ? scalarLength + nextLength
                    : scalarLength;
            return true;
        }

        if (kind is RegexGraphemeClass.Lf or RegexGraphemeClass.Control)
        {
            length = scalarLength;
            return true;
        }

        int coreStart = start;
        if (kind == RegexGraphemeClass.Prepend)
        {
            int prependEnd = scalarEnd;
            int nextStart = scalarEnd;
            while (TryDecodeClass(
                       haystack,
                       nextStart,
                       out _,
                       out int prependLength,
                       out RegexGraphemeClass prependKind) &&
                   prependKind == RegexGraphemeClass.Prepend)
            {
                nextStart += prependLength;
                prependEnd = nextStart;
            }

            if (!TryDecodeClass(
                    haystack,
                    nextStart,
                    out scalar,
                    out scalarLength,
                    out kind) ||
                kind is RegexGraphemeClass.Cr or RegexGraphemeClass.Lf or RegexGraphemeClass.Control)
            {
                length = prependEnd - start;
                return true;
            }

            coreStart = nextStart;
            scalarEnd = coreStart + scalarLength;
        }

        int coreEnd = MatchCore(haystack, coreStart, scalar, scalarLength, kind);
        length = ConsumeTrailingMarks(haystack, coreEnd) - start;
        return true;
    }

    private static long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int position = Math.Clamp(startAt, 0, haystack.Length);
        while (position < haystack.Length)
        {
            if (haystack[position] <= 0x7F)
            {
                int asciiEnd = position + 1;
                while (asciiEnd < haystack.Length && haystack[asciiEnd] <= 0x7F)
                {
                    asciiEnd++;
                }

                int bulkEnd = asciiEnd;
                if (asciiEnd < haystack.Length &&
                    IsAsciiExtendableGrapheme(haystack[asciiEnd - 1]))
                {
                    bulkEnd--;
                }

                if (bulkEnd > position)
                {
                    ReadOnlySpan<byte> asciiRun = haystack[position..bulkEnd];
                    total += sumSpans ? asciiRun.Length : CountAsciiGraphemeClusters(asciiRun);
                    position = bulkEnd;
                    continue;
                }
            }

            if (!TryMatchAt(haystack, position, out int length))
            {
                position++;
                continue;
            }

            total += sumSpans ? length : 1;
            position += length;
        }

        return total;
    }

    private static long CountAsciiGraphemeClusters(ReadOnlySpan<byte> ascii)
    {
        long count = ascii.Length;
        int search = 0;
        while (search < ascii.Length)
        {
            int relative = ascii[search..].IndexOf((byte)'\r');
            if (relative < 0)
            {
                return count;
            }

            int cr = search + relative;
            if (cr + 1 < ascii.Length && ascii[cr + 1] == (byte)'\n')
            {
                count--;
                search = cr + 2;
                continue;
            }

            search = cr + 1;
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiExtendableGrapheme(byte value)
    {
        return value is > 0x1F and not 0x7F;
    }

    private static int MatchCore(
        ReadOnlySpan<byte> haystack,
        int start,
        int scalar,
        int scalarLength,
        RegexGraphemeClass kind)
    {
        int hangulEnd = MatchHangulCore(haystack, start, scalarLength, kind);
        if (hangulEnd >= 0)
        {
            return hangulEnd;
        }

        int scalarEnd = start + scalarLength;
        if (kind == RegexGraphemeClass.RegionalIndicator &&
            TryDecodeClass(
                haystack,
                scalarEnd,
                out _,
                out int nextLength,
                out RegexGraphemeClass nextKind) &&
            nextKind == RegexGraphemeClass.RegionalIndicator)
        {
            return scalarEnd + nextLength;
        }

        if (scalar > 0x7F && IsExtendedPictographic(scalar))
        {
            return MatchExtendedPictographicCore(haystack, scalarEnd);
        }

        return scalarEnd;
    }

    private static int MatchHangulCore(
        ReadOnlySpan<byte> haystack,
        int start,
        int scalarLength,
        RegexGraphemeClass kind)
    {
        int position = start + scalarLength;
        if (kind == RegexGraphemeClass.L)
        {
            position = ConsumeClass(haystack, position, RegexGraphemeClass.L);
            if (!TryDecodeClass(
                    haystack,
                    position,
                    out _,
                    out int nextLength,
                    out RegexGraphemeClass nextKind))
            {
                return position;
            }

            if (nextKind == RegexGraphemeClass.V)
            {
                position = ConsumeClass(haystack, position + nextLength, RegexGraphemeClass.V);
                return ConsumeClass(haystack, position, RegexGraphemeClass.T);
            }

            if (nextKind == RegexGraphemeClass.Lv)
            {
                position = ConsumeClass(haystack, position + nextLength, RegexGraphemeClass.V);
                return ConsumeClass(haystack, position, RegexGraphemeClass.T);
            }

            if (nextKind == RegexGraphemeClass.Lvt)
            {
                return ConsumeClass(haystack, position + nextLength, RegexGraphemeClass.T);
            }

            return position;
        }

        if (kind == RegexGraphemeClass.V)
        {
            position = ConsumeClass(haystack, position, RegexGraphemeClass.V);
            return ConsumeClass(haystack, position, RegexGraphemeClass.T);
        }

        if (kind == RegexGraphemeClass.Lv)
        {
            position = ConsumeClass(haystack, position, RegexGraphemeClass.V);
            return ConsumeClass(haystack, position, RegexGraphemeClass.T);
        }

        if (kind == RegexGraphemeClass.Lvt)
        {
            return ConsumeClass(haystack, position, RegexGraphemeClass.T);
        }

        return kind == RegexGraphemeClass.T
            ? ConsumeClass(haystack, position, RegexGraphemeClass.T)
            : -1;
    }

    private static int MatchExtendedPictographicCore(
        ReadOnlySpan<byte> haystack,
        int position)
    {
        while (true)
        {
            int scan = ConsumeClass(haystack, position, RegexGraphemeClass.Extend);
            if (!TryDecodeClass(
                    haystack,
                    scan,
                    out _,
                    out int zwjLength,
                    out RegexGraphemeClass zwjKind) ||
                zwjKind != RegexGraphemeClass.Zwj)
            {
                return position;
            }

            int afterZwj = scan + zwjLength;
            if (!TryDecodeClass(
                    haystack,
                    afterZwj,
                    out int nextScalar,
                    out int pictographicLength,
                    out _) ||
                nextScalar <= 0x7F ||
                !IsExtendedPictographic(nextScalar))
            {
                return position;
            }

            position = afterZwj + pictographicLength;
        }
    }

    private static int ConsumeTrailingMarks(
        ReadOnlySpan<byte> haystack,
        int position)
    {
        while (TryDecodeClass(
                   haystack,
                   position,
                   out _,
                   out int length,
                   out RegexGraphemeClass kind) &&
               IsTrailingMark(kind))
        {
            position += length;
        }

        return position;
    }

    private static int ConsumeClass(
        ReadOnlySpan<byte> haystack,
        int position,
        RegexGraphemeClass expected)
    {
        while (TryDecodeClass(
                   haystack,
                   position,
                   out _,
                   out int length,
                   out RegexGraphemeClass kind) &&
               kind == expected)
        {
            position += length;
        }

        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTrailingMark(RegexGraphemeClass kind)
    {
        return kind is RegexGraphemeClass.Extend or RegexGraphemeClass.Zwj or RegexGraphemeClass.SpacingMark;
    }

    private static bool TryDecodeClass(
        ReadOnlySpan<byte> haystack,
        int position,
        out int scalar,
        out int length,
        out RegexGraphemeClass kind)
    {
        if (!TryDecodeUtf8Scalar(haystack, position, out scalar, out length))
        {
            kind = RegexGraphemeClass.Other;
            return false;
        }

        kind = Classify(scalar);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RegexGraphemeClass Classify(int scalar)
    {
        if (scalar <= 0x7F)
        {
            return scalar switch
            {
                '\r' => RegexGraphemeClass.Cr,
                '\n' => RegexGraphemeClass.Lf,
                <= 0x1F or 0x7F => RegexGraphemeClass.Control,
                _ => RegexGraphemeClass.Other,
            };
        }

        return RegexGraphemeTables.Classify(scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExtendedPictographic(int scalar)
    {
        return RegexGraphemeTables.IsExtendedPictographic(scalar);
    }

    private static bool TryDecodeUtf8Scalar(ReadOnlySpan<byte> haystack, int position, out int scalar, out int length)
    {
        scalar = 0;
        length = 0;
        if ((uint)position >= (uint)haystack.Length)
        {
            return false;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            scalar = first;
            length = 1;
            return true;
        }

        if (first is >= 0xC2 and <= 0xDF)
        {
            if (position + 1 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]))
            {
                return false;
            }

            scalar = ((first & 0x1F) << 6) | (haystack[position + 1] & 0x3F);
            length = 2;
            return true;
        }

        if (first is >= 0xE0 and <= 0xEF)
        {
            if (position + 2 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]) ||
                !IsUtf8Continuation(haystack[position + 2]) ||
                first == 0xE0 && haystack[position + 1] < 0xA0 ||
                first == 0xED && haystack[position + 1] >= 0xA0)
            {
                return false;
            }

            scalar = ((first & 0x0F) << 12) |
                ((haystack[position + 1] & 0x3F) << 6) |
                (haystack[position + 2] & 0x3F);
            length = 3;
            return true;
        }

        if (first is >= 0xF0 and <= 0xF4)
        {
            if (position + 3 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]) ||
                !IsUtf8Continuation(haystack[position + 2]) ||
                !IsUtf8Continuation(haystack[position + 3]) ||
                first == 0xF0 && haystack[position + 1] < 0x90 ||
                first == 0xF4 && haystack[position + 1] >= 0x90)
            {
                return false;
            }

            scalar = ((first & 0x07) << 18) |
                ((haystack[position + 1] & 0x3F) << 12) |
                ((haystack[position + 2] & 0x3F) << 6) |
                (haystack[position + 3] & 0x3F);
            length = 4;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUtf8Continuation(byte value)
    {
        return (value & 0xC0) == 0x80;
    }

    private static bool IsUnicodeGraphemeClusterPattern(RegexSyntaxNode root)
    {
        root = UnwrapTransparentGroups(root);
        return root is RegexAlternationNode { Alternatives.Count: 4 } alternation &&
            IsPropertySequence(
                alternation.Alternatives[0],
                RegexUnicodePropertyKind.GraphemeClusterBreakCr,
                RegexUnicodePropertyKind.GraphemeClusterBreakLf) &&
            IsProperty(alternation.Alternatives[1], RegexUnicodePropertyKind.GraphemeClusterBreakControl) &&
            IsMainClusterAlternative(alternation.Alternatives[2]) &&
            IsAnyClass(alternation.Alternatives[3]);
    }

    private static bool IsMainClusterAlternative(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsZeroOrMoreProperty(sequence.Nodes[0], RegexUnicodePropertyKind.GraphemeClusterBreakPrepend) &&
            IsGraphemeCore(sequence.Nodes[1]) &&
            IsZeroOrMoreClassSet(
                sequence.Nodes[2],
                RegexUnicodePropertyKind.GraphemeClusterBreakExtend,
                RegexUnicodePropertyKind.GraphemeClusterBreakZwj,
                RegexUnicodePropertyKind.GraphemeClusterBreakSpacingMark);
    }

    private static bool IsGraphemeCore(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAlternationNode { Alternatives.Count: 4 } alternation &&
            IsHangulCore(alternation.Alternatives[0]) &&
            IsPropertySequence(
                alternation.Alternatives[1],
                RegexUnicodePropertyKind.GraphemeClusterBreakRegionalIndicator,
                RegexUnicodePropertyKind.GraphemeClusterBreakRegionalIndicator) &&
            IsExtendedPictographicCore(alternation.Alternatives[2]) &&
            IsNotControlCrLfClass(alternation.Alternatives[3]);
    }

    private static bool IsHangulCore(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAlternationNode { Alternatives.Count: 3 } alternation &&
            IsLeadingHangulSyllableCore(alternation.Alternatives[0]) &&
            IsOneOrMoreProperty(alternation.Alternatives[1], RegexUnicodePropertyKind.GraphemeClusterBreakL) &&
            IsOneOrMoreProperty(alternation.Alternatives[2], RegexUnicodePropertyKind.GraphemeClusterBreakT);
    }

    private static bool IsLeadingHangulSyllableCore(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsZeroOrMoreProperty(sequence.Nodes[0], RegexUnicodePropertyKind.GraphemeClusterBreakL) &&
            IsHangulVowelCore(sequence.Nodes[1]) &&
            IsZeroOrMoreProperty(sequence.Nodes[2], RegexUnicodePropertyKind.GraphemeClusterBreakT);
    }

    private static bool IsHangulVowelCore(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAlternationNode { Alternatives.Count: 3 } alternation &&
            IsOneOrMoreProperty(alternation.Alternatives[0], RegexUnicodePropertyKind.GraphemeClusterBreakV) &&
            IsPropertyThenZeroOrMore(
                alternation.Alternatives[1],
                RegexUnicodePropertyKind.GraphemeClusterBreakLv,
                RegexUnicodePropertyKind.GraphemeClusterBreakV) &&
            IsProperty(alternation.Alternatives[2], RegexUnicodePropertyKind.GraphemeClusterBreakLvt);
    }

    private static bool IsExtendedPictographicCore(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            IsProperty(sequence.Nodes[0], RegexUnicodePropertyKind.ExtendedPictographic) &&
            IsZeroOrMoreExtendedPictographicLink(sequence.Nodes[1]);
    }

    private static bool IsZeroOrMoreExtendedPictographicLink(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode { Minimum: 0, Maximum: null } repetition &&
            IsExtendedPictographicLink(repetition.Child);
    }

    private static bool IsExtendedPictographicLink(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsZeroOrMoreProperty(sequence.Nodes[0], RegexUnicodePropertyKind.GraphemeClusterBreakExtend) &&
            IsProperty(sequence.Nodes[1], RegexUnicodePropertyKind.GraphemeClusterBreakZwj) &&
            IsProperty(sequence.Nodes[2], RegexUnicodePropertyKind.ExtendedPictographic);
    }

    private static bool IsPropertyThenZeroOrMore(
        RegexSyntaxNode node,
        RegexUnicodePropertyKind first,
        RegexUnicodePropertyKind repeated)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            IsProperty(sequence.Nodes[0], first) &&
            IsZeroOrMoreProperty(sequence.Nodes[1], repeated);
    }

    private static bool IsPropertySequence(
        RegexSyntaxNode node,
        RegexUnicodePropertyKind first,
        RegexUnicodePropertyKind second)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexSequenceNode sequence &&
            TryGetRuntimeSequenceOffset(sequence, expectedNodeCount: 2, out int offset) &&
            IsProperty(sequence.Nodes[offset], first) &&
            IsProperty(sequence.Nodes[offset + 1], second);
    }

    private static bool IsZeroOrMoreProperty(RegexSyntaxNode node, RegexUnicodePropertyKind kind)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode { Minimum: 0, Maximum: null } repetition &&
            IsProperty(repetition.Child, kind);
    }

    private static bool IsOneOrMoreProperty(RegexSyntaxNode node, RegexUnicodePropertyKind kind)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode { Minimum: 1, Maximum: null } repetition &&
            IsProperty(repetition.Child, kind);
    }

    private static bool IsProperty(RegexSyntaxNode node, RegexUnicodePropertyKind kind)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.UnicodePropertyClass } atom &&
            atom.Value.Span is [byte actual] &&
            (RegexUnicodePropertyKind)actual == kind;
    }

    private static bool IsAnyClass(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.AnyClass };
    }

    private static bool IsNotControlCrLfClass(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            TryReadClassPropertySet(
                atom.CharacterClass,
                negated: true,
                RegexUnicodePropertyKind.GraphemeClusterBreakControl,
                RegexUnicodePropertyKind.GraphemeClusterBreakCr,
                RegexUnicodePropertyKind.GraphemeClusterBreakLf);
    }

    private static bool IsZeroOrMoreClassSet(RegexSyntaxNode node, params RegexUnicodePropertyKind[] kinds)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode { Minimum: 0, Maximum: null } repetition &&
            IsClassSet(repetition.Child, kinds);
    }

    private static bool IsClassSet(RegexSyntaxNode node, params RegexUnicodePropertyKind[] kinds)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            TryReadClassPropertySet(atom.CharacterClass, negated: false, kinds);
    }

    private static bool TryReadClassPropertySet(
        RegexCharacterClass? characterClass,
        bool negated,
        params RegexUnicodePropertyKind[] kinds)
    {
        if (characterClass is null || characterClass.Negated != negated)
        {
            return false;
        }

        Span<bool> matched = kinds.Length <= 8
            ? stackalloc bool[kinds.Length]
            : new bool[kinds.Length];
        int tokenCount = 0;
        if (!TryReadClassPropertySetNode(
                characterClass.Expression,
                kinds,
                matched,
                ref tokenCount))
        {
            return false;
        }

        if (tokenCount != kinds.Length)
        {
            return false;
        }

        for (int matchedIndex = 0; matchedIndex < matched.Length; matchedIndex++)
        {
            if (!matched[matchedIndex])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadClassPropertySetNode(
        RegexClassSetNode node,
        ReadOnlySpan<RegexUnicodePropertyKind> kinds,
        Span<bool> matched,
        ref int tokenCount)
    {
        if (node.Kind == RegexClassSetKind.Union)
        {
            for (int index = 0; index < node.Items.Count; index++)
            {
                if (!TryReadClassPropertySetNode(node.Items[index], kinds, matched, ref tokenCount))
                {
                    return false;
                }
            }

            return true;
        }

        RegexUnicodeProperty? property = node.UnicodeProperty;
        if (node.Kind != RegexClassSetKind.Atom ||
            node.Negated ||
            node.AtomKind != RegexSyntaxKind.UnicodePropertyClass ||
            property is null ||
            property.Family != RegexUnicodePropertyFamily.Property)
        {
            return false;
        }

        int matchIndex = -1;
        for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
        {
            if (kinds[kindIndex] == property.PropertyKind)
            {
                matchIndex = kindIndex;
                break;
            }
        }

        if (matchIndex < 0 || matched[matchIndex])
        {
            return false;
        }

        matched[matchIndex] = true;
        tokenCount++;
        return true;
    }

    private static bool TryNormalizeRoot(RegexSyntaxNode root, out RegexSyntaxNode normalized)
    {
        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence)
        {
            normalized = root;
            return true;
        }

        RegexSyntaxNode? onlyRuntimeNode = null;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = sequence.Nodes[index];
            if (node is RegexInlineFlagsNode flags)
            {
                if (!IsParseOnlyFlags(flags.EnabledFlags) || !IsParseOnlyFlags(flags.DisabledFlags))
                {
                    normalized = root;
                    return false;
                }

                continue;
            }

            if (onlyRuntimeNode is not null)
            {
                normalized = root;
                return false;
            }

            onlyRuntimeNode = node;
        }

        normalized = onlyRuntimeNode is null
            ? root
            : UnwrapTransparentGroups(onlyRuntimeNode);
        return onlyRuntimeNode is not null;
    }

    private static bool TryGetRuntimeSequenceOffset(
        RegexSequenceNode sequence,
        int expectedNodeCount,
        out int offset)
    {
        offset = 0;
        while (offset < sequence.Nodes.Count && sequence.Nodes[offset] is RegexInlineFlagsNode flags)
        {
            if (!IsParseOnlyFlags(flags.EnabledFlags) || !IsParseOnlyFlags(flags.DisabledFlags))
            {
                return false;
            }

            offset++;
        }

        return sequence.Nodes.Count - offset == expectedNodeCount;
    }

    private static bool IsParseOnlyFlags(string flags)
    {
        for (int index = 0; index < flags.Length; index++)
        {
            if (flags[index] != 'x')
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

}
