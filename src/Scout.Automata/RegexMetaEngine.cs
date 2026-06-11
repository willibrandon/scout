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
    private const ulong DefaultDfaSizeLimit = 2UL * 1024UL * 1024UL;

    private readonly PikeVm? pikeVm;
    private readonly RegexBoundedBacktracker? boundedBacktracker;
    private readonly RegexOnePassDfa? onePassDfa;
    private readonly RegexDenseDfa? denseDfa;
    private readonly RegexSparseDfa? sparseDfa;
    private readonly RegexLazyDfa? lazyDfa;
    private readonly RegexLazyDfa? asciiFastDfa;
    private readonly RegexLiteralSetEngine? literalSet;
    private readonly RegexAlternationSetEngine? alternationSet;
    private readonly RegexSimpleSequenceEngine? simpleSequence;
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
    private RegexNfa? nfa;
    private RegexUnanchoredLazyDfa? unanchoredLazyDfa;
    private RegexUnanchoredLazyDfa? asciiFastUnanchoredDfa;
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
        Func<RegexUnanchoredLazyDfa?>? unanchoredLazyDfaFactory = null)
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
        this.asciiFastUnanchoredDfaFactory = asciiFastUnanchoredDfaFactory;
        this.unanchoredLazyDfaFactory = unanchoredLazyDfaFactory;
        this.literalSet = literalSet;
        this.alternationSet = alternationSet;
        this.simpleSequence = simpleSequence;
        this.lineContains = lineContains;
        this.dotStarClassFallback = dotStarClassFallback;
        this.scalarRun = scalarRun;
        this.asciiWordBoundary = asciiWordBoundary;
        this.prefilter = prefilter;
        this.utf8 = utf8;
    }

    public RegexEngineKind Kind { get; }

    public RegexPrefilterKind PrefilterKind => prefilter?.Kind ?? RegexPrefilterKind.None;

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
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter: null,
            utf8,
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
        RegexSimpleSequenceEngine? simpleSequence = null,
        RegexLineContainsEngine? lineContains = null,
        RegexDotStarClassFallbackEngine? dotStarClassFallback = null,
        RegexNfa? asciiFastNfa = null,
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
                simpleSequence: null,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
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
                simpleSequence,
                lineContains: null,
                dotStarClassFallback: null,
                prefilter,
                nfa.Utf8);
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
                    simpleSequence: null,
                    lineContains: null,
                    dotStarClassFallback: null,
                    prefilter,
                    nfa.Utf8);
            }

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
            simpleSequence: null,
            lineContains: null,
            dotStarClassFallback: null,
            prefilter,
            nfa.Utf8,
            unanchoredLazyDfaFactory: unanchoredLazyDfaFactory);
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

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate = null)
    {
        return Find(haystack, startAt, startPredicate, reachabilityCache: null);
    }

    private RegexMatch? Find(
        ReadOnlySpan<byte> haystack,
        int startAt,
        RegexStartPredicate? startPredicate,
        Dictionary<(int State, int Position), bool>? reachabilityCache)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (literalSet is not null)
        {
            return literalSet.Find(haystack, startOffset);
        }

        if (alternationSet is not null)
        {
            return alternationSet.Find(haystack, startOffset);
        }

        if (simpleSequence is not null && prefilter is null)
        {
            return simpleSequence.Find(haystack, startOffset);
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
            int nextStartToTry = startOffset;
            for (int requiredAt = prefilter.FindRequiredLiteral(haystack, startOffset);
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

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate = null)
    {
        if (literalSet is not null)
        {
            return literalSet.CountMatches(haystack, startAt);
        }

        if (alternationSet is not null)
        {
            return alternationSet.CountMatches(haystack, startAt);
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

        if (TryCountNonOverlapping(haystack, startAt, out long count, out _))
        {
            return count;
        }

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = GetUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null &&
            activeUnanchoredLazyDfa.TryCountMatches(haystack, startAt, out long unanchoredCount))
        {
            return unanchoredCount;
        }

        if (TryIterateAsciiFastUnanchored(haystack, startAt, sumSpans: false, out long asciiFastCount))
        {
            return asciiFastCount;
        }

        return IterateNonOverlapping(haystack, startAt, startPredicate, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt, RegexStartPredicate? startPredicate = null)
    {
        if (literalSet is not null)
        {
            return literalSet.SumMatchSpans(haystack, startAt);
        }

        if (alternationSet is not null)
        {
            return alternationSet.SumMatchSpans(haystack, startAt);
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

        if (TryCountNonOverlapping(haystack, startAt, out _, out long spanSum))
        {
            return spanSum;
        }

        RegexUnanchoredLazyDfa? activeUnanchoredLazyDfa = GetUnanchoredLazyDfa(haystack.Length);
        if (activeUnanchoredLazyDfa is not null &&
            activeUnanchoredLazyDfa.TrySumMatchSpans(haystack, startAt, out long unanchoredSpanSum))
        {
            return unanchoredSpanSum;
        }

        if (TryIterateAsciiFastUnanchored(haystack, startAt, sumSpans: true, out long asciiFastSpanSum))
        {
            return asciiFastSpanSum;
        }

        return IterateNonOverlapping(haystack, startAt, startPredicate, sumSpans: true);
    }

    private bool TryCountNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, out long count, out long spanSum)
    {
        if (simpleSequence is not null)
        {
            return simpleSequence.TryCountNonOverlapping(haystack, startAt, out count, out spanSum);
        }

        if (scalarRun is not null)
        {
            return scalarRun.TryCountNonOverlapping(haystack, startAt, out count, out spanSum);
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

    private bool TryIterateAsciiFastUnanchored(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans, out long total)
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
                return !gaveUp && IsAsciiRange(haystack, offset, haystack.Length);
            }

            if (!CanAcceptAsciiFastUnanchored(haystack, offset, match))
            {
                return false;
            }

            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return true;
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
        if (literalSet is not null)
        {
            return literalSet.MatchAt(haystack, startOffset);
        }

        if (alternationSet is not null)
        {
            return alternationSet.MatchAt(haystack, startOffset);
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

    public RegexMatch? FindEarliest(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (literalSet is not null)
        {
            return literalSet.FindEarliest(haystack, startOffset);
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
        if (literalSet is not null)
        {
            return literalSet.FindAllKindAt(haystack, startOffset);
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
        if (literalSet is not null)
        {
            return literalSet.FindOverlappingAt(haystack, startOffset);
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
        if (utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
        {
            length = 0;
            return false;
        }

        if (simpleSequence is not null)
        {
            return simpleSequence.TryMatchAt(haystack, start, out length);
        }

        if (scalarRun is not null)
        {
            return scalarRun.TryMatchAt(haystack, start, out length);
        }

        if (asciiWordBoundary is not null)
        {
            return asciiWordBoundary.TryMatchAt(haystack, start, out length);
        }

        if (lazyDfa is not null)
        {
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
}
