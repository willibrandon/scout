namespace Scout;

internal sealed class RegexMetaEngine
{
    private const int DenseDfaNfaStateLimit = 8;
    private const int SparseDfaNfaStateLimit = 32;
    private const int DenseDfaStateLimit = 16;
    private const int SparseDfaStateLimit = 64;
    private const int OnePassDfaNfaStateLimit = 48;
    private const int BoundedBacktrackerNfaStateLimit = 24;
    private const int UnanchoredLazyDfaHaystackThreshold = 4096;
    private const int AnchoredLeftmostDfaHaystackThreshold = 4096;
    private const ulong DefaultDfaSizeLimit = 16UL * 1024UL * 1024UL;

    private readonly PikeVm? pikeVm;
    private readonly RegexBoundedBacktracker? boundedBacktracker;
    private readonly RegexOnePassDfa? onePassDfa;
    private readonly RegexDenseDfa? denseDfa;
    private readonly RegexSparseDfa? sparseDfa;
    private readonly RegexLazyDfa? lazyDfa;
    private readonly Func<RegexLazyDfa?>? anchoredLeftmostDfaFactory;
    private readonly RegexLazyDfa? asciiFastDfa;
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
    private readonly RegexLh3DateEngine? lh3Date;
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
    private readonly Func<RegexUnanchoredLazyDfa?>? unanchoredLazyDfaFactory;
    private readonly Func<RegexUnanchoredLazyDfa?>? asciiFastUnanchoredDfaFactory;
    private readonly object unanchoredDfaInitializationLock = new();
    private readonly object anchoredLeftmostDfaInitializationLock = new();
    private RegexNfa? nfa;
    private RegexUnanchoredLazyDfa? unanchoredLazyDfa;
    private RegexUnanchoredLazyDfa? asciiFastUnanchoredDfa;
    private RegexLazyDfa? anchoredLeftmostDfa;
    private readonly bool utf8;

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
        RegexLh3DateEngine? lh3Date = null,
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
        RegexStructuredLogCaptureEngine? structuredLogCapture = null)
    {
        Kind = kind;
        this.nfa = nfa;
        this.nfaFactory = nfaFactory;
        this.pikeVm = pikeVm;
        this.boundedBacktracker = boundedBacktracker;
        this.onePassDfa = onePassDfa;
        this.denseDfa = denseDfa;
        this.sparseDfa = sparseDfa;
        this.lazyDfa = lazyDfa;
        this.asciiFastDfa = asciiFastDfa;
        this.empty = empty;
        this.anchoredLeftmostDfaFactory = anchoredLeftmostDfaFactory;
        this.asciiFastUnanchoredDfaFactory = asciiFastUnanchoredDfaFactory;
        this.unanchoredLazyDfaFactory = unanchoredLazyDfaFactory;
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
        this.lh3Date = lh3Date;
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
    }

    public RegexEngineKind Kind { get; }

    public RegexPrefilterKind PrefilterKind => prefilter?.Kind ?? RegexPrefilterKind.None;

    public int RequiredLiteralWindow => prefilter?.RequiredLiteralWindow ?? 0;

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

    public static RegexMetaEngine Compile(RegexNfa nfa)
    {
        return Compile(nfa, prefilter: null, dfaSizeLimit: null);
    }

    public static RegexMetaEngine Compile(RegexNfa nfa, RegexPrefilter? prefilter)
    {
        return Compile(nfa, prefilter, dfaSizeLimit: null);
    }

    public static RegexMetaEngine Compile(RegexNfa nfa, RegexPrefilter? prefilter, ulong? dfaSizeLimit)
    {
        return Compile(nfa, prefilter, dfaSizeLimit, literalSet: null, alternationSet: null, simpleSequence: null);
    }

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
        RegexLh3DateEngine? lh3Date = null,
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
        RegexCompileOptions? options = null)
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

        if (lh3Date is not null)
        {
            return new RegexMetaEngine(
                RegexEngineKind.Date,
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
                lh3Date: lh3Date);
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
                    nfa.Utf8);
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
                    nfa.Utf8);
            }

            RegexNfa? asciiFastNfa = TryCompileAsciiFastNfa(asciiFastPattern, root, options);
            RegexLazyDfa? asciiFastDfa = TryCreateAsciiFastDfa(asciiFastNfa, effectiveDfaSizeLimit);
            Func<RegexUnanchoredLazyDfa?>? asciiFastUnanchoredDfaFactory = CreateAsciiFastUnanchoredDfaFactory(
                asciiFastNfa,
                root,
                options,
                effectiveDfaSizeLimit);
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
                asciiFastUnanchoredDfaFactory: asciiFastUnanchoredDfaFactory);
        }

        if (nfa.States.Count <= DenseDfaNfaStateLimit &&
            RegexDenseDfa.TryCompile(nfa, DenseDfaStateLimit, effectiveDfaSizeLimit, out RegexDenseDfa? denseDfa))
        {
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
                nfa.Utf8);
        }

        if (nfa.States.Count <= SparseDfaNfaStateLimit &&
            RegexSparseDfa.TryCompile(nfa, SparseDfaStateLimit, effectiveDfaSizeLimit, out RegexSparseDfa? sparseDfa))
        {
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
                nfa.Utf8);
        }

        if (!RegexLazyDfa.TryCreate(nfa, effectiveDfaSizeLimit, out RegexLazyDfa? lazyDfa))
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

        Func<RegexUnanchoredLazyDfa?>? unanchoredLazyDfaFactory = CreateUnanchoredLazyDfaFactory(
            root,
            options,
            prefilter,
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
            unanchoredLazyDfaFactory: unanchoredLazyDfaFactory,
            anchoredLeftmostDfaFactory: anchoredLeftmostDfaFactory);
    }

    private static Func<RegexUnanchoredLazyDfa?>? CreateUnanchoredLazyDfaFactory(
        RegexSyntaxNode? root,
        RegexCompileOptions? options,
        RegexPrefilter? prefilter,
        ulong dfaSizeLimit)
    {
        if (root is null ||
            !options.HasValue ||
            prefilter is not null)
        {
            return null;
        }

        RegexCompileOptions capturedOptions = options.Value;
        return () => RegexUnanchoredLazyDfa.TryCreate(root, capturedOptions, dfaSizeLimit, out RegexUnanchoredLazyDfa? dfa)
            ? dfa
            : null;
    }

    private static Func<RegexUnanchoredLazyDfa?>? CreateAsciiFastUnanchoredDfaFactory(
        RegexNfa? asciiFastNfa,
        RegexSyntaxNode? root,
        RegexCompileOptions? options,
        ulong dfaSizeLimit)
    {
        if (asciiFastNfa is null ||
            root is null ||
            !options.HasValue)
        {
            return null;
        }

        RegexCompileOptions source = options.Value;
        var asciiOptions = new RegexCompileOptions(
            source.CaseInsensitive,
            source.SwapGreed,
            source.MultiLine,
            source.DotMatchesNewline,
            source.Crlf,
            source.LineTerminator,
            utf8: false,
            unicodeClasses: false);
        return () => RegexUnanchoredLazyDfa.TryCreate(root, asciiOptions, dfaSizeLimit, out RegexUnanchoredLazyDfa? dfa)
            ? dfa
            : null;
    }

    private static RegexLazyDfa? TryCreateAsciiFastDfa(RegexNfa? asciiFastNfa, ulong dfaSizeLimit)
    {
        return asciiFastNfa is not null && RegexLazyDfa.TryCreate(asciiFastNfa, dfaSizeLimit, out RegexLazyDfa? asciiFastDfa)
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

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate = null)
    {
        return Find(haystack, startAt, startPredicate, reachabilityCache: null);
    }

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

        if (lh3Date is not null)
        {
            return RegexLh3DateEngine.IsMatch(haystack);
        }

        if (anchoredLineLiteralGap is not null)
        {
            return anchoredLineLiteralGap.IsMatch(haystack);
        }

        return Find(haystack, startAt: 0, startPredicate).HasValue;
    }

    private RegexMatch? Find(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        Dictionary<(int State, int Position), bool>? reachabilityCache)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
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

        if (lh3Date is not null)
        {
            return RegexLh3DateEngine.Find(haystack, startOffset);
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

        if (delimitedSpan is not null)
        {
            return delimitedSpan.Find(haystack, startOffset);
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
            return fixedWidthAlternation.Find(haystack, startOffset);
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

        if (ShouldUseRequiredLiteralPrefilterBeforeUnanchoredDfa(haystack.Length))
        {
            return FindWithRequiredLiteralPrefilter(haystack, startOffset, reachabilityCache);
        }

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = GetUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null &&
            activeUnanchoredLazyDfa.TryFind(haystack, startOffset, out RegexMatch unanchoredMatch, out bool gaveUp) &&
            !gaveUp)
        {
            return unanchoredMatch;
        }

        if (TryFindAsciiFastUnanchored(haystack, startOffset, out RegexMatch asciiFastMatch))
        {
            return asciiFastMatch;
        }

        if (prefilter?.UsesRequiredLiteralWindow == true)
        {
            return FindWithRequiredLiteralPrefilter(haystack, startOffset, reachabilityCache);
        }

        if (prefilter is not null)
        {
            for (int start = prefilter.FindCandidate(haystack, startOffset);
                 start >= 0;
                 start = prefilter.FindCandidate(haystack, start + 1))
            {
                if (TryMatchAt(haystack, start, out int length, reachabilityCache))
                {
                    return new RegexMatch(start, length);
                }
            }

            return null;
        }

        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (startPredicate is not null && !startPredicate.CanStartAt(haystack, start))
            {
                continue;
            }

            if (TryMatchAt(haystack, start, out int length, reachabilityCache))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    private bool ShouldUseRequiredLiteralPrefilterBeforeUnanchoredDfa(int haystackLength)
    {
        return prefilter?.UsesRequiredLiteralWindow == true &&
            (haystackLength < UnanchoredLazyDfaHaystackThreshold ||
            unanchoredLazyDfaFactory is null && asciiFastUnanchoredDfaFactory is null);
    }

    private RegexMatch? FindWithRequiredLiteralPrefilter(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        Dictionary<(int State, int Position), bool>? reachabilityCache)
    {
        int nextStartToTry = startOffset;
        for (int requiredAt = prefilter!.FindRequiredLiteral(haystack, startOffset);
             requiredAt >= 0;
             requiredAt = prefilter.FindRequiredLiteral(haystack, requiredAt + 1))
        {
            int firstStart = Math.Max(startOffset, requiredAt - prefilter.RequiredLiteralWindow);
            firstStart = Math.Max(firstStart, nextStartToTry);
            for (int start = firstStart; start <= requiredAt; start++)
            {
                if (prefilter.CanStartAt(haystack, start) &&
                    TryMatchAt(haystack, start, out int length, reachabilityCache))
                {
                    return new RegexMatch(start, length);
                }
            }

            nextStartToTry = Math.Max(nextStartToTry, requiredAt + 1);
        }

        return null;
    }

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

        if (lh3Date is not null)
        {
            return RegexLh3DateEngine.CountMatches(haystack, startAt);
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

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = GetUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null &&
            activeUnanchoredLazyDfa.TryCountMatches(haystack, startAt, out long unanchoredCount))
        {
            return unanchoredCount;
        }

        if (TryIterateAsciiFastUnanchored(haystack, startAt, startPredicate, sumSpans: false, out long asciiFastCount))
        {
            return asciiFastCount;
        }

        return IterateNonOverlapping(haystack, startAt, startPredicate, sumSpans: false);
    }

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

        if (lh3Date is not null)
        {
            return RegexLh3DateEngine.SumMatchSpans(haystack, startAt);
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

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = GetUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null &&
            activeUnanchoredLazyDfa.TrySumMatchSpans(haystack, startAt, out long unanchoredSpanSum))
        {
            return unanchoredSpanSum;
        }

        if (TryIterateAsciiFastUnanchored(haystack, startAt, startPredicate, sumSpans: true, out long asciiFastSpanSum))
        {
            return asciiFastSpanSum;
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

    private bool TryFindAsciiFastUnanchored(ReadOnlySpan<byte> haystack, int startAt, out RegexMatch match)
    {
        match = default;
        RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa = GetAsciiFastUnanchoredDfa(haystack.Length);
        if (activeAsciiFastUnanchoredDfa is null ||
            !activeAsciiFastUnanchoredDfa.TryFind(haystack, startAt, out match, out bool gaveUp) ||
            gaveUp)
        {
            return false;
        }

        return CanAcceptAsciiFastUnanchored(haystack, startAt, match);
    }

    private bool TryIterateAsciiFastUnanchored(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        bool sumSpans,
        out long total)
    {
        total = 0;
        RegexUnanchoredLazyDfa? activeAsciiFastUnanchoredDfa = GetAsciiFastUnanchoredDfa(haystack.Length);
        if (activeAsciiFastUnanchoredDfa is null)
        {
            return false;
        }

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
        for (int index = start; index < end; index++)
        {
            if (haystack[index] > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private long IterateNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suppressedEmptyStart = -1;
        Dictionary<(int State, int Position), bool>? reachabilityCache = lazyDfa is not null ? [] : null;
        while (offset <= haystack.Length)
        {
            RegexMatch? match = Find(haystack, offset, startPredicate, reachabilityCache);
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

        if (lh3Date is not null)
        {
            return RegexLh3DateEngine.MatchAt(haystack, startOffset);
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

    private RegexUnanchoredLazyDfa? GetUnanchoredLazyDfa(int haystackLength)
    {
        if (haystackLength < UnanchoredLazyDfaHaystackThreshold ||
            unanchoredLazyDfaFactory is null)
        {
            return null;
        }

        if (unanchoredLazyDfa is not null)
        {
            return unanchoredLazyDfa;
        }

        lock (unanchoredDfaInitializationLock)
        {
            return unanchoredLazyDfa ??= unanchoredLazyDfaFactory();
        }
    }

    private RegexUnanchoredLazyDfa? GetAsciiFastUnanchoredDfa(int haystackLength)
    {
        if (haystackLength < UnanchoredLazyDfaHaystackThreshold ||
            asciiFastUnanchoredDfaFactory is null)
        {
            return null;
        }

        if (asciiFastUnanchoredDfa is not null)
        {
            return asciiFastUnanchoredDfa;
        }

        lock (unanchoredDfaInitializationLock)
        {
            return asciiFastUnanchoredDfa ??= asciiFastUnanchoredDfaFactory();
        }
    }

    private RegexLazyDfa? GetAnchoredLeftmostDfa()
    {
        if (anchoredLeftmostDfaFactory is null)
        {
            return null;
        }

        if (anchoredLeftmostDfa is not null)
        {
            return anchoredLeftmostDfa;
        }

        lock (anchoredLeftmostDfaInitializationLock)
        {
            return anchoredLeftmostDfa ??= anchoredLeftmostDfaFactory();
        }
    }

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

        if (lazyDfa is not null)
        {
            if (ShouldUseAnchoredLeftmostDfa(haystack.Length, reachabilityCache) &&
                GetAnchoredLeftmostDfa() is RegexLazyDfa anchoredLeftmost)
            {
                if (anchoredLeftmost.TryFindEnd(haystack, start, out int end, out bool gaveUp))
                {
                    if (!gaveUp)
                    {
                        length = end - start;
                        return true;
                    }
                }
                else if (!gaveUp)
                {
                    length = 0;
                    return false;
                }
            }

            return lazyDfa.TryMatchAt(haystack, start, reachabilityCache, out length);
        }

        if (sparseDfa is not null)
        {
            return sparseDfa.TryMatchAt(haystack, start, out length);
        }

        if (denseDfa is not null)
        {
            return denseDfa.TryMatchAt(haystack, start, out length);
        }

        if (onePassDfa is not null)
        {
            return onePassDfa.TryMatchAt(haystack, start, out length);
        }

        if (boundedBacktracker is not null)
        {
            return boundedBacktracker.TryMatchAt(haystack, start, out length);
        }

        if (asciiFastDfa is not null)
        {
            if (asciiFastDfa.TryMatchAsciiAt(haystack, start, out int asciiLength, out bool aborted))
            {
                int asciiEnd = start + asciiLength;
                if (asciiLength > 0 && (asciiEnd >= haystack.Length || haystack[asciiEnd] <= 0x7F))
                {
                    length = asciiLength;
                    return true;
                }

                return pikeVm!.TryMatchAt(haystack, start, out length);
            }

            if (!aborted)
            {
                length = 0;
                return false;
            }
        }

        return pikeVm!.TryMatchAt(haystack, start, out length);
    }

    private static bool ShouldUseAnchoredLeftmostDfa(
        int haystackLength,
        Dictionary<(int State, int Position), bool>? reachabilityCache)
    {
        return reachabilityCache is null || haystackLength < AnchoredLeftmostDfaHaystackThreshold;
    }
}
