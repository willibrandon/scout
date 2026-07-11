namespace Scout;

/// <summary>
/// Verifies bounded lazy-DFA execution and its on-demand PikeVM fallback.
/// </summary>
public sealed class RegexLazyDfaTests()
{
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
}
