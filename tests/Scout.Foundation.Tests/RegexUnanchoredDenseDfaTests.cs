namespace Scout;

/// <summary>
/// Verifies the shared table-driven unanchored DFA.
/// </summary>
[TestClass]
public sealed class RegexUnanchoredDenseDfaTests
{
    private const ulong GenerousDfaSizeLimit = 16UL * 1024UL * 1024UL;

    /// <summary>
    /// Verifies match-end search agrees with the authoritative PikeVM across leftmost priority,
    /// greedy and lazy repetition, alternation, bounded repetition, and byte classes.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="haystackText">The bytes to search.</param>
    [TestMethod]
    [DataRow("ab|a", "zzab ax")]
    [DataRow("a|ab", "zzab ax")]
    [DataRow("a+", "zz aaab a")]
    [DataRow("a+?", "zz aaab a")]
    [DataRow("(?:ab|a)+", "zzaba!a")]
    [DataRow("(?:ab|a)+?", "zzaba!a")]
    [DataRow("[A-Za-z_][A-Za-z_0-9]{1,3}", "!!alpha id_ x9")]
    [DataRow("[^,\\r\\n]+(?:,[^,\\r\\n]+){2}", "!aa,bb,cc!dd")]
    [DataRow("(?:cat|dog){2,3}", "--catdogdog--")]
    [DataRow(".*suffix", "xxprefix suffix yy")]
    [DataRow("(?s:.+)", "all bytes stay live\nthrough the end")]
    [DataRow(@"\b\w{5}\s+\w{5}\s+\w{5}\b", "!!alpha bravo charl!! delta echoo foxtt")]
    [DataRow(@"\Babc\B", "xabcx abc abc!")]
    [DataRow(@"\<alpha\>", "xalpha alpha alpha!")]
    [DataRow(@"(?m:^alpha$)", "no\nalpha\r\nalpha\nend")]
    [DataRow(@"\Aalpha", "alpha alpha")]
    [DataRow(@"alpha\z", "alpha alpha")]
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

            Assert.AreEqual(expected.HasValue, found);
            Assert.AreEqual(expected?.End ?? -1, end);
        }
    }

    /// <summary>
    /// Verifies a pure end-anchor expression agrees with PikeVM before LF, isolated LF and CR,
    /// CRLF, ordinary bytes, and end of input.
    /// </summary>
    /// <param name="crlf">Whether CR and LF use CRLF-aware anchor semantics.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
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
        Assert.IsTrue(RegexUnanchoredDenseDfa.TryCompile(
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

            Assert.AreEqual(expected.HasValue, found);
            Assert.AreEqual(expected?.End ?? -1, end);
        }

        Assert.AreEqual(
            fallback.CountMatches(haystack, startAt: 0),
            dfa!.CountMatches(haystack, startAt: 0));
    }

    /// <summary>
    /// Verifies match-end search reports a definitive no-match result and clamps offsets to the
    /// available haystack bounds.
    /// </summary>
    [TestMethod]
    public void TryFindEndHandlesNoMatchAndOutOfRangeOffsets()
    {
        RegexUnanchoredDenseDfa dfa = CompileDense("(?:cat|dog){2}");
        byte[] haystack = "one bird and one fish"u8.ToArray();
        byte[] matchingHaystack = "catdog then dogcat"u8.ToArray();

        Assert.IsFalse(dfa.TryFindEnd(haystack, startAt: 0, out int noMatchEnd));
        Assert.AreEqual(-1, noMatchEnd);
        Assert.IsFalse(dfa.TryFindEnd(haystack, startAt: haystack.Length + 100, out int afterEnd));
        Assert.AreEqual(-1, afterEnd);
        Assert.AreEqual(
            dfa.TryFindEnd(matchingHaystack, startAt: 0, out int zeroEnd),
            dfa.TryFindEnd(matchingHaystack, startAt: -100, out int negativeEnd));
        Assert.AreEqual(zeroEnd, negativeEnd);
    }

    /// <summary>
    /// Verifies non-overlapping counting agrees with the authoritative PikeVM for greedy, lazy,
    /// alternative-priority, bounded-repetition, and no-match searches.
    /// </summary>
    /// <param name="pattern">The pattern to compile.</param>
    /// <param name="haystackText">The bytes to search.</param>
    [TestMethod]
    [DataRow("a+", "aaaa aa aaaa")]
    [DataRow("a+?", "aaaa aa aaaa")]
    [DataRow("ab|a", "aba ab aa")]
    [DataRow("a|ab", "aba ab aa")]
    [DataRow("[0-9]{2,3}", "1 22 333 4444")]
    [DataRow("(?:cat|dog){2}", "catdog dogcat bird catcat")]
    [DataRow("needle", "a haystack without the token")]
    public void CountMatchesMatchesPikeVm(string pattern, string haystackText)
    {
        RegexUnanchoredDenseDfa dfa = CompileDense(pattern);
        RegexMetaEngine fallback = CompileFallback(pattern);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(haystackText);

        for (int startAt = 0; startAt <= haystack.Length; startAt++)
        {
            Assert.AreEqual(
                fallback.CountMatches(haystack, startAt),
                dfa.CountMatches(haystack, startAt));
        }
    }

    /// <summary>
    /// Verifies bounded determinization declines invalid limits, an insufficient state bound,
    /// insufficient storage, and empty matches without publishing a partial DFA.
    /// </summary>
    [TestMethod]
    public void TryCompileDeclinesUnsupportedOrOverBudgetAutomata()
    {
        RegexNfa nfa = CompileUnanchored("(?:ab|ac|ba|bc){2,4}");

        Assert.IsTrue(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? compiled));
        Assert.IsNotNull(compiled);
        Assert.IsFalse(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 0,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? invalidStateLimit));
        Assert.IsNull(invalidStateLimit);
        Assert.IsFalse(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? stateLimited));
        Assert.IsNull(stateLimited);
        Assert.IsFalse(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1_024,
            dfaSizeLimit: 1,
            out RegexUnanchoredDenseDfa? storageLimited));
        Assert.IsNull(storageLimited);

        RegexNfa emptyMatchNfa = CompileUnanchored("a*");
        Assert.IsFalse(RegexUnanchoredDenseDfa.TryCompile(
            emptyMatchNfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? emptyMatchDfa));
        Assert.IsNull(emptyMatchDfa);

        RegexNfa predicateNfa = CompileUnanchored(@"\bGeneratedRecord\b");
        Assert.IsTrue(RegexUnanchoredDenseDfa.TryCompile(
            predicateNfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? predicateDfa));
        Assert.IsNotNull(predicateDfa);
    }

    /// <summary>
    /// Verifies one immutable dense DFA can service concurrent find and count operations.
    /// </summary>
    [TestMethod]
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
            Assert.AreEqual(expected.HasValue, found[index]);
            Assert.AreEqual(expected?.End ?? -1, ends[index]);
            Assert.AreEqual(fallback.CountMatches(haystack, startAt), counts[index]);
        }
    }

    /// <summary>
    /// Verifies byte equivalence keeps sparse transitions with distinct targets in separate classes.
    /// </summary>
    [TestMethod]
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
        Assert.IsTrue(RegexUnanchoredDenseDfa.TryCompile(
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

            Assert.AreEqual(expected, found);
            Assert.AreEqual(expected ? expectedLength : -1, end);
        }
    }

    /// <summary>
    /// Verifies a general Unicode plan uses one shared dense ASCII projection without renting a
    /// mutable lazy-DFA runner.
    /// </summary>
    [TestMethod]
    public void MetaEngineSelectsSharedDenseAsciiProjectionGenerically()
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        Assert.IsNotNull(plan);
        RegexMetaEngine engine = GetMetaEngine(plan.Matcher);
        Assert.IsNotNull(GetDenseProjection(engine));
        Assert.AreEqual(0, GetAsciiProjectionActivation(engine));
        Assert.IsFalse(HasCachedAsciiProjectionRunner(engine));

        RegexMatchEndRunner runner = plan.Matcher.RentAsciiProjectedMatchEndRunner(
            activationLength: 8_192);
        try
        {
            Assert.IsTrue(runner.IsAvailable);
            Assert.IsTrue(runner.UsesAsciiProjection);
            Assert.IsTrue(runner.TryFindEnd(
                "!!alpha bravo charl!!"u8,
                startAt: 0,
                out int end,
                out bool completed));
            Assert.IsTrue(completed);
            Assert.AreEqual(19, end);
            Assert.IsTrue(runner.TryCountMatches(
                "alpha bravo charl--delta echoo foxtt"u8,
                startAt: 0,
                out long count));
            Assert.AreEqual(2, count);
        }
        finally
        {
            runner.Dispose();
        }

        Assert.AreEqual(1, GetAsciiProjectionActivation(engine));
        Assert.IsFalse(HasCachedAsciiProjectionRunner(engine));
    }

    /// <summary>
    /// Verifies ASCII word look-around uses the shared delayed-match projection.
    /// </summary>
    [TestMethod]
    public void MetaEngineSelectsSharedDenseAsciiProjectionForWordLookaround()
    {
        byte[][] patterns = [@"\b\w{5}\s+\w{5}\s+\w{5}\b"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        Assert.IsNotNull(plan);
        RegexMetaEngine engine = GetMetaEngine(plan.Matcher);
        Assert.IsNotNull(GetDenseProjection(engine));

        RegexMatchEndRunner runner = plan.Matcher.RentAsciiProjectedMatchEndRunner(
            activationLength: 8_192);
        try
        {
            Assert.IsTrue(runner.IsAvailable);
            Assert.IsTrue(runner.UsesAsciiProjection);
            Assert.IsTrue(runner.TryFindEnd(
                "!!alpha bravo charl!!"u8,
                startAt: 0,
                out int end,
                out bool completed));
            Assert.IsTrue(completed);
            Assert.AreEqual(19, end);
            Assert.IsTrue(runner.TryCountMatches(
                "alpha bravo charl--delta echoo foxtt"u8,
                startAt: 0,
                out long count));
            Assert.AreEqual(2, count);
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
    [TestMethod]
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
        Assert.IsTrue(RegexAsciiFastPath.TryCompileNfa(
            pattern,
            tree.Root,
            options,
            out RegexNfa? projectedNfa));
        Assert.IsGreaterThan(64, projectedNfa!.States.Count);

        var automaton = RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: 16 * 1024 * 1024,
            compilePrefilter: false);

        Assert.IsNull(GetDenseProjection(GetMetaEngine(automaton)));
        Assert.IsNull(automaton.Find("short"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies a projected runner factory that permanently fails is neither advertised nor
    /// retried for later search segments.
    /// </summary>
    [TestMethod]
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
        Assert.IsNull(GetDenseProjection(engine));

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

        Assert.IsTrue(automaton.HasAsciiProjectedMatchEndRunner);
        using (RegexMatchEndRunner first = automaton.RentAsciiProjectedMatchEndRunner(
                   activationLength: 8_192))
        {
            Assert.IsFalse(first.IsAvailable);
        }

        Assert.AreEqual(1, attempts);
        Assert.IsFalse(automaton.HasAsciiProjectedMatchEndRunner);
        using (RegexMatchEndRunner second = automaton.RentAsciiProjectedMatchEndRunner(
                   activationLength: 8_192))
        {
            Assert.IsFalse(second.IsAvailable);
        }

        Assert.AreEqual(1, attempts);
    }

    /// <summary>
    /// Verifies a word-assertion NFA that exceeds eager dense limits retains the authoritative
    /// fallback instead of publishing a slower projected lazy runner.
    /// </summary>
    [TestMethod]
    public void WordAssertionDenseStateExplosionRetainsAuthoritativeFallback()
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
        Assert.IsTrue(RegexAsciiFastPath.TryCompileNfa(
            pattern,
            tree.Root,
            options,
            out RegexNfa? projectedNfa));
        Assert.IsLessThanOrEqualTo(64, projectedNfa!.States.Count);
        RegexNfa unanchored = RegexUnanchoredLazyDfa.CreateUnanchoredForwardNfa(projectedNfa);
        Assert.IsFalse(RegexUnanchoredDenseDfa.TryCompile(
            unanchored,
            stateLimit: 64,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? rejectedDfa));
        Assert.IsNull(rejectedDfa);
        Assert.AreEqual(
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
        Assert.IsNull(GetDenseProjection(GetMetaEngine(automaton)));
        Assert.IsFalse(automaton.HasAsciiProjectedMatchEndRunner);
        using RegexMatchEndRunner runner = automaton.RentAsciiProjectedMatchEndRunner(
            activationLength: 8_192);
        Assert.IsFalse(runner.IsAvailable);
        Assert.AreEqual(1, automaton.CountMatches("ébaaaaaaaé baaaaaaa"u8));
    }

    /// <summary>
    /// Verifies the capture workload that exposed the regression retains its eager projected
    /// match-end DFA without publishing a projected lazy full-match runner.
    /// </summary>
    [TestMethod]
    public void WordBoundaryCaptureKeepsDenseMatchEndsWithoutLazyFullMatchProjection()
    {
        byte[] pattern = @"\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*)"u8.ToArray();
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
        Assert.IsTrue(RegexAsciiFastPath.TryCompileNfa(
            pattern,
            tree.Root,
            options,
            out RegexNfa? projectedNfa));
        RegexNfa unanchored = RegexUnanchoredLazyDfa.CreateUnanchoredForwardNfa(projectedNfa!);
        Assert.IsTrue(RegexUnanchoredDenseDfa.TryCompile(
            unanchored,
            stateLimit: 64,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? projectedDfa));
        Assert.IsNotNull(projectedDfa);

        var automaton = RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: GenerousDfaSizeLimit,
            compilePrefilter: true);

        RegexMetaEngine engine = GetMetaEngine(automaton);
        Assert.IsNotNull(GetDenseProjection(engine));
        Assert.IsNull(GetLazyProjectionFactory(engine));
        Assert.IsTrue(automaton.HasAsciiProjectedMatchEndRunner);
        Assert.AreEqual(new RegexMatch(1, 13), automaton.Find("!struct Widget!"u8, startAt: 0));
    }

    private static RegexUnanchoredDenseDfa CompileDense(string pattern)
    {
        RegexNfa nfa = CompileUnanchored(pattern);
        Assert.IsTrue(RegexUnanchoredDenseDfa.TryCompile(
            nfa,
            stateLimit: 1_024,
            GenerousDfaSizeLimit,
            out RegexUnanchoredDenseDfa? dfa));
        return Assert.IsExactInstanceOfType<RegexUnanchoredDenseDfa>(dfa);
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

    private static Func<RegexUnanchoredLazyDfa?>? GetLazyProjectionFactory(
        RegexMetaEngine engine)
    {
        return (Func<RegexUnanchoredLazyDfa?>?)typeof(RegexMetaEngine)
            .GetField(
                "_asciiFastUnanchoredDfaFactory",
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
