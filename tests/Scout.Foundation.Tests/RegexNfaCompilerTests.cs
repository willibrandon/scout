using System.Text;

namespace Scout;

/// <summary>
/// Verifies Thompson NFA compilation independently of meta-engine selection.
/// </summary>
public sealed class RegexNfaCompilerTests()
{
    /// <summary>
    /// Verifies every optional copy in a finite repetition skips directly to one shared exit.
    /// </summary>
    /// <param name="pattern">The bounded repetition pattern.</param>
    /// <param name="swapGreed">Whether to invert repetition preference.</param>
    /// <param name="reversed">Whether to compile the NFA in reverse.</param>
    /// <param name="lazy">Whether the effective repetition preference is lazy.</param>
    [Theory]
    [InlineData("a{2,5}z", false, false, false)]
    [InlineData("a{2,5}?z", false, false, true)]
    [InlineData("a{2,5}z", true, false, true)]
    [InlineData("a{2,5}?z", true, false, false)]
    [InlineData("za{2,5}", false, true, false)]
    [InlineData("za{2,5}?", false, true, true)]
    [InlineData("za{2,5}", true, true, true)]
    [InlineData("za{2,5}?", true, true, false)]
    public void FiniteRepetitionUsesCommonExit(
        string pattern,
        bool swapGreed,
        bool reversed,
        bool lazy)
    {
        RegexNfa nfa = Compile(pattern, swapGreed, reversed);
        int accept = FindAccept(nfa);
        int continuation = FindLiteral(nfa, (byte)'z');
        Assert.Equal(accept, nfa.States[continuation].Next);
        int current = nfa.StartState;

        for (int count = 0; count < 2; count++)
        {
            current = AssertLiteral(nfa, current, (byte)'a').Next;
        }

        for (int count = 2; count < 5; count++)
        {
            RegexNfaState split = nfa.States[current];
            Assert.Equal(RegexNfaStateKind.Split, split.Kind);

            int skip = lazy ? split.Next : split.Alternative;
            int consume = lazy ? split.Alternative : split.Next;
            Assert.Equal(continuation, skip);

            current = AssertLiteral(nfa, consume, (byte)'a').Next;
        }

        Assert.Equal(continuation, current);
    }

    /// <summary>
    /// Verifies finite repetition still honors greedy, lazy, ungreedy, and reversed matching priority.
    /// </summary>
    /// <param name="pattern">The bounded repetition pattern.</param>
    /// <param name="haystack">The bytes presented to the compiled NFA.</param>
    /// <param name="swapGreed">Whether to invert repetition preference.</param>
    /// <param name="reversed">Whether to compile the NFA in reverse.</param>
    /// <param name="expectedLength">The expected anchored match length.</param>
    [Theory]
    [InlineData("a{2,5}a", "aaaaaa", false, false, 6)]
    [InlineData("a{2,5}?a", "aaaaaa", false, false, 3)]
    [InlineData("a{2,5}a", "aaaaaa", true, false, 3)]
    [InlineData("a{2,5}?a", "aaaaaa", true, false, 6)]
    [InlineData("a{2,5}z", "zaaaaa", false, true, 6)]
    [InlineData("a{2,5}?z", "zaaaaa", false, true, 3)]
    public void FiniteRepetitionPreservesMatchingPriority(
        string pattern,
        string haystack,
        bool swapGreed,
        bool reversed,
        int expectedLength)
    {
        var vm = new PikeVm(Compile(pattern, swapGreed, reversed));

        Assert.True(vm.TryMatchAt(Encoding.UTF8.GetBytes(haystack), start: 0, out int length));
        Assert.Equal(expectedLength, length);
    }

    /// <summary>
    /// Verifies exact, bounded, and empty-child repetitions retain their language semantics.
    /// </summary>
    [Fact]
    public void FiniteRepetitionPreservesLanguageSemantics()
    {
        AssertMatch("a{3}z", "aaaz", expectedLength: 4);
        AssertNoMatch("a{3}z", "aaaaz");
        AssertMatch("a{2,5}z", "aaz", expectedLength: 3);
        AssertMatch("a{2,5}z", "aaaaaz", expectedLength: 6);
        AssertNoMatch("a{2,5}z", "az");
        AssertNoMatch("a{2,5}z", "aaaaaaz");
        AssertMatch("(?:){2,5}z", "z", expectedLength: 1);
    }

    private static RegexNfa Compile(string pattern, bool swapGreed = false, bool reversed = false)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed,
            multiLine: false,
            dotMatchesNewline: false);
        return reversed
            ? RegexNfaCompiler.CompileReversed(tree.Root, options)
            : RegexNfaCompiler.Compile(tree.Root, options);
    }

    private static int FindAccept(RegexNfa nfa)
    {
        int accept = -1;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            if (nfa.States[index].Kind != RegexNfaStateKind.Accept)
            {
                continue;
            }

            Assert.Equal(-1, accept);
            accept = index;
        }

        Assert.NotEqual(-1, accept);
        return accept;
    }

    private static RegexNfaState AssertLiteral(RegexNfa nfa, int stateIndex, byte value)
    {
        RegexNfaState state = nfa.States[stateIndex];
        Assert.Equal(RegexNfaStateKind.Atom, state.Kind);
        Assert.Equal(RegexSyntaxKind.Literal, state.AtomKind);
        Assert.True(state.Value.Span.SequenceEqual([value]));
        return state;
    }

    private static int FindLiteral(RegexNfa nfa, byte value)
    {
        int literal = -1;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.Kind != RegexNfaStateKind.Atom ||
                state.AtomKind != RegexSyntaxKind.Literal ||
                !state.Value.Span.SequenceEqual([value]))
            {
                continue;
            }

            Assert.Equal(-1, literal);
            literal = index;
        }

        Assert.NotEqual(-1, literal);
        return literal;
    }

    private static void AssertMatch(string pattern, string haystack, int expectedLength)
    {
        var vm = new PikeVm(Compile(pattern));

        Assert.True(vm.TryMatchAt(Encoding.UTF8.GetBytes(haystack), start: 0, out int length));
        Assert.Equal(expectedLength, length);
    }

    private static void AssertNoMatch(string pattern, string haystack)
    {
        var vm = new PikeVm(Compile(pattern));

        Assert.False(vm.TryMatchAt(Encoding.UTF8.GetBytes(haystack), start: 0, out int length));
        Assert.Equal(0, length);
    }
}
