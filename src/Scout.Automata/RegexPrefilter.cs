
namespace Scout;

internal sealed class RegexPrefilter
{
    private readonly MemmemFinder? memmem;
    private readonly RegexTeddyPrefilter? teddy;
    private readonly AhoCorasickAutomaton? ahoCorasick;
    private readonly RegexPrefixCandidateGate? candidateGate;

    private RegexPrefilter(
        RegexPrefilterKind kind,
        MemmemFinder? memmem,
        RegexTeddyPrefilter? teddy,
        AhoCorasickAutomaton? ahoCorasick,
        RegexPrefixCandidateGate? candidateGate = null)
    {
        Kind = kind;
        this.memmem = memmem;
        this.teddy = teddy;
        this.ahoCorasick = ahoCorasick;
        this.candidateGate = candidateGate;
    }

    public RegexPrefilterKind Kind { get; }

    public static RegexPrefilter? Compile(RegexSyntaxNode root, RegexCompileOptions options)
    {
        if (TryCreateAlternationPrefixPrefilter(root, options, out RegexPrefilter? prefilter) ||
            TryCreateSequenceAlternationPrefixPrefilter(root, options, out prefilter))
        {
            return prefilter;
        }

        var prefix = new List<byte>();
        if (!TryAppendRequiredPrefix(root, options, prefix, out _) || prefix.Count == 0)
        {
            return null;
        }

        return new RegexPrefilter(
            RegexPrefilterKind.Memmem,
            new MemmemFinder(prefix.ToArray()),
            teddy: null,
            ahoCorasick: null);
    }

    private static bool TryCreateSequenceAlternationPrefixPrefilter(RegexSyntaxNode root, RegexCompileOptions options, out RegexPrefilter? prefilter)
    {
        prefilter = null;
        if (!TryCollectSequenceAlternationPrefixes(root, options, out byte[][]? prefixes))
        {
            return false;
        }

        RegexPrefixCandidateGate? candidateGate = null;
        RegexPrefixCandidateGate.TryCreate(root, options, prefixes, out candidateGate);
        return prefixes is not null &&
            TryCreatePrefixSetPrefilter(prefixes, candidateGate, out prefilter);
    }

    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = startAt;
        while (searchAt < haystack.Length)
        {
            int candidate = FindRawCandidate(haystack, searchAt);
            if (candidate < 0)
            {
                return -1;
            }

            if (candidateGate is null ||
                candidateGate.CanMatch(haystack, candidate, out int resumeAt))
            {
                return candidate;
            }

            searchAt = Math.Clamp(Math.Max(resumeAt, candidate + 1), 0, haystack.Length);
        }

        return -1;
    }

    private int FindRawCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (memmem is not null)
        {
            int offset = memmem.Find(haystack[startAt..]);
            return offset < 0 ? -1 : startAt + offset;
        }

        if (teddy is not null)
        {
            return teddy.FindCandidate(haystack, startAt);
        }

        AhoCorasickMatch? match = ahoCorasick!.Find(haystack[startAt..]);
        return match.HasValue ? startAt + match.Value.Start : -1;
    }

    private static bool TryCreateAlternationPrefixPrefilter(RegexSyntaxNode root, RegexCompileOptions options, out RegexPrefilter? prefilter)
    {
        prefilter = null;
        root = UnwrapTransparentGroups(root);
        if (root is not RegexAlternationNode alternation || alternation.Alternatives.Count < 2)
        {
            return false;
        }

        byte[][] prefixes = new byte[alternation.Alternatives.Count][];
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            var prefix = new List<byte>();
            if (!TryAppendRequiredPrefix(alternation.Alternatives[index], options, prefix, out _) || prefix.Count == 0)
            {
                return false;
            }

            prefixes[index] = prefix.ToArray();
        }

        return TryCreatePrefixSetPrefilter(prefixes, candidateGate: null, out prefilter);
    }

    private static bool TryCreatePrefixSetPrefilter(byte[][] prefixes, RegexPrefixCandidateGate? candidateGate, out RegexPrefilter? prefilter)
    {
        prefilter = null;
        if (prefixes.Length < 2)
        {
            return false;
        }

        if (RegexTeddyPrefilter.TryCreate(prefixes, out RegexTeddyPrefilter? teddy))
        {
            prefilter = new RegexPrefilter(
                RegexPrefilterKind.Teddy,
                memmem: null,
                teddy,
                ahoCorasick: null,
                candidateGate);
            return true;
        }

        prefilter = new RegexPrefilter(
            RegexPrefilterKind.AhoCorasick,
            memmem: null,
            teddy: null,
            ahoCorasick: AhoCorasickAutomaton.Create(prefixes, AhoCorasickMatchKind.LeftmostFirst),
            candidateGate);
        return true;
    }

    private static bool TryCollectSequenceAlternationPrefixes(RegexSyntaxNode node, RegexCompileOptions options, out byte[][]? prefixes)
    {
        prefixes = null;
        node = UnwrapTransparentGroups(node);
        if (node is RegexAlternationNode alternation)
        {
            prefixes = new byte[alternation.Alternatives.Count][];
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                var prefix = new List<byte>();
                if (!TryAppendRequiredPrefix(alternation.Alternatives[index], options, prefix, out _) ||
                    prefix.Count == 0)
                {
                    prefixes = null;
                    return false;
                }

                prefixes[index] = prefix.ToArray();
            }

            return true;
        }

        if (node is RegexGroupNode group)
        {
            RegexCompileOptions groupOptions = options.Apply(group.EnabledFlags, group.DisabledFlags);
            return TryCollectSequenceAlternationPrefixes(group.Child, groupOptions, out prefixes);
        }

        if (node is RegexRepetitionNode repetition)
        {
            return repetition.Minimum > 0 &&
                TryCollectSequenceAlternationPrefixes(repetition.Child, options, out prefixes);
        }

        if (node is not RegexSequenceNode sequence)
        {
            return false;
        }

        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            return TryCollectSequenceAlternationPrefixes(child, currentOptions, out prefixes);
        }

        return false;
    }

    private static bool TryAppendRequiredPrefix(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool canContinue)
    {
        int originalCount = prefix.Count;
        canContinue = false;
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                canContinue = true;
                return true;
            case RegexSyntaxKind.Literal:
                if (options.CaseInsensitive)
                {
                    return false;
                }

                prefix.AddRange(((RegexAtomNode)node).Value.ToArray());
                canContinue = true;
                return prefix.Count > originalCount;
            case RegexSyntaxKind.Sequence:
                return TryAppendSequencePrefix((RegexSequenceNode)node, options, prefix, out canContinue);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                return TryAppendGroupPrefix((RegexGroupNode)node, options, prefix, out canContinue);
            case RegexSyntaxKind.Repetition:
                return TryAppendRepetitionPrefix((RegexRepetitionNode)node, options, prefix, out canContinue);
            default:
                return false;
        }
    }

    private static bool TryAppendSequencePrefix(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool canContinue)
    {
        int originalCount = prefix.Count;
        RegexCompileOptions currentOptions = options;
        canContinue = true;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryAppendRequiredPrefix(child, currentOptions, prefix, out bool childCanContinue))
            {
                canContinue = false;
                return prefix.Count > originalCount;
            }

            if (!childCanContinue)
            {
                canContinue = false;
                return prefix.Count > originalCount;
            }
        }

        return prefix.Count > originalCount;
    }

    private static bool TryAppendGroupPrefix(
        RegexGroupNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool canContinue)
    {
        RegexCompileOptions groupOptions = options.Apply(node.EnabledFlags, node.DisabledFlags);
        return TryAppendRequiredPrefix(node.Child, groupOptions, prefix, out canContinue);
    }

    private static bool TryAppendRepetitionPrefix(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool canContinue)
    {
        canContinue = false;
        if (node.Minimum == 0)
        {
            return false;
        }

        return TryAppendRequiredPrefix(node.Child, options, prefix, out _);
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }
}
