using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class RegexPrefilter
{
    private readonly MemmemFinder? memmem;
    private readonly AhoCorasickAutomaton? ahoCorasick;

    private RegexPrefilter(RegexPrefilterKind kind, MemmemFinder? memmem, AhoCorasickAutomaton? ahoCorasick)
    {
        Kind = kind;
        this.memmem = memmem;
        this.ahoCorasick = ahoCorasick;
    }

    public RegexPrefilterKind Kind { get; }

    public static RegexPrefilter? Compile(RegexSyntaxNode root, RegexCompileOptions options)
    {
        if (TryCreateAlternationPrefixPrefilter(root, options, out RegexPrefilter? prefilter))
        {
            return prefilter;
        }

        var prefix = new List<byte>();
        if (!TryAppendRequiredPrefix(root, options, prefix, out _) || prefix.Count == 0)
        {
            return null;
        }

        return new RegexPrefilter(RegexPrefilterKind.Memmem, new MemmemFinder(prefix.ToArray()), ahoCorasick: null);
    }

    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (memmem is not null)
        {
            int offset = memmem.Find(haystack[startAt..]);
            return offset < 0 ? -1 : startAt + offset;
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

        prefilter = new RegexPrefilter(
            RegexPrefilterKind.AhoCorasick,
            memmem: null,
            AhoCorasickAutomaton.Create(prefixes, AhoCorasickMatchKind.LeftmostFirst));
        return true;
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
