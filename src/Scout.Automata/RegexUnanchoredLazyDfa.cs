namespace Scout;

/// <summary>
/// Locates leftmost matches with paired forward-end and reverse-start lazy DFAs.
/// </summary>
/// <param name="forward">The unanchored forward DFA that locates a leftmost match end.</param>
/// <param name="reverse">The optional initialized reverse DFA.</param>
/// <param name="reverseFactory">The optional factory that lazily creates the reverse DFA.</param>
internal sealed class RegexUnanchoredLazyDfa(
    RegexLazyDfa forward,
    RegexLazyDfa? reverse,
    RegexUnanchoredLazyDfaFactory? reverseFactory)
{
    private readonly RegexLazyDfa _forward = forward;
    private RegexLazyDfa? _reverse = reverse;
    private readonly RegexUnanchoredLazyDfaFactory? _reverseFactory = reverseFactory;
    private long _runnerLeaseGeneration;
    private long _activeRunnerLease;

    /// <summary>
    /// Begins an exclusive pooled-runner lease and returns its monotonically increasing token.
    /// </summary>
    /// <returns>The nonzero token that identifies the new lease.</returns>
    internal long BeginRunnerLease()
    {
        long token;
        do
        {
            token = System.Threading.Interlocked.Increment(ref _runnerLeaseGeneration);
        }
        while (token == 0);

        System.Threading.Interlocked.Exchange(ref _activeRunnerLease, token);
        return token;
    }

    /// <summary>
    /// Determines whether a token still owns the active pooled-runner lease.
    /// </summary>
    /// <param name="token">The lease token to verify.</param>
    /// <returns><see langword="true" /> when the token still owns this runner.</returns>
    internal bool IsRunnerLeaseActive(long token)
    {
        return token != 0 &&
            System.Threading.Volatile.Read(ref _activeRunnerLease) == token;
    }

    /// <summary>
    /// Atomically ends a pooled-runner lease when its token still identifies the active lease.
    /// </summary>
    /// <param name="token">The token returned by <see cref="BeginRunnerLease" />.</param>
    /// <returns><see langword="true" /> only for the first successful end of the active lease.</returns>
    internal bool TryEndRunnerLease(long token)
    {
        return token != 0 &&
            System.Threading.Interlocked.CompareExchange(ref _activeRunnerLease, 0, token) == token;
    }

    /// <summary>
    /// Attempts to create paired unanchored DFAs by compiling both syntax directions.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
    /// <param name="dfa">Receives the paired DFA when successful.</param>
    /// <returns><see langword="true" /> when both directions are DFA-compatible.</returns>
    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ulong dfaSizeLimit,
        out RegexUnanchoredLazyDfa? dfa)
    {
        dfa = null;
        if (!CanCompileSyntax(root, options))
        {
            return false;
        }

        RegexNfaConstructionEstimate estimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(root, options);
        if (!estimate.Fits(dfaSizeLimit))
        {
            return false;
        }

        var constructionBudget = new RegexNfaConstructionBudget(dfaSizeLimit);
        if (!RegexNfaCompiler.TryCompileUnanchored(
                root,
                options,
                constructionBudget,
                out RegexNfa? forwardNfa) ||
            !RegexNfaCompiler.TryCompileReversed(
                root,
                options,
                constructionBudget,
                out RegexNfa? reverseNfa))
        {
            return false;
        }

        if (!RegexDfaOperations.CanCompile(forwardNfa!) ||
            !RegexDfaOperations.CanCompile(reverseNfa!) ||
            !constructionBudget.CanRetain(forwardNfa!, reverseNfa!))
        {
            return false;
        }

        return TryCreateFromCompatibleNfas(forwardNfa!, reverseNfa!, dfaSizeLimit, out dfa);
    }

    /// <summary>
    /// Attempts to create paired unanchored DFAs while reusing an authoritative forward NFA.
    /// </summary>
    /// <param name="nfa">The authoritative anchored forward NFA.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
    /// <param name="dfa">Receives the paired DFA when successful.</param>
    /// <returns><see langword="true" /> when both directions are DFA-compatible.</returns>
    public static bool TryCreate(
        RegexNfa nfa,
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ulong dfaSizeLimit,
        out RegexUnanchoredLazyDfa? dfa)
    {
        dfa = null;
        RegexNfaConstructionEstimate estimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(root, options);
        if (!estimate.Fits(dfaSizeLimit))
        {
            return false;
        }

        var constructionBudget = new RegexNfaConstructionBudget(dfaSizeLimit);
        if (!TryCompileForwardNfa(
                nfa,
                root,
                options,
                constructionBudget,
                out RegexNfa? forwardNfa) ||
            !TryCompileReverseNfa(
                root,
                options,
                constructionBudget,
                out RegexNfa? reverseNfa) ||
            !constructionBudget.CanRetain(forwardNfa!, reverseNfa!))
        {
            return false;
        }

        return TryCreateFromCompatibleNfas(forwardNfa!, reverseNfa!, dfaSizeLimit, out dfa);
    }

    /// <summary>
    /// Compiles the immutable forward and reverse NFAs shared by unanchored lazy-DFA runners.
    /// </summary>
    /// <param name="nfa">The authoritative anchored forward NFA.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="forwardNfa">Receives the unanchored forward NFA when successful.</param>
    /// <param name="reverseNfa">Receives the reverse NFA when successful.</param>
    /// <returns><see langword="true" /> when both NFAs are compatible with lazy DFA execution.</returns>
    internal static bool TryCompileNfas(
        RegexNfa nfa,
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexNfa? forwardNfa,
        out RegexNfa? reverseNfa)
    {
        reverseNfa = null;
        return TryCompileForwardNfa(nfa, root, options, out forwardNfa) &&
            TryCompileReverseNfa(root, options, out reverseNfa);
    }

    /// <summary>
    /// Compiles the immutable unanchored forward NFA independently of reverse reconstruction.
    /// </summary>
    /// <param name="nfa">The authoritative anchored forward NFA.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="forwardNfa">Receives the unanchored forward NFA when successful.</param>
    /// <returns><see langword="true" /> when the forward NFA is compatible with lazy DFA execution.</returns>
    internal static bool TryCompileForwardNfa(
        RegexNfa nfa,
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexNfa? forwardNfa)
    {
        forwardNfa = null;
        if (!CanCompileForwardNfa(nfa, root, options))
        {
            return false;
        }

        RegexNfa candidateForwardNfa = CreateUnanchoredForwardNfa(nfa);
        if (!RegexDfaOperations.CanCompile(candidateForwardNfa))
        {
            return false;
        }

        forwardNfa = candidateForwardNfa;
        return true;
    }

    /// <summary>
    /// Compiles the immutable unanchored forward NFA under a shared retained-graph budget.
    /// </summary>
    /// <param name="nfa">The authoritative anchored forward NFA.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="constructionBudget">The shared forward-and-reverse construction budget.</param>
    /// <param name="forwardNfa">Receives the unanchored forward NFA when successful.</param>
    /// <returns><see langword="true" /> when the forward graph is compatible and fits.</returns>
    internal static bool TryCompileForwardNfa(
        RegexNfa nfa,
        RegexSyntaxNode root,
        RegexCompileOptions options,
        RegexNfaConstructionBudget constructionBudget,
        out RegexNfa? forwardNfa)
    {
        forwardNfa = null;
        if (!CanCompileForwardNfa(nfa, root, options))
        {
            return false;
        }

        ulong checkpoint = constructionBudget.CreateCheckpoint();
        try
        {
            constructionBudget.ReserveRetainedNfa(nfa);
            constructionBudget.ReserveState(payloadBytes: 0);
            constructionBudget.ReserveState(payloadBytes: 0);
            RegexNfa candidateForwardNfa = CreateUnanchoredForwardNfa(nfa);
            if (!RegexDfaOperations.CanCompile(candidateForwardNfa))
            {
                constructionBudget.Restore(checkpoint);
                return false;
            }

            forwardNfa = candidateForwardNfa;
            return true;
        }
        catch (InsufficientMemoryException)
        {
            constructionBudget.Restore(checkpoint);
            return false;
        }
    }

    /// <summary>
    /// Determines whether an anchored NFA can be extended with the generic unanchored search prefix.
    /// </summary>
    /// <param name="nfa">The authoritative anchored forward NFA.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <returns><see langword="true" /> when forward lazy-DFA execution is compatible.</returns>
    internal static bool CanCompileForwardNfa(
        RegexNfa nfa,
        RegexSyntaxNode root,
        RegexCompileOptions options)
    {
        return CanCompileSyntax(root, options) &&
            RegexDfaOperations.CanCompile(nfa);
    }

    /// <summary>
    /// Determines whether syntax can be compiled into an unanchored byte DFA without first
    /// materializing its expanded byte NFA.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <returns><see langword="true" /> when the syntax is compatible with byte-DFA execution.</returns>
    internal static bool CanCompileSyntax(
        RegexSyntaxNode root,
        RegexCompileOptions options)
    {
        return !options.Utf8 &&
            !CanMatchEmpty(root) &&
            !HasNullableRepetition(root) &&
            !ContainsDfaUnsupportedPredicate(root);
    }

    /// <summary>
    /// Determines whether syntax is compatible and its saturated scalar-expansion estimate
    /// fits the shared forward-and-reverse NFA construction budget.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
    /// <returns><see langword="true" /> when expanded NFA construction may fit the budget.</returns>
    internal static bool CanCompileExpandedNfaWithinBudget(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ulong dfaSizeLimit)
    {
        return CanCompileSyntax(root, options) &&
            RegexNfaCompiler.EstimateUnanchoredConstruction(root, options).Fits(dfaSizeLimit);
    }

    /// <summary>
    /// Determines whether syntax is compatible and its saturated scalar-expansion estimate
    /// fits the forward NFA construction budget independently of reverse reconstruction.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
    /// <returns><see langword="true" /> when expanded forward-NFA construction may fit the budget.</returns>
    internal static bool CanCompileExpandedForwardNfaWithinBudget(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ulong dfaSizeLimit)
    {
        return CanCompileSyntax(root, options) &&
            RegexNfaCompiler.EstimateUnanchoredConstruction(root, options).ForwardFits(dfaSizeLimit);
    }

    /// <summary>
    /// Compiles the immutable reverse NFA used only when a match start must be reconstructed.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="reverseNfa">Receives the reverse NFA when successful.</param>
    /// <returns><see langword="true" /> when the reverse NFA is compatible with lazy DFA execution.</returns>
    internal static bool TryCompileReverseNfa(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexNfa? reverseNfa)
    {
        reverseNfa = null;
        if (!CanCompileSyntax(root, options))
        {
            return false;
        }

        RegexNfa candidateReverseNfa = RegexNfaCompiler.CompileReversed(root, options);
        if (!RegexDfaOperations.CanCompile(candidateReverseNfa))
        {
            return false;
        }

        reverseNfa = candidateReverseNfa;
        return true;
    }

    /// <summary>
    /// Compiles the immutable reverse NFA under a shared retained-graph budget.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="constructionBudget">The shared forward-and-reverse construction budget.</param>
    /// <param name="reverseNfa">Receives the reverse NFA when successful.</param>
    /// <returns><see langword="true" /> when the reverse graph is compatible and fits.</returns>
    internal static bool TryCompileReverseNfa(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        RegexNfaConstructionBudget constructionBudget,
        out RegexNfa? reverseNfa)
    {
        reverseNfa = null;
        if (!CanCompileSyntax(root, options) ||
            !RegexNfaCompiler.TryCompileReversed(
                root,
                options,
                constructionBudget,
                out RegexNfa? candidateReverseNfa) ||
            !RegexDfaOperations.CanCompile(candidateReverseNfa!))
        {
            return false;
        }

        reverseNfa = candidateReverseNfa;
        return true;
    }

    /// <summary>
    /// Attempts to create a mutable runner from compatible shared immutable forward and reverse NFAs.
    /// </summary>
    /// <param name="forwardNfa">The shared unanchored forward NFA.</param>
    /// <param name="reverseNfa">The shared reverse NFA.</param>
    /// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
    /// <param name="dfa">Receives the paired DFA when successful.</param>
    /// <returns><see langword="true" /> when both lazy-DFA start states fit within budget.</returns>
    internal static bool TryCreateFromCompatibleNfas(
        RegexNfa forwardNfa,
        RegexNfa reverseNfa,
        ulong dfaSizeLimit,
        out RegexUnanchoredLazyDfa? dfa)
    {
        dfa = null;
        if (!RegexLazyDfa.TryCreate(
                forwardNfa,
                dfaSizeLimit,
                leftmostPrune: true,
                out RegexLazyDfa? forwardDfa))
        {
            return false;
        }

        if (!RegexLazyDfa.TryCreate(
                reverseNfa,
                dfaSizeLimit,
                leftmostPrune: true,
                out RegexLazyDfa? reverseDfa))
        {
            return false;
        }

        dfa = new RegexUnanchoredLazyDfa(
            forwardDfa!,
            reverseDfa!,
            reverseFactory: null);
        return true;
    }

    /// <summary>
    /// Attempts to find the first leftmost match at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="match">Receives the leftmost match.</param>
    /// <param name="gaveUp">Receives whether either lazy DFA exhausted its cache budget.</param>
    /// <returns><see langword="true" /> when a match is found.</returns>
    public bool TryFind(ReadOnlySpan<byte> haystack, int startAt, out RegexMatch match, out bool gaveUp)
    {
        return TryFind(
            haystack,
            startAt,
            forwardReachabilityCache: null,
            reverseReachabilityCache: null,
            out match,
            out gaveUp);
    }

    /// <summary>
    /// Attempts to find the end of the first leftmost match without reconstructing its start.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="end">Receives the exclusive match end.</param>
    /// <param name="gaveUp">Receives whether the forward lazy DFA exhausted its cache budget.</param>
    /// <returns><see langword="true" /> when a match end is found.</returns>
    public bool TryFindEnd(ReadOnlySpan<byte> haystack, int startAt, out int end, out bool gaveUp)
    {
        return _forward.TryFindEnd(haystack, startAt, out end, out gaveUp);
    }

    /// <summary>
    /// Attempts to find the first leftmost match with reusable reachability caches.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="forwardReachabilityCache">Optional reusable forward reachability state.</param>
    /// <param name="reverseReachabilityCache">Optional reusable reverse reachability state.</param>
    /// <param name="match">Receives the leftmost match.</param>
    /// <param name="gaveUp">Receives whether either lazy DFA exhausted its cache budget.</param>
    /// <returns><see langword="true" /> when a match is found.</returns>
    public bool TryFind(
        ReadOnlySpan<byte> haystack,
        int startAt,
        Dictionary<(int State, int Position), bool>? forwardReachabilityCache,
        Dictionary<(int State, int Position), bool>? reverseReachabilityCache,
        out RegexMatch match,
        out bool gaveUp)
    {
        gaveUp = false;
        if (_reverse is null && _reverseFactory?.IsReverseUnavailable == true)
        {
            gaveUp = true;
            match = default;
            return false;
        }

        if (!_forward.TryFindEnd(haystack, startAt, forwardReachabilityCache, out int end, out bool forwardGaveUp) ||
            forwardGaveUp)
        {
            gaveUp = forwardGaveUp;
            match = default;
            return false;
        }

        RegexLazyDfa? reverseDfa = _reverse ??= _reverseFactory?.CreateReverseDfa();
        if (reverseDfa is null)
        {
            gaveUp = true;
            match = default;
            return false;
        }

        if (!reverseDfa.TryFindStartReverse(
                haystack,
                startAt,
                end,
                reverseReachabilityCache,
                out int start,
                out bool reverseGaveUp) ||
            reverseGaveUp)
        {
            gaveUp = true;
            match = default;
            return false;
        }

        match = new RegexMatch(start, end - start);
        return true;
    }

    /// <summary>
    /// Attempts to count non-overlapping matches at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="count">Receives the match count.</param>
    /// <returns><see langword="true" /> when the lazy DFA completes within budget.</returns>
    public bool TryCountMatches(ReadOnlySpan<byte> haystack, int startAt, out long count)
    {
        return TryIterateNonOverlapping(haystack, startAt, sumSpans: false, out count);
    }

    /// <summary>
    /// Attempts to sum non-overlapping match lengths at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="spanSum">Receives the sum of match lengths.</param>
    /// <returns><see langword="true" /> when the lazy DFA completes within budget.</returns>
    public bool TrySumMatchSpans(ReadOnlySpan<byte> haystack, int startAt, out long spanSum)
    {
        return TryIterateNonOverlapping(haystack, startAt, sumSpans: true, out spanSum);
    }

    private bool TryIterateNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
    {
        total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset <= haystack.Length)
        {
            if (!sumSpans)
            {
                bool found = _forward.TryFindEnd(
                    haystack,
                    offset,
                    out int end,
                    out bool forwardGaveUp);
                if (!found || forwardGaveUp)
                {
                    return !forwardGaveUp;
                }

                total++;
                offset = end;
                continue;
            }

            if (!TryFind(
                    haystack,
                    offset,
                    forwardReachabilityCache: null,
                    reverseReachabilityCache: null,
                    out RegexMatch match,
                    out bool gaveUp))
            {
                return !gaveUp;
            }

            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return true;
    }

    /// <summary>
    /// Creates an unanchored forward NFA by retaining the authoritative states and appending
    /// the standard lazy any-byte search prefix.
    /// </summary>
    internal static RegexNfa CreateUnanchoredForwardNfa(RegexNfa nfa)
    {
        var states = new RegexNfaState[nfa.States.Count + 2];
        for (int index = 0; index < nfa.States.Count; index++)
        {
            states[index] = nfa.States[index];
        }

        int split = nfa.States.Count;
        int any = split + 1;
        states[split] = new RegexNfaState(
            RegexNfaStateKind.LazyLoopSplit,
            RegexSyntaxKind.Empty,
            ReadOnlyMemory<byte>.Empty,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: false,
            nfa.StartState,
            any);
        states[any] = new RegexNfaState(
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
            alternative: -1);
        return new RegexNfa(states, split, nfa.Utf8, nfa.CaptureCount);
    }

    /// <summary>
    /// Reports whether a syntax subtree contains a zero-width predicate that byte DFA
    /// execution cannot compile.
    /// </summary>
    private static bool ContainsDfaUnsupportedPredicate(RegexSyntaxNode node)
    {
        return node switch
        {
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.StartAnchor
                or RegexSyntaxKind.EndAnchor
                or RegexSyntaxKind.AbsoluteStartAnchor
                or RegexSyntaxKind.AbsoluteEndAnchor
                or RegexSyntaxKind.WordBoundary
                or RegexSyntaxKind.NotWordBoundary
                or RegexSyntaxKind.WordStartBoundary
                or RegexSyntaxKind.WordEndBoundary
                or RegexSyntaxKind.WordStartHalfBoundary
                or RegexSyntaxKind.WordEndHalfBoundary,
            RegexGroupNode group => ContainsDfaUnsupportedPredicate(group.Child),
            RegexSequenceNode sequence => AnyContainsDfaUnsupportedPredicate(sequence.Nodes),
            RegexAlternationNode alternation => AnyContainsDfaUnsupportedPredicate(alternation.Alternatives),
            RegexRepetitionNode { Maximum: 0 } => false,
            RegexRepetitionNode repetition => ContainsDfaUnsupportedPredicate(repetition.Child),
            _ => false,
        };
    }

    /// <summary>
    /// Reports whether any syntax node in a collection contains a DFA-unsupported predicate.
    /// </summary>
    private static bool AnyContainsDfaUnsupportedPredicate(IReadOnlyList<RegexSyntaxNode> nodes)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (ContainsDfaUnsupportedPredicate(nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanMatchEmpty(RegexSyntaxNode node)
    {
        return node.Kind switch
        {
            RegexSyntaxKind.Empty => true,
            RegexSyntaxKind.InlineFlags => true,
            RegexSyntaxKind.Sequence => CanSequenceMatchEmpty((RegexSequenceNode)node),
            RegexSyntaxKind.Alternation => CanAlternationMatchEmpty((RegexAlternationNode)node),
            RegexSyntaxKind.CapturingGroup or RegexSyntaxKind.NonCapturingGroup => CanMatchEmpty(((RegexGroupNode)node).Child),
            RegexSyntaxKind.Repetition => ((RegexRepetitionNode)node).Minimum == 0 || CanMatchEmpty(((RegexRepetitionNode)node).Child),
            RegexSyntaxKind.StartAnchor
                or RegexSyntaxKind.EndAnchor
                or RegexSyntaxKind.AbsoluteStartAnchor
                or RegexSyntaxKind.AbsoluteEndAnchor
                or RegexSyntaxKind.WordBoundary
                or RegexSyntaxKind.NotWordBoundary
                or RegexSyntaxKind.WordStartBoundary
                or RegexSyntaxKind.WordEndBoundary
                or RegexSyntaxKind.WordStartHalfBoundary
                or RegexSyntaxKind.WordEndHalfBoundary => true,
            _ => false,
        };
    }

    private static bool CanSequenceMatchEmpty(RegexSequenceNode node)
    {
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            if (!CanMatchEmpty(node.Nodes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanAlternationMatchEmpty(RegexAlternationNode node)
    {
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (CanMatchEmpty(node.Alternatives[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNullableRepetition(RegexSyntaxNode node)
    {
        return node.Kind switch
        {
            RegexSyntaxKind.Sequence => HasNullableRepetition((RegexSequenceNode)node),
            RegexSyntaxKind.Alternation => HasNullableRepetition((RegexAlternationNode)node),
            RegexSyntaxKind.CapturingGroup or RegexSyntaxKind.NonCapturingGroup => HasNullableRepetition(((RegexGroupNode)node).Child),
            RegexSyntaxKind.Repetition => CanMatchEmpty(((RegexRepetitionNode)node).Child) ||
                HasNullableRepetition(((RegexRepetitionNode)node).Child),
            _ => false,
        };
    }

    private static bool HasNullableRepetition(RegexSequenceNode node)
    {
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            if (HasNullableRepetition(node.Nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNullableRepetition(RegexAlternationNode node)
    {
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (HasNullableRepetition(node.Alternatives[index]))
            {
                return true;
            }
        }

        return false;
    }

}
