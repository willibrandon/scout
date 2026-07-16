using System.Reflection;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies unanchored forward and reverse NFA construction estimates and hard budgets.
/// </summary>
public sealed class RegexNfaConstructionBudgetTests()
{
    /// <summary>
    /// Verifies alternation estimates add every branch and its split topology instead of using
    /// only the largest branch.
    /// </summary>
    [Fact]
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

        Assert.All(individualEstimates, estimate => Assert.True(estimate.Fits(sizeLimit)));
        Assert.False(combinedEstimate.Fits(sizeLimit));
        Assert.True(
            combinedEstimate.TotalStateCount >
            individualEstimates.Max(static estimate => estimate.TotalStateCount));
    }

    /// <summary>
    /// Verifies UTF-8 lowering is counted independently in each direction and agrees with the
    /// exact emitted topology for a single Unicode atom.
    /// </summary>
    [Fact]
    public void UnicodeAtomEstimateTracksForwardAndReverseLoweringIndependently()
    {
        RegexSyntaxTree tree = Parse(@"\w");
        RegexCompileOptions options = CreateUnicodeOptions();
        RegexNfaConstructionEstimate estimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(tree.Root, options);
        RegexNfa forward = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        RegexNfa reverse = RegexNfaCompiler.CompileReversed(tree.Root, options);

        Assert.Equal((ulong)forward.States.Count, estimate.ForwardStateCount);
        Assert.Equal((ulong)reverse.States.Count, estimate.ReverseStateCount);
        Assert.NotEqual(estimate.ForwardStateCount - 2, estimate.ReverseStateCount);
    }

    /// <summary>
    /// Verifies a rejected expanded factory caches its negative result and cannot construct an
    /// oversized graph on later runner requests.
    /// </summary>
    [Fact]
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

        Assert.Null(factory.Create());
        Assert.True(factory.IsPermanentlyRejected);
        Assert.Null(factory.Create());
        Assert.True(factory.IsPermanentlyRejected);
    }

    /// <summary>
    /// Verifies hard compiler reservations stop before exceeding the configured graph budget and
    /// roll back the abandoned partial construction.
    /// </summary>
    [Fact]
    public void HardConstructionBudgetRollsBackAbandonedGraph()
    {
        RegexSyntaxTree tree = Parse("(?:abcdefgh|ijklmnop){32}");
        var budget = new RegexNfaConstructionBudget(sizeLimit: 512);

        Assert.False(RegexNfaCompiler.TryCompileUnanchored(
            tree.Root,
            CreateAsciiOptions(),
            budget,
            out RegexNfa? nfa));
        Assert.Null(nfa);
        Assert.Equal(0UL, budget.UsedBytes);
    }

    /// <summary>
    /// Verifies the shared budget accounts for the materialized forward and reverse graphs and
    /// validates their actual state payloads before both are retained.
    /// </summary>
    [Fact]
    public void SharedBudgetMatchesMaterializedForwardAndReverseEstimate()
    {
        RegexSyntaxTree tree = Parse(@"\w{2,4}\s+\p{Greek}");
        RegexCompileOptions options = CreateUnicodeOptions();
        var budget = new RegexNfaConstructionBudget(sizeLimit: 16UL * 1024UL * 1024UL);

        Assert.True(RegexNfaCompiler.TryCompileUnanchored(
            tree.Root,
            options,
            budget,
            out RegexNfa? forward));
        Assert.True(RegexNfaCompiler.TryCompileReversed(
            tree.Root,
            options,
            budget,
            out RegexNfa? reverse));

        ulong retainedBytes = RegexNfaConstructionBudget.SaturatingAdd(
            RegexNfaConstructionBudget.EstimateRetainedBytes(forward!),
            RegexNfaConstructionBudget.EstimateRetainedBytes(reverse!));
        Assert.Equal(retainedBytes, budget.UsedBytes);
        Assert.True(budget.CanRetain(forward!, reverse!));
    }

    /// <summary>
    /// Verifies a lazy factory retains a forward runner when only later reverse reconstruction
    /// exceeds the remaining shared construction budget.
    /// </summary>
    [Fact]
    public void FactoryRetainsForwardRunnerWhenReverseExceedsRemainingBudget()
    {
        RegexSyntaxTree tree = Parse("(?:ab|ac){8}");
        RegexCompileOptions options = CreateAsciiOptions();
        RegexNfa anchored = RegexNfaCompiler.Compile(tree.Root, options);
        Assert.True(RegexUnanchoredLazyDfa.TryCompileForwardNfa(
            anchored,
            tree.Root,
            options,
            out RegexNfa? forward));
        Assert.True(RegexUnanchoredLazyDfa.TryCompileReverseNfa(
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

        Assert.NotNull(runner);
        Assert.Equal(0, factory.ReverseInitializationCount);
        byte[] haystack = Encoding.ASCII.GetBytes(
            "!!abababababababab!acacacacacacacac!!");
        Assert.True(runner!.TryFindEnd(
            haystack,
            startAt: 0,
            out int end,
            out bool endGaveUp));
        Assert.False(endGaveUp);
        Assert.Equal(18, end);
        Assert.True(runner.TryCountMatches(haystack, startAt: 0, out long count));
        Assert.Equal(2, count);
        Assert.False(runner.TryFind(
            haystack,
            startAt: 0,
            out _,
            out bool findGaveUp));
        Assert.True(findGaveUp);
        Assert.True(factory.IsReverseUnavailable);
        Assert.Equal(1, factory.ReverseInitializationCount);
        Assert.False(runner.TryFind(
            "!!acacacacacacacac!!"u8,
            startAt: 0,
            out _,
            out bool repeatedFindGaveUp));
        Assert.True(repeatedFindGaveUp);
        Assert.Equal(1, factory.ReverseInitializationCount);
    }

    /// <summary>
    /// Verifies copy-safe runner lease tokens end exactly once and cannot end a later lease.
    /// </summary>
    [Fact]
    public void RunnerLeaseTokensRejectDuplicateAndStaleEnds()
    {
        RegexSyntaxTree tree = Parse("ab");
        RegexCompileOptions options = CreateAsciiOptions();
        Assert.True(RegexUnanchoredLazyDfa.TryCreate(
            tree.Root,
            options,
            dfaSizeLimit: 1024 * 1024,
            out RegexUnanchoredLazyDfa? runner));

        long first = runner!.BeginRunnerLease();

        Assert.NotEqual(0, first);
        Assert.True(runner.TryEndRunnerLease(first));
        Assert.False(runner.TryEndRunnerLease(first));

        long second = runner.BeginRunnerLease();

        Assert.True(second > first);
        Assert.False(runner.TryEndRunnerLease(first));
        Assert.True(runner.TryEndRunnerLease(second));
    }

    /// <summary>
    /// Verifies public full-span search falls back authoritatively when a retained forward runner
    /// cannot add its reverse NFA within the shared construction budget.
    /// </summary>
    [Fact]
    public void AutomatonFallsBackAfterForwardOnlyRunnerRejectsReverseConstruction()
    {
        RegexSyntaxTree tree = Parse("(?:ab|ac){8}");
        RegexCompileOptions options = CreateAsciiOptions();
        RegexNfa anchored = RegexNfaCompiler.Compile(tree.Root, options);
        Assert.True(RegexUnanchoredLazyDfa.TryCompileForwardNfa(
            anchored,
            tree.Root,
            options,
            out RegexNfa? forward));
        Assert.True(RegexUnanchoredLazyDfa.TryCompileReverseNfa(
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
        RegexMatch expected = Assert.IsType<RegexMatch>(fallback.Find(haystack));
        RegexMatchEndRunner matchEndRunner = constrained.RentMatchEndRunner(
            haystack,
            startAt: 0);

        try
        {
            Assert.True(matchEndRunner.IsAvailable);
            Assert.False(matchEndRunner.UsesAsciiProjection);
            Assert.True(matchEndRunner.TryFindEnd(
                haystack,
                startAt: 0,
                out int end,
                out bool completed));
            Assert.True(completed);
            Assert.Equal(expected.End, end);
        }
        finally
        {
            matchEndRunner.Dispose();
        }

        Assert.Equal(expected, constrained.Find(haystack));
    }

    /// <summary>
    /// Verifies expanded Unicode syntax admits a forward-only runner when reverse construction
    /// cannot fit alongside its retained forward graph.
    /// </summary>
    [Fact]
    public void ExpandedFactoryRetainsForwardRunnerWhenReverseExceedsRemainingBudget()
    {
        RegexSyntaxTree tree = Parse(@"\w");
        RegexCompileOptions options = CreateUnicodeOptions();
        RegexNfa forward = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        RegexNfa reverse = RegexNfaCompiler.CompileReversed(tree.Root, options);
        ulong sizeLimit = GetForwardOnlyBudget(forward, reverse);
        RegexNfaConstructionEstimate estimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(tree.Root, options);

        Assert.True(estimate.ForwardFits(sizeLimit));
        Assert.False(estimate.Fits(sizeLimit));
        Assert.True(RegexUnanchoredLazyDfa.CanCompileExpandedForwardNfaWithinBudget(
            tree.Root,
            options,
            sizeLimit));
        Assert.False(RegexUnanchoredLazyDfa.CanCompileExpandedNfaWithinBudget(
            tree.Root,
            options,
            sizeLimit));

        var factory = new RegexExpandedUnanchoredLazyDfaFactory(
            tree.Root,
            options,
            sizeLimit);
        RegexUnanchoredLazyDfa? runner = factory.Create();

        Assert.NotNull(runner);
        Assert.True(runner!.TryCountMatches("!!alpha!!"u8, startAt: 0, out long count));
        Assert.Equal(5, count);
        Assert.False(runner.TryFind(
            "!!alpha!!"u8,
            startAt: 0,
            out _,
            out bool gaveUp));
        Assert.True(gaveUp);
    }

    /// <summary>
    /// Verifies all-ASCII full-span searches use their compact projection without materializing
    /// the larger expanded Unicode runner, including authoritative no-match results.
    /// </summary>
    [Fact]
    public void AsciiFullSpanSearchDoesNotInitializeExpandedUnicodeRunner()
    {
        RegexAutomaton automaton = CreateProjectedUnicodeAutomaton();
        Lazy<RegexUnanchoredLazyDfaFactory?> expandedFactory =
            GetExpandedUnanchoredFactory(automaton);
        byte[] noMatch = Enumerable.Repeat((byte)'!', 8192).ToArray();

        Assert.False(expandedFactory.IsValueCreated);
        Assert.Null(automaton.Find(noMatch));
        Assert.Equal(0, automaton.SumMatchSpans(noMatch));
        Assert.False(expandedFactory.IsValueCreated);

        byte[] matching = Enumerable.Repeat((byte)'!', 8192).ToArray();
        "alpha bravo charl"u8.CopyTo(matching.AsSpan(4096));

        Assert.Equal(new RegexMatch(4096, 17), automaton.Find(matching));
        Assert.Equal(17, automaton.SumMatchSpans(matching));
        Assert.False(expandedFactory.IsValueCreated);
    }

    /// <summary>
    /// Verifies a mixed full-span sum bypasses the unsafe ASCII projection and falls through to
    /// the authoritative ordinary runner without retaining a projected partial total.
    /// </summary>
    [Fact]
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

        Assert.False(expandedFactory.IsValueCreated);
        Assert.Equal(expected, automaton.SumMatchSpans(haystack));
        Assert.True(expandedFactory.IsValueCreated);
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
        RegexMetaEngine engine = Assert.IsType<RegexMetaEngine>(
            typeof(RegexAutomaton)
                .GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(automaton));
        Func<RegexUnanchoredLazyDfa?> runnerFactory =
            Assert.IsType<Func<RegexUnanchoredLazyDfa?>>(
                typeof(RegexMetaEngine)
                    .GetField("_unanchoredLazyDfaFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(engine));
        RegexExpandedUnanchoredLazyDfaFactory expandedFactory =
            Assert.IsType<RegexExpandedUnanchoredLazyDfaFactory>(runnerFactory.Target);
        return Assert.IsType<Lazy<RegexUnanchoredLazyDfaFactory?>>(
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

        Assert.True(forwardBytes < sizeLimit);
        Assert.True(sizeLimit < pairedBytes);
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

        Assert.True(forwardBytes < sizeLimit);
        Assert.True(sizeLimit < pairedBytes);
        return sizeLimit;
    }
}
