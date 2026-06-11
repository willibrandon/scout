namespace Scout;

internal sealed class RegexNfaCompiler
{
    private readonly List<RegexNfaState> states = [];
    private readonly bool includeCaptures;
    private readonly int captureCount;
    private bool sawUtf8Disabled;

    private RegexNfaCompiler(bool includeCaptures = false, int captureCount = 0)
    {
        this.includeCaptures = includeCaptures;
        this.captureCount = captureCount;
    }

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

    public static RegexNfa CompileUnanchored(RegexSyntaxNode root, RegexCompileOptions options)
    {
        var compiler = new RegexNfaCompiler();
        int accept = compiler.AddAccept();
        int patternStart = compiler.CompileNode(root, accept, options);
        int start = compiler.AddUnanchoredPrefix(patternStart);
        return new RegexNfa(compiler.states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8));
    }

    public static RegexNfa CompileReversed(RegexSyntaxNode root, RegexCompileOptions options)
    {
        var compiler = new RegexNfaCompiler();
        int accept = compiler.AddAccept();
        int start = compiler.CompileNodeReversed(root, accept, options);
        return new RegexNfa(compiler.states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8));
    }

    public static RegexNfa CompileCaptures(RegexSyntaxNode root, RegexCompileOptions options, int captureCount)
    {
        var compiler = new RegexNfaCompiler(includeCaptures: true, captureCount);
        int accept = compiler.AddAccept();
        int start = compiler.CompileNode(root, accept, options);
        return new RegexNfa(compiler.states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8), captureCount);
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

    private int CompileNodeReversed(RegexSyntaxNode node, int next, RegexCompileOptions options)
    {
        return node.Kind switch
        {
            RegexSyntaxKind.Empty => next,
            RegexSyntaxKind.Sequence => CompileSequenceReversed((RegexSequenceNode)node, next, options),
            RegexSyntaxKind.Alternation => CompileAlternationReversed((RegexAlternationNode)node, next, options),
            RegexSyntaxKind.CapturingGroup or RegexSyntaxKind.NonCapturingGroup => CompileGroupReversed((RegexGroupNode)node, next, options),
            RegexSyntaxKind.Repetition => CompileRepetitionReversed((RegexRepetitionNode)node, next, options),
            RegexSyntaxKind.InlineFlags => next,
            _ => CompileAtomReversed((RegexAtomNode)node, next, options),
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

    private int CompileSequenceReversed(RegexSequenceNode node, int next, RegexCompileOptions options)
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
        for (int index = 0; index < nodes.Count; index++)
        {
            start = CompileNodeReversed(nodes[index], start, nodeOptions[index]);
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

    private int CompileAlternationReversed(RegexAlternationNode node, int next, RegexCompileOptions options)
    {
        int start = CompileNodeReversed(node.Alternatives[^1], next, options);
        for (int index = node.Alternatives.Count - 2; index >= 0; index--)
        {
            int branch = CompileNodeReversed(node.Alternatives[index], next, options);
            start = AddSplit(branch, start);
        }

        return start;
    }

    private int CompileGroup(RegexGroupNode node, int next, RegexCompileOptions options)
    {
        RegexCompileOptions groupOptions = options.Apply(node.EnabledFlags, node.DisabledFlags);
        sawUtf8Disabled |= options.Utf8 && !groupOptions.Utf8;
        if (!includeCaptures || node.Kind != RegexSyntaxKind.CapturingGroup)
        {
            return CompileNode(node.Child, next, groupOptions);
        }

        int end = AddCapture(RegexNfaStateKind.CaptureEnd, node.CaptureIndex, next);
        int child = CompileNode(node.Child, end, groupOptions);
        return AddCapture(RegexNfaStateKind.CaptureStart, node.CaptureIndex, child);
    }

    private int CompileGroupReversed(RegexGroupNode node, int next, RegexCompileOptions options)
    {
        RegexCompileOptions groupOptions = options.Apply(node.EnabledFlags, node.DisabledFlags);
        sawUtf8Disabled |= options.Utf8 && !groupOptions.Utf8;
        return CompileNodeReversed(node.Child, next, groupOptions);
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

    private int CompileRepetitionReversed(RegexRepetitionNode node, int next, RegexCompileOptions options)
    {
        bool lazy = options.SwapGreed ? !node.Lazy : node.Lazy;
        int start = next;
        int maximum = node.Maximum ?? int.MaxValue;
        if (node.Maximum is null)
        {
            start = CompileStarReversed(node.Child, start, options, lazy);
            maximum = node.Minimum;
        }

        for (int count = maximum; count > node.Minimum; count--)
        {
            start = CompileOptionalReversed(node.Child, start, options, lazy);
        }

        for (int count = 0; count < node.Minimum; count++)
        {
            start = CompileNodeReversed(node.Child, start, options);
        }

        return start;
    }

    private int CompileOptional(RegexSyntaxNode child, int next, RegexCompileOptions options, bool lazy)
    {
        int childStart = CompileNode(child, next, options);
        return lazy ? AddSplit(next, childStart) : AddSplit(childStart, next);
    }

    private int CompileOptionalReversed(RegexSyntaxNode child, int next, RegexCompileOptions options, bool lazy)
    {
        int childStart = CompileNodeReversed(child, next, options);
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

    private int CompileStarReversed(RegexSyntaxNode child, int next, RegexCompileOptions options, bool lazy)
    {
        int split = states.Count;
        states.Add(CreateControlState(RegexNfaStateKind.Split, next: -1, alternative: -1));
        int childStart = CompileNodeReversed(child, split, options);
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

    private int CompileAtomReversed(RegexAtomNode node, int next, RegexCompileOptions options)
    {
        RegexSyntaxKind kind = ReverseAtomKind(node.Kind);
        ReadOnlyMemory<byte> value = kind == RegexSyntaxKind.Literal ? ReverseBytes(node.Value) : node.Value;
        RegexNfaStateKind stateKind = IsPredicate(kind) ? RegexNfaStateKind.Predicate : RegexNfaStateKind.Atom;
        int state = states.Count;
        states.Add(new RegexNfaState(
            stateKind,
            kind,
            value,
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

    private int AddUnanchoredPrefix(int next)
    {
        int split = states.Count;
        states.Add(CreateControlState(RegexNfaStateKind.Split, next: -1, alternative: -1));
        int any = states.Count;
        states.Add(new RegexNfaState(
            RegexNfaStateKind.Atom,
            RegexSyntaxKind.AnyClass,
            ReadOnlyMemory<byte>.Empty,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: true,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: false,
            split,
            alternative: -1));
        states[split] = CreateControlState(RegexNfaStateKind.LazyLoopSplit, next, any);
        return split;
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

    private int AddCapture(RegexNfaStateKind kind, int captureIndex, int next)
    {
        if (captureIndex <= 0 || captureIndex > captureCount)
        {
            throw new InvalidOperationException("Capture index is outside the compiled capture range.");
        }

        int state = states.Count;
        states.Add(new RegexNfaState(
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
            alternative: -1,
            captureIndex));
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
        if (!rootUtf8)
        {
            return false;
        }

        for (int index = 0; index < states.Count; index++)
        {
            RegexNfaState state = states[index];
            if (state.Kind is not (RegexNfaStateKind.Atom or RegexNfaStateKind.Predicate))
            {
                continue;
            }

            if (state.Utf8)
            {
                return true;
            }
        }

        return !sawUtf8Disabled;
    }

    private static bool IsPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
    }

    private static RegexSyntaxKind ReverseAtomKind(RegexSyntaxKind kind)
    {
        return kind switch
        {
            RegexSyntaxKind.StartAnchor => RegexSyntaxKind.EndAnchor,
            RegexSyntaxKind.EndAnchor => RegexSyntaxKind.StartAnchor,
            RegexSyntaxKind.AbsoluteStartAnchor => RegexSyntaxKind.AbsoluteEndAnchor,
            RegexSyntaxKind.AbsoluteEndAnchor => RegexSyntaxKind.AbsoluteStartAnchor,
            RegexSyntaxKind.WordStartBoundary => RegexSyntaxKind.WordEndBoundary,
            RegexSyntaxKind.WordEndBoundary => RegexSyntaxKind.WordStartBoundary,
            RegexSyntaxKind.WordStartHalfBoundary => RegexSyntaxKind.WordEndHalfBoundary,
            RegexSyntaxKind.WordEndHalfBoundary => RegexSyntaxKind.WordStartHalfBoundary,
            _ => kind,
        };
    }

    private static ReadOnlyMemory<byte> ReverseBytes(ReadOnlyMemory<byte> value)
    {
        if (value.Length <= 1)
        {
            return value;
        }

        byte[] reversed = value.ToArray();
        Array.Reverse(reversed);
        return reversed;
    }

}
