namespace Scout;

/// <summary>
/// Verifies the shared table-driven unanchored DFA.
/// </summary>
public sealed class RegexUnanchoredDenseDfaTests
{
    private const ulong GenerousDfaSizeLimit = 16UL * 1024UL * 1024UL;

    /// <summary>
    /// Verifies match-end search agrees with the authoritative PikeVM across leftmost priority,
    /// greedy and lazy repetition, alternation, bounded repetition, and byte classes.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="haystackText">The bytes to search.</param>
    [Theory]
    [InlineData("ab|a", "zzab ax")]
    [InlineData("a|ab", "zzab ax")]
    [InlineData("a+", "zz aaab a")]
    [InlineData("a+?", "zz aaab a")]
    [InlineData("(?:ab|a)+", "zzaba!a")]
    [InlineData("(?:ab|a)+?", "zzaba!a")]
    [InlineData("[A-Za-z_][A-Za-z_0-9]{1,3}", "!!alpha id_ x9")]
    [InlineData("[^,\\r\\n]+(?:,[^,\\r\\n]+){2}", "!aa,bb,cc!dd")]
    [InlineData("(?:cat|dog){2,3}", "--catdogdog--")]
    [InlineData(".*suffix", "xxprefix suffix yy")]
    [InlineData("(?s:.+)", "all bytes stay live\nthrough the end")]
    [InlineData(@"\b\w{5}\s+\w{5}\s+\w{5}\b", "!!alpha bravo charl!! delta echoo foxtt")]
    [InlineData(@"\Babc\B", "xabcx abc abc!")]
    [InlineData(@"\<alpha\>", "xalpha alpha alpha!")]
    [InlineData(@"(?m:^alpha$)", "no\nalpha\r\nalpha\nend")]
    [InlineData(@"\Aalpha", "alpha alpha")]
    [InlineData(@"alpha\z", "alpha alpha")]
    public void TryFindEndMatchesPikeVmForSupportedPatterns(
        string pattern,
        string haystackText)
    {
        RegexUnanchoredDenseDfa dfa = CompileDense(pattern);
        RegexMetaEngine fallback = CompileFallback(pattern);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(haystackText);

        for (int startAt = 0; startAt <= haystack.Length; startAt++)
        {
            RegexMatch? expected = fallback.Find(haystack, startAt);
            bool found = dfa.TryFindEnd(haystack, startAt, out int end);

            Assert.Equal(expected.HasValue, found);
            Assert.Equal(expected?.End ?? -1, end);
        }
    }

    /// <summary>
    /// Verifies a pure end-anchor expression agrees with PikeVM before LF, isolated LF and CR,
    /// CRLF, ordinary bytes, and end of input.
    /// </summary>
    /// <param name="crlf">Whether CR and LF use CRLF-aware anchor semantics.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndAnchorMatchesPikeVmAcrossLineContexts(bool crlf)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("foo$"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            crlf,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: false,
            specializationMode: RegexSpecializationMode.General);
        RegexNfa unanchored = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        Assert.True(RegexUnanchoredDenseDfa.TryCompile(
            unanchored,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? dfa));
        RegexNfa anchored = RegexNfaCompiler.Compile(tree.Root, options);
        var fallback = RegexMetaEngine.Compile(
            anchored,
            prefilter: null,
            dfaSizeLimit: 0);
        byte[] haystack = "foo\nfoo\r\nfoo\rbar\nfoo xfoo"u8.ToArray();

        for (int startAt = 0; startAt <= haystack.Length; startAt++)
        {
            RegexMatch? expected = fallback.Find(haystack, startAt);
            bool found = dfa!.TryFindEnd(haystack, startAt, out int end);

            Assert.Equal(expected.HasValue, found);
            Assert.Equal(expected?.End ?? -1, end);
        }

        Assert.Equal(
            fallback.CountMatches(haystack, startAt: 0),
            dfa!.CountMatches(haystack, startAt: 0));
    }

    /// <summary>
    /// Verifies match-end search reports a definitive no-match result and clamps offsets to the
    /// available haystack bounds.
    /// </summary>
    [Fact]
    public void TryFindEndHandlesNoMatchAndOutOfRangeOffsets()
    {
        RegexUnanchoredDenseDfa dfa = CompileDense("(?:cat|dog){2}");
        byte[] haystack = "one bird and one fish"u8.ToArray();
        byte[] matchingHaystack = "catdog then dogcat"u8.ToArray();

        Assert.False(dfa.TryFindEnd(haystack, startAt: 0, out int noMatchEnd));
        Assert.Equal(-1, noMatchEnd);
        Assert.False(dfa.TryFindEnd(haystack, startAt: haystack.Length + 100, out int afterEnd));
        Assert.Equal(-1, afterEnd);
        Assert.Equal(
            dfa.TryFindEnd(matchingHaystack, startAt: 0, out int zeroEnd),
            dfa.TryFindEnd(matchingHaystack, startAt: -100, out int negativeEnd));
        Assert.Equal(zeroEnd, negativeEnd);
    }

    /// <summary>
    /// Verifies non-overlapping counting agrees with the authoritative PikeVM for greedy, lazy,
    /// alternative-priority, bounded-repetition, and no-match searches.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="haystackText">The bytes to search.</param>
    [Theory]
    [InlineData("a+", "aaaa aa aaaa")]
    [InlineData("a+?", "aaaa aa aaaa")]
    [InlineData("ab|a", "aba ab aa")]
    [InlineData("a|ab", "aba ab aa")]
    [InlineData("[0-9]{2,3}", "1 22 333 4444")]
    [InlineData("(?:cat|dog){2}", "catdog dogcat bird catcat")]
    [InlineData("needle", "a haystack without the token")]
    public void CountMatchesMatchesPikeVm(string pattern, string haystackText)
    {
        RegexUnanchoredDenseDfa dfa = CompileDense(pattern);
        RegexMetaEngine fallback = CompileFallback(pattern);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(haystackText);

        for (int startAt = 0; startAt <= haystack.Length; startAt++)
        {
            Assert.Equal(
                fallback.CountMatches(haystack, startAt),
                dfa.CountMatches(haystack, startAt));
        }
    }

    /// <summary>
    /// Verifies bounded determinization declines invalid limits, an insufficient state bound,
    /// insufficient storage, and empty matches without publishing a partial DFA.
    /// </summary>
    [Fact]
    public void TryCompileDeclinesUnsupportedOrOverBudgetAutomata()
    {
        RegexNfa nfa = CompileUnanchored("(?:ab|ac|ba|bc){2,4}");

        Assert.True(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? compiled));
        Assert.NotNull(compiled);
        Assert.False(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 0,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? invalidStateLimit));
        Assert.Null(invalidStateLimit);
        Assert.False(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? stateLimited));
        Assert.Null(stateLimited);
        Assert.False(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1_024,
            dfaSizeLimit: 1,
            out RegexUnanchoredDenseDfa? storageLimited));
        Assert.Null(storageLimited);

        RegexNfa emptyMatchNfa = CompileUnanchored("a*");
        Assert.False(RegexUnanchoredDenseDfa.TryCompile(
            emptyMatchNfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? emptyMatchDfa));
        Assert.Null(emptyMatchDfa);

        RegexNfa predicateNfa = CompileUnanchored(@"\bGeneratedRecord\b");
        Assert.True(RegexUnanchoredDenseDfa.TryCompile(
            predicateNfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? predicateDfa));
        Assert.NotNull(predicateDfa);
    }

    /// <summary>
    /// Verifies one immutable dense DFA can service concurrent find and count operations.
    /// </summary>
    [Fact]
    public void SharedDfaSupportsConcurrentSearches()
    {
        RegexUnanchoredDenseDfa dfa = CompileDense("(?:ab|a)+?z");
        byte[] haystack = "!!ababaz--aaz--none"u8.ToArray();
        const int operationCount = 256;
        bool[] found = new bool[operationCount];
        int[] ends = new int[operationCount];
        long[] counts = new long[operationCount];

        Parallel.For(0, operationCount, index =>
        {
            int startAt = index % 12;
            found[index] = dfa.TryFindEnd(haystack, startAt, out ends[index]);
            counts[index] = dfa.CountMatches(haystack, startAt);
        });

        RegexMetaEngine fallback = CompileFallback("(?:ab|a)+?z");
        for (int index = 0; index < operationCount; index++)
        {
            int startAt = index % 12;
            RegexMatch? expected = fallback.Find(haystack, startAt);
            Assert.Equal(expected.HasValue, found[index]);
            Assert.Equal(expected?.End ?? -1, ends[index]);
            Assert.Equal(fallback.CountMatches(haystack, startAt), counts[index]);
        }
    }

    /// <summary>
    /// Verifies byte equivalence keeps sparse transitions with distinct targets in separate classes.
    /// </summary>
    [Fact]
    public void ByteClassesDistinguishSparseTransitionTargets()
    {
        RegexNfa unanchored = new(
            states:
            [
                new RegexNfaState(
                    RegexNfaStateKind.Sparse,
                    RegexSyntaxKind.Empty,
                    default,
                    caseInsensitive: false,
                    multiLine: true,
                    dotMatchesNewline: false,
                    crlf: false,
                    lineTerminator: (byte)'\n',
                    utf8: false,
                    unicodeClasses: false,
                    next: -1,
                    alternative: -1,
                    sparseTransitions:
                    [
                        new RegexNfaSparseTransition((byte)'a', (byte)'a', Next: 1),
                        new RegexNfaSparseTransition((byte)'b', (byte)'b', Next: 2),
                    ]),
                new RegexNfaState(
                    RegexNfaStateKind.Atom,
                    RegexSyntaxKind.Literal,
                    "x"u8.ToArray(),
                    caseInsensitive: false,
                    multiLine: true,
                    dotMatchesNewline: false,
                    crlf: false,
                    lineTerminator: (byte)'\n',
                    utf8: false,
                    unicodeClasses: false,
                    next: 3,
                    alternative: -1),
                new RegexNfaState(
                    RegexNfaStateKind.Atom,
                    RegexSyntaxKind.Literal,
                    "y"u8.ToArray(),
                    caseInsensitive: false,
                    multiLine: true,
                    dotMatchesNewline: false,
                    crlf: false,
                    lineTerminator: (byte)'\n',
                    utf8: false,
                    unicodeClasses: false,
                    next: 3,
                    alternative: -1),
                new RegexNfaState(
                    RegexNfaStateKind.Accept,
                    RegexSyntaxKind.Empty,
                    default,
                    caseInsensitive: false,
                    multiLine: true,
                    dotMatchesNewline: false,
                    crlf: false,
                    lineTerminator: (byte)'\n',
                    utf8: false,
                    unicodeClasses: false,
                    next: -1,
                    alternative: -1),
            ],
            startState: 0,
            utf8: false);
        Assert.True(RegexUnanchoredDenseDfa.TryCompile(
            unanchored,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? dfa));
        var fallback = new PikeVm(unanchored);
        byte[][] haystacks =
        [
            "ax"u8.ToArray(),
            "by"u8.ToArray(),
            "ay"u8.ToArray(),
            "bx"u8.ToArray(),
        ];

        for (int haystackIndex = 0; haystackIndex < haystacks.Length; haystackIndex++)
        {
            byte[] haystack = haystacks[haystackIndex];
            bool expected = fallback.TryMatchAt(haystack, start: 0, out int expectedLength);
            bool found = dfa!.TryFindEnd(haystack, startAt: 0, out int end);

            Assert.Equal(expected, found);
            Assert.Equal(expected ? expectedLength : -1, end);
        }
    }

    /// <summary>
    /// Verifies a general Unicode plan uses one shared dense ASCII projection without renting a
    /// mutable lazy-DFA runner.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsSharedDenseAsciiProjectionGenerically()
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        Assert.NotNull(plan);
        RegexMetaEngine engine = GetMetaEngine(plan.Matcher);
        Assert.NotNull(GetDenseProjection(engine));
        Assert.Equal(0, GetAsciiProjectionActivation(engine));
        Assert.False(HasCachedAsciiProjectionRunner(engine));

        RegexMatchEndRunner runner = plan.Matcher.RentAsciiProjectedMatchEndRunner(
            activationLength: 8_192);
        try
        {
            Assert.True(runner.IsAvailable);
            Assert.True(runner.UsesAsciiProjection);
            Assert.True(runner.TryFindEnd(
                "!!alpha bravo charl!!"u8,
                startAt: 0,
                out int end,
                out bool completed));
            Assert.True(completed);
            Assert.Equal(19, end);
            Assert.True(runner.TryCountMatches(
                "alpha bravo charl--delta echoo foxtt"u8,
                startAt: 0,
                out long count));
            Assert.Equal(2, count);
        }
        finally
        {
            runner.Dispose();
        }

        Assert.Equal(1, GetAsciiProjectionActivation(engine));
        Assert.False(HasCachedAsciiProjectionRunner(engine));
    }

    /// <summary>
    /// Verifies ASCII word look-around uses the shared delayed-match projection.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsSharedDenseAsciiProjectionForWordLookaround()
    {
        byte[][] patterns = [@"\b\w{5}\s+\w{5}\s+\w{5}\b"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        Assert.NotNull(plan);
        RegexMetaEngine engine = GetMetaEngine(plan.Matcher);
        Assert.NotNull(GetDenseProjection(engine));

        RegexMatchEndRunner runner = plan.Matcher.RentAsciiProjectedMatchEndRunner(
            activationLength: 8_192);
        try
        {
            Assert.True(runner.IsAvailable);
            Assert.True(runner.UsesAsciiProjection);
            Assert.True(runner.TryFindEnd(
                "!!alpha bravo charl!!"u8,
                startAt: 0,
                out int end,
                out bool completed));
            Assert.True(completed);
            Assert.Equal(19, end);
            Assert.True(runner.TryCountMatches(
                "alpha bravo charl--delta echoo foxtt"u8,
                startAt: 0,
                out long count));
            Assert.Equal(2, count);
        }
        finally
        {
            runner.Dispose();
        }
    }

    /// <summary>
    /// Verifies large projected NFAs skip eager dense determinization while retaining the
    /// authoritative fallback.
    /// </summary>
    [Fact]
    public void MetaEngineSkipsEagerDenseProjectionForLargeNfa()
    {
        byte[] pattern = "[A-Za-z0-9_-]{50,3000}"u8.ToArray();
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General,
            excludeLineTerminators: true);
        Assert.True(RegexAsciiFastPath.TryCompileNfa(
            pattern,
            tree.Root,
            options,
            out RegexNfa? projectedNfa));
        Assert.True(projectedNfa!.States.Count > 64);

        var automaton = RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: 16 * 1024 * 1024,
            compilePrefilter: false);

        Assert.Null(GetDenseProjection(GetMetaEngine(automaton)));
        Assert.Null(automaton.Find("short"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies a projected runner factory that permanently fails is neither advertised nor
    /// retried for later search segments.
    /// </summary>
    [Fact]
    public void FailedAsciiProjectionFactoryIsNotAdvertisedOrRetried()
    {
        byte[] pattern = "[A-Za-z0-9_-]{50,3000}"u8.ToArray();
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General,
            excludeLineTerminators: true);
        var automaton = RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: GenerousDfaSizeLimit,
            compilePrefilter: false);
        RegexMetaEngine engine = GetMetaEngine(automaton);
        Assert.Null(GetDenseProjection(engine));

        int attempts = 0;
        Func<RegexUnanchoredLazyDfa?> failingFactory = () =>
        {
            attempts++;
            return null;
        };
        typeof(RegexMetaEngine)
            .GetField(
                "_asciiFastUnanchoredDfaFactory",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)!
            .SetValue(engine, failingFactory);

        Assert.True(automaton.HasAsciiProjectedMatchEndRunner);
        using (RegexMatchEndRunner first = automaton.RentAsciiProjectedMatchEndRunner(
                   activationLength: 8_192))
        {
            Assert.False(first.IsAvailable);
        }

        Assert.Equal(1, attempts);
        Assert.False(automaton.HasAsciiProjectedMatchEndRunner);
        using (RegexMatchEndRunner second = automaton.RentAsciiProjectedMatchEndRunner(
                   activationLength: 8_192))
        {
            Assert.False(second.IsAvailable);
        }

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// Verifies bounded look-around determinization can decline a small NFA with an exponential
    /// state set without publishing a projected runner or weakening the Unicode fallback.
    /// </summary>
    [Fact]
    public void LookaroundStateExplosionRetainsAuthoritativeFallback()
    {
        byte[] pattern = @"\b\w*a[ab]{6}\b"u8.ToArray();
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General,
            excludeLineTerminators: true);
        Assert.True(RegexAsciiFastPath.TryCompileNfa(
            pattern,
            tree.Root,
            options,
            out RegexNfa? projectedNfa));
        Assert.True(projectedNfa!.States.Count <= 64);
        RegexNfa unanchored = RegexUnanchoredLazyDfa.CreateUnanchoredForwardNfa(projectedNfa);
        Assert.False(RegexUnanchoredDenseDfa.TryCompile(
            unanchored,
            stateLimit: 64,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? rejectedDfa));
        Assert.Null(rejectedDfa);
        Assert.Equal(
            RegexAutomaton.ShouldCompileCompactScalarNfa(
                tree.Root,
                options,
                hasSafeAsciiProjection: false),
            RegexAutomaton.ShouldCompileCompactScalarNfa(
                tree.Root,
                options,
                hasSafeAsciiProjection: true));

        var automaton = RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: GenerousDfaSizeLimit,
            compilePrefilter: false);
        Assert.Null(GetDenseProjection(GetMetaEngine(automaton)));
        Assert.False(automaton.HasAsciiProjectedMatchEndRunner);
        using RegexMatchEndRunner runner = automaton.RentAsciiProjectedMatchEndRunner(
            activationLength: 8_192);
        Assert.False(runner.IsAvailable);
        Assert.Equal(1, automaton.CountMatches("ébaaaaaaaé baaaaaaa"u8));
    }

    private static RegexUnanchoredDenseDfa CompileDense(string pattern)
    {
        RegexNfa nfa = CompileUnanchored(pattern);
        Assert.True(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? dfa));
        return Assert.IsType<RegexUnanchoredDenseDfa>(dfa);
    }

    private static RegexNfa CompileUnanchored(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(
            System.Text.Encoding.ASCII.GetBytes(pattern));
        return RegexNfaCompiler.CompileUnanchored(tree.Root, CreateCompileOptions());
    }

    private static RegexMetaEngine CompileFallback(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(
            System.Text.Encoding.ASCII.GetBytes(pattern));
        RegexNfa nfa = RegexNfaCompiler.Compile(tree.Root, CreateCompileOptions());
        return RegexMetaEngine.Compile(nfa, prefilter: null, dfaSizeLimit: 0);
    }

    private static RegexCompileOptions CreateCompileOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false,
            specializationMode: RegexSpecializationMode.General);
    }

    private static RegexMetaEngine GetMetaEngine(RegexAutomaton automaton)
    {
        return (RegexMetaEngine)typeof(RegexAutomaton)
            .GetField(
                "engine",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)!
            .GetValue(automaton)!;
    }

    private static RegexUnanchoredDenseDfa? GetDenseProjection(RegexMetaEngine engine)
    {
        return (RegexUnanchoredDenseDfa?)typeof(RegexMetaEngine)
            .GetField(
                "_asciiFastUnanchoredDenseDfa",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)!
            .GetValue(engine);
    }

    private static int GetAsciiProjectionActivation(RegexMetaEngine engine)
    {
        return (int)typeof(RegexMetaEngine)
            .GetField(
                "_asciiFastUnanchoredDfaActivated",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)!
            .GetValue(engine)!;
    }

    private static bool HasCachedAsciiProjectionRunner(RegexMetaEngine engine)
    {
        object? pool = typeof(RegexMetaEngine)
            .GetField(
                "_asciiFastUnanchoredDfaPool",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)!
            .GetValue(engine);
        if (pool is null)
        {
            return false;
        }

        var slots = (Array)pool.GetType()
            .GetField(
                "localSlots",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)!
            .GetValue(pool)!;
        foreach (object slot in slots)
        {
            if (slot.GetType()
                .GetField(
                    "Item",
                    System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)!
                .GetValue(slot) is RegexUnanchoredLazyDfa)
            {
                return true;
            }
        }

        return false;
    }
}
