namespace Scout;

/// <summary>
/// Executes a byte-oriented regular expression automaton.
/// </summary>
public sealed class RegexAutomaton
    {
        private readonly RegexMetaEngine engine;
        private readonly RegexCaptureEngine captureEngine;
        private readonly RegexDelimitedCaptureEngine? delimitedCaptureEngine;
        private readonly RegexStartPredicate? startPredicate;

        private RegexAutomaton(
            RegexMetaEngine engine,
            RegexCaptureEngine captureEngine,
            RegexDelimitedCaptureEngine? delimitedCaptureEngine,
            RegexStartPredicate? startPredicate)
        {
            this.engine = engine;
            this.captureEngine = captureEngine;
            this.delimitedCaptureEngine = delimitedCaptureEngine;
            this.startPredicate = startPredicate;
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
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        var options = new RegexCompileOptions(caseInsensitive, swapGreed: false, multiLine, dotMatchesNewline, crlf, lineTerminator, utf8, unicodeClasses);
        RegexNfa nfa = RegexNfaCompiler.Compile(
            tree.Root,
            options);
        RegexNfa captureNfa = RegexNfaCompiler.CompileCaptures(tree.Root, options, tree.CaptureCount);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        RegexLiteralSetEngine.TryCreate(tree.Root, options, out RegexLiteralSetEngine? literalSet);
        RegexAlternationSetEngine.TryCreate(pattern, tree.Root, tree.CaptureCount, options, out RegexAlternationSetEngine? alternationSet);
        RegexSimpleSequenceEngine.TryCreate(tree.Root, options, out RegexSimpleSequenceEngine? simpleSequence);
        RegexLineContainsEngine.TryCreate(tree.Root, options, out RegexLineContainsEngine? lineContains);
        RegexDotStarClassFallbackEngine.TryCreate(tree.Root, options, out RegexDotStarClassFallbackEngine? dotStarClassFallback);
        RegexScalarRunEngine.TryCreate(tree.Root, options, out RegexScalarRunEngine? scalarRun);
        RegexDelimitedCaptureEngine.TryCreate(tree.Root, options, tree.CaptureCount, out RegexDelimitedCaptureEngine? delimitedCaptureEngine);
        RegexAsciiFastPath.TryCompileNfa(pattern, tree.Root, options, out RegexNfa? asciiFastNfa);
        RegexStartPredicate.TryCreate(tree.Root, options, out RegexStartPredicate? startPredicate);
        return new RegexAutomaton(
            RegexMetaEngine.Compile(nfa, prefilter, dfaSizeLimit, literalSet, alternationSet, simpleSequence, lineContains, dotStarClassFallback, asciiFastNfa, scalarRun),
            new RegexCaptureEngine(captureNfa, prefilter),
            delimitedCaptureEngine,
            startPredicate);
    }

    internal RegexPrefilterKind PrefilterKind => engine.PrefilterKind;

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

    /// <summary>
    /// Finds the first match and its participating capture groups in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The first capture result, or <see langword="null" /> when no match exists.</returns>
    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt = 0)
    {
        RegexCaptures? delimitedCaptures = delimitedCaptureEngine?.MatchAt(haystack, Math.Clamp(startAt, 0, haystack.Length));
        if (delimitedCaptures is not null)
        {
            return delimitedCaptures;
        }

        RegexCaptures? syntheticCaptures = engine.FindSyntheticCaptures(haystack, startAt);
        if (syntheticCaptures is not null)
        {
            return syntheticCaptures;
        }

        RegexMatch? match = engine.Find(haystack, startAt, startPredicate);
        if (!match.HasValue)
        {
            return null;
        }

        return captureEngine.MatchAt(haystack, match.Value.Start);
    }
}
