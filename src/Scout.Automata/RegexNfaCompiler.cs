namespace Scout;

/// <summary>
/// Compiles parsed regex syntax into forward and reversed Thompson NFAs.
/// </summary>
/// <param name="includeCaptures">Whether to emit capture boundary states.</param>
/// <param name="captureCount">The number of capture groups in the syntax tree.</param>
/// <param name="utf8ByteTrieCache">The optional cache shared by UTF-8 byte-trie compilation.</param>
/// <param name="expandUtf8Atoms">Whether to expand Unicode scalar atoms into UTF-8 byte states.</param>
internal sealed class RegexNfaCompiler(
    bool includeCaptures = false,
    int captureCount = 0,
    Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache = null,
    bool expandUtf8Atoms = true)
{
    private readonly List<RegexNfaState> _states = [];
    private readonly Dictionary<string, RegexUtf8ByteTrie> _utf8ByteTrieCache = utf8ByteTrieCache ?? [];
    private readonly Dictionary<RegexNfaAtomCacheKey, int> _atomStateCache = [];
    private readonly Dictionary<RegexNfaByteClassCacheKey, int> _byteClassStateCache = [];
    private readonly Dictionary<RegexNfaControlCacheKey, int> _controlStateCache = [];
    private readonly Dictionary<RegexNfaSparseCacheKey, int> _sparseStateCache = [];
    private readonly Dictionary<(RegexUtf8ByteTrie Trie, int Next), int> _utf8ByteTrieStateCache = [];
    private readonly bool _includeCaptures = includeCaptures;
    private readonly int _captureCount = captureCount;
    private readonly bool _expandUtf8Atoms = expandUtf8Atoms;
    private bool _cacheStates;
    private bool _sawUtf8Disabled;

    /// <summary>
    /// Compiles a syntax tree with the default regex options.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <returns>The compiled NFA.</returns>
    public static RegexNfa Compile(RegexSyntaxNode root)
    {
        return Compile(
            root,
            new RegexCompileOptions(caseInsensitive: false, swapGreed: false, multiLine: false, dotMatchesNewline: false));
    }

    /// <summary>
    /// Compiles a syntax tree with the supplied regex options.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <returns>The compiled NFA.</returns>
    public static RegexNfa Compile(RegexSyntaxNode root, RegexCompileOptions options)
    {
        return Compile(root, options, utf8ByteTrieCache: null);
    }

    /// <summary>
    /// Compiles a syntax tree, optionally reusing a UTF-8 byte-trie cache.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <param name="utf8ByteTrieCache">The optional UTF-8 byte-trie cache.</param>
    /// <returns>The compiled NFA.</returns>
    public static RegexNfa Compile(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache)
    {
        var compiler = new RegexNfaCompiler(utf8ByteTrieCache: utf8ByteTrieCache);
        int accept = compiler.AddAccept();
        int start = compiler.CompileNode(root, accept, options);
        return new RegexNfa(compiler._states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8));
    }

    /// <summary>
    /// Compiles a syntax tree while retaining compact Unicode scalar atoms.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <param name="utf8ByteTrieCache">The optional UTF-8 byte-trie cache.</param>
    /// <returns>The compiled NFA.</returns>
    public static RegexNfa CompileWithCompactScalarAtoms(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache)
    {
        var compiler = new RegexNfaCompiler(utf8ByteTrieCache: utf8ByteTrieCache, expandUtf8Atoms: false);
        int accept = compiler.AddAccept();
        int start = compiler.CompileNode(root, accept, options);
        return new RegexNfa(compiler._states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8));
    }

    /// <summary>
    /// Compiles a syntax tree with a lazy unanchored prefix.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <returns>The compiled NFA.</returns>
    public static RegexNfa CompileUnanchored(RegexSyntaxNode root, RegexCompileOptions options)
    {
        var compiler = new RegexNfaCompiler();
        int accept = compiler.AddAccept();
        int patternStart = compiler.CompileNode(root, accept, options);
        int start = compiler.AddUnanchoredPrefix(patternStart);
        return new RegexNfa(compiler._states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8));
    }

    /// <summary>
    /// Compiles a syntax tree for matching bytes in reverse order.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <returns>The compiled reversed NFA.</returns>
    public static RegexNfa CompileReversed(RegexSyntaxNode root, RegexCompileOptions options)
    {
        var compiler = new RegexNfaCompiler();
        int accept = compiler.AddAccept();
        int start = compiler.CompileNodeReversed(root, accept, options);
        return new RegexNfa(compiler._states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8));
    }

    /// <summary>
    /// Compiles a syntax tree with capture boundary states and compact scalar atoms.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <param name="captureCount">The number of capture groups in the syntax tree.</param>
    /// <returns>The compiled capture NFA.</returns>
    public static RegexNfa CompileCaptures(RegexSyntaxNode root, RegexCompileOptions options, int captureCount)
    {
        var compiler = new RegexNfaCompiler(includeCaptures: true, captureCount, expandUtf8Atoms: false);
        int accept = compiler.AddAccept();
        int start = compiler.CompileNode(root, accept, options);
        return new RegexNfa(compiler._states, start, compiler.RequiresUtf8SearchBoundary(options.Utf8), captureCount);
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
        if (!HasInlineFlags(node))
        {
            int directStart = next;
            for (int index = node.Nodes.Count - 1; index >= 0; index--)
            {
                directStart = CompileNode(node.Nodes[index], directStart, options);
            }

            return directStart;
        }

        var nodes = new List<RegexSyntaxNode>();
        var nodeOptions = new List<RegexCompileOptions>();
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                RegexCompileOptions nextOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                _sawUtf8Disabled |= currentOptions.Utf8 && !nextOptions.Utf8;
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
        if (!HasInlineFlags(node))
        {
            int directStart = next;
            for (int index = 0; index < node.Nodes.Count; index++)
            {
                directStart = CompileNodeReversed(node.Nodes[index], directStart, options);
            }

            return directStart;
        }

        var nodes = new List<RegexSyntaxNode>();
        var nodeOptions = new List<RegexCompileOptions>();
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                RegexCompileOptions nextOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                _sawUtf8Disabled |= currentOptions.Utf8 && !nextOptions.Utf8;
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

    private static bool HasInlineFlags(RegexSequenceNode node)
    {
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            if (node.Nodes[index] is RegexInlineFlagsNode)
            {
                return true;
            }
        }

        return false;
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
        _sawUtf8Disabled |= options.Utf8 && !groupOptions.Utf8;
        if (!_includeCaptures || node.Kind != RegexSyntaxKind.CapturingGroup)
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
        _sawUtf8Disabled |= options.Utf8 && !groupOptions.Utf8;
        return CompileNodeReversed(node.Child, next, groupOptions);
    }

    private int CompileRepetition(RegexRepetitionNode node, int next, RegexCompileOptions options)
    {
        bool lazy = options.SwapGreed ? !node.Lazy : node.Lazy;
        int start = next;
        if (node.Maximum is null)
        {
            start = CompileStar(node.Child, start, options, lazy);
        }
        else
        {
            // Consuming branches chain through later copies, but every skip targets one exit.
            // Chaining skips through the remaining optional copies needlessly expands closures.
            int commonExit = next;
            for (int count = node.Maximum.Value; count > node.Minimum; count--)
            {
                int childStart = CompileNode(node.Child, start, options);
                start = lazy ? AddSplit(commonExit, childStart) : AddSplit(childStart, commonExit);
            }
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
        if (node.Maximum is null)
        {
            start = CompileStarReversed(node.Child, start, options, lazy);
        }
        else
        {
            // Keep the reversed topology identical to the forward common-exit construction.
            int commonExit = next;
            for (int count = node.Maximum.Value; count > node.Minimum; count--)
            {
                int childStart = CompileNodeReversed(node.Child, start, options);
                start = lazy ? AddSplit(commonExit, childStart) : AddSplit(childStart, commonExit);
            }
        }

        for (int count = 0; count < node.Minimum; count++)
        {
            start = CompileNodeReversed(node.Child, start, options);
        }

        return start;
    }

    private int CompileStar(RegexSyntaxNode child, int next, RegexCompileOptions options, bool lazy)
    {
        int split = _states.Count;
        _states.Add(CreateControlState(RegexNfaStateKind.Split, next: -1, alternative: -1));
        int childStart = CompileNode(child, split, options);
        _states[split] = lazy
            ? CreateControlState(RegexNfaStateKind.LazyLoopSplit, next, childStart)
            : CreateControlState(RegexNfaStateKind.GreedyLoopSplit, childStart, next);
        return split;
    }

    private int CompileStarReversed(RegexSyntaxNode child, int next, RegexCompileOptions options, bool lazy)
    {
        int split = _states.Count;
        _states.Add(CreateControlState(RegexNfaStateKind.Split, next: -1, alternative: -1));
        int childStart = CompileNodeReversed(child, split, options);
        _states[split] = lazy
            ? CreateControlState(RegexNfaStateKind.LazyLoopSplit, next, childStart)
            : CreateControlState(RegexNfaStateKind.GreedyLoopSplit, childStart, next);
        return split;
    }

    private int CompileAtom(RegexAtomNode node, int next, RegexCompileOptions options)
    {
        if (TryCompileUtf8ByteAtom(node.Kind, node.Value.Span, next, options, reversed: false, out int utf8Start))
        {
            return utf8Start;
        }

        RegexNfaStateKind stateKind = IsPredicate(node.Kind) ? RegexNfaStateKind.Predicate : RegexNfaStateKind.Atom;
        return AddAtomState(
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
            alternative: -1);
    }

    private int CompileAtomReversed(RegexAtomNode node, int next, RegexCompileOptions options)
    {
        RegexSyntaxKind kind = ReverseAtomKind(node.Kind);
        if (TryCompileUtf8ByteAtom(kind, node.Value.Span, next, options, reversed: true, out int utf8Start))
        {
            return utf8Start;
        }

        ReadOnlyMemory<byte> value = kind == RegexSyntaxKind.Literal ? ReverseBytes(node.Value) : node.Value;
        RegexNfaStateKind stateKind = IsPredicate(kind) ? RegexNfaStateKind.Predicate : RegexNfaStateKind.Atom;
        return AddAtomState(
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
            alternative: -1);
    }

    private bool TryCompileUtf8ByteAtom(
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> value,
        int next,
        RegexCompileOptions options,
        bool reversed,
        out int start)
    {
        if (!_expandUtf8Atoms)
        {
            start = -1;
            return false;
        }

        if (!RegexByteClass.RequiresUtf8ScalarMatch(kind, value, options.Utf8, options.CaseInsensitive, options.UnicodeClasses))
        {
            start = -1;
            return false;
        }

        int AddSourceByteClass(ReadOnlySpan<byte> ranges, int target) => AddByteClass(ranges, target, options.Utf8);
        if (RegexUtf8ByteCompiler.TryGetSharedTrie(kind, value, options, reversed, out RegexUtf8ByteTrie? sharedTrie))
        {
            _cacheStates = !_includeCaptures;
            start = CompileUtf8ByteTrie(sharedTrie!, next);
            return true;
        }

        string key = RegexUtf8ByteCompiler.CreateCacheKey(kind, value, options, reversed);
        if (_utf8ByteTrieCache.TryGetValue(key, out RegexUtf8ByteTrie? trie))
        {
            _cacheStates = !_includeCaptures;
            start = CompileUtf8ByteTrie(trie, next);
            return true;
        }

        if (!RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(kind, value, options, out List<RegexScalarRange> ranges))
        {
            start = -1;
            return false;
        }

        if (RegexUtf8ByteCompiler.TryCompileCompactFromRanges(ranges, reversed, next, AddSourceByteClass, AddSplit, out start))
        {
            return true;
        }

        if (RegexUtf8ByteCompiler.TryCompileRangeSequencesFromRanges(ranges, reversed, next, AddSourceByteClass, AddSplit, out start))
        {
            return true;
        }

        if (!RegexUtf8ByteCompiler.TryCreateFromRanges(ranges, reversed, out trie))
        {
            start = -1;
            return false;
        }

        _utf8ByteTrieCache.Add(key, trie!);
        _cacheStates = !_includeCaptures;
        start = CompileUtf8ByteTrie(trie!, next);
        return true;
    }

    private int CompileUtf8ByteTrie(RegexUtf8ByteTrie trie, int next)
    {
        if (_cacheStates && _utf8ByteTrieStateCache.TryGetValue((trie, next), out int existing))
        {
            return existing;
        }

        int start = trie.Compile(next, AddSplit, AddSparse);
        if (_cacheStates)
        {
            _utf8ByteTrieStateCache.Add((trie, next), start);
        }

        return start;
    }

    private int AddUnanchoredPrefix(int next)
    {
        int split = _states.Count;
        _states.Add(CreateControlState(RegexNfaStateKind.Split, next: -1, alternative: -1));
        int any = _states.Count;
        _states.Add(new RegexNfaState(
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
        _states[split] = CreateControlState(RegexNfaStateKind.LazyLoopSplit, next, any);
        return split;
    }

    private int AddSplit(int first, int second)
    {
        var key = new RegexNfaControlCacheKey(RegexNfaStateKind.Split, first, second);
        if (_cacheStates && _controlStateCache.TryGetValue(key, out int existing))
        {
            return existing;
        }

        int state = _states.Count;
        _states.Add(CreateControlState(RegexNfaStateKind.Split, first, second));
        if (_cacheStates)
        {
            _controlStateCache.Add(key, state);
        }

        return state;
    }

    private int AddByteClass(ReadOnlySpan<byte> ranges, int next)
    {
        return AddByteClass(ranges, next, utf8: false);
    }

    private int AddByteClass(ReadOnlySpan<byte> ranges, int next, bool utf8)
    {
        if (_cacheStates && ranges.Length == 2)
        {
            var key = new RegexNfaByteClassCacheKey(ranges[0], ranges[1], utf8, next);
            if (_byteClassStateCache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int cachedState = _states.Count;
            _states.Add(new RegexNfaState(
                RegexNfaStateKind.Atom,
                RegexSyntaxKind.ByteClass,
                new byte[] { ranges[0], ranges[1] },
                caseInsensitive: false,
                multiLine: false,
                dotMatchesNewline: false,
                crlf: false,
                lineTerminator: (byte)'\n',
                utf8: utf8,
                unicodeClasses: false,
                next,
                alternative: -1));
            _byteClassStateCache.Add(key, cachedState);
            return cachedState;
        }

        byte[] value = ranges.ToArray();
        return AddAtomState(
            RegexNfaStateKind.Atom,
            RegexSyntaxKind.ByteClass,
            value,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: utf8,
            unicodeClasses: false,
            next,
            alternative: -1);
    }

    private int AddAtomState(
        RegexNfaStateKind stateKind,
        RegexSyntaxKind atomKind,
        ReadOnlyMemory<byte> value,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        bool utf8,
        bool unicodeClasses,
        int next,
        int alternative)
    {
        if (_cacheStates)
        {
            var key = RegexNfaAtomCacheKey.Create(
                stateKind,
                atomKind,
                value.Span,
                caseInsensitive,
                multiLine,
                dotMatchesNewline,
                crlf,
                lineTerminator,
                utf8,
                unicodeClasses,
                next,
                alternative);
            if (_atomStateCache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int cachedState = _states.Count;
            _states.Add(new RegexNfaState(
                stateKind,
                atomKind,
                value,
                caseInsensitive,
                multiLine,
                dotMatchesNewline,
                crlf,
                lineTerminator,
                utf8,
                unicodeClasses,
                next,
                alternative));
            _atomStateCache.Add(key, cachedState);
            return cachedState;
        }

        int state = _states.Count;
        _states.Add(new RegexNfaState(
            stateKind,
            atomKind,
            value,
            caseInsensitive,
            multiLine,
            dotMatchesNewline,
            crlf,
            lineTerminator,
            utf8,
            unicodeClasses,
            next,
            alternative));
        return state;
    }

    private int AddSparse(ReadOnlySpan<RegexNfaSparseTransition> transitions)
    {
        RegexNfaSparseTransition[] value = transitions.ToArray();
        RegexNfaSparseCacheKey key = default;
        if (_cacheStates)
        {
            key = new RegexNfaSparseCacheKey(value);
            if (_sparseStateCache.TryGetValue(key, out int existing))
            {
                return existing;
            }
        }

        int state = _states.Count;
        _states.Add(new RegexNfaState(
            RegexNfaStateKind.Sparse,
            RegexSyntaxKind.Empty,
            ReadOnlyMemory<byte>.Empty,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: false,
            next: -1,
            alternative: -1,
            sparseTransitions: value));
        if (_cacheStates)
        {
            _sparseStateCache.Add(key, state);
        }

        return state;
    }

    private int AddAccept()
    {
        int state = _states.Count;
        _states.Add(CreateControlState(RegexNfaStateKind.Accept, next: -1, alternative: -1));
        return state;
    }

    private int AddCapture(RegexNfaStateKind kind, int captureIndex, int next)
    {
        if (captureIndex <= 0 || captureIndex > _captureCount)
        {
            throw new InvalidOperationException("Capture index is outside the compiled capture range.");
        }

        int state = _states.Count;
        _states.Add(new RegexNfaState(
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

        for (int index = 0; index < _states.Count; index++)
        {
            RegexNfaState state = _states[index];
            if (state.Kind is not (RegexNfaStateKind.Atom or RegexNfaStateKind.Predicate))
            {
                continue;
            }

            if (state.Utf8)
            {
                return true;
            }
        }

        return !_sawUtf8Disabled;
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
