using System.Buffers;
using System.Text;

namespace Scout;

/// <summary>
/// Locates conservative regex match candidates before authoritative engine evaluation.
/// </summary>
/// <param name="kind">The prefilter strategy.</param>
/// <param name="memmem">The case-sensitive single-prefix finder.</param>
/// <param name="teddy">The small prefix-set finder.</param>
/// <param name="ahoCorasick">The general prefix-set finder.</param>
/// <param name="candidateGate">The optional exact-prefix candidate gate.</param>
/// <param name="requiredLiteralPrefixGate">The optional reverse-prefix required-literal gate.</param>
/// <param name="requiredMemmem">The case-sensitive required-literal finder.</param>
/// <param name="requiredLiteral">The case-insensitive required-literal finder.</param>
/// <param name="requiredLiterals">The general required-literal-set finder.</param>
/// <param name="requiredTeddy">The small required-literal-set finder.</param>
/// <param name="startPredicate">The eagerly compiled start predicate.</param>
/// <param name="startPredicateFactory">The lazy start-predicate factory.</param>
/// <param name="requiredLiteralWindow">The conservative maximum required-literal lookbehind.</param>
internal sealed class RegexPrefilter(
    RegexPrefilterKind kind,
    MemmemFinder? memmem,
    RegexTeddyPrefilter? teddy,
    AhoCorasickAutomaton? ahoCorasick,
    RegexPrefixCandidateGate? candidateGate = null,
    RegexRequiredLiteralPrefixGate? requiredLiteralPrefixGate = null,
    MemmemFinder? requiredMemmem = null,
    RegexAsciiCaseInsensitiveFinder? requiredLiteral = null,
    AhoCorasickAutomaton? requiredLiterals = null,
    RegexTeddyPrefilter? requiredTeddy = null,
    RegexStartPredicate? startPredicate = null,
    Func<RegexStartPredicate?>? startPredicateFactory = null,
    int requiredLiteralWindow = RegexPrefilter.RequiredLiteralLookBehind)
{
    /// <summary>
    /// The largest conservative required-literal lookbehind supported by a prefilter.
    /// </summary>
    internal const int RequiredLiteralLookBehind = 512;

    /// <summary>
    /// The largest short lookbehind preferred when required literals are equally selective.
    /// </summary>
    internal const int MaxSelectiveRequiredLiteralLookBehind = 8;
    private const int MaxRequiredLiteralVariants = 128;
    private const int MaxExpandedRequiredLiteralVariants = 1024;
    private const int MaxClassLiteralVariants = 16;
    private const int PreferredPrefixBytes = 3;
    private const int MinimumSharedRequiredLiteralPrefixBytes = 8;
    private const int MaximumSharedRequiredLiteralPrefixBytes = 64;
    private const int MaxPrefixVariants = 512;
    private const int MaxPrefixAtomVariants = 32;
    private const int LowSelectivityPrefixSetThreshold = 64;
    private const int LargeCaseInsensitiveAsciiPrefixAnalysisNodeThreshold = 1024;
    private const int RequiredLiteralPrefilterAlternativePenalty = 64;

    private readonly MemmemFinder? _memmem = memmem;
    private readonly RegexTeddyPrefilter? _teddy = teddy;
    private readonly AhoCorasickAutomaton? _ahoCorasick = ahoCorasick;
    private readonly RegexPrefixCandidateGate? _candidateGate = candidateGate;
    private readonly RegexRequiredLiteralPrefixGate? _requiredLiteralPrefixGate = requiredLiteralPrefixGate;
    private readonly MemmemFinder? _requiredMemmem = requiredMemmem;
    private readonly RegexAsciiCaseInsensitiveFinder? _requiredLiteral = requiredLiteral;
    private readonly AhoCorasickAutomaton? _requiredLiterals = requiredLiterals;
    private readonly RegexTeddyPrefilter? _requiredTeddy = requiredTeddy;
    private readonly RegexStartPredicate? _startPredicate = startPredicate;
    private readonly Lazy<RegexStartPredicate?>? _lazyStartPredicate = startPredicateFactory is null
        ? null
        : new Lazy<RegexStartPredicate?>(startPredicateFactory);
    private readonly int _requiredLiteralWindow = Math.Clamp(
        requiredLiteralWindow,
        0,
        RequiredLiteralLookBehind);

    /// <summary>
    /// Gets the prefilter strategy.
    /// </summary>
    public RegexPrefilterKind Kind { get; } = kind;

    /// <summary>
    /// Gets a value indicating whether this prefilter searches for required inner literals.
    /// </summary>
    public bool UsesRequiredLiteralWindow => _requiredMemmem is not null ||
        _requiredLiteral is not null ||
        _requiredLiterals is not null ||
        _requiredTeddy is not null;

    /// <summary>
    /// Gets the conservative maximum lookbehind for required-literal hits.
    /// </summary>
    public int RequiredLiteralWindow => UsesRequiredLiteralWindow ? _requiredLiteralWindow : 0;

    /// <summary>
    /// Gets a value indicating whether every reported candidate is an exact match start.
    /// </summary>
    public bool UsesExactStartCandidates =>
        !UsesRequiredLiteralWindow || RequiredLiteralWindow == 0;

    /// <summary>
    /// Gets a value indicating whether required-literal ranges can use reverse-prefix narrowing.
    /// </summary>
    public bool UsesRequiredLiteralPrefixGate => _requiredLiteralPrefixGate is not null;

    /// <summary>
    /// Gets a value indicating whether required-literal search can detect NUL bytes in the same pass.
    /// </summary>
    public bool CanDetectNulDuringRequiredLiteralSearch => _requiredMemmem is not null;

    /// <summary>
    /// Determines whether a match can begin at one byte offset.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The proposed match start.</param>
    /// <returns><see langword="true"/> when the start is feasible.</returns>
    public bool CanStartAt(ReadOnlySpan<byte> haystack, int start)
    {
        RegexStartPredicate? predicate = _startPredicate ?? _lazyStartPredicate?.Value;
        return predicate is null || predicate.CanStartAt(haystack, start);
    }

    /// <summary>
    /// Compiles the most selective safe prefilter for a regex syntax tree.
    /// </summary>
    /// <param name="root">The regex syntax tree.</param>
    /// <param name="options">The regex compile options.</param>
    /// <returns>The compiled prefilter, or <see langword="null"/> when none is safe.</returns>
    public static RegexPrefilter? Compile(RegexSyntaxNode root, RegexCompileOptions options)
    {
        return Compile(root, options, out _);
    }

    internal static RegexPrefilter? Compile(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexStartPrefixSet? startPrefixSet)
    {
        startPrefixSet = null;
        bool skipLargeCaseInsensitiveAsciiPrefilter = ShouldSkipLargeCaseInsensitiveAsciiPrefilter(root, options);
        bool skipSequenceAlternationPrefixPrefilter = ShouldSkipSequenceAlternationPrefixPrefilter(root, options);
        bool rejectedLowSelectivityPrefixSet = false;
        if (!skipSequenceAlternationPrefixPrefilter &&
            TryCreateSequenceAlternationPrefixPrefilter(
                root,
                options,
                out RegexPrefilter? prefilter,
                out startPrefixSet,
                out rejectedLowSelectivityPrefixSet))
        {
            return prefilter;
        }

        if (skipLargeCaseInsensitiveAsciiPrefilter)
        {
            return TryCompileLargeCaseInsensitiveAsciiPrefilter(root, options);
        }

        if (rejectedLowSelectivityPrefixSet)
        {
            return null;
        }

        var prefix = new List<byte>();
        bool prefixCaseInsensitive = false;
        bool prefixUnicodeClasses = false;
        if (!TryAppendRequiredPrefix(root, options, prefix, out _, ref prefixCaseInsensitive, ref prefixUnicodeClasses) || prefix.Count == 0)
        {
            bool hasSelectedCandidate = false;
            bool selectedCandidateHasBoundedLookBehind = false;
            bool selectedCandidateIsSharedExactStart = false;
            RegexRequiredLiteralSetCandidate selectedCandidate = default;
            byte[][] selectedPreparedLiterals = [];

            void ConsiderRequiredLiteralCandidate(
                RegexRequiredLiteralSetCandidate candidate,
                int maxLookBehind,
                bool hasBoundedLookBehind,
                bool isSharedExactStart)
            {
                if (candidate.Literals.Length == 0 ||
                    !TryPrepareRequiredLiteralSet(
                        candidate.Literals,
                        candidate.CaseInsensitive,
                        candidate.UnicodeClasses,
                        out byte[][] preparedLiterals) ||
                    (hasSelectedCandidate &&
                    !IsBetterRequiredLiteralPrefilterCandidate(
                        preparedLiterals,
                        maxLookBehind,
                        selectedPreparedLiterals,
                        selectedCandidate.MaxLookBehind,
                        hasCurrent: true)))
                {
                    return;
                }

                selectedCandidate = new RegexRequiredLiteralSetCandidate(
                    candidate.Literals,
                    maxLookBehind,
                    candidate.CaseInsensitive,
                    candidate.UnicodeClasses);
                selectedPreparedLiterals = preparedLiterals;
                selectedCandidateHasBoundedLookBehind = hasBoundedLookBehind;
                selectedCandidateIsSharedExactStart = isSharedExactStart;
                hasSelectedCandidate = true;
            }

            bool hasSharedExactStartCandidate = TryCollectSharedExactStartRequiredLiteralCandidate(
                root,
                options,
                out RegexRequiredLiteralSetCandidate sharedExactStartCandidate,
                out int sharedExactStartCompetitorScoreCeiling);
            if (!hasSharedExactStartCandidate)
            {
                if (TryCollectRequiredLiteralSetWithLookBehind(
                        root,
                        options,
                        out RegexRequiredLiteralSetCandidate boundedEstablishedCandidate) &&
                    boundedEstablishedCandidate.Literals.Length > 0 &&
                    TryPrepareRequiredLiteralSet(
                        boundedEstablishedCandidate.Literals,
                        boundedEstablishedCandidate.CaseInsensitive,
                        boundedEstablishedCandidate.UnicodeClasses,
                        out byte[][] boundedEstablishedLiterals))
                {
                    RegexRequiredLiteralPrefixGate.TryCreate(
                        root,
                        options,
                        boundedEstablishedCandidate,
                        out RegexRequiredLiteralPrefixGate? establishedPrefixGate);
                    return CreateRequiredLiteralPrefilter(
                        boundedEstablishedLiterals,
                        boundedEstablishedCandidate.CaseInsensitive,
                        startPredicate: null,
                        maxLookBehind: boundedEstablishedCandidate.MaxLookBehind,
                        requiredLiteralPrefixGate: establishedPrefixGate,
                        startPredicateFactory: CreateStartPredicateFactory());
                }

                if (TryCollectRequiredLiteralSet(
                        root,
                        options,
                        out RegexRequiredLiteralSetCandidate ordinaryEstablishedCandidate) &&
                    ordinaryEstablishedCandidate.Literals.Length > 0 &&
                    TryPrepareRequiredLiteralSet(
                        ordinaryEstablishedCandidate.Literals,
                        ordinaryEstablishedCandidate.CaseInsensitive,
                        ordinaryEstablishedCandidate.UnicodeClasses,
                        out byte[][] ordinaryEstablishedLiterals))
                {
                    return CreateRequiredLiteralPrefilter(
                        ordinaryEstablishedLiterals,
                        ordinaryEstablishedCandidate.CaseInsensitive,
                        startPredicate: null,
                        maxLookBehind: RequiredLiteralLookBehind,
                        startPredicateFactory: CreateStartPredicateFactory());
                }

                return TryFindRequiredLiteralCandidate(
                        root,
                        options,
                        out RegexRequiredLiteralSetCandidate singleEstablishedCandidate) &&
                    singleEstablishedCandidate.Literals.Length == 1 &&
                    singleEstablishedCandidate.Literals[0].Length >= 3 &&
                    TryPrepareRequiredLiteralSet(
                        singleEstablishedCandidate.Literals,
                        singleEstablishedCandidate.CaseInsensitive,
                        singleEstablishedCandidate.UnicodeClasses,
                        out byte[][] singleEstablishedLiteral)
                    ? CreateRequiredLiteralPrefilter(
                        singleEstablishedLiteral,
                        singleEstablishedCandidate.CaseInsensitive,
                        startPredicate: null,
                        maxLookBehind: RequiredLiteralLookBehind,
                        startPredicateFactory: CreateStartPredicateFactory())
                    : null;
            }

            ConsiderRequiredLiteralCandidate(
                sharedExactStartCandidate,
                sharedExactStartCandidate.MaxLookBehind,
                hasBoundedLookBehind: true,
                isSharedExactStart: true);
            if (RequiredLiteralPrefilterScore(sharedExactStartCandidate.Literals) >=
                sharedExactStartCompetitorScoreCeiling)
            {
                return CreateSharedExactStartRequiredLiteralPrefilter(
                    root,
                    options,
                    sharedExactStartCandidate);
            }

            if (TryCollectRequiredLiteralSetWithLookBehind(
                    root,
                    options,
                    out RegexRequiredLiteralSetCandidate boundedCandidate))
            {
                ConsiderRequiredLiteralCandidate(
                    boundedCandidate,
                    boundedCandidate.MaxLookBehind,
                    hasBoundedLookBehind: true,
                    isSharedExactStart: false);
            }

            if (TryCollectRequiredLiteralSet(
                    root,
                    options,
                    out RegexRequiredLiteralSetCandidate requiredCandidate))
            {
                ConsiderRequiredLiteralCandidate(
                    requiredCandidate,
                    RequiredLiteralLookBehind,
                    hasBoundedLookBehind: false,
                    isSharedExactStart: false);
            }

            if (TryFindRequiredLiteralCandidate(root, options, out requiredCandidate) &&
                requiredCandidate.Literals.Length == 1 &&
                requiredCandidate.Literals[0].Length >= 3)
            {
                ConsiderRequiredLiteralCandidate(
                    requiredCandidate,
                    RequiredLiteralLookBehind,
                    hasBoundedLookBehind: false,
                    isSharedExactStart: false);
            }

            if (!hasSelectedCandidate)
            {
                return null;
            }

            if (selectedCandidateIsSharedExactStart)
            {
                return CreateSharedExactStartRequiredLiteralPrefilter(
                    root,
                    options,
                    selectedCandidate);
            }

            Func<RegexStartPredicate?> CreateStartPredicateFactory()
            {
                return () =>
                {
                    RegexStartPredicate.TryCreate(root, options, out RegexStartPredicate? predicate);
                    return predicate;
                };
            }

            RegexRequiredLiteralPrefixGate? requiredLiteralPrefixGate = null;
            if (selectedCandidateHasBoundedLookBehind)
            {
                RegexRequiredLiteralPrefixGate.TryCreate(
                    root,
                    options,
                    selectedCandidate,
                    out requiredLiteralPrefixGate);
            }

            return CreateRequiredLiteralPrefilter(
                selectedPreparedLiterals,
                selectedCandidate.CaseInsensitive,
                startPredicate: null,
                maxLookBehind: selectedCandidate.MaxLookBehind,
                requiredLiteralPrefixGate: requiredLiteralPrefixGate,
                startPredicateFactory: CreateStartPredicateFactory());
        }

        var prefixOptions = new RegexCompileOptions(
            prefixCaseInsensitive,
            options.SwapGreed,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            prefixUnicodeClasses);
        return TryCreateSinglePrefixPrefilter(prefix.ToArray(), prefixOptions, out RegexPrefilter? prefixPrefilter)
            ? prefixPrefilter
            : null;
    }

    private static bool ShouldSkipLargeCaseInsensitiveAsciiPrefilter(RegexSyntaxNode root, RegexCompileOptions options)
    {
        return options.CaseInsensitive &&
            !options.UnicodeClasses &&
            ExceedsPrefixAnalysisNodeThreshold(root, LargeCaseInsensitiveAsciiPrefixAnalysisNodeThreshold / 2);
    }

    private static RegexPrefilter? TryCompileLargeCaseInsensitiveAsciiPrefilter(
        RegexSyntaxNode root,
        RegexCompileOptions options)
    {
        var prefix = new List<byte>();
        bool prefixCaseInsensitive = false;
        bool prefixUnicodeClasses = false;
        if (TryAppendRequiredPrefix(root, options, prefix, out _, ref prefixCaseInsensitive, ref prefixUnicodeClasses) &&
            prefix.Count > 0)
        {
            var prefixOptions = new RegexCompileOptions(
                prefixCaseInsensitive,
                options.SwapGreed,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                prefixUnicodeClasses);
            return TryCreateSinglePrefixPrefilter(prefix.ToArray(), prefixOptions, out RegexPrefilter? prefixPrefilter)
                ? prefixPrefilter
                : null;
        }

        return TryFindRequiredLiteralCandidate(root, options, out RegexRequiredLiteralSetCandidate requiredCandidate) &&
            requiredCandidate.Literals.Length == 1 &&
            requiredCandidate.Literals[0].Length >= 3 &&
            TryPrepareRequiredLiteralSet(
                requiredCandidate.Literals,
                requiredCandidate.CaseInsensitive,
                requiredCandidate.UnicodeClasses,
                out byte[][] preparedRequired)
            ? CreateRequiredLiteralPrefilter(
                preparedRequired,
                requiredCandidate.CaseInsensitive,
                startPredicate: null,
                RequiredLiteralLookBehind)
            : null;
    }

    private static bool ShouldSkipSequenceAlternationPrefixPrefilter(RegexSyntaxNode root, RegexCompileOptions options)
    {
        if (!options.CaseInsensitive || options.UnicodeClasses)
        {
            return false;
        }

        return ExceedsPrefixAnalysisNodeThreshold(root, LargeCaseInsensitiveAsciiPrefixAnalysisNodeThreshold);
    }

    private static bool TryCreateSequenceAlternationStartPrefixSet(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexStartPrefixSet? startPrefixSet)
    {
        startPrefixSet = null;
        if (!TryCollectSequenceAlternationPrefixes(
                root,
                options,
                out byte[][]? prefixes,
                out bool caseInsensitivePrefixes,
                out bool unicodeCaseInsensitivePrefixes) ||
            prefixes is null)
        {
            return false;
        }

        startPrefixSet = new RegexStartPrefixSet(prefixes, caseInsensitivePrefixes, unicodeCaseInsensitivePrefixes);
        return true;
    }

    private static bool ExceedsPrefixAnalysisNodeThreshold(RegexSyntaxNode node, int threshold)
    {
        int remaining = threshold;
        return ExceedsPrefixAnalysisNodeThreshold(node, ref remaining);
    }

    private static bool ExceedsPrefixAnalysisNodeThreshold(RegexSyntaxNode node, ref int remaining)
    {
        if (--remaining < 0)
        {
            return true;
        }

        switch (node)
        {
            case RegexSequenceNode sequence:
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (ExceedsPrefixAnalysisNodeThreshold(sequence.Nodes[index], ref remaining))
                    {
                        return true;
                    }
                }

                return false;

            case RegexAlternationNode alternation:
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (ExceedsPrefixAnalysisNodeThreshold(alternation.Alternatives[index], ref remaining))
                    {
                        return true;
                    }
                }

                return false;

            case RegexGroupNode group:
                return ExceedsPrefixAnalysisNodeThreshold(group.Child, ref remaining);

            case RegexRepetitionNode repetition:
                return ExceedsPrefixAnalysisNodeThreshold(repetition.Child, ref remaining);

            default:
                return false;
        }
    }

    private static bool TryCreateSinglePrefixPrefilter(
        byte[] prefix,
        RegexCompileOptions options,
        out RegexPrefilter? prefilter)
    {
        prefilter = null;
        if (prefix.Length == 0)
        {
            return false;
        }

        if (!options.CaseInsensitive)
        {
            prefilter = new RegexPrefilter(
                RegexPrefilterKind.Memmem,
                new MemmemFinder(prefix),
                teddy: null,
                ahoCorasick: null);
            return true;
        }

        if (!TryPreparePrefixLiteralSet([prefix], options, out byte[][] preparedPrefixes))
        {
            if (TryPrepareRequiredLiteralSet([prefix], options, out byte[][] preparedRequired))
            {
                prefilter = CreateRequiredLiteralPrefilter(
                    preparedRequired,
                    options.CaseInsensitive,
                    startPredicate: null,
                    maxLookBehind: 0);
                return true;
            }

            return false;
        }

        prefilter = new RegexPrefilter(
            RegexPrefilterKind.AhoCorasick,
            memmem: null,
            teddy: null,
            ahoCorasick: AhoCorasickAutomaton.Create(preparedPrefixes, AhoCorasickMatchKind.LeftmostFirst, asciiCaseInsensitive: true));
        return true;
    }

    private static bool TryCreateSequenceAlternationPrefixPrefilter(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexPrefilter? prefilter,
        out RegexStartPrefixSet? startPrefixSet)
    {
        return TryCreateSequenceAlternationPrefixPrefilter(
            root,
            options,
            out prefilter,
            out startPrefixSet,
            out _);
    }

    private static bool TryCreateSequenceAlternationPrefixPrefilter(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexPrefilter? prefilter,
        out RegexStartPrefixSet? startPrefixSet,
        out bool rejectedLowSelectivityPrefixSet)
    {
        prefilter = null;
        startPrefixSet = null;
        rejectedLowSelectivityPrefixSet = false;
        if (!TryCollectSequenceAlternationPrefixes(
                root,
                options,
                out byte[][]? prefixes,
                out bool caseInsensitivePrefixes,
                out bool unicodeCaseInsensitivePrefixes))
        {
            return false;
        }

        if (prefixes is null)
        {
            return false;
        }

        startPrefixSet = new RegexStartPrefixSet(prefixes, caseInsensitivePrefixes, unicodeCaseInsensitivePrefixes);
        if (!unicodeCaseInsensitivePrefixes && IsLowSelectivityPrefixSet(prefixes))
        {
            rejectedLowSelectivityPrefixSet = true;
            return false;
        }

        RegexPrefixCandidateGate? candidateGate = null;
        RegexPrefixCandidateGate.TryCreate(root, options, prefixes, out candidateGate);
        return TryCreatePrefixSetPrefilter(
                prefixes,
                options,
                candidateGate,
                caseInsensitivePrefixes,
                unicodeCaseInsensitivePrefixes,
                out prefilter,
                out rejectedLowSelectivityPrefixSet);
    }

    /// <summary>
    /// Collects a required-literal candidate when every leading alternation branch shares a
    /// sufficiently selective case-sensitive prefix and then either ends as a fixed literal
    /// sequence or reaches an unbounded repetition. Finite variable-width and nonliteral branches
    /// deliberately fall through to the established required-literal analysis so that a more
    /// selective inner literal is not displaced.
    /// </summary>
    /// <param name="root">The syntax tree whose leading alternation is inspected.</param>
    /// <param name="options">The effective compile options.</param>
    /// <param name="candidate">Receives the shared exact-start candidate when successful.</param>
    /// <param name="competitorScoreCeiling">
    /// Receives the greatest score any competing candidate can have when every branch is a
    /// bounded fixed literal, or <see cref="int.MaxValue" /> when no safe ceiling is known.
    /// </param>
    /// <returns><see langword="true" /> when the shared prefix can compete with other candidates.</returns>
    private static bool TryCollectSharedExactStartRequiredLiteralCandidate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate,
        out int competitorScoreCeiling)
    {
        candidate = default;
        competitorScoreCeiling = int.MaxValue;
        if (!TryGetLeadingAlternation(
                root,
                options,
                out RegexAlternationNode alternation,
                out RegexCompileOptions alternationOptions))
        {
            return false;
        }

        List<byte>? sharedPrefix = null;
        var fixedBranchPrefixes = new List<byte[]>(alternation.Alternatives.Count);
        bool canBoundCompetitorScore = true;
        int sharedLength = 0;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            var branchPrefix = new List<byte>();
            if (!TryAppendEligibleSharedPrefixBranch(
                    alternation.Alternatives[index],
                    alternationOptions,
                    branchPrefix,
                    out bool reachedUnboundedBarrier))
            {
                return false;
            }

            if (canBoundCompetitorScore &&
                (reachedUnboundedBarrier ||
                branchPrefix.Count >= MaximumSharedRequiredLiteralPrefixBytes))
            {
                canBoundCompetitorScore = false;
                fixedBranchPrefixes.Clear();
            }
            else if (canBoundCompetitorScore)
            {
                fixedBranchPrefixes.Add(branchPrefix.ToArray());
            }

            if (sharedPrefix is null)
            {
                sharedPrefix = branchPrefix;
                sharedLength = branchPrefix.Count;
                continue;
            }

            int commonLength = Math.Min(sharedLength, branchPrefix.Count);
            int byteIndex = 0;
            while (byteIndex < commonLength && sharedPrefix[byteIndex] == branchPrefix[byteIndex])
            {
                byteIndex++;
            }

            sharedLength = byteIndex;
            if (sharedLength < MinimumSharedRequiredLiteralPrefixBytes)
            {
                return false;
            }
        }

        if (sharedPrefix is null || sharedLength < MinimumSharedRequiredLiteralPrefixBytes)
        {
            return false;
        }

        candidate = new RegexRequiredLiteralSetCandidate(
            [sharedPrefix.GetRange(0, sharedLength).ToArray()],
            maxLookBehind: 0);
        if (canBoundCompetitorScore)
        {
            int longestCommonSubstring = LongestCommonSubstringLength(fixedBranchPrefixes);
            int maximumLiteralLength = 0;
            for (int index = 0; index < fixedBranchPrefixes.Count; index++)
            {
                maximumLiteralLength = Math.Max(
                    maximumLiteralLength,
                    fixedBranchPrefixes[index].Length);
            }

            int singleLiteralCeiling = RequiredLiteralPrefilterScore(
                literalCount: 1,
                shortest: longestCommonSubstring,
                longest: longestCommonSubstring,
                totalLength: longestCommonSubstring);
            int multipleLiteralCeiling = RequiredLiteralPrefilterScore(
                literalCount: 2,
                shortest: maximumLiteralLength,
                longest: maximumLiteralLength,
                totalLength: maximumLiteralLength * 2);
            competitorScoreCeiling = Math.Max(singleLiteralCeiling, multipleLiteralCeiling);
        }

        return true;
    }

    /// <summary>
    /// Finds the longest contiguous byte sequence shared by every fixed branch.
    /// </summary>
    /// <param name="branches">The fixed branch byte sequences.</param>
    /// <returns>The length of the longest shared byte sequence.</returns>
    private static int LongestCommonSubstringLength(List<byte[]> branches)
    {
        if (branches.Count == 0)
        {
            return 0;
        }

        byte[] shortest = branches[0];
        for (int index = 1; index < branches.Count; index++)
        {
            if (branches[index].Length < shortest.Length)
            {
                shortest = branches[index];
            }
        }

        for (int length = shortest.Length; length > 0; length--)
        {
            for (int start = 0; start <= shortest.Length - length; start++)
            {
                ReadOnlySpan<byte> candidate = shortest.AsSpan(start, length);
                bool shared = true;
                for (int branchIndex = 0; branchIndex < branches.Count; branchIndex++)
                {
                    if (branches[branchIndex].AsSpan().IndexOf(candidate) < 0)
                    {
                        shared = false;
                        break;
                    }
                }

                if (shared)
                {
                    return length;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Creates the prefilter selected for a shared exact-start required-literal candidate.
    /// </summary>
    /// <param name="root">The syntax tree used to validate exact starts.</param>
    /// <param name="options">The effective compile options.</param>
    /// <param name="candidate">The selected shared exact-start candidate.</param>
    /// <returns>The compiled required-literal prefilter.</returns>
    private static RegexPrefilter CreateSharedExactStartRequiredLiteralPrefilter(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        RegexRequiredLiteralSetCandidate candidate)
    {
        RegexStartPredicate.TryCreate(root, options, out RegexStartPredicate? startPredicate);
        return CreateRequiredLiteralPrefilter(
            candidate.Literals,
            asciiCaseInsensitive: false,
            startPredicate,
            maxLookBehind: candidate.MaxLookBehind);
    }

    /// <summary>
    /// Finds the leading alternation after transparent groups and zero-width sequence nodes.
    /// </summary>
    /// <param name="node">The syntax subtree to inspect.</param>
    /// <param name="options">The effective compile options.</param>
    /// <param name="alternation">Receives the leading alternation.</param>
    /// <param name="alternationOptions">Receives the options effective for its branches.</param>
    /// <returns><see langword="true" /> when a leading alternation is found.</returns>
    private static bool TryGetLeadingAlternation(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexAlternationNode alternation,
        out RegexCompileOptions alternationOptions)
    {
        switch (node)
        {
            case RegexAlternationNode candidate:
                alternation = candidate;
                alternationOptions = options;
                return true;

            case RegexGroupNode group:
                return TryGetLeadingAlternation(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out alternation,
                    out alternationOptions);

            case RegexSequenceNode sequence:
                RegexCompileOptions currentOptions = options;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    RegexSyntaxNode child = sequence.Nodes[index];
                    if (child is RegexInlineFlagsNode flags)
                    {
                        currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                        continue;
                    }

                    if (IsZeroWidthPrefixTransparent(child, currentOptions))
                    {
                        continue;
                    }

                    return TryGetLeadingAlternation(
                        child,
                        currentOptions,
                        out alternation,
                        out alternationOptions);
                }

                break;
        }

        alternation = null!;
        alternationOptions = options;
        return false;
    }

    /// <summary>
    /// Appends one eligible branch's fixed case-sensitive prefix and reports whether it reaches
    /// the unbounded barrier that permits the conservative shortcut.
    /// </summary>
    private static bool TryAppendEligibleSharedPrefixBranch(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool reachedUnboundedBarrier)
    {
        reachedUnboundedBarrier = false;
        if (IsZeroWidthPrefixTransparent(node, options))
        {
            return true;
        }

        switch (node)
        {
            case RegexAtomNode atom when atom.Kind == RegexSyntaxKind.Literal && !options.CaseInsensitive:
                AppendSharedPrefixBytes(prefix, atom.Value.Span);
                return true;

            case RegexGroupNode group:
                return TryAppendEligibleSharedPrefixBranch(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    prefix,
                    out reachedUnboundedBarrier);

            case RegexSequenceNode sequence:
                RegexCompileOptions currentOptions = options;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    RegexSyntaxNode child = sequence.Nodes[index];
                    if (child is RegexInlineFlagsNode flags)
                    {
                        currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                        continue;
                    }

                    if (!TryAppendEligibleSharedPrefixBranch(
                            child,
                            currentOptions,
                            prefix,
                            out reachedUnboundedBarrier))
                    {
                        return false;
                    }

                    if (reachedUnboundedBarrier)
                    {
                        return true;
                    }
                }

                return true;

            case RegexRepetitionNode repetition when repetition.Maximum is null:
                if (repetition.Minimum > 0)
                {
                    var childPrefix = new List<byte>();
                    if (TryAppendEligibleSharedPrefixBranch(
                            repetition.Child,
                            options,
                            childPrefix,
                            out bool childReachedBarrier) &&
                        !childReachedBarrier &&
                        childPrefix.Count > 0)
                    {
                        for (int count = 0;
                             count < repetition.Minimum && prefix.Count < MaximumSharedRequiredLiteralPrefixBytes;
                             count++)
                        {
                            AppendSharedPrefixBytes(prefix, childPrefix);
                        }
                    }
                }

                reachedUnboundedBarrier = true;
                return true;

            case RegexRepetitionNode repetition when repetition.Maximum == repetition.Minimum:
                if (repetition.Minimum == 0)
                {
                    return true;
                }

                var fixedChildPrefix = new List<byte>();
                if (!TryAppendEligibleSharedPrefixBranch(
                        repetition.Child,
                        options,
                        fixedChildPrefix,
                        out bool fixedChildReachedBarrier))
                {
                    return false;
                }

                AppendSharedPrefixBytes(prefix, fixedChildPrefix);
                if (fixedChildReachedBarrier)
                {
                    reachedUnboundedBarrier = true;
                    return true;
                }

                if (fixedChildPrefix.Count == 0)
                {
                    return true;
                }

                for (int count = 1;
                     count < repetition.Minimum && prefix.Count < MaximumSharedRequiredLiteralPrefixBytes;
                     count++)
                {
                    AppendSharedPrefixBytes(prefix, fixedChildPrefix);
                }

                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Appends bytes up to the bounded shared-prefix analysis limit.
    /// </summary>
    private static void AppendSharedPrefixBytes(List<byte> prefix, ReadOnlySpan<byte> bytes)
    {
        int count = Math.Min(bytes.Length, MaximumSharedRequiredLiteralPrefixBytes - prefix.Count);
        for (int index = 0; index < count; index++)
        {
            prefix.Add(bytes[index]);
        }
    }

    /// <summary>
    /// Appends list-backed bytes up to the bounded shared-prefix analysis limit.
    /// </summary>
    private static void AppendSharedPrefixBytes(List<byte> prefix, List<byte> bytes)
    {
        int count = Math.Min(bytes.Count, MaximumSharedRequiredLiteralPrefixBytes - prefix.Count);
        for (int index = 0; index < count; index++)
        {
            prefix.Add(bytes[index]);
        }
    }

    /// <summary>
    /// Finds the next exact-prefix candidate at or after one byte offset.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first offset to inspect.</param>
    /// <returns>The next candidate offset, or <c>-1</c> when none remains.</returns>
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

            if (_candidateGate is null ||
                _candidateGate.CanMatch(haystack, candidate, out int resumeAt))
            {
                return candidate;
            }

            searchAt = Math.Clamp(Math.Max(resumeAt, candidate + 1), 0, haystack.Length);
        }

        return -1;
    }

    /// <summary>
    /// Finds the next required-literal occurrence at or after one byte offset.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first offset to inspect.</param>
    /// <returns>The next required-literal offset, or <c>-1</c> when none remains.</returns>
    public int FindRequiredLiteral(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (_requiredMemmem is not null)
        {
            int offset = _requiredMemmem.Find(haystack[startAt..]);
            return offset < 0 ? -1 : startAt + offset;
        }

        if (_requiredLiteral is not null)
        {
            int offset = _requiredLiteral.Find(haystack[startAt..]);
            return offset < 0 ? -1 : startAt + offset;
        }

        if (_requiredTeddy is not null)
        {
            return _requiredTeddy.FindCandidate(haystack, startAt);
        }

        AhoCorasickMatch? match = _requiredLiterals!.Find(haystack[startAt..]);
        return match.HasValue ? startAt + match.Value.Start : -1;
    }

    /// <summary>
    /// Finds the next required literal while detecting NUL bytes in the inspected prefix.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first offset to inspect.</param>
    /// <param name="containsNul">Set to <see langword="true" /> when an inspected byte is NUL.</param>
    /// <returns>The next required-literal offset, or <c>-1</c> when none remains.</returns>
    public int FindRequiredLiteralAndDetectNul(
        ReadOnlySpan<byte> haystack,
        int startAt,
        ref bool containsNul)
    {
        if (_requiredMemmem is null)
        {
            throw new InvalidOperationException("NUL detection requires a case-sensitive required literal.");
        }

        int offset = _requiredMemmem.FindAndDetectNul(haystack[startAt..], ref containsNul);
        return offset < 0 ? -1 : startAt + offset;
    }

    /// <summary>
    /// Gets the conservative or reverse-prefix-narrowed start range for one required literal.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="requiredAt">The byte offset at which the required literal begins.</param>
    /// <param name="minStart">The first permitted match start.</param>
    /// <param name="maxStart">The last permitted match start.</param>
    /// <param name="rangeStart">Receives the first candidate start.</param>
    /// <param name="rangeEnd">Receives the last candidate start.</param>
    /// <returns><see langword="true"/> when the range is nonempty.</returns>
    public bool TryGetRequiredLiteralRange(
        ReadOnlySpan<byte> haystack,
        int requiredAt,
        int minStart,
        int maxStart,
        out int rangeStart,
        out int rangeEnd)
    {
        rangeStart = Math.Max(minStart, requiredAt - RequiredLiteralWindow);
        rangeEnd = Math.Min(maxStart, requiredAt);
        if (rangeStart > rangeEnd)
        {
            return false;
        }

        return _requiredLiteralPrefixGate is null ||
            _requiredLiteralPrefixGate.TryNarrowRange(
                haystack,
                requiredAt,
                rangeStart,
                rangeEnd,
                out rangeStart,
                out rangeEnd);
    }

    private static RegexPrefilter CreateRequiredLiteralPrefilter(
        byte[][] preparedLiterals,
        bool asciiCaseInsensitive,
        RegexStartPredicate? startPredicate,
        int maxLookBehind,
        RegexRequiredLiteralPrefixGate? requiredLiteralPrefixGate = null,
        Func<RegexStartPredicate?>? startPredicateFactory = null)
    {
        if (!asciiCaseInsensitive &&
            maxLookBehind == 0 &&
            TryGetSharedRequiredLiteralPrefix(preparedLiterals, out byte[] sharedPrefix))
        {
            preparedLiterals = [sharedPrefix];
        }

        if (preparedLiterals.Length == 1)
        {
            if (!asciiCaseInsensitive)
            {
                return new RegexPrefilter(
                    RegexPrefilterKind.RequiredLiteral,
                    memmem: null,
                    teddy: null,
                    ahoCorasick: null,
                    requiredLiteralPrefixGate: requiredLiteralPrefixGate,
                    requiredMemmem: new MemmemFinder(preparedLiterals[0]),
                    startPredicate: startPredicate,
                    startPredicateFactory: startPredicateFactory,
                    requiredLiteralWindow: maxLookBehind);
            }

            return new RegexPrefilter(
                RegexPrefilterKind.RequiredLiteral,
                memmem: null,
                teddy: null,
                ahoCorasick: null,
                requiredLiteralPrefixGate: requiredLiteralPrefixGate,
                requiredLiteral: new RegexAsciiCaseInsensitiveFinder(preparedLiterals[0]),
                startPredicate: startPredicate,
                startPredicateFactory: startPredicateFactory,
                requiredLiteralWindow: maxLookBehind);
        }

        if (RegexTeddyPrefilter.TryCreate(preparedLiterals, asciiCaseInsensitive, out RegexTeddyPrefilter? requiredTeddy))
        {
            return new RegexPrefilter(
                RegexPrefilterKind.RequiredLiteral,
                memmem: null,
                teddy: null,
                ahoCorasick: null,
                requiredLiteralPrefixGate: requiredLiteralPrefixGate,
                requiredTeddy: requiredTeddy,
                startPredicate: startPredicate,
                startPredicateFactory: startPredicateFactory,
                requiredLiteralWindow: maxLookBehind);
        }

        return new RegexPrefilter(
            RegexPrefilterKind.RequiredLiteral,
            memmem: null,
            teddy: null,
            ahoCorasick: null,
            requiredLiteralPrefixGate: requiredLiteralPrefixGate,
            requiredLiterals: AhoCorasickAutomaton.Create(preparedLiterals, AhoCorasickMatchKind.LeftmostFirst, asciiCaseInsensitive),
            startPredicate: startPredicate,
            startPredicateFactory: startPredicateFactory,
            requiredLiteralWindow: maxLookBehind);
    }

    /// <summary>
    /// Extracts a selective prefix shared by every exact-start required literal.
    /// </summary>
    /// <param name="literals">The required literals.</param>
    /// <param name="prefix">Receives the shared prefix.</param>
    /// <returns><see langword="true" /> when a sufficiently selective prefix exists.</returns>
    private static bool TryGetSharedRequiredLiteralPrefix(byte[][] literals, out byte[] prefix)
    {
        prefix = [];
        if (literals.Length < 2)
        {
            return false;
        }

        int length = literals[0].Length;
        for (int literalIndex = 1;
             literalIndex < literals.Length && length >= MinimumSharedRequiredLiteralPrefixBytes;
             literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            length = Math.Min(length, literal.Length);
            int index = 0;
            while (index < length && literals[0][index] == literal[index])
            {
                index++;
            }

            length = index;
        }

        if (length < MinimumSharedRequiredLiteralPrefixBytes)
        {
            return false;
        }

        prefix = literals[0].AsSpan(0, length).ToArray();
        return true;
    }

    private int FindRawCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (_memmem is not null)
        {
            int offset = _memmem.Find(haystack[startAt..]);
            return offset < 0 ? -1 : startAt + offset;
        }

        if (_teddy is not null)
        {
            return _teddy.FindCandidate(haystack, startAt);
        }

        AhoCorasickMatch? match = _ahoCorasick!.Find(haystack[startAt..]);
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

        var collected = new List<byte[]>();
        bool caseInsensitivePrefixes = false;
        bool unicodeCaseInsensitivePrefixes = false;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryCollectLeadingPrefixSet(
                    alternation.Alternatives[index],
                    options,
                    out byte[][] prefixes,
                    out bool alternativeCaseInsensitive,
                    out bool alternativeUnicodeCaseInsensitive) ||
                !TryAddPrefixLiterals(collected, prefixes))
            {
                return false;
            }

            caseInsensitivePrefixes |= alternativeCaseInsensitive;
            unicodeCaseInsensitivePrefixes |= alternativeUnicodeCaseInsensitive;
        }

        return TryCreatePrefixSetPrefilter(
            collected.ToArray(),
            options,
            candidateGate: null,
            caseInsensitivePrefixes,
            unicodeCaseInsensitivePrefixes,
            out prefilter);
    }

    private static bool TryCreatePrefixSetPrefilter(
        byte[][] prefixes,
        RegexCompileOptions options,
        RegexPrefixCandidateGate? candidateGate,
        out RegexPrefilter? prefilter)
    {
        return TryCreatePrefixSetPrefilter(
            prefixes,
            options,
            candidateGate,
            options.CaseInsensitive,
            options.CaseInsensitive && options.UnicodeClasses,
            out prefilter);
    }

    private static bool TryCreatePrefixSetPrefilter(
        byte[][] prefixes,
        RegexCompileOptions options,
        RegexPrefixCandidateGate? candidateGate,
        bool caseInsensitivePrefixes,
        bool unicodeCaseInsensitivePrefixes,
        out RegexPrefilter? prefilter)
    {
        return TryCreatePrefixSetPrefilter(
            prefixes,
            options,
            candidateGate,
            caseInsensitivePrefixes,
            unicodeCaseInsensitivePrefixes,
            out prefilter,
            out _);
    }

    private static bool TryCreatePrefixSetPrefilter(
        byte[][] prefixes,
        RegexCompileOptions options,
        RegexPrefixCandidateGate? candidateGate,
        bool caseInsensitivePrefixes,
        bool unicodeCaseInsensitivePrefixes,
        out RegexPrefilter? prefilter,
        out bool rejectedLowSelectivityPrefixSet)
    {
        prefilter = null;
        rejectedLowSelectivityPrefixSet = false;
        if (prefixes.Length < 2)
        {
            return false;
        }

        byte[][] preparedPrefixes = prefixes;
        bool asciiCaseInsensitive = false;
        if (unicodeCaseInsensitivePrefixes)
        {
            var prefixOptions = new RegexCompileOptions(
                caseInsensitive: true,
                options.SwapGreed,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                unicodeClasses: true);
            if (!TryPreparePrefixLiteralSet(prefixes, prefixOptions, out preparedPrefixes))
            {
                return false;
            }

            asciiCaseInsensitive = true;
        }
        else if (caseInsensitivePrefixes)
        {
            asciiCaseInsensitive = true;
        }

        if (IsLowSelectivityPrefixSet(preparedPrefixes))
        {
            rejectedLowSelectivityPrefixSet = true;
            return false;
        }

        if (!asciiCaseInsensitive &&
            RegexTeddyPrefilter.TryCreate(preparedPrefixes, out RegexTeddyPrefilter? teddy))
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
            ahoCorasick: AhoCorasickAutomaton.Create(preparedPrefixes, AhoCorasickMatchKind.LeftmostFirst, asciiCaseInsensitive),
            candidateGate);
        return true;
    }

    private static bool IsLowSelectivityPrefixSet(byte[][] prefixes)
    {
        if (prefixes.Length <= LowSelectivityPrefixSetThreshold)
        {
            return false;
        }

        for (int index = 0; index < prefixes.Length; index++)
        {
            byte[] prefix = prefixes[index];
            if (prefix.Length == 1 &&
                prefix[0] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r')
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCollectSequenceAlternationPrefixes(RegexSyntaxNode node, RegexCompileOptions options, out byte[][]? prefixes)
    {
        return TryCollectSequenceAlternationPrefixes(
            node,
            options,
            out prefixes,
            out _,
            out _);
    }

    internal static bool TryCollectSequenceAlternationPrefixes(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out byte[][]? prefixes,
        out bool caseInsensitivePrefixes,
        out bool unicodeCaseInsensitivePrefixes)
    {
        prefixes = null;
        caseInsensitivePrefixes = false;
        unicodeCaseInsensitivePrefixes = false;
        node = UnwrapTransparentGroups(node);
        if (node is RegexAlternationNode alternation)
        {
            var collected = new List<byte[]>();
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryCollectLeadingPrefixSet(
                        alternation.Alternatives[index],
                        options,
                        out byte[][] alternativePrefixes,
                        out bool alternativeCaseInsensitive,
                        out bool alternativeUnicodeCaseInsensitive) ||
                    !TryAddPrefixLiterals(collected, alternativePrefixes))
                {
                    prefixes = null;
                    return false;
                }

                caseInsensitivePrefixes |= alternativeCaseInsensitive;
                unicodeCaseInsensitivePrefixes |= alternativeUnicodeCaseInsensitive;
            }

            prefixes = collected.ToArray();
            return true;
        }

        if (node is RegexGroupNode group)
        {
            RegexCompileOptions groupOptions = options.Apply(group.EnabledFlags, group.DisabledFlags);
            return TryCollectSequenceAlternationPrefixes(
                group.Child,
                groupOptions,
                out prefixes,
                out caseInsensitivePrefixes,
                out unicodeCaseInsensitivePrefixes);
        }

        if (node is RegexRepetitionNode repetition)
        {
            return repetition.Minimum > 0 &&
                TryCollectSequenceAlternationPrefixes(
                    repetition.Child,
                    options,
                    out prefixes,
                    out caseInsensitivePrefixes,
                    out unicodeCaseInsensitivePrefixes);
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

            if (IsZeroWidthPrefixTransparent(child, currentOptions))
            {
                continue;
            }

            return TryCollectSequenceAlternationPrefixes(
                child,
                currentOptions,
                out prefixes,
                out caseInsensitivePrefixes,
                out unicodeCaseInsensitivePrefixes);
        }

        return false;
    }

    private static bool IsZeroWidthPrefixTransparent(RegexSyntaxNode node, RegexCompileOptions options)
    {
        return TryGetMaximumByteLength(node, options, out int maximum) && maximum == 0;
    }

    private static bool TryCollectLeadingPrefixSet(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out byte[][] prefixes,
        out bool caseInsensitivePrefixes,
        out bool unicodeCaseInsensitivePrefixes)
    {
        prefixes = [];
        caseInsensitivePrefixes = false;
        unicodeCaseInsensitivePrefixes = false;
        if (!TryCollectLeadingPrefixCandidates(node, options, out List<RegexPrefixCandidate> candidates))
        {
            return false;
        }

        var collected = new List<byte[]>();
        for (int index = 0; index < candidates.Count; index++)
        {
            byte[] prefix = candidates[index].Bytes;
            if (prefix.Length == 0 ||
                !TryAddPrefixLiteral(collected, prefix))
            {
                return false;
            }

            caseInsensitivePrefixes |= candidates[index].CaseInsensitive;
            unicodeCaseInsensitivePrefixes |= candidates[index].CaseInsensitive && candidates[index].UnicodeClasses;
        }

        prefixes = collected.ToArray();
        return prefixes.Length > 0;
    }

    private static bool TryCollectLeadingPrefixCandidates(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out List<RegexPrefixCandidate> candidates)
    {
        candidates = [];
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.StartAnchor:
            case RegexSyntaxKind.EndAnchor:
            case RegexSyntaxKind.AbsoluteStartAnchor:
            case RegexSyntaxKind.AbsoluteEndAnchor:
            case RegexSyntaxKind.WordBoundary:
            case RegexSyntaxKind.NotWordBoundary:
            case RegexSyntaxKind.WordStartBoundary:
            case RegexSyntaxKind.WordEndBoundary:
            case RegexSyntaxKind.WordStartHalfBoundary:
            case RegexSyntaxKind.WordEndHalfBoundary:
                candidates.Add(new RegexPrefixCandidate([], sealedPrefix: false, PreferredPrefixBytes));
                return true;
            case RegexSyntaxKind.Literal:
                byte[] literal = ((RegexAtomNode)node).Value.ToArray();
                candidates.Add(new RegexPrefixCandidate(
                    literal,
                    sealedPrefix: false,
                    PreferredPrefixBytes,
                    options.CaseInsensitive,
                    options.UnicodeClasses));
                return true;
            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.LetterClass:
            case RegexSyntaxKind.AlphanumericClass:
                if (!TryGetPrefixAtomVariants((RegexAtomNode)node, options, out byte[][] variants, out bool sealVariants))
                {
                    return false;
                }

                for (int index = 0; index < variants.Length; index++)
                {
                    if (!TryAddPrefixCandidate(candidates, variants[index], sealVariants))
                    {
                        return false;
                    }
                }

                return candidates.Count > 0;
            case RegexSyntaxKind.Sequence:
                return TryCollectSequencePrefixCandidates((RegexSequenceNode)node, options, out candidates);
            case RegexSyntaxKind.Alternation:
                return TryCollectAlternationPrefixCandidates((RegexAlternationNode)node, options, out candidates);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryCollectLeadingPrefixCandidates(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out candidates);
            case RegexSyntaxKind.Repetition:
                return TryCollectRepetitionPrefixCandidates((RegexRepetitionNode)node, options, out candidates);
            default:
                return false;
        }
    }

    private static bool TryCollectSequencePrefixCandidates(
        RegexSequenceNode node,
        RegexCompileOptions options,
        out List<RegexPrefixCandidate> candidates)
    {
        candidates = [new RegexPrefixCandidate([], sealedPrefix: false, PreferredPrefixBytes)];
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryCollectLeadingPrefixCandidates(child, currentOptions, out List<RegexPrefixCandidate> childCandidates) ||
                !TryAppendPrefixCandidates(candidates, childCandidates, out candidates))
            {
                return false;
            }

            if (AllPrefixCandidatesSealed(candidates))
            {
                break;
            }
        }

        return candidates.Count > 0;
    }

    private static bool TryCollectAlternationPrefixCandidates(
        RegexAlternationNode node,
        RegexCompileOptions options,
        out List<RegexPrefixCandidate> candidates)
    {
        candidates = [];
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (!TryCollectLeadingPrefixCandidates(node.Alternatives[index], options, out List<RegexPrefixCandidate> alternativeCandidates))
            {
                return false;
            }

            for (int candidateIndex = 0; candidateIndex < alternativeCandidates.Count; candidateIndex++)
            {
                RegexPrefixCandidate candidate = alternativeCandidates[candidateIndex];
                if (!TryAddPrefixCandidate(candidates, candidate))
                {
                    return false;
                }
            }
        }

        return candidates.Count > 0;
    }

    private static bool TryCollectRepetitionPrefixCandidates(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        out List<RegexPrefixCandidate> candidates)
    {
        candidates = [new RegexPrefixCandidate([], sealedPrefix: false, PreferredPrefixBytes)];
        if (node.Maximum == 0)
        {
            return true;
        }

        if (!TryCollectLeadingPrefixCandidates(node.Child, options, out List<RegexPrefixCandidate> childCandidates))
        {
            return false;
        }

        bool hasEmptyCandidate = false;
        bool hasNonEmptyCandidate = false;
        for (int index = 0; index < childCandidates.Count; index++)
        {
            if (childCandidates[index].Bytes.Length == 0)
            {
                hasEmptyCandidate = true;
            }
            else
            {
                hasNonEmptyCandidate = true;
            }
        }

        if (hasEmptyCandidate)
        {
            if (!hasNonEmptyCandidate)
            {
                candidates = childCandidates;
                return true;
            }

            return false;
        }

        int required = node.Minimum;
        if (required == 0)
        {
            var optionalCandidates = new List<RegexPrefixCandidate> { new([], sealedPrefix: false, PreferredPrefixBytes) };
            List<RegexPrefixCandidate> repeatedCandidates = SealPrefixCandidates(childCandidates, node.Maximum is null || node.Maximum > 1);
            for (int index = 0; index < repeatedCandidates.Count; index++)
            {
                RegexPrefixCandidate candidate = repeatedCandidates[index];
                if (!TryAddPrefixCandidate(optionalCandidates, candidate))
                {
                    return false;
                }
            }

            candidates = optionalCandidates;
            return candidates.Count > 0;
        }

        for (int count = 0; count < required; count++)
        {
            if (!TryAppendPrefixCandidates(candidates, childCandidates, out candidates))
            {
                return false;
            }

            if (AllPrefixCandidatesSealed(candidates))
            {
                break;
            }
        }

        if (node.Maximum is null || node.Maximum > node.Minimum)
        {
            candidates = SealPrefixCandidates(candidates, sealAllNonEmpty: true);
        }

        return candidates.Count > 0;
    }

    private static bool TryAppendPrefixCandidates(
        List<RegexPrefixCandidate> prefixes,
        List<RegexPrefixCandidate> suffixes,
        out List<RegexPrefixCandidate> combined)
    {
        combined = [];
        for (int prefixIndex = 0; prefixIndex < prefixes.Count; prefixIndex++)
        {
            RegexPrefixCandidate prefix = prefixes[prefixIndex];
            if (prefix.Sealed)
            {
                if (!TryAddPrefixCandidate(
                    combined,
                    new RegexPrefixCandidate(
                        prefix.Bytes,
                        sealedPrefix: true,
                        PreferredPrefixBytes,
                        prefix.CaseInsensitive,
                        prefix.UnicodeClasses)))
                {
                    return false;
                }

                continue;
            }

            for (int suffixIndex = 0; suffixIndex < suffixes.Count; suffixIndex++)
            {
                RegexPrefixCandidate suffix = suffixes[suffixIndex];
                if (suffix.Bytes.Length == 0)
                {
                    if (!TryAddPrefixCandidate(combined, prefix))
                    {
                        return false;
                    }

                    continue;
                }

                byte[] bytes = new byte[prefix.Bytes.Length + suffix.Bytes.Length];
                prefix.Bytes.CopyTo(bytes, 0);
                suffix.Bytes.CopyTo(bytes, prefix.Bytes.Length);
                if (!TryAddPrefixCandidate(
                    combined,
                    new RegexPrefixCandidate(
                        bytes,
                        suffix.Sealed,
                        PreferredPrefixBytes,
                        prefix.CaseInsensitive || suffix.CaseInsensitive,
                        prefix.UnicodeClasses || suffix.UnicodeClasses)))
                {
                    return false;
                }
            }
        }

        return combined.Count > 0;
    }

    private static List<RegexPrefixCandidate> SealPrefixCandidates(List<RegexPrefixCandidate> candidates, bool sealAllNonEmpty)
    {
        var sealedCandidates = new List<RegexPrefixCandidate>(candidates.Count);
        for (int index = 0; index < candidates.Count; index++)
        {
            RegexPrefixCandidate candidate = candidates[index];
            bool sealedPrefix = candidate.Sealed || sealAllNonEmpty && candidate.Bytes.Length > 0;
            TryAddPrefixCandidate(
                sealedCandidates,
                new RegexPrefixCandidate(
                    candidate.Bytes,
                    sealedPrefix,
                    PreferredPrefixBytes,
                    candidate.CaseInsensitive,
                    candidate.UnicodeClasses));
        }

        return sealedCandidates;
    }

    private static bool TryAddPrefixLiterals(List<byte[]> target, byte[][] prefixes)
    {
        for (int index = 0; index < prefixes.Length; index++)
        {
            if (!TryAddPrefixLiteral(target, prefixes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAddPrefixLiteral(List<byte[]> target, byte[] prefix)
    {
        for (int index = 0; index < target.Count; index++)
        {
            byte[] existing = target[index];
            if (existing.AsSpan().SequenceEqual(prefix) ||
                StartsWith(prefix, existing))
            {
                return true;
            }

            if (StartsWith(existing, prefix))
            {
                target.RemoveAt(index);
                index--;
            }
        }

        target.Add(prefix.ToArray());
        return target.Count <= MaxExpandedRequiredLiteralVariants;
    }

    private static bool TryAddPrefixCandidate(List<RegexPrefixCandidate> target, byte[] bytes, bool sealedPrefix)
    {
        var candidate = new RegexPrefixCandidate(bytes.ToArray(), sealedPrefix, PreferredPrefixBytes);
        return TryAddPrefixCandidate(target, candidate);
    }

    private static bool TryAddPrefixCandidate(List<RegexPrefixCandidate> target, RegexPrefixCandidate candidate)
    {
        for (int index = 0; index < target.Count; index++)
        {
            RegexPrefixCandidate existing = target[index];
            if (existing.Bytes.AsSpan().SequenceEqual(candidate.Bytes))
            {
                target[index] = existing.Merge(candidate);
                return true;
            }

            if (existing.Sealed && StartsWith(candidate.Bytes, existing.Bytes))
            {
                return true;
            }

            if (candidate.Sealed && StartsWith(existing.Bytes, candidate.Bytes))
            {
                target.RemoveAt(index);
                index--;
            }
        }

        target.Add(candidate);
        return target.Count <= MaxPrefixVariants;
    }

    private static bool AllPrefixCandidatesSealed(List<RegexPrefixCandidate> candidates)
    {
        for (int index = 0; index < candidates.Count; index++)
        {
            if (!candidates[index].Sealed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWith(byte[] value, byte[] prefix)
    {
        return prefix.Length <= value.Length &&
            value.AsSpan(0, prefix.Length).SequenceEqual(prefix);
    }

    private static bool TryAppendRequiredPrefix(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool canContinue,
        ref bool prefixCaseInsensitive,
        ref bool prefixUnicodeClasses)
    {
        int originalCount = prefix.Count;
        canContinue = false;
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                canContinue = true;
                return true;
            case RegexSyntaxKind.Literal:
                prefix.AddRange(((RegexAtomNode)node).Value.ToArray());
                canContinue = true;
                if (prefix.Count > originalCount)
                {
                    prefixCaseInsensitive |= options.CaseInsensitive;
                    prefixUnicodeClasses |= options.UnicodeClasses;
                }

                return prefix.Count > originalCount;
            case RegexSyntaxKind.Sequence:
                return TryAppendSequencePrefix((RegexSequenceNode)node, options, prefix, out canContinue, ref prefixCaseInsensitive, ref prefixUnicodeClasses);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                return TryAppendGroupPrefix((RegexGroupNode)node, options, prefix, out canContinue, ref prefixCaseInsensitive, ref prefixUnicodeClasses);
            case RegexSyntaxKind.Repetition:
                return TryAppendRepetitionPrefix((RegexRepetitionNode)node, options, prefix, out canContinue, ref prefixCaseInsensitive, ref prefixUnicodeClasses);
            default:
                return false;
        }
    }

    private static bool TryAppendSequencePrefix(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool canContinue,
        ref bool prefixCaseInsensitive,
        ref bool prefixUnicodeClasses)
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

            if (!TryAppendRequiredPrefix(child, currentOptions, prefix, out bool childCanContinue, ref prefixCaseInsensitive, ref prefixUnicodeClasses))
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
        out bool canContinue,
        ref bool prefixCaseInsensitive,
        ref bool prefixUnicodeClasses)
    {
        RegexCompileOptions groupOptions = options.Apply(node.EnabledFlags, node.DisabledFlags);
        return TryAppendRequiredPrefix(node.Child, groupOptions, prefix, out canContinue, ref prefixCaseInsensitive, ref prefixUnicodeClasses);
    }

    private static bool TryAppendRepetitionPrefix(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<byte> prefix,
        out bool canContinue,
        ref bool prefixCaseInsensitive,
        ref bool prefixUnicodeClasses)
    {
        canContinue = false;
        if (node.Minimum == 0)
        {
            return false;
        }

        return TryAppendRequiredPrefix(node.Child, options, prefix, out _, ref prefixCaseInsensitive, ref prefixUnicodeClasses);
    }

    internal static bool TryCollectRequiredLiteralSet(RegexSyntaxNode node, RegexCompileOptions options, out byte[][] literals)
    {
        if (TryCollectRequiredLiteralSet(node, options, out RegexRequiredLiteralSetCandidate candidate))
        {
            literals = candidate.Literals;
            return true;
        }

        literals = [];
        return false;
    }

    private static bool TryCollectRequiredLiteralSet(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        if (TryFindRequiredLiteralCandidate(node, options, out candidate) &&
            candidate.Literals.Length == 1 &&
            candidate.Literals[0].Length >= 3)
        {
            return true;
        }

        candidate = default;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Sequence:
                return TryCollectRequiredLiteralSetInSequence((RegexSequenceNode)node, options, out candidate);
            case RegexSyntaxKind.Alternation:
                return TryCollectRequiredLiteralSetInAlternation((RegexAlternationNode)node, options, out candidate);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryCollectRequiredLiteralSet(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), out candidate);
            case RegexSyntaxKind.Repetition:
                var repetition = (RegexRepetitionNode)node;
                return repetition.Minimum > 0 &&
                    TryCollectRequiredLiteralSet(repetition.Child, options, out candidate);
            default:
                return false;
        }
    }

    internal static bool TryCollectRequiredLiteralSetWithLookBehind(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out byte[][] literals,
        out int maxLookBehind)
    {
        if (TryCollectRequiredLiteralSetWithLookBehind(node, options, out RegexRequiredLiteralSetCandidate candidate))
        {
            literals = candidate.Literals;
            maxLookBehind = candidate.MaxLookBehind;
            return true;
        }

        literals = [];
        maxLookBehind = RequiredLiteralLookBehind;
        return false;
    }

    private static bool TryCollectRequiredLiteralSetWithLookBehind(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        if (TryCollectRequiredLiteralSetCandidate(node, options, out candidate) &&
            candidate.Literals.Length > 0)
        {
            return true;
        }

        if (TryFindRequiredLiteralWithLookBehind(node, options, out candidate) &&
            candidate.Literals.Length == 1 &&
            candidate.Literals[0].Length >= 3)
        {
            return true;
        }

        return false;
    }

    private static bool TryCollectRequiredLiteralSetCandidate(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Sequence:
                return TryCollectRequiredLiteralSetCandidateInSequence((RegexSequenceNode)node, options, out candidate);
            case RegexSyntaxKind.Alternation:
                return TryCollectRequiredLiteralSetCandidateInAlternation((RegexAlternationNode)node, options, out candidate);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryCollectRequiredLiteralSetCandidate(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out candidate);
            case RegexSyntaxKind.Repetition:
                var repetition = (RegexRepetitionNode)node;
                return repetition.Minimum > 0 &&
                    TryCollectRequiredLiteralSetCandidate(repetition.Child, options, out candidate);
            default:
                return false;
        }
    }

    private static bool TryCollectRequiredLiteralSetCandidateInAlternation(
        RegexAlternationNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        var collected = new List<byte[]>();
        int maxLookBehind = 0;
        bool caseInsensitive = false;
        bool unicodeClasses = false;
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (!TryCollectRequiredLiteralSetWithLookBehind(
                    node.Alternatives[index],
                    options,
                    out RegexRequiredLiteralSetCandidate childCandidate) ||
                childCandidate.Literals.Length == 0)
            {
                return false;
            }

            collected.AddRange(childCandidate.Literals);
            maxLookBehind = Math.Max(maxLookBehind, childCandidate.MaxLookBehind);
            caseInsensitive |= childCandidate.CaseInsensitive;
            unicodeClasses |= childCandidate.UnicodeClasses;
        }

        if (collected.Count == 0)
        {
            return false;
        }

        candidate = new RegexRequiredLiteralSetCandidate(
            collected.ToArray(),
            maxLookBehind,
            caseInsensitive,
            unicodeClasses);
        return true;
    }

    private static bool TryCollectRequiredLiteralSetCandidateInSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        byte[][] bestLiterals = [];
        int bestLookBehind = 0;
        bool bestCaseInsensitive = false;
        bool bestUnicodeClasses = false;
        bool hasBest = false;
        var run = new List<byte[]>();
        int runLookBehind = 0;
        bool runHasBound = false;
        bool runCaseInsensitive = false;
        bool runUnicodeClasses = false;
        int prefixMax = 0;
        bool prefixKnown = true;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                FlushRequiredLiteralRunWithLookBehind(
                    run,
                    runHasBound,
                    runLookBehind,
                    runCaseInsensitive,
                    runUnicodeClasses,
                    ref bestLiterals,
                    ref bestLookBehind,
                    ref bestCaseInsensitive,
                    ref bestUnicodeClasses,
                    ref hasBest);
                run.Clear();
                runHasBound = false;
                runCaseInsensitive = false;
                runUnicodeClasses = false;
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (TryAppendRequiredLiteralRun(
                child,
                currentOptions,
                run,
                out bool appended,
                out bool canContinue,
                ref runCaseInsensitive,
                ref runUnicodeClasses))
            {
                if (appended)
                {
                    if (!runHasBound)
                    {
                        runLookBehind = prefixMax;
                        runHasBound = prefixKnown;
                    }

                    FlushRequiredLiteralRunWithLookBehind(
                        run,
                        runHasBound,
                        runLookBehind,
                        runCaseInsensitive,
                        runUnicodeClasses,
                        ref bestLiterals,
                        ref bestLookBehind,
                        ref bestCaseInsensitive,
                        ref bestUnicodeClasses,
                        ref hasBest);
                }

                if (!canContinue)
                {
                    FlushRequiredLiteralRunWithLookBehind(
                        run,
                        runHasBound,
                        runLookBehind,
                        runCaseInsensitive,
                        runUnicodeClasses,
                        ref bestLiterals,
                        ref bestLookBehind,
                        ref bestCaseInsensitive,
                        ref bestUnicodeClasses,
                        ref hasBest);
                    run.Clear();
                    runHasBound = false;
                    runCaseInsensitive = false;
                    runUnicodeClasses = false;
                }

                UpdateKnownPrefixMax(child, currentOptions, ref prefixKnown, ref prefixMax);
                if (!prefixKnown)
                {
                    break;
                }

                continue;
            }

            FlushRequiredLiteralRunWithLookBehind(
                run,
                runHasBound,
                runLookBehind,
                runCaseInsensitive,
                runUnicodeClasses,
                ref bestLiterals,
                ref bestLookBehind,
                ref bestCaseInsensitive,
                ref bestUnicodeClasses,
                ref hasBest);
            run.Clear();
            runHasBound = false;
            runCaseInsensitive = false;
            runUnicodeClasses = false;
            if (prefixKnown &&
                TryCollectRequiredLiteralSetCandidate(child, currentOptions, out RegexRequiredLiteralSetCandidate childCandidate))
            {
                int childLookBehind = AddLookBehind(prefixMax, childCandidate.MaxLookBehind);
                if (IsBetterRequiredLiteralSetCandidate(
                    childCandidate.Literals,
                    childLookBehind,
                    bestLiterals,
                    bestLookBehind,
                    hasBest,
                    preferEqual: true))
                {
                    bestLiterals = CopyLiteralSet(childCandidate.Literals);
                    bestLookBehind = childLookBehind;
                    bestCaseInsensitive = childCandidate.CaseInsensitive;
                    bestUnicodeClasses = childCandidate.UnicodeClasses;
                    hasBest = true;
                }
            }

            UpdateKnownPrefixMax(child, currentOptions, ref prefixKnown, ref prefixMax);
            if (!prefixKnown)
            {
                break;
            }
        }

        FlushRequiredLiteralRunWithLookBehind(
            run,
            runHasBound,
            runLookBehind,
            runCaseInsensitive,
            runUnicodeClasses,
            ref bestLiterals,
            ref bestLookBehind,
            ref bestCaseInsensitive,
            ref bestUnicodeClasses,
            ref hasBest);
        if (!hasBest)
        {
            return false;
        }

        candidate = new RegexRequiredLiteralSetCandidate(
            bestLiterals,
            bestLookBehind,
            bestCaseInsensitive,
            bestUnicodeClasses);
        return true;
    }

    private static void FlushRequiredLiteralRunWithLookBehind(
        List<byte[]> run,
        bool runHasBound,
        int runLookBehind,
        bool runCaseInsensitive,
        bool runUnicodeClasses,
        ref byte[][] bestLiterals,
        ref int bestLookBehind,
        ref bool bestCaseInsensitive,
        ref bool bestUnicodeClasses,
        ref bool hasBest)
    {
        if (run.Count == 0 || !runHasBound)
        {
            return;
        }

        byte[][] candidate = CopyLiteralSet(run);
        if (IsBetterRequiredLiteralSetCandidate(
            candidate,
            runLookBehind,
            bestLiterals,
            bestLookBehind,
            hasBest,
            preferEqual: true))
        {
            bestLiterals = candidate;
            bestLookBehind = runLookBehind;
            bestCaseInsensitive = runCaseInsensitive;
            bestUnicodeClasses = runUnicodeClasses;
            hasBest = true;
        }
    }

    private static bool IsBetterRequiredLiteralSetCandidate(
        byte[][] candidate,
        int candidateLookBehind,
        byte[][] current,
        int currentLookBehind,
        bool hasCurrent,
        bool preferEqual = false)
    {
        if (!hasCurrent || current.Length == 0)
        {
            return true;
        }

        int candidateScore = RequiredLiteralSetScore(candidate);
        int currentScore = RequiredLiteralSetScore(current);
        if (candidateScore != currentScore)
        {
            return preferEqual
                ? candidateScore >= currentScore
                : candidateScore > currentScore;
        }

        if (preferEqual &&
            candidateLookBehind > currentLookBehind &&
            candidateLookBehind <= MaxSelectiveRequiredLiteralLookBehind)
        {
            return true;
        }

        return candidateLookBehind < currentLookBehind;
    }

    /// <summary>
    /// Determines whether one prepared prefilter candidate has a better balance of selectivity,
    /// scanner cardinality, and lookbehind than the current candidate.
    /// </summary>
    /// <param name="candidate">The prepared candidate literals.</param>
    /// <param name="candidateLookBehind">The candidate's maximum lookbehind.</param>
    /// <param name="current">The prepared current literals.</param>
    /// <param name="currentLookBehind">The current candidate's maximum lookbehind.</param>
    /// <param name="hasCurrent">Whether a current candidate exists.</param>
    /// <returns><see langword="true" /> when the candidate should replace the current candidate.</returns>
    private static bool IsBetterRequiredLiteralPrefilterCandidate(
        byte[][] candidate,
        int candidateLookBehind,
        byte[][] current,
        int currentLookBehind,
        bool hasCurrent)
    {
        if (!hasCurrent || current.Length == 0)
        {
            return true;
        }

        int candidateScore = RequiredLiteralPrefilterScore(candidate);
        int currentScore = RequiredLiteralPrefilterScore(current);
        return candidateScore != currentScore
            ? candidateScore > currentScore
            : candidateLookBehind < currentLookBehind;
    }

    private static bool TryFindRequiredLiteralWithLookBehind(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Literal:
                byte[] literal = ((RegexAtomNode)node).Value.ToArray();
                candidate = new RegexRequiredLiteralSetCandidate(
                    [literal],
                    maxLookBehind: 0,
                    options.CaseInsensitive,
                    options.UnicodeClasses);
                return literal.Length > 0;
            case RegexSyntaxKind.Sequence:
                return TryFindRequiredLiteralWithLookBehindInSequence(
                    (RegexSequenceNode)node,
                    options,
                    out candidate);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryFindRequiredLiteralWithLookBehind(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out candidate);
            case RegexSyntaxKind.Repetition:
                var repetition = (RegexRepetitionNode)node;
                return repetition.Minimum > 0 &&
                    TryFindRequiredLiteralWithLookBehind(repetition.Child, options, out candidate);
            default:
                return false;
        }
    }

    private static bool TryFindRequiredLiteralWithLookBehindInSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        bool hasCandidate = false;
        var literalRun = new List<byte>();
        int runLookBehind = 0;
        bool runHasBound = false;
        bool runCaseInsensitive = false;
        bool runUnicodeClasses = false;
        int prefixMax = 0;
        bool prefixKnown = true;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                FlushLiteralRunWithLookBehind(
                    literalRun,
                    runHasBound,
                    runLookBehind,
                    runCaseInsensitive,
                    runUnicodeClasses,
                    ref candidate,
                    ref hasCandidate);
                runHasBound = false;
                runCaseInsensitive = false;
                runUnicodeClasses = false;
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (child is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
            {
                if (literalRun.Count == 0)
                {
                    runLookBehind = prefixMax;
                    runHasBound = prefixKnown;
                }

                literalRun.AddRange(atom.Value.ToArray());
                runCaseInsensitive |= currentOptions.CaseInsensitive;
                runUnicodeClasses |= currentOptions.UnicodeClasses;
                UpdateKnownPrefixMax(child, currentOptions, ref prefixKnown, ref prefixMax);
                if (!prefixKnown)
                {
                    break;
                }

                continue;
            }

            FlushLiteralRunWithLookBehind(
                literalRun,
                runHasBound,
                runLookBehind,
                runCaseInsensitive,
                runUnicodeClasses,
                ref candidate,
                ref hasCandidate);
            runHasBound = false;
            runCaseInsensitive = false;
            runUnicodeClasses = false;
            if (prefixKnown &&
                TryFindRequiredLiteralWithLookBehind(child, currentOptions, out RegexRequiredLiteralSetCandidate childCandidate))
            {
                int candidateLookBehind = AddLookBehind(prefixMax, childCandidate.MaxLookBehind);
                if (IsBetterRequiredLiteralSetCandidate(
                    childCandidate.Literals,
                    candidateLookBehind,
                    candidate.Literals,
                    candidate.MaxLookBehind,
                    hasCandidate))
                {
                    candidate = new RegexRequiredLiteralSetCandidate(
                        CopyLiteralSet(childCandidate.Literals),
                        candidateLookBehind,
                        childCandidate.CaseInsensitive,
                        childCandidate.UnicodeClasses);
                    hasCandidate = true;
                }
            }

            UpdateKnownPrefixMax(child, currentOptions, ref prefixKnown, ref prefixMax);
            if (!prefixKnown)
            {
                break;
            }
        }

        FlushLiteralRunWithLookBehind(
            literalRun,
            runHasBound,
            runLookBehind,
            runCaseInsensitive,
            runUnicodeClasses,
            ref candidate,
            ref hasCandidate);
        return hasCandidate && candidate.Literals.Length > 0;
    }

    private static void FlushLiteralRunWithLookBehind(
        List<byte> literalRun,
        bool runHasBound,
        int runLookBehind,
        bool runCaseInsensitive,
        bool runUnicodeClasses,
        ref RegexRequiredLiteralSetCandidate candidate,
        ref bool hasCandidate)
    {
        if (!runHasBound || literalRun.Count == 0)
        {
            literalRun.Clear();
            return;
        }

        byte[][] literals = [literalRun.ToArray()];
        if (IsBetterRequiredLiteralSetCandidate(
            literals,
            runLookBehind,
            candidate.Literals,
            candidate.MaxLookBehind,
            hasCandidate))
        {
            candidate = new RegexRequiredLiteralSetCandidate(
                literals,
                runLookBehind,
                runCaseInsensitive,
                runUnicodeClasses);
            hasCandidate = true;
        }

        literalRun.Clear();
    }

    private static void UpdateKnownPrefixMax(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        ref bool prefixKnown,
        ref int prefixMax)
    {
        if (!prefixKnown)
        {
            return;
        }

        if (!TryGetMaximumByteLength(node, options, out int childMax) ||
            prefixMax > int.MaxValue - childMax)
        {
            prefixKnown = false;
            return;
        }

        prefixMax += childMax;
    }

    private static int AddLookBehind(int prefixMax, int childLookBehind)
    {
        return prefixMax > int.MaxValue - childLookBehind
            ? int.MaxValue
            : prefixMax + childLookBehind;
    }

    /// <summary>
    /// Attempts to compute a finite maximum byte length for a syntax subtree.
    /// </summary>
    /// <param name="node">The syntax subtree.</param>
    /// <param name="options">The options in effect at the subtree root.</param>
    /// <param name="maximum">Receives the maximum byte length.</param>
    /// <returns><see langword="true"/> when the maximum is finite and representable.</returns>
    internal static bool TryGetMaximumByteLength(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int maximum)
    {
        maximum = 0;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.StartAnchor:
            case RegexSyntaxKind.EndAnchor:
            case RegexSyntaxKind.AbsoluteStartAnchor:
            case RegexSyntaxKind.AbsoluteEndAnchor:
            case RegexSyntaxKind.WordBoundary:
            case RegexSyntaxKind.NotWordBoundary:
            case RegexSyntaxKind.WordStartBoundary:
            case RegexSyntaxKind.WordEndBoundary:
            case RegexSyntaxKind.WordStartHalfBoundary:
            case RegexSyntaxKind.WordEndHalfBoundary:
            case RegexSyntaxKind.InlineFlags:
                return true;
            case RegexSyntaxKind.Literal:
                return TryGetMaximumLiteralByteLength(((RegexAtomNode)node).Value.Span, options, out maximum);
            case RegexSyntaxKind.Dot:
            case RegexSyntaxKind.AnyClass:
            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
            case RegexSyntaxKind.LetterClass:
            case RegexSyntaxKind.AlphanumericClass:
                maximum = options.Utf8 || options.UnicodeClasses ? 4 : 1;
                return true;
            case RegexSyntaxKind.UnicodePropertyClass:
            case RegexSyntaxKind.NotUnicodePropertyClass:
                maximum = 4;
                return true;
            case RegexSyntaxKind.Sequence:
                return TryGetMaximumSequenceByteLength((RegexSequenceNode)node, options, out maximum);
            case RegexSyntaxKind.Alternation:
                return TryGetMaximumAlternationByteLength((RegexAlternationNode)node, options, out maximum);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryGetMaximumByteLength(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out maximum);
            case RegexSyntaxKind.Repetition:
                return TryGetMaximumRepetitionByteLength((RegexRepetitionNode)node, options, out maximum);
            default:
                return false;
        }
    }

    private static bool TryGetMaximumSequenceByteLength(
        RegexSequenceNode node,
        RegexCompileOptions options,
        out int maximum)
    {
        maximum = 0;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryGetMaximumByteLength(child, currentOptions, out int childMax) ||
                maximum > int.MaxValue - childMax)
            {
                maximum = 0;
                return false;
            }

            maximum += childMax;
        }

        return true;
    }

    private static bool TryGetMaximumAlternationByteLength(
        RegexAlternationNode node,
        RegexCompileOptions options,
        out int maximum)
    {
        maximum = 0;
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (!TryGetMaximumByteLength(node.Alternatives[index], options, out int childMax))
            {
                maximum = 0;
                return false;
            }

            maximum = Math.Max(maximum, childMax);
        }

        return true;
    }

    private static bool TryGetMaximumRepetitionByteLength(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        out int maximum)
    {
        maximum = 0;
        if (!node.Maximum.HasValue)
        {
            return false;
        }

        if (!TryGetMaximumByteLength(node.Child, options, out int childMax) ||
            childMax != 0 && node.Maximum.Value > int.MaxValue / childMax)
        {
            return false;
        }

        maximum = childMax * node.Maximum.Value;
        return true;
    }

    private static bool TryGetMaximumLiteralByteLength(
        ReadOnlySpan<byte> literal,
        RegexCompileOptions options,
        out int maximum)
    {
        maximum = literal.Length;
        if (!options.CaseInsensitive || !options.UnicodeClasses)
        {
            return true;
        }

        if (!TryDecodeRunes(literal.ToArray(), out Rune[] runes))
        {
            maximum = 0;
            return false;
        }

        maximum = 0;
        for (int index = 0; index < runes.Length; index++)
        {
            byte[][] equivalents = GetCaseFoldEquivalentBytes(runes[index]);
            int runeMaximum = 0;
            for (int equivalentIndex = 0; equivalentIndex < equivalents.Length; equivalentIndex++)
            {
                runeMaximum = Math.Max(runeMaximum, equivalents[equivalentIndex].Length);
            }

            if (maximum > int.MaxValue - runeMaximum)
            {
                maximum = 0;
                return false;
            }

            maximum += runeMaximum;
        }

        return true;
    }

    internal static bool TryPrepareRequiredLiteralSet(
        byte[][] literals,
        RegexCompileOptions options,
        out byte[][] prepared)
    {
        return TryPrepareRequiredLiteralSet(literals, options.CaseInsensitive, options.UnicodeClasses, out prepared);
    }

    internal static bool IsRequiredLiteralSetAtLeastAsSelective(byte[][] candidate, byte[][] current)
    {
        return IsBetterRequiredLiteralSet(candidate, current, preferEqual: true);
    }

    private static bool TryPrepareRequiredLiteralSet(
        byte[][] literals,
        bool caseInsensitive,
        bool unicodeClasses,
        out byte[][] prepared)
    {
        prepared = [];
        var options = new RegexCompileOptions(
            caseInsensitive,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: unicodeClasses);
        var collected = new List<byte[]>();
        for (int index = 0; index < literals.Length; index++)
        {
            if (!TryAddPreparedRequiredLiteral(collected, literals[index], options))
            {
                return false;
            }

            if (collected.Count > MaxExpandedRequiredLiteralVariants)
            {
                return false;
            }
        }

        if (HasShortLiteral(collected))
        {
            return false;
        }

        prepared = collected.ToArray();
        return prepared.Length > 0;
    }

    internal static bool TryPreparePrefixLiteralSet(
        byte[][] literals,
        RegexCompileOptions options,
        out byte[][] prepared)
    {
        prepared = [];
        var collected = new List<byte[]>();
        for (int index = 0; index < literals.Length; index++)
        {
            if (options.UnicodeClasses)
            {
                if (!TryAddFullUnicodeCaseFoldLiteralVariants(collected, literals[index]))
                {
                    return false;
                }
            }
            else
            {
                AddDistinctLiteral(collected, NormalizeAsciiCase(literals[index]));
            }

            if (collected.Count > MaxExpandedRequiredLiteralVariants)
            {
                return false;
            }
        }

        prepared = collected.ToArray();
        return prepared.Length > 0;
    }

    private static bool TryCollectRequiredLiteralSetInAlternation(
        RegexAlternationNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        var collected = new List<byte[]>();
        bool caseInsensitive = false;
        bool unicodeClasses = false;
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            if (!TryCollectRequiredLiteralSet(node.Alternatives[index], options, out RegexRequiredLiteralSetCandidate childCandidate) ||
                childCandidate.Literals.Length == 0)
            {
                return false;
            }

            collected.AddRange(childCandidate.Literals);
            caseInsensitive |= childCandidate.CaseInsensitive;
            unicodeClasses |= childCandidate.UnicodeClasses;
        }

        if (collected.Count == 0)
        {
            return false;
        }

        candidate = new RegexRequiredLiteralSetCandidate(
            collected.ToArray(),
            RequiredLiteralLookBehind,
            caseInsensitive,
            unicodeClasses);
        return true;
    }

    private static bool TryCollectRequiredLiteralSetInSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        bool hasCandidate = false;
        var run = new List<byte[]>();
        bool runCaseInsensitive = false;
        bool runUnicodeClasses = false;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                FlushRequiredLiteralRun(run, runCaseInsensitive, runUnicodeClasses, ref candidate, ref hasCandidate);
                run.Clear();
                runCaseInsensitive = false;
                runUnicodeClasses = false;
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (TryAppendRequiredLiteralRun(
                child,
                currentOptions,
                run,
                out bool appended,
                out bool canContinue,
                ref runCaseInsensitive,
                ref runUnicodeClasses))
            {
                if (appended &&
                    IsBetterRequiredLiteralSet(run.ToArray(), hasCandidate ? candidate.Literals : []))
                {
                    candidate = new RegexRequiredLiteralSetCandidate(
                        CopyLiteralSet(run),
                        RequiredLiteralLookBehind,
                        runCaseInsensitive,
                        runUnicodeClasses);
                    hasCandidate = true;
                }

                if (!canContinue)
                {
                    FlushRequiredLiteralRun(run, runCaseInsensitive, runUnicodeClasses, ref candidate, ref hasCandidate);
                    run.Clear();
                    runCaseInsensitive = false;
                    runUnicodeClasses = false;
                }

                continue;
            }

            FlushRequiredLiteralRun(run, runCaseInsensitive, runUnicodeClasses, ref candidate, ref hasCandidate);
            run.Clear();
            runCaseInsensitive = false;
            runUnicodeClasses = false;
            if (TryCollectRequiredLiteralSet(child, currentOptions, out RegexRequiredLiteralSetCandidate childCandidate) &&
                IsBetterRequiredLiteralSet(
                    childCandidate.Literals,
                    hasCandidate ? candidate.Literals : [],
                    preferEqual: true))
            {
                candidate = childCandidate;
                hasCandidate = true;
            }
        }

        FlushRequiredLiteralRun(run, runCaseInsensitive, runUnicodeClasses, ref candidate, ref hasCandidate);
        return hasCandidate && candidate.Literals.Length > 0;
    }

    private static bool TryAppendRequiredLiteralRun(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte[]> run,
        out bool appended,
        out bool canContinue,
        ref bool runCaseInsensitive,
        ref bool runUnicodeClasses)
    {
        appended = false;
        canContinue = false;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.StartAnchor:
            case RegexSyntaxKind.EndAnchor:
            case RegexSyntaxKind.AbsoluteStartAnchor:
            case RegexSyntaxKind.AbsoluteEndAnchor:
            case RegexSyntaxKind.WordBoundary:
            case RegexSyntaxKind.NotWordBoundary:
            case RegexSyntaxKind.WordStartBoundary:
            case RegexSyntaxKind.WordEndBoundary:
            case RegexSyntaxKind.WordStartHalfBoundary:
            case RegexSyntaxKind.WordEndHalfBoundary:
                canContinue = true;
                return true;
            case RegexSyntaxKind.Literal:
                appended = AppendLiteralVariants(run, [((RegexAtomNode)node).Value.ToArray()]);
                canContinue = appended;
                if (appended)
                {
                    runCaseInsensitive |= options.CaseInsensitive;
                    runUnicodeClasses |= options.UnicodeClasses;
                }

                return appended;
            case RegexSyntaxKind.CharacterClass:
                if (!TryGetSimpleClassLiteralVariants((RegexAtomNode)node, out byte[][] variants))
                {
                    return false;
                }

                appended = AppendLiteralVariants(run, variants);
                canContinue = appended;
                if (appended)
                {
                    runCaseInsensitive |= options.CaseInsensitive;
                    runUnicodeClasses |= options.UnicodeClasses;
                }

                return appended;
            case RegexSyntaxKind.Sequence:
                return TryAppendRequiredLiteralSequence(
                    (RegexSequenceNode)node,
                    options,
                    run,
                    out appended,
                    out canContinue,
                    ref runCaseInsensitive,
                    ref runUnicodeClasses);
            case RegexSyntaxKind.Alternation:
                return TryAppendRequiredLiteralAlternation(
                    (RegexAlternationNode)node,
                    options,
                    run,
                    out appended,
                    out canContinue,
                    ref runCaseInsensitive,
                    ref runUnicodeClasses);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAppendRequiredLiteralRun(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    run,
                    out appended,
                    out canContinue,
                    ref runCaseInsensitive,
                    ref runUnicodeClasses);
            case RegexSyntaxKind.Repetition:
                return TryAppendRequiredLiteralRepetition(
                    (RegexRepetitionNode)node,
                    options,
                    run,
                    out appended,
                    out canContinue,
                    ref runCaseInsensitive,
                    ref runUnicodeClasses);
            default:
                return false;
        }
    }

    private static bool TryAppendRequiredLiteralSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<byte[]> run,
        out bool appended,
        out bool canContinue,
        ref bool runCaseInsensitive,
        ref bool runUnicodeClasses)
    {
        appended = false;
        canContinue = true;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryAppendRequiredLiteralRun(
                child,
                currentOptions,
                run,
                out bool childAppended,
                out bool childCanContinue,
                ref runCaseInsensitive,
                ref runUnicodeClasses))
            {
                canContinue = false;
                return appended;
            }

            appended |= childAppended;
            if (!childCanContinue)
            {
                canContinue = false;
                return true;
            }
        }

        return true;
    }

    private static bool TryAppendRequiredLiteralAlternation(
        RegexAlternationNode node,
        RegexCompileOptions options,
        List<byte[]> run,
        out bool appended,
        out bool canContinue,
        ref bool runCaseInsensitive,
        ref bool runUnicodeClasses)
    {
        appended = false;
        canContinue = true;
        byte[][] baseRun = run.Count == 0 ? [Array.Empty<byte>()] : run.ToArray();
        var collected = new List<byte[]>();
        bool collectedCaseInsensitive = false;
        bool collectedUnicodeClasses = false;
        for (int index = 0; index < node.Alternatives.Count; index++)
        {
            List<byte[]> alternativeRun = CloneLiteralRun(baseRun);
            bool alternativeCaseInsensitive = runCaseInsensitive;
            bool alternativeUnicodeClasses = runUnicodeClasses;
            if (!TryAppendRequiredLiteralRun(
                node.Alternatives[index],
                options,
                alternativeRun,
                out bool childAppended,
                out bool childCanContinue,
                ref alternativeCaseInsensitive,
                ref alternativeUnicodeClasses))
            {
                return false;
            }

            appended |= childAppended;
            canContinue &= childCanContinue;
            collectedCaseInsensitive |= alternativeCaseInsensitive;
            collectedUnicodeClasses |= alternativeUnicodeClasses;
            if (!TryAddLiteralVariants(collected, alternativeRun))
            {
                return false;
            }
        }

        run.Clear();
        run.AddRange(collected);
        runCaseInsensitive = collectedCaseInsensitive;
        runUnicodeClasses = collectedUnicodeClasses;
        return true;
    }

    private static List<byte[]> CloneLiteralRun(byte[][] literals)
    {
        var clone = new List<byte[]>(literals.Length);
        for (int index = 0; index < literals.Length; index++)
        {
            clone.Add(literals[index].ToArray());
        }

        return clone;
    }

    private static bool TryAppendRequiredLiteralRepetition(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<byte[]> run,
        out bool appended,
        out bool canContinue,
        ref bool runCaseInsensitive,
        ref bool runUnicodeClasses)
    {
        appended = false;
        canContinue = false;
        if (node.Minimum == 0)
        {
            return false;
        }

        var childRun = new List<byte[]> { Array.Empty<byte>() };
        bool childCaseInsensitive = false;
        bool childUnicodeClasses = false;
        if (!TryAppendRequiredLiteralRun(
                node.Child,
                options,
                childRun,
                out bool childAppended,
                out bool childCanContinue,
                ref childCaseInsensitive,
                ref childUnicodeClasses) ||
            !childAppended)
        {
            return false;
        }

        int contiguousCopies = childCanContinue ? node.Minimum : 1;
        for (int count = 0; count < contiguousCopies; count++)
        {
            if (!AppendLiteralVariants(run, childRun.ToArray()))
            {
                return false;
            }
        }

        appended = true;
        canContinue = childCanContinue && node.Maximum == node.Minimum;
        runCaseInsensitive |= childCaseInsensitive;
        runUnicodeClasses |= childUnicodeClasses;
        return true;
    }

    private static void FlushRequiredLiteralRun(
        List<byte[]> run,
        bool runCaseInsensitive,
        bool runUnicodeClasses,
        ref RegexRequiredLiteralSetCandidate candidate,
        ref bool hasCandidate)
    {
        if (run.Count == 0)
        {
            return;
        }

        byte[][] literals = CopyLiteralSet(run);
        if (IsBetterRequiredLiteralSet(literals, hasCandidate ? candidate.Literals : []))
        {
            candidate = new RegexRequiredLiteralSetCandidate(
                literals,
                RequiredLiteralLookBehind,
                runCaseInsensitive,
                runUnicodeClasses);
            hasCandidate = true;
        }
    }

    private static bool AppendLiteralVariants(List<byte[]> run, byte[][] variants)
    {
        if (variants.Length == 0)
        {
            return false;
        }

        if (run.Count == 0)
        {
            run.Add(Array.Empty<byte>());
        }

        var next = new List<byte[]>();
        for (int runIndex = 0; runIndex < run.Count; runIndex++)
        {
            byte[] prefix = run[runIndex];
            for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                byte[] variant = variants[variantIndex];
                byte[] literal = new byte[prefix.Length + variant.Length];
                prefix.CopyTo(literal, 0);
                variant.CopyTo(literal, prefix.Length);
                AddDistinctLiteral(next, literal);
                if (next.Count > MaxRequiredLiteralVariants)
                {
                    return false;
                }
            }
        }

        run.Clear();
        run.AddRange(next);
        return run.Count > 0;
    }

    private static bool TryAddLiteralVariants(List<byte[]> target, List<byte[]> variants)
    {
        for (int index = 0; index < variants.Count; index++)
        {
            AddDistinctLiteral(target, variants[index]);
            if (target.Count > MaxRequiredLiteralVariants)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[][] CopyLiteralSet(List<byte[]> literals)
    {
        byte[][] copy = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            copy[index] = literals[index].ToArray();
        }

        return copy;
    }

    private static byte[][] CopyLiteralSet(byte[][] literals)
    {
        byte[][] copy = new byte[literals.Length][];
        for (int index = 0; index < literals.Length; index++)
        {
            copy[index] = literals[index].ToArray();
        }

        return copy;
    }

    private static void AddDistinctLiteral(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return;
            }
        }

        literals.Add(literal);
    }

    private static bool TryAddPreparedRequiredLiteral(
        List<byte[]> literals,
        byte[] literal,
        RegexCompileOptions options)
    {
        if (!options.CaseInsensitive)
        {
            AddDistinctLiteral(literals, literal.ToArray());
            return true;
        }

        if (options.UnicodeClasses &&
            TryAddUnicodeCaseFoldLiteralVariants(literals, literal))
        {
            return true;
        }

        AddDistinctLiteral(literals, NormalizeAsciiCase(literal));
        return true;
    }

    private static bool TryAddUnicodeCaseFoldLiteralVariants(List<byte[]> literals, byte[] literal)
    {
        if (!TryDecodeRunes(literal, out Rune[] runes) ||
            runes.Length == 0)
        {
            AddDistinctLiteral(literals, NormalizeAsciiCase(literal));
            return true;
        }

        byte[][][] equivalents = new byte[runes.Length][][];
        for (int index = 0; index < runes.Length; index++)
        {
            equivalents[index] = GetCaseFoldEquivalentBytes(runes[index]);
        }

        if (!TryChooseUnicodeLiteralWindow(equivalents, out int start, out int length))
        {
            return false;
        }

        List<byte[]> variants = [Array.Empty<byte>()];
        for (int index = start; index < start + length; index++)
        {
            List<byte[]> next = [];
            byte[][] runeEquivalents = equivalents[index];
            for (int prefixIndex = 0; prefixIndex < variants.Count; prefixIndex++)
            {
                byte[] prefix = variants[prefixIndex];
                for (int runeIndex = 0; runeIndex < runeEquivalents.Length; runeIndex++)
                {
                    byte[] encoded = runeEquivalents[runeIndex];
                    byte[] variant = new byte[prefix.Length + encoded.Length];
                    prefix.CopyTo(variant, 0);
                    encoded.CopyTo(variant, prefix.Length);
                    AddDistinctLiteral(next, NormalizeAsciiCase(variant));
                    if (next.Count > MaxRequiredLiteralVariants)
                    {
                        return false;
                    }
                }
            }

            variants = next;
        }

        for (int index = 0; index < variants.Count; index++)
        {
            AddDistinctLiteral(literals, variants[index]);
        }

        return true;
    }

    private static bool TryAddFullUnicodeCaseFoldLiteralVariants(List<byte[]> literals, byte[] literal)
    {
        if (!TryDecodeRunes(literal, out Rune[] runes) ||
            runes.Length == 0)
        {
            AddDistinctLiteral(literals, NormalizeAsciiCase(literal));
            return true;
        }

        List<byte[]> variants = [Array.Empty<byte>()];
        for (int index = 0; index < runes.Length; index++)
        {
            List<byte[]> next = [];
            byte[][] runeEquivalents = GetCaseFoldEquivalentBytes(runes[index]);
            for (int prefixIndex = 0; prefixIndex < variants.Count; prefixIndex++)
            {
                byte[] prefix = variants[prefixIndex];
                for (int runeIndex = 0; runeIndex < runeEquivalents.Length; runeIndex++)
                {
                    byte[] encoded = runeEquivalents[runeIndex];
                    byte[] variant = new byte[prefix.Length + encoded.Length];
                    prefix.CopyTo(variant, 0);
                    encoded.CopyTo(variant, prefix.Length);
                    AddDistinctLiteral(next, NormalizeAsciiCase(variant));
                    if (next.Count > MaxRequiredLiteralVariants)
                    {
                        return false;
                    }
                }
            }

            variants = next;
        }

        for (int index = 0; index < variants.Count; index++)
        {
            AddDistinctLiteral(literals, variants[index]);
        }

        return true;
    }

    private static byte[][] GetCaseFoldEquivalentBytes(Rune value)
    {
        List<Rune> runeEquivalents = [];
        RegexUnicodeTables.AddSimpleCaseFoldEquivalents(value, runeEquivalents);
        List<byte[]> byteEquivalents = [];
        for (int index = 0; index < runeEquivalents.Count; index++)
        {
            Rune equivalent = runeEquivalents[index];
            byte[] encoded = equivalent.IsAscii
                ? [FoldAsciiByte((byte)equivalent.Value)]
                : Encoding.UTF8.GetBytes(equivalent.ToString());
            AddDistinctLiteral(byteEquivalents, encoded);
        }

        return byteEquivalents.ToArray();
    }

    private static byte FoldAsciiByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }

    private static bool TryChooseUnicodeLiteralWindow(byte[][][] equivalents, out int start, out int length)
    {
        start = 0;
        length = 0;
        int bestScore = int.MinValue;
        for (int candidateStart = 0; candidateStart < equivalents.Length; candidateStart++)
        {
            int variantCount = 1;
            int byteLength = 0;
            for (int candidateEnd = candidateStart; candidateEnd < equivalents.Length; candidateEnd++)
            {
                byte[][] runeEquivalents = equivalents[candidateEnd];
                if (runeEquivalents.Length == 0 ||
                    variantCount > MaxRequiredLiteralVariants / runeEquivalents.Length)
                {
                    break;
                }

                variantCount *= runeEquivalents.Length;
                byteLength += ShortestLiteralLength(runeEquivalents);
                if (byteLength < 3)
                {
                    continue;
                }

                int score = (byteLength * 32) - variantCount;
                if (score > bestScore)
                {
                    bestScore = score;
                    start = candidateStart;
                    length = candidateEnd - candidateStart + 1;
                }
            }
        }

        return length > 0;
    }

    private static int ShortestLiteralLength(byte[][] literals)
    {
        int shortest = int.MaxValue;
        for (int index = 0; index < literals.Length; index++)
        {
            shortest = Math.Min(shortest, literals[index].Length);
        }

        return shortest;
    }

    private static bool HasShortLiteral(List<byte[]> literals)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].Length < 2)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeRunes(byte[] bytes, out Rune[] runes)
    {
        List<Rune> decoded = [];
        ReadOnlySpan<byte> remaining = bytes;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed);
            if (status != OperationStatus.Done || consumed <= 0)
            {
                runes = [];
                return false;
            }

            decoded.Add(rune);
            remaining = remaining[consumed..];
        }

        runes = decoded.ToArray();
        return true;
    }

    private static byte[] NormalizeAsciiCase(byte[] literal)
    {
        byte[] normalized = literal.ToArray();
        for (int index = 0; index < normalized.Length; index++)
        {
            byte value = normalized[index];
            if (value is >= (byte)'A' and <= (byte)'Z')
            {
                normalized[index] = (byte)(value + 32);
            }
        }

        return normalized;
    }

    private static bool TryGetPrefixAtomVariants(
        RegexAtomNode node,
        RegexCompileOptions options,
        out byte[][] variants,
        out bool sealVariants)
    {
        variants = [];
        sealVariants = false;
        switch (node.Kind)
        {
            case RegexSyntaxKind.CharacterClass:
                return TryGetSimpleClassLiteralVariants(
                    node,
                    options.UnicodeClasses,
                    MaxPrefixAtomVariants,
                    out variants,
                    out sealVariants);
            case RegexSyntaxKind.DigitClass:
                variants = options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddDecimalNumberPrefixBytes)
                    : RangeVariants((byte)'0', (byte)'9');
                sealVariants = options.UnicodeClasses;
                return true;
            case RegexSyntaxKind.WordClass:
                variants = options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlWordPrefixBytes)
                    : WordVariants();
                sealVariants = options.UnicodeClasses;
                return true;
            case RegexSyntaxKind.WhitespaceClass:
                variants = options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlSpacePrefixBytes)
                    : WhitespaceVariants();
                sealVariants = options.UnicodeClasses;
                return true;
            case RegexSyntaxKind.LetterClass:
                variants = options.UnicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddAlphabeticPrefixBytes)
                    : LetterVariants();
                sealVariants = options.UnicodeClasses;
                return true;
            case RegexSyntaxKind.AlphanumericClass:
                if (options.UnicodeClasses)
                {
                    List<byte[]> unicodeAlphanumeric = [];
                    RegexUnicodeTables.AddAlphabeticPrefixBytes(unicodeAlphanumeric);
                    RegexUnicodeTables.AddDecimalNumberPrefixBytes(unicodeAlphanumeric);
                    variants = unicodeAlphanumeric.ToArray();
                    sealVariants = true;
                }
                else
                {
                    variants = AlphanumericVariants();
                }

                return true;
            default:
                return false;
        }
    }

    private static bool TryGetSimpleClassLiteralVariants(RegexAtomNode node, out byte[][] variants)
    {
        return TryGetSimpleClassLiteralVariants(
            node,
            unicodeClasses: false,
            MaxClassLiteralVariants,
            out variants,
            out _);
    }

    private static bool TryGetSimpleClassLiteralVariants(
        RegexAtomNode node,
        bool unicodeClasses,
        int maxVariants,
        out byte[][] variants,
        out bool sealVariants)
    {
        variants = [];
        sealVariants = false;
        ReadOnlySpan<byte> expression = node.Value.Span;
        if (expression.Length == 0 || expression[0] == (byte)'^')
        {
            return false;
        }

        var bytes = new List<byte>();
        int index = 0;
        while (index < expression.Length)
        {
            if (expression[index] == (byte)'[' &&
                index + 1 < expression.Length &&
                expression[index + 1] == (byte)':')
            {
                return false;
            }

            if (!TryReadFiniteClassToken(
                    expression,
                    unicodeClasses,
                    ref index,
                    out byte[][] tokenVariants,
                    out byte? rangeLiteral,
                    out bool sealToken))
            {
                return false;
            }

            if (index < expression.Length - 1 && expression[index] == (byte)'-')
            {
                if (!rangeLiteral.HasValue)
                {
                    return false;
                }

                index++;
                if (!TryReadFiniteClassToken(
                        expression,
                        unicodeClasses,
                        ref index,
                        out _,
                        out byte? rangeEndLiteral,
                        out bool sealRangeEnd) ||
                    !rangeEndLiteral.HasValue ||
                    sealToken ||
                    sealRangeEnd ||
                    rangeEndLiteral.Value < rangeLiteral.Value)
                {
                    return false;
                }

                AddClassRange(bytes, rangeLiteral.Value, rangeEndLiteral.Value, maxVariants);
            }
            else
            {
                sealVariants |= sealToken;
                AddClassVariants(bytes, tokenVariants, maxVariants);
            }

            if (bytes.Count > maxVariants)
            {
                return false;
            }
        }

        variants = new byte[bytes.Count][];
        for (int variantIndex = 0; variantIndex < bytes.Count; variantIndex++)
        {
            variants[variantIndex] = [bytes[variantIndex]];
        }

        return variants.Length > 0;
    }

    private static bool TryReadFiniteClassToken(
        ReadOnlySpan<byte> expression,
        bool unicodeClasses,
        ref int index,
        out byte[][] variants,
        out byte? rangeLiteral,
        out bool sealVariants)
    {
        variants = [];
        rangeLiteral = null;
        sealVariants = false;
        if (index >= expression.Length)
        {
            return false;
        }

        byte value = expression[index++];
        if (value != (byte)'\\')
        {
            variants = [[value]];
            rangeLiteral = value;
            return true;
        }

        if (index >= expression.Length)
        {
            return false;
        }

        byte escaped = expression[index++];
        switch (escaped)
        {
            case (byte)'d':
                variants = unicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddDecimalNumberPrefixBytes)
                    : RangeVariants((byte)'0', (byte)'9');
                sealVariants = unicodeClasses;
                return true;
            case (byte)'w':
                variants = unicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlWordPrefixBytes)
                    : WordVariants();
                sealVariants = unicodeClasses;
                return true;
            case (byte)'s':
                variants = unicodeClasses
                    ? UnicodePrefixVariants(RegexUnicodeTables.AddPerlSpacePrefixBytes)
                    : WhitespaceVariants();
                sealVariants = unicodeClasses;
                return true;
            case (byte)'D':
            case (byte)'W':
            case (byte)'S':
            case (byte)'p':
            case (byte)'P':
                return false;
            default:
                if (RegexByteClass.TryReadEscapedHexByte(expression, ref index, escaped, out value))
                {
                    variants = [[value]];
                    rangeLiteral = value;
                    return true;
                }

                if (!TryGetEscapedClassLiteralByte(escaped, out value))
                {
                    return false;
                }

                variants = [[value]];
                rangeLiteral = value;
                return true;
        }
    }

    private static void AddClassRange(List<byte> bytes, byte start, byte end, int maxVariants)
    {
        for (int value = start; value <= end && bytes.Count <= maxVariants; value++)
        {
            byte literal = (byte)value;
            if (!bytes.Contains(literal))
            {
                bytes.Add(literal);
            }
        }
    }

    private static void AddClassVariants(List<byte> bytes, byte[][] variants, int maxVariants)
    {
        for (int index = 0; index < variants.Length && bytes.Count <= maxVariants; index++)
        {
            byte[] variant = variants[index];
            if (variant.Length == 1 && !bytes.Contains(variant[0]))
            {
                bytes.Add(variant[0]);
            }
        }
    }

    private static byte[][] RangeVariants(byte start, byte end)
    {
        byte[][] variants = new byte[end - start + 1][];
        for (int index = 0; index < variants.Length; index++)
        {
            variants[index] = [(byte)(start + index)];
        }

        return variants;
    }

    private static byte[][] UnicodePrefixVariants(Action<List<byte[]>> addPrefixes)
    {
        List<byte[]> prefixes = [];
        addPrefixes(prefixes);
        return prefixes.ToArray();
    }

    private static byte[][] WordVariants()
    {
        byte[][] variants = new byte[63][];
        int index = 0;
        FillRangeVariants(variants, ref index, (byte)'0', (byte)'9');
        FillRangeVariants(variants, ref index, (byte)'A', (byte)'Z');
        FillRangeVariants(variants, ref index, (byte)'a', (byte)'z');
        variants[index] = [(byte)'_'];
        return variants;
    }

    private static byte[][] LetterVariants()
    {
        byte[][] variants = new byte[52][];
        int index = 0;
        FillRangeVariants(variants, ref index, (byte)'A', (byte)'Z');
        FillRangeVariants(variants, ref index, (byte)'a', (byte)'z');
        return variants;
    }

    private static byte[][] AlphanumericVariants()
    {
        byte[][] variants = new byte[62][];
        int index = 0;
        FillRangeVariants(variants, ref index, (byte)'0', (byte)'9');
        FillRangeVariants(variants, ref index, (byte)'A', (byte)'Z');
        FillRangeVariants(variants, ref index, (byte)'a', (byte)'z');
        return variants;
    }

    private static byte[][] WhitespaceVariants()
    {
        return [[(byte)' '], [(byte)'\t'], [(byte)'\n'], [(byte)'\r'], [(byte)'\f'], [0x0b]];
    }

    private static void FillRangeVariants(byte[][] variants, ref int index, byte start, byte end)
    {
        for (int value = start; value <= end; value++)
        {
            variants[index++] = [(byte)value];
        }
    }

    private static bool TryGetEscapedClassLiteralByte(byte escaped, out byte value)
    {
        value = escaped switch
        {
            (byte)'n' => (byte)'\n',
            (byte)'t' => (byte)'\t',
            (byte)'r' => (byte)'\r',
            (byte)'f' => (byte)'\f',
            (byte)'\\' => (byte)'\\',
            (byte)']' => (byte)']',
            (byte)'[' => (byte)'[',
            (byte)'-' => (byte)'-',
            (byte)'^' => (byte)'^',
            _ => escaped,
        };

        return escaped is not ((byte)'d' or (byte)'D' or (byte)'w' or (byte)'W' or (byte)'s' or (byte)'S' or (byte)'p' or (byte)'P');
    }

    private static bool IsBetterRequiredLiteralSet(byte[][] candidate, byte[][] current)
    {
        return IsBetterRequiredLiteralSet(candidate, current, preferEqual: false);
    }

    private static bool IsBetterRequiredLiteralSet(byte[][] candidate, byte[][] current, bool preferEqual)
    {
        if (candidate.Length == 0)
        {
            return false;
        }

        if (current.Length == 0)
        {
            return true;
        }

        int candidateScore = RequiredLiteralSetScore(candidate);
        int currentScore = RequiredLiteralSetScore(current);
        return preferEqual
            ? candidateScore >= currentScore
            : candidateScore > currentScore;
    }

    private static int RequiredLiteralSetScore(byte[][] literals)
    {
        int shortest = int.MaxValue;
        int longest = 0;
        int totalLength = 0;
        for (int index = 0; index < literals.Length; index++)
        {
            int length = literals[index].Length;
            shortest = Math.Min(shortest, length);
            longest = Math.Max(longest, length);
            totalLength += length;
        }

        int averageLength = totalLength / literals.Length;
        return (averageLength * 8) +
            (longest * 4) +
            (shortest * 2) -
            (literals.Length * 6);
    }

    /// <summary>
    /// Scores one prepared prefilter candidate while accounting for multi-pattern scanner cost.
    /// </summary>
    /// <param name="literals">The prepared literal set.</param>
    /// <returns>The relative prefilter score.</returns>
    private static int RequiredLiteralPrefilterScore(byte[][] literals)
    {
        int shortest = int.MaxValue;
        int longest = 0;
        int totalLength = 0;
        for (int index = 0; index < literals.Length; index++)
        {
            int length = literals[index].Length;
            shortest = Math.Min(shortest, length);
            longest = Math.Max(longest, length);
            totalLength += length;
        }

        return RequiredLiteralPrefilterScore(
            literals.Length,
            shortest,
            longest,
            totalLength);
    }

    /// <summary>
    /// Scores one prepared prefilter candidate from its cardinality and length summary.
    /// </summary>
    /// <param name="literalCount">The number of literals.</param>
    /// <param name="shortest">The shortest literal length.</param>
    /// <param name="longest">The longest literal length.</param>
    /// <param name="totalLength">The sum of all literal lengths.</param>
    /// <returns>The relative selectivity score.</returns>
    private static int RequiredLiteralPrefilterScore(
        int literalCount,
        int shortest,
        int longest,
        int totalLength)
    {
        int averageLength = totalLength / literalCount;
        return (averageLength * 8) +
            (longest * 4) +
            (shortest * 2) -
            (literalCount * RequiredLiteralPrefilterAlternativePenalty);
    }

    internal static bool TryFindRequiredLiteral(RegexSyntaxNode node, RegexCompileOptions options, out byte[] literal)
    {
        if (TryFindRequiredLiteralCandidate(node, options, out RegexRequiredLiteralSetCandidate candidate) &&
            candidate.Literals.Length == 1)
        {
            literal = candidate.Literals[0];
            return true;
        }

        literal = [];
        return false;
    }

    internal static bool TryFindRequiredLiteralCandidate(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Literal:
                byte[] literal = ((RegexAtomNode)node).Value.ToArray();
                candidate = new RegexRequiredLiteralSetCandidate(
                    [literal],
                    RequiredLiteralLookBehind,
                    options.CaseInsensitive,
                    options.UnicodeClasses);
                return literal.Length > 0;
            case RegexSyntaxKind.Sequence:
                return TryFindRequiredLiteralInSequence((RegexSequenceNode)node, options, out candidate);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryFindRequiredLiteralCandidate(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), out candidate);
            case RegexSyntaxKind.Repetition:
                var repetition = (RegexRepetitionNode)node;
                return repetition.Minimum > 0 &&
                    TryFindRequiredLiteralCandidate(repetition.Child, options, out candidate);
            default:
                return false;
        }
    }

    private static bool TryCollectAlternationRequiredLiterals(RegexSyntaxNode node, RegexCompileOptions options, out byte[][] literals)
    {
        literals = [];
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAlternationNode alternation || alternation.Alternatives.Count < 2)
        {
            return false;
        }

        literals = new byte[alternation.Alternatives.Count][];
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryFindRequiredLiteral(alternation.Alternatives[index], options, out byte[] literal) ||
                literal.Length < 3)
            {
                literals = [];
                return false;
            }

            literals[index] = literal;
        }

        return true;
    }

    private static bool TryFindRequiredLiteralInSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        out RegexRequiredLiteralSetCandidate candidate)
    {
        candidate = default;
        bool hasCandidate = false;
        var literalRun = new List<byte>();
        bool runCaseInsensitive = false;
        bool runUnicodeClasses = false;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                FlushLiteralRun(literalRun, runCaseInsensitive, runUnicodeClasses, ref candidate, ref hasCandidate);
                runCaseInsensitive = false;
                runUnicodeClasses = false;
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (child is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
            {
                literalRun.AddRange(atom.Value.ToArray());
                runCaseInsensitive |= currentOptions.CaseInsensitive;
                runUnicodeClasses |= currentOptions.UnicodeClasses;
                continue;
            }

            FlushLiteralRun(literalRun, runCaseInsensitive, runUnicodeClasses, ref candidate, ref hasCandidate);
            runCaseInsensitive = false;
            runUnicodeClasses = false;
            if (TryFindRequiredLiteralCandidate(child, currentOptions, out RegexRequiredLiteralSetCandidate childCandidate) &&
                IsBetterRequiredLiteralSet(childCandidate.Literals, hasCandidate ? candidate.Literals : []))
            {
                candidate = childCandidate;
                hasCandidate = true;
            }
        }

        FlushLiteralRun(literalRun, runCaseInsensitive, runUnicodeClasses, ref candidate, ref hasCandidate);
        return hasCandidate && candidate.Literals.Length > 0;
    }

    private static void FlushLiteralRun(
        List<byte> literalRun,
        bool runCaseInsensitive,
        bool runUnicodeClasses,
        ref RegexRequiredLiteralSetCandidate candidate,
        ref bool hasCandidate)
    {
        if (literalRun.Count == 0)
        {
            return;
        }

        byte[][] literals = [literalRun.ToArray()];
        if (IsBetterRequiredLiteralSet(literals, hasCandidate ? candidate.Literals : []))
        {
            candidate = new RegexRequiredLiteralSetCandidate(
                literals,
                RequiredLiteralLookBehind,
                runCaseInsensitive,
                runUnicodeClasses);
            hasCandidate = true;
        }

        literalRun.Clear();
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
