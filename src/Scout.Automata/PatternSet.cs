using System;
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Represents an ordered multi-regex set.
/// </summary>
public sealed class PatternSet
{
    private readonly RegexAutomaton[] automata;
    private readonly int[] automataPatternIds;
    private readonly PatternSetLiteralAccelerator? literalAccelerator;
    private readonly int count;

    private PatternSet(
        int count,
        RegexAutomaton[] automata,
        int[] automataPatternIds,
        PatternSetLiteralAccelerator? literalAccelerator)
    {
        this.count = count;
        this.automata = automata;
        this.automataPatternIds = automataPatternIds;
        this.literalAccelerator = literalAccelerator;
    }

    /// <summary>
    /// Gets the number of patterns in the set.
    /// </summary>
    public int Count => count;

    internal bool UsesLiteralAccelerator => literalAccelerator is not null;

    /// <summary>
    /// Compiles an ordered set of byte regex patterns.
    /// </summary>
    /// <param name="patterns">The ordered pattern bytes.</param>
    /// <returns>The compiled set.</returns>
    public static PatternSet Compile(IReadOnlyList<byte[]> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var automata = new List<RegexAutomaton>();
        var automataPatternIds = new List<int>();
        var literalPatterns = new List<byte[]>();
        var literalPatternIds = new List<int>();
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index] ?? throw new ArgumentNullException(nameof(patterns));
            if (TryGetLiteralPattern(pattern, out byte[] literal) && literal.Length != 0)
            {
                literalPatterns.Add(literal);
                literalPatternIds.Add(index);
                continue;
            }

            automata.Add(RegexAutomaton.Compile(pattern));
            automataPatternIds.Add(index);
        }

        PatternSetLiteralAccelerator? literalAccelerator = literalPatterns.Count == 0
            ? null
            : new PatternSetLiteralAccelerator(literalPatterns, literalPatternIds);
        return new PatternSet(patterns.Count, automata.ToArray(), automataPatternIds.ToArray(), literalAccelerator);
    }

    /// <summary>
    /// Returns a value indicating whether any pattern matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when at least one pattern matches.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        if (literalAccelerator is not null && literalAccelerator.IsMatch(haystack))
        {
            return true;
        }

        for (int index = 0; index < automata.Length; index++)
        {
            if (automata[index].IsMatch(haystack))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the leftmost match across all patterns, using pattern order to break ties.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The selected match, or <see langword="null" /> when no pattern matches.</returns>
    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack)
    {
        PatternSetMatch? best = literalAccelerator?.Find(haystack);
        for (int index = 0; index < automata.Length; index++)
        {
            RegexMatch? match = automata[index].Find(haystack);
            if (!match.HasValue)
            {
                continue;
            }

            var candidate = new PatternSetMatch(automataPatternIds[index], match.Value);
            if (IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns pattern identifiers whose regex matches the haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>Matching pattern identifiers in insertion order.</returns>
    public IReadOnlyList<int> MatchingPatternIds(ReadOnlySpan<byte> haystack)
    {
        bool[] matched = new bool[count];
        literalAccelerator?.MarkMatchingPatternIds(haystack, matched);
        for (int index = 0; index < automata.Length; index++)
        {
            if (automata[index].IsMatch(haystack))
            {
                matched[automataPatternIds[index]] = true;
            }
        }

        var patternIds = new List<int>();
        for (int index = 0; index < matched.Length; index++)
        {
            if (matched[index])
            {
                patternIds.Add(index);
            }
        }

        return patternIds;
    }

    internal static bool IsBetter(PatternSetMatch candidate, PatternSetMatch? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        PatternSetMatch current = best.Value;
        if (candidate.Match.Start != current.Match.Start)
        {
            return candidate.Match.Start < current.Match.Start;
        }

        return candidate.PatternId < current.PatternId;
    }

    private static bool TryGetLiteralPattern(byte[] pattern, out byte[] literal)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        var bytes = new List<byte>();
        if (TryAppendLiteralPattern(tree.Root, bytes))
        {
            literal = bytes.ToArray();
            return true;
        }

        literal = [];
        return false;
    }

    private static bool TryAppendLiteralPattern(RegexSyntaxNode node, List<byte> bytes)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                return true;
            case RegexSyntaxKind.Literal:
                bytes.AddRange(((RegexAtomNode)node).Value.ToArray());
                return true;
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (!TryAppendLiteralPattern(sequence.Nodes[index], bytes))
                    {
                        return false;
                    }
                }

                return true;
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return string.IsNullOrEmpty(group.EnabledFlags) &&
                    string.IsNullOrEmpty(group.DisabledFlags) &&
                    TryAppendLiteralPattern(group.Child, bytes);
            default:
                return false;
        }
    }
}
