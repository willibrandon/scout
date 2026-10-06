using System.Reflection;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies unanchored forward and reverse NFA construction estimates and hard budgets.
/// </summary>
[TestClass]
public sealed class RegexNfaConstructionBudgetTests()
{
    /// <summary>
    /// Verifies alternation estimates add every branch and its split topology instead of using
    /// only the largest branch.
    /// </summary>
    [TestMethod]
    public void AlternationEstimateRejectsCombinedBranchesThatIndividuallyFit()
    {
        string[] branches = Enumerable.Range(0, 256)
            .Select(static index => $"(?:a{{16}}z{index:D4})")
            .ToArray();
        RegexCompileOptions options = CreateAsciiOptions();
        RegexNfaConstructionEstimate[] individualEstimates = branches
            .Select(pattern => RegexNfaCompiler.EstimateUnanchoredConstruction(
                Parse(pattern).Root,
                options))
            .ToArray();

        ulong sizeLimit = 1;
        while (individualEstimates.Any(estimate => !estimate.Fits(sizeLimit)))
        {
            sizeLimit = checked(sizeLimit * 2);
        }

        RegexSyntaxTree combined = Parse(string.Join('|', branches));
        RegexNfaConstructionEstimate combinedEstimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(combined.Root, options);
        TestAssert.All(individualEstimates, estimate => Assert.IsTrue(estimate.Fits(sizeLimit)));
        Assert.IsFalse(combinedEstimate.Fits(sizeLimit));
        Assert.IsGreaterThan(
            individualEstimates.Max(static estimate => estimate.TotalStateCount), combinedEstimate.TotalStateCount);
    }

    /// <summary>
    /// Verifies UTF-8 lowering is counted independently in each direction and agrees with the
    /// exact emitted topology for a single Unicode atom.
    /// </summary>
    [TestMethod]
    public void UnicodeAtomEstimateTracksForwardAndReverseLoweringIndependently()
    {
        RegexSyntaxTree tree = Parse(@"\w");
        RegexCompileOptions options = CreateUnicodeOptions();
        RegexNfaConstructionEstimate estimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(tree.Root, options);
        RegexNfa forward = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        RegexNfa reverse = RegexNfaCompiler.CompileReversed(tree.Root, options);

        Assert.AreEqual((ulong)forward.States.Count, estimate.ForwardStateCount);
        Assert.AreEqual((ulong)reverse.States.Count, estimate.ReverseStateCount);
        Assert.AreNotEqual(estimate.ForwardStateCount - 2, estimate.ReverseStateCount);
    }

    /// <summary>
    /// Verifies a rejected expanded factory caches its negative result and cannot construct an
    /// oversized graph on later runner requests.
    /// </summary>
    [TestMethod]
    public void ExpandedFactoryPermanentlyCachesAlternationBudgetRejection()
    {
        string pattern = string.Join(
            '|',
            Enumerable.Range(0, 256).Select(static index => $"(?:a{{16}}z{index:D4})"));
        RegexSyntaxTree tree = Parse(pattern);
        RegexCompileOptions options = CreateAsciiOptions();
        var factory = new RegexExpandedUnanchoredLazyDfaFactory(
            tree.Root,
            options,
            dfaSizeLimit: 16 * 1024);

        Assert.IsNull(factory.Create());
        Assert.IsTrue(factory.IsPermanentlyRejected);
        Assert.IsNull(factory.Create());
        Assert.IsTrue(factory.IsPermanentlyRejected);
    }

    /// <summary>
    /// Verifies hard compiler reservations stop before exceeding the configured graph budget and
    /// roll back the abandoned partial construction.
    /// </summary>
    [TestMethod]
    public void HardConstructionBudgetRollsBackAbandonedGraph()
    {
        RegexSyntaxTree tree = Parse("(?:abcdefgh|ijklmnop){32}");
        var budget = new RegexNfaConstructionBudget(sizeLimit: 512);

        Assert.IsFalse(RegexNfaCompiler.TryCompileUnanchored(
            tree.Root,
            CreateAsciiOptions(),
            budget,
            out RegexNfa? nfa));
        Assert.IsNull(nfa);
        Assert.AreEqual(0UL, budget.UsedBytes);
    }

    /// <summary>
    /// Verifies the shared budget accounts for the materialized forward and reverse graphs and
    /// validates their actual state payloads before both are retained.
    /// </summary>
    [TestMethod]
    public void SharedBudgetMatchesMaterializedForwardAndReverseEstimate()
    {
        RegexSyntaxTree tree = Parse(@"\w{2,4}\s+\p{Greek}");
        RegexCompileOptions options = CreateUnicodeOptions();
        var budget = new RegexNfaConstructionBudget(sizeLimit: 16UL * 1024UL * 1024UL);

        Assert.IsTrue(RegexNfaCompiler.TryCompileUnanchored(
            tree.Root,
            options,
            budget,
            out RegexNfa? forward));
        Assert.IsTrue(RegexNfaCompiler.TryCompileReversed(
            tree.Root,
            options,
            budget,
            out RegexNfa? reverse));

        ulong retainedBytes = RegexNfaConstructionBudget.SaturatingAdd(
            RegexNfaConstructionBudget.EstimateRetainedBytes(forward!),
            RegexNfaConstructionBudget.EstimateRetainedBytes(reverse!));
        Assert.AreEqual(retainedBytes, budget.UsedBytes);
        Assert.IsTrue(budget.CanRetain(forward!, reverse!));
    }

    /// <summary>
    /// Verifies a lazy factory retains a forward runner when only later reverse reconstruction
    /// exceeds the remaining shared construction budget.
    /// </summary>
    [TestMethod]
    public void FactoryRetainsForwardRunnerWhenReverseExceedsRemainingBudget()
    {
        RegexSyntaxTree tree = Parse("(?:ab|ac){8}");
        RegexCompileOptions options = CreateAsciiOptions();
        RegexNfa anchored = RegexNfaCompiler.Compile(tree.Root, options);
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCompileForwardNfa(
            anchored,
            tree.Root,
            options,
            out RegexNfa? forward));
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCompileReverseNfa(
            tree.Root,
            options,
            out RegexNfa? reverse));
        ulong sizeLimit = GetMaximumForwardOnlyBudget(forward!, reverse!);
        var factory = new RegexUnanchoredLazyDfaFactory(
            anchored,
            tree.Root,
            options,
            sizeLimit);

        RegexUnanchoredLazyDfa? runner = factory.Create();

        Assert.IsNotNull(runner);
        Assert.AreEqual(0, factory.ReverseInitializationCount);
        byte[] haystack = Encoding.ASCII.GetBytes(
            "!!abababababababab!acacacacacacacac!!");
        Assert.IsTrue(runner!.TryFindEnd(
            haystack,
            startAt: 0,
            out int end,
            out bool endGaveUp));
        Assert.IsFalse(endGaveUp);
        Assert.AreEqual(18, end);
        Assert.IsTrue(runner.TryCountMatches(haystack, startAt: 0, out long count));
        Assert.AreEqual(2, count);
        Assert.IsFalse(runner.TryFind(
            haystack,
            startAt: 0,
            out _,
            out bool findGaveUp));
        Assert.IsTrue(findGaveUp);
        Assert.IsTrue(factory.IsReverseUnavailable);
        Assert.AreEqual(1, factory.ReverseInitializationCount);
        Assert.IsFalse(runner.TryFind(
            "!!acacacacacacacac!!"u8,
            startAt: 0,
            out _,
            out bool repeatedFindGaveUp));
        Assert.IsTrue(repeatedFindGaveUp);
        Assert.AreEqual(1, factory.ReverseInitializationCount);
    }

    /// <summary>
    /// Verifies copy-safe runner lease tokens end exactly once and cannot end a later lease.
    /// </summary>
    [TestMethod]
    public void RunnerLeaseTokensRejectDuplicateAndStaleEnds()
    {
        RegexSyntaxTree tree = Parse("ab");
        RegexCompileOptions options = CreateAsciiOptions();
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreate(
            tree.Root,
            options,
            dfaSizeLimit: 1024 * 1024,
            out RegexUnanchoredLazyDfa? runner));

        long first = runner!.BeginRunnerLease();

        Assert.AreNotEqual(0, first);
        Assert.IsTrue(runner.TryEndRunnerLease(first));
        Assert.IsFalse(runner.TryEndRunnerLease(first));

        long second = runner.BeginRunnerLease();

        Assert.IsGreaterThan(first, second);
        Assert.IsFalse(runner.TryEndRunnerLease(first));
        Assert.IsTrue(runner.TryEndRunnerLease(second));
    }

    /// <summary>
    /// Verifies public full-span search falls back authoritatively when a retained forward runner
    /// cannot add its reverse NFA within the shared construction budget.
    /// </summary>
    [TestMethod]
    public void AutomatonFallsBackAfterForwardOnlyRunnerRejectsReverseConstruction()
    {
        RegexSyntaxTree tree = Parse("(?:ab|ac){8}");
        RegexCompileOptions options = CreateAsciiOptions();
        RegexNfa anchored = RegexNfaCompiler.Compile(tree.Root, options);
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCompileForwardNfa(
            anchored,
            tree.Root,
            options,
            out RegexNfa? forward));
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCompileReverseNfa(
            tree.Root,
            options,
            out RegexNfa? reverse));
        ulong sizeLimit = GetForwardOnlyBudget(forward!, reverse!);
        var constrained = RegexAutomaton.CompileParsed(
            tree,
            options,
            sizeLimit,
            compilePrefilter: false);
        var fallback = RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit: 0,
            compilePrefilter: false);
        byte[] haystack = new byte[8192];
        Array.Fill(haystack, (byte)'!');
        haystack[0] = 0xCE;
        haystack[1] = 0xB4;
        Encoding.ASCII.GetBytes("abababababababab!").CopyTo(
            haystack,
            haystack.Length - 17);
        RegexMatch expected = Assert.IsExactInstanceOfType<RegexMatch>(fallback.Find(haystack));
        RegexMatchEndRunner matchEndRunner = constrained.RentMatchEndRunner(
            haystack,
            startAt: 0);

        try
        {
            Assert.IsTrue(matchEndRunner.IsAvailable);
            Assert.IsFalse(matchEndRunner.UsesAsciiProjection);
            Assert.IsTrue(matchEndRunner.TryFindEnd(
                haystack,
                startAt: 0,
                out int end,
                out bool completed));
            Assert.IsTrue(completed);
            Assert.AreEqual(expected.End, end);
        }
        finally
        {
            matchEndRunner.Dispose();
        }

        Assert.AreEqual(expected, constrained.Find(haystack));
    }

    /// <summary>
    /// Verifies expanded Unicode syntax admits a forward-only runner when reverse construction
    /// cannot fit alongside its retained forward graph.
    /// </summary>
    [TestMethod]
    public void ExpandedFactoryRetainsForwardRunnerWhenReverseExceedsRemainingBudget()
    {
        RegexSyntaxTree tree = Parse(@"\w");
        RegexCompileOptions options = CreateUnicodeOptions();
        RegexNfa forward = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        RegexNfa reverse = RegexNfaCompiler.CompileReversed(tree.Root, options);
        ulong sizeLimit = GetForwardOnlyBudget(forward, reverse);
        RegexNfaConstructionEstimate estimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(tree.Root, options);

        Assert.IsTrue(estimate.ForwardFits(sizeLimit));
        Assert.IsFalse(estimate.Fits(sizeLimit));
        Assert.IsTrue(RegexUnanchoredLazyDfa.CanCompileExpandedForwardNfaWithinBudget(
            tree.Root,
            options,
            sizeLimit));
        Assert.IsFalse(RegexUnanchoredLazyDfa.CanCompileExpandedNfaWithinBudget(
            tree.Root,
            options,
            sizeLimit));

        var factory = new RegexExpandedUnanchoredLazyDfaFactory(
            tree.Root,
            options,
            sizeLimit);
        RegexUnanchoredLazyDfa? runner = factory.Create();

        Assert.IsNotNull(runner);
        Assert.IsTrue(runner!.TryCountMatches("!!alpha!!"u8, startAt: 0, out long count));
        Assert.AreEqual(5, count);
        Assert.IsFalse(runner.TryFind(
            "!!alpha!!"u8,
            startAt: 0,
            out _,
            out bool gaveUp));
        Assert.IsTrue(gaveUp);
    }

    /// <summary>
    /// Verifies all-ASCII full-span searches use their compact projection without materializing
    /// the larger expanded Unicode runner, including authoritative no-match results.
    /// </summary>
    [TestMethod]
    public void AsciiFullSpanSearchDoesNotInitializeExpandedUnicodeRunner()
    {
        RegexAutomaton automaton = CreateProjectedUnicodeAutomaton();
        Lazy<RegexUnanchoredLazyDfaFactory?> expandedFactory =
            GetExpandedUnanchoredFactory(automaton);
        byte[] noMatch = Enumerable.Repeat((byte)'!', 8192).ToArray();

        Assert.IsFalse(expandedFactory.IsValueCreated);
        Assert.IsNull(automaton.Find(noMatch));
        Assert.AreEqual(0, automaton.SumMatchSpans(noMatch));
        Assert.IsFalse(expandedFactory.IsValueCreated);

        byte[] matching = Enumerable.Repeat((byte)'!', 8192).ToArray();
        "alpha bravo charl"u8.CopyTo(matching.AsSpan(4096));

        Assert.AreEqual(new RegexMatch(4096, 17), automaton.Find(matching));
        Assert.AreEqual(17, automaton.SumMatchSpans(matching));
        Assert.IsFalse(expandedFactory.IsValueCreated);
    }

    /// <summary>
    /// Verifies a mixed full-span sum bypasses the unsafe ASCII projection and falls through to
    /// the authoritative ordinary runner without retaining a projected partial total.
    /// </summary>
    [TestMethod]
    public void MixedFullSpanSumInitializesAuthoritativeExpandedRunner()
    {
        RegexAutomaton automaton = CreateProjectedUnicodeAutomaton();
        RegexAutomaton fallback = CreateProjectedUnicodeAutomaton(dfaSizeLimit: 0);
        Lazy<RegexUnanchoredLazyDfaFactory?> expandedFactory =
            GetExpandedUnanchoredFactory(automaton);
        byte[] haystack = Enumerable.Repeat((byte)'!', 8192).ToArray();
        "alpha bravo charl"u8.CopyTo(haystack.AsSpan(4096));
        haystack[6144] = 0xCE;
        haystack[6145] = 0xB4;

        long expected = fallback.SumMatchSpans(haystack);

        Assert.IsFalse(expandedFactory.IsValueCreated);
        Assert.AreEqual(expected, automaton.SumMatchSpans(haystack));
        Assert.IsTrue(expandedFactory.IsValueCreated);
    }

    private static RegexSyntaxTree Parse(string pattern)
    {
        return RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
    }

    private static RegexCompileOptions CreateAsciiOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false,
            excludeLineTerminators: true);
    }

    private static RegexCompileOptions CreateUnicodeOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true,
            excludeLineTerminators: true);
    }

    private static RegexAutomaton CreateProjectedUnicodeAutomaton(
        ulong dfaSizeLimit = 1024UL * 1024UL)
    {
        RegexSyntaxTree tree = Parse(@"\w{5}\s+\w{5}\s+\w{5}");
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General,
            excludeLineTerminators: true);
        return RegexAutomaton.CompileParsed(
            tree,
            options,
            dfaSizeLimit,
            compilePrefilter: false);
    }

    private static Lazy<RegexUnanchoredLazyDfaFactory?> GetExpandedUnanchoredFactory(
        RegexAutomaton automaton)
    {
        RegexMetaEngine engine = Assert.IsExactInstanceOfType<RegexMetaEngine>(
            typeof(RegexAutomaton)
                .GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(automaton));
        Func<RegexUnanchoredLazyDfa?> runnerFactory =
            Assert.IsExactInstanceOfType<Func<RegexUnanchoredLazyDfa?>>(
                typeof(RegexMetaEngine)
                    .GetField("_unanchoredLazyDfaFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(engine));
        RegexExpandedUnanchoredLazyDfaFactory expandedFactory =
            Assert.IsExactInstanceOfType<RegexExpandedUnanchoredLazyDfaFactory>(runnerFactory.Target);
        return Assert.IsExactInstanceOfType<Lazy<RegexUnanchoredLazyDfaFactory?>>(
            typeof(RegexExpandedUnanchoredLazyDfaFactory)
                .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(expandedFactory));
    }

    private static ulong GetForwardOnlyBudget(RegexNfa forward, RegexNfa reverse)
    {
        ulong forwardBytes = RegexNfaConstructionBudget.EstimateRetainedBytes(forward);
        ulong reverseBytes = RegexNfaConstructionBudget.EstimateRetainedBytes(reverse);
        ulong sizeLimit = RegexNfaConstructionBudget.SaturatingAdd(
            forwardBytes,
            Math.Max(1, reverseBytes / 2));
        ulong pairedBytes = RegexNfaConstructionBudget.SaturatingAdd(
            forwardBytes,
            reverseBytes);

        Assert.IsLessThan(sizeLimit, forwardBytes);
        Assert.IsLessThan(pairedBytes, sizeLimit);
        return sizeLimit;
    }

    private static ulong GetMaximumForwardOnlyBudget(RegexNfa forward, RegexNfa reverse)
    {
        ulong forwardBytes = RegexNfaConstructionBudget.EstimateRetainedBytes(forward);
        ulong reverseBytes = RegexNfaConstructionBudget.EstimateRetainedBytes(reverse);
        ulong pairedBytes = RegexNfaConstructionBudget.SaturatingAdd(
            forwardBytes,
            reverseBytes);
        ulong sizeLimit = pairedBytes - 1;

        Assert.IsLessThan(sizeLimit, forwardBytes);
        Assert.IsLessThan(pairedBytes, sizeLimit);
        return sizeLimit;
    }
}
