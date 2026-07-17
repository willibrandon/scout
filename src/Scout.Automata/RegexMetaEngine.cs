namespace Scout;

/// <summary>
/// Selects and coordinates the compiled regex engines, prefilters, and pooled runner state.
/// </summary>
internal sealed class RegexMetaEngine
{
    private const int DenseDfaNfaStateLimit = 8;
    private const int SparseDfaNfaStateLimit = 32;
    private const int DenseDfaStateLimit = 16;
    private const int SparseDfaStateLimit = 64;
    private const int OnePassDfaNfaStateLimit = 48;
    private const int BoundedBacktrackerNfaStateLimit = 256;
    private const int CompactScalarFallbackNfaStateThreshold = 4096;
    private const int UnanchoredLazyDfaNfaStateLimit = 4096;
    private const int UnanchoredDenseDfaNfaStateLimit = 64;
    private const int UnanchoredDenseDfaStateLimit = 64;
    /// <summary>
    /// Gets the minimum search-window length that amortizes an unanchored DFA runner.
    /// </summary>
    internal const int UnanchoredLazyDfaHaystackThreshold = 4096;
    private const int AnchoredLeftmostDfaHaystackThreshold = 4096;
    private const int RunnerAvailabilityUnknown = 0;
    private const int RunnerAvailable = 1;
    private const int RunnerUnavailable = -1;
    private const ulong DefaultDfaSizeLimit = 16UL * 1024UL * 1024UL;

    private readonly RegexRunnerPool<PikeVm>? pikeVmPool;
    private readonly RegexRunnerPool<RegexBoundedBacktracker>? boundedBacktrackerPool;
    private readonly RegexRunnerPool<RegexOnePassDfa>? onePassDfaPool;
    private readonly RegexDenseDfa? denseDfa;
    private readonly RegexSparseDfa? sparseDfa;
    private readonly RegexRunnerPool<RegexLazyDfa>? lazyDfaPool;
    private readonly RegexRunnerPool<RegexLazyDfa>? anchoredLeftmostDfaPool;
    private readonly RegexRunnerPool<RegexLazyDfa>? asciiFastDfaPool;
    private readonly RegexEmptyEngine? empty;
    private readonly RegexLiteralSetEngine? literalSet;
    private readonly RegexAlternationSetEngine? alternationSet;
    private readonly RegexWholeLineEngine? wholeLine;
    private readonly RegexDotStarEngine? dotStar;
    private readonly RegexIpv4AddressEngine? ipv4Address;
    private readonly RegexEmailAddressEngine? emailAddress;
    private readonly RegexLh3EmailEngine? lh3Email;
    private readonly RegexUriEngine? uri;
    private readonly RegexLh3UriEngine? lh3Uri;
    private readonly RegexLh3UriOrEmailEngine? lh3UriOrEmail;
    private readonly RegexBoundedDigitDelimiterEngine? boundedDigitDelimiter;
    private readonly RegexWordWhitespaceLiteralEngine? wordWhitespaceLiteral;
    private readonly RegexUnicodeWordWhitespaceLiteralEngine? unicodeWordWhitespaceLiteral;
    private readonly RegexBoundedLetterSuffixWhitespaceEngine? boundedLetterSuffixWhitespace;
    private readonly RegexRunLiteralDotStarEngine? runLiteralDotStar;
    private readonly RegexLiteralPrefixRunEngine? literalPrefixRun;
    private readonly RegexBoundedLiteralGapEngine? boundedLiteralGap;
    private readonly RegexBoundedLineLiteralGapEngine? boundedLineLiteralGap;
    private readonly RegexAnchoredLineLiteralGapEngine? anchoredLineLiteralGap;
    private readonly RegexBoundedPrefixLiteralSetEngine? boundedPrefixLiteralSet;
    private readonly RegexUnicodeGraphemeClusterEngine? unicodeGraphemeCluster;
    private readonly RegexBoundedScalarClassSequenceEngine? boundedScalarClassSequence;
    private readonly RegexBoundedByteClassSequenceEngine? boundedByteClassSequence;
    private readonly RegexRepeatedLazyDotStarLiteralEngine? repeatedLazyDotStarLiteral;
    private readonly RegexRepeatedLiteralRunOrEmptyEngine? repeatedLiteralRunOrEmpty;
    private readonly RegexDelimitedSpanEngine? delimitedSpan;
    private readonly RegexDelimitedCaptureEngine? delimitedCapture;
    private readonly RegexStructuredLogCaptureEngine? structuredLogCapture;
    private readonly RegexFixedWidthAlternationEngine? fixedWidthAlternation;
    private readonly RegexFixedWordWhitespaceSequenceEngine? fixedWordWhitespaceSequence;
    private readonly RegexLeadingClassLiteralEngine? leadingClassLiteral;
    private readonly RegexLineBoundaryLiteralEngine? lineBoundaryLiteral;
    private readonly RegexUnicodeLetterLiteralRunEngine? unicodeLetterLiteralRun;
    private readonly RegexWordBoundaryLiteralSetEngine? wordBoundaryLiteralSet;
    private readonly RegexWordSuffixLiteralEngine? wordSuffixLiteral;
    private readonly RegexDelimitedRunEngine? delimitedRun;
    private readonly RegexSimpleSequenceEngine? simpleSequence;
    private readonly RegexEndAnchoredSequenceEngine? endAnchoredSequence;
    private readonly RegexEndAnchoredAtomEngine? endAnchoredAtom;
    private readonly RegexLineContainsEngine? lineContains;
    private readonly RegexDotStarClassFallbackEngine? dotStarClassFallback;
    private readonly RegexScalarRunEngine? scalarRun;
    private readonly RegexAsciiWordBoundaryEngine? asciiWordBoundary;
    private readonly RegexPrefilter? prefilter;
    private readonly Func<RegexNfa>? nfaFactory;
    private readonly object nfaInitializationLock = new();
    private RegexRunnerPool<RegexUnanchoredLazyDfa>? _unanchoredLazyDfaPool;
    private Func<RegexUnanchoredLazyDfa?>? _unanchoredLazyDfaFactory;
    private RegexRunnerPool<RegexUnanchoredLazyDfa>? _asciiFastUnanchoredDfaPool;
    private Func<RegexUnanchoredLazyDfa?>? _asciiFastUnanchoredDfaFactory;
    private readonly RegexUnanchoredDenseDfa? _asciiFastUnanchoredDenseDfa;
    private int _unanchoredLazyDfaActivated;
    private int _asciiFastUnanchoredDfaActivated;
    private int _unanchoredLazyDfaAvailability = RunnerAvailabilityUnknown;
    private int _asciiFastUnanchoredDfaAvailability = RunnerAvailabilityUnknown;
    private RegexNfa? nfa;
    private readonly bool utf8;
    private readonly RegexUnguardedFindDelegate? unguardedFind;
    private readonly bool _usesParsedPatternSet;

    private RegexMetaEngine(
        RegexEngineKind kind,
        RegexNfa? nfa,
        PikeVm? pikeVm,
        RegexBoundedBacktracker? boundedBacktracker,
        RegexOnePassDfa? onePassDfa,
        RegexDenseDfa? denseDfa,
        RegexSparseDfa? sparseDfa,
        RegexLazyDfa? lazyDfa,
        RegexLiteralSetEngine? literalSet,
        RegexAlternationSetEngine? alternationSet,
        RegexDelimitedRunEngine? delimitedRun,
        RegexSimpleSequenceEngine? simpleSequence,
        RegexLineContainsEngine? lineContains,
        RegexDotStarClassFallbackEngine? dotStarClassFallback,
        RegexPrefilter? prefilter,
        bool utf8,
        RegexLazyDfa? asciiFastDfa = null,
        Func<RegexUnanchoredLazyDfa?>? asciiFastUnanchoredDfaFactory = null,
        RegexScalarRunEngine? scalarRun = null,
        RegexAsciiWordBoundaryEngine? asciiWordBoundary = null,
        Func<RegexNfa>? nfaFactory = null,
        Func<RegexUnanchoredLazyDfa?>? unanchoredLazyDfaFactory = null,
        Func<RegexLazyDfa?>? anchoredLeftmostDfaFactory = null,
        RegexEndAnchoredAtomEngine? endAnchoredAtom = null,
        RegexDotStarEngine? dotStar = null,
        RegexIpv4AddressEngine? ipv4Address = null,
        RegexEmailAddressEngine? emailAddress = null,
        RegexLh3EmailEngine? lh3Email = null,
        RegexUriEngine? uri = null,
        RegexLh3UriEngine? lh3Uri = null,
        RegexLh3UriOrEmailEngine? lh3UriOrEmail = null,
        RegexBoundedDigitDelimiterEngine? boundedDigitDelimiter = null,
        RegexWordWhitespaceLiteralEngine? wordWhitespaceLiteral = null,
        RegexUnicodeWordWhitespaceLiteralEngine? unicodeWordWhitespaceLiteral = null,
        RegexBoundedLetterSuffixWhitespaceEngine? boundedLetterSuffixWhitespace = null,
        RegexRunLiteralDotStarEngine? runLiteralDotStar = null,
        RegexLiteralPrefixRunEngine? literalPrefixRun = null,
        RegexBoundedLiteralGapEngine? boundedLiteralGap = null,
        RegexBoundedLineLiteralGapEngine? boundedLineLiteralGap = null,
        RegexAnchoredLineLiteralGapEngine? anchoredLineLiteralGap = null,
        RegexBoundedPrefixLiteralSetEngine? boundedPrefixLiteralSet = null,
        RegexUnicodeGraphemeClusterEngine? unicodeGraphemeCluster = null,
        RegexBoundedScalarClassSequenceEngine? boundedScalarClassSequence = null,
        RegexBoundedByteClassSequenceEngine? boundedByteClassSequence = null,
        RegexRepeatedLazyDotStarLiteralEngine? repeatedLazyDotStarLiteral = null,
        RegexRepeatedLiteralRunOrEmptyEngine? repeatedLiteralRunOrEmpty = null,
        RegexDelimitedSpanEngine? delimitedSpan = null,
        RegexFixedWidthAlternationEngine? fixedWidthAlternation = null,
        RegexFixedWordWhitespaceSequenceEngine? fixedWordWhitespaceSequence = null,
        RegexLeadingClassLiteralEngine? leadingClassLiteral = null,
        RegexLineBoundaryLiteralEngine? lineBoundaryLiteral = null,
        RegexUnicodeLetterLiteralRunEngine? unicodeLetterLiteralRun = null,
        RegexWordBoundaryLiteralSetEngine? wordBoundaryLiteralSet = null,
        RegexWordSuffixLiteralEngine? wordSuffixLiteral = null,
        RegexEndAnchoredSequenceEngine? endAnchoredSequence = null,
        RegexWholeLineEngine? wholeLine = null,
        RegexEmptyEngine? empty = null,
        RegexDelimitedCaptureEngine? delimitedCapture = null,
        RegexStructuredLogCaptureEngine? structuredLogCapture = null,
        Func<RegexLazyDfa?>? lazyDfaFactory = null,
        Func<RegexLazyDfa?>? asciiFastDfaFactory = null,
        RegexUnanchoredDenseDfa? asciiFastUnanchoredDenseDfa = null,
        bool usesParsedPatternSet = false)
    {
        Kind = kind;
        this.nfa = nfa;
        this.nfaFactory = nfaFactory;
        pikeVmPool = pikeVm is null || nfa is null
            ? null
            : new RegexRunnerPool<PikeVm>(pikeVm, () => new PikeVm(nfa));
        boundedBacktrackerPool = boundedBacktracker is null || nfa is null
            ? null
            : new RegexRunnerPool<RegexBoundedBacktracker>(boundedBacktracker, () => new RegexBoundedBacktracker(nfa));
        onePassDfaPool = onePassDfa is null || nfa is null
            ? null
            : new RegexRunnerPool<RegexOnePassDfa>(onePassDfa, () => new RegexOnePassDfa(nfa));
        this.denseDfa = denseDfa;
        this.sparseDfa = sparseDfa;
        lazyDfaPool = CreatePool(lazyDfa, lazyDfaFactory);
        anchoredLeftmostDfaPool = CreatePool(initial: null, anchoredLeftmostDfaFactory);
        asciiFastDfaPool = CreatePool(asciiFastDfa, asciiFastDfaFactory);
        this.empty = empty;
        _asciiFastUnanchoredDfaFactory = asciiFastUnanchoredDfaFactory;
        _asciiFastUnanchoredDenseDfa = asciiFastUnanchoredDenseDfa;
        _unanchoredLazyDfaFactory = unanchoredLazyDfaFactory;
        this.literalSet = literalSet;
        this.alternationSet = alternationSet;
        this.wholeLine = wholeLine;
        this.dotStar = dotStar;
        this.ipv4Address = ipv4Address;
        this.emailAddress = emailAddress;
        this.lh3Email = lh3Email;
        this.uri = uri;
        this.lh3Uri = lh3Uri;
        this.lh3UriOrEmail = lh3UriOrEmail;
        this.boundedDigitDelimiter = boundedDigitDelimiter;
        this.wordWhitespaceLiteral = wordWhitespaceLiteral;
        this.unicodeWordWhitespaceLiteral = unicodeWordWhitespaceLiteral;
        this.boundedLetterSuffixWhitespace = boundedLetterSuffixWhitespace;
        this.runLiteralDotStar = runLiteralDotStar;
        this.literalPrefixRun = literalPrefixRun;
        this.boundedLiteralGap = boundedLiteralGap;
        this.boundedLineLiteralGap = boundedLineLiteralGap;
        this.anchoredLineLiteralGap = anchoredLineLiteralGap;
        this.boundedPrefixLiteralSet = boundedPrefixLiteralSet;
        this.unicodeGraphemeCluster = unicodeGraphemeCluster;
        this.boundedScalarClassSequence = boundedScalarClassSequence;
        this.boundedByteClassSequence = boundedByteClassSequence;
        this.repeatedLazyDotStarLiteral = repeatedLazyDotStarLiteral;
        this.repeatedLiteralRunOrEmpty = repeatedLiteralRunOrEmpty;
        this.delimitedSpan = delimitedSpan;
        this.delimitedCapture = delimitedCapture;
        this.structuredLogCapture = structuredLogCapture;
        this.fixedWidthAlternation = fixedWidthAlternation;
        this.fixedWordWhitespaceSequence = fixedWordWhitespaceSequence;
        this.leadingClassLiteral = leadingClassLiteral;
        this.lineBoundaryLiteral = lineBoundaryLiteral;
        this.unicodeLetterLiteralRun = unicodeLetterLiteralRun;
        this.wordBoundaryLiteralSet = wordBoundaryLiteralSet;
        this.wordSuffixLiteral = wordSuffixLiteral;
        this.delimitedRun = delimitedRun;
        this.simpleSequence = simpleSequence;
        this.endAnchoredSequence = endAnchoredSequence;
        this.endAnchoredAtom = endAnchoredAtom;
        this.lineContains = lineContains;
        this.dotStarClassFallback = dotStarClassFallback;
        this.scalarRun = scalarRun;
        this.asciiWordBoundary = asciiWordBoundary;
        this.prefilter = prefilter;
        this.utf8 = utf8;
        _usesParsedPatternSet = usesParsedPatternSet;
        unguardedFind = prefilter is null ? CreateUnguardedFind() : null;
    }

    private static RegexRunnerPool<T>? CreatePool<T>(T? initial, Func<T?>? factory)
        where T : class
    {
        if (initial is null)
        {
            return factory is null ? null : new RegexRunnerPool<T>(factory);
        }

        return new RegexRunnerPool<T>(initial, factory ?? (() => null));
    }

    /// <summary>
    /// Gets the selected regex engine kind.
    /// </summary>
    public RegexEngineKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether complete match spans can be searched without an
    /// unanchored-DFA runner.
    /// </summary>
    internal bool CanSearchWholeHaystackWithFullMatches =>
        prefilter is not null || unguardedFind is not null;

    /// <summary>
    /// Gets a value indicating whether the ordered pattern-set engine was selected from parsed syntax.
    /// </summary>
    internal bool UsesParsedPatternSet => _usesParsedPatternSet;

    /// <summary>
    /// Gets a value indicating whether the selected AST-proven exact-literal engine uses a common-prefix scan.
    /// </summary>
    internal bool UsesCommonPrefixLiteralScanner =>
        literalSet?.UsesCommonPrefixScanner == true ||
        _usesParsedPatternSet && alternationSet?.UsesCommonPrefixLiteralScanner == true;

    /// <summary>
    /// Gets a value indicating whether a safe ASCII-projected match-end runner can be rented.
    /// </summary>
    internal bool HasAsciiProjectedMatchEndRunner =>
        _asciiFastUnanchoredDenseDfa is not null ||
        (HasAsciiFastUnanchoredDfaRunner &&
            System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaAvailability) != RunnerUnavailable);

    /// <summary>
    /// Gets the minimum independent ASCII record-run length that amortizes the available
    /// projected match-end runner.
    /// </summary>
    internal int AsciiProjectedMatchEndActivationLength =>
        _asciiFastUnanchoredDenseDfa is not null
            ? 1
            : HasAsciiFastUnanchoredDfaRunner &&
                System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaAvailability) != RunnerUnavailable
                ? UnanchoredLazyDfaHaystackThreshold
                : int.MaxValue;

    /// <summary>
    /// Gets a value indicating whether an operation-scoped full-match runner can use an
    /// unanchored lazy DFA.
    /// </summary>
    internal bool CanRentFindUnanchoredDfa =>
        !UsesExactStartRequiredLiteralPrefilter(prefilter) &&
        (HasPrimaryUnanchoredDfaRunner &&
                System.Threading.Volatile.Read(ref _unanchoredLazyDfaAvailability) != RunnerUnavailable ||
            HasAsciiFastUnanchoredDfaRunner &&
                System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaAvailability) != RunnerUnavailable);

    /// <summary>
    /// Gets a value indicating whether exact-start prefilter candidates can share one anchored
    /// lazy DFA for an operation.
    /// </summary>
    internal bool CanRentFindAnchoredDfa =>
        anchoredLeftmostDfaPool is not null && UsesExactStartPrefilter(prefilter);

    /// <summary>
    /// Gets the selected prefilter kind.
    /// </summary>
    public RegexPrefilterKind PrefilterKind => prefilter?.Kind ?? RegexPrefilterKind.None;

    /// <summary>
    /// Gets the maximum lookbehind window required by the selected literal prefilter.
    /// </summary>
    public int RequiredLiteralWindow => prefilter?.RequiredLiteralWindow ?? 0;

    /// <summary>
    /// Gets the sole case-sensitive literal selected by the exact compiled engine.
    /// </summary>
    /// <param name="literal">Receives the immutable literal bytes.</param>
    /// <returns><see langword="true" /> when this meta engine is an exact single-literal engine.</returns>
    internal bool TryGetSingleCaseSensitiveLiteral(out ReadOnlyMemory<byte> literal)
    {
        if (Kind == RegexEngineKind.LiteralSet &&
            literalSet is not null &&
            literalSet.TryGetSingleCaseSensitiveLiteral(out literal))
        {
            return true;
        }

        literal = default;
        return false;
    }

    /// <summary>
    /// Creates a meta engine backed by an empty-match engine.
    /// </summary>
    public static RegexMetaEngine CompileEmpty(RegexEmptyEngine empty)
    {
        return new RegexMetaEngine(
            RegexEngineKind.Empty,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8: true,
            empty: empty);
    }

    /// <summary>
    /// Creates a meta engine backed by a literal-set engine.
    /// </summary>
    public static RegexMetaEngine CompileLiteralSet(RegexLiteralSetEngine literalSet, bool utf8)
    {
        return new RegexMetaEngine(
            RegexEngineKind.LiteralSet,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8);
    }

    /// <summary>
    /// Creates a meta engine backed by an alternation-set engine.
    /// </summary>
    public static RegexMetaEngine CompileAlternationSet(
        RegexAlternationSetEngine alternationSet,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(alternationSet);
        return new RegexMetaEngine(
            RegexEngineKind.AlternationSet,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: alternationSet,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            nfaFactory: fallbackNfaFactory);
    }

    /// <summary>
    /// Creates a meta engine backed by an ordered pattern set selected from parsed syntax.
    /// </summary>
    /// <param name="patternSet">The parsed-syntax pattern-set engine.</param>
    /// <param name="utf8">Whether matches must respect UTF-8 code point boundaries.</param>
    /// <param name="fallbackNfaFactory">Creates the general NFA when an operation requires it.</param>
    /// <returns>The compiled meta engine.</returns>
    public static RegexMetaEngine CompileParsedPatternSet(
        RegexAlternationSetEngine patternSet,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(patternSet);
        return new RegexMetaEngine(
            RegexEngineKind.AlternationSet,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: patternSet,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            nfaFactory: fallbackNfaFactory,
            usesParsedPatternSet: true);
    }

    /// <summary>
    /// Creates a meta engine backed by a word-boundary literal-set engine.
    /// </summary>
    public static RegexMetaEngine CompileWordBoundaryLiteralSet(
        RegexWordBoundaryLiteralSetEngine wordBoundaryLiteralSet,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(wordBoundaryLiteralSet);
        return new RegexMetaEngine(
            RegexEngineKind.WordBoundaryLiteralSet,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            nfaFactory: fallbackNfaFactory,
            wordBoundaryLiteralSet: wordBoundaryLiteralSet);
    }

    /// <summary>
    /// Creates a meta engine backed by a bounded scalar-class sequence engine.
    /// </summary>
    public static RegexMetaEngine CompileBoundedScalarClassSequence(
        RegexBoundedScalarClassSequenceEngine boundedScalarClassSequence,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(boundedScalarClassSequence);
        return new RegexMetaEngine(
            RegexEngineKind.BoundedScalarClassSequence,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            boundedScalarClassSequence: boundedScalarClassSequence,
            nfaFactory: fallbackNfaFactory);
    }

    /// <summary>
    /// Creates a meta engine backed by a bounded byte-class sequence engine.
    /// </summary>
    public static RegexMetaEngine CompileBoundedByteClassSequence(
        RegexBoundedByteClassSequenceEngine boundedByteClassSequence,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(boundedByteClassSequence);
        return new RegexMetaEngine(
            RegexEngineKind.BoundedByteClassSequence,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            boundedByteClassSequence: boundedByteClassSequence,
            nfaFactory: fallbackNfaFactory);
    }

    /// <summary>
    /// Creates a meta engine backed by a Unicode grapheme-cluster engine.
    /// </summary>
    public static RegexMetaEngine CompileUnicodeGraphemeCluster(
        RegexUnicodeGraphemeClusterEngine unicodeGraphemeCluster,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(unicodeGraphemeCluster);
        return new RegexMetaEngine(
            RegexEngineKind.UnicodeGraphemeCluster,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            unicodeGraphemeCluster: unicodeGraphemeCluster,
            nfaFactory: fallbackNfaFactory);
    }

    /// <summary>
    /// Creates a meta engine backed by a simple-sequence engine.
    /// </summary>
    public static RegexMetaEngine CompileSimpleSequence(
        RegexSimpleSequenceEngine simpleSequence,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(simpleSequence);
        return new RegexMetaEngine(
            RegexEngineKind.SimpleSequence,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: simpleSequence,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            nfaFactory: fallbackNfaFactory);
    }

    /// <summary>
    /// Creates a meta engine backed by a delimited-span engine.
    /// </summary>
    public static RegexMetaEngine CompileDelimitedSpan(
        RegexDelimitedSpanEngine delimitedSpan,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(delimitedSpan);
        return new RegexMetaEngine(
            RegexEngineKind.DelimitedSpan,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            nfaFactory: fallbackNfaFactory,
            delimitedSpan: delimitedSpan);
    }

    /// <summary>
    /// Creates a meta engine backed by a delimited-capture engine.
    /// </summary>
    public static RegexMetaEngine CompileDelimitedCapture(
        RegexDelimitedCaptureEngine delimitedCapture,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(delimitedCapture);
        return new RegexMetaEngine(
            RegexEngineKind.DelimitedCapture,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            nfaFactory: fallbackNfaFactory,
            delimitedCapture: delimitedCapture);
    }

    /// <summary>
    /// Creates a meta engine backed by a structured-log capture engine.
    /// </summary>
    public static RegexMetaEngine CompileStructuredLogCapture(
        RegexStructuredLogCaptureEngine structuredLogCapture,
        bool utf8)
    {
        ArgumentNullException.ThrowIfNull(structuredLogCapture);
        return new RegexMetaEngine(
            RegexEngineKind.StructuredLogCapture,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            structuredLogCapture: structuredLogCapture);
    }

    /// <summary>
    /// Creates a meta engine backed by a fixed word-and-whitespace sequence engine.
    /// </summary>
    public static RegexMetaEngine CompileFixedWordWhitespaceSequence(
        RegexFixedWordWhitespaceSequenceEngine fixedWordWhitespaceSequence,
        bool utf8)
    {
        ArgumentNullException.ThrowIfNull(fixedWordWhitespaceSequence);
        return new RegexMetaEngine(
            RegexEngineKind.FixedWordWhitespaceSequence,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            fixedWordWhitespaceSequence: fixedWordWhitespaceSequence);
    }

    /// <summary>
    /// Creates a meta engine backed by a repeated-literal-run engine.
    /// </summary>
    public static RegexMetaEngine CompileRepeatedLiteralRunOrEmpty(
        RegexRepeatedLiteralRunOrEmptyEngine repeatedLiteralRunOrEmpty,
        bool utf8)
    {
        ArgumentNullException.ThrowIfNull(repeatedLiteralRunOrEmpty);
        return new RegexMetaEngine(
            RegexEngineKind.RepeatedLiteralRunOrEmpty,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            repeatedLiteralRunOrEmpty: repeatedLiteralRunOrEmpty);
    }

    /// <summary>
    /// Creates a meta engine backed by a fixed-width alternation engine.
    /// </summary>
    public static RegexMetaEngine CompileFixedWidthAlternation(
        RegexFixedWidthAlternationEngine fixedWidthAlternation,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(fixedWidthAlternation);
        return new RegexMetaEngine(
            RegexEngineKind.FixedWidthAlternation,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            nfaFactory: fallbackNfaFactory,
            fixedWidthAlternation: fixedWidthAlternation);
    }

    /// <summary>
    /// Creates a meta engine backed by a scalar-run engine.
    /// </summary>
    public static RegexMetaEngine CompileScalarRun(
        RegexScalarRunEngine scalarRun,
        bool utf8,
        Func<RegexNfa>? fallbackNfaFactory)
    {
        ArgumentNullException.ThrowIfNull(scalarRun);
        return new RegexMetaEngine(
            RegexEngineKind.SimpleSequence,
            nfa: null,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: null,
            literalSet: null,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
            scalarRun: scalarRun,
            nfaFactory: fallbackNfaFactory);
    }

    /// <summary>
    /// Selects a meta engine for an NFA without a prefilter or explicit DFA size limit.
    /// </summary>
    public static RegexMetaEngine Compile(RegexNfa nfa)
    {
        return Compile(nfa, prefilter: null, dfaSizeLimit: null);
    }

    /// <summary>
    /// Selects a meta engine for an NFA and optional prefilter.
    /// </summary>
    public static RegexMetaEngine Compile(RegexNfa nfa, RegexPrefilter? prefilter)
    {
        return Compile(nfa, prefilter, dfaSizeLimit: null);
    }

    /// <summary>
    /// Selects a meta engine for an NFA, optional prefilter, and optional DFA size limit.
    /// </summary>
    public static RegexMetaEngine Compile(RegexNfa nfa, RegexPrefilter? prefilter, ulong? dfaSizeLimit)
    {
        return Compile(nfa, prefilter, dfaSizeLimit, literalSet: null, alternationSet: null, simpleSequence: null);
    }

    /// <summary>
    /// Selects and configures the most appropriate authoritative engine and conservative accelerators.
    /// </summary>
    public static RegexMetaEngine Compile(
        RegexNfa nfa,
        RegexPrefilter? prefilter,
        ulong? dfaSizeLimit,
        RegexLiteralSetEngine? literalSet,
        RegexAlternationSetEngine? alternationSet,
        RegexWholeLineEngine? wholeLine = null,
        RegexDotStarEngine? dotStar = null,
        RegexIpv4AddressEngine? ipv4Address = null,
        RegexEmailAddressEngine? emailAddress = null,
        RegexLh3EmailEngine? lh3Email = null,
        RegexUriEngine? uri = null,
        RegexLh3UriEngine? lh3Uri = null,
        RegexLh3UriOrEmailEngine? lh3UriOrEmail = null,
        RegexBoundedDigitDelimiterEngine? boundedDigitDelimiter = null,
        RegexWordWhitespaceLiteralEngine? wordWhitespaceLiteral = null,
        RegexUnicodeWordWhitespaceLiteralEngine? unicodeWordWhitespaceLiteral = null,
        RegexBoundedLetterSuffixWhitespaceEngine? boundedLetterSuffixWhitespace = null,
        RegexRunLiteralDotStarEngine? runLiteralDotStar = null,
        RegexLiteralPrefixRunEngine? literalPrefixRun = null,
        RegexBoundedLiteralGapEngine? boundedLiteralGap = null,
        RegexBoundedLineLiteralGapEngine? boundedLineLiteralGap = null,
        RegexAnchoredLineLiteralGapEngine? anchoredLineLiteralGap = null,
        RegexBoundedPrefixLiteralSetEngine? boundedPrefixLiteralSet = null,
        RegexBoundedScalarClassSequenceEngine? boundedScalarClassSequence = null,
        RegexBoundedByteClassSequenceEngine? boundedByteClassSequence = null,
        RegexRepeatedLazyDotStarLiteralEngine? repeatedLazyDotStarLiteral = null,
        RegexDelimitedSpanEngine? delimitedSpan = null,
        RegexFixedWidthAlternationEngine? fixedWidthAlternation = null,
        RegexLeadingClassLiteralEngine? leadingClassLiteral = null,
        RegexLineBoundaryLiteralEngine? lineBoundaryLiteral = null,
        RegexUnicodeLetterLiteralRunEngine? unicodeLetterLiteralRun = null,
        RegexWordBoundaryLiteralSetEngine? wordBoundaryLiteralSet = null,
        RegexWordSuffixLiteralEngine? wordSuffixLiteral = null,
        RegexDelimitedRunEngine? delimitedRun = null,
        RegexSimpleSequenceEngine? simpleSequence = null,
        RegexEndAnchoredSequenceEngine? endAnchoredSequence = null,
        RegexEndAnchoredAtomEngine? endAnchoredAtom = null,
        RegexLineContainsEngine? lineContains = null,
        RegexDotStarClassFallbackEngine? dotStarClassFallback = null,
        ReadOnlyMemory<byte> asciiFastPattern = default,
        RegexScalarRunEngine? scalarRun = null,
        RegexAsciiWordBoundaryEngine? asciiWordBoundary = null,
        RegexSyntaxNode? root = null,
        RegexCompileOptions? options = null,
        RegexNfa? precompiledAsciiFastNfa = null)
    {
        ulong effectiveDfaSizeLimit = dfaSizeLimit ?? DefaultDfaSizeLimit;
        if (literalSet is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.LiteralSet,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
        }

        if (alternationSet is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.AlternationSet,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
        }

        if (wholeLine is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.WholeLine,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                wholeLine: wholeLine);
        }

        if (dotStar is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.DotStar,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                dotStar: dotStar);
        }

        if (ipv4Address is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.Ipv4Address,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                ipv4Address: ipv4Address);
        }

        if (emailAddress is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.EmailAddress,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                emailAddress: emailAddress);
        }

        if (lh3Email is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.EmailAddress,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                lh3Email: lh3Email);
        }

        if (uri is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.Uri,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                uri: uri);
        }

        if (lh3Uri is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.Uri,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                lh3Uri: lh3Uri);
        }

        if (lh3UriOrEmail is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.UriOrEmail,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                lh3UriOrEmail: lh3UriOrEmail);
        }

        if (boundedDigitDelimiter is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.BoundedDigitDelimiter,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                boundedDigitDelimiter: boundedDigitDelimiter);
        }

        if (wordWhitespaceLiteral is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.WordWhitespaceLiteral,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                wordWhitespaceLiteral: wordWhitespaceLiteral);
        }

        if (unicodeWordWhitespaceLiteral is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.WordWhitespaceLiteral,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                unicodeWordWhitespaceLiteral: unicodeWordWhitespaceLiteral);
        }

        if (boundedLetterSuffixWhitespace is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.BoundedLetterSuffixWhitespace,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                boundedLetterSuffixWhitespace: boundedLetterSuffixWhitespace);
        }

        if (runLiteralDotStar is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.RunLiteralDotStar,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                runLiteralDotStar: runLiteralDotStar);
        }

        if (literalPrefixRun is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.LiteralPrefixRun,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                literalPrefixRun: literalPrefixRun);
        }

        if (boundedLiteralGap is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.BoundedLiteralGap,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                boundedLiteralGap: boundedLiteralGap);
        }

        if (boundedLineLiteralGap is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.BoundedLineLiteralGap,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                boundedLineLiteralGap: boundedLineLiteralGap);
        }

        if (anchoredLineLiteralGap is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.AnchoredLineLiteralGap,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                anchoredLineLiteralGap: anchoredLineLiteralGap);
        }

        if (boundedPrefixLiteralSet is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.BoundedPrefixLiteralSet,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                boundedPrefixLiteralSet: boundedPrefixLiteralSet);
        }

        if (boundedScalarClassSequence is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.BoundedScalarClassSequence,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                boundedScalarClassSequence: boundedScalarClassSequence);
        }

        if (boundedByteClassSequence is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.BoundedByteClassSequence,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                boundedByteClassSequence: boundedByteClassSequence);
        }

        if (repeatedLazyDotStarLiteral is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.RepeatedLazyDotStarLiteral,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                repeatedLazyDotStarLiteral: repeatedLazyDotStarLiteral);
        }

        if (delimitedSpan is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.DelimitedSpan,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                delimitedSpan: delimitedSpan);
        }

        if (fixedWidthAlternation is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.FixedWidthAlternation,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                fixedWidthAlternation: fixedWidthAlternation);
        }

        if (leadingClassLiteral is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.LeadingClassLiteral,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                leadingClassLiteral: leadingClassLiteral);
        }

        if (lineBoundaryLiteral is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.LineBoundaryLiteral,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                lineBoundaryLiteral: lineBoundaryLiteral);
        }

        if (unicodeLetterLiteralRun is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.UnicodeLetterLiteralRun,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                unicodeLetterLiteralRun: unicodeLetterLiteralRun);
        }

        if (wordBoundaryLiteralSet is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.WordBoundaryLiteralSet,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                wordBoundaryLiteralSet: wordBoundaryLiteralSet);
        }

        if (wordSuffixLiteral is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.WordSuffixLiteral,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                wordSuffixLiteral: wordSuffixLiteral);
        }

        if (delimitedRun is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.SimpleSequence,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: delimitedRun,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
        }

        if (endAnchoredAtom is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.EndAnchoredAtom,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                endAnchoredAtom: endAnchoredAtom);
        }

        if (simpleSequence is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.SimpleSequence,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
        }

        if (endAnchoredSequence is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.EndAnchoredSequence,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                endAnchoredSequence: endAnchoredSequence);
        }

        if (asciiWordBoundary is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.SimpleSequence,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                asciiFastDfa: null,
                scalarRun: null,
                asciiWordBoundary: asciiWordBoundary);
        }

        if (lineContains is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.LineContains,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
        }

        if (dotStarClassFallback is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.DotStarClassFallback,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback,
                prefilter,
                nfa.Utf8);
        }

        if (scalarRun is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.SimpleSequence,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                asciiFastDfa: null,
                scalarRun: scalarRun);
        }

        if (!RegexDfaOperations.CanCompile(nfa))
        {
            nfa = TryUseCompactScalarFallbackNfa(nfa, root, options);
            RegexNfa? incompatibleAsciiFastNfa = precompiledAsciiFastNfa ??
                TryCompileAsciiFastNfa(asciiFastPattern, root, options);
            Func<RegexUnanchoredLazyDfa?>? incompatibleAsciiFastUnanchoredDfaFactory =
                UsesExactStartRequiredLiteralPrefilter(prefilter)
                    ? null
                    : CreateAsciiFastUnanchoredDfaFactory(
                        incompatibleAsciiFastNfa,
                        root,
                        options,
                        effectiveDfaSizeLimit,
                        useConstructionBudget: prefilter is null);
            RegexUnanchoredDenseDfa? incompatibleAsciiFastUnanchoredDenseDfa =
                UsesExactStartRequiredLiteralPrefilter(prefilter)
                    ? null
                    : CreateAsciiFastUnanchoredDenseDfa(
                        incompatibleAsciiFastNfa,
                        root,
                        options,
                        effectiveDfaSizeLimit);
            Func<RegexUnanchoredLazyDfa?>? incompatibleUnanchoredLazyDfaFactory =
                UsesExactStartRequiredLiteralPrefilter(prefilter)
                    ? null
                    : CreateExpandedUnanchoredDfaFactory(
                        root,
                        options,
                        effectiveDfaSizeLimit);
            if (nfa.States.Count <= OnePassDfaNfaStateLimit && RegexOnePassDfa.CanCompile(nfa))
            {
                return new RegexMetaEngine(
                    RegexEngineKind.OnePassDfa,
                    nfa,
                    pikeVm: null,
                    boundedBacktracker: null,
                    onePassDfa: new RegexOnePassDfa(nfa),
                    denseDfa: null,
                    sparseDfa: null,
                    lazyDfa: null,
                    literalSet: null,
                    alternationSet: null,
                    delimitedRun: null,
                    simpleSequence: null,
                    lineContains: null,
                    dotStarClassFallback: null,
                    prefilter,
                    nfa.Utf8,
                    asciiFastUnanchoredDfaFactory: incompatibleAsciiFastUnanchoredDfaFactory,
                    unanchoredLazyDfaFactory: incompatibleUnanchoredLazyDfaFactory,
                    asciiFastUnanchoredDenseDfa: incompatibleAsciiFastUnanchoredDenseDfa);
            }

            if (nfa.States.Count <= BoundedBacktrackerNfaStateLimit && RegexBoundedBacktracker.CanCompile(nfa))
            {
                return new RegexMetaEngine(
                    RegexEngineKind.BoundedBacktracker,
                    nfa,
                    pikeVm: null,
                    boundedBacktracker: new RegexBoundedBacktracker(nfa),
                    onePassDfa: null,
                    denseDfa: null,
                    sparseDfa: null,
                    lazyDfa: null,
                    literalSet: null,
                    alternationSet: null,
                    delimitedRun: null,
                    simpleSequence: null,
                    lineContains: null,
                    dotStarClassFallback: null,
                    prefilter,
                    nfa.Utf8,
                    asciiFastUnanchoredDfaFactory: incompatibleAsciiFastUnanchoredDfaFactory,
                    unanchoredLazyDfaFactory: incompatibleUnanchoredLazyDfaFactory,
                    asciiFastUnanchoredDenseDfa: incompatibleAsciiFastUnanchoredDenseDfa);
            }

            Func<RegexLazyDfa?>? asciiFastDfaFactory = CreateAsciiFastDfaFactory(
                incompatibleAsciiFastNfa,
                effectiveDfaSizeLimit);
            RegexLazyDfa? asciiFastDfa = incompatibleAsciiFastUnanchoredDenseDfa is null
                ? asciiFastDfaFactory?.Invoke()
                : null;
            if (incompatibleAsciiFastUnanchoredDenseDfa is null && asciiFastDfa is null)
            {
                asciiFastDfaFactory = null;
            }

            return new RegexMetaEngine(
                RegexEngineKind.PikeVm,
                nfa,
                new PikeVm(nfa),
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                asciiFastDfa: asciiFastDfa,
                asciiFastDfaFactory: asciiFastDfaFactory,
                asciiFastUnanchoredDfaFactory: incompatibleAsciiFastUnanchoredDfaFactory,
                unanchoredLazyDfaFactory: incompatibleUnanchoredLazyDfaFactory,
                asciiFastUnanchoredDenseDfa: incompatibleAsciiFastUnanchoredDenseDfa);
        }

        if (nfa.States.Count <= DenseDfaNfaStateLimit &&
            RegexDenseDfa.TryCompile(nfa, DenseDfaStateLimit, effectiveDfaSizeLimit, out RegexDenseDfa? denseDfa))
        {
            Func<RegexUnanchoredLazyDfa?>? denseUnanchoredLazyDfaFactory = prefilter is null
                ? CreateUnanchoredLazyDfaFactory(
                    nfa,
                    root,
                    options,
                    effectiveDfaSizeLimit,
                    useConstructionBudget: true)
                : null;
            return new RegexMetaEngine(
                RegexEngineKind.DenseDfa,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: denseDfa,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                unanchoredLazyDfaFactory: denseUnanchoredLazyDfaFactory);
        }

        if (nfa.States.Count <= SparseDfaNfaStateLimit &&
            RegexSparseDfa.TryCompile(nfa, SparseDfaStateLimit, effectiveDfaSizeLimit, out RegexSparseDfa? sparseDfa))
        {
            Func<RegexUnanchoredLazyDfa?>? sparseUnanchoredLazyDfaFactory = prefilter is null
                ? CreateUnanchoredLazyDfaFactory(
                    nfa,
                    root,
                    options,
                    effectiveDfaSizeLimit,
                    useConstructionBudget: true)
                : null;
            return new RegexMetaEngine(
                RegexEngineKind.SparseDfa,
                nfa,
                pikeVm: null,
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: sparseDfa,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8,
                unanchoredLazyDfaFactory: sparseUnanchoredLazyDfaFactory);
        }

        Func<RegexLazyDfa?> lazyDfaFactory = () =>
            RegexLazyDfa.TryCreate(nfa, effectiveDfaSizeLimit, out RegexLazyDfa? dfa) ? dfa : null;
        RegexLazyDfa? lazyDfa = lazyDfaFactory();
        if (lazyDfa is null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.PikeVm,
                nfa,
                new PikeVm(nfa),
                boundedBacktracker: null,
                onePassDfa: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null,
                literalSet: null,
                alternationSet: null,
                delimitedRun: null,
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
        }

        Func<RegexUnanchoredLazyDfa?>? unanchoredLazyDfaFactory =
            UsesExactStartRequiredLiteralPrefilter(prefilter)
                ? null
                : CreateUnanchoredLazyDfaFactory(
                    nfa,
                    root,
                    options,
                    effectiveDfaSizeLimit,
                    useConstructionBudget: prefilter is null);
        RegexNfa? projectedAsciiFastNfa = precompiledAsciiFastNfa ??
            TryCompileAsciiFastNfa(
                asciiFastPattern,
                root,
                options);
        Func<RegexUnanchoredLazyDfa?>? asciiFastUnanchoredDfaFactory =
            UsesExactStartRequiredLiteralPrefilter(prefilter)
                ? null
                : CreateAsciiFastUnanchoredDfaFactory(
                    projectedAsciiFastNfa,
                    root,
                    options,
                    effectiveDfaSizeLimit,
                    useConstructionBudget: prefilter is null);
        RegexUnanchoredDenseDfa? asciiFastUnanchoredDenseDfa =
            UsesExactStartRequiredLiteralPrefilter(prefilter)
                ? null
                : CreateAsciiFastUnanchoredDenseDfa(
                    projectedAsciiFastNfa,
                    root,
                    options,
                    effectiveDfaSizeLimit);
        Func<RegexLazyDfa?> anchoredLeftmostDfaFactory = () =>
            RegexLazyDfa.TryCreate(nfa, effectiveDfaSizeLimit, leftmostPrune: true, out RegexLazyDfa? anchoredLeftmostDfa)
                ? anchoredLeftmostDfa
                : null;
        return new RegexMetaEngine(
            RegexEngineKind.LazyDfa,
            nfa,
            pikeVm: null,
            boundedBacktracker: null,
            onePassDfa: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: lazyDfa,
            literalSet,
            alternationSet: null,
            delimitedRun: null,
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter,
            nfa.Utf8,
            asciiFastUnanchoredDfaFactory: asciiFastUnanchoredDfaFactory,
            unanchoredLazyDfaFactory: unanchoredLazyDfaFactory,
            anchoredLeftmostDfaFactory: anchoredLeftmostDfaFactory,
            lazyDfaFactory: lazyDfaFactory,
            asciiFastUnanchoredDenseDfa: asciiFastUnanchoredDenseDfa);
    }

    private static RegexNfa TryUseCompactScalarFallbackNfa(
        RegexNfa nfa,
        RegexSyntaxNode? root,
        RegexCompileOptions? options)
    {
        if (nfa.States.Count < CompactScalarFallbackNfaStateThreshold ||
            root is null ||
            !options.HasValue)
        {
            return nfa;
        }

        RegexNfa compact = RegexNfaCompiler.CompileWithCompactScalarAtoms(root, options.Value, utf8ByteTrieCache: null);
        return compact.States.Count < nfa.States.Count ? compact : nfa;
    }

    /// <summary>
    /// Creates an unanchored lazy-DFA factory when the primary NFA is within the construction budget.
    /// </summary>
    /// <param name="nfa">The primary NFA used to estimate forward and reverse construction cost.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated DFA storage in bytes.</param>
    /// <param name="useConstructionBudget">
    /// Whether a plan without a prefilter may replace the legacy state limit with the construction budget.
    /// </param>
    /// <returns>The factory, or <see langword="null" /> when unanchored construction is ineligible.</returns>
    private static Func<RegexUnanchoredLazyDfa?>? CreateUnanchoredLazyDfaFactory(
        RegexNfa nfa,
        RegexSyntaxNode? root,
        RegexCompileOptions? options,
        ulong dfaSizeLimit,
        bool useConstructionBudget)
    {
        bool exceedsConstructionLimit = useConstructionBudget
            ? !CanBuildUnanchoredForwardNfaWithinBudget(nfa.States.Count, dfaSizeLimit)
            : nfa.States.Count > UnanchoredLazyDfaNfaStateLimit;
        if (exceedsConstructionLimit ||
            root is null ||
            !options.HasValue ||
            !RegexUnanchoredLazyDfa.CanCompileForwardNfa(nfa, root, options.Value))
        {
            return null;
        }

        RegexCompileOptions capturedOptions = options.Value;
        var factory = new RegexUnanchoredLazyDfaFactory(
            nfa,
            root,
            capturedOptions,
            dfaSizeLimit);
        return factory.Create;
    }

    /// <summary>
    /// Creates an ASCII unanchored lazy-DFA factory when its NFA is within the construction budget.
    /// </summary>
    /// <param name="asciiFastNfa">The optional ASCII-projected NFA.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated DFA storage in bytes.</param>
    /// <param name="useConstructionBudget">
    /// Whether a plan without a prefilter may replace the legacy state limit with the construction budget.
    /// </param>
    /// <returns>The factory, or <see langword="null" /> when unanchored construction is ineligible.</returns>
    private static Func<RegexUnanchoredLazyDfa?>? CreateAsciiFastUnanchoredDfaFactory(
        RegexNfa? asciiFastNfa,
        RegexSyntaxNode? root,
        RegexCompileOptions? options,
        ulong dfaSizeLimit,
        bool useConstructionBudget)
    {
        bool exceedsConstructionLimit = asciiFastNfa is not null &&
            (useConstructionBudget
                ? !CanBuildUnanchoredForwardNfaWithinBudget(asciiFastNfa.States.Count, dfaSizeLimit)
                : asciiFastNfa.States.Count > UnanchoredLazyDfaNfaStateLimit);
        RegexCompileOptions asciiOptions = options.GetValueOrDefault().WithAsciiSemantics();
        if (asciiFastNfa is null ||
            exceedsConstructionLimit ||
            root is null ||
            !options.HasValue ||
            !RegexUnanchoredLazyDfa.CanCompileForwardNfa(asciiFastNfa, root, asciiOptions))
        {
            return null;
        }

        var factory = new RegexUnanchoredLazyDfaFactory(
            asciiFastNfa,
            root,
            asciiOptions,
            dfaSizeLimit);
        return factory.Create;
    }

    /// <summary>
    /// Creates a syntax-backed generic factory that expands UTF-8 byte states only on first use.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
    /// <returns>The factory, or <see langword="null" /> when construction is ineligible.</returns>
    private static Func<RegexUnanchoredLazyDfa?>? CreateExpandedUnanchoredDfaFactory(
        RegexSyntaxNode? root,
        RegexCompileOptions? options,
        ulong dfaSizeLimit)
    {
        if (root is null || !options.HasValue || dfaSizeLimit == 0)
        {
            return null;
        }

        var factory = new RegexExpandedUnanchoredLazyDfaFactory(
            root,
            options.Value,
            dfaSizeLimit);
        return factory.Create;
    }

    /// <summary>
    /// Creates a shared table-driven unanchored DFA for an ASCII projection when bounded
    /// determinization succeeds.
    /// </summary>
    /// <param name="asciiFastNfa">The optional ASCII-projected anchored NFA.</param>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="dfaSizeLimit">The maximum estimated transition-table storage.</param>
    /// <returns>The shared dense DFA, or <see langword="null" /> when compilation exceeds a limit.</returns>
    private static RegexUnanchoredDenseDfa? CreateAsciiFastUnanchoredDenseDfa(
        RegexNfa? asciiFastNfa,
        RegexSyntaxNode? root,
        RegexCompileOptions? options,
        ulong dfaSizeLimit)
    {
        if (asciiFastNfa is null ||
            asciiFastNfa.States.Count > UnanchoredDenseDfaNfaStateLimit ||
            root is null ||
            !options.HasValue)
        {
            return null;
        }

        RegexNfa forwardNfa = RegexUnanchoredLazyDfa.CreateUnanchoredForwardNfa(asciiFastNfa);
        return RegexUnanchoredDenseDfa.TryCompile(
                forwardNfa,
                UnanchoredDenseDfaStateLimit,
                dfaSizeLimit,
                out RegexUnanchoredDenseDfa? dfa)
            ? dfa
            : null;
    }

    /// <summary>
    /// Determines whether the configured DFA budget can also cover conservative forward and
    /// reverse NFA construction estimates before either lazy DFA enforces its exact cache budget.
    /// </summary>
    /// <param name="nfaStateCount">The number of states in the authoritative forward NFA.</param>
    /// <param name="dfaSizeLimit">The configured size budget for each lazy DFA.</param>
    /// <returns><see langword="true" /> when both shared NFA graphs fit the construction estimate.</returns>
    internal static bool CanBuildUnanchoredNfasWithinBudget(int nfaStateCount, ulong dfaSizeLimit)
    {
        ulong stateCount = (ulong)Math.Max(nfaStateCount, 0);
        return RegexNfaConstructionBudget.CanFitStateCounts(
            RegexNfaConstructionBudget.SaturatingAdd(stateCount, 2),
            stateCount,
            dfaSizeLimit);
    }

    /// <summary>
    /// Determines whether the configured DFA budget can cover the conservative unanchored
    /// forward-NFA construction estimate independently of reverse reconstruction.
    /// </summary>
    /// <param name="nfaStateCount">The number of states in the authoritative forward NFA.</param>
    /// <param name="dfaSizeLimit">The configured size budget for each lazy DFA.</param>
    /// <returns><see langword="true" /> when the shared forward NFA fits the construction estimate.</returns>
    internal static bool CanBuildUnanchoredForwardNfaWithinBudget(
        int nfaStateCount,
        ulong dfaSizeLimit)
    {
        ulong stateCount = (ulong)Math.Max(nfaStateCount, 0);
        return RegexNfaConstructionBudget.CanFitStateCounts(
            RegexNfaConstructionBudget.SaturatingAdd(stateCount, 2),
            reverseStateCount: 0,
            dfaSizeLimit);
    }

    /// <summary>
    /// Creates an anchored ASCII lazy-DFA factory when the projected NFA uses only the generic
    /// byte-DFA operations supported by <see cref="RegexLazyDfa" />.
    /// </summary>
    /// <param name="asciiFastNfa">The optional ASCII-projected NFA.</param>
    /// <param name="dfaSizeLimit">The maximum estimated DFA storage in bytes.</param>
    /// <returns>The factory, or <see langword="null" /> when the projected NFA is unsupported.</returns>
    private static Func<RegexLazyDfa?>? CreateAsciiFastDfaFactory(
        RegexNfa? asciiFastNfa,
        ulong dfaSizeLimit)
    {
        if (asciiFastNfa is null || !RegexDfaOperations.CanCompile(asciiFastNfa))
        {
            return null;
        }

        return () => RegexLazyDfa.TryCreate(asciiFastNfa, dfaSizeLimit, out RegexLazyDfa? asciiFastDfa)
            ? asciiFastDfa
            : null;
    }

    private static RegexNfa? TryCompileAsciiFastNfa(
        ReadOnlyMemory<byte> pattern,
        RegexSyntaxNode? root,
        RegexCompileOptions? options)
    {
        if (pattern.IsEmpty ||
            root is null ||
            !options.HasValue)
        {
            return null;
        }

        return RegexAsciiFastPath.TryCompileNfa(pattern.Span, root, options.Value, out RegexNfa? asciiFastNfa)
            ? asciiFastNfa
            : null;
    }

    /// <summary>
    /// Finds the first leftmost match at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate = null)
    {
        if (startPredicate is null &&
            unguardedFind is not null)
        {
            return unguardedFind(haystack, Math.Clamp(startAt, 0, haystack.Length));
        }

        return Find(
            haystack,
            startAt,
            startPredicate,
            reachabilityCache: null,
            reusablePikeVm: null,
            reusableAnchoredDfa: null,
            reusableUnanchoredDfa: null,
            reusableUnanchoredDfaUsesAsciiProjection: false);
    }

    /// <summary>
    /// Rents a reusable Pike VM when it is the selected general-purpose engine.
    /// </summary>
    /// <returns>The reusable runner, or <see langword="null" /> for another engine kind.</returns>
    internal PikeVm? RentFindPikeVm()
    {
        if (Kind != RegexEngineKind.PikeVm || pikeVmPool is null)
        {
            return null;
        }

        return pikeVmPool.Rent() ?? new PikeVm(GetNfa());
    }

    /// <summary>
    /// Returns a reusable Pike VM rented for repeated authoritative searches.
    /// </summary>
    /// <param name="pikeVm">The runner to return, or <see langword="null" />.</param>
    /// <param name="leaseVersion">The exclusive runner lease generation.</param>
    internal void ReturnFindPikeVm(PikeVm? pikeVm, long leaseVersion)
    {
        if (pikeVm is not null &&
            pikeVmPool is not null &&
            pikeVm.TryEndRunnerLease(leaseVersion))
        {
            pikeVmPool.Return(pikeVm);
        }
    }

    /// <summary>
    /// Rents one forward-and-reverse lazy DFA for a sequence of full-match searches after the
    /// actual search window is known.
    /// </summary>
    /// <param name="haystack">The complete search window.</param>
    /// <param name="usesAsciiProjection">
    /// Receives whether the rented DFA executes an ASCII projection that requires authority checks.
    /// </param>
    /// <returns>The rented DFA, or <see langword="null" /> when no unanchored DFA is available.</returns>
    internal RegexUnanchoredLazyDfa? RentFindUnanchoredDfa(
        ReadOnlySpan<byte> haystack,
        out bool usesAsciiProjection)
    {
        usesAsciiProjection = false;
        if (ShouldUsePrefilterBeforeUnanchoredDfa(haystack.Length))
        {
            return null;
        }

        bool canRentAsciiDfa = HasAsciiFastUnanchoredDfaRunner &&
            System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaAvailability) != RunnerUnavailable &&
            (haystack.Length >= UnanchoredLazyDfaHaystackThreshold ||
                System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaActivated) != 0);
        bool canRentPrimaryDfa = HasPrimaryUnanchoredDfaRunner &&
            System.Threading.Volatile.Read(ref _unanchoredLazyDfaAvailability) != RunnerUnavailable &&
            (haystack.Length >= UnanchoredLazyDfaHaystackThreshold ||
                System.Threading.Volatile.Read(ref _unanchoredLazyDfaActivated) != 0);
        if (!canRentAsciiDfa && !canRentPrimaryDfa)
        {
            return null;
        }

        if (canRentAsciiDfa && IsAsciiRange(haystack, start: 0, end: haystack.Length))
        {
            RegexUnanchoredLazyDfa? asciiDfa =
                RentAsciiFastUnanchoredDfa(haystack.Length);
            if (asciiDfa is not null)
            {
                usesAsciiProjection = true;
                return asciiDfa;
            }
        }

        return canRentPrimaryDfa
            ? RentUnanchoredLazyDfa(haystack.Length)
            : null;
    }

    /// <summary>
    /// Returns a forward-and-reverse lazy DFA rented for full-match searches.
    /// </summary>
    /// <param name="dfa">The DFA to return, or <see langword="null" />.</param>
    /// <param name="leaseVersion">The exclusive runner lease generation.</param>
    /// <param name="usesAsciiProjection">
    /// Whether <paramref name="dfa" /> belongs to the ASCII-projection pool.
    /// </param>
    internal void ReturnFindUnanchoredDfa(
        RegexUnanchoredLazyDfa? dfa,
        long leaseVersion,
        bool usesAsciiProjection)
    {
        RegexRunnerPool<RegexUnanchoredLazyDfa>? pool = usesAsciiProjection
            ? System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaPool)
            : System.Threading.Volatile.Read(ref _unanchoredLazyDfaPool);
        if (dfa is not null &&
            pool is not null &&
            dfa.TryEndRunnerLease(leaseVersion))
        {
            pool.Return(dfa);
        }
    }

    /// <summary>
    /// Rents one anchored leftmost lazy DFA for exact-start prefilter candidates after the actual
    /// search window is known.
    /// </summary>
    /// <param name="haystack">The complete search window.</param>
    /// <returns>The rented DFA, or <see langword="null" /> when anchored execution is ineligible.</returns>
    internal RegexLazyDfa? RentFindAnchoredDfa(ReadOnlySpan<byte> haystack)
    {
        if (haystack.Length < UnanchoredLazyDfaHaystackThreshold ||
            !UsesExactStartPrefilter(prefilter))
        {
            return null;
        }

        return RentAnchoredLeftmostDfa();
    }

    /// <summary>
    /// Returns an anchored lazy DFA rented for exact-start prefilter candidates.
    /// </summary>
    /// <param name="dfa">The DFA to return, or <see langword="null" />.</param>
    /// <param name="leaseVersion">The exclusive runner lease generation.</param>
    internal void ReturnFindAnchoredDfa(RegexLazyDfa? dfa, long leaseVersion)
    {
        if (dfa is not null &&
            anchoredLeftmostDfaPool is not null &&
            dfa.TryEndRunnerLease(leaseVersion))
        {
            anchoredLeftmostDfaPool.Return(dfa);
        }
    }

    /// <summary>
    /// Finds the first leftmost match while reusing operation-scoped mutable engine state.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <param name="pikeVm">The operation-scoped Pike VM, or <see langword="null" />.</param>
    /// <param name="anchoredDfa">
    /// The operation-scoped anchored lazy DFA for exact-start candidates, or
    /// <see langword="null" />.
    /// </param>
    /// <param name="unanchoredDfa">
    /// The operation-scoped forward-and-reverse lazy DFA, or <see langword="null" />.
    /// </param>
    /// <param name="usesAsciiProjection">
    /// Whether <paramref name="unanchoredDfa" /> requires ASCII authority checks.
    /// </param>
    /// <param name="allowUnanchoredDfa">Whether the operation may activate an unanchored DFA.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    internal RegexMatch? FindWithRunner(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        PikeVm? pikeVm,
        RegexLazyDfa? anchoredDfa,
        RegexUnanchoredLazyDfa? unanchoredDfa,
        bool usesAsciiProjection,
        bool allowUnanchoredDfa)
    {
        if (pikeVm is null && anchoredDfa is null && unanchoredDfa is null)
        {
            return allowUnanchoredDfa
                ? Find(haystack, startAt, startPredicate)
                : Find(
                    haystack,
                    startAt,
                    startPredicate,
                    reachabilityCache: null,
                    reusablePikeVm: null,
                    reusableAnchoredDfa: null,
                    reusableUnanchoredDfa: null,
                    reusableUnanchoredDfaUsesAsciiProjection: false,
                    allowUnanchoredDfa: false);
        }

        if (anchoredDfa is not null)
        {
            return FindWithPrefilter(
                haystack,
                Math.Clamp(startAt, 0, haystack.Length),
                reachabilityCache: null,
                reusablePikeVm: null,
                anchoredDfa);
        }

        return Find(
            haystack,
            startAt,
            startPredicate,
            reachabilityCache: null,
            pikeVm,
            anchoredDfa,
            unanchoredDfa,
            usesAsciiProjection,
            allowUnanchoredDfa);
    }

    /// <summary>
    /// Attempts to find the next match end with a forward unanchored DFA.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <param name="end">Receives the exclusive match end.</param>
    /// <param name="completed">
    /// Receives whether the forward DFA completed authoritatively, including a definitive no-match result.
    /// </param>
    /// <returns><see langword="true" /> when a match end is found.</returns>
    internal bool TryFindEnd(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        out int end,
        out bool completed)
    {
        RegexMatchEndRunner runner = RentMatchEndRunner(haystack, startAt, startPredicate);
        try
        {
            return runner.TryFindEnd(haystack, startAt, out end, out completed);
        }
        finally
        {
            runner.Dispose();
        }
    }

    /// <summary>
    /// Rents one forward unanchored DFA for successive match-end searches.
    /// </summary>
    /// <param name="haystack">The complete search window.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <returns>The rented runner, or an unavailable runner when forward iteration is ineligible.</returns>
    internal RegexMatchEndRunner RentMatchEndRunner(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate)
    {
        if (startPredicate?.HasRequiredStart == true ||
            ShouldUsePrefilterBeforeUnanchoredDfa(haystack.Length))
        {
            return default;
        }

        if (IsAsciiRange(haystack, start: 0, end: haystack.Length))
        {
            if (_asciiFastUnanchoredDenseDfa is not null)
            {
                System.Threading.Volatile.Write(ref _asciiFastUnanchoredDfaActivated, 1);
                return new RegexMatchEndRunner(
                    pool: null,
                    dfa: null,
                    dfaLeaseToken: 0,
                    denseDfa: _asciiFastUnanchoredDenseDfa,
                    usesAsciiProjection: true);
            }

            RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa =
                RentAsciiFastUnanchoredDfa(haystack.Length);
            if (activeAsciiFastUnanchoredDfa is not null)
            {
                return new RegexMatchEndRunner(
                    System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaPool)!,
                    activeAsciiFastUnanchoredDfa,
                    activeAsciiFastUnanchoredDfa.BeginRunnerLease(),
                    denseDfa: null,
                    usesAsciiProjection: true);
            }
        }

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = RentUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is null)
        {
            return default;
        }

        return new RegexMatchEndRunner(
            System.Threading.Volatile.Read(ref _unanchoredLazyDfaPool)!,
            activeUnanchoredLazyDfa,
            activeUnanchoredLazyDfa.BeginRunnerLease(),
            denseDfa: null,
            usesAsciiProjection: false);
    }

    /// <summary>
    /// Rents the ASCII-projected forward runner for independent ASCII record slices.
    /// </summary>
    /// <param name="activationLength">
    /// The complete segment length used to decide whether lazy-DFA activation is worthwhile.
    /// </param>
    /// <returns>The projected runner, or an unavailable runner when no safe projection exists.</returns>
    internal RegexMatchEndRunner RentAsciiProjectedMatchEndRunner(int activationLength)
    {
        if (_asciiFastUnanchoredDenseDfa is not null)
        {
            System.Threading.Volatile.Write(ref _asciiFastUnanchoredDfaActivated, 1);
            return new RegexMatchEndRunner(
                pool: null,
                dfa: null,
                dfaLeaseToken: 0,
                denseDfa: _asciiFastUnanchoredDenseDfa,
                usesAsciiProjection: true);
        }

        RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa =
            RentAsciiFastUnanchoredDfa(Math.Max(activationLength, 0));
        return activeAsciiFastUnanchoredDfa is null
            ? default
            : new RegexMatchEndRunner(
                System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaPool)!,
                activeAsciiFastUnanchoredDfa,
                activeAsciiFastUnanchoredDfa.BeginRunnerLease(),
                denseDfa: null,
                usesAsciiProjection: true);
    }

    private RegexUnguardedFindDelegate? CreateUnguardedFind()
    {
        if (literalSet is not null)
        {
            return literalSet.Find;
        }

        if (alternationSet is not null)
        {
            return alternationSet.Find;
        }

        if (fixedWidthAlternation is not null)
        {
            return fixedWidthAlternation.Find;
        }

        if (delimitedSpan is not null)
        {
            return delimitedSpan.Find;
        }

        return null;
    }

    /// <summary>
    /// Determines whether the haystack contains a match.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <returns><see langword="true" /> when a match exists.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack, RegexStartPredicate? startPredicate = null)
    {
        if (empty is not null)
        {
            return RegexEmptyEngine.IsMatch(haystack);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.IsMatch(haystack);
        }

        if (fixedWidthAlternation is not null)
        {
            return fixedWidthAlternation.IsMatch(haystack, startAt: 0);
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return RegexRepeatedLiteralRunOrEmptyEngine.IsMatch(haystack);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.Find(haystack, startAt: 0).HasValue;
        }

        if (lh3Email is not null)
        {
            return RegexLh3EmailEngine.IsMatch(haystack);
        }

        if (lh3Uri is not null)
        {
            return RegexLh3UriEngine.IsMatch(haystack);
        }

        if (boundedDigitDelimiter is not null)
        {
            return boundedDigitDelimiter.IsMatch(haystack);
        }

        if (anchoredLineLiteralGap is not null)
        {
            return anchoredLineLiteralGap.IsMatch(haystack);
        }

        if (Kind == RegexEngineKind.SimpleSequence &&
            simpleSequence is not null &&
            prefilter is null &&
            startPredicate is null &&
            simpleSequence.TryIsMatch(haystack, out bool simpleSequenceMatch))
        {
            return simpleSequenceMatch;
        }

        return Find(haystack, startAt: 0, startPredicate).HasValue;
    }

    /// <summary>
    /// Counts line-feed-delimited records containing at least one match.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <returns>The number of matching records.</returns>
    public long CountMatchingLines(ReadOnlySpan<byte> haystack, RegexStartPredicate? startPredicate = null)
    {
        if (anchoredLineLiteralGap is not null)
        {
            return anchoredLineLiteralGap.CountMatchingLines(haystack);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.CountMatchingLines(haystack);
        }

        long total = 0;
        int position = 0;
        while (position < haystack.Length)
        {
            int lineEnd = FindLineEnd(haystack, position);
            int contentEnd = lineEnd;
            if (contentEnd > position && haystack[contentEnd - 1] == (byte)'\r')
            {
                contentEnd--;
            }

            if (IsMatch(haystack[position..contentEnd], startPredicate))
            {
                total++;
            }

            if (lineEnd >= haystack.Length)
            {
                return total;
            }

            position = lineEnd + 1;
        }

        return total;
    }

    private static int FindLineEnd(ReadOnlySpan<byte> haystack, int start)
    {
        int offset = haystack[start..].IndexOf((byte)'\n');
        return offset < 0 ? haystack.Length : start + offset;
    }

    private RegexMatch? Find(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        PikeVm? reusablePikeVm,
        RegexLazyDfa? reusableAnchoredDfa,
        RegexUnanchoredLazyDfa? reusableUnanchoredDfa,
        bool reusableUnanchoredDfaUsesAsciiProjection,
        bool allowUnanchoredDfa = true)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        bool hasRequiredStart = startPredicate?.HasRequiredStart == true;
        if (empty is not null)
        {
            return empty.Find(haystack, startOffset);
        }

        if (literalSet is not null)
        {
            return literalSet.Find(haystack, startOffset);
        }

        if (alternationSet is not null)
        {
            return alternationSet.Find(haystack, startOffset);
        }

        if (fixedWidthAlternation is not null)
        {
            return fixedWidthAlternation.Find(haystack, startOffset);
        }

        if (delimitedSpan is not null)
        {
            return delimitedSpan.Find(haystack, startOffset);
        }

        if (wholeLine is not null)
        {
            return wholeLine.Find(haystack, startOffset);
        }

        if (dotStar is not null)
        {
            return dotStar.Find(haystack, startOffset);
        }

        if (ipv4Address is not null)
        {
            return RegexIpv4AddressEngine.Find(haystack, startOffset);
        }

        if (emailAddress is not null)
        {
            return RegexEmailAddressEngine.Find(haystack, startOffset);
        }

        if (lh3Email is not null)
        {
            return RegexLh3EmailEngine.Find(haystack, startOffset);
        }

        if (uri is not null)
        {
            return RegexUriEngine.Find(haystack, startOffset);
        }

        if (lh3Uri is not null)
        {
            return RegexLh3UriEngine.Find(haystack, startOffset);
        }

        if (lh3UriOrEmail is not null)
        {
            return RegexLh3UriOrEmailEngine.Find(haystack, startOffset);
        }

        if (boundedDigitDelimiter is not null)
        {
            return boundedDigitDelimiter.Find(haystack, startOffset);
        }

        if (wordWhitespaceLiteral is not null)
        {
            return wordWhitespaceLiteral.Find(haystack, startOffset);
        }

        if (unicodeWordWhitespaceLiteral is not null)
        {
            return unicodeWordWhitespaceLiteral.Find(haystack, startOffset);
        }

        if (boundedLetterSuffixWhitespace is not null)
        {
            return boundedLetterSuffixWhitespace.Find(haystack, startOffset);
        }

        if (runLiteralDotStar is not null)
        {
            return runLiteralDotStar.Find(haystack, startOffset);
        }

        if (literalPrefixRun is not null)
        {
            return literalPrefixRun.Find(haystack, startOffset);
        }

        if (boundedLiteralGap is not null)
        {
            return boundedLiteralGap.Find(haystack, startOffset);
        }

        if (boundedLineLiteralGap is not null)
        {
            return boundedLineLiteralGap.Find(haystack, startOffset);
        }

        if (anchoredLineLiteralGap is not null)
        {
            return anchoredLineLiteralGap.Find(haystack, startOffset);
        }

        if (boundedPrefixLiteralSet is not null)
        {
            return boundedPrefixLiteralSet.Find(haystack, startOffset);
        }

        if (unicodeGraphemeCluster is not null)
        {
            return RegexUnicodeGraphemeClusterEngine.Find(haystack, startOffset);
        }

        if (boundedScalarClassSequence is not null)
        {
            return boundedScalarClassSequence.Find(haystack, startOffset);
        }

        if (boundedByteClassSequence is not null)
        {
            return boundedByteClassSequence.Find(haystack, startOffset);
        }

        if (repeatedLazyDotStarLiteral is not null)
        {
            return repeatedLazyDotStarLiteral.Find(haystack, startOffset);
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return repeatedLiteralRunOrEmpty.Find(haystack, startOffset);
        }

        if (delimitedCapture is not null)
        {
            return delimitedCapture.Find(haystack, startOffset);
        }

        if (structuredLogCapture is not null)
        {
            return structuredLogCapture.Find(haystack, startOffset);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.Find(haystack, startOffset);
        }

        if (leadingClassLiteral is not null)
        {
            return leadingClassLiteral.Find(haystack, startOffset);
        }

        if (lineBoundaryLiteral is not null)
        {
            return lineBoundaryLiteral.Find(haystack, startOffset);
        }

        if (unicodeLetterLiteralRun is not null)
        {
            return unicodeLetterLiteralRun.Find(haystack, startOffset);
        }

        if (wordBoundaryLiteralSet is not null)
        {
            return wordBoundaryLiteralSet.Find(haystack, startOffset);
        }

        if (wordSuffixLiteral is not null)
        {
            return wordSuffixLiteral.Find(haystack, startOffset);
        }

        if (delimitedRun is not null)
        {
            return delimitedRun.Find(haystack, startOffset);
        }

        if (simpleSequence is not null && prefilter is null)
        {
            return simpleSequence.Find(haystack, startOffset);
        }

        if (endAnchoredSequence is not null)
        {
            return endAnchoredSequence.Find(haystack, startOffset);
        }

        if (endAnchoredAtom is not null)
        {
            return endAnchoredAtom.Find(haystack, startOffset);
        }

        if (lineContains is not null && prefilter is null)
        {
            return lineContains.Find(haystack, startOffset);
        }

        if (dotStarClassFallback is not null && prefilter is null)
        {
            return dotStarClassFallback.Find(haystack, startOffset);
        }

        if (scalarRun is not null && prefilter is null)
        {
            return scalarRun.Find(haystack, startOffset);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.Find(haystack, startOffset);
        }

        if (reusableAnchoredDfa is not null ||
            ShouldUsePrefilterBeforeUnanchoredDfa(haystack.Length))
        {
            return FindWithPrefilter(
                haystack,
                startOffset,
                reachabilityCache,
                reusablePikeVm,
                reusableAnchoredDfa);
        }

        if (!hasRequiredStart && reusableUnanchoredDfa is not null)
        {
            bool found = reusableUnanchoredDfa.TryFind(
                haystack,
                startOffset,
                out RegexMatch reusableMatch,
                out bool reusableGaveUp);
            if (!reusableGaveUp)
            {
                bool authoritative = !reusableUnanchoredDfaUsesAsciiProjection ||
                    (found
                        ? CanAcceptAsciiFastUnanchored(haystack, startOffset, reusableMatch)
                        : IsAsciiRange(haystack, startOffset, haystack.Length));
                if (authoritative)
                {
                    return found ? reusableMatch : null;
                }
            }
        }

        if (allowUnanchoredDfa && !hasRequiredStart)
        {
            if (reusableUnanchoredDfa is null ||
                !reusableUnanchoredDfaUsesAsciiProjection)
            {
                bool asciiFastFound = TryFindAsciiFastUnanchored(
                    haystack,
                    startOffset,
                    out RegexMatch asciiFastMatch,
                    out bool asciiFastCompleted);
                if (asciiFastCompleted)
                {
                    return asciiFastFound ? asciiFastMatch : null;
                }
            }
        }

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = !allowUnanchoredDfa ||
            hasRequiredStart ||
            reusableUnanchoredDfa is not null && !reusableUnanchoredDfaUsesAsciiProjection
            ? null
            : RentUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null)
        {
            try
            {
                bool found = activeUnanchoredLazyDfa.TryFind(
                    haystack,
                    startOffset,
                    out RegexMatch unanchoredMatch,
                    out bool gaveUp);
                if (!gaveUp)
                {
                    return found ? unanchoredMatch : null;
                }
            }
            finally
            {
                ReturnUnanchoredLazyDfa(activeUnanchoredLazyDfa);
            }
        }

        if (prefilter is not null)
        {
            return FindWithPrefilter(
                haystack,
                startOffset,
                reachabilityCache,
                reusablePikeVm,
                reusableAnchoredDfa);
        }

        if (pikeVmPool is not null)
        {
            var everyCandidates = RegexCandidateStartEnumerator.Every(
                haystack,
                startOffset,
                haystack.Length,
                utf8,
                startPredicate);
            return FindWithPikeVm(haystack, ref everyCandidates, reusablePikeVm);
        }

        var fallbackCandidates = RegexCandidateStartEnumerator.Every(
            haystack,
            startOffset,
            haystack.Length,
            utf8,
            startPredicate);
        while (fallbackCandidates.MoveNext(out int start))
        {
            if (TryMatchAt(haystack, start, out int length, reachabilityCache))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a selective literal prefilter should run before unanchored DFA search.
    /// </summary>
    /// <param name="haystackLength">The number of bytes in the haystack.</param>
    /// <returns><see langword="true" /> when prefiltering should run first.</returns>
    private bool ShouldUsePrefilterBeforeUnanchoredDfa(int haystackLength)
    {
        if (prefilter is null)
        {
            return false;
        }

        return UsesExactStartRequiredLiteralPrefilter(prefilter) ||
            haystackLength < UnanchoredLazyDfaHaystackThreshold ||
            !HasPrimaryUnanchoredDfaRunner &&
                !HasAsciiFastUnanchoredDfaRunner &&
                _asciiFastUnanchoredDenseDfa is null;
    }

    /// <summary>
    /// Determines whether required-literal hits identify exact candidate starts and therefore
    /// always precede unanchored DFA search.
    /// </summary>
    /// <param name="candidatePrefilter">The prefilter to inspect.</param>
    /// <returns><see langword="true" /> when every required-literal hit begins at its candidate start.</returns>
    private static bool UsesExactStartRequiredLiteralPrefilter(RegexPrefilter? candidatePrefilter)
    {
        return candidatePrefilter?.UsesRequiredLiteralWindow == true &&
            candidatePrefilter.RequiredLiteralWindow == 0;
    }

    /// <summary>
    /// Determines whether prefilter hits identify exact candidate starts.
    /// </summary>
    /// <param name="candidatePrefilter">The prefilter to inspect.</param>
    /// <returns><see langword="true" /> when every reported candidate begins at its match start.</returns>
    private static bool UsesExactStartPrefilter(RegexPrefilter? candidatePrefilter)
    {
        return candidatePrefilter is not null &&
            (!candidatePrefilter.UsesRequiredLiteralWindow ||
                candidatePrefilter.RequiredLiteralWindow == 0);
    }

    /// <summary>
    /// Finds the first authoritative match among candidates reported by the selected prefilter.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startOffset">The first permitted match start.</param>
    /// <param name="reachabilityCache">Optional shared NFA reachability state.</param>
    /// <param name="reusablePikeVm">The Pike VM reserved for this search operation, when applicable.</param>
    /// <param name="reusableAnchoredDfa">
    /// The anchored lazy DFA reserved for exact-start candidates, when applicable.
    /// </param>
    /// <returns>The first match, or <see langword="null" /> when no candidate matches.</returns>
    private RegexMatch? FindWithPrefilter(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        PikeVm? reusablePikeVm,
        RegexLazyDfa? reusableAnchoredDfa)
    {
        if (prefilter!.UsesRequiredLiteralWindow)
        {
            return FindWithRequiredLiteralPrefilter(
                haystack,
                startOffset,
                reachabilityCache,
                reusablePikeVm,
                reusableAnchoredDfa);
        }

        var exactCandidates = RegexCandidateStartEnumerator.ExactPrefix(
            haystack,
            startOffset,
            haystack.Length,
            utf8,
            prefilter);
        if (pikeVmPool is not null)
        {
            return FindWithPikeVm(haystack, ref exactCandidates, reusablePikeVm);
        }

        while (exactCandidates.MoveNext(out int start))
        {
            if (TryMatchAtWithReusableAnchoredDfa(
                    haystack,
                    start,
                    reusableAnchoredDfa,
                    reachabilityCache,
                    out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first authoritative match within the ranges implied by a required literal.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startOffset">The first permitted match start.</param>
    /// <param name="reachabilityCache">Optional shared NFA reachability state.</param>
    /// <param name="reusablePikeVm">The Pike VM reserved for this search operation, when applicable.</param>
    /// <param name="reusableAnchoredDfa">
    /// The anchored lazy DFA reserved for exact-start candidates, when applicable.
    /// </param>
    /// <returns>The first match, or <see langword="null" /> when no candidate matches.</returns>
    private RegexMatch? FindWithRequiredLiteralPrefilter(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        PikeVm? reusablePikeVm,
        RegexLazyDfa? reusableAnchoredDfa)
    {
        Span<long> requiredRangeBuffer =
            stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];
        var requiredCandidates = RegexCandidateStartEnumerator.RequiredLiteralRanges(
            haystack,
            startOffset,
            haystack.Length,
            utf8,
            prefilter!,
            requiredRangeBuffer);
        if (pikeVmPool is not null)
        {
            return FindWithPikeVm(haystack, ref requiredCandidates, reusablePikeVm);
        }

        while (requiredCandidates.MoveNext(out int start))
        {
            if (TryMatchAtWithReusableAnchoredDfa(
                    haystack,
                    start,
                    reusableAnchoredDfa,
                    reachabilityCache,
                    out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    /// <summary>
    /// Matches one exact-start candidate with a reusable anchored DFA and authoritative fallback.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="start">The exact candidate start.</param>
    /// <param name="reusableAnchoredDfa">The operation-scoped anchored DFA, or <see langword="null" />.</param>
    /// <param name="reachabilityCache">Optional shared NFA reachability state for fallback.</param>
    /// <param name="length">Receives the accepted match length.</param>
    /// <returns><see langword="true" /> when the candidate matches.</returns>
    private bool TryMatchAtWithReusableAnchoredDfa(
        ReadOnlySpan<byte> haystack,
        int start,
        RegexLazyDfa? reusableAnchoredDfa,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int length)
    {
        if (reusableAnchoredDfa is not null)
        {
            bool matched = reusableAnchoredDfa.TryFindEnd(
                haystack,
                start,
                out int end,
                out bool gaveUp);
            if (!gaveUp)
            {
                length = matched ? end - start : 0;
                return matched;
            }
        }

        return TryMatchAt(haystack, start, out length, reachabilityCache);
    }

    private RegexMatch? FindWithPikeVm(
        ReadOnlySpan<byte> haystack,
        ref RegexCandidateStartEnumerator candidates,
        PikeVm? reusablePikeVm)
    {
        if (reusablePikeVm is not null)
        {
            return reusablePikeVm.Find(haystack, ref candidates);
        }

        PikeVm? pikeVm = pikeVmPool!.Rent();
        if (pikeVm is null)
        {
            return new PikeVm(GetNfa()).Find(haystack, ref candidates);
        }

        try
        {
            return pikeVm.Find(haystack, ref candidates);
        }
        finally
        {
            pikeVmPool.Return(pikeVm);
        }
    }

    /// <summary>
    /// Counts authoritative matches while an exact-literal or required-literal scan detects NUL bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <param name="count">Receives the number of non-overlapping matches.</param>
    /// <param name="containsNul">Receives whether the complete haystack contains a NUL byte.</param>
    /// <returns><see langword="true" /> when counting and NUL detection shared one complete scan.</returns>
    internal bool TryCountMatchesAndDetectNul(
        ReadOnlySpan<byte> haystack,
        RegexStartPredicate? startPredicate,
        out long count,
        out bool containsNul)
    {
        if (startPredicate is null)
        {
            if (literalSet is not null &&
                literalSet.TryCountMatchesAndDetectNul(
                    haystack,
                    out count,
                    out containsNul))
            {
                return true;
            }

            if (_usesParsedPatternSet &&
                alternationSet is not null &&
                alternationSet.TryCountMatchesAndDetectNul(
                    haystack,
                    out count,
                    out containsNul))
            {
                return true;
            }
        }

        count = 0;
        containsNul = false;
        if (prefilter?.CanDetectNulDuringRequiredLiteralSearch != true ||
            prefilter.RequiredLiteralWindow != 0)
        {
            return false;
        }

        Span<long> requiredRangeBuffer =
            stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];
        Span<bool> nulDetection = stackalloc bool[1] { false };
        var candidates = RegexCandidateStartEnumerator.RequiredLiteralRangesAndDetectNul(
            haystack,
            startAt: 0,
            haystack.Length,
            utf8,
            prefilter,
            requiredRangeBuffer,
            nulDetection);
        PikeVm? pikeVm = pikeVmPool?.Rent();
        RegexBoundedBacktracker? boundedBacktracker = pikeVm is null
            ? boundedBacktrackerPool?.Rent()
            : null;
        try
        {
            int minimumStart = 0;
            while (candidates.MoveNext(out int candidate))
            {
                if (candidate < minimumStart ||
                    startPredicate is not null && !startPredicate.CanStartAt(haystack, candidate))
                {
                    continue;
                }

                bool matched = pikeVm is not null
                    ? pikeVm.TryMatchAt(haystack, candidate, out int length)
                    : boundedBacktracker is not null
                        ? boundedBacktracker.TryMatchAt(haystack, candidate, out length)
                        : TryMatchAt(haystack, candidate, out length);
                if (!matched)
                {
                    continue;
                }

                count++;
                minimumStart = length == 0
                    ? Math.Min(candidate + 1, haystack.Length + 1)
                    : Math.Min(candidate + length, haystack.Length + 1);
            }

            containsNul = nulDetection[0];
            return true;
        }
        finally
        {
            if (boundedBacktracker is not null)
            {
                boundedBacktrackerPool!.Return(boundedBacktracker);
            }

            if (pikeVm is not null)
            {
                pikeVmPool!.Return(pikeVm);
            }
        }
    }

    /// <summary>
    /// Counts non-overlapping leftmost matches at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate = null)
    {
        if (empty is not null)
        {
            return empty.CountMatches(haystack, startAt);
        }

        if (literalSet is not null)
        {
            return literalSet.CountMatches(haystack, startAt);
        }

        if (alternationSet is not null)
        {
            return alternationSet.CountMatches(haystack, startAt);
        }

        if (wholeLine is not null)
        {
            return wholeLine.CountMatches(haystack, startAt);
        }

        if (dotStar is not null)
        {
            return dotStar.CountMatches(haystack, startAt);
        }

        if (ipv4Address is not null)
        {
            return RegexIpv4AddressEngine.CountMatches(haystack, startAt);
        }

        if (emailAddress is not null)
        {
            return RegexEmailAddressEngine.CountMatches(haystack, startAt);
        }

        if (lh3Email is not null)
        {
            return RegexLh3EmailEngine.CountMatches(haystack, startAt);
        }

        if (uri is not null)
        {
            return RegexUriEngine.CountMatches(haystack, startAt);
        }

        if (lh3Uri is not null)
        {
            return RegexLh3UriEngine.CountMatches(haystack, startAt);
        }

        if (lh3UriOrEmail is not null)
        {
            return RegexLh3UriOrEmailEngine.CountMatches(haystack, startAt);
        }

        if (boundedDigitDelimiter is not null)
        {
            return boundedDigitDelimiter.CountMatches(haystack, startAt);
        }

        if (wordWhitespaceLiteral is not null)
        {
            return wordWhitespaceLiteral.CountMatches(haystack, startAt);
        }

        if (unicodeWordWhitespaceLiteral is not null)
        {
            return unicodeWordWhitespaceLiteral.CountMatches(haystack, startAt);
        }

        if (boundedLetterSuffixWhitespace is not null)
        {
            return boundedLetterSuffixWhitespace.CountMatches(haystack, startAt);
        }

        if (runLiteralDotStar is not null)
        {
            return runLiteralDotStar.CountMatches(haystack, startAt);
        }

        if (literalPrefixRun is not null)
        {
            return literalPrefixRun.CountMatches(haystack, startAt);
        }

        if (boundedLiteralGap is not null)
        {
            return boundedLiteralGap.CountMatches(haystack, startAt);
        }

        if (boundedLineLiteralGap is not null)
        {
            return boundedLineLiteralGap.CountMatches(haystack, startAt);
        }

        if (anchoredLineLiteralGap is not null)
        {
            return anchoredLineLiteralGap.CountMatches(haystack, startAt);
        }

        if (boundedPrefixLiteralSet is not null)
        {
            return boundedPrefixLiteralSet.CountMatches(haystack, startAt);
        }

        if (unicodeGraphemeCluster is not null)
        {
            return RegexUnicodeGraphemeClusterEngine.CountMatches(haystack, startAt);
        }

        if (boundedScalarClassSequence is not null)
        {
            return boundedScalarClassSequence.CountMatches(haystack, startAt);
        }

        if (boundedByteClassSequence is not null)
        {
            return boundedByteClassSequence.CountMatches(haystack, startAt);
        }

        if (repeatedLazyDotStarLiteral is not null)
        {
            return repeatedLazyDotStarLiteral.CountMatches(haystack, startAt);
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return repeatedLiteralRunOrEmpty.CountMatches(haystack, startAt);
        }

        if (delimitedSpan is not null)
        {
            return delimitedSpan.CountMatches(haystack, startAt);
        }

        if (delimitedCapture is not null)
        {
            return delimitedCapture.CountMatches(haystack, startAt);
        }

        if (structuredLogCapture is not null)
        {
            return structuredLogCapture.CountMatches(haystack, startAt);
        }

        if (fixedWidthAlternation is not null)
        {
            return fixedWidthAlternation.CountMatches(haystack, startAt);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.CountMatches(haystack, startAt);
        }

        if (leadingClassLiteral is not null)
        {
            return leadingClassLiteral.CountMatches(haystack, startAt);
        }

        if (lineBoundaryLiteral is not null)
        {
            return lineBoundaryLiteral.CountMatches(haystack, startAt);
        }

        if (unicodeLetterLiteralRun is not null)
        {
            return unicodeLetterLiteralRun.CountMatches(haystack, startAt);
        }

        if (wordBoundaryLiteralSet is not null)
        {
            return wordBoundaryLiteralSet.CountMatches(haystack, startAt);
        }

        if (wordSuffixLiteral is not null)
        {
            return wordSuffixLiteral.CountMatches(haystack, startAt);
        }

        if (delimitedRun is not null)
        {
            return delimitedRun.CountMatches(haystack, startAt);
        }

        if (endAnchoredAtom is not null)
        {
            return endAnchoredAtom.CountMatches(haystack, startAt);
        }

        if (lineContains is not null)
        {
            return lineContains.CountMatches(haystack, startAt);
        }

        if (dotStarClassFallback is not null)
        {
            return dotStarClassFallback.CountMatches(haystack, startAt);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.CountMatches(haystack, startAt);
        }

        if (TryCountNonOverlapping(haystack, startAt, sumSpans: false, out long count, out _))
        {
            return count;
        }

        if (endAnchoredSequence is not null)
        {
            return endAnchoredSequence.CountMatches(haystack, startAt);
        }

        if (TryIteratePrefilteredBoundedBacktracker(
            haystack,
            startAt,
            startPredicate,
            sumSpans: false,
            out long prefilteredBoundedCount))
        {
            return prefilteredBoundedCount;
        }

        bool hasRequiredStart = startPredicate?.HasRequiredStart == true;
        if (hasRequiredStart &&
            TryIterateRequiredStartBoundedBacktracker(
                haystack,
                startAt,
                startPredicate!,
                sumSpans: false,
                out long requiredStartBoundedCount))
        {
            return requiredStartBoundedCount;
        }

        if (hasRequiredStart &&
            TryIterateRequiredStartPikeVm(
                haystack,
                startAt,
                startPredicate!,
                sumSpans: false,
                out long requiredStartCount))
        {
            return requiredStartCount;
        }

        if (ShouldUsePrefilterBeforeUnanchoredDfa(haystack.Length))
        {
            return IterateNonOverlapping(haystack, startAt, startPredicate, sumSpans: false);
        }

        if (!hasRequiredStart &&
            TryCountAsciiFastUnanchored(haystack, startAt, out long asciiFastCount))
        {
            return asciiFastCount;
        }

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = hasRequiredStart
            ? null
            : RentUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null)
        {
            try
            {
                if (activeUnanchoredLazyDfa.TryCountMatches(haystack, startAt, out long unanchoredCount))
                {
                    return unanchoredCount;
                }
            }
            finally
            {
                ReturnUnanchoredLazyDfa(activeUnanchoredLazyDfa);
            }
        }

        if (!hasRequiredStart &&
            TryIterateAsciiFastUnanchored(
                haystack,
                startAt,
                startPredicate,
                sumSpans: false,
                out long iteratedAsciiFastCount))
        {
            return iteratedAsciiFastCount;
        }

        return IterateNonOverlapping(haystack, startAt, startPredicate, sumSpans: false);
    }

    /// <summary>
    /// Sums the byte lengths of non-overlapping leftmost matches at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="startPredicate">An optional conservative candidate-start predicate.</param>
    /// <returns>The sum of matched byte lengths.</returns>
    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate = null)
    {
        if (empty is not null)
        {
            return RegexEmptyEngine.SumMatchSpans(haystack, startAt);
        }

        if (literalSet is not null)
        {
            return literalSet.SumMatchSpans(haystack, startAt);
        }

        if (alternationSet is not null)
        {
            return alternationSet.SumMatchSpans(haystack, startAt);
        }

        if (wholeLine is not null)
        {
            return wholeLine.SumMatchSpans(haystack, startAt);
        }

        if (dotStar is not null)
        {
            return dotStar.SumMatchSpans(haystack, startAt);
        }

        if (ipv4Address is not null)
        {
            return RegexIpv4AddressEngine.SumMatchSpans(haystack, startAt);
        }

        if (emailAddress is not null)
        {
            return RegexEmailAddressEngine.SumMatchSpans(haystack, startAt);
        }

        if (lh3Email is not null)
        {
            return RegexLh3EmailEngine.SumMatchSpans(haystack, startAt);
        }

        if (uri is not null)
        {
            return RegexUriEngine.SumMatchSpans(haystack, startAt);
        }

        if (lh3Uri is not null)
        {
            return RegexLh3UriEngine.SumMatchSpans(haystack, startAt);
        }

        if (lh3UriOrEmail is not null)
        {
            return RegexLh3UriOrEmailEngine.SumMatchSpans(haystack, startAt);
        }

        if (boundedDigitDelimiter is not null)
        {
            return boundedDigitDelimiter.SumMatchSpans(haystack, startAt);
        }

        if (wordWhitespaceLiteral is not null)
        {
            return wordWhitespaceLiteral.SumMatchSpans(haystack, startAt);
        }

        if (unicodeWordWhitespaceLiteral is not null)
        {
            return unicodeWordWhitespaceLiteral.SumMatchSpans(haystack, startAt);
        }

        if (boundedLetterSuffixWhitespace is not null)
        {
            return boundedLetterSuffixWhitespace.SumMatchSpans(haystack, startAt);
        }

        if (runLiteralDotStar is not null)
        {
            return runLiteralDotStar.SumMatchSpans(haystack, startAt);
        }

        if (literalPrefixRun is not null)
        {
            return literalPrefixRun.SumMatchSpans(haystack, startAt);
        }

        if (boundedLiteralGap is not null)
        {
            return boundedLiteralGap.SumMatchSpans(haystack, startAt);
        }

        if (boundedLineLiteralGap is not null)
        {
            return boundedLineLiteralGap.SumMatchSpans(haystack, startAt);
        }

        if (anchoredLineLiteralGap is not null)
        {
            return anchoredLineLiteralGap.SumMatchSpans(haystack, startAt);
        }

        if (boundedPrefixLiteralSet is not null)
        {
            return boundedPrefixLiteralSet.SumMatchSpans(haystack, startAt);
        }

        if (unicodeGraphemeCluster is not null)
        {
            return RegexUnicodeGraphemeClusterEngine.SumMatchSpans(haystack, startAt);
        }

        if (boundedScalarClassSequence is not null)
        {
            return boundedScalarClassSequence.SumMatchSpans(haystack, startAt);
        }

        if (boundedByteClassSequence is not null)
        {
            return boundedByteClassSequence.SumMatchSpans(haystack, startAt);
        }

        if (repeatedLazyDotStarLiteral is not null)
        {
            return repeatedLazyDotStarLiteral.SumMatchSpans(haystack, startAt);
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return repeatedLiteralRunOrEmpty.SumMatchSpans(haystack, startAt);
        }

        if (delimitedSpan is not null)
        {
            return delimitedSpan.SumMatchSpans(haystack, startAt);
        }

        if (delimitedCapture is not null)
        {
            return delimitedCapture.SumMatchSpans(haystack, startAt);
        }

        if (structuredLogCapture is not null)
        {
            return structuredLogCapture.SumMatchSpans(haystack, startAt);
        }

        if (fixedWidthAlternation is not null)
        {
            return fixedWidthAlternation.SumMatchSpans(haystack, startAt);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.SumMatchSpans(haystack, startAt);
        }

        if (leadingClassLiteral is not null)
        {
            return leadingClassLiteral.SumMatchSpans(haystack, startAt);
        }

        if (lineBoundaryLiteral is not null)
        {
            return lineBoundaryLiteral.SumMatchSpans(haystack, startAt);
        }

        if (unicodeLetterLiteralRun is not null)
        {
            return unicodeLetterLiteralRun.SumMatchSpans(haystack, startAt);
        }

        if (wordBoundaryLiteralSet is not null)
        {
            return wordBoundaryLiteralSet.SumMatchSpans(haystack, startAt);
        }

        if (wordSuffixLiteral is not null)
        {
            return wordSuffixLiteral.SumMatchSpans(haystack, startAt);
        }

        if (delimitedRun is not null)
        {
            return delimitedRun.SumMatchSpans(haystack, startAt);
        }

        if (endAnchoredAtom is not null)
        {
            return endAnchoredAtom.SumMatchSpans(haystack, startAt);
        }

        if (lineContains is not null)
        {
            return lineContains.SumMatchSpans(haystack, startAt);
        }

        if (dotStarClassFallback is not null)
        {
            return dotStarClassFallback.SumMatchSpans(haystack, startAt);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.SumMatchSpans(haystack, startAt);
        }

        if (TryCountNonOverlapping(haystack, startAt, sumSpans: true, out _, out long spanSum))
        {
            return spanSum;
        }

        if (endAnchoredSequence is not null)
        {
            return endAnchoredSequence.SumMatchSpans(haystack, startAt);
        }

        if (TryIteratePrefilteredBoundedBacktracker(
            haystack,
            startAt,
            startPredicate,
            sumSpans: true,
            out long prefilteredBoundedSpanSum))
        {
            return prefilteredBoundedSpanSum;
        }

        bool hasRequiredStart = startPredicate?.HasRequiredStart == true;
        if (hasRequiredStart &&
            TryIterateRequiredStartBoundedBacktracker(
                haystack,
                startAt,
                startPredicate!,
                sumSpans: true,
                out long requiredStartBoundedSpanSum))
        {
            return requiredStartBoundedSpanSum;
        }

        if (hasRequiredStart &&
            TryIterateRequiredStartPikeVm(
                haystack,
                startAt,
                startPredicate!,
                sumSpans: true,
                out long requiredStartSpanSum))
        {
            return requiredStartSpanSum;
        }

        if (ShouldUsePrefilterBeforeUnanchoredDfa(haystack.Length))
        {
            return IterateNonOverlapping(haystack, startAt, startPredicate, sumSpans: true);
        }

        if (!hasRequiredStart &&
            TrySumAsciiFastUnanchored(haystack, startAt, out long asciiFastSpanSum))
        {
            return asciiFastSpanSum;
        }

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = hasRequiredStart
            ? null
            : RentUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null)
        {
            try
            {
                if (activeUnanchoredLazyDfa.TrySumMatchSpans(haystack, startAt, out long unanchoredSpanSum))
                {
                    return unanchoredSpanSum;
                }
            }
            finally
            {
                ReturnUnanchoredLazyDfa(activeUnanchoredLazyDfa);
            }
        }

        return IterateNonOverlapping(haystack, startAt, startPredicate, sumSpans: true);
    }

    private bool TryCountNonOverlapping(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        out long count,
        out long spanSum)
    {
        if (simpleSequence is not null)
        {
            return simpleSequence.TryCountNonOverlapping(haystack, startAt, sumSpans, out count, out spanSum);
        }

        if (scalarRun is not null)
        {
            return scalarRun.TryCountNonOverlapping(haystack, startAt, sumSpans, out count, out spanSum);
        }

        count = 0;
        spanSum = 0;
        return false;
    }

    private bool TryIteratePrefilteredBoundedBacktracker(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        bool sumSpans,
        out long total)
    {
        total = 0;
        if (boundedBacktrackerPool is null ||
            prefilter is null ||
            startPredicate is not null ||
            !ShouldUsePrefilterBeforeUnanchoredDfa(haystack.Length))
        {
            return false;
        }

        RegexBoundedBacktracker? boundedBacktracker = boundedBacktrackerPool.Rent();
        if (boundedBacktracker is null)
        {
            return false;
        }

        try
        {
            if (prefilter.UsesRequiredLiteralWindow)
            {
                Span<long> requiredRangeBuffer =
                    stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];
                var candidates = RegexCandidateStartEnumerator.RequiredLiteralRanges(
                    haystack,
                    startAt,
                    haystack.Length,
                    utf8,
                    prefilter,
                    requiredRangeBuffer);
                total = IteratePrefilteredBoundedCandidates(
                    haystack,
                    startAt,
                    sumSpans,
                    boundedBacktracker,
                    ref candidates);
            }
            else
            {
                var candidates = RegexCandidateStartEnumerator.ExactPrefix(
                    haystack,
                    startAt,
                    haystack.Length,
                    utf8,
                    prefilter);
                total = IteratePrefilteredBoundedCandidates(
                    haystack,
                    startAt,
                    sumSpans,
                    boundedBacktracker,
                    ref candidates);
            }

            return true;
        }
        finally
        {
            boundedBacktrackerPool.Return(boundedBacktracker);
        }
    }

    private bool TryIterateRequiredStartPikeVm(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate startPredicate,
        bool sumSpans,
        out long total)
    {
        total = 0;
        if (pikeVmPool is null || prefilter is not null)
        {
            return false;
        }

        PikeVm? pikeVm = pikeVmPool.Rent();
        if (pikeVm is null)
        {
            return false;
        }

        RegexLazyDfa? asciiFastDfa = asciiFastDfaPool?.Rent();

        try
        {
            var candidates = RegexCandidateStartEnumerator.Every(
                haystack,
                startAt,
                haystack.Length,
                utf8,
                startPredicate);
            total = IterateRequiredStartPikeVmCandidates(
                haystack,
                startAt,
                sumSpans,
                pikeVm,
                asciiFastDfa,
                ref candidates);
            return true;
        }
        finally
        {
            if (asciiFastDfa is not null)
            {
                asciiFastDfaPool!.Return(asciiFastDfa);
            }

            pikeVmPool.Return(pikeVm);
        }
    }

    private bool TryIterateRequiredStartBoundedBacktracker(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate startPredicate,
        bool sumSpans,
        out long total)
    {
        total = 0;
        if (boundedBacktrackerPool is null || prefilter is not null)
        {
            return false;
        }

        RegexBoundedBacktracker? boundedBacktracker = boundedBacktrackerPool.Rent();
        if (boundedBacktracker is null)
        {
            return false;
        }

        try
        {
            var candidates = RegexCandidateStartEnumerator.Every(
                haystack,
                startAt,
                haystack.Length,
                utf8,
                startPredicate);
            total = IterateRequiredStartBoundedCandidates(
                haystack,
                startAt,
                sumSpans,
                boundedBacktracker,
                ref candidates);
            return true;
        }
        finally
        {
            boundedBacktrackerPool.Return(boundedBacktracker);
        }
    }

    private static long IterateRequiredStartBoundedCandidates(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        RegexBoundedBacktracker boundedBacktracker,
        scoped ref RegexCandidateStartEnumerator candidates)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suppressedEmptyStart = -1;
        while (candidates.MoveNext(out int candidate))
        {
            if (candidate < offset ||
                !boundedBacktracker.TryMatchAt(haystack, candidate, out int length))
            {
                continue;
            }

            if (length == 0 && candidate == suppressedEmptyStart)
            {
                offset = Math.Min(candidate + 1, haystack.Length + 1);
                suppressedEmptyStart = -1;
                continue;
            }

            total += sumSpans ? length : 1;
            if (length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(candidate + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = Math.Min(candidate + length, haystack.Length + 1);
                offset = suppressedEmptyStart;
            }
        }

        return total;
    }

    private static long IterateRequiredStartPikeVmCandidates(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        PikeVm pikeVm,
        RegexLazyDfa? asciiFastDfa,
        scoped ref RegexCandidateStartEnumerator candidates)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suppressedEmptyStart = -1;
        while (candidates.MoveNext(out int candidate))
        {
            if (candidate < offset ||
                !TryMatchRequiredStartCandidate(
                    haystack,
                    candidate,
                    pikeVm,
                    asciiFastDfa,
                    out int length))
            {
                continue;
            }

            if (length == 0 && candidate == suppressedEmptyStart)
            {
                offset = Math.Min(candidate + 1, haystack.Length + 1);
                suppressedEmptyStart = -1;
                continue;
            }

            total += sumSpans ? length : 1;
            if (length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(candidate + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = Math.Min(candidate + length, haystack.Length + 1);
                offset = suppressedEmptyStart;
            }
        }

        return total;
    }

    private static bool TryMatchRequiredStartCandidate(
        ReadOnlySpan<byte> haystack,
        int start,
        PikeVm pikeVm,
        RegexLazyDfa? asciiFastDfa,
        out int length)
    {
        if (asciiFastDfa is not null)
        {
            if (asciiFastDfa.TryMatchAsciiAt(haystack, start, out int asciiLength, out bool aborted))
            {
                int asciiEnd = start + asciiLength;
                if (asciiLength > 0 &&
                    (asciiEnd >= haystack.Length || haystack[asciiEnd] <= 0x7F))
                {
                    length = asciiLength;
                    return true;
                }
            }
            else if (!aborted)
            {
                length = 0;
                return false;
            }
        }

        return pikeVm.TryMatchAt(haystack, start, out length);
    }

    private static long IteratePrefilteredBoundedCandidates(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        RegexBoundedBacktracker boundedBacktracker,
        scoped ref RegexCandidateStartEnumerator candidates)
    {
        long total = 0;
        int minimumStart = Math.Clamp(startAt, 0, haystack.Length);
        while (candidates.MoveNext(out int candidate))
        {
            if (candidate < minimumStart ||
                !boundedBacktracker.TryMatchAt(haystack, candidate, out int length))
            {
                continue;
            }

            total += sumSpans ? length : 1;
            minimumStart = length == 0
                ? Math.Min(candidate + 1, haystack.Length + 1)
                : Math.Min(candidate + length, haystack.Length + 1);
        }

        return total;
    }

    /// <summary>
    /// Attempts a complete full-span search with the ASCII-projected unanchored DFA.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="match">Receives the projected match.</param>
    /// <param name="completed">
    /// Receives whether the projected DFA produced an authoritative match or no-match result.
    /// </param>
    /// <returns><see langword="true" /> when an authoritative match is found.</returns>
    private bool TryFindAsciiFastUnanchored(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out RegexMatch match,
        out bool completed)
    {
        match = default;
        completed = false;
        RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa = RentAsciiFastUnanchoredDfa(haystack.Length);
        if (activeAsciiFastUnanchoredDfa is null)
        {
            return false;
        }

        try
        {
            bool found = activeAsciiFastUnanchoredDfa.TryFind(
                haystack,
                startAt,
                out match,
                out bool gaveUp);
            if (gaveUp)
            {
                return false;
            }

            if (!found)
            {
                completed = IsAsciiRange(haystack, startAt, haystack.Length);
                return false;
            }

            completed = CanAcceptAsciiFastUnanchored(haystack, startAt, match);
            return completed;
        }
        finally
        {
            ReturnAsciiFastUnanchoredDfa(activeAsciiFastUnanchoredDfa);
        }
    }

    /// <summary>
    /// Attempts to sum full match spans with the ASCII-projected unanchored DFA.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="spanSum">Receives the authoritative span sum.</param>
    /// <returns>
    /// <see langword="true" /> when the complete ASCII remainder is summed within budget.
    /// </returns>
    private bool TrySumAsciiFastUnanchored(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out long spanSum)
    {
        spanSum = 0;
        if (!IsAsciiRange(haystack, startAt, haystack.Length))
        {
            return false;
        }

        RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa =
            RentAsciiFastUnanchoredDfa(haystack.Length);
        if (activeAsciiFastUnanchoredDfa is null)
        {
            return false;
        }

        try
        {
            if (!activeAsciiFastUnanchoredDfa.TrySumMatchSpans(
                    haystack,
                    startAt,
                    out long candidateSpanSum))
            {
                return false;
            }

            spanSum = candidateSpanSum;
            return true;
        }
        finally
        {
            ReturnAsciiFastUnanchoredDfa(activeAsciiFastUnanchoredDfa);
        }
    }

    /// <summary>
    /// Attempts to count an entirely ASCII search window with the projected unanchored DFA.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="count">Receives the non-overlapping match count.</param>
    /// <returns><see langword="true" /> when the projected DFA completes authoritatively.</returns>
    private bool TryCountAsciiFastUnanchored(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out long count)
    {
        count = 0;
        if (!IsAsciiRange(haystack, start: 0, end: haystack.Length))
        {
            return false;
        }

        if (_asciiFastUnanchoredDenseDfa is not null)
        {
            System.Threading.Volatile.Write(ref _asciiFastUnanchoredDfaActivated, 1);
            count = _asciiFastUnanchoredDenseDfa.CountMatches(haystack, startAt);
            return true;
        }

        RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa =
            RentAsciiFastUnanchoredDfa(haystack.Length);
        if (activeAsciiFastUnanchoredDfa is null)
        {
            return false;
        }

        try
        {
            return activeAsciiFastUnanchoredDfa.TryCountMatches(
                haystack,
                startAt,
                out count);
        }
        finally
        {
            ReturnAsciiFastUnanchoredDfa(activeAsciiFastUnanchoredDfa);
        }
    }

    private bool TryIterateAsciiFastUnanchored(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        bool sumSpans,
        out long total)
    {
        total = 0;
        RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa = RentAsciiFastUnanchoredDfa(haystack.Length);
        if (activeAsciiFastUnanchoredDfa is null)
        {
            return false;
        }

        try
        {
            int offset = Math.Clamp(startAt, 0, haystack.Length);
            Dictionary<(int State, int Position), bool> forwardReachabilityCache = [];
            Dictionary<(int State, int Position), bool> reverseReachabilityCache = [];
            while (offset <= haystack.Length)
            {
                if (!activeAsciiFastUnanchoredDfa.TryFind(
                        haystack,
                        offset,
                        forwardReachabilityCache,
                        reverseReachabilityCache,
                        out RegexMatch match,
                        out bool gaveUp))
                {
                    if (!gaveUp && IsAsciiRange(haystack, offset, haystack.Length))
                    {
                        return true;
                    }

                    if (!TryFindByAnchoredScan(haystack, offset, startPredicate, out match))
                    {
                        return true;
                    }

                    total += sumSpans ? match.Length : 1;
                    offset = AdvanceAfterNonOverlappingMatch(match, haystack.Length);
                    continue;
                }

                if (!CanAcceptAsciiFastUnanchored(haystack, offset, match))
                {
                    if (!TryFindByAnchoredScan(haystack, offset, startPredicate, out match))
                    {
                        return true;
                    }
                }
                else
                {
                    total += sumSpans ? match.Length : 1;
                    offset = AdvanceAfterNonOverlappingMatch(match, haystack.Length);
                    continue;
                }

                total += sumSpans ? match.Length : 1;
                offset = AdvanceAfterNonOverlappingMatch(match, haystack.Length);
            }

            return true;
        }
        finally
        {
            ReturnAsciiFastUnanchoredDfa(activeAsciiFastUnanchoredDfa);
        }
    }

    private static int AdvanceAfterNonOverlappingMatch(RegexMatch match, int haystackLength)
    {
        return match.Length == 0
            ? Math.Min(match.End + 1, haystackLength + 1)
            : match.End;
    }

    private bool TryFindByAnchoredScan(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        RegexStartPredicate? startPredicate,
        out RegexMatch match)
    {
        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (startPredicate is not null && !startPredicate.CanStartAt(haystack, start))
            {
                continue;
            }

            if (TryMatchAt(haystack, start, out int length))
            {
                match = new RegexMatch(start, length);
                return true;
            }
        }

        match = default;
        return false;
    }

    private static bool CanAcceptAsciiFastUnanchored(ReadOnlySpan<byte> haystack, int searchStart, RegexMatch match)
    {
        return match.Length > 0 &&
            IsAsciiRange(haystack, searchStart, match.End) &&
            (match.End >= haystack.Length || haystack[match.End] <= 0x7F);
    }

    private static bool IsAsciiRange(ReadOnlySpan<byte> haystack, int start, int end)
    {
        int boundedStart = Math.Clamp(start, 0, haystack.Length);
        int boundedEnd = Math.Clamp(end, boundedStart, haystack.Length);
        return haystack[boundedStart..boundedEnd]
            .IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F) < 0;
    }

    private long IterateNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suppressedEmptyStart = -1;
        Dictionary<(int State, int Position), bool>? reachabilityCache = lazyDfaPool is not null && prefilter is null ? [] : null;
        while (offset <= haystack.Length)
        {
            RegexMatch? match = Find(
                haystack,
                offset,
                startPredicate,
                reachabilityCache,
                reusablePikeVm: null,
                reusableAnchoredDfa: null,
                reusableUnanchoredDfa: null,
                reusableUnanchoredDfaUsesAsciiProjection: false);
            if (!match.HasValue)
            {
                return total;
            }

            if (match.Value.Length == 0 && match.Value.Start == suppressedEmptyStart)
            {
                offset = Math.Min(match.Value.Start + 1, haystack.Length + 1);
                suppressedEmptyStart = -1;
                continue;
            }

            total += sumSpans ? match.Value.Length : 1;
            if (match.Value.Length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(match.Value.End + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = Math.Min(match.Value.End, haystack.Length + 1);
                offset = suppressedEmptyStart;
            }
        }

        return total;
    }

    internal RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (empty is not null)
        {
            return empty.MatchAt(haystack, startOffset);
        }

        if (literalSet is not null)
        {
            return literalSet.MatchAt(haystack, startOffset);
        }

        if (alternationSet is not null)
        {
            return alternationSet.MatchAt(haystack, startOffset);
        }

        if (wholeLine is not null)
        {
            return wholeLine.MatchAt(haystack, startOffset);
        }

        if (dotStar is not null)
        {
            return dotStar.MatchAt(haystack, startOffset);
        }

        if (ipv4Address is not null)
        {
            return RegexIpv4AddressEngine.MatchAt(haystack, startOffset);
        }

        if (emailAddress is not null)
        {
            return RegexEmailAddressEngine.MatchAt(haystack, startOffset);
        }

        if (lh3Email is not null)
        {
            return RegexLh3EmailEngine.MatchAt(haystack, startOffset);
        }

        if (uri is not null)
        {
            return RegexUriEngine.MatchAt(haystack, startOffset);
        }

        if (lh3Uri is not null)
        {
            return RegexLh3UriEngine.MatchAt(haystack, startOffset);
        }

        if (lh3UriOrEmail is not null)
        {
            return RegexLh3UriOrEmailEngine.MatchAt(haystack, startOffset);
        }

        if (boundedDigitDelimiter is not null)
        {
            return boundedDigitDelimiter.MatchAt(haystack, startOffset);
        }

        if (unicodeWordWhitespaceLiteral is not null)
        {
            return unicodeWordWhitespaceLiteral.MatchAt(haystack, startOffset);
        }

        if (runLiteralDotStar is not null)
        {
            return runLiteralDotStar.MatchAt(haystack, startOffset);
        }

        if (literalPrefixRun is not null)
        {
            return literalPrefixRun.MatchAt(haystack, startOffset);
        }

        if (boundedLiteralGap is not null)
        {
            return boundedLiteralGap.MatchAt(haystack, startOffset);
        }

        if (boundedLineLiteralGap is not null)
        {
            return boundedLineLiteralGap.MatchAt(haystack, startOffset);
        }

        if (anchoredLineLiteralGap is not null)
        {
            return anchoredLineLiteralGap.MatchAt(haystack, startOffset);
        }

        if (boundedPrefixLiteralSet is not null)
        {
            return boundedPrefixLiteralSet.MatchAt(haystack, startOffset);
        }

        if (unicodeGraphemeCluster is not null)
        {
            return RegexUnicodeGraphemeClusterEngine.MatchAt(haystack, startOffset);
        }

        if (boundedByteClassSequence is not null)
        {
            return boundedByteClassSequence.MatchAt(haystack, startOffset);
        }

        if (repeatedLazyDotStarLiteral is not null)
        {
            return repeatedLazyDotStarLiteral.MatchAt(haystack, startOffset);
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return repeatedLiteralRunOrEmpty.MatchAt(haystack, startOffset);
        }

        if (delimitedSpan is not null)
        {
            return delimitedSpan.MatchAt(haystack, startOffset);
        }

        if (delimitedCapture is not null)
        {
            return delimitedCapture.Find(haystack, startOffset);
        }

        if (structuredLogCapture is not null)
        {
            return structuredLogCapture.Find(haystack, startOffset);
        }

        if (fixedWidthAlternation is not null)
        {
            return fixedWidthAlternation.MatchAt(haystack, startOffset);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.MatchAt(haystack, startOffset);
        }

        if (leadingClassLiteral is not null)
        {
            return leadingClassLiteral.MatchAt(haystack, startOffset);
        }

        if (lineBoundaryLiteral is not null)
        {
            return lineBoundaryLiteral.MatchAt(haystack, startOffset);
        }

        if (unicodeLetterLiteralRun is not null)
        {
            return unicodeLetterLiteralRun.MatchAt(haystack, startOffset);
        }

        if (wordBoundaryLiteralSet is not null)
        {
            return wordBoundaryLiteralSet.MatchAt(haystack, startOffset);
        }

        if (wordSuffixLiteral is not null)
        {
            return wordSuffixLiteral.MatchAt(haystack, startOffset);
        }

        if (delimitedRun is not null)
        {
            return delimitedRun.MatchAt(haystack, startOffset);
        }

        if (endAnchoredAtom is not null)
        {
            return endAnchoredAtom.MatchAt(haystack, startOffset);
        }

        if (lineContains is not null)
        {
            return lineContains.MatchAt(haystack, startOffset);
        }

        if (dotStarClassFallback is not null)
        {
            return dotStarClassFallback.MatchAt(haystack, startOffset);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.MatchAt(haystack, startOffset);
        }

        return TryMatchAt(haystack, startOffset, out int length)
            ? new RegexMatch(startOffset, length)
            : null;
    }

    internal RegexCaptures? FindSyntheticCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        return alternationSet?.FindSyntheticCaptures(haystack, Math.Clamp(startAt, 0, haystack.Length));
    }

    private RegexNfa GetNfa()
    {
        if (nfa is not null)
        {
            return nfa;
        }

        if (nfaFactory is null)
        {
            throw new InvalidOperationException("The selected regex engine does not have a fallback NFA.");
        }

        lock (nfaInitializationLock)
        {
            return nfa ??= nfaFactory();
        }
    }

    private RegexUnanchoredLazyDfa? RentUnanchoredLazyDfa(int haystackLength)
    {
        if (System.Threading.Volatile.Read(ref _unanchoredLazyDfaAvailability) == RunnerUnavailable ||
            (haystackLength < UnanchoredLazyDfaHaystackThreshold &&
                System.Threading.Volatile.Read(ref _unanchoredLazyDfaActivated) == 0))
        {
            return null;
        }

        RegexRunnerPool<RegexUnanchoredLazyDfa>? pool =
            GetOrCreateUnanchoredLazyDfaPool();
        if (pool is null)
        {
            return null;
        }

        RegexUnanchoredLazyDfa? dfa = pool.Rent();
        if (dfa is null)
        {
            System.Threading.Volatile.Write(
                ref _unanchoredLazyDfaAvailability,
                RunnerUnavailable);
            return null;
        }

        System.Threading.Volatile.Write(ref _unanchoredLazyDfaAvailability, RunnerAvailable);
        System.Threading.Volatile.Write(ref _unanchoredLazyDfaActivated, 1);
        return dfa;
    }

    private bool HasPrimaryUnanchoredDfaRunner =>
        System.Threading.Volatile.Read(ref _unanchoredLazyDfaFactory) is not null ||
        System.Threading.Volatile.Read(ref _unanchoredLazyDfaPool) is not null;

    private RegexRunnerPool<RegexUnanchoredLazyDfa>? GetOrCreateUnanchoredLazyDfaPool()
    {
        Func<RegexUnanchoredLazyDfa?>? factory =
            System.Threading.Volatile.Read(ref _unanchoredLazyDfaFactory);
        if (factory is null)
        {
            return System.Threading.Volatile.Read(ref _unanchoredLazyDfaPool);
        }

        RegexRunnerPool<RegexUnanchoredLazyDfa>? pool =
            System.Threading.Volatile.Read(ref _unanchoredLazyDfaPool);
        if (pool is not null)
        {
            return pool;
        }

        lock (nfaInitializationLock)
        {
            pool = _unanchoredLazyDfaPool;
            factory = _unanchoredLazyDfaFactory;
            if (pool is null && factory is not null)
            {
                pool = new RegexRunnerPool<RegexUnanchoredLazyDfa>(factory);
                System.Threading.Volatile.Write(ref _unanchoredLazyDfaPool, pool);
                System.Threading.Volatile.Write(ref _unanchoredLazyDfaFactory, null);
            }

            return pool;
        }
    }

    private void ReturnUnanchoredLazyDfa(RegexUnanchoredLazyDfa dfa)
    {
        System.Threading.Volatile.Read(ref _unanchoredLazyDfaPool)?.Return(dfa);
    }

    private RegexUnanchoredLazyDfa? RentAsciiFastUnanchoredDfa(int haystackLength)
    {
        if (System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaAvailability) == RunnerUnavailable ||
            (haystackLength < UnanchoredLazyDfaHaystackThreshold &&
                System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaActivated) == 0))
        {
            return null;
        }

        RegexRunnerPool<RegexUnanchoredLazyDfa>? pool =
            GetOrCreateAsciiFastUnanchoredDfaPool();
        if (pool is null)
        {
            return null;
        }

        RegexUnanchoredLazyDfa? dfa = pool.Rent();
        if (dfa is null)
        {
            System.Threading.Volatile.Write(
                ref _asciiFastUnanchoredDfaAvailability,
                RunnerUnavailable);
            return null;
        }

        System.Threading.Volatile.Write(ref _asciiFastUnanchoredDfaAvailability, RunnerAvailable);
        System.Threading.Volatile.Write(ref _asciiFastUnanchoredDfaActivated, 1);
        return dfa;
    }

    private bool HasAsciiFastUnanchoredDfaRunner =>
        System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaFactory) is not null ||
        System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaPool) is not null;

    private RegexRunnerPool<RegexUnanchoredLazyDfa>? GetOrCreateAsciiFastUnanchoredDfaPool()
    {
        Func<RegexUnanchoredLazyDfa?>? factory =
            System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaFactory);
        if (factory is null)
        {
            return System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaPool);
        }

        RegexRunnerPool<RegexUnanchoredLazyDfa>? pool =
            System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaPool);
        if (pool is not null)
        {
            return pool;
        }

        lock (nfaInitializationLock)
        {
            pool = _asciiFastUnanchoredDfaPool;
            factory = _asciiFastUnanchoredDfaFactory;
            if (pool is null && factory is not null)
            {
                pool = new RegexRunnerPool<RegexUnanchoredLazyDfa>(factory);
                System.Threading.Volatile.Write(ref _asciiFastUnanchoredDfaPool, pool);
                System.Threading.Volatile.Write(ref _asciiFastUnanchoredDfaFactory, null);
            }

            return pool;
        }
    }

    private void ReturnAsciiFastUnanchoredDfa(RegexUnanchoredLazyDfa dfa)
    {
        System.Threading.Volatile.Read(ref _asciiFastUnanchoredDfaPool)?.Return(dfa);
    }

    private RegexLazyDfa? RentAnchoredLeftmostDfa()
    {
        if (anchoredLeftmostDfaPool is null)
        {
            return null;
        }

        return anchoredLeftmostDfaPool.Rent();
    }

    /// <summary>
    /// Finds the earliest match end among candidates at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The earliest-ending match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? FindEarliest(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (empty is not null)
        {
            return empty.FindEarliest(haystack, startOffset);
        }

        if (literalSet is not null)
        {
            return literalSet.FindEarliest(haystack, startOffset);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.Find(haystack, startOffset);
        }

        if (lineBoundaryLiteral is not null)
        {
            return lineBoundaryLiteral.Find(haystack, startOffset);
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return RegexRepeatedLiteralRunOrEmptyEngine.FindEarliest(haystack, startOffset);
        }

        if (boundedScalarClassSequence is not null)
        {
            return boundedScalarClassSequence.Find(haystack, startOffset);
        }

        RegexNfa activeNfa = GetNfa();
        var earliestPikeVm = new PikeVm(activeNfa);
        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (activeNfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
            {
                continue;
            }

            if (earliestPikeVm.TryMatchEarliestAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    internal RegexMatch? FindAllKindAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (empty is not null)
        {
            return empty.FindAllKindAt(haystack, startOffset);
        }

        if (literalSet is not null)
        {
            return literalSet.FindAllKindAt(haystack, startOffset);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.MatchAt(haystack, startOffset);
        }

        if (lineBoundaryLiteral is not null)
        {
            return lineBoundaryLiteral.MatchAt(haystack, startOffset);
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return repeatedLiteralRunOrEmpty.FindAllKindAt(haystack, startOffset);
        }

        if (boundedScalarClassSequence is not null)
        {
            return boundedScalarClassSequence.MatchAt(haystack, startOffset);
        }

        RegexNfa activeNfa = GetNfa();
        if (activeNfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, startOffset))
        {
            return null;
        }

        var allPikeVm = new PikeVm(activeNfa);
        return allPikeVm.TryMatchLongestAt(haystack, startOffset, out int length)
            ? new RegexMatch(startOffset, length)
            : null;
    }

    internal IReadOnlyList<RegexMatch> FindOverlappingAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (empty is not null)
        {
            return empty.FindOverlappingAt(haystack, startOffset);
        }

        if (literalSet is not null)
        {
            return literalSet.FindOverlappingAt(haystack, startOffset);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            RegexMatch? match = fixedWordWhitespaceSequence.MatchAt(haystack, startOffset);
            return match.HasValue ? [match.Value] : [];
        }

        if (repeatedLiteralRunOrEmpty is not null)
        {
            return repeatedLiteralRunOrEmpty.FindOverlappingAt(haystack, startOffset);
        }

        if (boundedScalarClassSequence is not null)
        {
            RegexMatch? match = boundedScalarClassSequence.MatchAt(haystack, startOffset);
            return match.HasValue ? [match.Value] : [];
        }

        RegexNfa activeNfa = GetNfa();
        if (activeNfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, startOffset))
        {
            return [];
        }

        var lengths = new List<int>();
        var overlappingPikeVm = new PikeVm(activeNfa);
        overlappingPikeVm.AddMatchLengthsAt(haystack, startOffset, lengths);
        var matches = new RegexMatch[lengths.Count];
        for (int index = 0; index < lengths.Count; index++)
        {
            matches[index] = new RegexMatch(startOffset, lengths[index]);
        }

        return matches;
    }

    private bool TryMatchAt(
        ReadOnlySpan<byte> haystack,
        int start,
        out int length,
        Dictionary<(int State, int Position), bool>? reachabilityCache = null)
    {
        if (empty is not null)
        {
            RegexMatch? match = empty.MatchAt(haystack, start);
            length = 0;
            return match.HasValue;
        }

        if (utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
        {
            length = 0;
            return false;
        }

        if (simpleSequence is not null)
        {
            return simpleSequence.TryMatchAt(haystack, start, out length);
        }

        if (fixedWordWhitespaceSequence is not null)
        {
            return fixedWordWhitespaceSequence.TryMatchAt(haystack, start, out length);
        }

        if (wordWhitespaceLiteral is not null)
        {
            return wordWhitespaceLiteral.TryMatchAt(haystack, start, out length);
        }

        if (lineBoundaryLiteral is not null)
        {
            return lineBoundaryLiteral.TryMatchAt(haystack, start, out length);
        }

        if (unicodeWordWhitespaceLiteral is not null)
        {
            return unicodeWordWhitespaceLiteral.TryMatchAt(haystack, start, out length);
        }

        if (boundedLetterSuffixWhitespace is not null)
        {
            return boundedLetterSuffixWhitespace.TryMatchAt(haystack, start, out length);
        }

        if (runLiteralDotStar is not null)
        {
            return runLiteralDotStar.TryMatchAt(haystack, start, out length);
        }

        if (unicodeLetterLiteralRun is not null)
        {
            return unicodeLetterLiteralRun.TryMatchAt(haystack, start, out length);
        }

        if (wordBoundaryLiteralSet is not null)
        {
            RegexMatch? match = wordBoundaryLiteralSet.MatchAt(haystack, start);
            length = match?.Length ?? 0;
            return match.HasValue;
        }

        if (wordSuffixLiteral is not null)
        {
            return wordSuffixLiteral.TryMatchAt(haystack, start, out length);
        }

        if (boundedPrefixLiteralSet is not null)
        {
            return boundedPrefixLiteralSet.TryMatchAt(haystack, start, out length);
        }

        if (unicodeGraphemeCluster is not null)
        {
            return RegexUnicodeGraphemeClusterEngine.TryMatchAt(haystack, start, out length);
        }

        if (boundedScalarClassSequence is not null)
        {
            return boundedScalarClassSequence.TryMatchAt(haystack, start, out length);
        }

        if (endAnchoredSequence is not null)
        {
            return endAnchoredSequence.TryMatchAt(haystack, start, out length);
        }

        if (delimitedRun is not null)
        {
            return delimitedRun.TryMatchAt(haystack, start, out length);
        }

        if (delimitedCapture is not null)
        {
            return delimitedCapture.TryMatchAt(haystack, start, out length);
        }

        if (structuredLogCapture is not null)
        {
            return structuredLogCapture.TryMatchAt(haystack, start, out length);
        }

        if (scalarRun is not null)
        {
            return scalarRun.TryMatchAt(haystack, start, out length);
        }

        if (endAnchoredAtom is not null)
        {
            return endAnchoredAtom.TryMatchAt(haystack, start, out length);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.TryMatchAt(haystack, start, out length);
        }

        if (lazyDfaPool is not null)
        {
            RegexLazyDfa? lazyDfa = lazyDfaPool.Rent();
            if (lazyDfa is not null)
            {
                try
                {
                    if (ShouldUseAnchoredLeftmostDfa(haystack.Length, reachabilityCache) &&
                        TryMatchWithAnchoredLeftmostDfa(haystack, start, out bool anchoredMatched, out length, out bool anchoredHandled))
                    {
                        if (anchoredHandled)
                        {
                            return anchoredMatched;
                        }
                    }

                    return lazyDfa.TryMatchAt(haystack, start, reachabilityCache, out length);
                }
                finally
                {
                    lazyDfaPool.Return(lazyDfa);
                }
            }
        }

        if (sparseDfa is not null)
        {
            return sparseDfa.TryMatchAt(haystack, start, out length);
        }

        if (denseDfa is not null)
        {
            return denseDfa.TryMatchAt(haystack, start, out length);
        }

        if (onePassDfaPool is not null)
        {
            RegexOnePassDfa? onePassDfa = onePassDfaPool.Rent();
            if (onePassDfa is not null)
            {
                try
                {
                    return onePassDfa.TryMatchAt(haystack, start, out length);
                }
                finally
                {
                    onePassDfaPool.Return(onePassDfa);
                }
            }
        }

        if (boundedBacktrackerPool is not null)
        {
            RegexBoundedBacktracker? boundedBacktracker = boundedBacktrackerPool.Rent();
            if (boundedBacktracker is not null)
            {
                try
                {
                    return boundedBacktracker.TryMatchAt(haystack, start, out length);
                }
                finally
                {
                    boundedBacktrackerPool.Return(boundedBacktracker);
                }
            }
        }

        if (asciiFastDfaPool is not null)
        {
            RegexLazyDfa? asciiFastDfa = asciiFastDfaPool.Rent();
            if (asciiFastDfa is not null)
            {
                try
                {
                    if (asciiFastDfa.TryMatchAsciiAt(haystack, start, out int asciiLength, out bool aborted))
                    {
                        int asciiEnd = start + asciiLength;
                        if (asciiLength > 0 && (asciiEnd >= haystack.Length || haystack[asciiEnd] <= 0x7F))
                        {
                            length = asciiLength;
                            return true;
                        }

                        return TryPikeVmMatchAt(haystack, start, out length);
                    }

                    if (!aborted)
                    {
                        length = 0;
                        return false;
                    }
                }
                finally
                {
                    asciiFastDfaPool.Return(asciiFastDfa);
                }
            }
        }

        return TryPikeVmMatchAt(haystack, start, out length);
    }

    private bool TryMatchWithAnchoredLeftmostDfa(
        ReadOnlySpan<byte> haystack,
        int start,
        out bool matched,
        out int length,
        out bool handled)
    {
        matched = false;
        length = 0;
        handled = false;
        RegexLazyDfa? anchoredLeftmost = RentAnchoredLeftmostDfa();
        if (anchoredLeftmost is null)
        {
            return false;
        }

        try
        {
            if (anchoredLeftmost.TryFindEnd(haystack, start, out int end, out bool gaveUp))
            {
                if (!gaveUp)
                {
                    matched = true;
                    length = end - start;
                    handled = true;
                    return true;
                }
            }
            else if (!gaveUp)
            {
                matched = false;
                length = 0;
                handled = true;
                return true;
            }

            return false;
        }
        finally
        {
            anchoredLeftmostDfaPool!.Return(anchoredLeftmost);
        }
    }

    private bool TryPikeVmMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        if (pikeVmPool is null)
        {
            var pikeVm = new PikeVm(GetNfa());
            return pikeVm.TryMatchAt(haystack, start, out length);
        }

        PikeVm? pooledPikeVm = pikeVmPool.Rent();
        if (pooledPikeVm is null)
        {
            var pikeVm = new PikeVm(GetNfa());
            return pikeVm.TryMatchAt(haystack, start, out length);
        }

        try
        {
            return pooledPikeVm.TryMatchAt(haystack, start, out length);
        }
        finally
        {
            pikeVmPool.Return(pooledPikeVm);
        }
    }

    private static bool ShouldUseAnchoredLeftmostDfa(
        int haystackLength,
        Dictionary<(int State, int Position), bool>? reachabilityCache)
    {
        return reachabilityCache is null || haystackLength < AnchoredLeftmostDfaHaystackThreshold;
    }
}
