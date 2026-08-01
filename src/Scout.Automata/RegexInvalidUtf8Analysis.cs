namespace Scout;

/// <summary>
/// Determines whether a syntax tree must consume the replacement scalar supplied for malformed UTF-8.
/// </summary>
internal static class RegexInvalidUtf8Analysis
{
    private const int ReplacementScalar = 0xFFFD;

    /// <summary>
    /// Reports whether malformed UTF-8 matching requires replacement-scalar consumption.
    /// </summary>
    public static bool RequiresReplacementScalarConsumption(
        RegexSyntaxNode node,
        RegexCompileOptions options)
    {
        if (!options.MatchInvalidUtf8)
        {
            return false;
        }

        var scalarPlans = new RegexScalarAtomPlanCache();
        return RequiresReplacementScalarConsumption(node, options, scalarPlans);
    }

    private static bool RequiresReplacementScalarConsumption(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        RegexScalarAtomPlanCache scalarPlans)
    {
        return node switch
        {
            RegexAtomNode atom => AtomRequiresReplacementScalarConsumption(atom, options, scalarPlans),
            RegexGroupNode group => RequiresReplacementScalarConsumption(
                group.Child,
                options.Apply(group.EnabledFlags, group.DisabledFlags),
                scalarPlans),
            RegexSequenceNode sequence => SequenceRequiresReplacementScalarConsumption(sequence, options, scalarPlans),
            RegexAlternationNode alternation => AnyRequiresReplacementScalarConsumption(
                alternation.Alternatives,
                options,
                scalarPlans),
            RegexRepetitionNode repetition => RequiresReplacementScalarConsumption(
                repetition.Child,
                options,
                scalarPlans),
            _ => false,
        };
    }

    private static bool SequenceRequiresReplacementScalarConsumption(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        RegexScalarAtomPlanCache scalarPlans)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
            }
            else if (RequiresReplacementScalarConsumption(child, currentOptions, scalarPlans))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyRequiresReplacementScalarConsumption(
        IReadOnlyList<RegexSyntaxNode> nodes,
        RegexCompileOptions options,
        RegexScalarAtomPlanCache scalarPlans)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (RequiresReplacementScalarConsumption(nodes[index], options, scalarPlans))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AtomRequiresReplacementScalarConsumption(
        RegexAtomNode atom,
        RegexCompileOptions options,
        RegexScalarAtomPlanCache scalarPlans)
    {
        if (!options.Utf8 && !options.UnicodeClasses)
        {
            return false;
        }

        // Zero-width predicates carry MatchInvalidUtf8 into their NFA states and evaluate
        // malformed context in place. Only consuming scalar atoms need the compact scalar path.
        if (!RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        if (!scalarPlans.TryGet(atom, options, out RegexScalarAtomPlan? plan))
        {
            // A scalar operation that cannot be reduced to a set is conservatively sensitive.
            return true;
        }

        RegexScalarRange[] ranges = plan!.Ranges;
        int low = 0;
        int high = ranges.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            RegexScalarRange range = ranges[middle];
            if (ReplacementScalar < range.Start)
            {
                high = middle - 1;
            }
            else if (ReplacementScalar > range.End)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
