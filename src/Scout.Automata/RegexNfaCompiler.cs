namespace Scout;

/// <summary>
/// Compiles parsed regex syntax into forward and reversed Thompson NFAs.
/// </summary>
/// <param name="includeCaptures">Whether to emit capture boundary states.</param>
/// <param name="captureCount">The number of capture groups in the syntax tree.</param>
/// <param name="utf8ByteTrieCache">The optional cache shared by UTF-8 byte-trie compilation.</param>
/// <param name="expandUtf8Atoms">Whether to expand Unicode scalar atoms into UTF-8 byte states.</param>
/// <param name="constructionBudget">The optional shared retained-NFA construction budget.</param>
internal sealed class RegexNfaCompiler(
    bool includeCaptures = false,
    int captureCount = 0,
    Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache = null,
    bool expandUtf8Atoms = true,
    RegexNfaConstructionBudget? constructionBudget = null)
{
    private readonly List<RegexNfaState> _states = [];
    private readonly Dictionary<string, RegexUtf8ByteTrie> _utf8ByteTrieCache = utf8ByteTrieCache ?? [];
    private readonly Dictionary<RegexNfaAtomCacheKey, int> _atomStateCache = [];
    private readonly Dictionary<RegexNfaByteClassCacheKey, int> _byteClassStateCache = [];
    private readonly Dictionary<RegexNfaControlCacheKey, int> _controlStateCache = [];
    private readonly Dictionary<RegexNfaSparseCacheKey, int> _sparseStateCache = [];
    private readonly Dictionary<(RegexUtf8ByteTrie Trie, int Next), int> _utf8ByteTrieStateCache = [];
    private Dictionary<(RegexScalarAtomPlan Plan, bool Reversed), RegexUtf8ByteTrie?>? _authoritativeUtf8ByteTrieCache;
    private RegexScalarAtomPlanCache? _scalarAtomPlanCache;
    private HashSet<RegexScalarRange[]>? _reservedScalarRangePayloads;
    private readonly bool _includeCaptures = includeCaptures;
    private readonly int _captureCount = captureCount;
    private readonly bool _expandUtf8Atoms = expandUtf8Atoms;
    private readonly RegexNfaConstructionBudget? _constructionBudget = constructionBudget;
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
    /// Attempts to compile a syntax tree with a lazy unanchored prefix under a shared budget.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <param name="constructionBudget">The shared forward-and-reverse construction budget.</param>
    /// <param name="nfa">Receives the compiled NFA when successful.</param>
    /// <returns><see langword="true" /> when construction remained within budget.</returns>
    internal static bool TryCompileUnanchored(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        RegexNfaConstructionBudget constructionBudget,
        out RegexNfa? nfa)
    {
        ulong checkpoint = constructionBudget.CreateCheckpoint();
        try
        {
            var compiler = new RegexNfaCompiler(constructionBudget: constructionBudget);
            int accept = compiler.AddAccept();
            int patternStart = compiler.CompileNode(root, accept, options);
            int start = compiler.AddUnanchoredPrefix(patternStart);
            nfa = new RegexNfa(
                compiler._states,
                start,
                compiler.RequiresUtf8SearchBoundary(options.Utf8));
            return true;
        }
        catch (InsufficientMemoryException)
        {
            constructionBudget.Restore(checkpoint);
            nfa = null;
            return false;
        }
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
    /// Attempts to compile a syntax tree in reverse under a shared construction budget.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <param name="constructionBudget">The shared forward-and-reverse construction budget.</param>
    /// <param name="nfa">Receives the compiled reverse NFA when successful.</param>
    /// <returns><see langword="true" /> when construction remained within budget.</returns>
    internal static bool TryCompileReversed(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        RegexNfaConstructionBudget constructionBudget,
        out RegexNfa? nfa)
    {
        ulong checkpoint = constructionBudget.CreateCheckpoint();
        try
        {
            var compiler = new RegexNfaCompiler(constructionBudget: constructionBudget);
            int accept = compiler.AddAccept();
            int start = compiler.CompileNodeReversed(root, accept, options);
            nfa = new RegexNfa(
                compiler._states,
                start,
                compiler.RequiresUtf8SearchBoundary(options.Utf8));
            return true;
        }
        catch (InsufficientMemoryException)
        {
            constructionBudget.Restore(checkpoint);
            nfa = null;
            return false;
        }
    }

    /// <summary>
    /// Computes conservative compiler-grounded state upper bounds for paired unanchored NFAs.
    /// </summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="options">The regex compilation options.</param>
    /// <returns>The forward and reverse state-count upper bounds.</returns>
    internal static RegexNfaConstructionEstimate EstimateUnanchoredConstruction(
        RegexSyntaxNode root,
        RegexCompileOptions options)
    {
        var scalarAtomPlanCache = new RegexScalarAtomPlanCache();
        ulong forward = EstimateNodeStateCount(
            root,
            options,
            reversed: false,
            scalarAtomPlanCache);
        forward = RegexNfaConstructionBudget.SaturatingAdd(forward, 3);
        ulong reverse = EstimateNodeStateCount(
            root,
            options,
            reversed: true,
            scalarAtomPlanCache);
        reverse = RegexNfaConstructionBudget.SaturatingAdd(reverse, 1);
        return new RegexNfaConstructionEstimate(forward, reverse);
    }

    private static ulong EstimateNodeStateCount(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        bool reversed,
        RegexScalarAtomPlanCache scalarAtomPlanCache)
    {
        return node switch
        {
            RegexEmptyNode => 0,
            RegexSequenceNode sequence => EstimateSequenceStateCount(
                sequence,
                options,
                reversed,
                scalarAtomPlanCache),
            RegexAlternationNode alternation => EstimateAlternationStateCount(
                alternation,
                options,
                reversed,
                scalarAtomPlanCache),
            RegexGroupNode group => EstimateNodeStateCount(
                group.Child,
                options.Apply(group.EnabledFlags, group.DisabledFlags),
                reversed,
                scalarAtomPlanCache),
            RegexRepetitionNode repetition => EstimateRepetitionStateCount(
                repetition,
                options,
                reversed,
                scalarAtomPlanCache),
            RegexInlineFlagsNode => 0,
            RegexAtomNode atom => EstimateAtomStateCount(
                atom,
                options,
                reversed,
                scalarAtomPlanCache),
            _ => 0,
        };
    }

    private static ulong EstimateSequenceStateCount(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        bool reversed,
        RegexScalarAtomPlanCache scalarAtomPlanCache)
    {
        ulong count = 0;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            count = RegexNfaConstructionBudget.SaturatingAdd(
                count,
                EstimateNodeStateCount(
                    child,
                    currentOptions,
                    reversed,
                    scalarAtomPlanCache));
        }

        return count;
    }

    private static ulong EstimateAlternationStateCount(
        RegexAlternationNode alternation,
        RegexCompileOptions options,
        bool reversed,
        RegexScalarAtomPlanCache scalarAtomPlanCache)
    {
        ulong count = alternation.Alternatives.Count > 0
            ? (ulong)alternation.Alternatives.Count - 1
            : 0;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            count = RegexNfaConstructionBudget.SaturatingAdd(
                count,
                EstimateNodeStateCount(
                    alternation.Alternatives[index],
                    options,
                    reversed,
                    scalarAtomPlanCache));
        }

        return count;
    }

    private static ulong EstimateRepetitionStateCount(
        RegexRepetitionNode repetition,
        RegexCompileOptions options,
        bool reversed,
        RegexScalarAtomPlanCache scalarAtomPlanCache)
    {
        ulong childCount = EstimateNodeStateCount(
            repetition.Child,
            options,
            reversed,
            scalarAtomPlanCache);
        if (!repetition.Maximum.HasValue)
        {
            ulong copies = RegexNfaConstructionBudget.SaturatingAdd(
                (ulong)repetition.Minimum,
                1);
            return RegexNfaConstructionBudget.SaturatingAdd(
                RegexNfaConstructionBudget.SaturatingMultiply(childCount, copies),
                1);
        }

        ulong maximum = (ulong)repetition.Maximum.Value;
        ulong optionalCopies = maximum - (ulong)repetition.Minimum;
        return RegexNfaConstructionBudget.SaturatingAdd(
            RegexNfaConstructionBudget.SaturatingMultiply(childCount, maximum),
            optionalCopies);
    }

    private static ulong EstimateAtomStateCount(
        RegexAtomNode atom,
        RegexCompileOptions options,
        bool reversed,
        RegexScalarAtomPlanCache scalarAtomPlanCache)
    {
        RegexSyntaxKind kind = reversed ? ReverseAtomKind(atom.Kind) : atom.Kind;
        bool authoritative = HasAuthoritativeScalarSemantics(atom);
        if (!authoritative &&
            !RegexByteClass.RequiresUtf8ScalarMatch(
                kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return 1;
        }

        if (!authoritative &&
            RegexUtf8ByteCompiler.TryGetSharedTrie(
                kind,
                atom.Value.Span,
                options,
                reversed,
                out RegexUtf8ByteTrie? sharedTrie))
        {
            return CountUtf8TrieStates(sharedTrie!);
        }

        IReadOnlyList<RegexScalarRange> ranges;
        if (authoritative)
        {
            if (!scalarAtomPlanCache.TryGet(atom, options, out RegexScalarAtomPlan? plan))
            {
                return 1;
            }

            ranges = plan!.Ranges;
        }
        else if (RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(
                     atom,
                     options,
                     out List<RegexScalarRange> builtRanges))
        {
            ranges = builtRanges;
        }
        else
        {
            return 1;
        }

        if (RegexUtf8ByteCompiler.CanLowerToAsciiByteClass(ranges))
        {
            return 1;
        }

        ulong count = 0;
        int AddByteClass(ReadOnlySpan<byte> value, int next)
        {
            count = RegexNfaConstructionBudget.SaturatingAdd(count, 1);
            return count >= int.MaxValue ? int.MaxValue : (int)count;
        }

        int AddSplit(int first, int second)
        {
            count = RegexNfaConstructionBudget.SaturatingAdd(count, 1);
            return count >= int.MaxValue ? int.MaxValue : (int)count;
        }

        if (RegexUtf8ByteCompiler.TryCompileCompactFromRanges(
                ranges,
                reversed,
                next: -1,
                AddByteClass,
                AddSplit,
                out _))
        {
            return count;
        }

        if (RegexUtf8ByteCompiler.TryCompileRangeSequencesFromRanges(
                ranges,
                reversed,
                next: -1,
                AddByteClass,
                AddSplit,
                out _))
        {
            return count;
        }

        if (!RegexUtf8ByteCompiler.TryCreateFromRanges(ranges, reversed, out RegexUtf8ByteTrie? trie))
        {
            return 1;
        }

        return CountUtf8TrieStates(trie!);
    }

    private static ulong CountUtf8TrieStates(RegexUtf8ByteTrie trie)
    {
        int nextState = 1;
        ulong count = 0;
        Dictionary<RegexNfaControlCacheKey, int> controlStates = [];
        Dictionary<RegexNfaSparseCacheKey, int> sparseStates = [];

        int AddSplit(int first, int second)
        {
            var key = new RegexNfaControlCacheKey(RegexNfaStateKind.Split, first, second);
            if (controlStates.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int state = nextState++;
            controlStates.Add(key, state);
            count = RegexNfaConstructionBudget.SaturatingAdd(count, 1);
            return state;
        }

        int AddSparse(ReadOnlySpan<RegexNfaSparseTransition> transitions)
        {
            RegexNfaSparseTransition[] value = transitions.ToArray();
            var key = new RegexNfaSparseCacheKey(value);
            if (sparseStates.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int state = nextState++;
            sparseStates.Add(key, state);
            count = RegexNfaConstructionBudget.SaturatingAdd(count, 1);
            return state;
        }

        _ = trie.Compile(next: 0, AddSplit, AddSparse);
        return count;
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
        _constructionBudget?.ReserveState(payloadBytes: 0);
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
        _constructionBudget?.ReserveState(payloadBytes: 0);
        _states.Add(CreateControlState(RegexNfaStateKind.Split, next: -1, alternative: -1));
        int childStart = CompileNodeReversed(child, split, options);
        _states[split] = lazy
            ? CreateControlState(RegexNfaStateKind.LazyLoopSplit, next, childStart)
            : CreateControlState(RegexNfaStateKind.GreedyLoopSplit, childStart, next);
        return split;
    }

    private int CompileAtom(RegexAtomNode node, int next, RegexCompileOptions options)
    {
        if (TryCompileUtf8ByteAtom(node, node.Kind, next, options, reversed: false, out int utf8Start))
        {
            return utf8Start;
        }

        RegexScalarRange[]? scalarRanges = TryGetRetainedScalarRanges(node, options, out bool scalarRangesUseUtf8);
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
            alternative: -1,
            excludeLineTerminators: options.ExcludeLineTerminators,
            excludeCrLf: options.ExcludeCrLf,
            excludedLineTerminator: options.ExcludedLineTerminator,
            scalarRanges: scalarRanges,
            scalarRangesUseUtf8: scalarRangesUseUtf8);
    }

    private int CompileAtomReversed(RegexAtomNode node, int next, RegexCompileOptions options)
    {
        RegexSyntaxKind kind = ReverseAtomKind(node.Kind);
        if (TryCompileUtf8ByteAtom(node, kind, next, options, reversed: true, out int utf8Start))
        {
            return utf8Start;
        }

        RegexScalarRange[]? scalarRanges = TryGetRetainedScalarRanges(node, options, out bool scalarRangesUseUtf8);
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
            alternative: -1,
            excludeLineTerminators: options.ExcludeLineTerminators,
            excludeCrLf: options.ExcludeCrLf,
            excludedLineTerminator: options.ExcludedLineTerminator,
            scalarRanges: scalarRanges,
            scalarRangesUseUtf8: scalarRangesUseUtf8);
    }

    private RegexScalarRange[]? TryGetRetainedScalarRanges(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out bool useUtf8)
    {
        useUtf8 = false;
        if (!HasAuthoritativeScalarSemantics(atom))
        {
            return null;
        }

        if (!ScalarAtomPlanCache.TryGet(atom, options, out RegexScalarAtomPlan? plan))
        {
            return null;
        }

        useUtf8 = AuthoritativeScalarRangesUseUtf8(atom, options);
        return plan!.Ranges;
    }

    private static bool HasAuthoritativeScalarSemantics(RegexAtomNode atom)
    {
        return atom.CharacterClass is not null ||
            atom.UnicodeProperty is not null ||
            atom.Kind == RegexSyntaxKind.Literal &&
            atom.LiteralKind is RegexLiteralKind.HexFixed
                or RegexLiteralKind.HexBrace
                or RegexLiteralKind.UnicodeShort
                or RegexLiteralKind.UnicodeLong;
    }

    private static bool AuthoritativeScalarRangesUseUtf8(RegexAtomNode atom, RegexCompileOptions options)
    {
        if (atom.Kind == RegexSyntaxKind.Literal && atom.Scalar >= 0)
        {
            return options.Utf8 ||
                options.UnicodeClasses ||
                atom.LiteralKind != RegexLiteralKind.HexFixed;
        }

        return options.Utf8 || options.UnicodeClasses;
    }

    private bool TryCompileUtf8ByteAtom(
        RegexAtomNode atom,
        RegexSyntaxKind kind,
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

        bool authoritative = HasAuthoritativeScalarSemantics(atom);
        bool scalarRangesUseUtf8 = AuthoritativeScalarRangesUseUtf8(atom, options);
        if (authoritative && !scalarRangesUseUtf8)
        {
            start = -1;
            return false;
        }

        RegexScalarAtomPlan? authoritativePlan = null;
        if (authoritative &&
            !ScalarAtomPlanCache.TryGet(atom, options, out authoritativePlan))
        {
            start = -1;
            return false;
        }

        if (!authoritative &&
            !RegexByteClass.RequiresUtf8ScalarMatch(
                kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            start = -1;
            return false;
        }

        int AddSourceByteClass(ReadOnlySpan<byte> ranges, int target) => AddByteClass(ranges, target, options.Utf8);
        if (!authoritative &&
            RegexUtf8ByteCompiler.TryGetSharedTrie(
                kind,
                atom.Value.Span,
                options,
                reversed,
                out RegexUtf8ByteTrie? sharedTrie))
        {
            _cacheStates = !_includeCaptures;
            start = CompileUtf8ByteTrie(sharedTrie!, next);
            return true;
        }

        string? key = authoritative
            ? null
            : RegexUtf8ByteCompiler.CreateCacheKey(kind, atom.Value.Span, options, reversed);
        RegexUtf8ByteTrie? trie;
        if (key is not null && _utf8ByteTrieCache.TryGetValue(key, out trie))
        {
            _cacheStates = !_includeCaptures;
            start = CompileUtf8ByteTrie(trie, next);
            return true;
        }

        IReadOnlyList<RegexScalarRange> ranges;
        byte[]? asciiByteRanges;
        if (authoritative)
        {
            ranges = authoritativePlan!.Ranges;
            asciiByteRanges = authoritativePlan.AsciiByteRanges;
        }
        else if (RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(
                     atom,
                     options,
                     out List<RegexScalarRange> builtRanges))
        {
            ranges = builtRanges;
            asciiByteRanges = RegexUtf8ByteCompiler.TryGetAsciiByteRanges(
                builtRanges,
                out byte[] builtAsciiByteRanges)
                    ? builtAsciiByteRanges
                    : null;
        }
        else
        {
            start = -1;
            return false;
        }

        if (asciiByteRanges is not null)
        {
            start = AddSourceByteClass(asciiByteRanges, next);
            return true;
        }

        if (RegexUtf8ByteCompiler.TryCompileCompactFromRanges(ranges, reversed, next, AddSourceByteClass, AddSplit, out start))
        {
            return true;
        }

        if (RegexUtf8ByteCompiler.TryCompileRangeSequencesFromRanges(ranges, reversed, next, AddSourceByteClass, AddSplit, out start))
        {
            return true;
        }

        if (authoritative)
        {
            (RegexScalarAtomPlan Plan, bool Reversed) planKey = (authoritativePlan!, reversed);
            Dictionary<(RegexScalarAtomPlan Plan, bool Reversed), RegexUtf8ByteTrie?> trieCache =
                _authoritativeUtf8ByteTrieCache ??= [];
            if (!trieCache.TryGetValue(planKey, out trie))
            {
                _ = RegexUtf8ByteCompiler.TryCreateFromRanges(ranges, reversed, out trie);
                trieCache.Add(planKey, trie);
            }
        }
        else if (RegexUtf8ByteCompiler.TryCreateFromRanges(ranges, reversed, out trie))
        {
            _utf8ByteTrieCache.Add(key!, trie!);
        }
        else
        {
            trie = null;
        }

        if (trie is null)
        {
            start = -1;
            return false;
        }

        _cacheStates = !_includeCaptures;
        start = CompileUtf8ByteTrie(trie, next);
        return true;
    }

    private RegexScalarAtomPlanCache ScalarAtomPlanCache =>
        _scalarAtomPlanCache ??= new RegexScalarAtomPlanCache();

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
        _constructionBudget?.ReserveState(payloadBytes: 0);
        _constructionBudget?.ReserveState(payloadBytes: 0);
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
        _constructionBudget?.ReserveState(payloadBytes: 0);
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
            _constructionBudget?.ReserveState(payloadBytes: 2);
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

        ulong checkpoint = _constructionBudget?.CreateCheckpoint() ?? 0;
        _constructionBudget?.ReserveState((ulong)ranges.Length);
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
            alternative: -1,
            payloadReserved: true,
            reservationCheckpoint: checkpoint);
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
        int alternative,
        bool excludeLineTerminators = false,
        bool excludeCrLf = false,
        byte? excludedLineTerminator = null,
        RegexScalarRange[]? scalarRanges = null,
        bool scalarRangesUseUtf8 = false,
        bool payloadReserved = false,
        ulong reservationCheckpoint = 0)
    {
        byte effectiveExcludedLineTerminator = excludedLineTerminator ?? lineTerminator;
        ulong scalarPayloadBytes = _constructionBudget is not null &&
            scalarRanges is not null &&
            (_reservedScalarRangePayloads ??= new HashSet<RegexScalarRange[]>(
                ReferenceEqualityComparer.Instance)).Add(scalarRanges)
                ? RegexNfaConstructionBudget.SaturatingMultiply(
                    (ulong)scalarRanges.Length,
                    (ulong)(sizeof(int) * 2))
                : 0;
        ulong payloadBytes = RegexNfaConstructionBudget.SaturatingAdd(
            (ulong)value.Length,
            scalarPayloadBytes);
        if (_cacheStates && scalarRanges is null)
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
                alternative,
                excludeLineTerminators,
                excludeCrLf,
                effectiveExcludedLineTerminator);
            if (_atomStateCache.TryGetValue(key, out int existing))
            {
                if (payloadReserved)
                {
                    _constructionBudget?.Restore(reservationCheckpoint);
                }

                return existing;
            }

            int cachedState = _states.Count;
            if (!payloadReserved)
            {
                _constructionBudget?.ReserveState(payloadBytes);
            }

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
                alternative,
                excludeLineTerminators: excludeLineTerminators,
                excludeCrLf: excludeCrLf,
                excludedLineTerminator: effectiveExcludedLineTerminator,
                scalarRanges: scalarRanges,
                scalarRangesUseUtf8: scalarRangesUseUtf8));
            _atomStateCache.Add(key, cachedState);
            return cachedState;
        }

        int state = _states.Count;
        if (!payloadReserved)
        {
            _constructionBudget?.ReserveState(payloadBytes);
        }

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
            alternative,
            excludeLineTerminators: excludeLineTerminators,
            excludeCrLf: excludeCrLf,
            excludedLineTerminator: effectiveExcludedLineTerminator,
            scalarRanges: scalarRanges,
            scalarRangesUseUtf8: scalarRangesUseUtf8));
        return state;
    }

    private int AddSparse(ReadOnlySpan<RegexNfaSparseTransition> transitions)
    {
        ulong checkpoint = _constructionBudget?.CreateCheckpoint() ?? 0;
        _constructionBudget?.ReserveState(
            RegexNfaConstructionBudget.EstimateSparsePayloadBytes(transitions.Length));
        RegexNfaSparseTransition[] value = transitions.ToArray();
        RegexNfaSparseCacheKey key = default;
        if (_cacheStates)
        {
            key = new RegexNfaSparseCacheKey(value);
            if (_sparseStateCache.TryGetValue(key, out int existing))
            {
                _constructionBudget?.Restore(checkpoint);
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
        _constructionBudget?.ReserveState(payloadBytes: 0);
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
        _constructionBudget?.ReserveState(payloadBytes: 0);
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
