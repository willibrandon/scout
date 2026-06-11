namespace Scout;

internal sealed class RegexLiteralSetEngine
{
    private const int MinimumLiteralCount = 16;

    private readonly AhoCorasickAutomaton automaton;
    private readonly byte[][] literals;
    private readonly bool asciiCaseInsensitive;
    private readonly int maxLiteralLength;

    private RegexLiteralSetEngine(IReadOnlyList<byte[]> literals, bool asciiCaseInsensitive)
    {
        this.literals = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            this.literals[index] = literals[index].ToArray();
            maxLiteralLength = Math.Max(maxLiteralLength, this.literals[index].Length);
        }

        this.asciiCaseInsensitive = asciiCaseInsensitive;
        automaton = AhoCorasickAutomaton
            .Builder()
            .WithMatchKind(AhoCorasickMatchKind.Standard)
            .WithStartKind(AhoCorasickStartKind.Unanchored)
            .WithAsciiCaseInsensitive(asciiCaseInsensitive)
            .Build(literals);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexLiteralSetEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive && options.UnicodeClasses)
        {
            return false;
        }

        var literals = new List<byte[]>();
        bool? asciiCaseInsensitive = null;
        if (!TryCollectLiteralBranches(root, options, literals, ref asciiCaseInsensitive) ||
            literals.Count < MinimumLiteralCount)
        {
            return false;
        }

        engine = new RegexLiteralSetEngine(literals, asciiCaseInsensitive == true);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(haystack[startOffset..]);
        AhoCorasickMatch? best = null;
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            if (best.HasValue && match.End > best.Value.Start + maxLiteralLength)
            {
                break;
            }

            if (!IsBetter(match, best))
            {
                continue;
            }

            best = match;
        }

        return best.HasValue
            ? new RegexMatch(startOffset + best.Value.Start, best.Value.Length)
            : null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        for (int index = 0; index < literals.Length; index++)
        {
            byte[] literal = literals[index];
            if (literal.Length <= haystack.Length - startOffset &&
                LiteralEquals(haystack.Slice(startOffset, literal.Length), literal, asciiCaseInsensitive))
            {
                return new RegexMatch(startOffset, literal.Length);
            }
        }

        return null;
    }

    private static bool TryCollectLiteralBranches(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte[]> literals,
        ref bool? asciiCaseInsensitive)
    {
        node = UnwrapTransparentGroups(node);
        if (node is RegexAlternationNode alternation)
        {
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryCollectLiteralBranches(alternation.Alternatives[index], options, literals, ref asciiCaseInsensitive))
                {
                    return false;
                }
            }

            return true;
        }

        var literal = new List<byte>();
        return TryAppendLiteral(node, options, literal, ref asciiCaseInsensitive) &&
            literal.Count > 0 &&
            AddLiteral(literals, literal.ToArray());
    }

    private static bool TryAppendLiteral(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte> literal,
        ref bool? asciiCaseInsensitive)
    {
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                return true;
            case RegexSyntaxKind.Literal:
                if (!SetCaseMode(options, ref asciiCaseInsensitive))
                {
                    return false;
                }

                literal.AddRange(((RegexAtomNode)node).Value.ToArray());
                return true;
            case RegexSyntaxKind.Sequence:
                return TryAppendLiteralSequence((RegexSequenceNode)node, options, literal, ref asciiCaseInsensitive);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAppendLiteral(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    literal,
                    ref asciiCaseInsensitive);
            case RegexSyntaxKind.Repetition:
                return TryAppendLiteralRepetition((RegexRepetitionNode)node, options, literal, ref asciiCaseInsensitive);
            default:
                return false;
        }
    }

    private static bool TryAppendLiteralSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<byte> literal,
        ref bool? asciiCaseInsensitive)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryAppendLiteral(child, currentOptions, literal, ref asciiCaseInsensitive))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendLiteralRepetition(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<byte> literal,
        ref bool? asciiCaseInsensitive)
    {
        if (node.Maximum != node.Minimum)
        {
            return false;
        }

        for (int count = 0; count < node.Minimum; count++)
        {
            if (!TryAppendLiteral(node.Child, options, literal, ref asciiCaseInsensitive))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SetCaseMode(RegexCompileOptions options, ref bool? asciiCaseInsensitive)
    {
        if (options.CaseInsensitive && options.UnicodeClasses)
        {
            return false;
        }

        if (asciiCaseInsensitive.HasValue && asciiCaseInsensitive.Value != options.CaseInsensitive)
        {
            return false;
        }

        asciiCaseInsensitive = options.CaseInsensitive;
        return true;
    }

    private static bool AddLiteral(List<byte[]> literals, byte[] literal)
    {
        if (literal.Length == 0)
        {
            return false;
        }

        literals.Add(literal);
        return true;
    }

    private static bool IsBetter(AhoCorasickMatch candidate, AhoCorasickMatch? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        AhoCorasickMatch current = best.Value;
        if (candidate.Start != current.Start)
        {
            return candidate.Start < current.Start;
        }

        return candidate.PatternId < current.PatternId;
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

    private static bool LiteralEquals(ReadOnlySpan<byte> haystack, byte[] literal, bool asciiCaseInsensitive)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            byte left = haystack[index];
            byte right = literal[index];
            if (asciiCaseInsensitive)
            {
                left = FoldAscii(left);
                right = FoldAscii(right);
            }

            if (left != right)
            {
                return false;
            }
        }

        return true;
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }
}
