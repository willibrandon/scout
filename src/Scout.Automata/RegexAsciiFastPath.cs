namespace Scout;

/// <summary>
/// Builds byte-oriented DFA projections for syntax whose ASCII behavior is equivalent.
/// </summary>
internal static class RegexAsciiFastPath
{
    /// <summary>
    /// Attempts to compile an ASCII-only projection of a parsed regex.
    /// </summary>
    /// <param name="pattern">The original regex pattern.</param>
    /// <param name="root">The parsed syntax root.</param>
    /// <param name="options">The original compilation options.</param>
    /// <param name="nfa">Receives the projected NFA when compilation succeeds.</param>
    /// <returns><see langword="true" /> when the projection is safe and DFA-compatible.</returns>
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

        RegexCompileOptions asciiOptions = options.WithAsciiSemantics();
        RegexNfa candidate = RegexNfaCompiler.Compile(root, asciiOptions);
        if (!RegexUnanchoredDenseDfa.CanCompile(candidate))
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
            case RegexSyntaxKind.UnicodePropertyClass:
            case RegexSyntaxKind.NotUnicodePropertyClass:
                return true;
            case RegexSyntaxKind.CharacterClass:
                var atom = (RegexAtomNode)node;
                return RegexByteClass.ContainsUnicodePropertyClassToken(atom.Value.Span) ||
                    atom.CharacterClass is not null &&
                    ClassSetContainsNonAsciiScalar(atom.CharacterClass.Expression);
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

    private static bool ClassSetContainsNonAsciiScalar(RegexClassSetNode node)
    {
        switch (node.Kind)
        {
            case RegexClassSetKind.Literal:
                return node.Scalar > 0x7F;
            case RegexClassSetKind.Range:
                return node.Scalar > 0x7F || node.RangeEnd > 0x7F;
            case RegexClassSetKind.Atom:
                return node.UnicodeProperty is not null;
            case RegexClassSetKind.Bracketed:
                return node.Bracketed is not null &&
                    ClassSetContainsNonAsciiScalar(node.Bracketed.Expression);
            case RegexClassSetKind.Union:
                for (int index = 0; index < node.Items.Count; index++)
                {
                    if (ClassSetContainsNonAsciiScalar(node.Items[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexClassSetKind.Binary:
                return node.Left is not null && ClassSetContainsNonAsciiScalar(node.Left) ||
                    node.Right is not null && ClassSetContainsNonAsciiScalar(node.Right);
            default:
                return false;
        }
    }
}
