namespace Scout;

internal static class RegexAsciiFastPath
{
    public static bool TryCompileNfa(
        ReadOnlySpan<byte> pattern,
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexNfa? nfa)
    {
        nfa = null;
        if ((!options.UnicodeClasses && !options.Utf8) ||
            !IsAscii(pattern) ||
            HasUnsafeSyntax(root))
        {
            return false;
        }

        var asciiOptions = new RegexCompileOptions(
            options.CaseInsensitive,
            options.SwapGreed,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            utf8: false,
            unicodeClasses: false);
        RegexNfa candidate = RegexNfaCompiler.Compile(root, asciiOptions);
        if (!RegexDfaOperations.CanCompile(candidate))
        {
            return false;
        }

        nfa = candidate;
        return true;
    }

    private static bool IsAscii(ReadOnlySpan<byte> pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasUnsafeSyntax(RegexSyntaxNode node)
    {
        switch (node.Kind)
        {
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
            case RegexSyntaxKind.UnicodePropertyClass:
            case RegexSyntaxKind.NotUnicodePropertyClass:
                return true;
            case RegexSyntaxKind.CharacterClass:
                return RegexByteClass.ContainsUnicodePropertyClassToken(((RegexAtomNode)node).Value.Span);
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (HasUnsafeSyntax(sequence.Nodes[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.Alternation:
                var alternation = (RegexAlternationNode)node;
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (HasUnsafeSyntax(alternation.Alternatives[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return ContainsUnicodeFlag(group.EnabledFlags) ||
                    ContainsUnicodeFlag(group.DisabledFlags) ||
                    HasUnsafeSyntax(group.Child);
            case RegexSyntaxKind.InlineFlags:
                var flags = (RegexInlineFlagsNode)node;
                return ContainsUnicodeFlag(flags.EnabledFlags) ||
                    ContainsUnicodeFlag(flags.DisabledFlags);
            case RegexSyntaxKind.Repetition:
                return HasUnsafeSyntax(((RegexRepetitionNode)node).Child);
            default:
                return false;
        }
    }

    private static bool ContainsUnicodeFlag(string flags)
    {
        return flags.Contains('u', StringComparison.Ordinal);
    }
}
