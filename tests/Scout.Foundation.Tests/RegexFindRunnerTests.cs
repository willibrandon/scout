namespace Scout;

/// <summary>
/// Verifies operation-scoped authoritative regex runner ownership and reuse.
/// </summary>
public sealed class RegexFindRunnerTests
{
    /// <summary>
    /// Verifies one runner preserves leftmost-first and non-overlapping behavior across sequential searches.
    /// </summary>
    [Fact]
    public void SequentialReusePreservesLeftmostNonOverlappingMatches()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        ReadOnlySpan<byte> haystack = "abazz"u8;
        using RegexFindRunner runner = automaton.RentFindRunner();

        RegexMatch? first = runner.Find(haystack, startAt: 0);
        RegexMatch? second = runner.Find(haystack, first!.Value.End);
        RegexMatch? third = runner.Find(haystack, second!.Value.End);
        RegexMatch? afterLast = runner.Find(haystack, third!.Value.End);

        Assert.Equal(new RegexMatch(0, 2), first);
        Assert.Equal(new RegexMatch(2, 1), second);
        Assert.Equal(new RegexMatch(3, 2), third);
        Assert.Null(afterLast);
    }

    /// <summary>
    /// Verifies disposing a runner repeatedly returns its rented state at most once.
    /// </summary>
    [Fact]
    public void DisposeIsIdempotent()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        RegexFindRunner runner = automaton.RentFindRunner();

        Assert.True(runner.IsInitialized);

        runner.Dispose();
        runner.Dispose();

        Assert.False(runner.IsInitialized);

        using RegexFindRunner replacement = automaton.RentFindRunner();
        Assert.Equal(new RegexMatch(0, 2), replacement.Find("ab"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies copied values cannot use or return the same mutable Pike VM after one copy ends
    /// the shared generation lease.
    /// </summary>
    [Fact]
    public void CopiedValueCannotUseOrReturnPikeVmTwice()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        RegexFindRunner runner = automaton.RentFindRunner();
        RegexFindRunner copy = runner;
        Assert.True(runner.SharesPooledStateWith(copy));

        runner.Dispose();

        Assert.False(copy.IsInitialized);
        ObjectDisposedException? exception = null;
        try
        {
            _ = copy.Find("ab"u8, startAt: 0);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        copy.Dispose();

        RegexFindRunner first = automaton.RentFindRunner();
        RegexFindRunner second = automaton.RentFindRunner();
        try
        {
            Assert.False(first.SharesPooledStateWith(second));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    /// <summary>
    /// Verifies a disposed runner rejects subsequent searches.
    /// </summary>
    [Fact]
    public void FindAfterDisposeThrowsObjectDisposedException()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        RegexFindRunner runner = automaton.RentFindRunner();
        runner.Dispose();

        ObjectDisposedException? exception = null;
        try
        {
            _ = runner.Find("ab"u8, startAt: 0);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        Assert.Equal(nameof(RegexFindRunner), exception.ObjectName);
    }

    /// <summary>
    /// Verifies concurrent operations rent and use independent operation-scoped runners safely.
    /// </summary>
    /// <returns>A task that represents the concurrent runner verification.</returns>
    [Fact]
    public async Task ConcurrentOperationsRentIndependentRunnersAsync()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        using var barrier = new Barrier(participantCount: 2);
        byte[] firstHaystack = "abazzzab"u8.ToArray();
        byte[] secondHaystack = "a zz aba"u8.ToArray();

        Task<RegexMatch[]> first = Task.Run(
            () => FindAllAfterBarrier(automaton, barrier, firstHaystack));
        Task<RegexMatch[]> second = Task.Run(
            () => FindAllAfterBarrier(automaton, barrier, secondHaystack));

        RegexMatch[][] results = await Task.WhenAll(first, second).ConfigureAwait(true);

        Assert.Equal(
            [
                new RegexMatch(0, 2),
                new RegexMatch(2, 1),
                new RegexMatch(3, 3),
                new RegexMatch(6, 2),
            ],
            results[0]);
        Assert.Equal(
            [
                new RegexMatch(0, 1),
                new RegexMatch(2, 2),
                new RegexMatch(5, 2),
                new RegexMatch(7, 1),
            ],
            results[1]);
    }

    /// <summary>
    /// Verifies concurrent dense operations activate and use independent lazy-DFA state.
    /// </summary>
    /// <returns>A task that represents the concurrent runner verification.</returns>
    [Fact]
    public async Task ConcurrentDenseOperationsUseIndependentAnchoredDfasAsync()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"x[a-z]{50,1000}"u8.ToArray());
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50) + "\n");
        byte[] firstHaystack = CreateRepeatedHaystack(record, count: 128);
        byte[] secondHaystack = CreateRepeatedHaystack(record, count: 160);
        using var barrier = new Barrier(participantCount: 2);

        Task<RegexMatch[]> first = Task.Run(
            () => FindAllAfterBarrier(plan.Matcher, barrier, firstHaystack));
        Task<RegexMatch[]> second = Task.Run(
            () => FindAllAfterBarrier(plan.Matcher, barrier, secondHaystack));

        RegexMatch[][] results = await Task.WhenAll(first, second).ConfigureAwait(true);

        Assert.Equal(128, results[0].Length);
        Assert.Equal(160, results[1].Length);
        Assert.Equal(new RegexMatch(0, record.Length - 1), results[0][0]);
        Assert.Equal(
            new RegexMatch(127 * record.Length, record.Length - 1),
            results[0][^1]);
        Assert.Equal(new RegexMatch(0, record.Length - 1), results[1][0]);
        Assert.Equal(
            new RegexMatch(159 * record.Length, record.Length - 1),
            results[1][^1]);
    }

    /// <summary>
    /// Verifies a reusable ASCII-projected lazy DFA defers authority to the Unicode engine when
    /// non-ASCII input occurs before, inside, immediately after, or later than a projected match.
    /// </summary>
    [Fact]
    public void AsciiProjectedLazyDfaPreservesMatchesAcrossNonAsciiBoundaries()
    {
        RegexSearchPlan classPlan = CompileAsciiProjectedSearchPlan(
            @"x[a-z]{50,1000}"u8.ToArray());
        RegexAutomaton classFallback = CompileFallbackAutomaton(classPlan.Pattern);
        RegexSearchPlan dotPlan = CompileAsciiProjectedSearchPlan(
            @"x.{50,1000}"u8.ToArray());
        RegexAutomaton dotFallback = CompileFallbackAutomaton(dotPlan.Pattern);
        string prefix = new('!', 4_096);
        string asciiMatch = "x" + new string('a', 50);
        string[] classHaystacks =
        [
            prefix + "δ" + asciiMatch,
            prefix + asciiMatch + "δ",
            prefix + asciiMatch + " δ " + asciiMatch,
        ];
        byte[] insideProjectedMatch = System.Text.Encoding.UTF8.GetBytes(
            prefix + "x" + new string('a', 25) + "δ" + new string('a', 25) + "\n");

        AssertAsciiProjectedLazyDfa(classPlan.Matcher);
        AssertAsciiProjectedPath(dotPlan.Matcher);

        foreach (string haystackText in classHaystacks)
        {
            byte[] haystack = System.Text.Encoding.UTF8.GetBytes(haystackText);
            Assert.Equal(FindAll(classFallback, haystack), FindAll(classPlan.Matcher, haystack));
        }

        Assert.Equal(
            FindAll(dotFallback, insideProjectedMatch),
            FindAll(dotPlan.Matcher, insideProjectedMatch));
    }

    /// <summary>
    /// Verifies dense match-line output remains within a linear-work budget when the authoritative
    /// matcher uses an exact-start prefilter with an operation-scoped anchored lazy DFA.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void DenseAsciiMatchLineOutputCompletesWithinLinearWorkBudget()
    {
        const int RecordCount = 512 * 1_024;
        byte[] pattern = @"x[a-z]{50,1000}"u8.ToArray();
        byte[][] patterns = [pattern];
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50) + "\n");
        byte[] haystack = GC.AllocateUninitializedArray<byte>(record.Length * RecordCount);
        for (int offset = 0; offset < haystack.Length; offset += record.Length)
        {
            record.CopyTo(haystack, offset);
        }

        var sink = new CapturingMatchLineSink();
        bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink);

        Assert.True(matched);
        Assert.Equal((ulong)RecordCount, sink.Matches);
    }

    /// <summary>
    /// Verifies repeated searches of small independent records do not activate an operation-scoped
    /// DFA.
    /// </summary>
    [Fact]
    public void SmallIndependentRecordsDoNotActivateDfa()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"x[a-z]{50,1000}"u8.ToArray());
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50));
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();

        for (int index = 0; index < 1_024; index++)
        {
            Assert.Equal(new RegexMatch(0, record.Length), runner.Find(record, startAt: 0));
        }

        Assert.Equal(0, runner.AnchoredDfaLeaseVersion);
        Assert.Equal(0, runner.UnanchoredDfaLeaseVersion);
    }

    /// <summary>
    /// Verifies a no-prefilter runner rejects sub-threshold records before scanning them for
    /// ASCII-projection eligibility.
    /// </summary>
    [Fact]
    public void NoPrefilterSmallRecordsDoNotActivateUnanchoredDfa()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray());
        byte[] record = GC.AllocateUninitializedArray<byte>(4_000);
        record.AsSpan().Fill((byte)'!');
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();

        Assert.Equal(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
        for (int index = 0; index < 256; index++)
        {
            Assert.Null(runner.Find(record, startAt: 0));
        }

        Assert.Equal(0, runner.AnchoredDfaLeaseVersion);
        Assert.Equal(0, runner.UnanchoredDfaLeaseVersion);
    }

    /// <summary>
    /// Verifies a large dense exact-prefix search rents one anchored DFA and reuses its lease.
    /// </summary>
    [Fact]
    public void LargeDenseExactPrefixSearchReusesOneAnchoredDfaLease()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"x[a-z]{50,1000}"u8.ToArray());
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50) + "\n");
        byte[] haystack = CreateRepeatedHaystack(record, count: 128);
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();

        RegexMatch? first = runner.Find(haystack, startAt: 0);
        long leaseVersion = runner.AnchoredDfaLeaseVersion;
        RegexMatch? second = runner.Find(haystack, first!.Value.End);

        Assert.Equal(new RegexMatch(0, record.Length - 1), first);
        Assert.Equal(new RegexMatch(record.Length, record.Length - 1), second);
        Assert.True(leaseVersion > 0);
        Assert.Equal(leaseVersion, runner.AnchoredDfaLeaseVersion);
        Assert.Equal(0, runner.UnanchoredDfaLeaseVersion);
        Assert.False(runner.UsesAsciiProjection);
    }

    /// <summary>
    /// Verifies disposing a copied lazy runner invalidates every copy and returns the shared lease once.
    /// </summary>
    [Fact]
    public void CopiedValueCannotUseOrReturnAnchoredDfaTwice()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"x[a-z]{50,1000}"u8.ToArray());
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50) + "\n");
        byte[] haystack = CreateRepeatedHaystack(record, count: 128);
        RegexFindRunner runner = plan.Matcher.RentFindRunner();
        RegexFindRunner copy = runner;

        Assert.True(runner.SharesPooledStateWith(copy));
        Assert.NotNull(runner.Find(haystack, startAt: 0));
        Assert.True(runner.AnchoredDfaLeaseVersion > 0);
        Assert.Equal(0, runner.UnanchoredDfaLeaseVersion);

        copy.Dispose();

        Assert.False(runner.IsInitialized);
        ObjectDisposedException? exception = null;
        try
        {
            _ = runner.Find(haystack, startAt: 0);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        Assert.Equal(nameof(RegexFindRunner), exception.ObjectName);
        runner.Dispose();
    }

    /// <summary>
    /// Verifies an ordinary ASCII full-match search materializes the retained projected runner
    /// pool only when it is first needed.
    /// </summary>
    [Fact]
    public void AsciiWindowCreatesProjectedUnanchoredDfaPoolOnFirstUse()
    {
        byte[] pattern = @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            new string('!', 4_096) + "alpha bravo charl\n");

        Assert.True(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));

        RegexMatch? actual = runner.Find(haystack, startAt: 0);

        Assert.Equal(fallback.Find(haystack, startAt: 0), actual);
        Assert.Equal(0, runner.AnchoredDfaLeaseVersion);
        Assert.True(runner.UnanchoredDfaLeaseVersion > 0);
        Assert.True(runner.UsesAsciiProjection);
        Assert.True(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies concurrent first ASCII use publishes one projected runner pool and preserves
    /// authoritative results for every caller.
    /// </summary>
    /// <returns>A task that represents the concurrent first-use verification.</returns>
    [Fact(Timeout = 30_000)]
    public async Task ConcurrentFirstUsePublishesAsciiFastUnanchoredDfaPoolAsync()
    {
        const int OperationCount = 4;
        byte[] pattern = @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            new string('!', 4_096) + "alpha bravo charl\n");
        RegexMatch? expected = fallback.Find(haystack, startAt: 0);
        using var barrier = new Barrier(OperationCount);

        Assert.True(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Task<RegexMatch?>[] searches = Enumerable.Range(0, OperationCount)
            .Select(_ => Task.Run(() =>
            {
                using RegexFindRunner runner = plan.Matcher.RentFindRunner();
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("Concurrent ASCII runner synchronization timed out.");
                }

                return runner.Find(haystack, startAt: 0);
            }))
            .ToArray();

        RegexMatch?[] results = await Task.WhenAll(searches).ConfigureAwait(true);

        Assert.All(results, result => Assert.Equal(expected, result));
        Assert.True(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies a non-ASCII search window rents the primary unanchored DFA and remains equivalent
    /// to the authoritative fallback engine.
    /// </summary>
    [Fact]
    public void NonAsciiWindowUsesAuthoritativePrimaryUnanchoredDfa()
    {
        byte[] pattern = @"x.{50,1000}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "x" + new string('a', 25) + "δ" + new string('a', 25) + "\n");

        Assert.True(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
        Assert.False(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();
        Assert.False(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));

        RegexMatch? actual = runner.Find(haystack, startAt: 0);

        Assert.Equal(fallback.Find(haystack, startAt: 0), actual);
        Assert.Equal(0, runner.AnchoredDfaLeaseVersion);
        Assert.True(runner.UnanchoredDfaLeaseVersion > 0);
        Assert.False(runner.UsesAsciiProjection);
        Assert.True(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies concurrent first use publishes one primary runner pool and preserves authoritative
    /// results for every caller.
    /// </summary>
    /// <returns>A task that represents the concurrent first-use verification.</returns>
    [Fact(Timeout = 30_000)]
    public async Task ConcurrentFirstUsePublishesPrimaryUnanchoredDfaPoolAsync()
    {
        const int OperationCount = 4;
        byte[] pattern = @"x.{50,1000}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "x" + new string('a', 25) + "δ" + new string('a', 25) + "\n");
        RegexMatch? expected = fallback.Find(haystack, startAt: 0);
        using var barrier = new Barrier(OperationCount);

        Assert.True(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
        Assert.False(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        Task<RegexMatch?>[] searches = Enumerable.Range(0, OperationCount)
            .Select(_ => Task.Run(() =>
            {
                using RegexFindRunner runner = plan.Matcher.RentFindRunner();
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("Concurrent primary runner synchronization timed out.");
                }

                return runner.Find(haystack, startAt: 0);
            }))
            .ToArray();

        RegexMatch?[] results = await Task.WhenAll(searches).ConfigureAwait(true);

        Assert.All(results, result => Assert.Equal(expected, result));
        Assert.True(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies an independently bounded record uses the compact authoritative runner without
    /// materializing the expanded unanchored DFA reserved for whole-window searches.
    /// </summary>
    [Fact]
    public void RecordRunnerSkipsExpandedUnanchoredDfaForNonAsciiWindow()
    {
        byte[] pattern = @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            new string('!', 4_096) + "αβγδε ζηθικ λμνξο\n");

        Assert.False(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
        using RegexFindRunner runner = plan.Matcher.RentRecordFindRunner();

        RegexMatch? actual = runner.Find(haystack, startAt: 0);

        Assert.Equal(fallback.Find(haystack, startAt: 0), actual);
        Assert.Equal(0, runner.AnchoredDfaLeaseVersion);
        Assert.Equal(0, runner.UnanchoredDfaLeaseVersion);
        Assert.False(runner.UsesAsciiProjection);
        Assert.False(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
    }

    /// <summary>
    /// Verifies a cache-limited operation-scoped unanchored DFA preserves authoritative results
    /// when lazy execution exhausts its budget.
    /// </summary>
    [Fact]
    public void CacheLimitedUnanchoredDfaFallsBackAuthoritatively()
    {
        const string SourcePattern = "(?:a|b)*a(?:a|b){8}";
        byte[] combinedPattern = System.Text.Encoding.ASCII.GetBytes(
            $"(?:{SourcePattern})");
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(combinedPattern);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General,
            excludeLineTerminators: true,
            excludeCrLf: false,
            excludedLineTerminator: (byte)'\n');
        RegexNfa nfa = RegexNfaCompiler.Compile(tree.Root, options);
        byte[] haystack = CreateCachePressureHaystack();
        ulong giveUpBudget = FindGiveUpBudget(nfa, tree.Root, options, haystack);
        var matcher = RegexAutomaton.CompileParsed(
            tree,
            options,
            giveUpBudget,
            compilePrefilter: false);
        var fallback = RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: 0,
            compilePrefilter: false);
        using RegexFindRunner runner = matcher.RentFindRunner();

        RegexMatch[] expected = FindAll(fallback, haystack);
        var actual = new List<RegexMatch>();
        int startAt = 0;
        while (startAt < haystack.Length)
        {
            RegexMatch? match = runner.Find(haystack, startAt);
            if (!match.HasValue)
            {
                break;
            }

            actual.Add(match.Value);
            startAt = match.Value.End;
        }

        Assert.True(giveUpBudget > 0);
        Assert.True(runner.UnanchoredDfaLeaseVersion > 0);
        Assert.Equal(expected, actual);
    }

    private static RegexAutomaton CompilePikeAutomaton()
    {
        var automaton = RegexAutomaton.Compile(
            "(?:ab|a)|(?:z+z)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false,
            dfaSizeLimit: 0,
            specializationMode: RegexSpecializationMode.Fallback);

        Assert.Equal(RegexEngineKind.PikeVm, automaton.EngineKind);
        return automaton;
    }

    private static RegexSearchPlan CompileAsciiProjectedSearchPlan(byte[] pattern)
    {
        byte[][] patterns = [pattern];
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(plan);
        return plan;
    }

    private static void AssertAsciiProjectedLazyDfa(RegexAutomaton automaton)
    {
        Assert.Equal(RegexEngineKind.LazyDfa, automaton.EngineKind);
        AssertAsciiProjectedPath(automaton);
    }

    private static void AssertAsciiProjectedPath(RegexAutomaton automaton)
    {
        Assert.Equal(RegexPrefilterKind.Memmem, automaton.PrefilterKind);
        Assert.True(automaton.CanSearchWholeHaystackWithFullMatches);
        Assert.True(HasAsciiFastUnanchoredDfaRunner(automaton));
    }

    private static RegexAutomaton CompileFallbackAutomaton(ReadOnlyMemory<byte> pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern.Span);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.Fallback,
            excludeLineTerminators: true);
        return RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: 0,
            compilePrefilter: false);
    }

    private static bool HasAsciiFastUnanchoredDfaRunner(RegexAutomaton automaton)
    {
        return HasAsciiFastUnanchoredDfaFactory(automaton) ||
            HasCreatedAsciiFastUnanchoredDfaPool(automaton);
    }

    private static bool HasAsciiFastUnanchoredDfaFactory(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return typeof(RegexMetaEngine)
            .GetField("_asciiFastUnanchoredDfaFactory", Flags)?
            .GetValue(engine) is not null;
    }

    private static bool HasCreatedAsciiFastUnanchoredDfaPool(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return typeof(RegexMetaEngine)
            .GetField("_asciiFastUnanchoredDfaPool", Flags)?
            .GetValue(engine) is not null;
    }

    private static bool HasActivatedPrimaryUnanchoredDfa(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return Assert.IsType<int>(
            typeof(RegexMetaEngine).GetField("_unanchoredLazyDfaActivated", Flags)?.GetValue(engine)) != 0;
    }

    private static bool HasCreatedPrimaryUnanchoredDfaPool(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return typeof(RegexMetaEngine)
            .GetField("_unanchoredLazyDfaPool", Flags)?
            .GetValue(engine) is not null;
    }

    private static bool HasPrimaryUnanchoredDfaFactory(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return typeof(RegexMetaEngine)
            .GetField("_unanchoredLazyDfaFactory", Flags)?
            .GetValue(engine) is not null;
    }

    private static RegexMatch[] FindAll(RegexAutomaton automaton, byte[] haystack)
    {
        var matches = new List<RegexMatch>();
        using RegexFindRunner runner = automaton.RentFindRunner();
        int startAt = 0;
        while (startAt <= haystack.Length)
        {
            RegexMatch? match = runner.Find(haystack, startAt);
            if (!match.HasValue)
            {
                break;
            }

            matches.Add(match.Value);
            startAt = match.Value.End;
        }

        return matches.ToArray();
    }

    private static RegexMatch[] FindAllAfterBarrier(
        RegexAutomaton automaton,
        Barrier barrier,
        byte[] haystack)
    {
        var matches = new List<RegexMatch>();
        using RegexFindRunner runner = automaton.RentFindRunner();
        int startAt = 0;

        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Concurrent regex runner synchronization timed out.");
        }

        while (true)
        {
            RegexMatch? match = runner.Find(haystack, startAt);
            if (!match.HasValue)
            {
                return matches.ToArray();
            }

            matches.Add(match.Value);
            startAt = match.Value.End;
        }
    }

    private static byte[] CreateRepeatedHaystack(byte[] record, int count)
    {
        byte[] haystack = GC.AllocateUninitializedArray<byte>(record.Length * count);
        for (int offset = 0; offset < haystack.Length; offset += record.Length)
        {
            record.CopyTo(haystack, offset);
        }

        return haystack;
    }

    private static byte[] CreateCachePressureHaystack()
    {
        var builder = new System.Text.StringBuilder();
        for (int value = 0; value < 512; value++)
        {
            for (int bit = 11; bit >= 0; bit--)
            {
                builder.Append((value & 1 << bit) == 0 ? 'a' : 'b');
            }

            builder.Append('\n');
        }

        return System.Text.Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static ulong FindGiveUpBudget(
        RegexNfa nfa,
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ReadOnlySpan<byte> haystack)
    {
        for (ulong dfaSizeLimit = 16 * 1024; dfaSizeLimit <= 256 * 1024; dfaSizeLimit += 1024)
        {
            var factory = new RegexUnanchoredLazyDfaFactory(
                nfa,
                root,
                options,
                dfaSizeLimit);
            RegexUnanchoredLazyDfa? candidate = factory.Create();
            if (candidate is null)
            {
                continue;
            }

            int offset = 0;
            while (offset < haystack.Length)
            {
                bool found = candidate.TryFindEnd(
                    haystack,
                    offset,
                    out int end,
                    out bool gaveUp);
                if (gaveUp)
                {
                    return dfaSizeLimit;
                }

                if (!found)
                {
                    break;
                }

                offset = end;
            }
        }

        return 0;
    }

}
