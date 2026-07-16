namespace Scout;

/// <summary>
/// Verifies bounded lazy-DFA execution and its on-demand PikeVM fallback.
/// </summary>
public sealed class RegexLazyDfaTests()
{
    /// <summary>
    /// Verifies a stale or copied lease cannot return the same mutable DFA more than once.
    /// </summary>
    [Fact]
    public void RunnerLeaseEndsExactlyOnce()
    {
        RegexNfa nfa = CompileNfa("ab"u8);

        Assert.True(RegexLazyDfa.TryCreate(nfa, 1_024 * 1_024, out RegexLazyDfa? dfa));

        long first = dfa!.BeginRunnerLease();
        Assert.True(dfa.IsRunnerLeaseActive(first));
        Assert.True(dfa.TryEndRunnerLease(first));
        Assert.False(dfa.TryEndRunnerLease(first));

        long second = dfa.BeginRunnerLease();
        Assert.False(dfa.IsRunnerLeaseActive(first));
        Assert.True(dfa.IsRunnerLeaseActive(second));
        Assert.False(dfa.TryEndRunnerLease(first));
        Assert.True(dfa.TryEndRunnerLease(second));
    }

    /// <summary>
    /// Verifies transition-budget exhaustion creates one fallback only when matching needs it.
    /// </summary>
    [Fact]
    public void TransitionBudgetExhaustionCreatesFallbackOnDemand()
    {
        RegexNfa nfa = CompileNfa("ab"u8);
        int[] startStates = RegexDfaOperations.Closure(nfa, nfa.StartState);
        ulong startStateBudget = RegexDfaBudget.EstimateStateBytes(
            startStates.Length,
            denseTransitions: false);

        Assert.True(RegexLazyDfa.TryCreate(nfa, startStateBudget, out RegexLazyDfa? dfa));
        Assert.Null(GetFallback(dfa!));

        Assert.True(dfa!.TryMatchAt("ab"u8, start: 0, out int length));
        Assert.Equal(2, length);
        PikeVm fallback = Assert.IsType<PikeVm>(GetFallback(dfa));

        Assert.False(dfa.TryMatchAt("ac"u8, start: 0, out length));
        Assert.Equal(0, length);
        Assert.Same(fallback, GetFallback(dfa));
    }

    /// <summary>
    /// Verifies the dense reference-table estimate includes the managed array header and entries.
    /// </summary>
    [Fact]
    public void DenseTransitionTableBudgetIncludesArrayHeaderAndReferences()
    {
        ulong expected = IntPtr.Size == 8 ? 2_072UL : 1_036UL;

        Assert.Equal(expected, RegexDfaBudget.DenseReferenceTransitionTableBytes);
    }

    /// <summary>
    /// Verifies transition-table promotion falls back one byte below its exact cache budget.
    /// </summary>
    [Fact]
    public void DenseTransitionPromotionFallsBackBeforeExceedingBudget()
    {
        RegexNfa nfa = CompileNfa("(?:a|b)"u8);
        int[] startStates = RegexDfaOperations.Closure(nfa, nfa.StartState);
        int[] acceptStates = RegexDfaOperations.Move(nfa, startStates, (byte)'a');
        Assert.Equal(
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

        Assert.True(RegexLazyDfa.TryCreate(nfa, exactDfaSizeLimit - 1, out RegexLazyDfa? dfa));
        Assert.True(dfa!.TryMatchAt("a"u8, start: 0, out int firstLength));
        Assert.Equal(1, firstLength);
        Assert.Null(GetFallback(dfa));

        Assert.True(dfa.TryMatchAt("b"u8, start: 0, out int secondLength));
        Assert.Equal(1, secondLength);
        Assert.NotNull(GetFallback(dfa));
        Assert.Null(GetDenseTransitions(GetStartState(dfa)));

        Assert.True(RegexLazyDfa.TryCreate(nfa, exactDfaSizeLimit, out RegexLazyDfa? exactDfa));
        Assert.True(exactDfa!.TryMatchAt("a"u8, start: 0, out firstLength));
        Assert.True(exactDfa.TryMatchAt("b"u8, start: 0, out secondLength));
        Assert.Equal(1, firstLength);
        Assert.Equal(1, secondLength);
        Assert.Null(GetFallback(exactDfa));
        RegexLazyDfaState?[] denseTransitions = Assert.IsType<RegexLazyDfaState?[]>(
            GetDenseTransitions(GetStartState(exactDfa)));
        Assert.Equal(256, denseTransitions.Length);
        Assert.Same(denseTransitions[(byte)'a'], denseTransitions[(byte)'b']);
    }

    private static RegexNfa CompileNfa(ReadOnlySpan<byte> pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        return RegexNfaCompiler.Compile(tree.Root, options);
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
