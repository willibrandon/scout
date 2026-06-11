namespace Scout;

internal sealed class RegexUnanchoredLazyDfa
{
    private readonly RegexLazyDfa forward;
    private readonly RegexLazyDfa reverse;

    private RegexUnanchoredLazyDfa(RegexLazyDfa forward, RegexLazyDfa reverse)
    {
        this.forward = forward;
        this.reverse = reverse;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ulong dfaSizeLimit,
        out RegexUnanchoredLazyDfa? dfa)
    {
        dfa = null;
        if (options.Utf8 || CanMatchEmpty(root) || HasNullableRepetition(root) || HasMultiByteLiteral(root))
        {
            return false;
        }

        RegexNfa forwardNfa = RegexNfaCompiler.CompileUnanchored(root, options);
        if (!RegexDfaOperations.CanCompile(forwardNfa) ||
            !RegexLazyDfa.TryCreate(forwardNfa, dfaSizeLimit, leftmostPrune: true, out RegexLazyDfa? forwardDfa))
        {
            return false;
        }

        RegexNfa reverseNfa = RegexNfaCompiler.CompileReversed(root, options);
        if (!RegexDfaOperations.CanCompile(reverseNfa) ||
            !RegexLazyDfa.TryCreate(reverseNfa, dfaSizeLimit, leftmostPrune: true, out RegexLazyDfa? reverseDfa))
        {
            return false;
        }

        dfa = new RegexUnanchoredLazyDfa(forwardDfa!, reverseDfa!);
        return true;
    }

    public bool TryFind(ReadOnlySpan<byte> haystack, int startAt, out RegexMatch match, out bool gaveUp)
    {
        return TryFind(
            haystack,
            startAt,
            forwardReachabilityCache: null,
            reverseReachabilityCache: null,
            out match,
            out gaveUp);
    }

    public bool TryFind(
        ReadOnlySpan<byte> haystack,
        int startAt,
        Dictionary<(int State, int Position), bool>? forwardReachabilityCache,
        Dictionary<(int State, int Position), bool>? reverseReachabilityCache,
        out RegexMatch match,
        out bool gaveUp)
    {
        gaveUp = false;
        if (!forward.TryFindEnd(haystack, startAt, forwardReachabilityCache, out int end))
        {
            match = default;
            return false;
        }

        if (!reverse.TryFindStartReverse(haystack, startAt, end, reverseReachabilityCache, out int start))
        {
            gaveUp = true;
            match = default;
            return false;
        }

        match = new RegexMatch(start, end - start);
        return true;
    }

    public bool TryCountMatches(ReadOnlySpan<byte> haystack, int startAt, out long count)
    {
        return TryIterateNonOverlapping(haystack, startAt, sumSpans: false, out count);
    }

    public bool TrySumMatchSpans(ReadOnlySpan<byte> haystack, int startAt, out long spanSum)
    {
        return TryIterateNonOverlapping(haystack, startAt, sumSpans: true, out spanSum);
    }

    private bool TryIterateNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
    {
        total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        Dictionary<(int State, int Position), bool> forwardReachabilityCache = [];
        Dictionary<(int State, int Position), bool> reverseReachabilityCache = [];
        while (offset <= haystack.Length)
        {
            if (!TryFind(
                    haystack,
                    offset,
                    forwardReachabilityCache,
                    reverseReachabilityCache,
                    out RegexMatch match,
                    out bool gaveUp))
            {
                return !gaveUp;
            }

            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return true;
    }

    private static bool CanMatchEmpty(RegexSyntaxNode node)
    {
        return node.Kind switch
        {
            RegexSyntaxKind.Empty => true,
            RegexSyntaxKind.InlineFlags => true,
            RegexSyntaxKind.Sequence => CanSequenceMatchEmpty((RegexSequenceNode)node),
            RegexSyntaxKind.Alternation => CanAlternationMatchEmpty((RegexAlternationNode)node),
            RegexSyntaxKind.CapturingGroup or RegexSyntaxKind.NonCapturingGroup => CanMatchEmpty(((RegexGroupNode)node).Child),
            RegexSyntaxKind.Repetition => ((RegexRepetitionNode)node).Minimum == 0 || CanMatchEmpty(((RegexRepetitionNode)node).Child),
            RegexSyntaxKind.StartAnchor
                or RegexSyntaxKind.EndAnchor
                or RegexSyntaxKind.AbsoluteStartAnchor
                or RegexSyntaxKind.AbsoluteEndAnchor
                or RegexSyntaxKind.WordBoundary
                or RegexSyntaxKind.NotWordBoundary
                or RegexSyntaxKind.WordStartBoundary
                or RegexSyntaxKind.WordEndBoundary
                or RegexSyntaxKind.WordStartHalfBoundary
                or RegexSyntaxKind.WordEndHalfBoundary => true,
            _ => false,
        };
    }

    private static bool CanSequenceMatchEmpty(RegexSequenceNode node)
    {
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            if (!CanMatchEmpty(node.Nodes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanAlternationMatchEmpty(RegexAlternationNode node)
    {
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (CanMatchEmpty(node.Alternatives[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMultiByteLiteral(RegexSyntaxNode node)
    {
        return node.Kind switch
        {
            RegexSyntaxKind.Literal => ((RegexAtomNode)node).Value.Length > 1,
            RegexSyntaxKind.Sequence => HasMultiByteLiteral((RegexSequenceNode)node),
            RegexSyntaxKind.Alternation => HasMultiByteLiteral((RegexAlternationNode)node),
            RegexSyntaxKind.CapturingGroup or RegexSyntaxKind.NonCapturingGroup => HasMultiByteLiteral(((RegexGroupNode)node).Child),
            RegexSyntaxKind.Repetition => HasMultiByteLiteral(((RegexRepetitionNode)node).Child),
            _ => false,
        };
    }

    private static bool HasNullableRepetition(RegexSyntaxNode node)
    {
        return node.Kind switch
        {
            RegexSyntaxKind.Sequence => HasNullableRepetition((RegexSequenceNode)node),
            RegexSyntaxKind.Alternation => HasNullableRepetition((RegexAlternationNode)node),
            RegexSyntaxKind.CapturingGroup or RegexSyntaxKind.NonCapturingGroup => HasNullableRepetition(((RegexGroupNode)node).Child),
            RegexSyntaxKind.Repetition => CanMatchEmpty(((RegexRepetitionNode)node).Child) ||
                HasNullableRepetition(((RegexRepetitionNode)node).Child),
            _ => false,
        };
    }

    private static bool HasNullableRepetition(RegexSequenceNode node)
    {
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            if (HasNullableRepetition(node.Nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNullableRepetition(RegexAlternationNode node)
    {
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (HasNullableRepetition(node.Alternatives[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMultiByteLiteral(RegexSequenceNode node)
    {
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            if (HasMultiByteLiteral(node.Nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMultiByteLiteral(RegexAlternationNode node)
    {
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (HasMultiByteLiteral(node.Alternatives[index]))
            {
                return true;
            }
        }

        return false;
    }
}
