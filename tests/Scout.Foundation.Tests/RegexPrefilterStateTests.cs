using System.Text;

namespace Scout;

/// <summary>
/// Verifies operation-scoped adaptive regex prefilter effectiveness and authoritative fallback.
/// </summary>
public sealed class RegexPrefilterStateTests
{
    /// <summary>
    /// Verifies the first forty scans run before a low cumulative average permanently disables the prefilter.
    /// </summary>
    [Fact]
    public void BecomesInertAfterFortyIneffectiveSkips()
    {
        var state = new RegexPrefilterState();

        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            Assert.True(state.IsEffective);
            state.RecordSkip(RegexPrefilterState.MinimumAverageSkippedBytes - 1);
        }

        Assert.False(state.IsEffective);
        Assert.True(state.IsInert);
        Assert.Equal(RegexPrefilterState.MinimumSkipCount, state.SkipCount);
        Assert.Equal(
            RegexPrefilterState.MinimumSkipCount *
                (RegexPrefilterState.MinimumAverageSkippedBytes - 1),
            state.SkippedByteCount);

        state.RecordSkip(1_000_000);

        Assert.False(state.IsEffective);
        Assert.Equal(RegexPrefilterState.MinimumSkipCount, state.SkipCount);
    }

    /// <summary>
    /// Verifies a cumulative average of sixteen skipped bytes keeps the prefilter active.
    /// </summary>
    [Fact]
    public void RemainsEffectiveAtMinimumAverageSkip()
    {
        var state = new RegexPrefilterState();

        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            state.RecordSkip(RegexPrefilterState.MinimumAverageSkippedBytes);
        }

        Assert.True(state.IsEffective);
        Assert.False(state.IsInert);
    }

    /// <summary>
    /// Verifies dense exact-prefix candidates switch to monotonically ordered unfiltered starts.
    /// </summary>
    [Fact]
    public void ExactPrefixEnumeratorFallsBackToEveryRemainingStart()
    {
        const string pattern = "abcdefgh(?:foo|bar)";
        RegexPrefilter prefilter = CompilePrefilter(pattern, out _);
        byte[] haystack = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("abcdefghx", RegexPrefilterState.MinimumSkipCount)));
        Span<RegexPrefilterState> state = stackalloc RegexPrefilterState[1] { default };
        var candidates = RegexCandidateStartEnumerator.ExactPrefix(
            haystack,
            startAt: 0,
            maxStart: haystack.Length,
            utf8: true,
            prefilter,
            state);

        int previous = -1;
        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            Assert.True(candidates.MoveNext(out int candidate));
            Assert.True(candidate > previous);
            Assert.Equal(index * "abcdefghx".Length, candidate);
            previous = candidate;
        }

        Assert.True(candidates.MoveNext(out int unfilteredStart));
        Assert.Equal(previous + 1, unfilteredStart);
        Assert.True(state[0].IsInert);
        Assert.NotEqual((byte)'a', haystack[unfilteredStart]);
    }

    /// <summary>
    /// Verifies an ineffective prefilter falls back to the authoritative Pike VM without losing a later match.
    /// </summary>
    [Fact]
    public void IneffectivePrefilterPreservesAuthoritativeMatch()
    {
        const string pattern = "abcdefgh(?:foo|bar)";
        RegexPrefilter prefilter = CompilePrefilter(pattern, out RegexNfa nfa);
        string falseCandidates = string.Concat(
            Enumerable.Repeat("abcdefghx", RegexPrefilterState.MinimumSkipCount));
        byte[] haystack = Encoding.UTF8.GetBytes(falseCandidates + "abcdefghfoo");
        Span<RegexPrefilterState> state = stackalloc RegexPrefilterState[1] { default };
        var candidates = RegexCandidateStartEnumerator.ExactPrefix(
            haystack,
            startAt: 0,
            maxStart: haystack.Length,
            nfa.Utf8,
            prefilter,
            state);

        RegexMatch? match = new PikeVm(nfa).Find(haystack, ref candidates);

        Assert.Equal(new RegexMatch(falseCandidates.Length, "abcdefghfoo".Length), match);
        Assert.True(state[0].IsInert);
    }

    /// <summary>
    /// Verifies a record runner retains adaptive state across dense false candidates and still finds a real match.
    /// </summary>
    [Fact]
    public void RecordRunnerDisablesDensePrefilterAndPreservesLaterMatch()
    {
        RegexSearchPlan plan = CreateExactPrefixPlan();
        using RegexFindRunner runner = plan.Matcher.RentRecordFindRunner();

        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            Assert.Null(runner.Find("abcdefghfooX"u8, startAt: 0));
        }

        RegexMatch? match = runner.Find("abcdefghfoo1"u8, startAt: 0);

        Assert.True(
            runner.IsPrefilterInert,
            $"Observed {runner.PrefilterSkipCount} prefilter scans without becoming inert.");
        Assert.NotNull(match);
        Assert.Equal(new RegexMatch(0, "abcdefghfoo1"u8.Length), match);
    }

    /// <summary>
    /// Verifies sparse candidates retain the prefilter when the cumulative skip average remains selective.
    /// </summary>
    [Fact]
    public void RecordRunnerKeepsSparsePrefilterEffective()
    {
        RegexSearchPlan plan = CreateExactPrefixPlan();
        using RegexFindRunner runner = plan.Matcher.RentRecordFindRunner();
        ReadOnlySpan<byte> sparseFalseCandidate =
            "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxabcdefghfooX"u8;

        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            Assert.Null(runner.Find(sparseFalseCandidate, startAt: 0));
        }

        Assert.False(runner.IsPrefilterInert);
        Assert.True(runner.PrefilterSkipCount >= RegexPrefilterState.MinimumSkipCount);
    }

    /// <summary>
    /// Verifies a fresh runner starts with independent prefilter-effectiveness state.
    /// </summary>
    [Fact]
    public void FreshRunnerDoesNotShareInertPrefilterState()
    {
        RegexSearchPlan plan = CreateExactPrefixPlan();
        using RegexFindRunner first = plan.Matcher.RentRecordFindRunner();
        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            Assert.Null(first.Find("abcdefghfooX"u8, startAt: 0));
        }

        using RegexFindRunner second = plan.Matcher.RentRecordFindRunner();

        Assert.True(
            first.IsPrefilterInert,
            $"Observed {first.PrefilterSkipCount} prefilter scans without becoming inert.");
        Assert.False(second.IsPrefilterInert);
        Assert.Equal(0, second.PrefilterSkipCount);
        Assert.False(first.SharesPooledStateWith(second));
    }

    /// <summary>
    /// Verifies dense required-literal scans disable their prefilter and continue with every start.
    /// </summary>
    [Fact]
    public void RequiredLiteralEnumeratorFallsBackForDenseHits()
    {
        RegexPrefilter prefilter = CompileRequiredLiteralPrefilter();
        byte[] haystack = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("needle", RegexPrefilterState.MinimumSkipCount)));
        Span<RegexPrefilterState> state = stackalloc RegexPrefilterState[1] { default };
        Span<long> ranges =
            stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];
        var candidates = RegexCandidateStartEnumerator.RequiredLiteralRanges(
            haystack,
            startAt: 0,
            maxStart: haystack.Length,
            utf8: true,
            prefilter,
            ranges,
            state);

        int previous = -1;
        while (!state[0].IsInert && candidates.MoveNext(out int candidate))
        {
            Assert.True(candidate > previous);
            previous = candidate;
        }

        if (!state[0].IsInert)
        {
            Assert.False(state[0].IsEffective);
        }

        Assert.True(state[0].IsInert);
        Assert.True(candidates.MoveNext(out int unfilteredStart));
        Assert.Equal(previous + 1, unfilteredStart);
    }

    /// <summary>
    /// Verifies sparse required-literal scans retain their prefilter at the minimum sample count.
    /// </summary>
    [Fact]
    public void RequiredLiteralEnumeratorRemainsEffectiveForSparseHits()
    {
        RegexPrefilter prefilter = CompileRequiredLiteralPrefilter();
        string sparseUnit = new string('x', 32) + "needle";
        byte[] haystack = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat(sparseUnit, RegexPrefilterState.MinimumSkipCount)));
        Span<RegexPrefilterState> state = stackalloc RegexPrefilterState[1] { default };
        Span<long> ranges =
            stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];
        var candidates = RegexCandidateStartEnumerator.RequiredLiteralRanges(
            haystack,
            startAt: 0,
            maxStart: haystack.Length,
            utf8: true,
            prefilter,
            ranges,
            state);

        while (candidates.MoveNext(out _))
        {
        }

        Assert.True(state[0].IsEffective);
        Assert.False(state[0].IsInert);
        Assert.True(state[0].SkipCount >= RegexPrefilterState.MinimumSkipCount);
    }

    /// <summary>
    /// Verifies required-literal fallback preserves NUL detection when the unseen NUL follows the
    /// scan that renders the prefilter inert.
    /// </summary>
    [Fact]
    public void RequiredLiteralFallbackPreservesNulDetection()
    {
        RegexPrefilter prefilter = CompileRequiredLiteralPrefilter();
        string denseHits = string.Concat(
            Enumerable.Repeat("needle", RegexPrefilterState.MinimumSkipCount));
        byte[] haystack = Encoding.UTF8.GetBytes(denseHits + "\0tail");
        Span<RegexPrefilterState> state = stackalloc RegexPrefilterState[1] { default };
        Span<long> ranges =
            stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];
        Span<bool> nulDetection = stackalloc bool[1];
        var candidates = RegexCandidateStartEnumerator.RequiredLiteralRangesAndDetectNul(
            haystack,
            startAt: 0,
            maxStart: haystack.Length,
            utf8: true,
            prefilter,
            ranges,
            nulDetection,
            state);

        while (candidates.MoveNext(out _))
        {
        }

        Assert.True(state[0].IsInert);
        Assert.True(nulDetection[0]);
    }

    /// <summary>
    /// Verifies concurrently executed operations retain independent effectiveness state.
    /// </summary>
    [Fact]
    public void ConcurrentOperationsRetainIndependentState()
    {
        RegexPrefilterState[] states = Enumerable.Range(0, Environment.ProcessorCount + 1)
            .Select(static _ => new RegexPrefilterState())
            .ToArray();

        Parallel.For(0, states.Length, stateIndex =>
        {
            for (int skipIndex = 0;
                 skipIndex < RegexPrefilterState.MinimumSkipCount;
                 skipIndex++)
            {
                states[stateIndex].RecordSkip(0);
            }

            Assert.False(states[stateIndex].IsEffective);
        });

        Assert.All(states, static state =>
        {
            Assert.True(state.IsInert);
            Assert.Equal(RegexPrefilterState.MinimumSkipCount, state.SkipCount);
        });
    }

    private static RegexPrefilter CompilePrefilter(string pattern, out RegexNfa nfa)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        Assert.NotNull(prefilter);
        Assert.False(prefilter.UsesRequiredLiteralWindow);
        nfa = RegexNfaCompiler.CompileWithCompactScalarAtoms(
            tree.Root,
            options,
            utf8ByteTrieCache: null);
        return prefilter;
    }

    private static RegexSearchPlan CreateExactPrefixPlan()
    {
        return RegexSearchPlan.Create(
            ["abcdefgh(?:foo|bar)[0-9]+"u8.ToArray()],
            asciiCaseInsensitive: false);
    }

    private static RegexPrefilter CompileRequiredLiteralPrefilter()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?:Z.{99}|Q)(?:needle).$"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        Assert.NotNull(prefilter);
        Assert.True(prefilter.UsesRequiredLiteralWindow);
        return prefilter;
    }
}
