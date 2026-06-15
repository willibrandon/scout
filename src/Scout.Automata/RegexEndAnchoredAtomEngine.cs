namespace Scout;

internal sealed class RegexEndAnchoredAtomEngine
{
    private readonly RegexSyntaxKind kind;
    private readonly byte[] value;
    private readonly RegexCompileOptions options;

    private RegexEndAnchoredAtomEngine(RegexSyntaxKind kind, byte[] value, RegexCompileOptions options)
    {
        this.kind = kind;
        this.value = value;
        this.options = options;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexEndAnchoredAtomEngine? engine)
    {
        engine = null;
        if (options.MultiLine)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            UnwrapTransparentGroups(sequence.Nodes[0]) is not RegexAtomNode atom ||
            !IsEndAnchor(sequence.Nodes[1]) ||
            IsPredicate(atom.Kind))
        {
            return false;
        }

        engine = new RegexEndAnchoredAtomEngine(atom.Kind, atom.Value.ToArray(), options);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryFindEndingAtHaystackEnd(haystack, out RegexMatch match) &&
            match.Start >= Math.Clamp(startAt, 0, haystack.Length)
            ? match
            : null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryFindEndingAtHaystackEnd(haystack, out RegexMatch match) &&
            match.Start == start
            ? match
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Find(haystack, startAt).HasValue ? 1 : 0;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Find(haystack, startAt) is RegexMatch match ? match.Length : 0;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        if (TryFindEndingAtHaystackEnd(haystack, out RegexMatch match) && match.Start == start)
        {
            length = match.Length;
            return true;
        }

        length = 0;
        return false;
    }

    private bool TryFindEndingAtHaystackEnd(ReadOnlySpan<byte> haystack, out RegexMatch match)
    {
        int end = haystack.Length;
        bool scalarMode = options.Utf8 ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                kind,
                value,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses);
        int firstStart = scalarMode ? Math.Max(0, end - 4) : Math.Max(0, end - 1);
        for (int start = end - 1; start >= firstStart; start--)
        {
            if (RegexByteClass.TryGetAtomMatchLength(
                    haystack,
                    start,
                    kind,
                    value,
                    options.CaseInsensitive,
                    options.MultiLine,
                    options.DotMatchesNewline,
                    options.Crlf,
                    options.LineTerminator,
                    options.Utf8,
                    options.UnicodeClasses,
                    out int length) &&
                start + length == end)
            {
                match = new RegexMatch(start, length);
                return true;
            }
        }

        match = default;
        return false;
    }

    private static bool IsEndAnchor(RegexSyntaxNode node)
    {
        return UnwrapTransparentGroups(node) is RegexAtomNode
        {
            Kind: RegexSyntaxKind.EndAnchor or RegexSyntaxKind.AbsoluteEndAnchor,
        };
    }

    private static bool IsPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
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
