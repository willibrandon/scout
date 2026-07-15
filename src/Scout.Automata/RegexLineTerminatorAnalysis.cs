namespace Scout;

/// <summary>
/// Analyzes parsed syntax before configured record terminators are excluded from consuming atoms.
/// </summary>
internal static class RegexLineTerminatorAnalysis
{
    /// <summary>
    /// Finds the first syntax incompatibility introduced by line-terminator exclusion.
    /// </summary>
    /// <param name="root">The parsed syntax root.</param>
    /// <param name="options">The root compile options.</param>
    /// <param name="position">Receives the incompatible atom's pattern byte offset.</param>
    /// <returns>The incompatibility, or <see cref="RegexLineTerminatorAnalysisResult.None" />.</returns>
    public static RegexLineTerminatorAnalysisResult Analyze(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out int position)
    {
        position = -1;
        return options.ExcludeLineTerminators
            ? AnalyzeNode(root, options, out position)
            : RegexLineTerminatorAnalysisResult.None;
    }

    /// <summary>
    /// Validates that line-terminator exclusion leaves the parsed regex well defined.
    /// </summary>
    /// <param name="root">The parsed syntax root.</param>
    /// <param name="options">The root compile options.</param>
    /// <exception cref="FormatException">Thrown when the syntax explicitly consumes only an excluded terminator.</exception>
    public static void Validate(RegexSyntaxNode root, RegexCompileOptions options)
    {
        RegexLineTerminatorAnalysisResult result = Analyze(root, options, out int position);
        if (result == RegexLineTerminatorAnalysisResult.None)
        {
            return;
        }

        string message = result == RegexLineTerminatorAnalysisResult.ExplicitLiteral
            ? "an explicit line terminator is not allowed in a line-oriented regex"
            : "line-terminator exclusion makes a regex atom match nothing";
        throw new FormatException($"{message} at byte offset {position}");
    }

    private static RegexLineTerminatorAnalysisResult AnalyzeNode(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int position)
    {
        switch (node)
        {
            case RegexSequenceNode sequence:
                return AnalyzeSequence(sequence, options, out position);
            case RegexAlternationNode alternation:
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    RegexLineTerminatorAnalysisResult result = AnalyzeNode(
                        alternation.Alternatives[index],
                        options,
                        out position);
                    if (result != RegexLineTerminatorAnalysisResult.None)
                    {
                        return result;
                    }
                }

                break;
            case RegexGroupNode group:
                return AnalyzeNode(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out position);
            case RegexRepetitionNode repetition:
                return AnalyzeNode(repetition.Child, options, out position);
            case RegexAtomNode atom:
                return AnalyzeAtom(atom, options, out position);
        }

        position = -1;
        return RegexLineTerminatorAnalysisResult.None;
    }

    private static RegexLineTerminatorAnalysisResult AnalyzeSequence(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out int position)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            RegexLineTerminatorAnalysisResult result = AnalyzeNode(child, currentOptions, out position);
            if (result != RegexLineTerminatorAnalysisResult.None)
            {
                return result;
            }
        }

        position = -1;
        return RegexLineTerminatorAnalysisResult.None;
    }

    private static RegexLineTerminatorAnalysisResult AnalyzeAtom(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out int position)
    {
        position = atom.Position;
        if (!IsConsumingAtom(atom.Kind))
        {
            position = -1;
            return RegexLineTerminatorAnalysisResult.None;
        }

        if (atom.Kind == RegexSyntaxKind.Literal &&
            atom.Value.Length == 1 &&
            options.IsExcludedLineTerminator(atom.Value.Span[0]))
        {
            return RegexLineTerminatorAnalysisResult.ExplicitLiteral;
        }

        RegexCompileOptions unrestricted = options.WithLineTerminatorExclusion(exclude: false);
        bool hasUnrestrictedScalarRange = RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(
            atom.Kind,
            atom.Value.Span,
            unrestricted,
            out _);
        if (hasUnrestrictedScalarRange)
        {
            bool hasRestrictedScalarRange = RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(
                atom.Kind,
                atom.Value.Span,
                options,
                out _);
            if (!hasRestrictedScalarRange)
            {
                return RegexLineTerminatorAnalysisResult.EmptyAtom;
            }

            position = -1;
            return RegexLineTerminatorAnalysisResult.None;
        }

        bool matchedBeforeExclusion = false;
        for (int value = byte.MinValue; value <= byte.MaxValue; value++)
        {
            byte candidate = (byte)value;
            if (!RegexByteClass.AtomMatches(
                    candidate,
                    atom.Kind,
                    atom.Value.Span,
                    unrestricted.CaseInsensitive,
                    unrestricted.MultiLine,
                    unrestricted.DotMatchesNewline,
                    unrestricted.Crlf,
                    unrestricted.LineTerminator))
            {
                continue;
            }

            matchedBeforeExclusion = true;
            if (!options.IsExcludedLineTerminator(candidate))
            {
                position = -1;
                return RegexLineTerminatorAnalysisResult.None;
            }
        }

        if (matchedBeforeExclusion)
        {
            return RegexLineTerminatorAnalysisResult.EmptyAtom;
        }

        position = -1;
        return RegexLineTerminatorAnalysisResult.None;
    }

    private static bool IsConsumingAtom(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.Literal
            or RegexSyntaxKind.Dot
            or RegexSyntaxKind.AnyClass
            or RegexSyntaxKind.UnicodePropertyClass
            or RegexSyntaxKind.NotUnicodePropertyClass
            or RegexSyntaxKind.CharacterClass
            or RegexSyntaxKind.ByteClass
            or RegexSyntaxKind.DigitClass
            or RegexSyntaxKind.NotDigitClass
            or RegexSyntaxKind.WordClass
            or RegexSyntaxKind.NotWordClass
            or RegexSyntaxKind.WhitespaceClass
            or RegexSyntaxKind.NotWhitespaceClass
            or RegexSyntaxKind.LetterClass
            or RegexSyntaxKind.AlphanumericClass;
    }
}
