namespace Scout;

internal sealed class RegexAnchoredQuotedStringCaptureEngine
{
    private readonly bool[] prefixBytes;
    private readonly bool[] quoteBytes;
    private readonly RegexCompileOptions dotOptions;
    private readonly int rawCaptureIndex;
    private readonly int captureCount;

    private RegexAnchoredQuotedStringCaptureEngine(
        bool[] prefixBytes,
        bool[] quoteBytes,
        RegexCompileOptions dotOptions,
        int rawCaptureIndex,
        int captureCount)
    {
        this.prefixBytes = prefixBytes;
        this.quoteBytes = quoteBytes;
        this.dotOptions = dotOptions;
        this.rawCaptureIndex = rawCaptureIndex;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexAnchoredQuotedStringCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 || options.Utf8 || options.MultiLine || options.DotMatchesNewline)
        {
            return false;
        }

        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> nodes = GetEffectiveNodes(root, options);
        if (nodes.Count != 6 ||
            !IsAtom(nodes[0].Node, RegexSyntaxKind.StartAnchor) ||
            !TryCreateByteLookup(nodes[1].Node, nodes[1].Options, out bool[] prefixBytes) ||
            !TryCreateByteLookup(nodes[2].Node, nodes[2].Options, out bool[] quoteBytes) ||
            !TryGetDotStarCapture(nodes[3].Node, nodes[3].Options, out int rawCaptureIndex, out RegexCompileOptions dotOptions) ||
            !TryCreateByteLookup(nodes[4].Node, nodes[4].Options, out bool[] trailingQuoteBytes) ||
            !SameLookup(quoteBytes, trailingQuoteBytes) ||
            !IsAtom(nodes[5].Node, RegexSyntaxKind.EndAnchor))
        {
            return false;
        }

        engine = new RegexAnchoredQuotedStringCaptureEngine(prefixBytes, quoteBytes, dotOptions, rawCaptureIndex, captureCount);
        return true;
    }

    public RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (startAt != 0 || haystack.Length < 2)
        {
            return null;
        }

        int position = 0;
        while (position < haystack.Length && prefixBytes[haystack[position]])
        {
            position++;
        }

        if (position >= haystack.Length - 1 ||
            !quoteBytes[haystack[position]] ||
            !quoteBytes[haystack[^1]])
        {
            return null;
        }

        int rawStart = position + 1;
        int rawEnd = haystack.Length - 1;
        if (!dotOptions.DotMatchesNewline && ContainsLineTerminator(haystack, rawStart, rawEnd, dotOptions))
        {
            return null;
        }

        var match = new RegexMatch(0, haystack.Length);
        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;
        groups[rawCaptureIndex] = new RegexMatch(rawStart, rawEnd - rawStart);
        return new RegexCaptures(match, groups);
    }

    private static bool TryGetDotStarCapture(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int captureIndex,
        out RegexCompileOptions captureOptions)
    {
        captureIndex = 0;
        captureOptions = default;
        if (!TryGetCapture(node, options, out RegexGroupNode group, out RegexCompileOptions groupOptions) ||
            groupOptions.Utf8 ||
            groupOptions.SwapGreed ||
            groupOptions.DotMatchesNewline ||
            UnwrapScopedNonCapturingGroups(group.Child, ref groupOptions) is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
                Lazy: false,
            } repetition ||
            !IsAtom(repetition.Child, RegexSyntaxKind.Dot))
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        captureOptions = groupOptions;
        return true;
    }

    private static bool TryCreateByteLookup(RegexSyntaxNode node, RegexCompileOptions options, out bool[] lookup)
    {
        lookup = [];
        node = UnwrapScopedNonCapturingGroups(node, ref options);
        if (node is RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: null,
                Lazy: false,
            } repetition)
        {
            node = UnwrapScopedNonCapturingGroups(repetition.Child, ref options);
        }

        if (node is not RegexAtomNode atom ||
            RegexByteClass.RequiresUtf8ScalarMatch(atom.Kind, atom.Value.Span, options.Utf8, options.CaseInsensitive, options.UnicodeClasses))
        {
            return false;
        }

        bool[] result = new bool[256];
        bool any = false;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator))
            {
                result[value] = true;
                any = true;
            }
        }

        lookup = result;
        return any;
    }

    private static bool ContainsLineTerminator(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        RegexCompileOptions options)
    {
        for (int index = start; index < end; index++)
        {
            byte value = haystack[index];
            if (options.Crlf
                    ? value is (byte)'\n' or (byte)'\r'
                    : value == options.LineTerminator)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCapture(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexGroupNode group,
        out RegexCompileOptions groupOptions)
    {
        node = UnwrapScopedNonCapturingGroups(node, ref options);
        if (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
            } capture)
        {
            group = capture;
            groupOptions = options.Apply(capture.EnabledFlags, capture.DisabledFlags);
            return true;
        }

        group = null!;
        groupOptions = default;
        return false;
    }

    private static List<(RegexSyntaxNode Node, RegexCompileOptions Options)> GetEffectiveNodes(
        RegexSyntaxNode node,
        RegexCompileOptions options)
    {
        node = UnwrapScopedNonCapturingGroups(node, ref options);
        var nodes = new List<(RegexSyntaxNode Node, RegexCompileOptions Options)>();
        if (node is not RegexSequenceNode sequence)
        {
            nodes.Add((node, options));
            return nodes;
        }

        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            nodes.Add((child, currentOptions));
        }

        return nodes;
    }

    private static RegexSyntaxNode UnwrapScopedNonCapturingGroups(RegexSyntaxNode node, ref RegexCompileOptions options)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        return node;
    }

    private static bool IsAtom(RegexSyntaxNode node, RegexSyntaxKind kind)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode atom && atom.Kind == kind;
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.NonCapturingGroup,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group)
        {
            node = group.Child;
        }

        return node;
    }

    private static bool SameLookup(bool[] left, bool[] right)
    {
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }
}
