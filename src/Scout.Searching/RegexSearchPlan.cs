using System.Collections.ObjectModel;

namespace Scout;

/// <summary>
/// Holds one authoritative regex matcher for an ordered pattern set.
/// </summary>
/// <param name="matcher">The authoritative matcher compiled from every pattern.</param>
/// <param name="pattern">The combined regex pattern.</param>
/// <param name="patternCount">The number of source patterns in the combined expression.</param>
/// <param name="options">The semantic options used to compile the matcher.</param>
/// <param name="captureCount">The number of capturing groups in the combined expression.</param>
/// <param name="captureNames">The global capture indexes keyed by capture name.</param>
/// <param name="hasAbsoluteAnchors">Whether the expression contains an absolute haystack anchor.</param>
/// <param name="hasLineAnchors">Whether the expression contains a line anchor.</param>
/// <param name="hasHaystackAnchors">Whether the expression contains an anchor that is absolute under its effective options.</param>
/// <param name="canMatchEmpty">Whether the expression can match without consuming a byte.</param>
/// <param name="emptyMatchRequiresEndAssertion">Whether every empty-match path requires an end assertion.</param>
internal sealed class RegexSearchPlan(
    RegexAutomaton matcher,
    ReadOnlyMemory<byte> pattern,
    int patternCount,
    RegexSearchPlanOptions options,
    int captureCount,
    IReadOnlyDictionary<string, int> captureNames,
    bool hasAbsoluteAnchors,
    bool hasLineAnchors,
    bool hasHaystackAnchors,
    bool canMatchEmpty,
    bool emptyMatchRequiresEndAssertion)
{
    private static readonly IReadOnlyDictionary<string, int> s_emptyCaptureNames =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal));

    private readonly RegexAutomaton _matcher = matcher;
    private readonly RegexSearchPlanOptions _options = options;

    /// <summary>
    /// Gets the authoritative matcher for the combined pattern set.
    /// </summary>
    internal RegexAutomaton Matcher => _matcher;

    /// <summary>
    /// Gets the combined regex pattern compiled by this plan.
    /// </summary>
    internal ReadOnlyMemory<byte> Pattern { get; } = pattern;

    /// <summary>
    /// Gets the number of source patterns represented by the combined expression.
    /// </summary>
    internal int PatternCount { get; } = patternCount;

    /// <summary>
    /// Gets the semantic options used to compile this plan.
    /// </summary>
    internal RegexSearchPlanOptions Options => _options;

    /// <summary>
    /// Gets the number of capturing groups in the combined expression.
    /// </summary>
    internal int CaptureCount { get; } = captureCount;

    /// <summary>
    /// Gets the number of flattened start and exclusive-end capture slots required for replay.
    /// </summary>
    internal int CaptureSlotCount => _matcher.CaptureSlotCount;

    /// <summary>
    /// Gets the syntax-derived minimum number of bytes that any match can consume.
    /// </summary>
    internal int MinimumMatchLength => _matcher.MinimumMatchLength;

    /// <summary>
    /// Gets the global capture indexes keyed by capture name.
    /// </summary>
    internal IReadOnlyDictionary<string, int> CaptureNames { get; } = captureNames;

    /// <summary>
    /// Gets a value indicating whether the combined expression contains <c>\A</c> or <c>\z</c>.
    /// </summary>
    internal bool HasAbsoluteAnchors { get; } = hasAbsoluteAnchors;

    /// <summary>
    /// Gets a value indicating whether the combined expression contains <c>^</c> or <c>$</c>.
    /// </summary>
    internal bool HasLineAnchors { get; } = hasLineAnchors;

    /// <summary>
    /// Gets a value indicating whether the combined expression contains an anchor that is absolute under its effective options.
    /// </summary>
    internal bool HasHaystackAnchors { get; } = hasHaystackAnchors;

    /// <summary>
    /// Gets a value indicating whether the combined expression can match without consuming a byte.
    /// </summary>
    internal bool CanMatchEmpty { get; } = canMatchEmpty;

    /// <summary>
    /// Gets a value indicating whether every empty-match path requires <c>$</c> or <c>\z</c>.
    /// </summary>
    internal bool EmptyMatchRequiresEndAssertion { get; } = emptyMatchRequiresEndAssertion;

    /// <summary>
    /// Creates a plan with the default ordinary line-search options.
    /// </summary>
    /// <param name="needles">The ordered regex patterns.</param>
    /// <param name="asciiCaseInsensitive">Whether literals and classes use ASCII case-insensitive matching.</param>
    /// <returns>The compiled plan, or <see langword="null" /> for an empty pattern set.</returns>
    internal static RegexSearchPlan? Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive)
    {
        return Create(needles, new RegexSearchPlanOptions(asciiCaseInsensitive));
    }

    /// <summary>
    /// Creates an authoritative plan for an ordered pattern set.
    /// </summary>
    /// <param name="needles">The ordered regex patterns.</param>
    /// <param name="options">The semantic options used to compile the pattern set.</param>
    /// <returns>The compiled plan, or <see langword="null" /> for an empty pattern set.</returns>
    internal static RegexSearchPlan? Create(
        IReadOnlyList<byte[]> needles,
        RegexSearchPlanOptions options)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (needles.Count == 0)
        {
            return null;
        }

        byte[] combinedPattern = CombinePatterns(needles, options.LineRegexp, options.WordRegexp);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(combinedPattern);
        bool crlf = options.Crlf && !options.NullData;
        var compileOptions = new RegexCompileOptions(
            options.AsciiCaseInsensitive,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: options.MultilineDotall,
            crlf,
            lineTerminator: (byte)'\n',
            utf8: false,
            excludeLineTerminators: !options.Multiline,
            excludeCrLf: crlf && !options.PreserveCrlfCarriageReturn,
            excludedLineTerminator: options.NullData ? (byte)0 : (byte)'\n');
        var matcher = RegexAutomaton.CompileParsed(
            tree,
            compileOptions,
            compilePrefilter: true);
        IReadOnlyDictionary<string, int> captureNames = CollectCaptureNames(tree.Root);
        AnalyzeAnchors(
            tree.Root,
            compileOptions,
            out bool hasAbsoluteAnchors,
            out bool hasLineAnchors,
            out bool hasHaystackAnchors);
        AnalyzeEmptyMatchPaths(
            tree.Root,
            out bool canMatchEmpty,
            out bool canMatchEmptyWithoutEndAssertion);

        return new RegexSearchPlan(
            matcher,
            combinedPattern,
            needles.Count,
            options,
            tree.CaptureCount,
            captureNames,
            hasAbsoluteAnchors,
            hasLineAnchors,
            hasHaystackAnchors,
            canMatchEmpty,
            canMatchEmpty && !canMatchEmptyWithoutEndAssertion);
    }

    /// <summary>
    /// Determines whether this plan was compiled with the supplied semantic options.
    /// </summary>
    /// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
    /// <param name="lineRegexp">Whether the expression must match a complete record.</param>
    /// <param name="wordRegexp">Whether matches must have word boundaries.</param>
    /// <param name="crlf">Whether CRLF-aware matching is enabled.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <param name="multiline">Whether matches may span records.</param>
    /// <param name="multilineDotall">Whether dot matches record terminators in multiline mode.</param>
    /// <returns><see langword="true" /> when all semantic options match.</returns>
    internal bool IsCompatible(
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        bool multiline,
        bool multilineDotall)
    {
        return _options.IsCompatible(
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            multiline,
            multilineDotall);
    }

    /// <summary>
    /// Replays captures for a known match span into caller-owned flattened slots.
    /// </summary>
    /// <param name="haystack">The complete haystack used by the authoritative match.</param>
    /// <param name="startAt">The known match start.</param>
    /// <param name="endAt">The known exclusive match end.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end offsets for every capture.</param>
    /// <returns><see langword="true" /> when the exact span can be replayed.</returns>
    internal bool TryReplayCaptures(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int endAt,
        Span<int> captureSlots)
    {
        return _matcher.TryReplayCaptures(haystack, startAt, endAt, captureSlots);
    }

    /// <summary>
    /// Replays captures for a known match span without losing its original haystack context.
    /// </summary>
    /// <param name="haystack">The complete haystack used by the authoritative match.</param>
    /// <param name="startAt">The known match start.</param>
    /// <param name="endAt">The known exclusive match end.</param>
    /// <returns>The capture result for the exact span, or <see langword="null" /> when it cannot be replayed.</returns>
    internal RegexCaptures? ReplayCaptures(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int endAt)
    {
        return _matcher.ReplayCaptures(haystack, startAt, endAt);
    }

    private static byte[] CombinePatterns(
        IReadOnlyList<byte[]> patterns,
        bool lineRegexp,
        bool wordRegexp)
    {
        ReadOnlySpan<byte> outerPrefix = lineRegexp
            ? "^(?:"u8
            : wordRegexp ? "\\b{start-half}(?:"u8 : ""u8;
        ReadOnlySpan<byte> outerSuffix = lineRegexp
            ? ")$"u8
            : wordRegexp ? ")\\b{end-half}"u8 : ""u8;
        int length = outerPrefix.Length + outerSuffix.Length + patterns.Count - 1;
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index];
            ArgumentNullException.ThrowIfNull(pattern);
            length = checked(length + pattern.Length + 4);
        }

        byte[] combined = GC.AllocateUninitializedArray<byte>(length);
        int destination = 0;
        outerPrefix.CopyTo(combined.AsSpan(destination));
        destination += outerPrefix.Length;
        for (int index = 0; index < patterns.Count; index++)
        {
            if (index > 0)
            {
                combined[destination++] = (byte)'|';
            }

            "(?:"u8.CopyTo(combined.AsSpan(destination));
            destination += 3;
            patterns[index].CopyTo(combined.AsSpan(destination));
            destination += patterns[index].Length;
            combined[destination++] = (byte)')';
        }

        outerSuffix.CopyTo(combined.AsSpan(destination));
        return combined;
    }

    private static IReadOnlyDictionary<string, int> CollectCaptureNames(RegexSyntaxNode root)
    {
        Dictionary<string, int>? names = null;
        CollectCaptureNames(root, ref names);
        return names is null
            ? s_emptyCaptureNames
            : new ReadOnlyDictionary<string, int>(names);
    }

    private static void CollectCaptureNames(
        RegexSyntaxNode node,
        ref Dictionary<string, int>? names)
    {
        switch (node)
        {
            case RegexSequenceNode sequence:
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    CollectCaptureNames(sequence.Nodes[index], ref names);
                }

                break;
            case RegexAlternationNode alternation:
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    CollectCaptureNames(alternation.Alternatives[index], ref names);
                }

                break;
            case RegexGroupNode group:
                if (group.CaptureName is not null)
                {
                    names ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    names.Add(group.CaptureName, group.CaptureIndex);
                }

                CollectCaptureNames(group.Child, ref names);
                break;
            case RegexRepetitionNode repetition:
                CollectCaptureNames(repetition.Child, ref names);
                break;
        }
    }

    private static void AnalyzeAnchors(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out bool hasAbsoluteAnchors,
        out bool hasLineAnchors,
        out bool hasHaystackAnchors)
    {
        hasAbsoluteAnchors = false;
        hasLineAnchors = false;
        hasHaystackAnchors = false;
        AnalyzeNodeAnchors(
            root,
            options,
            ref hasAbsoluteAnchors,
            ref hasLineAnchors,
            ref hasHaystackAnchors);
    }

    private static void AnalyzeNodeAnchors(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        ref bool hasAbsoluteAnchors,
        ref bool hasLineAnchors,
        ref bool hasHaystackAnchors)
    {
        switch (node)
        {
            case RegexAtomNode atom:
                if (atom.Kind is RegexSyntaxKind.AbsoluteStartAnchor or RegexSyntaxKind.AbsoluteEndAnchor)
                {
                    hasAbsoluteAnchors = true;
                    hasHaystackAnchors = true;
                }
                else if (!options.MultiLine &&
                    atom.Kind is RegexSyntaxKind.StartAnchor or RegexSyntaxKind.EndAnchor)
                {
                    hasLineAnchors = true;
                    hasHaystackAnchors = true;
                }
                else if (atom.Kind is RegexSyntaxKind.StartAnchor or RegexSyntaxKind.EndAnchor)
                {
                    hasLineAnchors = true;
                }

                break;
            case RegexSequenceNode sequence:
                RegexCompileOptions currentOptions = options;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    RegexSyntaxNode child = sequence.Nodes[index];
                    AnalyzeNodeAnchors(
                        child,
                        currentOptions,
                        ref hasAbsoluteAnchors,
                        ref hasLineAnchors,
                        ref hasHaystackAnchors);
                    if (child is RegexInlineFlagsNode flags)
                    {
                        currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                    }
                }

                break;
            case RegexAlternationNode alternation:
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    AnalyzeNodeAnchors(
                        alternation.Alternatives[index],
                        options,
                        ref hasAbsoluteAnchors,
                        ref hasLineAnchors,
                        ref hasHaystackAnchors);
                }

                break;
            case RegexGroupNode group:
                AnalyzeNodeAnchors(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    ref hasAbsoluteAnchors,
                    ref hasLineAnchors,
                    ref hasHaystackAnchors);
                break;
            case RegexRepetitionNode repetition:
                AnalyzeNodeAnchors(
                    repetition.Child,
                    options,
                    ref hasAbsoluteAnchors,
                    ref hasLineAnchors,
                    ref hasHaystackAnchors);
                break;
        }
    }

    private static void AnalyzeEmptyMatchPaths(
        RegexSyntaxNode node,
        out bool canMatchEmpty,
        out bool canMatchEmptyWithoutEndAssertion)
    {
        switch (node)
        {
            case RegexEmptyNode:
            case RegexInlineFlagsNode:
                canMatchEmpty = true;
                canMatchEmptyWithoutEndAssertion = true;
                return;
            case RegexAtomNode atom:
                canMatchEmpty = IsEmptyAssertion(atom.Kind);
                canMatchEmptyWithoutEndAssertion = canMatchEmpty &&
                    atom.Kind is not RegexSyntaxKind.EndAnchor and not RegexSyntaxKind.AbsoluteEndAnchor;
                return;
            case RegexSequenceNode sequence:
                canMatchEmpty = true;
                canMatchEmptyWithoutEndAssertion = true;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    AnalyzeEmptyMatchPaths(
                        sequence.Nodes[index],
                        out bool childCanMatchEmpty,
                        out bool childCanMatchEmptyWithoutEndAssertion);
                    canMatchEmpty &= childCanMatchEmpty;
                    canMatchEmptyWithoutEndAssertion &= childCanMatchEmptyWithoutEndAssertion;
                }

                return;
            case RegexAlternationNode alternation:
                canMatchEmpty = false;
                canMatchEmptyWithoutEndAssertion = false;
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    AnalyzeEmptyMatchPaths(
                        alternation.Alternatives[index],
                        out bool alternativeCanMatchEmpty,
                        out bool alternativeCanMatchEmptyWithoutEndAssertion);
                    canMatchEmpty |= alternativeCanMatchEmpty;
                    canMatchEmptyWithoutEndAssertion |= alternativeCanMatchEmptyWithoutEndAssertion;
                }

                return;
            case RegexGroupNode group:
                AnalyzeEmptyMatchPaths(
                    group.Child,
                    out canMatchEmpty,
                    out canMatchEmptyWithoutEndAssertion);
                return;
            case RegexRepetitionNode repetition:
                if (repetition.Minimum == 0)
                {
                    canMatchEmpty = true;
                    canMatchEmptyWithoutEndAssertion = true;
                    return;
                }

                AnalyzeEmptyMatchPaths(
                    repetition.Child,
                    out canMatchEmpty,
                    out canMatchEmptyWithoutEndAssertion);
                return;
            default:
                canMatchEmpty = false;
                canMatchEmptyWithoutEndAssertion = false;
                return;
        }
    }

    private static bool IsEmptyAssertion(RegexSyntaxKind kind)
    {
        return kind is
            RegexSyntaxKind.StartAnchor or
            RegexSyntaxKind.EndAnchor or
            RegexSyntaxKind.AbsoluteStartAnchor or
            RegexSyntaxKind.AbsoluteEndAnchor or
            RegexSyntaxKind.WordBoundary or
            RegexSyntaxKind.NotWordBoundary or
            RegexSyntaxKind.WordStartBoundary or
            RegexSyntaxKind.WordEndBoundary or
            RegexSyntaxKind.WordStartHalfBoundary or
            RegexSyntaxKind.WordEndHalfBoundary;
    }
}
