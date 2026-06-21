namespace Scout;

internal sealed class RegexAnchoredLineLiteralGapEngine
{
    private const int MaxTailParts = 16;

    private readonly bool[]? leadingBytes;
    private readonly byte[] prefix;
    private readonly RegexAnchoredLineLiteralGapPart[] tailParts;
    private readonly byte[] tailAnchor;
    private readonly MemmemFinder tailAnchorFinder;
    private readonly byte lineTerminator;

    private RegexAnchoredLineLiteralGapEngine(
        bool[]? leadingBytes,
        byte[] prefix,
        RegexAnchoredLineLiteralGapPart[] tailParts,
        byte lineTerminator)
    {
        this.leadingBytes = leadingBytes;
        this.prefix = prefix;
        this.tailParts = tailParts;
        tailAnchor = tailParts[0].Literal!;
        tailAnchorFinder = new MemmemFinder(tailAnchor);
        this.lineTerminator = lineTerminator;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexAnchoredLineLiteralGapEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.MultiLine ||
            options.DotMatchesNewline ||
            options.Crlf ||
            options.Utf8 ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: >= 5 } sequence ||
            !IsStartAnchor(sequence.Nodes[0]))
        {
            return false;
        }

        int index = 1;
        bool[]? leadingBytes = null;
        if (TryGetUnboundedByteRepetition(sequence.Nodes[index], options, out bool[]? candidateLeadingBytes, out bool leadingLazy) &&
            candidateLeadingBytes is not null)
        {
            if (leadingLazy || candidateLeadingBytes[options.LineTerminator])
            {
                return false;
            }

            leadingBytes = candidateLeadingBytes;
            index++;
        }

        if (index >= sequence.Nodes.Count ||
            !TryGetAsciiLiteral(sequence.Nodes[index], out byte[] prefix) ||
            prefix.Length == 0 ||
            ContainsByte(prefix, options.LineTerminator) ||
            leadingBytes is not null && leadingBytes[prefix[0]])
        {
            return false;
        }

        index++;
        if (index >= sequence.Nodes.Count ||
            !IsLazyDotStar(sequence.Nodes[index]))
        {
            return false;
        }

        index++;
        if (!TryGetTailParts(sequence.Nodes, index, options, out RegexAnchoredLineLiteralGapPart[] tailParts) ||
            tailParts.Length == 0 ||
            tailParts[0].Kind != RegexAnchoredLineLiteralGapPartKind.Literal)
        {
            return false;
        }

        engine = new RegexAnchoredLineLiteralGapEngine(leadingBytes, prefix, tailParts, options.LineTerminator);
        return true;
    }

    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return TryMatchAtAbsoluteStart(haystack, out _);
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (Math.Clamp(startAt, 0, haystack.Length) != 0)
        {
            return null;
        }

        return TryMatchAtAbsoluteStart(haystack, out int length)
            ? new RegexMatch(0, length)
            : null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (Math.Clamp(startAt, 0, haystack.Length) != 0)
        {
            return null;
        }

        return TryMatchAtAbsoluteStart(haystack, out int length)
            ? new RegexMatch(0, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Math.Clamp(startAt, 0, haystack.Length) == 0 && IsMatch(haystack) ? 1 : 0;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Math.Clamp(startAt, 0, haystack.Length) == 0 && TryMatchAtAbsoluteStart(haystack, out int length)
            ? length
            : 0;
    }

    public long CountMatchingLines(ReadOnlySpan<byte> haystack)
    {
        long total = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            int lineEnd = FindLineEnd(haystack, lineStart);
            int contentEnd = lineEnd;
            if (contentEnd > lineStart && haystack[contentEnd - 1] == (byte)'\r')
            {
                contentEnd--;
            }

            if (TryMatchAtLineStart(haystack, lineStart, contentEnd, out _))
            {
                total++;
            }

            if (lineEnd >= haystack.Length)
            {
                return total;
            }

            lineStart = lineEnd + 1;
        }

        return total;
    }

    private bool TryMatchAtAbsoluteStart(ReadOnlySpan<byte> haystack, out int length)
    {
        return TryMatchAtLineStart(haystack, start: 0, FindLineEnd(haystack, start: 0), out length);
    }

    private bool TryMatchAtLineStart(ReadOnlySpan<byte> haystack, int start, int lineEnd, out int length)
    {
        length = 0;
        int position = start;
        if (leadingBytes is not null)
        {
            while (position < lineEnd && leadingBytes[haystack[position]])
            {
                position++;
            }
        }

        if (prefix.Length > lineEnd - position ||
            !haystack.Slice(position, prefix.Length).SequenceEqual(prefix))
        {
            return false;
        }

        position += prefix.Length;
        int lastAnchorStart = lineEnd - tailAnchor.Length;
        int searchAt = position;
        while (searchAt <= lastAnchorStart)
        {
            int offset = tailAnchorFinder.Find(haystack[searchAt..lineEnd]);
            if (offset < 0)
            {
                return false;
            }

            int candidate = searchAt + offset;
            if (TryMatchTail(haystack, partIndex: 0, candidate, lineEnd, out int end))
            {
                length = end - start;
                return true;
            }

            searchAt = candidate + 1;
        }

        return false;
    }

    private bool TryMatchTail(
        ReadOnlySpan<byte> haystack,
        int partIndex,
        int position,
        int lineEnd,
        out int end)
    {
        end = 0;
        if (partIndex >= tailParts.Length)
        {
            end = position;
            return true;
        }

        RegexAnchoredLineLiteralGapPart part = tailParts[partIndex];
        switch (part.Kind)
        {
            case RegexAnchoredLineLiteralGapPartKind.Literal:
                byte[] literal = part.Literal!;
                if (literal.Length > lineEnd - position ||
                    !haystack.Slice(position, literal.Length).SequenceEqual(literal))
                {
                    return false;
                }

                return TryMatchTail(haystack, partIndex + 1, position + literal.Length, lineEnd, out end);

            case RegexAnchoredLineLiteralGapPartKind.ByteSet:
                if (position >= lineEnd || !part.Bytes![haystack[position]])
                {
                    return false;
                }

                return TryMatchTail(haystack, partIndex + 1, position + 1, lineEnd, out end);

            case RegexAnchoredLineLiteralGapPartKind.OptionalByteSet:
                if (part.Lazy)
                {
                    if (TryMatchTail(haystack, partIndex + 1, position, lineEnd, out end))
                    {
                        return true;
                    }

                    return position < lineEnd &&
                        part.Bytes![haystack[position]] &&
                        TryMatchTail(haystack, partIndex + 1, position + 1, lineEnd, out end);
                }

                if (position < lineEnd &&
                    part.Bytes![haystack[position]] &&
                    TryMatchTail(haystack, partIndex + 1, position + 1, lineEnd, out end))
                {
                    return true;
                }

                return TryMatchTail(haystack, partIndex + 1, position, lineEnd, out end);

            case RegexAnchoredLineLiteralGapPartKind.StarByteSet:
                int limit = position;
                while (limit < lineEnd && part.Bytes![haystack[limit]])
                {
                    limit++;
                }

                if (part.Lazy)
                {
                    for (int candidate = position; candidate <= limit; candidate++)
                    {
                        if (TryMatchTail(haystack, partIndex + 1, candidate, lineEnd, out end))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    for (int candidate = limit; candidate >= position; candidate--)
                    {
                        if (TryMatchTail(haystack, partIndex + 1, candidate, lineEnd, out end))
                        {
                            return true;
                        }
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private int FindLineEnd(ReadOnlySpan<byte> haystack, int start)
    {
        int offset = haystack[start..].IndexOf(lineTerminator);
        return offset < 0 ? haystack.Length : start + offset;
    }

    private static bool TryGetTailParts(
        IReadOnlyList<RegexSyntaxNode> nodes,
        int startIndex,
        RegexCompileOptions options,
        out RegexAnchoredLineLiteralGapPart[] parts)
    {
        parts = [];
        if (startIndex >= nodes.Count || nodes.Count - startIndex > MaxTailParts)
        {
            return false;
        }

        var collected = new List<RegexAnchoredLineLiteralGapPart>();
        for (int index = startIndex; index < nodes.Count; index++)
        {
            RegexSyntaxNode node = UnwrapTransparentGroups(nodes[index]);
            if (TryGetAsciiLiteral(node, out byte[] literal))
            {
                if (ContainsByte(literal, options.LineTerminator))
                {
                    return false;
                }

                collected.Add(RegexAnchoredLineLiteralGapPart.CreateLiteral(literal));
                continue;
            }

            if (TryGetByteSet(node, options, out bool[]? bytes) &&
                bytes is not null)
            {
                if (bytes[options.LineTerminator])
                {
                    return false;
                }

                collected.Add(RegexAnchoredLineLiteralGapPart.CreateByteSet(bytes));
                continue;
            }

            if (node is RegexRepetitionNode
                {
                    Minimum: 0,
                    Maximum: null,
                } star &&
                TryGetByteSet(star.Child, options, out bytes) &&
                bytes is not null)
            {
                if (bytes[options.LineTerminator])
                {
                    return false;
                }

                collected.Add(RegexAnchoredLineLiteralGapPart.CreateStarByteSet(bytes, star.Lazy));
                continue;
            }

            if (node is RegexRepetitionNode
                {
                    Minimum: 0,
                    Maximum: 1,
                } optional &&
                TryGetByteSet(optional.Child, options, out bytes) &&
                bytes is not null)
            {
                if (bytes[options.LineTerminator])
                {
                    return false;
                }

                collected.Add(RegexAnchoredLineLiteralGapPart.CreateOptionalByteSet(bytes, optional.Lazy));
                continue;
            }

            return false;
        }

        parts = collected.ToArray();
        return parts.Length > 0;
    }

    private static bool TryGetUnboundedByteRepetition(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out bool[]? bytes,
        out bool lazy)
    {
        bytes = null;
        lazy = false;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
            } repetition ||
            !TryGetByteSet(repetition.Child, options, out bytes))
        {
            return false;
        }

        lazy = repetition.Lazy;
        return true;
    }

    private static bool TryGetByteSet(RegexSyntaxNode node, RegexCompileOptions options, out bool[]? bytes)
    {
        bytes = null;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom ||
            atom.Kind is not (RegexSyntaxKind.Literal
                or RegexSyntaxKind.Dot
                or RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.CharacterClass
                or RegexSyntaxKind.ByteClass
                or RegexSyntaxKind.DigitClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.WordClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.WhitespaceClass
                or RegexSyntaxKind.NotWhitespaceClass) ||
            atom.Kind == RegexSyntaxKind.Literal && atom.Value.Length != 1 ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        bytes = new bool[256];
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bytes[value] = RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
        }

        return true;
    }

    private static bool TryGetAsciiLiteral(RegexSyntaxNode node, out byte[] literal)
    {
        literal = [];
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = atom.Value.Span;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 0x7F)
            {
                return false;
            }
        }

        literal = bytes.ToArray();
        return true;
    }

    private static bool IsStartAnchor(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode { Kind: RegexSyntaxKind.StartAnchor or RegexSyntaxKind.AbsoluteStartAnchor };
    }

    private static bool IsLazyDotStar(RegexSyntaxNode node)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexRepetitionNode
        {
            Child.Kind: RegexSyntaxKind.Dot,
            Minimum: 0,
            Maximum: null,
            Lazy: true,
        };
    }

    private static bool ContainsByte(byte[] bytes, byte value)
    {
        return bytes.AsSpan().IndexOf(value) >= 0;
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
