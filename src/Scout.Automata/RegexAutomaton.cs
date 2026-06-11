namespace Scout;

/// <summary>
/// Executes a byte-oriented regular expression automaton.
/// </summary>
public sealed class RegexAutomaton
    {
        private readonly RegexMetaEngine engine;
        private readonly RegexStartPredicate? startPredicate;
        private readonly object captureInitializationLock = new();
        private readonly ReadOnlyMemory<byte> capturePattern;
        private readonly RegexSyntaxNode? captureRoot;
        private readonly RegexCompileOptions captureOptions;
        private readonly RegexPrefilter? capturePrefilter;
        private readonly int captureCount;

        private RegexCaptureEngine? captureEngine;
        private RegexAlternationSetEngine? syntheticCaptureAlternationSet;
        private RegexDelimitedCaptureEngine? delimitedCaptureEngine;
        private RegexStructuredLogCaptureEngine? structuredLogCaptureEngine;
        private volatile bool captureEnginesInitialized;

        private RegexAutomaton(
            RegexMetaEngine engine,
            RegexStartPredicate? startPredicate,
            RegexAlternationSetEngine? syntheticCaptureAlternationSet,
            ReadOnlyMemory<byte> capturePattern,
            RegexSyntaxNode? captureRoot,
            RegexCompileOptions captureOptions,
            RegexPrefilter? capturePrefilter,
            int captureCount)
        {
            this.engine = engine;
            this.startPredicate = startPredicate;
            this.syntheticCaptureAlternationSet = syntheticCaptureAlternationSet;
            this.capturePattern = capturePattern;
            this.captureRoot = captureRoot;
            this.captureOptions = captureOptions;
            this.capturePrefilter = capturePrefilter;
            this.captureCount = captureCount;
            captureEnginesInitialized = captureCount == 0;
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
        ulong? dfaSizeLimit = null)
    {
        var options = new RegexCompileOptions(caseInsensitive, swapGreed: false, multiLine, dotMatchesNewline, crlf, lineTerminator, utf8, unicodeClasses);
        if (RegexLiteralSetEngine.TryCreateLiteralAlternation(pattern, options, out RegexLiteralSetEngine? literalSet) &&
            literalSet is not null)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileLiteralSet(literalSet, options.Utf8),
                startPredicate: null,
                syntheticCaptureAlternationSet: null,
                capturePattern: default,
                captureRoot: null,
                captureOptions: default,
                capturePrefilter: null,
                captureCount: 0);
        }

        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        RegexLiteralSetEngine.TryCreate(tree.Root, options, out literalSet);
        if (literalSet is not null && tree.CaptureCount == 0)
        {
            return new RegexAutomaton(
                RegexMetaEngine.CompileLiteralSet(literalSet, options.Utf8),
                startPredicate: null,
                syntheticCaptureAlternationSet: null,
                capturePattern: default,
                captureRoot: null,
                captureOptions: default,
                capturePrefilter: null,
                captureCount: 0);
        }

        RegexNfa nfa = RegexNfaCompiler.Compile(
            tree.Root,
            options);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        RegexAlternationSetEngine.TryCreate(pattern, tree.Root, tree.CaptureCount, options, out RegexAlternationSetEngine? alternationSet);
        RegexAlternationSetEngine? syntheticCaptureAlternationSet = alternationSet?.CanSynthesizeCaptures == true
            ? alternationSet
            : null;

        RegexSimpleSequenceEngine.TryCreate(tree.Root, options, out RegexSimpleSequenceEngine? simpleSequence);
        RegexLineContainsEngine.TryCreate(tree.Root, options, out RegexLineContainsEngine? lineContains);
        RegexDotStarClassFallbackEngine.TryCreate(tree.Root, options, out RegexDotStarClassFallbackEngine? dotStarClassFallback);
        RegexScalarRunEngine.TryCreate(tree.Root, options, out RegexScalarRunEngine? scalarRun);
        RegexAsciiWordBoundaryEngine.TryCreate(tree.Root, options, out RegexAsciiWordBoundaryEngine? asciiWordBoundary);
        RegexAsciiFastPath.TryCompileNfa(pattern, tree.Root, options, out RegexNfa? asciiFastNfa);
        RegexStartPredicate.TryCreate(tree.Root, options, out RegexStartPredicate? startPredicate);

        return new RegexAutomaton(
            RegexMetaEngine.Compile(nfa, prefilter, dfaSizeLimit, literalSet, alternationSet, simpleSequence, lineContains, dotStarClassFallback, asciiFastNfa, scalarRun, asciiWordBoundary),
            startPredicate,
            syntheticCaptureAlternationSet,
            tree.CaptureCount > 0 ? tree.Pattern : default,
            tree.CaptureCount > 0 ? tree.Root : null,
            options,
            prefilter,
            tree.CaptureCount);
    }

    internal RegexPrefilterKind PrefilterKind => engine.PrefilterKind;

    internal bool UsesSyntheticCaptureAlternationSet
    {
        get
        {
            EnsureCaptureEngines();
            return syntheticCaptureAlternationSet?.CanSynthesizeCaptures == true;
        }
    }

    internal bool UsesStructuredLogCaptureEngine
    {
        get
        {
            EnsureCaptureEngines();
            return structuredLogCaptureEngine is not null;
        }
    }

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
        return engine.Find(haystack, startAt, startPredicate);
    }

    internal RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
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

    /// <summary>
    /// Finds the earliest-ending match in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The earliest match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? FindEarliest(ReadOnlySpan<byte> haystack, int startAt)
    {
        return engine.FindEarliest(haystack, startAt);
    }

    internal RegexMatch? FindAllKindAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        return engine.FindAllKindAt(haystack, startAt);
    }

    internal IReadOnlyList<RegexMatch> FindOverlappingAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        return engine.FindOverlappingAt(haystack, startAt);
    }

    /// <summary>
    /// Returns a value indicating whether the regex matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when a match exists.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return Find(haystack).HasValue;
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
        return engine.SumMatchSpans(haystack, startAt, startPredicate);
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

            if (captureRoot is not null && captureCount > 0)
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
                    out delimitedCaptureEngine);
                RegexStructuredLogCaptureEngine.TryCreate(
                    captureRoot,
                    captureOptions,
                    captureCount,
                    out structuredLogCaptureEngine);
                if (syntheticCaptureAlternationSet is null)
                {
                    RegexNfa captureNfa = RegexNfaCompiler.CompileCaptures(captureRoot, captureOptions, captureCount);
                    captureEngine = new RegexCaptureEngine(captureNfa, capturePrefilter);
                }
            }

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
        EnsureCaptureEngines();

        if (structuredLogCaptureEngine is not null)
        {
            RegexCaptures? structuredCaptures = RegexStructuredLogCaptureEngine.MatchAt(haystack, Math.Clamp(startAt, 0, haystack.Length));
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

        RegexCaptures? syntheticCaptures = syntheticCaptureAlternationSet?.FindSyntheticCaptures(haystack, startAt);
        if (syntheticCaptures is not null)
        {
            return syntheticCaptures;
        }

        RegexMatch? match = engine.Find(haystack, startAt, startPredicate);
        if (!match.HasValue)
        {
            return null;
        }

        if (captureEngine is null)
        {
            return new RegexCaptures(match.Value, [match.Value]);
        }

        return captureEngine.MatchAt(haystack, match.Value.Start);
    }
}
