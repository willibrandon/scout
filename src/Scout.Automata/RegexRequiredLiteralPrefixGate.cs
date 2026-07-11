using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

/// <summary>
/// Narrows conservative required-literal lookbehind ranges by reverse-matching a proven prefix.
/// </summary>
/// <param name="nfa">The NFA for the ASCII projection of the reversed prefix.</param>
/// <param name="dfaSizeLimit">The maximum size of each lazily populated DFA.</param>
/// <param name="initialDfa">The initial pooled DFA runner.</param>
internal sealed class RegexRequiredLiteralPrefixGate(
    RegexNfa nfa,
    ulong dfaSizeLimit,
    RegexLazyDfa initialDfa)
{
    private const ulong MaxDfaSize = 1024 * 1024;

    private readonly RegexRunnerPool<RegexLazyDfa> _dfaPool = new(
        initialDfa,
        () => RegexLazyDfa.TryCreate(nfa, dfaSizeLimit, out RegexLazyDfa? dfa) ? dfa : null);

    /// <summary>
    /// Attempts to create a reverse-prefix gate for a uniquely proven required literal.
    /// </summary>
    /// <param name="root">The complete regex syntax tree.</param>
    /// <param name="options">The compile options in effect at the pattern root.</param>
    /// <param name="candidate">The required-literal candidate selected by analysis.</param>
    /// <param name="gate">Receives the gate when the prefix can be projected safely.</param>
    /// <returns><see langword="true"/> when a bounded thread-safe gate was compiled.</returns>
    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        RegexRequiredLiteralSetCandidate candidate,
        out RegexRequiredLiteralPrefixGate? gate)
    {
        gate = null;
        if (candidate.Literals.Length != 1 ||
            candidate.MaxLookBehind <= 0 ||
            candidate.MaxLookBehind > RegexPrefilter.RequiredLiteralLookBehind ||
            !Ascii.IsValid(candidate.Literals[0]) ||
            !TryExtractUniquePrefix(
                root,
                options,
                candidate.Literals[0],
                out RegexSyntaxNode? prefix,
                out RegexCompileOptions prefixOptions) ||
            IsAsciiProjectionUnsafe(prefix!) ||
            !RegexPrefilter.TryGetMaximumByteLength(prefix!, prefixOptions, out int maximum) ||
            maximum != candidate.MaxLookBehind)
        {
            return false;
        }

        var asciiOptions = new RegexCompileOptions(
            prefixOptions.CaseInsensitive,
            prefixOptions.SwapGreed,
            prefixOptions.MultiLine,
            prefixOptions.DotMatchesNewline,
            prefixOptions.Crlf,
            prefixOptions.LineTerminator,
            utf8: false,
            unicodeClasses: false,
            prefixOptions.SpecializationMode);
        RegexNfa reversed = RegexNfaCompiler.CompileReversed(prefix!, asciiOptions);
        if (!RegexDfaOperations.CanCompile(reversed) ||
            !RegexLazyDfa.TryCreate(reversed, MaxDfaSize, out RegexLazyDfa? dfa))
        {
            return false;
        }

        gate = new RegexRequiredLiteralPrefixGate(reversed, MaxDfaSize, dfa!);
        return true;
    }

    /// <summary>
    /// Attempts to narrow one required-literal range without excluding a feasible match start.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="requiredAt">The byte offset at which the required literal begins.</param>
    /// <param name="rangeStart">The conservative first start.</param>
    /// <param name="rangeEnd">The conservative last start.</param>
    /// <param name="narrowedStart">Receives the narrowed first start.</param>
    /// <param name="narrowedEnd">Receives the narrowed last start.</param>
    /// <returns>
    /// <see langword="true"/> when the range contains at least one feasible prefix start;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryNarrowRange(
        ReadOnlySpan<byte> haystack,
        int requiredAt,
        int rangeStart,
        int rangeEnd,
        out int narrowedStart,
        out int narrowedEnd)
    {
        narrowedStart = rangeStart;
        narrowedEnd = rangeEnd;
        if (!Ascii.IsValid(haystack[rangeStart..requiredAt]))
        {
            return rangeStart <= rangeEnd;
        }

        RegexLazyDfa? dfa = _dfaPool.Rent();
        if (dfa is null)
        {
            return true;
        }

        try
        {
            bool found = dfa.TryFindStartBoundsReverse(
                haystack,
                rangeStart,
                requiredAt,
                out int earliest,
                out int latest,
                out bool gaveUp);
            if (gaveUp)
            {
                return true;
            }

            if (!found)
            {
                return false;
            }

            narrowedStart = Math.Max(rangeStart, earliest);
            narrowedEnd = Math.Min(rangeEnd, latest);
            return narrowedStart <= narrowedEnd;
        }
        finally
        {
            _dfaPool.Return(dfa);
        }
    }

    private static bool TryExtractUniquePrefix(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ReadOnlySpan<byte> requiredLiteral,
        out RegexSyntaxNode? prefix,
        out RegexCompileOptions prefixOptions)
    {
        while (root is RegexGroupNode group)
        {
            if (ContainsUnicodeFlag(group.EnabledFlags) || ContainsUnicodeFlag(group.DisabledFlags))
            {
                prefix = null;
                prefixOptions = default;
                return false;
            }

            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            root = group.Child;
        }

        if (root is not RegexSequenceNode sequence)
        {
            prefix = null;
            prefixOptions = default;
            return false;
        }

        if (CountMatchingExactLiteralSubtrees(root, requiredLiteral) != 1)
        {
            prefix = null;
            prefixOptions = default;
            return false;
        }

        int literalIndex = -1;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            var exactLiteral = new List<byte>();
            if (!TryAppendExactLiteral(sequence.Nodes[index], exactLiteral) ||
                !CollectionsMarshal.AsSpan(exactLiteral).SequenceEqual(requiredLiteral))
            {
                continue;
            }

            if (literalIndex >= 0)
            {
                prefix = null;
                prefixOptions = default;
                return false;
            }

            literalIndex = index;
        }

        if (literalIndex < 0)
        {
            prefix = null;
            prefixOptions = default;
            return false;
        }

        prefix = new RegexSequenceNode(sequence.Nodes.Take(literalIndex).ToArray(), sequence.Position);
        prefixOptions = options;
        return true;
    }

    private static bool TryAppendExactLiteral(RegexSyntaxNode node, List<byte> literal)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.InlineFlags:
                return true;
            case RegexSyntaxKind.Literal:
                literal.AddRange(((RegexAtomNode)node).Value.ToArray());
                return true;
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (!TryAppendExactLiteral(sequence.Nodes[index], literal))
                    {
                        return false;
                    }
                }

                return true;
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                return TryAppendExactLiteral(((RegexGroupNode)node).Child, literal);
            default:
                return false;
        }
    }

    private static int CountMatchingExactLiteralSubtrees(
        RegexSyntaxNode node,
        ReadOnlySpan<byte> requiredLiteral)
    {
        var exactLiteral = new List<byte>();
        if (TryAppendExactLiteral(node, exactLiteral))
        {
            return CollectionsMarshal.AsSpan(exactLiteral).SequenceEqual(requiredLiteral) ? 1 : 0;
        }

        switch (node.Kind)
        {
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                return CountMatchingExactLiteralSubtrees(sequence.Nodes, requiredLiteral);
            case RegexSyntaxKind.Alternation:
                var alternation = (RegexAlternationNode)node;
                return CountMatchingExactLiteralSubtrees(alternation.Alternatives, requiredLiteral);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                return CountMatchingExactLiteralSubtrees(
                    ((RegexGroupNode)node).Child,
                    requiredLiteral);
            case RegexSyntaxKind.Repetition:
                return CountMatchingExactLiteralSubtrees(
                    ((RegexRepetitionNode)node).Child,
                    requiredLiteral);
            default:
                return 0;
        }
    }

    private static int CountMatchingExactLiteralSubtrees(
        IReadOnlyList<RegexSyntaxNode> nodes,
        ReadOnlySpan<byte> requiredLiteral)
    {
        int count = 0;
        for (int index = 0; index < nodes.Count; index++)
        {
            count += CountMatchingExactLiteralSubtrees(nodes[index], requiredLiteral);
            if (count > 1)
            {
                return count;
            }
        }

        return count;
    }

    private static bool IsAsciiProjectionUnsafe(RegexSyntaxNode node)
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
            case RegexSyntaxKind.Literal:
                return !Ascii.IsValid(((RegexAtomNode)node).Value.Span);
            case RegexSyntaxKind.CharacterClass:
                ReadOnlySpan<byte> expression = ((RegexAtomNode)node).Value.Span;
                return !Ascii.IsValid(expression) ||
                    RegexByteClass.ContainsUnicodePropertyClassToken(expression);
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (IsAsciiProjectionUnsafe(sequence.Nodes[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.Alternation:
                var alternation = (RegexAlternationNode)node;
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (IsAsciiProjectionUnsafe(alternation.Alternatives[index]))
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
                    IsAsciiProjectionUnsafe(group.Child);
            case RegexSyntaxKind.InlineFlags:
                var flags = (RegexInlineFlagsNode)node;
                return ContainsUnicodeFlag(flags.EnabledFlags) ||
                    ContainsUnicodeFlag(flags.DisabledFlags);
            case RegexSyntaxKind.Repetition:
                return IsAsciiProjectionUnsafe(((RegexRepetitionNode)node).Child);
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.Dot:
            case RegexSyntaxKind.AnyClass:
            case RegexSyntaxKind.ByteClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
            case RegexSyntaxKind.LetterClass:
            case RegexSyntaxKind.AlphanumericClass:
                return false;
            default:
                return true;
        }
    }

    private static bool ContainsUnicodeFlag(string flags)
    {
        return flags.Contains('u', StringComparison.Ordinal);
    }
}
