
namespace Scout;

internal static class RegexSyntaxAnalysis
{
    public static bool CanMatchLineFeed(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (CanMatchLineFeed(RegexSyntaxParser.Parse(patterns[index]).Root))
            {
                return true;
            }
        }

        return false;
    }

    public static bool CanMatchAnchorLineBoundary(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (CanMatchAnchorLineBoundary(RegexSyntaxParser.Parse(patterns[index]).Root))
            {
                return true;
            }
        }

        return false;
    }

    public static bool RequiresWholeHaystack(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (RequiresWholeHaystack(RegexSyntaxParser.Parse(patterns[index]).Root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanMatchLineFeed(RegexSyntaxNode node)
    {
        return node switch
        {
            RegexInlineFlagsNode flags => flags.EnabledFlags.Contains('s', StringComparison.Ordinal),
            RegexGroupNode group => group.EnabledFlags.Contains('s', StringComparison.Ordinal) || CanMatchLineFeed(group.Child),
            RegexSequenceNode sequence => AnyCanMatchLineFeed(sequence.Nodes),
            RegexAlternationNode alternation => AnyCanMatchLineFeed(alternation.Alternatives),
            RegexRepetitionNode repetition => CanMatchLineFeed(repetition.Child),
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.CharacterClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.WhitespaceClass,
            _ => false,
        };
    }

    private static bool CanMatchAnchorLineBoundary(RegexSyntaxNode node)
    {
        return node switch
        {
            RegexGroupNode group => CanMatchAnchorLineBoundary(group.Child),
            RegexSequenceNode sequence => AnyCanMatchAnchorLineBoundary(sequence.Nodes),
            RegexAlternationNode alternation => AnyCanMatchAnchorLineBoundary(alternation.Alternatives),
            RegexRepetitionNode repetition => CanMatchAnchorLineBoundary(repetition.Child),
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.StartAnchor or RegexSyntaxKind.EndAnchor,
            _ => false,
        };
    }

    private static bool RequiresWholeHaystack(RegexSyntaxNode node)
    {
        return node switch
        {
            RegexGroupNode group => RequiresWholeHaystack(group.Child),
            RegexSequenceNode sequence => AnyRequiresWholeHaystack(sequence.Nodes),
            RegexAlternationNode alternation => AnyRequiresWholeHaystack(alternation.Alternatives),
            RegexRepetitionNode repetition => RequiresWholeHaystack(repetition.Child),
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.AbsoluteStartAnchor or RegexSyntaxKind.AbsoluteEndAnchor,
            _ => false,
        };
    }

    private static bool AnyCanMatchLineFeed(IReadOnlyList<RegexSyntaxNode> nodes)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (CanMatchLineFeed(nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyCanMatchAnchorLineBoundary(IReadOnlyList<RegexSyntaxNode> nodes)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (CanMatchAnchorLineBoundary(nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyRequiresWholeHaystack(IReadOnlyList<RegexSyntaxNode> nodes)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (RequiresWholeHaystack(nodes[index]))
            {
                return true;
            }
        }

        return false;
    }
}
