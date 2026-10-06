using System.Text;

namespace Scout;

/// <summary>
/// Verifies Thompson NFA compilation independently of meta-engine selection.
/// </summary>
[TestClass]
public sealed class RegexNfaCompilerTests()
{
    /// <summary>
    /// Verifies every optional copy in a finite repetition skips directly to one shared exit.
    /// </summary>
    /// <param name="pattern">The bounded repetition pattern.</param>
    /// <param name="swapGreed">Whether to invert repetition preference.</param>
    /// <param name="reversed">Whether to compile the NFA in reverse.</param>
    /// <param name="lazy">Whether the effective repetition preference is lazy.</param>
    [TestMethod]
    [DataRow("a{2,5}z", false, false, false)]
    [DataRow("a{2,5}?z", false, false, true)]
    [DataRow("a{2,5}z", true, false, true)]
    [DataRow("a{2,5}?z", true, false, false)]
    [DataRow("za{2,5}", false, true, false)]
    [DataRow("za{2,5}?", false, true, true)]
    [DataRow("za{2,5}", true, true, true)]
    [DataRow("za{2,5}?", true, true, false)]
    public void FiniteRepetitionUsesCommonExit(
        string pattern,
        bool swapGreed,
        bool reversed,
        bool lazy)
    {
        RegexNfa nfa = Compile(pattern, swapGreed, reversed);
        int accept = FindAccept(nfa);
        int continuation = FindLiteral(nfa, (byte)'z');
        Assert.AreEqual(accept, nfa.States[continuation].Next);
        int current = nfa.StartState;

        for (int count = 0; count < 2; count++)
        {
            current = AssertLiteral(nfa, current, (byte)'a').Next;
        }

        for (int count = 2; count < 5; count++)
        {
            RegexNfaState split = nfa.States[current];
            Assert.AreEqual(RegexNfaStateKind.Split, split.Kind);

            int skip = lazy ? split.Next : split.Alternative;
            int consume = lazy ? split.Alternative : split.Next;
            Assert.AreEqual(continuation, skip);

            current = AssertLiteral(nfa, consume, (byte)'a').Next;
        }

        Assert.AreEqual(continuation, current);
    }

    /// <summary>
    /// Verifies finite repetition still honors greedy, lazy, ungreedy, and reversed matching priority.
    /// </summary>
    /// <param name="pattern">The bounded repetition pattern.</param>
    /// <param name="haystack">The bytes presented to the compiled NFA.</param>
    /// <param name="swapGreed">Whether to invert repetition preference.</param>
    /// <param name="reversed">Whether to compile the NFA in reverse.</param>
    /// <param name="expectedLength">The expected anchored match length.</param>
    [TestMethod]
    [DataRow("a{2,5}a", "aaaaaa", false, false, 6)]
    [DataRow("a{2,5}?a", "aaaaaa", false, false, 3)]
    [DataRow("a{2,5}a", "aaaaaa", true, false, 3)]
    [DataRow("a{2,5}?a", "aaaaaa", true, false, 6)]
    [DataRow("a{2,5}z", "zaaaaa", false, true, 6)]
    [DataRow("a{2,5}?z", "zaaaaa", false, true, 3)]
    public void FiniteRepetitionPreservesMatchingPriority(
        string pattern,
        string haystack,
        bool swapGreed,
        bool reversed,
        int expectedLength)
    {
        var vm = new PikeVm(Compile(pattern, swapGreed, reversed));

        Assert.IsTrue(vm.TryMatchAt(Encoding.UTF8.GetBytes(haystack), start: 0, out int length));
        Assert.AreEqual(expectedLength, length);
    }

    /// <summary>
    /// Verifies exact, bounded, and empty-child repetitions retain their language semantics.
    /// </summary>
    [TestMethod]
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

    /// <summary>
    /// Verifies bounded authoritative ASCII classes lower to one byte-class state per copy and
    /// retain exact forward and reverse construction estimates.
    /// </summary>
    /// <param name="pattern">The bounded line expression to compile.</param>
    [TestMethod]
    [DataRow("^[A-Za-z_]{70,90}$")]
    [DataRow(@"^[A-Za-z_]{70,90}\r?$")]
    public void BoundedAsciiCharacterClassesUseByteClassStates(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        RegexCompileOptions options = CreateLineRegexOptions();
        RegexNfa nfa = RegexNfaCompiler.Compile(tree.Root, options);
        RegexNfaConstructionEstimate estimate =
            RegexNfaCompiler.EstimateUnanchoredConstruction(tree.Root, options);
        RegexNfa forward = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        RegexNfa reverse = RegexNfaCompiler.CompileReversed(tree.Root, options);
        byte[] expectedRanges = [(byte)'A', (byte)'Z', (byte)'_', (byte)'_', (byte)'a', (byte)'z'];
        int classStateCount = 0;

        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.AtomKind != RegexSyntaxKind.ByteClass ||
                !state.Value.Span.SequenceEqual(expectedRanges))
            {
                continue;
            }

            classStateCount++;
            Assert.IsNull(state.ScalarRanges);
            Assert.IsFalse(state.RequiresUtf8ScalarMatch);
        }

        Assert.AreEqual(90, classStateCount);
        Assert.AreEqual((ulong)forward.States.Count, estimate.ForwardStateCount);
        Assert.AreEqual((ulong)reverse.States.Count, estimate.ReverseStateCount);
    }

    /// <summary>
    /// Verifies ranges containing a non-ASCII scalar continue through Unicode scalar lowering.
    /// </summary>
    [TestMethod]
    public void NonAsciiCharacterClassesRetainUnicodeScalarLowering()
    {
        const string Pattern = @"^[A-Za-z_\u0100]{2}$";
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(Pattern));
        RegexCompileOptions options = CreateLineRegexOptions();
        RegexNfa nfa = RegexNfaCompiler.Compile(tree.Root, options);
        RegexAtomNode atom = FindFirstAtom(tree.Root, RegexSyntaxKind.CharacterClass) ??
            throw new InvalidOperationException("The syntax tree does not contain a character-class atom.");

        Assert.IsTrue(RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(
            atom,
            options,
            out List<RegexScalarRange> ranges));
        Assert.IsFalse(RegexUtf8ByteCompiler.TryGetAsciiByteRanges(ranges, out _));
        Assert.IsGreaterThan(2, CountConsumingStates(nfa));

        var vm = new PikeVm(nfa);
        Assert.IsTrue(vm.TryMatchAt("AĀ"u8, start: 0, out int length));
        Assert.AreEqual(3, length);
    }

    /// <summary>
    /// Verifies bounded authoritative ASCII class compilation does not allocate a UTF-8 trie for
    /// every repetition copy.
    /// </summary>
    /// <param name="pattern">The bounded line expression to compile.</param>
    [TestMethod]
    [DataRow("^[A-Za-z_]{70,90}$")]
    [DataRow(@"^[A-Za-z_]{70,90}\r?$")]
    public void BoundedAsciiCharacterClassCompileAllocationsStayBounded(string pattern)
    {
        const long AllocationLimit = 256 * 1024;
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        RegexCompileOptions options = CreateLineRegexOptions();

        _ = RegexNfaCompiler.Compile(tree.Root, options);
        _ = RegexNfaCompiler.Compile(tree.Root, options);

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = RegexNfaCompiler.Compile(tree.Root, options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsLessThanOrEqualTo(
            AllocationLimit, allocated,
            $"Expected bounded ASCII class compilation to allocate at most {AllocationLimit} bytes, but it allocated {allocated} bytes.");
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

    private static RegexCompileOptions CreateLineRegexOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true,
            excludeLineTerminators: true,
            excludeCrLf: false,
            excludedLineTerminator: (byte)'\n');
    }

    private static RegexAtomNode? FindFirstAtom(RegexSyntaxNode node, RegexSyntaxKind kind)
    {
        switch (node)
        {
            case RegexAtomNode atom when atom.Kind == kind:
                return atom;
            case RegexSequenceNode sequence:
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    RegexAtomNode? atom = FindFirstAtom(sequence.Nodes[index], kind);
                    if (atom is not null)
                    {
                        return atom;
                    }
                }

                break;
            case RegexGroupNode group:
                return FindFirstAtom(group.Child, kind);
            case RegexRepetitionNode repetition:
                return FindFirstAtom(repetition.Child, kind);
        }

        return null;
    }

    private static int CountConsumingStates(RegexNfa nfa)
    {
        int count = 0;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            if (nfa.States[index].Kind is RegexNfaStateKind.Atom or RegexNfaStateKind.Sparse)
            {
                count++;
            }
        }

        return count;
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

            Assert.AreEqual(-1, accept);
            accept = index;
        }

        Assert.AreNotEqual(-1, accept);
        return accept;
    }

    private static RegexNfaState AssertLiteral(RegexNfa nfa, int stateIndex, byte value)
    {
        RegexNfaState state = nfa.States[stateIndex];
        Assert.AreEqual(RegexNfaStateKind.Atom, state.Kind);
        Assert.AreEqual(RegexSyntaxKind.Literal, state.AtomKind);
        Assert.IsTrue(state.Value.Span.SequenceEqual([value]));
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

            Assert.AreEqual(-1, literal);
            literal = index;
        }

        Assert.AreNotEqual(-1, literal);
        return literal;
    }

    private static void AssertMatch(string pattern, string haystack, int expectedLength)
    {
        var vm = new PikeVm(Compile(pattern));

        Assert.IsTrue(vm.TryMatchAt(Encoding.UTF8.GetBytes(haystack), start: 0, out int length));
        Assert.AreEqual(expectedLength, length);
    }

    private static void AssertNoMatch(string pattern, string haystack)
    {
        var vm = new PikeVm(Compile(pattern));

        Assert.IsFalse(vm.TryMatchAt(Encoding.UTF8.GetBytes(haystack), start: 0, out int length));
        Assert.AreEqual(0, length);
    }
}
