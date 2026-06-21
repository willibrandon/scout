namespace Scout;

internal sealed class RegexAnchoredDotStarCaptureEngine
{
    private readonly int[] wholeSpanCaptureIndexes;
    private readonly int[] endCaptureIndexes;
    private readonly int captureCount;

    private RegexAnchoredDotStarCaptureEngine(
        int[] wholeSpanCaptureIndexes,
        int[] endCaptureIndexes,
        int captureCount)
    {
        this.wholeSpanCaptureIndexes = wholeSpanCaptureIndexes;
        this.endCaptureIndexes = endCaptureIndexes;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexAnchoredDotStarCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 || options.Utf8)
        {
            return false;
        }

        var wholeSpanCaptures = new List<int>();
        var endCaptures = new List<int>();
        if (!TryAnalyzeRoot(root, options, wholeSpanCaptures, endCaptures) ||
            wholeSpanCaptures.Count == 0)
        {
            return false;
        }

        engine = new RegexAnchoredDotStarCaptureEngine(
            wholeSpanCaptures.ToArray(),
            endCaptures.ToArray(),
            captureCount);
        return true;
    }

    public RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (Math.Clamp(startAt, 0, haystack.Length) != 0)
        {
            return null;
        }

        var match = new RegexMatch(0, haystack.Length);
        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;

        for (int index = 0; index < wholeSpanCaptureIndexes.Length; index++)
        {
            groups[wholeSpanCaptureIndexes[index]] = match;
        }

        var endCapture = new RegexMatch(haystack.Length, 0);
        for (int index = 0; index < endCaptureIndexes.Length; index++)
        {
            groups[endCaptureIndexes[index]] = endCapture;
        }

        return new RegexCaptures(match, groups);
    }

    private static bool TryAnalyzeRoot(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        List<int> wholeSpanCaptures,
        List<int> endCaptures)
    {
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> nodes = GetEffectiveNodes(root, options);
        if (nodes.Count != 2 ||
            nodes[0].Options.MultiLine ||
            !IsAtom(nodes[0].Node, RegexSyntaxKind.StartAnchor) ||
            !TryGetCapture(nodes[1].Node, nodes[1].Options, out RegexGroupNode outerCapture, out RegexCompileOptions captureOptions) ||
            !TryAnalyzeOuterCapture(outerCapture, captureOptions, wholeSpanCaptures, endCaptures))
        {
            return false;
        }

        return true;
    }

    private static bool TryAnalyzeOuterCapture(
        RegexGroupNode outerCapture,
        RegexCompileOptions options,
        List<int> wholeSpanCaptures,
        List<int> endCaptures)
    {
        wholeSpanCaptures.Add(outerCapture.CaptureIndex);
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> nodes = GetEffectiveNodes(outerCapture.Child, options);

        bool sawDotStar = false;
        for (int index = 0; index < nodes.Count; index++)
        {
            RegexSyntaxNode node = nodes[index].Node;
            RegexCompileOptions nodeOptions = nodes[index].Options;
            if (!sawDotStar)
            {
                if (!TryGetDotStarCapture(node, nodeOptions, out int dotStarCapture))
                {
                    return false;
                }

                wholeSpanCaptures.Add(dotStarCapture);
                sawDotStar = true;
                continue;
            }

            if (TryGetEmptyCapture(node, nodeOptions, out int emptyCapture) ||
                TryGetEndAnchorCapture(node, nodeOptions, out emptyCapture))
            {
                endCaptures.Add(emptyCapture);
                continue;
            }

            return false;
        }

        return sawDotStar && endCaptures.Count > 0;
    }

    private static bool TryGetDotStarCapture(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int captureIndex)
    {
        captureIndex = 0;
        if (!TryGetCapture(node, options, out RegexGroupNode group, out RegexCompileOptions groupOptions) ||
            groupOptions.Utf8 ||
            groupOptions.SwapGreed ||
            !groupOptions.DotMatchesNewline ||
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
        return true;
    }

    private static bool TryGetEmptyCapture(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int captureIndex)
    {
        captureIndex = 0;
        if (!TryGetCapture(node, options, out RegexGroupNode group, out RegexCompileOptions groupOptions) ||
            UnwrapScopedNonCapturingGroups(group.Child, ref groupOptions).Kind != RegexSyntaxKind.Empty)
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool TryGetEndAnchorCapture(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int captureIndex)
    {
        captureIndex = 0;
        if (!TryGetCapture(node, options, out RegexGroupNode group, out RegexCompileOptions groupOptions) ||
            groupOptions.MultiLine ||
            !IsEndAnchor(UnwrapScopedNonCapturingGroups(group.Child, ref groupOptions)))
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        return true;
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

    private static bool IsAtom(RegexSyntaxNode node, RegexSyntaxKind kind)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode atom && atom.Kind == kind;
    }

    private static bool IsEndAnchor(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        return node is RegexAtomNode
        {
            Kind: RegexSyntaxKind.EndAnchor or RegexSyntaxKind.AbsoluteEndAnchor,
        };
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

}
