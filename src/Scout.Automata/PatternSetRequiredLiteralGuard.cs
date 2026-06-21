namespace Scout;

internal sealed class PatternSetRequiredLiteralGuard
{
    private const int MaxAlternatives = 64;
    private const int MaxElements = 512;
    private const int MaxBoundedRepeat = 1024;
    internal const int MaxUnboundedBacktrackChoices = 256;
    private const int InitialBudget = 4096;

    private readonly PatternSetRequiredLiteralGuardSequence[] alternatives;

    private PatternSetRequiredLiteralGuard(PatternSetRequiredLiteralGuardSequence[] alternatives)
    {
        this.alternatives = alternatives;
    }

    public static PatternSetRequiredLiteralGuard? TryCreate(RegexSyntaxNode root, RegexCompileOptions options)
    {
        if (options.Utf8 || options.UnicodeClasses)
        {
            return null;
        }

        var alternatives = new List<List<PatternSetRequiredLiteralGuardElement>> { new() };
        if (!TryAppend(root, options, alternatives) ||
            alternatives.Count == 0 ||
            alternatives.Count > MaxAlternatives)
        {
            return null;
        }

        var sequences = new PatternSetRequiredLiteralGuardSequence[alternatives.Count];
        for (int index = 0; index < alternatives.Count; index++)
        {
            List<PatternSetRequiredLiteralGuardElement> elements = alternatives[index];
            if (elements.Count == 0 || elements.Count > MaxElements)
            {
                return null;
            }

            sequences[index] = new PatternSetRequiredLiteralGuardSequence(elements.ToArray());
        }

        return new PatternSetRequiredLiteralGuard(sequences);
    }

    public bool CanMatchAt(ReadOnlySpan<byte> haystack, int start)
    {
        if (start < 0 || start > haystack.Length)
        {
            return false;
        }

        for (int index = 0; index < alternatives.Length; index++)
        {
            int budget = InitialBudget;
            if (alternatives[index].CouldMatchFrom(haystack, elementIndex: 0, start, ref budget))
            {
                return true;
            }

            if (budget <= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAppend(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.InlineFlags:
                return true;

            case RegexSyntaxKind.Literal:
                return TryAppendLiteral(((RegexAtomNode)node).Value.Span, options, alternatives);

            case RegexSyntaxKind.Dot:
            case RegexSyntaxKind.AnyClass:
            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
                return TryAppendAtom((RegexAtomNode)node, options, minimum: 1, maximum: 1, lazy: false, alternatives);

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
                return TryAppendPredicate((RegexAtomNode)node, options, alternatives);

            case RegexSyntaxKind.Sequence:
                return TryAppendSequence((RegexSequenceNode)node, options, alternatives);

            case RegexSyntaxKind.Alternation:
                return TryAppendAlternation((RegexAlternationNode)node, options, alternatives);

            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                RegexCompileOptions groupOptions = options.Apply(group.EnabledFlags, group.DisabledFlags);
                return !groupOptions.Utf8 &&
                    !groupOptions.UnicodeClasses &&
                    TryAppend(group.Child, groupOptions, alternatives);

            case RegexSyntaxKind.Repetition:
                return TryAppendRepetition((RegexRepetitionNode)node, options, alternatives);

            default:
                return false;
        }
    }

    private static bool TryAppendSequence(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                if (currentOptions.Utf8 || currentOptions.UnicodeClasses)
                {
                    return false;
                }

                continue;
            }

            if (!TryAppend(child, currentOptions, alternatives))
            {
                return HasAnyElements(alternatives);
            }
        }

        return true;
    }

    private static bool TryAppendAlternation(
        RegexAlternationNode alternation,
        RegexCompileOptions options,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        var merged = new List<List<PatternSetRequiredLiteralGuardElement>>();
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            List<List<PatternSetRequiredLiteralGuardElement>> branch = Clone(alternatives);
            if (!TryAppend(alternation.Alternatives[index], options, branch))
            {
                return false;
            }

            merged.AddRange(branch);
            if (merged.Count > MaxAlternatives)
            {
                return false;
            }
        }

        alternatives.Clear();
        alternatives.AddRange(merged);
        return true;
    }

    private static bool TryAppendRepetition(
        RegexRepetitionNode repetition,
        RegexCompileOptions options,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        if (repetition.Maximum is > MaxBoundedRepeat)
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapGroups(repetition.Child, ref options);
        if (options.Utf8 || options.UnicodeClasses)
        {
            return false;
        }

        if (child is RegexAtomNode { Kind: RegexSyntaxKind.Literal } literal)
        {
            return literal.Value.Length == 1 &&
                TryAppendLiteralAtom(literal.Value.Span[0], options, repetition.Minimum, repetition.Maximum, repetition.Lazy, alternatives);
        }

        return child is RegexAtomNode atom &&
            atom.Kind is (RegexSyntaxKind.Dot
                or RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.CharacterClass
                or RegexSyntaxKind.DigitClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.WordClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.WhitespaceClass
                or RegexSyntaxKind.NotWhitespaceClass) &&
            TryAppendAtom(atom, options, repetition.Minimum, repetition.Maximum, repetition.Lazy, alternatives);
    }

    private static RegexSyntaxNode UnwrapGroups(RegexSyntaxNode node, ref RegexCompileOptions options)
    {
        while (node is RegexGroupNode group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        return node;
    }

    private static bool TryAppendLiteral(
        ReadOnlySpan<byte> literal,
        RegexCompileOptions options,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        if (literal.Length == 0)
        {
            return true;
        }

        for (int index = 0; index < literal.Length; index++)
        {
            if (!TryAppendLiteralAtom(literal[index], options, minimum: 1, maximum: 1, lazy: false, alternatives))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendLiteralAtom(
        byte value,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        bool lazy,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        var segment = new RegexSimpleSequenceSegment(
            RegexSyntaxKind.Literal,
            [value],
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum,
            maximum,
            lazy);
        AppendElement(alternatives, PatternSetRequiredLiteralGuardElement.CreateByte(segment));
        return true;
    }

    private static bool TryAppendAtom(
        RegexAtomNode atom,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        bool lazy,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        if (RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        var segment = new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum,
            maximum,
            lazy);
        AppendElement(alternatives, PatternSetRequiredLiteralGuardElement.CreateByte(segment));
        return true;
    }

    private static bool TryAppendPredicate(
        RegexAtomNode atom,
        RegexCompileOptions options,
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        if (options.Utf8 || options.UnicodeClasses)
        {
            return false;
        }

        AppendElement(alternatives, PatternSetRequiredLiteralGuardElement.CreatePredicate(
            atom.Kind,
            options.MultiLine,
            options.Crlf,
            options.LineTerminator));
        return true;
    }

    private static void AppendElement(
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives,
        PatternSetRequiredLiteralGuardElement element)
    {
        for (int index = 0; index < alternatives.Count; index++)
        {
            alternatives[index].Add(element);
        }
    }

    private static List<List<PatternSetRequiredLiteralGuardElement>> Clone(
        List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        var clone = new List<List<PatternSetRequiredLiteralGuardElement>>(alternatives.Count);
        for (int index = 0; index < alternatives.Count; index++)
        {
            clone.Add([.. alternatives[index]]);
        }

        return clone;
    }

    private static bool HasAnyElements(List<List<PatternSetRequiredLiteralGuardElement>> alternatives)
    {
        for (int index = 0; index < alternatives.Count; index++)
        {
            if (alternatives[index].Count > 0)
            {
                return true;
            }
        }

        return false;
    }
}
