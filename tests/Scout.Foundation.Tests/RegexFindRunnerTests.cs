namespace Scout;

/// <summary>
/// Verifies operation-scoped authoritative regex runner ownership and reuse.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[TestClass]
public sealed class RegexFindRunnerTests(TestContext testContext)
{
    /// <summary>
    /// Verifies one runner preserves leftmost-first and non-overlapping behavior across sequential searches.
    /// </summary>
    [TestMethod]
    public void SequentialReusePreservesLeftmostNonOverlappingMatches()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        ReadOnlySpan<byte> haystack = "abazz"u8;
        using RegexFindRunner runner = automaton.RentFindRunner();

        RegexMatch? first = runner.Find(haystack, startAt: 0);
        RegexMatch? second = runner.Find(haystack, first!.Value.End);
        RegexMatch? third = runner.Find(haystack, second!.Value.End);
        RegexMatch? afterLast = runner.Find(haystack, third!.Value.End);

        Assert.AreEqual(new RegexMatch(0, 2), first);
        Assert.AreEqual(new RegexMatch(2, 1), second);
        Assert.AreEqual(new RegexMatch(3, 2), third);
        Assert.IsNull(afterLast);
    }

    /// <summary>
    /// Verifies disposing a runner repeatedly returns its rented state at most once.
    /// </summary>
    [TestMethod]
    public void DisposeIsIdempotent()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        RegexFindRunner runner = automaton.RentFindRunner();

        Assert.IsTrue(runner.IsInitialized);

        runner.Dispose();
        runner.Dispose();

        Assert.IsFalse(runner.IsInitialized);

        using RegexFindRunner replacement = automaton.RentFindRunner();
        Assert.AreEqual(new RegexMatch(0, 2), replacement.Find("ab"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies copied values cannot use or return the same mutable Pike VM after one copy ends
    /// the shared generation lease.
    /// </summary>
    [TestMethod]
    public void CopiedValueCannotUseOrReturnPikeVmTwice()
    {
        RegexAutomaton automaton = CompilePikeAutomaton();
        RegexFindRunner runner = automaton.RentFindRunner();
        RegexFindRunner copy = runner;
        Assert.IsTrue(runner.SharesPooledStateWith(copy));

        runner.Dispose();

        Assert.IsFalse(copy.IsInitialized);
        ObjectDisposedException? exception = null;
        try
        {
            _ = copy.Find("ab"u8, startAt: 0);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        copy.Dispose();

        RegexFindRunner first = automaton.RentFindRunner();
        RegexFindRunner second = automaton.RentFindRunner();
        try
        {
            Assert.IsFalse(first.SharesPooledStateWith(second));
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
    [TestMethod]
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

        Assert.IsNotNull(exception);
        Assert.AreEqual(nameof(RegexFindRunner), exception.ObjectName);
    }

    /// <summary>
    /// Verifies concurrent operations rent and use independent operation-scoped runners safely.
    /// </summary>
    /// <returns>A task that represents the concurrent runner verification.</returns>
    [TestMethod]
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

        Assert.AreSequenceEqual<RegexMatch>(
            [
                new RegexMatch(0, 2),
                new RegexMatch(2, 1),
                new RegexMatch(3, 3),
                new RegexMatch(6, 2),
            ],
            results[0]);
        Assert.AreSequenceEqual<RegexMatch>(
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
    [TestMethod]
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

        Assert.HasCount(128, results[0]);
        Assert.HasCount(160, results[1]);
        Assert.AreEqual(new RegexMatch(0, record.Length - 1), results[0][0]);
        Assert.AreEqual(
            new RegexMatch(127 * record.Length, record.Length - 1),
            results[0][^1]);
        Assert.AreEqual(new RegexMatch(0, record.Length - 1), results[1][0]);
        Assert.AreEqual(
            new RegexMatch(159 * record.Length, record.Length - 1),
            results[1][^1]);
    }

    /// <summary>
    /// Verifies a reusable ASCII-projected lazy DFA defers authority to the Unicode engine when
    /// non-ASCII input occurs before, inside, immediately after, or later than a projected match.
    /// </summary>
    [TestMethod]
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
            Assert.AreSequenceEqual(FindAll(classFallback, haystack), FindAll(classPlan.Matcher, haystack));
        }

        Assert.AreSequenceEqual(
            FindAll(dotFallback, insideProjectedMatch),
            FindAll(dotPlan.Matcher, insideProjectedMatch));
    }

    /// <summary>
    /// Verifies dense match-line output remains within a linear-work budget when the authoritative
    /// matcher uses an exact-start prefilter with an operation-scoped anchored lazy DFA.
    /// </summary>
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void DenseAsciiMatchLineOutputCompletesWithinLinearWorkBudget()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        const int RecordCount = 512 * 1_024;
        byte[] pattern = @"x[a-z]{50,1000}"u8.ToArray();
        byte[][] patterns = [pattern];
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50) + "\n");
        byte[] haystack = GC.AllocateUninitializedArray<byte>(record.Length * RecordCount);
        for (int offset = 0; offset < haystack.Length; offset += record.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            record.CopyTo(haystack, offset);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sink = new CapturingMatchLineSink();
        bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink);

        cancellationToken.ThrowIfCancellationRequested();
        Assert.IsTrue(matched);
        Assert.AreEqual((ulong)RecordCount, sink.Matches);
    }

    /// <summary>
    /// Verifies repeated searches of small independent records do not activate an operation-scoped
    /// DFA.
    /// </summary>
    [TestMethod]
    public void SmallIndependentRecordsDoNotActivateDfa()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"x[a-z]{50,1000}"u8.ToArray());
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50));
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();

        for (int index = 0; index < 1_024; index++)
        {
            Assert.AreEqual(new RegexMatch(0, record.Length), runner.Find(record, startAt: 0));
        }

        Assert.AreEqual(0, runner.AnchoredDfaLeaseVersion);
        Assert.AreEqual(0, runner.UnanchoredDfaLeaseVersion);
    }

    /// <summary>
    /// Verifies a no-prefilter runner rejects sub-threshold records before scanning them for
    /// ASCII-projection eligibility.
    /// </summary>
    [TestMethod]
    public void NoPrefilterSmallRecordsDoNotActivateUnanchoredDfa()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray());
        byte[] record = GC.AllocateUninitializedArray<byte>(4_000);
        record.AsSpan().Fill((byte)'!');
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();

        Assert.AreEqual(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
        for (int index = 0; index < 256; index++)
        {
            Assert.IsNull(runner.Find(record, startAt: 0));
        }

        Assert.AreEqual(0, runner.AnchoredDfaLeaseVersion);
        Assert.AreEqual(0, runner.UnanchoredDfaLeaseVersion);
    }

    /// <summary>
    /// Verifies a large dense exact-prefix search rents one anchored DFA and reuses its lease.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(new RegexMatch(0, record.Length - 1), first);
        Assert.AreEqual(new RegexMatch(record.Length, record.Length - 1), second);
        Assert.IsGreaterThan(0, leaseVersion);
        Assert.AreEqual(leaseVersion, runner.AnchoredDfaLeaseVersion);
        Assert.AreEqual(0, runner.UnanchoredDfaLeaseVersion);
        Assert.IsFalse(runner.UsesAsciiProjection);
    }

    /// <summary>
    /// Verifies disposing a copied lazy runner invalidates every copy and returns the shared lease once.
    /// </summary>
    [TestMethod]
    public void CopiedValueCannotUseOrReturnAnchoredDfaTwice()
    {
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(
            @"x[a-z]{50,1000}"u8.ToArray());
        byte[] record = System.Text.Encoding.ASCII.GetBytes(
            "x" + new string('a', 50) + "\n");
        byte[] haystack = CreateRepeatedHaystack(record, count: 128);
        RegexFindRunner runner = plan.Matcher.RentFindRunner();
        RegexFindRunner copy = runner;

        Assert.IsTrue(runner.SharesPooledStateWith(copy));
        Assert.IsNotNull(runner.Find(haystack, startAt: 0));
        Assert.IsGreaterThan(0, runner.AnchoredDfaLeaseVersion);
        Assert.AreEqual(0, runner.UnanchoredDfaLeaseVersion);

        copy.Dispose();

        Assert.IsFalse(runner.IsInitialized);
        ObjectDisposedException? exception = null;
        try
        {
            _ = runner.Find(haystack, startAt: 0);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        Assert.AreEqual(nameof(RegexFindRunner), exception.ObjectName);
        runner.Dispose();
    }

    /// <summary>
    /// Verifies an ordinary ASCII full-match search materializes the retained projected runner
    /// pool only when it is first needed.
    /// </summary>
    [TestMethod]
    public void AsciiWindowCreatesProjectedUnanchoredDfaPoolOnFirstUse()
    {
        byte[] pattern = @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            new string('!', 4_096) + "alpha bravo charl\n");

        Assert.IsTrue(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
        Assert.IsFalse(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();
        Assert.IsFalse(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));

        RegexMatch? actual = runner.Find(haystack, startAt: 0);

        Assert.AreEqual(fallback.Find(haystack, startAt: 0), actual);
        Assert.AreEqual(0, runner.AnchoredDfaLeaseVersion);
        Assert.IsGreaterThan(0, runner.UnanchoredDfaLeaseVersion);
        Assert.IsTrue(runner.UsesAsciiProjection);
        Assert.IsTrue(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.IsFalse(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies concurrent first ASCII use publishes one projected runner pool and preserves
    /// authoritative results for every caller.
    /// </summary>
    /// <returns>A task that represents the concurrent first-use verification.</returns>
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ConcurrentFirstUsePublishesAsciiFastUnanchoredDfaPoolAsync()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        const int OperationCount = 4;
        byte[] pattern = @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            new string('!', 4_096) + "alpha bravo charl\n");
        RegexMatch? expected = fallback.Find(haystack, startAt: 0);
        using var barrier = new Barrier(OperationCount);

        Assert.IsTrue(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
        Assert.IsFalse(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Task<RegexMatch?>[] searches = Enumerable.Range(0, OperationCount)
            .Select(_ => Task.Run(() =>
            {
                using RegexFindRunner runner = plan.Matcher.RentFindRunner();
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    throw new TimeoutException("Concurrent ASCII runner synchronization timed out.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                return runner.Find(haystack, startAt: 0);
            }, cancellationToken))
            .ToArray();

        RegexMatch?[] results = await Task.WhenAll(searches).ConfigureAwait(true);
        TestAssert.All(results, result => Assert.AreEqual(expected, result));
        Assert.IsTrue(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.IsFalse(HasAsciiFastUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies a non-ASCII search window rents the primary unanchored DFA and remains equivalent
    /// to the authoritative fallback engine.
    /// </summary>
    [TestMethod]
    public void NonAsciiWindowUsesAuthoritativePrimaryUnanchoredDfa()
    {
        byte[] pattern = @"x.{50,1000}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "x" + new string('a', 25) + "δ" + new string('a', 25) + "\n");

        Assert.IsTrue(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
        Assert.IsFalse(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        using RegexFindRunner runner = plan.Matcher.RentFindRunner();
        Assert.IsFalse(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));

        RegexMatch? actual = runner.Find(haystack, startAt: 0);

        Assert.AreEqual(fallback.Find(haystack, startAt: 0), actual);
        Assert.AreEqual(0, runner.AnchoredDfaLeaseVersion);
        Assert.IsGreaterThan(0, runner.UnanchoredDfaLeaseVersion);
        Assert.IsFalse(runner.UsesAsciiProjection);
        Assert.IsTrue(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        Assert.IsFalse(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies concurrent first use publishes one primary runner pool and preserves authoritative
    /// results for every caller.
    /// </summary>
    /// <returns>A task that represents the concurrent first-use verification.</returns>
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ConcurrentFirstUsePublishesPrimaryUnanchoredDfaPoolAsync()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        const int OperationCount = 4;
        byte[] pattern = @"x.{50,1000}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "x" + new string('a', 25) + "δ" + new string('a', 25) + "\n");
        RegexMatch? expected = fallback.Find(haystack, startAt: 0);
        using var barrier = new Barrier(OperationCount);

        Assert.IsTrue(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
        Assert.IsFalse(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        Task<RegexMatch?>[] searches = Enumerable.Range(0, OperationCount)
            .Select(_ => Task.Run(() =>
            {
                using RegexFindRunner runner = plan.Matcher.RentFindRunner();
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    throw new TimeoutException("Concurrent primary runner synchronization timed out.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                return runner.Find(haystack, startAt: 0);
            }, cancellationToken))
            .ToArray();

        RegexMatch?[] results = await Task.WhenAll(searches).ConfigureAwait(true);
        TestAssert.All(results, result => Assert.AreEqual(expected, result));
        Assert.IsTrue(HasCreatedPrimaryUnanchoredDfaPool(plan.Matcher));
        Assert.IsFalse(HasPrimaryUnanchoredDfaFactory(plan.Matcher));
    }

    /// <summary>
    /// Verifies an independently bounded record uses the compact authoritative runner without
    /// materializing the expanded unanchored DFA reserved for whole-window searches.
    /// </summary>
    [TestMethod]
    public void RecordRunnerSkipsExpandedUnanchoredDfaForNonAsciiWindow()
    {
        byte[] pattern = @"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray();
        RegexSearchPlan plan = CompileAsciiProjectedSearchPlan(pattern);
        RegexAutomaton fallback = CompileFallbackAutomaton(pattern);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            new string('!', 4_096) + "αβγδε ζηθικ λμνξο\n");

        Assert.IsFalse(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
        using RegexFindRunner runner = plan.Matcher.RentRecordFindRunner();

        RegexMatch? actual = runner.Find(haystack, startAt: 0);

        Assert.AreEqual(fallback.Find(haystack, startAt: 0), actual);
        Assert.AreEqual(0, runner.AnchoredDfaLeaseVersion);
        Assert.AreEqual(0, runner.UnanchoredDfaLeaseVersion);
        Assert.IsFalse(runner.UsesAsciiProjection);
        Assert.IsFalse(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
    }

    /// <summary>
    /// Verifies an independently bounded record runner retains one one-pass DFA across exact
    /// candidate checks and subsequent authoritative searches.
    /// </summary>
    [TestMethod]
    public void CandidateRecordRunnerReusesOnePassDfaAcrossExactChecksAndFinds()
    {
        RegexSearchPlan plan = CompileOnePassRecordPlan();
        using RegexFindRunner runner = plan.Matcher.RentCandidateRecordFindRunner();
        long leaseVersion = runner.OnePassDfaLeaseVersion;

        bool matchedAtCandidate = runner.TryMatchAt(
            "struct First\n"u8,
            startAt: 0,
            out int length);
        RegexMatch? later = runner.Find("struct! enum Second\n"u8, startAt: 1);

        Assert.IsGreaterThan(0, leaseVersion);
        Assert.IsTrue(matchedAtCandidate);
        Assert.AreEqual(12, length);
        Assert.AreEqual(new RegexMatch(8, 11), later);
        Assert.AreEqual(leaseVersion, runner.OnePassDfaLeaseVersion);
        Assert.AreEqual(0, runner.AnchoredDfaLeaseVersion);
        Assert.AreEqual(0, runner.UnanchoredDfaLeaseVersion);
    }

    /// <summary>
    /// Verifies copied record runners cannot use or return the same mutable one-pass DFA after
    /// one copy ends its generation lease.
    /// </summary>
    [TestMethod]
    public void CopiedValueCannotUseOrReturnOnePassDfaTwice()
    {
        RegexSearchPlan plan = CompileOnePassRecordPlan();
        RegexFindRunner runner = plan.Matcher.RentCandidateRecordFindRunner();
        RegexFindRunner copy = runner;

        Assert.IsGreaterThan(0, runner.OnePassDfaLeaseVersion);
        Assert.IsTrue(runner.SharesPooledStateWith(copy));

        runner.Dispose();

        Assert.IsFalse(copy.IsInitialized);
        ObjectDisposedException? exception = null;
        try
        {
            _ = copy.TryMatchAt("struct First\n"u8, startAt: 0, out _);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        Assert.AreEqual(nameof(RegexFindRunner), exception.ObjectName);

        exception = null;
        try
        {
            _ = copy.Find("struct First\n"u8, startAt: 0);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        Assert.AreEqual(nameof(RegexFindRunner), exception.ObjectName);
        copy.Dispose();

        RegexFindRunner first = plan.Matcher.RentCandidateRecordFindRunner();
        RegexFindRunner second = plan.Matcher.RentCandidateRecordFindRunner();
        try
        {
            Assert.IsFalse(first.SharesPooledStateWith(second));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    /// <summary>
    /// Verifies a cache-limited operation-scoped unanchored DFA preserves authoritative results
    /// when lazy execution exhausts its budget.
    /// </summary>
    [TestMethod]
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

        Assert.IsGreaterThan(0UL, giveUpBudget);
        Assert.IsGreaterThan(0, runner.UnanchoredDfaLeaseVersion);
        Assert.AreSequenceEqual(expected, actual);
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

        Assert.AreEqual(RegexEngineKind.PikeVm, automaton.EngineKind);
        return automaton;
    }

    private static RegexSearchPlan CompileAsciiProjectedSearchPlan(byte[] pattern)
    {
        byte[][] patterns = [pattern];
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(plan);
        return plan;
    }

    private static RegexSearchPlan CompileOnePassRecordPlan()
    {
        byte[][] patterns =
        [
            @"\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*"u8.ToArray(),
        ];
        using RegexSpecializationModeScope scope =
            RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.AreEqual(RegexEngineKind.OnePassDfa, plan.Matcher.EngineKind);
        return plan;
    }

    private static void AssertAsciiProjectedLazyDfa(RegexAutomaton automaton)
    {
        Assert.AreEqual(RegexEngineKind.LazyDfa, automaton.EngineKind);
        AssertAsciiProjectedPath(automaton);
    }

    private static void AssertAsciiProjectedPath(RegexAutomaton automaton)
    {
        Assert.AreEqual(RegexPrefilterKind.Memmem, automaton.PrefilterKind);
        Assert.IsTrue(automaton.CanSearchWholeHaystackWithFullMatches);
        Assert.IsTrue(HasAsciiFastUnanchoredDfaRunner(automaton));
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
        RegexMetaEngine engine = Assert.IsExactInstanceOfType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return typeof(RegexMetaEngine)
            .GetField("_asciiFastUnanchoredDfaFactory", Flags)?
            .GetValue(engine) is not null;
    }

    private static bool HasCreatedAsciiFastUnanchoredDfaPool(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsExactInstanceOfType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return typeof(RegexMetaEngine)
            .GetField("_asciiFastUnanchoredDfaPool", Flags)?
            .GetValue(engine) is not null;
    }

    private static bool HasActivatedPrimaryUnanchoredDfa(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsExactInstanceOfType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return Assert.IsExactInstanceOfType<int>(
            typeof(RegexMetaEngine).GetField("_unanchoredLazyDfaActivated", Flags)?.GetValue(engine)) != 0;
    }

    private static bool HasCreatedPrimaryUnanchoredDfaPool(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsExactInstanceOfType<RegexMetaEngine>(
            typeof(RegexAutomaton).GetField("engine", Flags)?.GetValue(automaton));
        return typeof(RegexMetaEngine)
            .GetField("_unanchoredLazyDfaPool", Flags)?
            .GetValue(engine) is not null;
    }

    private static bool HasPrimaryUnanchoredDfaFactory(RegexAutomaton automaton)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        RegexMetaEngine engine = Assert.IsExactInstanceOfType<RegexMetaEngine>(
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
