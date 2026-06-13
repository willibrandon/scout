namespace Scout;

/// <summary>
/// Represents an ordered multi-regex set.
/// </summary>
public sealed class PatternSet
{
    private readonly RegexAutomaton[] automata;
    private readonly int[] automataPatternIds;
    private readonly int[] allAutomataIndexes;
    private readonly int[][]? exactAutomataByFirstByte;
    private readonly PatternSetLiteralAccelerator? literalAccelerator;
    private readonly PatternSetBoundaryLiteralAccelerator? boundaryLiteralAccelerator;
    private readonly PatternSetRequiredLiteralAccelerator? requiredLiteralAccelerator;
    private readonly PatternSetAnchoredMatcher[][]? anchoredMatchersByFirstByte;
    private readonly bool coversEveryByteWithPositiveWidth;
    private readonly int count;

    private PatternSet(
        int count,
        RegexAutomaton[] automata,
        int[] automataPatternIds,
        int[] allAutomataIndexes,
        int[][]? exactAutomataByFirstByte,
        PatternSetLiteralAccelerator? literalAccelerator,
        PatternSetBoundaryLiteralAccelerator? boundaryLiteralAccelerator,
        PatternSetRequiredLiteralAccelerator? requiredLiteralAccelerator,
        PatternSetAnchoredMatcher[][]? anchoredMatchersByFirstByte,
        bool coversEveryByteWithPositiveWidth)
    {
        this.count = count;
        this.automata = automata;
        this.automataPatternIds = automataPatternIds;
        this.allAutomataIndexes = allAutomataIndexes;
        this.exactAutomataByFirstByte = exactAutomataByFirstByte;
        this.literalAccelerator = literalAccelerator;
        this.boundaryLiteralAccelerator = boundaryLiteralAccelerator;
        this.requiredLiteralAccelerator = requiredLiteralAccelerator;
        this.anchoredMatchersByFirstByte = anchoredMatchersByFirstByte;
        this.coversEveryByteWithPositiveWidth = coversEveryByteWithPositiveWidth;
    }

    /// <summary>
    /// Gets the number of patterns in the set.
    /// </summary>
    public int Count => count;

    internal bool UsesLiteralAccelerator => literalAccelerator is not null;

    internal bool UsesBoundaryLiteralAccelerator => boundaryLiteralAccelerator is not null;

    internal bool UsesRequiredLiteralAccelerator => requiredLiteralAccelerator is not null;

    internal bool RequiredLiteralAcceleratorCoversAll => requiredLiteralAccelerator?.CoversAllAutomata == true;

    internal bool CanAccelerateEveryPattern => automata.Length == 0 || RequiredLiteralAcceleratorCoversAll;

    internal bool UsesAnchoredMatcherAccelerator => anchoredMatchersByFirstByte is not null;

    internal bool CoversEveryByteWithPositiveWidth => coversEveryByteWithPositiveWidth;

    internal static bool CanPreflightAccelerateEveryPattern(IReadOnlyList<byte[]> patterns, RegexCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index] ?? throw new ArgumentNullException(nameof(patterns));
            if (!TryCreatePatternPlan(pattern, options, requireAcceleration: true, out _))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryCompileAccelerated(
        IReadOnlyList<byte[]> patterns,
        RegexCompileOptions options,
        out PatternSet? patternSet)
    {
        return TryCompile(patterns, options, requireFullAcceleration: true, out patternSet);
    }

    /// <summary>
    /// Compiles an ordered set of byte regex patterns.
    /// </summary>
    /// <param name="patterns">The ordered pattern bytes.</param>
    /// <returns>The compiled set.</returns>
    public static PatternSet Compile(IReadOnlyList<byte[]> patterns)
    {
        return Compile(
            patterns,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false);
    }

    /// <summary>
    /// Compiles an ordered set of byte regex patterns with root regex options.
    /// </summary>
    /// <param name="patterns">The ordered pattern bytes.</param>
    /// <param name="caseInsensitive">Whether literal and class atoms match ASCII case-insensitively.</param>
    /// <param name="multiLine">Whether <c>^</c> and <c>$</c> match adjacent to line feeds.</param>
    /// <param name="dotMatchesNewline">Whether <c>.</c> matches line feeds.</param>
    /// <param name="crlf">Whether CRLF mode treats carriage returns and line feeds as line terminators.</param>
    /// <param name="lineTerminator">The line terminator byte used when CRLF mode is disabled.</param>
    /// <param name="utf8">Whether empty and scalar-consuming matches must respect UTF-8 code point boundaries.</param>
    /// <param name="unicodeClasses">Whether Perl classes and word-boundary assertions use Unicode word definitions.</param>
    /// <returns>The compiled set.</returns>
    public static PatternSet Compile(
        IReadOnlyList<byte[]> patterns,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf = false,
        byte lineTerminator = (byte)'\n',
        bool utf8 = true,
        bool unicodeClasses = true)
    {
        var options = new RegexCompileOptions(caseInsensitive, swapGreed: false, multiLine, dotMatchesNewline, crlf, lineTerminator, utf8, unicodeClasses);
        TryCompile(patterns, options, requireFullAcceleration: false, out PatternSet? patternSet);
        return patternSet!;
    }

    private static bool TryCompile(
        IReadOnlyList<byte[]> patterns,
        RegexCompileOptions options,
        bool requireFullAcceleration,
        out PatternSet? patternSet)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        patternSet = null;

        var plans = new PatternSetPatternPlan[patterns.Count];
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index] ?? throw new ArgumentNullException(nameof(patterns));
            if (!TryCreatePatternPlan(pattern, options, requireFullAcceleration, out plans[index]))
            {
                return false;
            }
        }

        var automata = new List<RegexAutomaton>();
        var automataPatternIds = new List<int>();
        var literalPatterns = new List<byte[]>();
        var literalPatternIds = new List<int>();
        var boundaryLiteralPatterns = new List<byte[]>();
        var boundaryLiteralPatternIds = new List<int>();
        var requiredLiteralEntries = new List<PatternSetRequiredLiteralEntry>();
        var utf8ByteTrieCache = new Dictionary<string, RegexUtf8ByteTrie>();
        for (int index = 0; index < plans.Length; index++)
        {
            PatternSetPatternPlan plan = plans[index];
            if (plan.LiteralPatterns is not null)
            {
                for (int literalIndex = 0; literalIndex < plan.LiteralPatterns.Length; literalIndex++)
                {
                    literalPatterns.Add(plan.LiteralPatterns[literalIndex]);
                    literalPatternIds.Add(index);
                }

                continue;
            }

            if (plan.BoundaryLiteralPatterns is not null)
            {
                for (int literalIndex = 0; literalIndex < plan.BoundaryLiteralPatterns.Length; literalIndex++)
                {
                    boundaryLiteralPatterns.Add(plan.BoundaryLiteralPatterns[literalIndex]);
                    boundaryLiteralPatternIds.Add(index);
                }

                continue;
            }

            if (plan.RequiredLiterals is not null)
            {
                requiredLiteralEntries.Add(new PatternSetRequiredLiteralEntry(
                    automata.Count,
                    plan.RequiredLiterals,
                    plan.RequiredLiteralLookBehind));
            }

            automata.Add(RegexAutomaton.CompileParsedWithCache(
                plan.Tree!,
                options,
                dfaSizeLimit: null,
                compilePrefilter: plan.RequiredLiterals is null,
                utf8ByteTrieCache: utf8ByteTrieCache));
            automataPatternIds.Add(index);
        }

        PatternSetLiteralAccelerator? literalAccelerator = literalPatterns.Count == 0
            ? null
            : new PatternSetLiteralAccelerator(literalPatterns, literalPatternIds, options.CaseInsensitive);
        PatternSetBoundaryLiteralAccelerator? boundaryLiteralAccelerator = boundaryLiteralPatterns.Count == 0
            ? null
            : new PatternSetBoundaryLiteralAccelerator(boundaryLiteralPatterns, boundaryLiteralPatternIds, options);
        RegexAutomaton[] compiledAutomata = automata.ToArray();
        PatternSetRequiredLiteralAccelerator? requiredLiteralAccelerator = requiredLiteralEntries.Count == 0
            ? null
            : new PatternSetRequiredLiteralAccelerator(requiredLiteralEntries, compiledAutomata.Length, compiledAutomata);
        bool coversEveryByteWithPositiveWidth = DetectEveryBytePositiveWidthCoverage(patterns, plans, options);
        patternSet = new PatternSet(
            patterns.Count,
            compiledAutomata,
            automataPatternIds.ToArray(),
            BuildAllAutomataIndexes(compiledAutomata.Length),
            BuildExactAutomataByFirstByte(compiledAutomata),
            literalAccelerator,
            boundaryLiteralAccelerator,
            requiredLiteralAccelerator,
            coversEveryByteWithPositiveWidth
                ? BuildAnchoredMatchersByFirstByte(patterns, plans, options, compiledAutomata, automataPatternIds)
                : null,
            coversEveryByteWithPositiveWidth);
        return true;
    }

    private static bool TryCreatePatternPlan(
        byte[] pattern,
        RegexCompileOptions options,
        bool requireAcceleration,
        out PatternSetPatternPlan plan)
    {
        if (TryGetRawLiteralPattern(pattern, out byte[] rawLiteral) &&
            TryPrepareLiteralPatterns(rawLiteral, options, out byte[][] rawLiteralPatterns))
        {
            plan = new PatternSetPatternPlan(tree: null, rawLiteralPatterns, requiredLiterals: null, requiredLiteralLookBehind: 0);
            return true;
        }

        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        if (TryGetLiteralPattern(tree.Root, out byte[] literal) &&
            literal.Length != 0 &&
            TryPrepareLiteralPatterns(literal, options, out byte[][] preparedLiteralPatterns))
        {
            plan = new PatternSetPatternPlan(tree, preparedLiteralPatterns, requiredLiterals: null, requiredLiteralLookBehind: 0);
            return true;
        }

        if (TryGetBoundaryLiteralPattern(tree.Root, options, out byte[] boundaryLiteral))
        {
            plan = new PatternSetPatternPlan(tree, literalPatterns: null, boundaryLiteralPatterns: [boundaryLiteral], requiredLiterals: null, requiredLiteralLookBehind: 0);
            return true;
        }

        if (RegexPrefilter.TryCollectRequiredLiteralSet(tree.Root, options, out byte[][] requiredLiterals) &&
            requiredLiterals.Length > 0 &&
            RegexPrefilter.TryPrepareRequiredLiteralSet(requiredLiterals, options, out byte[][] preparedLiterals))
        {
            int maxLookBehind = TryGetRequiredLiteralLookBehind(tree.Root, options, requiredLiterals);
            plan = new PatternSetPatternPlan(tree, literalPatterns: null, preparedLiterals, maxLookBehind);
            return true;
        }

        if (RegexPrefilter.TryFindRequiredLiteral(tree.Root, options, out byte[] requiredLiteral) &&
            requiredLiteral.Length >= 2 &&
            RegexPrefilter.TryPrepareRequiredLiteralSet([requiredLiteral], options, out preparedLiterals))
        {
            plan = new PatternSetPatternPlan(
                tree,
                literalPatterns: null,
                preparedLiterals,
                RegexPrefilter.RequiredLiteralLookBehind);
            return true;
        }

        if (requireAcceleration)
        {
            plan = default;
            return false;
        }

        plan = new PatternSetPatternPlan(tree, literalPatterns: null, requiredLiterals: null, requiredLiteralLookBehind: 0);
        return true;
    }

    /// <summary>
    /// Returns a value indicating whether any pattern matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when at least one pattern matches.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        if (literalAccelerator is not null && literalAccelerator.IsMatch(haystack))
        {
            return true;
        }

        if (boundaryLiteralAccelerator is not null && boundaryLiteralAccelerator.IsMatch(haystack))
        {
            return true;
        }

        if (requiredLiteralAccelerator is not null &&
            requiredLiteralAccelerator.Find(haystack, 0, automata, automataPatternIds, best: null).HasValue)
        {
            return true;
        }

        for (int index = 0; index < automata.Length; index++)
        {
            if (requiredLiteralAccelerator?.CoversAutomaton(index) == true)
            {
                continue;
            }

            if (automata[index].IsMatch(haystack))
            {
                return true;
            }
        }

        return false;
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
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (literalAccelerator is not null &&
            boundaryLiteralAccelerator is null &&
            automata.Length == 0)
        {
            return literalAccelerator.CountMatches(haystack, startOffset);
        }

        if (literalAccelerator is null &&
            boundaryLiteralAccelerator is null &&
            requiredLiteralAccelerator is not null &&
            requiredLiteralAccelerator.CoversAllAutomata)
        {
            return requiredLiteralAccelerator.CountMatches(haystack, startOffset, automata, automataPatternIds);
        }

        return IterateNonOverlapping(haystack, startAt, sumSpans: false);
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
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (coversEveryByteWithPositiveWidth)
        {
            return haystack.Length - startOffset;
        }

        if (literalAccelerator is not null &&
            boundaryLiteralAccelerator is null &&
            automata.Length == 0)
        {
            return literalAccelerator.SumMatchSpans(haystack, startOffset);
        }

        return IterateNonOverlapping(haystack, startAt, sumSpans: true);
    }

    /// <summary>
    /// Finds the leftmost match across all patterns, using pattern order to break ties.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The selected match, or <see langword="null" /> when no pattern matches.</returns>
    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack)
    {
        return Find(haystack, startAt: 0);
    }

    /// <summary>
    /// Finds the leftmost match across all patterns at or after a byte offset, using pattern order to break ties.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The selected match, or <see langword="null" /> when no pattern matches.</returns>
    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        PatternSetMatch? anchoredMatch = FindWithAnchoredMatchers(haystack, startOffset);
        if (anchoredMatch.HasValue)
        {
            return anchoredMatch;
        }

        PatternSetMatch? exact = literalAccelerator?.FindAt(haystack, startOffset);
        PatternSetMatch? boundaryExact = boundaryLiteralAccelerator?.FindAt(haystack, startOffset);
        if (boundaryExact.HasValue && IsBetter(boundaryExact.Value, exact))
        {
            exact = boundaryExact;
        }

        ReadOnlySpan<int> exactAutomata = GetExactAutomataForStart(haystack, startOffset);
        for (int candidateIndex = 0; candidateIndex < exactAutomata.Length; candidateIndex++)
        {
            int index = exactAutomata[candidateIndex];
            if (exact.HasValue && automataPatternIds[index] >= exact.Value.PatternId)
            {
                break;
            }

            RegexMatch? match = automata[index].MatchAt(haystack, startOffset);
            if (!match.HasValue)
            {
                continue;
            }

            return new PatternSetMatch(automataPatternIds[index], match.Value);
        }

        if (exact.HasValue)
        {
            return exact;
        }

        PatternSetMatch? best = null;
        if (literalAccelerator is not null)
        {
            PatternSetMatch? literalMatch = literalAccelerator.Find(haystack[startOffset..]);
            if (literalMatch.HasValue)
            {
                best = new PatternSetMatch(
                    literalMatch.Value.PatternId,
                    new RegexMatch(literalMatch.Value.Match.Start + startOffset, literalMatch.Value.Match.Length));
            }
        }

        if (boundaryLiteralAccelerator is not null)
        {
            PatternSetMatch? boundaryLiteralMatch = boundaryLiteralAccelerator.Find(haystack[startOffset..]);
            if (boundaryLiteralMatch.HasValue)
            {
                var candidate = new PatternSetMatch(
                    boundaryLiteralMatch.Value.PatternId,
                    new RegexMatch(
                        boundaryLiteralMatch.Value.Match.Start + startOffset,
                        boundaryLiteralMatch.Value.Match.Length));
                if (IsBetter(candidate, best))
                {
                    best = candidate;
                }
            }
        }

        if (requiredLiteralAccelerator is not null)
        {
            best = requiredLiteralAccelerator.Find(haystack, startOffset, automata, automataPatternIds, best);
        }

        for (int index = 0; index < automata.Length; index++)
        {
            if (requiredLiteralAccelerator?.CoversAutomaton(index) == true)
            {
                continue;
            }

            RegexMatch? match = automata[index].Find(haystack, startOffset);
            if (!match.HasValue)
            {
                continue;
            }

            var candidate = new PatternSetMatch(automataPatternIds[index], match.Value);
            if (IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    private PatternSetMatch? FindWithAnchoredMatchers(ReadOnlySpan<byte> haystack, int startOffset)
    {
        if (anchoredMatchersByFirstByte is null || startOffset >= haystack.Length)
        {
            return null;
        }

        PatternSetAnchoredMatcher[] candidates = anchoredMatchersByFirstByte[haystack[startOffset]];
        for (int index = 0; index < candidates.Length; index++)
        {
            PatternSetAnchoredMatcher matcher = candidates[index];
            int length = matcher.MatchAt(haystack, startOffset);
            if (length >= 0)
            {
                return new PatternSetMatch(matcher.PatternId, new RegexMatch(startOffset, length));
            }
        }

        return null;
    }

    private ReadOnlySpan<int> GetExactAutomataForStart(ReadOnlySpan<byte> haystack, int startOffset)
    {
        if (exactAutomataByFirstByte is null || startOffset >= haystack.Length)
        {
            return allAutomataIndexes;
        }

        return exactAutomataByFirstByte[haystack[startOffset]];
    }

    private static int[][]? BuildExactAutomataByFirstByte(RegexAutomaton[] automata)
    {
        if (automata.Length == 0)
        {
            return null;
        }

        var buckets = new List<int>[256];
        for (int index = 0; index < buckets.Length; index++)
        {
            buckets[index] = [];
        }

        for (int automatonIndex = 0; automatonIndex < automata.Length; automatonIndex++)
        {
            bool[] firstBytes = new bool[256];
            if (!automata[automatonIndex].TryAddStartBytes(firstBytes))
            {
                for (int value = 0; value <= byte.MaxValue; value++)
                {
                    buckets[value].Add(automatonIndex);
                }

                continue;
            }

            for (int value = 0; value <= byte.MaxValue; value++)
            {
                if (firstBytes[value])
                {
                    buckets[value].Add(automatonIndex);
                }
            }
        }

        int[][] indexed = new int[256][];
        for (int index = 0; index < indexed.Length; index++)
        {
            indexed[index] = buckets[index].ToArray();
        }

        return indexed;
    }

    private static PatternSetAnchoredMatcher[][] BuildAnchoredMatchersByFirstByte(
        IReadOnlyList<byte[]> patterns,
        PatternSetPatternPlan[] plans,
        RegexCompileOptions options,
        RegexAutomaton[] automata,
        List<int> automataPatternIds)
    {
        var automataByPatternId = new RegexAutomaton?[patterns.Count];
        for (int index = 0; index < automataPatternIds.Count; index++)
        {
            automataByPatternId[automataPatternIds[index]] = automata[index];
        }

        var buckets = new List<PatternSetAnchoredMatcher>[256];
        for (int index = 0; index < buckets.Length; index++)
        {
            buckets[index] = [];
        }

        for (int patternId = 0; patternId < patterns.Count; patternId++)
        {
            var matcher = PatternSetAnchoredMatcher.Create(
                patternId,
                patterns[patternId],
                plans[patternId],
                options,
                automataByPatternId[patternId]);
            bool[] startBytes = new bool[256];
            matcher.AddStartBytes(startBytes);
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                if (startBytes[value])
                {
                    buckets[value].Add(matcher);
                }
            }
        }

        var indexed = new PatternSetAnchoredMatcher[256][];
        for (int index = 0; index < indexed.Length; index++)
        {
            indexed[index] = buckets[index].ToArray();
        }

        return indexed;
    }

    private static bool DetectEveryBytePositiveWidthCoverage(
        IReadOnlyList<byte[]> patterns,
        PatternSetPatternPlan[] plans,
        RegexCompileOptions options)
    {
        if (options.Utf8 || patterns.Count == 0)
        {
            return false;
        }

        bool coversNonLineBytes = false;
        bool coversLineFeed = !options.Crlf && options.LineTerminator != (byte)'\n';
        bool coversCarriageReturn = !options.Crlf && options.LineTerminator != (byte)'\r';
        bool coversCustomLineTerminator = options.Crlf || options.LineTerminator is (byte)'\n' or (byte)'\r';
        for (int index = 0; index < patterns.Count; index++)
        {
            RegexSyntaxNode? root = plans[index].Tree?.Root;
            if (root is null)
            {
                if (patterns[index].Length == 0)
                {
                    return false;
                }

                continue;
            }

            if (CanMatchEmpty(root, options))
            {
                return false;
            }

            if (TryGetSingleByteCoveringAtom(root, options, out bool coversLineTerminators))
            {
                coversNonLineBytes = true;
                if (coversLineTerminators)
                {
                    coversLineFeed = true;
                    coversCarriageReturn = true;
                    coversCustomLineTerminator = true;
                }

                continue;
            }

            if (!coversLineFeed && CanMatchByteAtStart(root, options, (byte)'\n'))
            {
                coversLineFeed = true;
            }

            if (!coversCarriageReturn && CanMatchByteAtStart(root, options, (byte)'\r'))
            {
                coversCarriageReturn = true;
            }

            if (!coversCustomLineTerminator && CanMatchByteAtStart(root, options, options.LineTerminator))
            {
                coversCustomLineTerminator = true;
            }
        }

        return coversNonLineBytes &&
            coversLineFeed &&
            coversCarriageReturn &&
            coversCustomLineTerminator;
    }

    private static bool TryGetSingleByteCoveringAtom(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out bool coversLineTerminators)
    {
        coversLineTerminators = false;
        while (true)
        {
            switch (node)
            {
                case RegexSequenceNode { Nodes.Count: 1 } sequence:
                    node = sequence.Nodes[0];
                    continue;
                case RegexGroupNode group:
                    options = options.Apply(group.EnabledFlags, group.DisabledFlags);
                    node = group.Child;
                    continue;
                case RegexAtomNode { Kind: RegexSyntaxKind.AnyClass }:
                    coversLineTerminators = true;
                    return true;
                case RegexAtomNode { Kind: RegexSyntaxKind.Dot }:
                    coversLineTerminators = options.DotMatchesNewline;
                    return true;
                default:
                    return false;
            }
        }
    }

    private static bool CanMatchByteAtStart(RegexSyntaxNode node, RegexCompileOptions options, byte value)
    {
        node = UnwrapSingleNodeSequence(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Literal:
                var literal = (RegexAtomNode)node;
                return literal.Value.Length > 0 &&
                    RegexByteClass.AtomMatches(
                        value,
                        RegexSyntaxKind.Literal,
                        literal.Value.Span,
                        options.CaseInsensitive,
                        options.MultiLine,
                        options.DotMatchesNewline,
                        options.Crlf,
                        options.LineTerminator);
            case RegexSyntaxKind.Dot:
            case RegexSyntaxKind.AnyClass:
            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.ByteClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
                var atom = (RegexAtomNode)node;
                return RegexByteClass.AtomMatches(
                    value,
                    atom.Kind,
                    atom.Value.Span,
                    options.CaseInsensitive,
                    options.MultiLine,
                    options.DotMatchesNewline,
                    options.Crlf,
                    options.LineTerminator);
            case RegexSyntaxKind.Sequence:
                return CanSequenceMatchByteAtStart((RegexSequenceNode)node, options, value);
            case RegexSyntaxKind.Alternation:
                var alternation = (RegexAlternationNode)node;
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (CanMatchByteAtStart(alternation.Alternatives[index], options, value))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return CanMatchByteAtStart(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), value);
            case RegexSyntaxKind.Repetition:
                var repetition = (RegexRepetitionNode)node;
                return repetition.Maximum != 0 && CanMatchByteAtStart(repetition.Child, options, value);
            default:
                return false;
        }
    }

    private static bool CanSequenceMatchByteAtStart(RegexSequenceNode sequence, RegexCompileOptions options, byte value)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (CanMatchByteAtStart(child, currentOptions, value))
            {
                return true;
            }

            if (!CanMatchEmpty(child, currentOptions))
            {
                return false;
            }

            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
            }
        }

        return false;
    }

    private static bool CanMatchEmpty(RegexSyntaxNode node, RegexCompileOptions options)
    {
        node = UnwrapSingleNodeSequence(node);
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
            case RegexSyntaxKind.Sequence:
                return CanSequenceMatchEmpty((RegexSequenceNode)node, options);
            case RegexSyntaxKind.Alternation:
                var alternation = (RegexAlternationNode)node;
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (CanMatchEmpty(alternation.Alternatives[index], options))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return CanMatchEmpty(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags));
            case RegexSyntaxKind.Repetition:
                var repetition = (RegexRepetitionNode)node;
                return repetition.Minimum == 0 || CanMatchEmpty(repetition.Child, options);
            default:
                return false;
        }
    }

    private static bool CanSequenceMatchEmpty(RegexSequenceNode sequence, RegexCompileOptions options)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (!CanMatchEmpty(child, currentOptions))
            {
                return false;
            }

            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
            }
        }

        return true;
    }

    private static RegexSyntaxNode UnwrapSingleNodeSequence(RegexSyntaxNode node)
    {
        while (node is RegexSequenceNode { Nodes.Count: 1 } sequence)
        {
            node = sequence.Nodes[0];
        }

        return node;
    }

    private static bool TryGetRawLiteralPattern(byte[] pattern, out byte[] literal)
    {
        if (pattern.Length == 0)
        {
            literal = [];
            return false;
        }

        for (int index = 0; index < pattern.Length; index++)
        {
            if (!IsRawLiteralByte(pattern[index]))
            {
                literal = [];
                return false;
            }
        }

        literal = pattern;
        return true;
    }

    private static bool IsRawLiteralByte(byte value)
    {
        return value is >= 0x20 and <= 0x7E &&
            value is not (byte)'\\' and
            not (byte)'.' and
            not (byte)'|' and
            not (byte)'*' and
            not (byte)'+' and
            not (byte)'?' and
            not (byte)'(' and
            not (byte)')' and
            not (byte)'[' and
            not (byte)']' and
            not (byte)'{' and
            not (byte)'}' and
            not (byte)'^' and
            not (byte)'$';
    }

    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_';
    }

    private static int[] BuildAllAutomataIndexes(int count)
    {
        int[] indexes = new int[count];
        for (int index = 0; index < indexes.Length; index++)
        {
            indexes[index] = index;
        }

        return indexes;
    }

    private long IterateNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suppressedEmptyStart = -1;
        while (offset <= haystack.Length)
        {
            PatternSetMatch? match = Find(haystack, offset);
            if (!match.HasValue)
            {
                return total;
            }

            RegexMatch span = match.Value.Match;
            if (span.Length == 0 && span.Start == suppressedEmptyStart)
            {
                offset = Math.Min(span.Start + 1, haystack.Length + 1);
                suppressedEmptyStart = -1;
                continue;
            }

            total += sumSpans ? span.Length : 1;
            if (span.Length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(span.End + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = Math.Min(span.End, haystack.Length + 1);
                offset = suppressedEmptyStart;
            }
        }

        return total;
    }

    /// <summary>
    /// Returns pattern identifiers whose regex matches the haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>Matching pattern identifiers in insertion order.</returns>
    public IReadOnlyList<int> MatchingPatternIds(ReadOnlySpan<byte> haystack)
    {
        bool[] matched = new bool[count];
        literalAccelerator?.MarkMatchingPatternIds(haystack, matched);
        boundaryLiteralAccelerator?.MarkMatchingPatternIds(haystack, matched);
        for (int index = 0; index < automata.Length; index++)
        {
            if (automata[index].IsMatch(haystack))
            {
                matched[automataPatternIds[index]] = true;
            }
        }

        var patternIds = new List<int>();
        for (int index = 0; index < matched.Length; index++)
        {
            if (matched[index])
            {
                patternIds.Add(index);
            }
        }

        return patternIds;
    }

    internal static bool IsBetter(PatternSetMatch candidate, PatternSetMatch? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        PatternSetMatch current = best.Value;
        if (candidate.Match.Start != current.Match.Start)
        {
            return candidate.Match.Start < current.Match.Start;
        }

        return candidate.PatternId < current.PatternId;
    }

    private static bool TryGetLiteralPattern(RegexSyntaxNode root, out byte[] literal)
    {
        var bytes = new List<byte>();
        if (TryAppendLiteralPattern(root, bytes))
        {
            literal = bytes.ToArray();
            return true;
        }

        literal = [];
        return false;
    }

    private static bool TryGetBoundaryLiteralPattern(RegexSyntaxNode root, RegexCompileOptions options, out byte[] literal)
    {
        literal = [];
        if (options.CaseInsensitive)
        {
            return false;
        }

        root = UnwrapTransparentGroup(root);
        if (root is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 3 ||
            sequence.Nodes[0].Kind != RegexSyntaxKind.WordBoundary ||
            sequence.Nodes[^1].Kind != RegexSyntaxKind.WordBoundary)
        {
            return false;
        }

        var bytes = new List<byte>();
        for (int index = 1; index < sequence.Nodes.Count - 1; index++)
        {
            if (!TryAppendLiteralPattern(sequence.Nodes[index], bytes))
            {
                return false;
            }
        }

        if (bytes.Count == 0)
        {
            return false;
        }

        for (int index = 0; index < bytes.Count; index++)
        {
            if (!IsAsciiWord(bytes[index]))
            {
                return false;
            }
        }

        literal = bytes.ToArray();
        return true;
    }

    private static RegexSyntaxNode UnwrapTransparentGroup(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }

    private static bool TryAppendLiteralPattern(RegexSyntaxNode node, List<byte> bytes)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                return true;
            case RegexSyntaxKind.Literal:
                bytes.AddRange(((RegexAtomNode)node).Value.ToArray());
                return true;
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (!TryAppendLiteralPattern(sequence.Nodes[index], bytes))
                    {
                        return false;
                    }
                }

                return true;
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return string.IsNullOrEmpty(group.EnabledFlags) &&
                    string.IsNullOrEmpty(group.DisabledFlags) &&
                    TryAppendLiteralPattern(group.Child, bytes);
            default:
                return false;
        }
    }

    private static bool TryPrepareLiteralPatterns(byte[] literal, RegexCompileOptions options, out byte[][] prepared)
    {
        if (!options.CaseInsensitive)
        {
            prepared = [literal];
            return true;
        }

        return RegexPrefilter.TryPreparePrefixLiteralSet([literal], options, out prepared);
    }

    private static int TryGetRequiredLiteralLookBehind(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        byte[][] requiredLiterals)
    {
        if (options.UnicodeClasses ||
            !RegexPrefilter.TryCollectRequiredLiteralSetWithLookBehind(root, options, out byte[][] boundedLiterals, out int maxLookBehind) ||
            !LiteralSetsEqual(requiredLiterals, boundedLiterals))
        {
            return RegexPrefilter.RequiredLiteralLookBehind;
        }

        return Math.Min(maxLookBehind, RegexPrefilter.RequiredLiteralLookBehind);
    }

    private static bool LiteralSetsEqual(byte[][] left, byte[][] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (!left[index].AsSpan().SequenceEqual(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
