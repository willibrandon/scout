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
/// <param name="containsExplicitNul">Whether the parsed expression contains an atom that consumes only NUL.</param>
/// <param name="isEmptyLanguage">Whether the plan represents an empty pattern set that can never match.</param>
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
    bool emptyMatchRequiresEndAssertion,
    bool containsExplicitNul = false,
    bool isEmptyLanguage = false) : IReplacementCaptureProvider
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

    /// <inheritdoc />
    int IReplacementCaptureProvider.CaptureCount => CaptureCount;

    /// <inheritdoc />
    IReadOnlyDictionary<string, int> IReplacementCaptureProvider.CaptureNames => CaptureNames;

    /// <summary>
    /// Gets the single exact case-sensitive literal selected by this plan's compiled matcher.
    /// </summary>
    /// <param name="literal">Receives the literal when the matcher has exactly one.</param>
    /// <returns><see langword="true" /> when the compiled matcher selected a single exact case-sensitive literal.</returns>
    internal bool TryGetSingleCaseSensitiveLiteral(out ReadOnlyMemory<byte> literal)
    {
        return _matcher.TryGetSingleCaseSensitiveLiteral(out literal);
    }

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
    /// Gets a value indicating whether the parsed expression contains an atom that consumes only NUL.
    /// </summary>
    internal bool ContainsExplicitNul { get; } = containsExplicitNul;

    /// <summary>
    /// Gets a value indicating whether this plan represents an empty language.
    /// </summary>
    internal bool IsEmptyLanguage { get; } = isEmptyLanguage;

    /// <summary>
    /// Creates a plan with the default ordinary line-search options.
    /// </summary>
    /// <param name="needles">The ordered regex patterns.</param>
    /// <param name="asciiCaseInsensitive">Whether literals and classes use ASCII case-insensitive matching.</param>
    /// <returns>The compiled authoritative plan.</returns>
    internal static RegexSearchPlan Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive)
    {
        return Create(needles, new RegexSearchPlanOptions(asciiCaseInsensitive));
    }

    /// <summary>
    /// Creates an authoritative plan for an ordered pattern set.
    /// </summary>
    /// <param name="needles">The ordered regex patterns.</param>
    /// <param name="options">The semantic options used to compile the pattern set.</param>
    /// <returns>The compiled authoritative plan.</returns>
    internal static RegexSearchPlan Create(
        IReadOnlyList<byte[]> needles,
        RegexSearchPlanOptions options)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (needles.Count == 0)
        {
            return CreateEmptyLanguage(options);
        }

        byte[] combinedPattern = CombinePatterns(needles, options.LineRegexp, options.WordRegexp);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(combinedPattern);
        return CreateParsed(
            tree,
            combinedPattern,
            needles.Count,
            options,
            dfaSizeLimit: null);
    }

    /// <summary>
    /// Creates an authoritative plan whose effective scope is derived from the combined parsed expression.
    /// </summary>
    /// <param name="needles">The ordered regex patterns.</param>
    /// <param name="options">The requested semantic options used to compile the pattern set.</param>
    /// <param name="scopePolicy">The policy used to select record or whole-buffer execution.</param>
    /// <param name="dfaSizeLimit">The maximum DFA cache size in bytes, or <see langword="null" /> for the default.</param>
    /// <returns>The compiled authoritative plan.</returns>
    internal static RegexSearchPlan CreateScoped(
        IReadOnlyList<byte[]> needles,
        RegexSearchPlanOptions options,
        RegexSearchScopePolicy scopePolicy,
        ulong? dfaSizeLimit = null)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (needles.Count == 0)
        {
            bool wholeBuffer = scopePolicy == RegexSearchScopePolicy.JsonMultiline && options.NullData;
            return CreateEmptyLanguage(
                CreateEffectiveOptions(options, wholeBuffer),
                dfaSizeLimit);
        }

        byte[] combinedPattern = CombinePatterns(needles, options.LineRegexp, options.WordRegexp);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(combinedPattern);
        bool usesWholeBuffer = UsesWholeBuffer(tree.Root, options, scopePolicy);
        RegexSearchPlanOptions effectiveOptions = CreateEffectiveOptions(options, usesWholeBuffer);
        return CreateParsed(
            tree,
            combinedPattern,
            needles.Count,
            effectiveOptions,
            dfaSizeLimit);
    }

    private static RegexSearchPlan CreateParsed(
        RegexSyntaxTree tree,
        ReadOnlyMemory<byte> combinedPattern,
        int patternCount,
        RegexSearchPlanOptions options,
        ulong? dfaSizeLimit)
    {
        RegexCompileOptions compileOptions = CreateCompileOptions(options);
        var matcher = RegexAutomaton.CompileParsedAuthoritative(
            tree,
            compileOptions,
            dfaSizeLimit,
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
        bool containsExplicitNul = SyntaxContainsExplicitNul(tree.Root, compileOptions);

        return new RegexSearchPlan(
            matcher,
            combinedPattern,
            patternCount,
            options,
            tree.CaptureCount,
            captureNames,
            hasAbsoluteAnchors,
            hasLineAnchors,
            hasHaystackAnchors,
            canMatchEmpty,
            canMatchEmpty && !canMatchEmptyWithoutEndAssertion,
            containsExplicitNul);
    }

    private static RegexSearchPlan CreateEmptyLanguage(
        RegexSearchPlanOptions options,
        ulong? dfaSizeLimit = null)
    {
        RegexCompileOptions compileOptions = CreateCompileOptions(options);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\b\B"u8);
        var matcher = RegexAutomaton.CompileParsedAuthoritative(
            tree,
            compileOptions,
            dfaSizeLimit,
            compilePrefilter: false);
        return new RegexSearchPlan(
            matcher,
            ReadOnlyMemory<byte>.Empty,
            patternCount: 0,
            options,
            captureCount: 0,
            s_emptyCaptureNames,
            hasAbsoluteAnchors: false,
            hasLineAnchors: false,
            hasHaystackAnchors: false,
            canMatchEmpty: false,
            emptyMatchRequiresEndAssertion: false,
            containsExplicitNul: false,
            isEmptyLanguage: true);
    }

    private static RegexCompileOptions CreateCompileOptions(
        RegexSearchPlanOptions options)
    {
        bool crlf = options.Crlf && !options.NullData;
        return new RegexCompileOptions(
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

    /// <summary>
    /// Collects globally numbered captures for a complete match span.
    /// </summary>
    /// <param name="matched">The match span reported by this plan.</param>
    /// <param name="captureStarts">Receives capture starts relative to <paramref name="matched" />.</param>
    /// <param name="captureLengths">Receives capture lengths.</param>
    /// <param name="captureNames">Receives global capture-name mappings when requested.</param>
    /// <returns><see langword="true" /> when this plan associates the complete span with captures.</returns>
    internal bool TryCollectCaptures(
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int>? captureNames)
    {
        return TryCollectCaptures(
            matched,
            matchStart: 0,
            matched.Length,
            captureStarts,
            captureLengths,
            captureNames);
    }

    /// <summary>
    /// Collects globally numbered captures for a known span in its original haystack context.
    /// </summary>
    /// <param name="haystack">The complete record or haystack used by this plan.</param>
    /// <param name="matchStart">The known match start in <paramref name="haystack" />.</param>
    /// <param name="matchLength">The known match length.</param>
    /// <param name="captureStarts">Receives capture starts relative to the known match.</param>
    /// <param name="captureLengths">Receives capture lengths.</param>
    /// <param name="captureNames">Receives global capture-name mappings when requested.</param>
    /// <returns><see langword="true" /> when this plan associates the exact span with captures.</returns>
    internal bool TryCollectCaptures(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int>? captureNames)
    {
        ArgumentNullException.ThrowIfNull(captureStarts);
        ArgumentNullException.ThrowIfNull(captureLengths);
        ValidateMatchSpan(haystack, matchStart, matchLength);

        Array.Fill(captureStarts, -1);
        Array.Fill(captureLengths, -1);
        captureNames?.Clear();
        if (captureStarts.Length > 0 && captureLengths.Length > 0)
        {
            captureStarts[0] = 0;
            captureLengths[0] = matchLength;
        }

        int[] captureSlots = new int[CaptureSlotCount];
        if (!TryCollectCaptureSlots(
            haystack,
            matchStart,
            matchLength,
            captureSlots))
        {
            return false;
        }

        int groupCount = Math.Min(
            captureSlots.Length / 2,
            Math.Min(captureStarts.Length, captureLengths.Length));
        for (int group = 0; group < groupCount; group++)
        {
            int captureStart = captureSlots[2 * group];
            int captureEnd = captureSlots[(2 * group) + 1];
            if (captureStart >= 0 && captureEnd >= captureStart)
            {
                captureStarts[group] = captureStart - matchStart;
                captureLengths[group] = captureEnd - captureStart;
            }
        }

        if (captureNames is not null)
        {
            foreach (KeyValuePair<string, int> captureName in CaptureNames)
            {
                captureNames.Add(captureName.Key, captureName.Value);
            }
        }

        return true;
    }

    /// <summary>
    /// Collects flattened capture slots for a complete match span.
    /// </summary>
    /// <param name="matched">The match span reported by this plan.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end offsets for every capture.</param>
    /// <returns><see langword="true" /> when this plan associates the complete span with captures.</returns>
    internal bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> matched,
        Span<int> captureSlots)
    {
        return TryCollectCaptureSlots(
            matched,
            matchStart: 0,
            matched.Length,
            captureSlots);
    }

    /// <summary>
    /// Collects flattened capture slots for a known span in its original haystack context.
    /// </summary>
    /// <param name="haystack">The complete record or haystack used by this plan.</param>
    /// <param name="matchStart">The known match start in <paramref name="haystack" />.</param>
    /// <param name="matchLength">The known match length.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end offsets for every capture.</param>
    /// <returns><see langword="true" /> when this plan associates the exact span with captures.</returns>
    internal bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        Span<int> captureSlots)
    {
        ValidateMatchSpan(haystack, matchStart, matchLength);
        if (captureSlots.Length < CaptureSlotCount)
        {
            throw new ArgumentException("The capture slot buffer is too small.", nameof(captureSlots));
        }

        int matchEnd = matchStart + matchLength;
        ReadOnlySpan<byte> replayHaystack = GetCaptureReplayHaystack(haystack);
        if (matchEnd > replayHaystack.Length)
        {
            captureSlots.Fill(-1);
            return false;
        }

        return TryReplayCaptures(
            replayHaystack,
            matchStart,
            matchEnd,
            captureSlots);
    }

    /// <summary>
    /// Collects flattened capture slots through an operation-scoped replay runner.
    /// </summary>
    /// <param name="haystack">The complete record or haystack used by this plan.</param>
    /// <param name="matchStart">The known match start in <paramref name="haystack" />.</param>
    /// <param name="matchLength">The known match length.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end offsets.</param>
    /// <param name="runner">The active operation-scoped capture runner.</param>
    /// <returns><see langword="true" /> when the exact span can be replayed.</returns>
    internal bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        Span<int> captureSlots,
        in RegexCaptureRunner runner)
    {
        ValidateMatchSpan(haystack, matchStart, matchLength);
        if (captureSlots.Length < CaptureSlotCount)
        {
            throw new ArgumentException("The capture slot buffer is too small.", nameof(captureSlots));
        }

        int matchEnd = matchStart + matchLength;
        ReadOnlySpan<byte> replayHaystack = GetCaptureReplayHaystack(haystack);
        if (matchEnd > replayHaystack.Length)
        {
            captureSlots.Fill(-1);
            return false;
        }

        return runner.TryReplayCaptures(
            replayHaystack,
            matchStart,
            matchEnd,
            captureSlots);
    }

    /// <inheritdoc />
    bool IReplacementCaptureProvider.TryCollectCaptureSlots(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        int searchStart,
        Span<int> captureSlots)
    {
        _ = searchStart;
        return TryCollectCaptureSlots(
            haystack,
            matchStart,
            matchLength,
            captureSlots);
    }

    private ReadOnlySpan<byte> GetCaptureReplayHaystack(ReadOnlySpan<byte> haystack)
    {
        if (_options.Multiline ||
            (!_options.NullData &&
            !_options.LineRegexp &&
            !HasHaystackAnchors))
        {
            return haystack;
        }

        byte terminator = _options.NullData ? (byte)0 : (byte)'\n';
        if (haystack.IsEmpty || haystack[^1] != terminator)
        {
            return haystack;
        }

        haystack = haystack[..^1];
        if (!_options.NullData &&
            _options.Crlf &&
            !haystack.IsEmpty &&
            haystack[^1] == (byte)'\r')
        {
            haystack = haystack[..^1];
        }

        return haystack;
    }

    private static void ValidateMatchSpan(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength)
    {
        if ((uint)matchStart > (uint)haystack.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStart));
        }

        if (matchLength < 0 || matchLength > haystack.Length - matchStart)
        {
            throw new ArgumentOutOfRangeException(nameof(matchLength));
        }
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

    private static RegexSearchPlanOptions CreateEffectiveOptions(
        RegexSearchPlanOptions requestedOptions,
        bool multiline)
    {
        return new RegexSearchPlanOptions(
            requestedOptions.AsciiCaseInsensitive,
            requestedOptions.LineRegexp,
            requestedOptions.WordRegexp,
            requestedOptions.Crlf,
            requestedOptions.NullData,
            multiline,
            multiline && requestedOptions.MultilineDotall,
            !multiline && requestedOptions.PreserveCrlfCarriageReturn);
    }

    private static bool UsesWholeBuffer(
        RegexSyntaxNode root,
        RegexSearchPlanOptions options,
        RegexSearchScopePolicy scopePolicy)
    {
        if (scopePolicy == RegexSearchScopePolicy.Records)
        {
            return false;
        }

        var analysisOptions = new RegexCompileOptions(
            options.AsciiCaseInsensitive,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: options.MultilineDotall,
            utf8: false);
        bool wholeBuffer = CanMatchLineFeed(root, analysisOptions) ||
            RequiresWholeHaystack(root, multiLine: true);
        return wholeBuffer ||
            scopePolicy == RegexSearchScopePolicy.JsonMultiline &&
            (options.NullData || ContainsLineAnchor(root));
    }

    private static bool SyntaxContainsExplicitNul(
        RegexSyntaxNode root,
        RegexCompileOptions compileOptions)
    {
        var nulExclusionOptions = new RegexCompileOptions(
            compileOptions.CaseInsensitive,
            compileOptions.SwapGreed,
            compileOptions.MultiLine,
            compileOptions.DotMatchesNewline,
            crlf: false,
            compileOptions.LineTerminator,
            compileOptions.Utf8,
            compileOptions.UnicodeClasses,
            compileOptions.SpecializationMode,
            excludeLineTerminators: true,
            excludeCrLf: false,
            excludedLineTerminator: 0,
            compileOptions.AllowRawPatternSpecializations);
        return RegexLineTerminatorAnalysis.Analyze(root, nulExclusionOptions, out _) !=
            RegexLineTerminatorAnalysisResult.None;
    }

    private static bool CanMatchLineFeed(
        RegexSyntaxNode node,
        RegexCompileOptions options)
    {
        return node switch
        {
            RegexGroupNode group => CanMatchLineFeed(
                group.Child,
                options.Apply(group.EnabledFlags, group.DisabledFlags)),
            RegexSequenceNode sequence => SequenceCanMatchLineFeed(sequence.Nodes, options),
            RegexAlternationNode alternation => AnyCanMatchLineFeed(alternation.Alternatives, options),
            RegexRepetitionNode repetition => CanMatchLineFeed(repetition.Child, options),
            RegexAtomNode atom => AtomCanMatchLineFeed(atom, options),
            _ => false,
        };
    }

    private static bool SequenceCanMatchLineFeed(
        IReadOnlyList<RegexSyntaxNode> nodes,
        RegexCompileOptions options)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < nodes.Count; index++)
        {
            RegexSyntaxNode node = nodes[index];
            if (CanMatchLineFeed(node, currentOptions))
            {
                return true;
            }

            if (node is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
            }
        }

        return false;
    }

    private static bool AnyCanMatchLineFeed(
        IReadOnlyList<RegexSyntaxNode> nodes,
        RegexCompileOptions options)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (CanMatchLineFeed(nodes[index], options))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AtomCanMatchLineFeed(
        RegexAtomNode atom,
        RegexCompileOptions options)
    {
        return RegexByteClass.TryGetAtomMatchLength(
            "\n"u8,
            position: 0,
            atom.Kind,
            atom.Value.Span,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out _);
    }

    private static bool ContainsLineAnchor(RegexSyntaxNode node)
    {
        return node switch
        {
            RegexGroupNode group => ContainsLineAnchor(group.Child),
            RegexSequenceNode sequence => AnyContainsLineAnchor(sequence.Nodes),
            RegexAlternationNode alternation => AnyContainsLineAnchor(alternation.Alternatives),
            RegexRepetitionNode repetition => ContainsLineAnchor(repetition.Child),
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.StartAnchor or RegexSyntaxKind.EndAnchor,
            _ => false,
        };
    }

    private static bool AnyContainsLineAnchor(IReadOnlyList<RegexSyntaxNode> nodes)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (ContainsLineAnchor(nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresWholeHaystack(RegexSyntaxNode node, bool multiLine)
    {
        return node switch
        {
            RegexGroupNode group => RequiresWholeHaystack(
                group.Child,
                ApplyMultiLine(multiLine, group.EnabledFlags, group.DisabledFlags)),
            RegexSequenceNode sequence => AnyRequiresWholeHaystack(sequence.Nodes, multiLine),
            RegexAlternationNode alternation => AnyRequiresWholeHaystack(alternation.Alternatives, multiLine),
            RegexRepetitionNode repetition => RequiresWholeHaystack(repetition.Child, multiLine),
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.AbsoluteStartAnchor or RegexSyntaxKind.AbsoluteEndAnchor ||
                !multiLine && atom.Kind is RegexSyntaxKind.StartAnchor or RegexSyntaxKind.EndAnchor,
            _ => false,
        };
    }

    private static bool AnyRequiresWholeHaystack(
        IReadOnlyList<RegexSyntaxNode> nodes,
        bool multiLine)
    {
        bool effectiveMultiLine = multiLine;
        for (int index = 0; index < nodes.Count; index++)
        {
            RegexSyntaxNode node = nodes[index];
            if (RequiresWholeHaystack(node, effectiveMultiLine))
            {
                return true;
            }

            if (node is RegexInlineFlagsNode flags)
            {
                effectiveMultiLine = ApplyMultiLine(
                    effectiveMultiLine,
                    flags.EnabledFlags,
                    flags.DisabledFlags);
            }
        }

        return false;
    }

    private static bool ApplyMultiLine(
        bool multiLine,
        string enabledFlags,
        string disabledFlags)
    {
        if (enabledFlags.Contains('m', StringComparison.Ordinal))
        {
            multiLine = true;
        }

        if (disabledFlags.Contains('m', StringComparison.Ordinal))
        {
            multiLine = false;
        }

        return multiLine;
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
