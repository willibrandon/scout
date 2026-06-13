using System.Buffers;

namespace Scout;

internal sealed class RegexRequiredByteSetGuard
{
    private const int MaximumRequiredSets = 4;
    private const int MaximumSelectiveBytes = 128;

    private readonly SearchValues<byte>[] requiredByteSets;

    private RegexRequiredByteSetGuard(IReadOnlyList<RegexRequiredByteSet> requiredByteSets)
    {
        this.requiredByteSets = new SearchValues<byte>[requiredByteSets.Count];
        for (int index = 0; index < requiredByteSets.Count; index++)
        {
            this.requiredByteSets[index] = SearchValues.Create(requiredByteSets[index].ToArray());
        }
    }

    public static RegexRequiredByteSetGuard? TryCreate(RegexSyntaxNode root, RegexCompileOptions options)
    {
        return TryAnalyze(root, options, out List<RegexRequiredByteSet>? sets) &&
            sets is { Count: > 0 }
            ? new RegexRequiredByteSetGuard(sets)
            : null;
    }

    public bool CanSearch(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        ReadOnlySpan<byte> suffix = haystack[startOffset..];
        for (int index = 0; index < requiredByteSets.Length; index++)
        {
            if (suffix.IndexOfAny(requiredByteSets[index]) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAnalyze(RegexSyntaxNode node, RegexCompileOptions options, out List<RegexRequiredByteSet>? sets)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.Sequence:
                return TryAnalyzeSequence((RegexSequenceNode)node, options, out sets);

            case RegexSyntaxKind.Alternation:
                return TryAnalyzeAlternation((RegexAlternationNode)node, options, out sets);

            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAnalyze(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), out sets);

            case RegexSyntaxKind.Repetition:
                var repetition = (RegexRepetitionNode)node;
                if (repetition.Minimum == 0)
                {
                    sets = null;
                    return false;
                }

                return TryAnalyze(repetition.Child, options, out sets);

            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.ByteClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
                return TryAnalyzeAtom((RegexAtomNode)node, options, out sets);

            default:
                sets = null;
                return false;
        }
    }

    private static bool TryAnalyzeSequence(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out List<RegexRequiredByteSet>? sets)
    {
        var required = new List<RegexRequiredByteSet>();
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (TryAnalyze(child, currentOptions, out List<RegexRequiredByteSet>? childSets) &&
                childSets is not null)
            {
                AddMostSelective(required, childSets);
            }
        }

        sets = required.Count == 0 ? null : required;
        return sets is not null;
    }

    private static bool TryAnalyzeAlternation(
        RegexAlternationNode alternation,
        RegexCompileOptions options,
        out List<RegexRequiredByteSet>? sets)
    {
        var union = new RegexRequiredByteSet();
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryAnalyze(alternation.Alternatives[index], options, out List<RegexRequiredByteSet>? childSets) ||
                childSets is null ||
                !TryGetMostSelective(childSets, out RegexRequiredByteSet? childSet))
            {
                sets = null;
                return false;
            }

            union.UnionWith(childSet!);
            if (union.Count > MaximumSelectiveBytes)
            {
                sets = null;
                return false;
            }
        }

        sets = union.Count > 0 ? [union] : null;
        return sets is not null;
    }

    private static bool TryAnalyzeAtom(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out List<RegexRequiredByteSet>? sets)
    {
        sets = null;
        ReadOnlySpan<byte> expression = atom.Value.Span;
        if (RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                expression,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        var bytes = new RegexRequiredByteSet();
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                expression,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator))
            {
                bytes.Add((byte)value);
                if (bytes.Count > MaximumSelectiveBytes)
                {
                    return false;
                }
            }
        }

        sets = bytes.Count > 0 ? [bytes] : null;
        return sets is not null;
    }

    private static void AddMostSelective(List<RegexRequiredByteSet> destination, List<RegexRequiredByteSet> source)
    {
        for (int index = 0; index < source.Count; index++)
        {
            RegexRequiredByteSet candidate = source[index];
            if (destination.Count < MaximumRequiredSets)
            {
                destination.Add(candidate);
                continue;
            }

            int worstIndex = 0;
            for (int destinationIndex = 1; destinationIndex < destination.Count; destinationIndex++)
            {
                if (destination[destinationIndex].Count > destination[worstIndex].Count)
                {
                    worstIndex = destinationIndex;
                }
            }

            if (candidate.Count < destination[worstIndex].Count)
            {
                destination[worstIndex] = candidate;
            }
        }
    }

    private static bool TryGetMostSelective(List<RegexRequiredByteSet> sets, out RegexRequiredByteSet? best)
    {
        best = null;
        for (int index = 0; index < sets.Count; index++)
        {
            RegexRequiredByteSet candidate = sets[index];
            if (best is null || candidate.Count < best.Count)
            {
                best = candidate;
            }
        }

        return best is not null;
    }
}
