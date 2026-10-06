namespace Scout;

/// <summary>
/// Verifies bounded lazy-DFA execution and its on-demand PikeVM fallback.
/// </summary>
[TestClass]
public sealed class RegexLazyDfaTests()
{
    /// <summary>
    /// Verifies a stale or copied lease cannot return the same mutable DFA more than once.
    /// </summary>
    [TestMethod]
    public void RunnerLeaseEndsExactlyOnce()
    {
        RegexNfa nfa = CompileNfa("ab"u8);

        Assert.IsTrue(RegexLazyDfa.TryCreate(nfa, 1_024 * 1_024, out RegexLazyDfa? dfa));

        long first = dfa!.BeginRunnerLease();
        Assert.IsTrue(dfa.IsRunnerLeaseActive(first));
        Assert.IsTrue(dfa.TryEndRunnerLease(first));
        Assert.IsFalse(dfa.TryEndRunnerLease(first));

        long second = dfa.BeginRunnerLease();
        Assert.IsFalse(dfa.IsRunnerLeaseActive(first));
        Assert.IsTrue(dfa.IsRunnerLeaseActive(second));
        Assert.IsFalse(dfa.TryEndRunnerLease(first));
        Assert.IsTrue(dfa.TryEndRunnerLease(second));
    }

    /// <summary>
    /// Verifies transition-budget exhaustion creates one fallback only when matching needs it.
    /// </summary>
    [TestMethod]
    public void TransitionBudgetExhaustionCreatesFallbackOnDemand()
    {
        RegexNfa nfa = CompileNfa("ab"u8);
        int[] startStates = RegexDfaOperations.Closure(nfa, nfa.StartState);
        ulong startStateBudget = RegexDfaBudget.EstimateStateBytes(
            startStates.Length,
            denseTransitions: false);

        Assert.IsTrue(RegexLazyDfa.TryCreate(nfa, startStateBudget, out RegexLazyDfa? dfa));
        Assert.IsNull(GetFallback(dfa!));

        Assert.IsTrue(dfa!.TryMatchAt("ab"u8, start: 0, out int length));
        Assert.AreEqual(2, length);
        PikeVm fallback = Assert.IsExactInstanceOfType<PikeVm>(GetFallback(dfa));

        Assert.IsFalse(dfa.TryMatchAt("ac"u8, start: 0, out length));
        Assert.AreEqual(0, length);
        Assert.AreSame(fallback, GetFallback(dfa));
    }

    /// <summary>
    /// Verifies the dense reference-table estimate includes the managed array header and entries.
    /// </summary>
    [TestMethod]
    public void DenseTransitionTableBudgetIncludesArrayHeaderAndReferences()
    {
        ulong expected = IntPtr.Size == 8 ? 2_072UL : 1_036UL;

        Assert.AreEqual(expected, RegexDfaBudget.DenseReferenceTransitionTableBytes);
    }

    /// <summary>
    /// Verifies transition-table promotion falls back one byte below its exact cache budget.
    /// </summary>
    [TestMethod]
    public void DenseTransitionPromotionFallsBackBeforeExceedingBudget()
    {
        RegexNfa nfa = CompileNfa("(?:a|b)"u8);
        int[] startStates = RegexDfaOperations.Closure(nfa, nfa.StartState);
        int[] acceptStates = RegexDfaOperations.Move(nfa, startStates, (byte)'a');
        Assert.AreSequenceEqual(
            acceptStates,
            RegexDfaOperations.Move(nfa, startStates, (byte)'b'));
        ulong firstTransitionBytes = RegexDfaBudget.SparseTransitionBytes +
            RegexDfaBudget.EstimateStateBytes(
                acceptStates.Length,
                denseTransitions: false);
        ulong secondTransitionBytes = RegexDfaBudget.SparseTransitionBytes +
            RegexDfaBudget.DenseReferenceTransitionTableBytes;
        ulong exactDfaSizeLimit = RegexDfaBudget.EstimateStateBytes(
            startStates.Length,
            denseTransitions: false) +
            firstTransitionBytes +
            secondTransitionBytes;

        Assert.IsTrue(RegexLazyDfa.TryCreate(nfa, exactDfaSizeLimit - 1, out RegexLazyDfa? dfa));
        Assert.IsTrue(dfa!.TryMatchAt("a"u8, start: 0, out int firstLength));
        Assert.AreEqual(1, firstLength);
        Assert.IsNull(GetFallback(dfa));

        Assert.IsTrue(dfa.TryMatchAt("b"u8, start: 0, out int secondLength));
        Assert.AreEqual(1, secondLength);
        Assert.IsNotNull(GetFallback(dfa));
        Assert.IsNull(GetDenseTransitions(GetStartState(dfa)));

        Assert.IsTrue(RegexLazyDfa.TryCreate(nfa, exactDfaSizeLimit, out RegexLazyDfa? exactDfa));
        Assert.IsTrue(exactDfa!.TryMatchAt("a"u8, start: 0, out firstLength));
        Assert.IsTrue(exactDfa.TryMatchAt("b"u8, start: 0, out secondLength));
        Assert.AreEqual(1, firstLength);
        Assert.AreEqual(1, secondLength);
        Assert.IsNull(GetFallback(exactDfa));
        RegexLazyDfaState?[] denseTransitions = Assert.IsExactInstanceOfType<RegexLazyDfaState?[]>(
            GetDenseTransitions(GetStartState(exactDfa)));
        Assert.HasCount(256, denseTransitions);
        Assert.AreSame(denseTransitions[(byte)'a'], denseTransitions[(byte)'b']);
    }

    /// <summary>
    /// Verifies reverse all-match execution continues past a higher-priority empty alternative
    /// to recover the earliest accepted start.
    /// </summary>
    [TestMethod]
    public void ReverseAllFindsEarliestStartAcrossEmptyAlternative()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?:|Public)Key"u8);
        RegexCompileOptions options = CreateOptions();
        RegexNfa reversed = RegexNfaCompiler.CompileReversed(tree.Root, options);

        Assert.IsTrue(RegexLazyDfa.TryCreate(
            reversed,
            dfaSizeLimit: 1_024 * 1_024,
            RegexDfaMatchKind.All,
            out RegexLazyDfa? dfa));
        Assert.IsTrue(dfa!.TryFindStartReverse(
            "PublicKey"u8,
            start: 0,
            end: 9,
            out int matchStart,
            out bool gaveUp));

        Assert.IsFalse(gaveUp);
        Assert.AreEqual(0, matchStart);
    }

    /// <summary>
    /// Verifies reverse all-match execution reports transition-budget exhaustion.
    /// </summary>
    [TestMethod]
    public void ReverseAllTransitionBudgetExhaustionReportsGiveUp()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?:|Public)Key"u8);
        RegexCompileOptions options = CreateOptions();
        RegexNfa reversed = RegexNfaCompiler.CompileReversed(tree.Root, options);
        int[] startStates = RegexDfaOperations.Closure(reversed, reversed.StartState);
        ulong startStateBudget = RegexDfaBudget.EstimateStateBytes(
            startStates.Length,
            denseTransitions: false);

        Assert.IsTrue(RegexLazyDfa.TryCreate(
            reversed,
            startStateBudget,
            RegexDfaMatchKind.All,
            out RegexLazyDfa? dfa));
        Assert.IsFalse(dfa!.TryFindStartReverse(
            "PublicKey"u8,
            start: 0,
            end: 9,
            out int matchStart,
            out bool gaveUp));

        Assert.IsTrue(gaveUp);
        Assert.AreEqual(-1, matchStart);
    }

    /// <summary>
    /// Verifies a paired search rejects a start reconstructed by a reverse DFA that exhausts its
    /// transition budget, allowing the caller to rerun the authoritative engine.
    /// </summary>
    [TestMethod]
    public void PairedReverseBudgetExhaustionRequiresAuthoritativeFallback()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?:|Public)Key"u8);
        RegexCompileOptions options = CreateOptions();
        RegexNfa forwardNfa = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        RegexNfa reverseNfa = RegexNfaCompiler.CompileReversed(tree.Root, options);
        int[] reverseStartStates = RegexDfaOperations.Closure(
            reverseNfa,
            reverseNfa.StartState);
        ulong reverseStartBudget = RegexDfaBudget.EstimateStateBytes(
            reverseStartStates.Length,
            denseTransitions: false);
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreateDirection(
            forwardNfa,
            dfaSizeLimit: 1_024 * 1_024,
            RegexDfaMatchKind.LeftmostFirst,
            out IRegexLazyDfaDirection? forward));
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreateDirection(
            reverseNfa,
            reverseStartBudget,
            RegexDfaMatchKind.All,
            out IRegexLazyDfaDirection? reverse));
        var paired = new RegexUnanchoredLazyDfa(forward!, reverse!, reverseFactory: null);

        Assert.IsFalse(paired.TryFind(
            "PublicKey"u8,
            startAt: 0,
            out RegexMatch match,
            out bool gaveUp));

        Assert.IsTrue(gaveUp);
        Assert.AreEqual(default, match);
    }

    private static RegexNfa CompileNfa(ReadOnlySpan<byte> pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        return RegexNfaCompiler.Compile(tree.Root, CreateOptions());
    }

    private static RegexCompileOptions CreateOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
    }

    private static PikeVm? GetFallback(RegexLazyDfa dfa)
    {
        return (PikeVm?)typeof(RegexLazyDfa)
            .GetField("_fallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(dfa);
    }

    private static RegexLazyDfaState GetStartState(RegexLazyDfa dfa)
    {
        return (RegexLazyDfaState)typeof(RegexLazyDfa)
            .GetField("_startState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(dfa)!;
    }

    private static RegexLazyDfaState?[]? GetDenseTransitions(RegexLazyDfaState state)
    {
        return (RegexLazyDfaState?[]?)typeof(RegexLazyDfaState)
            .GetField("_denseTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(state);
    }
}
