namespace Scout;

/// <summary>
/// Executes a byte-oriented regular expression automaton. Instances are safe to share across threads for matching.
/// </summary>
public sealed class RegexAutomaton
{
    private readonly RegexMetaEngine engine;
    private readonly RegexStartPredicate? startPredicate;
    private readonly RegexLengthGuard? lengthGuard;
    private readonly RegexRequiredByteSetGuard? requiredByteSetGuard;
    private readonly RegexRequiredLiteralAnySetGuard? requiredLiteralAnySetGuard;
    private readonly bool hasSearchGuards;
    private readonly object captureInitializationLock = new();
    private readonly ReadOnlyMemory<byte> capturePattern;
    private readonly RegexSyntaxNode? captureRoot;
    private readonly RegexCompileOptions captureOptions;
    private readonly RegexPrefilter? capturePrefilter;
    private readonly int captureCount;
    private readonly int wholePatternCaptureIndex;

    private RegexRunnerPool<RegexCaptureEngine>? captureEnginePool;
    private RegexAlternationSetEngine? syntheticCaptureAlternationSet;
    private RegexDelimitedCaptureEngine? delimitedCaptureEngine;
    private RegexStructuredLogCaptureEngine? structuredLogCaptureEngine;
    private RegexTabbedLogCaptureEngine? tabbedLogCaptureEngine;
    private RegexScalarRunCaptureEngine? scalarRunCaptureEngine;
    private RegexAnchoredWordCaptureEngine? anchoredWordCaptureEngine;
    private RegexAnchoredRunBoundaryCaptureEngine? anchoredRunBoundaryCaptureEngine;
    private RegexAnchoredDotStarCaptureEngine? anchoredDotStarCaptureEngine;
    private RegexAnchoredQuotedStringCaptureEngine? anchoredQuotedStringCaptureEngine;
    private RegexKeywordWhitespaceCaptureEngine? keywordWhitespaceCaptureEngine;
    private RegexNoqaCaptureEngine? noqaCaptureEngine;
    private RegexLinePrefixCaptureEngine? linePrefixCaptureEngine;
    private RegexOperatorSpacingCaptureEngine? operatorSpacingCaptureEngine;
    private RegexFixedByteSequenceCaptureEngine? fixedByteSequenceCaptureEngine;
    private RegexLiteralWordCaptureEngine? literalWordCaptureEngine;
    private RegexLiteralRunAlternationCaptureEngine? literalRunAlternationCaptureEngine;
    private RegexPathSemverCaptureEngine? pathSemverCaptureEngine;
    private RegexAsciiLetterLengthAlternationCaptureEngine? asciiLetterLengthAlternationCaptureEngine;
    private RegexAsciiWordLengthAlternationCaptureEngine? asciiWordLengthAlternationCaptureEngine;
    private RegexBibleReferenceCaptureEngine? bibleReferenceCaptureEngine;
    private RegexFnPredicateCaptureEngine? fnPredicateCaptureEngine;
    private volatile bool captureEnginesInitialized;
    private volatile bool genericCaptureOnly;

    private RegexAutomaton(
        RegexMetaEngine engine,
        RegexStartPredicate? startPredicate,
        RegexLengthGuard? lengthGuard,
        RegexRequiredByteSetGuard? requiredByteSetGuard,
        RegexRequiredLiteralAnySetGuard? requiredLiteralAnySetGuard,
        RegexAlternationSetEngine? syntheticCaptureAlternationSet,
        ReadOnlyMemory<byte> capturePattern,
        RegexSyntaxNode? captureRoot,
        RegexCompileOptions captureOptions,
        RegexPrefilter? capturePrefilter,
        int captureCount,
        int wholePatternCaptureIndex = 0)
    {
        this.engine = engine;
        this.startPredicate = startPredicate;
        this.lengthGuard = lengthGuard;
        this.requiredByteSetGuard = requiredByteSetGuard;
        this.requiredLiteralAnySetGuard = requiredLiteralAnySetGuard;
        hasSearchGuards = lengthGuard is not null ||
            !IgnoresRequiredSearchGuards(engine.Kind) &&
            (requiredByteSetGuard is not null || requiredLiteralAnySetGuard is not null);
        this.syntheticCaptureAlternationSet = syntheticCaptureAlternationSet;
        this.capturePattern = capturePattern;
        this.captureRoot = captureRoot;
        this.captureOptions = captureOptions;
        this.capturePrefilter = capturePrefilter;
        this.captureCount = captureCount;
        this.wholePatternCaptureIndex = wholePatternCaptureIndex;
        captureEnginesInitialized = captureCount == 0;
        genericCaptureOnly = captureCount == 0;
    }

    /// <summary>
    /// Compiles a regex pattern into a meta-selected automaton.
    /// </summary>
    /// <param name="pattern">The regex pattern bytes.</param>
    /// <returns>The compiled automaton.</returns>
    public static RegexAutomaton Compile(ReadOnlySpan<byte> pattern)
    {
        return Compile(pattern, caseInsensitive: false, multiLine: false, dotMatchesNewline: false);
    }

    /// <summary>
    /// Compiles a regex pattern into a meta-selected automaton with root regex options.
    /// </summary>
    /// <param name="pattern">The regex pattern bytes.</param>
    /// <param name="multiLine">Whether <c>^</c> and <c>$</c> match adjacent to line feeds.</param>
    /// <param name="dotMatchesNewline">Whether <c>.</c> matches line feeds.</param>
    /// <returns>The compiled automaton.</returns>
    public static RegexAutomaton Compile(ReadOnlySpan<byte> pattern, bool multiLine, bool dotMatchesNewline)
    {
        return Compile(pattern, caseInsensitive: false, multiLine, dotMatchesNewline);
    }

    /// <summary>
    /// Compiles a regex pattern into a meta-selected automaton with root regex options.
    /// </summary>
    /// <param name="pattern">The regex pattern bytes.</param>
    /// <param name="caseInsensitive">Whether literal and class atoms match ASCII case-insensitively.</param>
    /// <param name="multiLine">Whether <c>^</c> and <c>$</c> match adjacent to line feeds.</param>
    /// <param name="dotMatchesNewline">Whether <c>.</c> matches line feeds.</param>
    /// <param name="crlf">Whether CRLF mode treats carriage returns and line feeds as line terminators.</param>
    /// <param name="lineTerminator">The line terminator byte used when CRLF mode is disabled.</param>
    /// <param name="utf8">Whether empty and scalar-consuming matches must respect UTF-8 code point boundaries.</param>
    /// <param name="unicodeClasses">Whether Perl classes and word-boundary assertions use Unicode word definitions.</param>
    /// <param name="dfaSizeLimit">The maximum DFA cache size in bytes, or <see langword="null" /> for the default.</param>
    /// <param name="specializationMode">The specialization mode to use, or <see langword="null" /> for Scout's current default.</param>
    /// <returns>The compiled automaton.</returns>
    public static RegexAutomaton Compile(
        ReadOnlySpan<byte> pattern,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf = false,
        byte lineTerminator = (byte)'\n',
        bool utf8 = true,
        bool unicodeClasses = true,
        ulong? dfaSizeLimit = null,
        RegexSpecializationMode? specializationMode = null)
    {
        var options = new RegexCompileOptions(caseInsensitive, swapGreed: false, multiLine, dotMatchesNewline, crlf, lineTerminator, utf8, unicodeClasses, specializationMode);
        if (options.SpecializationMode != RegexSpecializationMode.Fallback &&
            RegexLiteralSetEngine.TryCreateLiteralAlternation(pattern, options, out RegexLiteralSetEngine? literalSet) &&
            literalSet is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileLiteralSet(literalSet, options.Utf8),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                capturePattern: default,
                captureRoot: null,
                captureOptions: default,
                capturePrefilter: null,
                captureCount: 0);
        }

        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        return CompileParsed(tree, options, dfaSizeLimit);
    }

    internal static RegexAutomaton CompileParsed(
        RegexSyntaxTree tree,
        RegexCompileOptions options,
        ulong? dfaSizeLimit = null,
        bool compilePrefilter = true)
    {
        return CompileParsedWithCache(tree, options, dfaSizeLimit, compilePrefilter, utf8ByteTrieCache: null);
    }

    internal static RegexAutomaton CompileParsedWithCache(
        RegexSyntaxTree tree,
        RegexCompileOptions options,
        ulong? dfaSizeLimit,
        bool compilePrefilter,
        Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache = null)
    {
        if (options.SpecializationMode == RegexSpecializationMode.Fallback)
        {
            return CompileParsedFallback(tree, options, dfaSizeLimit, utf8ByteTrieCache);
        }

        RegexEmptyEngine.TryCreate(tree.Root, options, out RegexEmptyEngine? empty);
        if (empty is not null && tree.CaptureCount == 0)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileEmpty(empty),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                capturePattern: default,
                captureRoot: null,
                captureOptions: default,
                capturePrefilter: null,
                captureCount: 0);
        }

        RegexRepeatedLiteralRunOrEmptyEngine.TryCreate(
            tree.Root,
            options,
            tree.CaptureCount,
            out RegexRepeatedLiteralRunOrEmptyEngine? repeatedLiteralRunOrEmpty);
        if (repeatedLiteralRunOrEmpty is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileRepeatedLiteralRunOrEmpty(
                    repeatedLiteralRunOrEmpty,
                    options.Utf8),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                capturePattern: default,
                captureRoot: null,
                captureOptions: default,
                capturePrefilter: null,
                captureCount: 0);
        }

        RegexLiteralSetEngine.TryCreate(tree.Root, options, out RegexLiteralSetEngine? literalSet);
        if (literalSet is not null && tree.CaptureCount == 0)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileLiteralSet(literalSet, options.Utf8),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                capturePattern: default,
                captureRoot: null,
                captureOptions: default,
                capturePrefilter: null,
                captureCount: 0);
        }

        RegexWordBoundaryLiteralSetEngine.TryCreate(tree.Root, options, out RegexWordBoundaryLiteralSetEngine? wordBoundaryLiteralSet);
        int wholePatternCaptureIndex = TryGetWholePatternCaptureIndex(tree.Root, tree.CaptureCount);
        if (wordBoundaryLiteralSet is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileWordBoundaryLiteralSet(
                    wordBoundaryLiteralSet,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexAlternationSetEngine.TryCreate(tree.Pattern.Span, tree.Root, tree.CaptureCount, options, out RegexAlternationSetEngine? alternationSet);
        if (alternationSet is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileAlternationSet(
                    alternationSet,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                alternationSet.CanSynthesizeCaptures ? alternationSet : null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexDelimitedCaptureEngine.TryCreate(
            tree.Root,
            options,
            tree.CaptureCount,
            out RegexDelimitedCaptureEngine? earlyDelimitedCapture,
            compactFields: true);
        if (earlyDelimitedCapture is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileDelimitedCapture(
                    earlyDelimitedCapture,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexStructuredLogCaptureEngine.TryCreate(
            tree.Root,
            options,
            tree.CaptureCount,
            out RegexStructuredLogCaptureEngine? earlyStructuredLogCapture);
        if (earlyStructuredLogCapture is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileStructuredLogCapture(
                    earlyStructuredLogCapture,
                    options.Utf8),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexBoundedScalarClassSequenceEngine.TryCreate(tree.Root, options, out RegexBoundedScalarClassSequenceEngine? earlyBoundedScalarClassSequence);
        if (earlyBoundedScalarClassSequence is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileBoundedScalarClassSequence(
                    earlyBoundedScalarClassSequence,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexBoundedByteClassSequenceEngine.TryCreate(tree.Root, options, out RegexBoundedByteClassSequenceEngine? earlyBoundedByteClassSequence);
        if (earlyBoundedByteClassSequence is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileBoundedByteClassSequence(
                    earlyBoundedByteClassSequence,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexFixedWordWhitespaceSequenceEngine.TryCreate(
            tree.Root,
            options,
            out RegexFixedWordWhitespaceSequenceEngine? fixedWordWhitespaceSequence);
        if (fixedWordWhitespaceSequence is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileFixedWordWhitespaceSequence(
                    fixedWordWhitespaceSequence,
                    options.Utf8),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexUnicodeGraphemeClusterEngine.TryCreate(tree.Root, options, out RegexUnicodeGraphemeClusterEngine? unicodeGraphemeCluster);
        if (unicodeGraphemeCluster is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileUnicodeGraphemeCluster(
                    unicodeGraphemeCluster,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexScalarRunEngine.TryCreate(tree.Root, options, out RegexScalarRunEngine? earlyScalarRun);
        if (earlyScalarRun is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileScalarRun(
                    earlyScalarRun,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: RegexLengthGuard.TryCreate(tree.Root, options),
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexSimpleSequenceEngine.TryCreateSingleRepeatedByteAtom(tree.Root, options, out RegexSimpleSequenceEngine? earlySimpleSequence);
        if (earlySimpleSequence is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileSimpleSequence(
                    earlySimpleSequence,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: RegexLengthGuard.TryCreate(tree.Root, options),
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexFixedWidthAlternationEngine.TryCreate(
            tree.Root,
            tree.CaptureCount,
            options,
            out RegexFixedWidthAlternationEngine? earlyFixedWidthAlternation);
        if (earlyFixedWidthAlternation is not null &&
            (CanSkipHigherPriorityFixedWidthGuards(tree.Root, options) ||
            !HasHigherPriorityFixedWidthSpecialization(tree.Root, options)))
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileFixedWidthAlternation(
                    earlyFixedWidthAlternation,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                tree.CaptureCount > 0 ? tree.Pattern : default,
                tree.CaptureCount > 0 ? tree.Root : null,
                options,
                capturePrefilter: null,
                tree.CaptureCount,
                wholePatternCaptureIndex);
        }

        RegexDelimitedSpanEngine.TryCreate(tree.Root, options, out RegexDelimitedSpanEngine? earlyDelimitedSpan);
        if (earlyDelimitedSpan is not null &&
            tree.CaptureCount == 0 &&
            !HasHigherPriorityDelimitedSpanSpecialization(tree.Root, options))
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileDelimitedSpan(
                    earlyDelimitedSpan,
                    options.Utf8,
                    () => CompileGeneralNfa(tree.Root, options, utf8ByteTrieCache)),
                startPredicate: null,
                lengthGuard: null,
                requiredByteSetGuard: null,
                requiredLiteralAnySetGuard: null,
                syntheticCaptureAlternationSet: null,
                capturePattern: default,
                captureRoot: null,
                captureOptions: default,
                capturePrefilter: null,
                captureCount: 0);
        }

        RegexNfa nfa = CompileGeneralNfa(
            tree.Root,
            options,
            utf8ByteTrieCache);
        bool shouldCompilePrefilter = compilePrefilter && !HasHardStartAnchor(tree.Root, options);
        RegexStartPrefixSet? startPrefixSet = null;
        RegexPrefilter? prefilter = shouldCompilePrefilter
            ? RegexPrefilter.Compile(tree.Root, options, out startPrefixSet)
            : null;

        bool allowDomainRecognizers = AllowsDomainRecognizers(options);
        RegexWholeLineEngine.TryCreate(tree.Root, options, out RegexWholeLineEngine? wholeLine);
        RegexDotStarEngine.TryCreate(tree.Root, options, out RegexDotStarEngine? dotStar);
        RegexIpv4AddressEngine? ipv4Address = null;
        RegexEmailAddressEngine? emailAddress = null;
        if (allowDomainRecognizers)
        {
            RegexIpv4AddressEngine.TryCreate(tree.Root, options, out ipv4Address);
            RegexEmailAddressEngine.TryCreate(tree.Root, options, out emailAddress);
        }

        RegexLh3EmailEngine? lh3Email = null;
        if (AllowsBenchmarkFamilyRecognizers(options))
        {
            RegexLh3EmailEngine.TryCreate(tree.Root, options, out lh3Email);
        }

        RegexUriEngine? uri = null;
        if (allowDomainRecognizers)
        {
            RegexUriEngine.TryCreate(tree.Root, options, out uri);
        }

        RegexLh3UriEngine? lh3Uri = null;
        RegexLh3UriOrEmailEngine? lh3UriOrEmail = null;
        if (AllowsBenchmarkFamilyRecognizers(options))
        {
            RegexLh3UriEngine.TryCreate(tree.Root, options, out lh3Uri);
            RegexLh3UriOrEmailEngine.TryCreate(tree.Root, options, out lh3UriOrEmail);
        }

        RegexBoundedDigitDelimiterEngine.TryCreate(tree.Root, options, out RegexBoundedDigitDelimiterEngine? boundedDigitDelimiter);
        RegexWordWhitespaceLiteralEngine.TryCreate(tree.Root, options, out RegexWordWhitespaceLiteralEngine? wordWhitespaceLiteral);
        RegexUnicodeWordWhitespaceLiteralEngine.TryCreate(tree.Root, options, out RegexUnicodeWordWhitespaceLiteralEngine? unicodeWordWhitespaceLiteral);
        RegexBoundedLetterSuffixWhitespaceEngine.TryCreate(tree.Root, options, out RegexBoundedLetterSuffixWhitespaceEngine? boundedLetterSuffixWhitespace);
        RegexRunLiteralDotStarEngine.TryCreate(tree.Root, options, out RegexRunLiteralDotStarEngine? runLiteralDotStar);
        RegexLiteralPrefixRunEngine.TryCreate(tree.Root, options, out RegexLiteralPrefixRunEngine? literalPrefixRun);
        RegexBoundedLiteralGapEngine.TryCreate(tree.Root, options, out RegexBoundedLiteralGapEngine? boundedLiteralGap);
        RegexBoundedLineLiteralGapEngine.TryCreate(tree.Root, options, out RegexBoundedLineLiteralGapEngine? boundedLineLiteralGap);
        RegexAnchoredLineLiteralGapEngine.TryCreate(tree.Root, options, out RegexAnchoredLineLiteralGapEngine? anchoredLineLiteralGap);
        RegexBoundedPrefixLiteralSetEngine.TryCreate(tree.Root, options, out RegexBoundedPrefixLiteralSetEngine? boundedPrefixLiteralSet);
        RegexBoundedScalarClassSequenceEngine? boundedScalarClassSequence = earlyBoundedScalarClassSequence;
        RegexBoundedByteClassSequenceEngine? boundedByteClassSequence = earlyBoundedByteClassSequence;
        RegexRepeatedLazyDotStarLiteralEngine.TryCreate(tree.Root, options, out RegexRepeatedLazyDotStarLiteralEngine? repeatedLazyDotStarLiteral);
        RegexDelimitedSpanEngine.TryCreate(tree.Root, options, out RegexDelimitedSpanEngine? delimitedSpan);
        RegexFixedWidthAlternationEngine? fixedWidthAlternation = earlyFixedWidthAlternation;
        RegexLeadingClassLiteralEngine.TryCreate(tree.Root, options, out RegexLeadingClassLiteralEngine? leadingClassLiteral);
        RegexLineBoundaryLiteralEngine? lineBoundaryLiteral = null;
        if (tree.CaptureCount == 0)
        {
            RegexLineBoundaryLiteralEngine.TryCreate(tree.Root, options, out lineBoundaryLiteral);
        }

        RegexUnicodeLetterLiteralRunEngine.TryCreate(tree.Root, options, out RegexUnicodeLetterLiteralRunEngine? unicodeLetterLiteralRun);
        RegexWordSuffixLiteralEngine.TryCreate(tree.Root, options, out RegexWordSuffixLiteralEngine? wordSuffixLiteral);
        RegexDelimitedRunEngine.TryCreate(tree.Root, options, out RegexDelimitedRunEngine? delimitedRun);
        RegexSimpleSequenceEngine? simpleSequence = null;
        if (boundedLiteralGap is null &&
            boundedLineLiteralGap is null &&
            anchoredLineLiteralGap is null &&
            boundedPrefixLiteralSet is null)
        {
            RegexSimpleSequenceEngine.TryCreate(tree.Root, options, out simpleSequence);
        }

        RegexEndAnchoredAtomEngine.TryCreate(tree.Root, options, out RegexEndAnchoredAtomEngine? endAnchoredAtom);
        RegexEndAnchoredSequenceEngine? endAnchoredSequence = null;
        RegexLineContainsEngine? lineContains = null;
        RegexDotStarClassFallbackEngine? dotStarClassFallback = null;
        RegexScalarRunEngine? scalarRun = null;
        RegexAsciiWordBoundaryEngine? asciiWordBoundary = null;
        if (simpleSequence is null && endAnchoredAtom is null)
        {
            RegexEndAnchoredSequenceEngine.TryCreate(tree.Root, options, out endAnchoredSequence);
            RegexLineContainsEngine.TryCreate(tree.Root, options, out lineContains);
            RegexDotStarClassFallbackEngine.TryCreate(tree.Root, options, out dotStarClassFallback);
            RegexScalarRunEngine.TryCreate(tree.Root, options, out scalarRun);
            RegexAsciiWordBoundaryEngine.TryCreate(tree.Root, options, out asciiWordBoundary);
        }

        var lengthGuard = RegexLengthGuard.TryCreate(tree.Root, options);
        var requiredByteSetGuard = RegexRequiredByteSetGuard.TryCreate(tree.Root, options);
        RegexRequiredLiteralAnySetGuard? requiredLiteralAnySetGuard = prefilter is null
            ? RegexRequiredLiteralAnySetGuard.TryCreate(tree.Root, options)
            : null;

        var metaEngine = RegexMetaEngine.Compile(
            nfa,
            prefilter,
            dfaSizeLimit,
            literalSet,
            alternationSet: null,
            wholeLine: wholeLine,
            dotStar: dotStar,
            ipv4Address: ipv4Address,
            emailAddress: emailAddress,
            lh3Email: lh3Email,
            uri: uri,
            lh3Uri: lh3Uri,
            lh3UriOrEmail: lh3UriOrEmail,
            boundedDigitDelimiter: boundedDigitDelimiter,
            wordWhitespaceLiteral: wordWhitespaceLiteral,
            unicodeWordWhitespaceLiteral: unicodeWordWhitespaceLiteral,
            boundedLetterSuffixWhitespace: boundedLetterSuffixWhitespace,
            runLiteralDotStar: runLiteralDotStar,
            literalPrefixRun: literalPrefixRun,
            boundedLiteralGap: boundedLiteralGap,
            boundedLineLiteralGap: boundedLineLiteralGap,
            anchoredLineLiteralGap: anchoredLineLiteralGap,
            boundedPrefixLiteralSet: boundedPrefixLiteralSet,
            boundedScalarClassSequence: boundedScalarClassSequence,
            boundedByteClassSequence: boundedByteClassSequence,
            repeatedLazyDotStarLiteral: repeatedLazyDotStarLiteral,
            delimitedSpan: delimitedSpan,
            fixedWidthAlternation: fixedWidthAlternation,
            leadingClassLiteral: leadingClassLiteral,
            lineBoundaryLiteral: lineBoundaryLiteral,
            unicodeLetterLiteralRun: unicodeLetterLiteralRun,
            wordBoundaryLiteralSet: wordBoundaryLiteralSet,
            wordSuffixLiteral: wordSuffixLiteral,
            delimitedRun: delimitedRun,
            simpleSequence: simpleSequence,
            endAnchoredSequence: endAnchoredSequence,
            endAnchoredAtom: endAnchoredAtom,
            lineContains: lineContains,
            dotStarClassFallback: dotStarClassFallback,
            asciiFastPattern: tree.Pattern,
            scalarRun: scalarRun,
            asciiWordBoundary: asciiWordBoundary,
            root: tree.Root,
            options: options);
        RegexStartPredicate? startPredicate = null;
        if (ShouldCompileStartPredicate(metaEngine.Kind, prefilter))
        {
            RegexStartPredicate.TryCreate(tree.Root, options, startPrefixSet, out startPredicate);
        }

        return new RegexAutomaton(
            metaEngine,
            startPredicate,
            lengthGuard,
            requiredByteSetGuard,
            requiredLiteralAnySetGuard,
            syntheticCaptureAlternationSet: null,
            tree.CaptureCount > 0 ? tree.Pattern : default,
            tree.CaptureCount > 0 ? tree.Root : null,
            options,
            prefilter,
            tree.CaptureCount,
            wholePatternCaptureIndex);
    }

    private static RegexAutomaton CompileParsedFallback(
        RegexSyntaxTree tree,
        RegexCompileOptions options,
        ulong? dfaSizeLimit,
        Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache)
    {
        RegexNfa nfa = CompileGeneralNfa(
            tree.Root,
            options,
            utf8ByteTrieCache);
        int wholePatternCaptureIndex = TryGetWholePatternCaptureIndex(tree.Root, tree.CaptureCount);
        return new RegexAutomaton(
            RegexMetaEngine.Compile(
                nfa,
                prefilter: null,
                dfaSizeLimit: dfaSizeLimit,
                literalSet: null,
                alternationSet: null,
                root: tree.Root,
                options: options),
            startPredicate: null,
            lengthGuard: null,
            requiredByteSetGuard: null,
            requiredLiteralAnySetGuard: null,
            syntheticCaptureAlternationSet: null,
            tree.CaptureCount > 0 ? tree.Pattern : default,
            tree.CaptureCount > 0 ? tree.Root : null,
            options,
            capturePrefilter: null,
            tree.CaptureCount,
            wholePatternCaptureIndex);
    }

    /// <summary>
    /// Compiles the general-purpose NFA, avoiding an expanded UTF-8 byte graph when the
    /// meta engine is guaranteed to replace that graph with compact scalar atoms.
    /// </summary>
    private static RegexNfa CompileGeneralNfa(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache)
    {
        return ShouldCompileCompactScalarNfa(root, options)
            ? RegexNfaCompiler.CompileWithCompactScalarAtoms(root, options, utf8ByteTrieCache)
            : RegexNfaCompiler.Compile(root, options, utf8ByteTrieCache);
    }

    /// <summary>
    /// Determines whether an option-aware syntax analysis proves that eager UTF-8 expansion
    /// cannot enable a DFA and would cross the meta engine's compact-fallback threshold.
    /// </summary>
    /// <param name="root">The parsed regex root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <returns><see langword="true" /> when compact scalar construction should be used.</returns>
    internal static bool ShouldCompileCompactScalarNfa(
        RegexSyntaxNode root,
        RegexCompileOptions options)
    {
        const int compactScalarFallbackNfaStateThreshold = 4096;

        return ContainsDfaUnsupportedPredicate(root) &&
            EstimateExpandedScalarStateCount(
                root,
                options,
                compactScalarFallbackNfaStateThreshold) >= compactScalarFallbackNfaStateThreshold;
    }

    /// <summary>
    /// Reports whether a syntax subtree contains a zero-width predicate that the byte DFA
    /// engines cannot compile.
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

    /// <summary>
    /// Computes a saturated lower bound for scalar-expansion states in a syntax subtree.
    /// </summary>
    private static int EstimateExpandedScalarStateCount(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        int limit)
    {
        switch (node)
        {
            case RegexAtomNode atom:
                return CountExpandedScalarAtomStates(atom, options, limit);

            case RegexGroupNode group:
                return EstimateExpandedScalarStateCount(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    limit);

            case RegexSequenceNode sequence:
                return EstimateSequenceExpandedScalarStateCount(sequence, options, limit);

            case RegexAlternationNode alternation:
                {
                    int maximum = 0;
                    for (int index = 0; index < alternation.Alternatives.Count; index++)
                    {
                        maximum = Math.Max(
                            maximum,
                            EstimateExpandedScalarStateCount(alternation.Alternatives[index], options, limit));
                    }

                    return maximum;
                }

            case RegexRepetitionNode repetition:
                {
                    int childCount = EstimateExpandedScalarStateCount(repetition.Child, options, limit);
                    int compilations = repetition.Maximum ?? SaturatingAdd(repetition.Minimum, 1, limit);
                    return SaturatingMultiply(childCount, compilations, limit);
                }

            default:
                return 0;
        }
    }

    /// <summary>
    /// Computes a saturated scalar-expansion state lower bound for a sequence while applying
    /// inline option changes in source order.
    /// </summary>
    private static int EstimateSequenceExpandedScalarStateCount(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        int limit)
    {
        int count = 0;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            count = SaturatingAdd(
                count,
                EstimateExpandedScalarStateCount(child, currentOptions, limit),
                limit);
        }

        return count;
    }

    /// <summary>
    /// Counts the byte-NFA states needed to expand one scalar-consuming atom without retaining
    /// the temporary graph.
    /// </summary>
    private static int CountExpandedScalarAtomStates(
        RegexAtomNode atom,
        RegexCompileOptions options,
        int limit)
    {
        if (!RegexByteClass.RequiresUtf8ScalarMatch(
            atom.Kind,
            atom.Value.Span,
            options.Utf8,
            options.CaseInsensitive,
            options.UnicodeClasses))
        {
            return 0;
        }

        int count = 0;
        int AddByteClass(ReadOnlySpan<byte> ranges, int next)
        {
            return count++;
        }

        int AddSplit(int first, int second)
        {
            return count++;
        }

        int AddSparse(ReadOnlySpan<RegexNfaSparseTransition> transitions)
        {
            return count++;
        }

        if (RegexUtf8ByteCompiler.TryGetSharedTrie(
            atom.Kind,
            atom.Value.Span,
            options,
            reversed: false,
            out RegexUtf8ByteTrie? sharedTrie))
        {
            _ = sharedTrie!.Compile(next: -1, AddSplit, AddSparse);
            return Math.Min(count, limit);
        }

        if (!RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(
            atom.Kind,
            atom.Value.Span,
            options,
            out List<RegexScalarRange> ranges))
        {
            return 0;
        }

        if (RegexUtf8ByteCompiler.TryCompileCompactFromRanges(
            ranges,
            reversed: false,
            next: -1,
            AddByteClass,
            AddSplit,
            out _))
        {
            return Math.Min(count, limit);
        }

        if (RegexUtf8ByteCompiler.TryCompileRangeSequencesFromRanges(
            ranges,
            reversed: false,
            next: -1,
            AddByteClass,
            AddSplit,
            out _))
        {
            return Math.Min(count, limit);
        }

        if (!RegexUtf8ByteCompiler.TryCreateFromRanges(ranges, reversed: false, out RegexUtf8ByteTrie? trie))
        {
            return 0;
        }

        _ = trie!.Compile(next: -1, AddSplit, AddSparse);
        return Math.Min(count, limit);
    }

    /// <summary>
    /// Adds two non-negative values and saturates at a limit.
    /// </summary>
    private static int SaturatingAdd(int left, int right, int limit)
    {
        return left >= limit - Math.Min(right, limit) ? limit : left + right;
    }

    /// <summary>
    /// Multiplies two non-negative values and saturates at a limit.
    /// </summary>
    private static int SaturatingMultiply(int left, int right, int limit)
    {
        if (left == 0 || right == 0)
        {
            return 0;
        }

        if (left >= limit || right >= limit)
        {
            return limit;
        }

        return left >= (limit + right - 1) / right ? limit : left * right;
    }

    internal static RegexAutomaton CompileParsedForPatternSet(
        RegexSyntaxTree tree,
        RegexCompileOptions options,
        ulong? dfaSizeLimit,
        Dictionary<string, RegexUtf8ByteTrie>? utf8ByteTrieCache = null)
    {
        RegexNfa nfa = RegexNfaCompiler.CompileWithCompactScalarAtoms(
            tree.Root,
            options,
            utf8ByteTrieCache);
        RegexStartPredicate.TryCreateFirstByteOnly(tree.Root, options, out RegexStartPredicate? startPredicate);
        var lengthGuard = RegexLengthGuard.TryCreate(tree.Root, options);
        return new RegexAutomaton(
            RegexMetaEngine.Compile(nfa, prefilter: null, dfaSizeLimit: dfaSizeLimit),
            startPredicate,
            lengthGuard,
            requiredByteSetGuard: null,
            requiredLiteralAnySetGuard: null,
            syntheticCaptureAlternationSet: null,
            capturePattern: default,
            captureRoot: null,
            captureOptions: options,
            capturePrefilter: null,
            captureCount: 0);
    }

    private static bool ShouldCompileStartPredicate(RegexEngineKind engineKind, RegexPrefilter? prefilter)
    {
        if (prefilter is not null)
        {
            return false;
        }

        return engineKind is RegexEngineKind.PikeVm
            or RegexEngineKind.LazyDfa
            or RegexEngineKind.DenseDfa
            or RegexEngineKind.SparseDfa
            or RegexEngineKind.OnePassDfa
            or RegexEngineKind.BoundedBacktracker;
    }

    private static bool HasHardStartAnchor(RegexSyntaxNode root, RegexCompileOptions options)
    {
        root = UnwrapTransparentGroups(root);
        if (root is RegexSequenceNode sequence)
        {
            RegexCompileOptions currentOptions = options;
            for (int index = 0; index < sequence.Nodes.Count; index++)
            {
                RegexSyntaxNode node = UnwrapTransparentGroups(sequence.Nodes[index]);
                if (node is RegexInlineFlagsNode flags)
                {
                    currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                    continue;
                }

                return IsHardStartAnchor(node, currentOptions);
            }

            return false;
        }

        return IsHardStartAnchor(root, options);
    }

    private static bool IsHardStartAnchor(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapTransparentGroups(node);
        return node is RegexAtomNode atom &&
            (atom.Kind == RegexSyntaxKind.AbsoluteStartAnchor ||
            atom.Kind == RegexSyntaxKind.StartAnchor && !options.MultiLine);
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.NonCapturingGroup,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group)
        {
            node = group.Child;
        }

        return node;
    }

    internal RegexPrefilterKind PrefilterKind => engine.PrefilterKind;

    internal int RequiredLiteralWindow => engine.RequiredLiteralWindow;

    internal bool UsesRequiredLiteralAnySetGuard => requiredLiteralAnySetGuard is not null;

    internal bool UsesSyntheticCaptureAlternationSet
    {
        get
        {
            EnsureCaptureEngines();
            return syntheticCaptureAlternationSet?.CanSynthesizeCaptures == true;
        }
    }

    internal bool UsesWholePatternCaptureSynthesis => wholePatternCaptureIndex > 0;

    internal bool UsesStructuredLogCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return structuredLogCaptureEngine is not null;
        }
    }

    internal bool UsesTabbedLogCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return tabbedLogCaptureEngine is not null;
        }
    }

    internal bool UsesAnchoredWordCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return anchoredWordCaptureEngine is not null;
        }
    }

    internal bool UsesAnchoredRunBoundaryCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return anchoredRunBoundaryCaptureEngine is not null;
        }
    }

    internal bool UsesAnchoredDotStarCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return anchoredDotStarCaptureEngine is not null;
        }
    }

    internal bool UsesAnchoredQuotedStringCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return anchoredQuotedStringCaptureEngine is not null;
        }
    }

    internal bool UsesScalarRunCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return scalarRunCaptureEngine is not null;
        }
    }

    internal bool UsesKeywordWhitespaceCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return keywordWhitespaceCaptureEngine is not null;
        }
    }

    internal bool UsesNoqaCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return noqaCaptureEngine is not null;
        }
    }

    internal bool UsesLinePrefixCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return linePrefixCaptureEngine is not null;
        }
    }

    internal bool UsesOperatorSpacingCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return operatorSpacingCaptureEngine is not null;
        }
    }

    internal bool UsesFixedByteSequenceCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return fixedByteSequenceCaptureEngine is not null;
        }
    }

    internal bool UsesLiteralWordCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return literalWordCaptureEngine is not null;
        }
    }

    internal bool UsesLiteralRunAlternationCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return literalRunAlternationCaptureEngine is not null;
        }
    }

    internal bool UsesPathSemverCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return pathSemverCaptureEngine is not null;
        }
    }

    internal bool UsesAsciiLetterLengthAlternationCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return asciiLetterLengthAlternationCaptureEngine is not null;
        }
    }

    internal bool UsesAsciiWordLengthAlternationCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return asciiWordLengthAlternationCaptureEngine is not null;
        }
    }

    internal bool UsesBibleReferenceCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return bibleReferenceCaptureEngine is not null;
        }
    }

    internal bool UsesFnPredicateCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return fnPredicateCaptureEngine is not null;
        }
    }

    internal RegexEngineKind EngineKind => engine.Kind;

    /// <summary>
    /// Finds the first match in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? Find(ReadOnlySpan<byte> haystack)
    {
        return Find(haystack, startAt: 0);
    }

    /// <summary>
    /// Finds the first match in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (hasSearchGuards && !CanSearch(haystack, startAt))
        {
            return null;
        }

        return engine.Find(haystack, startAt, startPredicate);
    }

    internal RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!CanSearch(haystack, startAt))
        {
            return null;
        }

        if (startPredicate is not null && !startPredicate.CanStartAt(haystack, Math.Clamp(startAt, 0, haystack.Length)))
        {
            return null;
        }

        return engine.MatchAt(haystack, startAt);
    }

    internal bool TryAddStartBytes(bool[] bytes)
    {
        return startPredicate?.TryAddFirstBytes(bytes) == true;
    }

    private bool CanSearch(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!hasSearchGuards)
        {
            return true;
        }

        if (lengthGuard is not null && !lengthGuard.CanSearch(haystack, startAt))
        {
            return false;
        }

        if (IgnoresRequiredSearchGuards(engine.Kind))
        {
            return true;
        }

        return (requiredByteSetGuard is null || requiredByteSetGuard.CanSearch(haystack, startAt)) &&
            (requiredLiteralAnySetGuard is null || requiredLiteralAnySetGuard.CanSearch(haystack, startAt));
    }

    private static bool IgnoresRequiredSearchGuards(RegexEngineKind kind)
    {
        return kind is RegexEngineKind.Uri
            or RegexEngineKind.BoundedDigitDelimiter
            or RegexEngineKind.EndAnchoredSequence
            or RegexEngineKind.EndAnchoredAtom
            or RegexEngineKind.RunLiteralDotStar
            or RegexEngineKind.UnicodeLetterLiteralRun
            or RegexEngineKind.WordBoundaryLiteralSet;
    }

    /// <summary>
    /// Finds the earliest-ending match in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The earliest match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? FindEarliest(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!CanSearch(haystack, startAt))
        {
            return null;
        }

        return engine.FindEarliest(haystack, startAt);
    }

    internal RegexMatch? FindAllKindAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!CanSearch(haystack, startAt))
        {
            return null;
        }

        return engine.FindAllKindAt(haystack, startAt);
    }

    internal IReadOnlyList<RegexMatch> FindOverlappingAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!CanSearch(haystack, startAt))
        {
            return [];
        }

        return engine.FindOverlappingAt(haystack, startAt);
    }

    /// <summary>
    /// Returns a value indicating whether the regex matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when a match exists.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return CanSearch(haystack, startAt: 0) &&
            engine.IsMatch(haystack, startPredicate);
    }

    /// <summary>
    /// Counts lines whose content matches the regex.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The number of matching lines.</returns>
    public long CountMatchingLines(ReadOnlySpan<byte> haystack)
    {
        return engine.CountMatchingLines(haystack, startPredicate);
    }

    /// <summary>
    /// Counts all non-overlapping matches in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public long CountMatches(ReadOnlySpan<byte> haystack)
    {
        return CountMatches(haystack, startAt: 0);
    }

    /// <summary>
    /// Counts all non-overlapping matches in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!CanSearch(haystack, startAt))
        {
            return 0;
        }

        return engine.CountMatches(haystack, startAt, startPredicate);
    }

    /// <summary>
    /// Sums the byte lengths of all non-overlapping matches in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The sum of non-overlapping match lengths.</returns>
    public long SumMatchSpans(ReadOnlySpan<byte> haystack)
    {
        return SumMatchSpans(haystack, startAt: 0);
    }

    /// <summary>
    /// Sums the byte lengths of all non-overlapping matches in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The sum of non-overlapping match lengths.</returns>
    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!CanSearch(haystack, startAt))
        {
            return 0;
        }

        return engine.SumMatchSpans(haystack, startAt, startPredicate);
    }

    /// <summary>
    /// Counts all participating captures for non-overlapping matches in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The participating capture count.</returns>
    public long CountCaptures(ReadOnlySpan<byte> haystack)
    {
        return CountCaptures(haystack, startAt: 0);
    }

    /// <summary>
    /// Counts all participating captures for non-overlapping matches in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The participating capture count.</returns>
    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (!CanSearch(haystack, startAt))
        {
            return 0;
        }

        if (wholePatternCaptureIndex > 0)
        {
            return CountMatches(haystack, startAt) * 2;
        }

        if (captureEnginesInitialized && genericCaptureOnly)
        {
            return CountCapturesWithFind(haystack, startAt);
        }

        EnsureCaptureEngines();
        if (anchoredWordCaptureEngine is not null)
        {
            return anchoredWordCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (keywordWhitespaceCaptureEngine is not null)
        {
            return keywordWhitespaceCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (noqaCaptureEngine is not null)
        {
            return noqaCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (operatorSpacingCaptureEngine is not null)
        {
            return operatorSpacingCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (fixedByteSequenceCaptureEngine is not null)
        {
            return fixedByteSequenceCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (delimitedCaptureEngine is not null)
        {
            return delimitedCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (structuredLogCaptureEngine is not null)
        {
            return structuredLogCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (tabbedLogCaptureEngine is not null)
        {
            return tabbedLogCaptureEngine.CountCaptures(haystack, startAt);
        }

        if (linePrefixCaptureEngine is not null)
        {
            return linePrefixCaptureEngine.CountCaptures(haystack, startAt);
        }

        return CountCapturesWithFind(haystack, startAt);
    }

    private void EnsureCaptureEngines()
    {
        if (captureEnginesInitialized)
        {
            return;
        }

        lock (captureInitializationLock)
        {
            if (captureEnginesInitialized)
            {
                return;
            }

            if (captureOptions.SpecializationMode == RegexSpecializationMode.Fallback)
            {
                if (captureRoot is not null && captureCount > 0 && wholePatternCaptureIndex == 0)
                {
                    RegexNfa captureNfa = RegexNfaCompiler.CompileCaptures(captureRoot, captureOptions, captureCount);
                    captureEnginePool = new RegexRunnerPool<RegexCaptureEngine>(
                        new RegexCaptureEngine(captureNfa, prefilter: null),
                        () => new RegexCaptureEngine(captureNfa, prefilter: null));
                }

                genericCaptureOnly = true;
                captureEnginesInitialized = true;
                return;
            }

            if (captureRoot is not null && captureCount > 0 && wholePatternCaptureIndex == 0)
            {
                if (syntheticCaptureAlternationSet is null)
                {
                    RegexAlternationSetEngine.TryCreateSyntheticCaptures(
                        capturePattern.Span,
                        captureRoot,
                        captureCount,
                        captureOptions,
                        out syntheticCaptureAlternationSet);
                }

                RegexDelimitedCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out delimitedCaptureEngine,
                    compactFields: true);
                RegexStructuredLogCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out structuredLogCaptureEngine);
                RegexTabbedLogCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out tabbedLogCaptureEngine);
                RegexAnchoredWordCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out anchoredWordCaptureEngine);
                RegexAnchoredRunBoundaryCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out anchoredRunBoundaryCaptureEngine);
                RegexAnchoredDotStarCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out anchoredDotStarCaptureEngine);
                RegexAnchoredQuotedStringCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out anchoredQuotedStringCaptureEngine);
                RegexScalarRunCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out scalarRunCaptureEngine);
                bool allowCorpusSpecificCaptureRecognizers = AllowsCorpusSpecificCaptureRecognizers(captureOptions);
                if (allowCorpusSpecificCaptureRecognizers)
                {
                    RegexKeywordWhitespaceCaptureEngine.TryCreate(
                        captureRoot,
                        captureOptions,
                        captureCount,
                        out keywordWhitespaceCaptureEngine);
                    RegexNoqaCaptureEngine.TryCreate(
                        captureRoot,
                        captureOptions,
                        captureCount,
                        out noqaCaptureEngine);
                }

                RegexLinePrefixCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out linePrefixCaptureEngine);
                if (allowCorpusSpecificCaptureRecognizers)
                {
                    RegexOperatorSpacingCaptureEngine.TryCreate(
                        captureRoot,
                        captureOptions,
                        captureCount,
                        out operatorSpacingCaptureEngine);
                }

                RegexFixedByteSequenceCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out fixedByteSequenceCaptureEngine);
                RegexLiteralWordCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out literalWordCaptureEngine);
                RegexLiteralRunAlternationCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out literalRunAlternationCaptureEngine);
                if (allowCorpusSpecificCaptureRecognizers)
                {
                    RegexPathSemverCaptureEngine.TryCreate(
                        captureRoot,
                        captureOptions,
                        captureCount,
                        out pathSemverCaptureEngine);
                }

                RegexAsciiLetterLengthAlternationCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out asciiLetterLengthAlternationCaptureEngine);
                RegexAsciiWordLengthAlternationCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out asciiWordLengthAlternationCaptureEngine);
                if (allowCorpusSpecificCaptureRecognizers)
                {
                    RegexBibleReferenceCaptureEngine.TryCreate(
                        captureRoot,
                        captureOptions,
                        captureCount,
                        out bibleReferenceCaptureEngine);
                    RegexFnPredicateCaptureEngine.TryCreate(
                        captureRoot,
                        captureOptions,
                        captureCount,
                        out fnPredicateCaptureEngine);
                }

                if (syntheticCaptureAlternationSet is null &&
                    structuredLogCaptureEngine is null &&
                    delimitedCaptureEngine is null &&
                    anchoredWordCaptureEngine is null &&
                    anchoredRunBoundaryCaptureEngine is null &&
                    anchoredDotStarCaptureEngine is null &&
                    anchoredQuotedStringCaptureEngine is null &&
                    tabbedLogCaptureEngine is null &&
                    scalarRunCaptureEngine is null &&
                    keywordWhitespaceCaptureEngine is null &&
                    noqaCaptureEngine is null &&
                    linePrefixCaptureEngine is null &&
                    operatorSpacingCaptureEngine is null &&
                    fixedByteSequenceCaptureEngine is null &&
                    literalWordCaptureEngine is null &&
                    literalRunAlternationCaptureEngine is null &&
                    pathSemverCaptureEngine is null &&
                    asciiLetterLengthAlternationCaptureEngine is null &&
                    asciiWordLengthAlternationCaptureEngine is null &&
                    bibleReferenceCaptureEngine is null &&
                    fnPredicateCaptureEngine is null)
                {
                    RegexNfa captureNfa = RegexNfaCompiler.CompileCaptures(captureRoot, captureOptions, captureCount);
                    RegexPrefilter? effectiveCapturePrefilter = capturePrefilter ?? RegexPrefilter.Compile(captureRoot, captureOptions);
                    captureEnginePool = new RegexRunnerPool<RegexCaptureEngine>(
                        new RegexCaptureEngine(captureNfa, effectiveCapturePrefilter),
                        () => new RegexCaptureEngine(captureNfa, effectiveCapturePrefilter));
                }
            }

            genericCaptureOnly = structuredLogCaptureEngine is null &&
                delimitedCaptureEngine is null &&
                anchoredWordCaptureEngine is null &&
                anchoredRunBoundaryCaptureEngine is null &&
                anchoredDotStarCaptureEngine is null &&
                anchoredQuotedStringCaptureEngine is null &&
                tabbedLogCaptureEngine is null &&
                scalarRunCaptureEngine is null &&
                keywordWhitespaceCaptureEngine is null &&
                noqaCaptureEngine is null &&
                linePrefixCaptureEngine is null &&
                operatorSpacingCaptureEngine is null &&
                fixedByteSequenceCaptureEngine is null &&
                literalWordCaptureEngine is null &&
                literalRunAlternationCaptureEngine is null &&
                pathSemverCaptureEngine is null &&
                asciiLetterLengthAlternationCaptureEngine is null &&
                asciiWordLengthAlternationCaptureEngine is null &&
                bibleReferenceCaptureEngine is null &&
                fnPredicateCaptureEngine is null &&
                syntheticCaptureAlternationSet is null;
            captureEnginesInitialized = true;
        }
    }

    /// <summary>
    /// Finds the first match and its participating capture groups in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The first capture result, or <see langword="null" /> when no match exists.</returns>
    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt = 0)
    {
        if (!CanSearch(haystack, startAt))
        {
            return null;
        }

        if (captureEnginesInitialized && genericCaptureOnly)
        {
            return FindGenericCaptures(haystack, startAt);
        }

        if (wholePatternCaptureIndex > 0)
        {
            return FindWholePatternCaptures(haystack, startAt);
        }

        EnsureCaptureEngines();
        if (genericCaptureOnly)
        {
            return FindGenericCaptures(haystack, startAt);
        }

        if (structuredLogCaptureEngine is not null)
        {
            RegexCaptures? structuredCaptures = structuredLogCaptureEngine.MatchAt(
                haystack,
                Math.Clamp(startAt, 0, haystack.Length));
            if (structuredCaptures is not null)
            {
                return structuredCaptures;
            }
        }

        RegexCaptures? delimitedCaptures = delimitedCaptureEngine?.MatchAt(haystack, Math.Clamp(startAt, 0, haystack.Length));
        if (delimitedCaptures is not null)
        {
            return delimitedCaptures;
        }

        if (tabbedLogCaptureEngine is not null)
        {
            return tabbedLogCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (linePrefixCaptureEngine is not null)
        {
            return linePrefixCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (literalRunAlternationCaptureEngine is not null)
        {
            return literalRunAlternationCaptureEngine.FindCaptures(haystack, startAt);
        }

        RegexCaptures? syntheticCaptures = syntheticCaptureAlternationSet?.FindSyntheticCaptures(haystack, startAt);
        if (syntheticCaptures is not null)
        {
            return syntheticCaptures;
        }

        if (anchoredWordCaptureEngine is not null)
        {
            return anchoredWordCaptureEngine.MatchAt(haystack, Math.Clamp(startAt, 0, haystack.Length));
        }

        if (anchoredRunBoundaryCaptureEngine is not null)
        {
            return anchoredRunBoundaryCaptureEngine.MatchAt(haystack, Math.Clamp(startAt, 0, haystack.Length));
        }

        if (anchoredDotStarCaptureEngine is not null)
        {
            return anchoredDotStarCaptureEngine.MatchAt(haystack, startAt);
        }

        if (anchoredQuotedStringCaptureEngine is not null)
        {
            return anchoredQuotedStringCaptureEngine.MatchAt(haystack, startAt);
        }

        if (scalarRunCaptureEngine is not null)
        {
            return scalarRunCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (keywordWhitespaceCaptureEngine is not null)
        {
            return keywordWhitespaceCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (noqaCaptureEngine is not null)
        {
            return noqaCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (operatorSpacingCaptureEngine is not null)
        {
            return operatorSpacingCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (fixedByteSequenceCaptureEngine is not null)
        {
            return fixedByteSequenceCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (literalWordCaptureEngine is not null)
        {
            return literalWordCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (pathSemverCaptureEngine is not null)
        {
            return pathSemverCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (asciiLetterLengthAlternationCaptureEngine is not null)
        {
            return asciiLetterLengthAlternationCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (asciiWordLengthAlternationCaptureEngine is not null)
        {
            return asciiWordLengthAlternationCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (bibleReferenceCaptureEngine is not null)
        {
            return bibleReferenceCaptureEngine.FindCaptures(haystack, startAt);
        }

        if (fnPredicateCaptureEngine is not null)
        {
            return RegexFnPredicateCaptureEngine.MatchAt(haystack, startAt);
        }

        return FindGenericCaptures(haystack, startAt);
    }

    private long CountCapturesWithFind(ReadOnlySpan<byte> haystack, int startAt)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suppressedEmptyStart = -1;
        while (offset <= haystack.Length)
        {
            RegexCaptures? captures = FindCaptures(haystack, offset);
            if (captures is null)
            {
                return total;
            }

            RegexMatch match = captures.Match;
            if (match.Length == 0 && match.Start == suppressedEmptyStart)
            {
                offset = Math.Min(match.Start + 1, haystack.Length + 1);
                suppressedEmptyStart = -1;
                continue;
            }

            total += captures.ParticipatingCount();
            if (match.Length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(match.End + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = Math.Min(match.End, haystack.Length + 1);
                offset = suppressedEmptyStart;
            }
        }

        return total;
    }

    private static bool AllowsDomainRecognizers(RegexCompileOptions options)
    {
        return options.SpecializationMode == RegexSpecializationMode.Default;
    }

    private static bool AllowsBenchmarkFamilyRecognizers(RegexCompileOptions options)
    {
        return options.SpecializationMode == RegexSpecializationMode.Default;
    }

    private static bool AllowsCorpusSpecificCaptureRecognizers(RegexCompileOptions options)
    {
        return options.SpecializationMode == RegexSpecializationMode.Default;
    }

    private RegexCaptures? FindWholePatternCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        RegexMatch? match = engine.Find(haystack, startAt, startPredicate);
        if (!match.HasValue)
        {
            return null;
        }

        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match.Value;
        groups[wholePatternCaptureIndex] = match.Value;
        return new RegexCaptures(match.Value, groups);
    }

    private RegexCaptures? FindGenericCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        RegexMatch? match = engine.Find(haystack, startAt, startPredicate);
        if (!match.HasValue)
        {
            return null;
        }

        if (captureEnginePool is null)
        {
            return new RegexCaptures(match.Value, [match.Value]);
        }

        RegexCaptureEngine? captureEngine = captureEnginePool.Rent();
        if (captureEngine is null)
        {
            return null;
        }

        try
        {
            return captureEngine.MatchAt(haystack, match.Value.Start, match.Value.End);
        }
        finally
        {
            captureEnginePool.Return(captureEngine);
        }
    }

    private static bool HasHigherPriorityFixedWidthSpecialization(RegexSyntaxNode root, RegexCompileOptions options)
    {
        bool allowDomainRecognizers = AllowsDomainRecognizers(options);
        bool allowBenchmarkFamilyRecognizers = AllowsBenchmarkFamilyRecognizers(options);
        if (RegexWholeLineEngine.TryCreate(root, options, out _) ||
            RegexDotStarEngine.TryCreate(root, options, out _) ||
            allowDomainRecognizers && RegexIpv4AddressEngine.TryCreate(root, options, out _) ||
            allowDomainRecognizers && RegexEmailAddressEngine.TryCreate(root, options, out _) ||
            allowBenchmarkFamilyRecognizers && RegexLh3EmailEngine.TryCreate(root, options, out _) ||
            allowDomainRecognizers && RegexUriEngine.TryCreate(root, options, out _) ||
            allowBenchmarkFamilyRecognizers && RegexLh3UriEngine.TryCreate(root, options, out _) ||
            allowBenchmarkFamilyRecognizers && RegexLh3UriOrEmailEngine.TryCreate(root, options, out _) ||
            RegexBoundedDigitDelimiterEngine.TryCreate(root, options, out _) ||
            RegexWordWhitespaceLiteralEngine.TryCreate(root, options, out _) ||
            RegexUnicodeWordWhitespaceLiteralEngine.TryCreate(root, options, out _) ||
            RegexBoundedLetterSuffixWhitespaceEngine.TryCreate(root, options, out _) ||
            RegexRunLiteralDotStarEngine.TryCreate(root, options, out _) ||
            RegexLiteralPrefixRunEngine.TryCreate(root, options, out _) ||
            RegexBoundedLiteralGapEngine.TryCreate(root, options, out _) ||
            RegexBoundedLineLiteralGapEngine.TryCreate(root, options, out _) ||
            RegexAnchoredLineLiteralGapEngine.TryCreate(root, options, out _) ||
            RegexBoundedPrefixLiteralSetEngine.TryCreate(root, options, out _) ||
            RegexRepeatedLazyDotStarLiteralEngine.TryCreate(root, options, out _) ||
            RegexDelimitedSpanEngine.TryCreate(root, options, out _))
        {
            return true;
        }

        return false;
    }

    private static bool CanSkipHigherPriorityFixedWidthGuards(RegexSyntaxNode root, RegexCompileOptions options)
    {
        return IsSimpleFixedWidthAtomTree(root, options);
    }

    private static bool IsSimpleFixedWidthAtomTree(RegexSyntaxNode root, RegexCompileOptions options)
    {
        root = UnwrapTransparentGroups(root);
        switch (root)
        {
            case RegexSequenceNode sequence:
                return IsSimpleFixedWidthAtomSequence(sequence, options);

            case RegexAlternationNode alternation:
                if (alternation.Alternatives.Count == 0)
                {
                    return false;
                }

                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (!IsSimpleFixedWidthAtomTree(alternation.Alternatives[index], options))
                    {
                        return false;
                    }
                }

                return true;

            case RegexGroupNode group:
                RegexCompileOptions groupOptions = options.Apply(group.EnabledFlags, group.DisabledFlags);
                return !groupOptions.CaseInsensitive &&
                    !groupOptions.Utf8 &&
                    IsSimpleFixedWidthAtomTree(group.Child, groupOptions);

            case RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom:
                return atom.Value.Length == 1;

            case RegexAtomNode atom:
                return !IsPredicateAtom(atom.Kind) &&
                    !RegexByteClass.RequiresUtf8ScalarMatch(
                        atom.Kind,
                        atom.Value.Span,
                        options.Utf8,
                        options.CaseInsensitive,
                        options.UnicodeClasses);

            default:
                return false;
        }
    }

    private static bool IsSimpleFixedWidthAtomSequence(RegexSequenceNode sequence, RegexCompileOptions options)
    {
        if (sequence.Nodes.Count == 0)
        {
            return false;
        }

        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = sequence.Nodes[index];
            if (node is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                if (currentOptions.CaseInsensitive || currentOptions.Utf8)
                {
                    return false;
                }

                continue;
            }

            if (!IsSimpleFixedWidthAtomTree(node, currentOptions))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPredicateAtom(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary;
    }

    private static bool HasHigherPriorityDelimitedSpanSpecialization(RegexSyntaxNode root, RegexCompileOptions options)
    {
        bool allowDomainRecognizers = AllowsDomainRecognizers(options);
        bool allowBenchmarkFamilyRecognizers = AllowsBenchmarkFamilyRecognizers(options);
        if (RegexWholeLineEngine.TryCreate(root, options, out _) ||
            RegexDotStarEngine.TryCreate(root, options, out _) ||
            allowDomainRecognizers && RegexIpv4AddressEngine.TryCreate(root, options, out _) ||
            allowDomainRecognizers && RegexEmailAddressEngine.TryCreate(root, options, out _) ||
            allowBenchmarkFamilyRecognizers && RegexLh3EmailEngine.TryCreate(root, options, out _) ||
            allowDomainRecognizers && RegexUriEngine.TryCreate(root, options, out _) ||
            allowBenchmarkFamilyRecognizers && RegexLh3UriEngine.TryCreate(root, options, out _) ||
            allowBenchmarkFamilyRecognizers && RegexLh3UriOrEmailEngine.TryCreate(root, options, out _) ||
            RegexBoundedDigitDelimiterEngine.TryCreate(root, options, out _) ||
            RegexWordWhitespaceLiteralEngine.TryCreate(root, options, out _) ||
            RegexUnicodeWordWhitespaceLiteralEngine.TryCreate(root, options, out _) ||
            RegexBoundedLetterSuffixWhitespaceEngine.TryCreate(root, options, out _) ||
            RegexRunLiteralDotStarEngine.TryCreate(root, options, out _) ||
            RegexLiteralPrefixRunEngine.TryCreate(root, options, out _) ||
            RegexBoundedLiteralGapEngine.TryCreate(root, options, out _) ||
            RegexBoundedLineLiteralGapEngine.TryCreate(root, options, out _) ||
            RegexAnchoredLineLiteralGapEngine.TryCreate(root, options, out _) ||
            RegexBoundedPrefixLiteralSetEngine.TryCreate(root, options, out _) ||
            RegexBoundedScalarClassSequenceEngine.TryCreate(root, options, out _) ||
            RegexBoundedByteClassSequenceEngine.TryCreate(root, options, out _) ||
            RegexRepeatedLazyDotStarLiteralEngine.TryCreate(root, options, out _))
        {
            return true;
        }

        return false;
    }

    internal static int TryGetWholePatternCaptureIndex(RegexSyntaxNode root, int captureCount)
    {
        if (captureCount != 1)
        {
            return 0;
        }

        RegexSyntaxNode node = UnwrapWholePatternNonCapturingGroups(root);
        return node is RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group &&
            group.CaptureIndex > 0 &&
            !HasCapturingGroup(group.Child)
            ? group.CaptureIndex
            : 0;
    }

    private static RegexSyntaxNode UnwrapWholePatternNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group)
        {
            node = group.Child;
        }

        return node;
    }

    private static bool HasCapturingGroup(RegexSyntaxNode node)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.CapturingGroup:
                return true;
            case RegexSyntaxKind.NonCapturingGroup:
                return HasCapturingGroup(((RegexGroupNode)node).Child);
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (HasCapturingGroup(sequence.Nodes[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.Alternation:
                var alternation = (RegexAlternationNode)node;
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (HasCapturingGroup(alternation.Alternatives[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.Repetition:
                return HasCapturingGroup(((RegexRepetitionNode)node).Child);
            default:
                return false;
        }
    }
}
