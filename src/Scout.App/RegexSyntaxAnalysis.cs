namespace Scout;

/// <summary>
/// Provides semantic queries over parsed regex syntax trees used to select the search execution mode.
/// </summary>
internal static class RegexSyntaxAnalysis
{
    /// <summary>
    /// Determines whether any pattern can consume a line-feed byte.
    /// </summary>
    /// <param name="patterns">The regex patterns to inspect.</param>
    /// <param name="dotMatchesNewline">Whether dot matches line terminators before inline flags are applied.</param>
    /// <returns><see langword="true" /> when a pattern can consume a line feed.</returns>
    public static bool CanMatchLineFeed(
        IReadOnlyList<byte[]> patterns,
        bool dotMatchesNewline = false)
    {
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline,
            utf8: false);
        for (int index = 0; index < patterns.Count; index++)
        {
            if (CanMatchLineFeed(RegexSyntaxParser.Parse(patterns[index]).Root, options))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether any pattern contains a line anchor.
    /// </summary>
    /// <param name="patterns">The regex patterns to inspect.</param>
    /// <returns><see langword="true" /> when a pattern contains a line anchor.</returns>
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

    /// <summary>
    /// Determines whether any pattern requires the original haystack to preserve anchor semantics.
    /// </summary>
    /// <param name="patterns">The regex patterns to inspect.</param>
    /// <returns><see langword="true" /> when a pattern requires whole-haystack matching.</returns>
    public static bool RequiresWholeHaystack(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (RequiresWholeHaystack(RegexSyntaxParser.Parse(patterns[index]).Root, multiLine: true))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanMatchLineFeed(
        RegexSyntaxNode node,
        RegexCompileOptions options)
    {
        return node switch
        {
            RegexGroupNode group => CanMatchLineFeed(
                group.Child,
                options.Apply(group.EnabledFlags, group.DisabledFlags)),
            RegexSequenceNode sequence => SequenceCanMatchLineFeed(sequence.Nodes, options),
            RegexAlternationNode alternation => AnyCanMatchLineFeed(alternation.Alternatives, options),
            RegexRepetitionNode repetition => CanMatchLineFeed(repetition.Child, options),
            RegexAtomNode atom => AtomCanMatchLineFeed(atom, options),
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

    private static bool RequiresWholeHaystack(RegexSyntaxNode node, bool multiLine)
    {
        return node switch
        {
            RegexGroupNode group => RequiresWholeHaystack(
                group.Child,
                ApplyMultiLine(multiLine, group.EnabledFlags, group.DisabledFlags)),
            RegexSequenceNode sequence => AnyRequiresWholeHaystack(sequence.Nodes, multiLine),
            RegexAlternationNode alternation => AnyRequiresWholeHaystack(alternation.Alternatives, multiLine),
            RegexRepetitionNode repetition => RequiresWholeHaystack(repetition.Child, multiLine),
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.AbsoluteStartAnchor or RegexSyntaxKind.AbsoluteEndAnchor ||
                !multiLine && atom.Kind is RegexSyntaxKind.StartAnchor or RegexSyntaxKind.EndAnchor,
            _ => false,
        };
    }

    private static bool SequenceCanMatchLineFeed(
        IReadOnlyList<RegexSyntaxNode> nodes,
        RegexCompileOptions options)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < nodes.Count; index++)
        {
            RegexSyntaxNode node = nodes[index];
            if (CanMatchLineFeed(node, currentOptions))
            {
                return true;
            }

            if (node is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
            }
        }

        return false;
    }

    private static bool AnyCanMatchLineFeed(
        IReadOnlyList<RegexSyntaxNode> nodes,
        RegexCompileOptions options)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (CanMatchLineFeed(nodes[index], options))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AtomCanMatchLineFeed(
        RegexAtomNode atom,
        RegexCompileOptions options)
    {
        return RegexByteClass.TryGetAtomMatchLength(
            "\n"u8,
            position: 0,
            atom.Kind,
            atom.Value.Span,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out _);
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

    private static bool AnyRequiresWholeHaystack(
        IReadOnlyList<RegexSyntaxNode> nodes,
        bool multiLine)
    {
        bool effectiveMultiLine = multiLine;
        for (int index = 0; index < nodes.Count; index++)
        {
            RegexSyntaxNode node = nodes[index];
            if (RequiresWholeHaystack(node, effectiveMultiLine))
            {
                return true;
            }

            if (node is RegexInlineFlagsNode flags)
            {
                effectiveMultiLine = ApplyMultiLine(
                    effectiveMultiLine,
                    flags.EnabledFlags,
                    flags.DisabledFlags);
            }
        }

        return false;
    }

    private static bool ApplyMultiLine(
        bool multiLine,
        string enabledFlags,
        string disabledFlags)
    {
        if (enabledFlags.Contains('m', StringComparison.Ordinal))
        {
            multiLine = true;
        }

        if (disabledFlags.Contains('m', StringComparison.Ordinal))
        {
            multiLine = false;
        }

        return multiLine;
    }
}
