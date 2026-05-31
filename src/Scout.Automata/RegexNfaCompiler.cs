using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class RegexNfaCompiler
{
    private readonly List<RegexNfaState> states = [];
    private bool sawUtf8Disabled;

    public static RegexNfa Compile(RegexSyntaxNode root)
    {
        return Compile(
            root,
            new RegexCompileOptions(caseInsensitive: false, swapGreed: false, multiLine: false, dotMatchesNewline: false));
    }

    public static RegexNfa Compile(RegexSyntaxNode root, RegexCompileOptions options)
    {
        var compiler = new RegexNfaCompiler();
        int accept = compiler.AddAccept();
        int start = compiler.CompileNode(root, accept, options);
        return new RegexNfa(compiler.states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8));
    }

    private int CompileNode(RegexSyntaxNode node, int next, RegexCompileOptions options)
    {
        return node.Kind switch
        {
            RegexSyntaxKind.Empty => next,
            RegexSyntaxKind.Sequence => CompileSequence((RegexSequenceNode)node, next, options),
            RegexSyntaxKind.Alternation => CompileAlternation((RegexAlternationNode)node, next, options),
            RegexSyntaxKind.CapturingGroup or RegexSyntaxKind.NonCapturingGroup => CompileGroup((RegexGroupNode)node, next, options),
            RegexSyntaxKind.Repetition => CompileRepetition((RegexRepetitionNode)node, next, options),
            RegexSyntaxKind.InlineFlags => next,
            _ => CompileAtom((RegexAtomNode)node, next, options),
        };
    }

    private int CompileSequence(RegexSequenceNode node, int next, RegexCompileOptions options)
    {
        var nodes = new List<RegexSyntaxNode>();
        var nodeOptions = new List<RegexCompileOptions>();
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                RegexCompileOptions nextOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                sawUtf8Disabled |= currentOptions.Utf8 && !nextOptions.Utf8;
                currentOptions = nextOptions;
                continue;
            }

            nodes.Add(child);
            nodeOptions.Add(currentOptions);
        }

        int start = next;
        for (int index = nodes.Count - 1; index >= 0; index--)
        {
            start = CompileNode(nodes[index], start, nodeOptions[index]);
        }

        return start;
    }

    private int CompileAlternation(RegexAlternationNode node, int next, RegexCompileOptions options)
    {
        int start = CompileNode(node.Alternatives[^1], next, options);
        for (int index = node.Alternatives.Count - 2; index >= 0; index--)
        {
            int branch = CompileNode(node.Alternatives[index], next, options);
            start = AddSplit(branch, start);
        }

        return start;
    }

    private int CompileGroup(RegexGroupNode node, int next, RegexCompileOptions options)
    {
        RegexCompileOptions groupOptions = options.Apply(node.EnabledFlags, node.DisabledFlags);
        sawUtf8Disabled |= options.Utf8 && !groupOptions.Utf8;
        return CompileNode(node.Child, next, groupOptions);
    }

    private int CompileRepetition(RegexRepetitionNode node, int next, RegexCompileOptions options)
    {
        bool lazy = options.SwapGreed ? !node.Lazy : node.Lazy;
        int start = next;
        int maximum = node.Maximum ?? int.MaxValue;
        if (node.Maximum is null)
        {
            start = CompileStar(node.Child, start, options, lazy);
            maximum = node.Minimum;
        }

        for (int count = maximum; count > node.Minimum; count--)
        {
            start = CompileOptional(node.Child, start, options, lazy);
        }

        for (int count = 0; count < node.Minimum; count++)
        {
            start = CompileNode(node.Child, start, options);
        }

        return start;
    }

    private int CompileOptional(RegexSyntaxNode child, int next, RegexCompileOptions options, bool lazy)
    {
        int childStart = CompileNode(child, next, options);
        return lazy ? AddSplit(next, childStart) : AddSplit(childStart, next);
    }

    private int CompileStar(RegexSyntaxNode child, int next, RegexCompileOptions options, bool lazy)
    {
        int split = states.Count;
        states.Add(CreateControlState(RegexNfaStateKind.Split, next: -1, alternative: -1));
        int childStart = CompileNode(child, split, options);
        states[split] = lazy
            ? CreateControlState(RegexNfaStateKind.LazyLoopSplit, next, childStart)
            : CreateControlState(RegexNfaStateKind.GreedyLoopSplit, childStart, next);
        return split;
    }

    private int CompileAtom(RegexAtomNode node, int next, RegexCompileOptions options)
    {
        RegexNfaStateKind stateKind = IsPredicate(node.Kind) ? RegexNfaStateKind.Predicate : RegexNfaStateKind.Atom;
        int state = states.Count;
        states.Add(new RegexNfaState(
            stateKind,
            node.Kind,
            node.Value,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            next,
            alternative: -1));
        return state;
    }

    private int AddSplit(int first, int second)
    {
        int state = states.Count;
        states.Add(CreateControlState(RegexNfaStateKind.Split, first, second));
        return state;
    }

    private int AddAccept()
    {
        int state = states.Count;
        states.Add(CreateControlState(RegexNfaStateKind.Accept, next: -1, alternative: -1));
        return state;
    }

    private static RegexNfaState CreateControlState(RegexNfaStateKind kind, int next, int alternative)
    {
        return new RegexNfaState(
            kind,
            RegexSyntaxKind.Empty,
            ReadOnlyMemory<byte>.Empty,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: false,
            next,
            alternative);
    }

    private bool RequiresUtf8SearchBoundary(bool rootUtf8)
    {
        if (!rootUtf8 || sawUtf8Disabled)
        {
            return false;
        }

        for (int index = 0; index < states.Count; index++)
        {
            RegexNfaState state = states[index];
            if (state.Kind is (RegexNfaStateKind.Atom or RegexNfaStateKind.Predicate) &&
                !state.Utf8)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
    }

}
