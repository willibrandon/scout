namespace Scout;

internal sealed class RegexStartPredicate
{
    private const int MaxPredicateLength = 64;

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
            predicate = null;
            return false;
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
