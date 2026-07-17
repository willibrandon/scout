using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies adaptive prefilter state has operation scope without per-match heap allocation.
/// </summary>
public sealed class RegexPublicIterationAllocationTests
{
    private const int MeasurementIterations = 128;

    /// <summary>
    /// Verifies one-shot public finds keep their adaptive state in stack storage.
    /// </summary>
    [Fact]
    public void OneShotFindDoesNotAllocatePrefilterState()
    {
        var plan = RegexSearchPlan.Create(
            ["abcdefgh(?:foo|bar)[0-9]"u8.ToArray()],
            asciiCaseInsensitive: false);
        byte[] haystack = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("abcdefghfooX", RegexPrefilterState.MinimumSkipCount)));

        for (int index = 0; index < 4; index++)
        {
            _ = plan.Matcher.Find(haystack);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        RegexMatch? match = null;
        for (int index = 0; index < MeasurementIterations; index++)
        {
            match = plan.Matcher.Find(haystack);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Null(match);
        Assert.True(
            allocated <= 256,
            $"Expected stack-backed one-shot prefilter state, but {allocated} bytes were allocated.");
    }

    /// <summary>
    /// Verifies public match iteration retains one runner and one adaptive state regardless of match count.
    /// </summary>
    [Fact]
    public void ByteRegexIterationAllocationDoesNotGrowWithMatchCount()
    {
        var regex = ByteRegex.Compile(
            "abcdefgh(?:foo|bar)[0-9]",
            new ByteRegexOptions
            {
                DfaSizeLimit = 1,
                EngineMode = ByteRegexEngineMode.General,
            });
        byte[] oneMatch = "abcdefghfoo1"u8.ToArray();
        byte[] manyMatches = Encoding.UTF8.GetBytes(string.Join(
            '|',
            Enumerable.Repeat("abcdefghfoo1", RegexPrefilterState.MinimumSkipCount * 2)));

        _ = MeasureIterationAllocation(regex, oneMatch, out _);
        _ = MeasureIterationAllocation(regex, manyMatches, out _);

        long oneMatchAllocation = MeasureIterationAllocation(regex, oneMatch, out int oneCount);
        long manyMatchAllocation = MeasureIterationAllocation(regex, manyMatches, out int manyCount);

        Assert.Equal(1, oneCount);
        Assert.Equal(RegexPrefilterState.MinimumSkipCount * 2, manyCount);
        Assert.True(
            manyMatchAllocation <= oneMatchAllocation + 256,
            $"Expected operation-scoped iteration state, but one match allocated {oneMatchAllocation} bytes and many matches allocated {manyMatchAllocation} bytes.");
    }

    /// <summary>
    /// Verifies byte-regex iteration retains its primed runner while callbacks execute.
    /// </summary>
    [Fact]
    public void ByteRegexIterationRetainsRunnerDuringCallbacks()
    {
        ByteRegex regex = CreateByteRegex();
        RegexAutomaton automaton = GetAutomaton(regex);
        PrimePikeVmPool(automaton);
        var observer = new RegexRunnerLeaseObserver(automaton);

        int count = regex.ForEachMatch(
            "abcdefghfoo1|abcdefghbar2"u8,
            ref observer,
            ObserveByteRegexLease);

        AssertRetainedRunner(observer, count);
    }

    /// <summary>
    /// Verifies struct-sink matcher iteration retains its primed runner while callbacks execute.
    /// </summary>
    [Fact]
    public void RegexMatcherStructIterationRetainsRunnerDuringCallbacks()
    {
        RegexMatcher matcher = CreateRegexMatcher(out RegexAutomaton automaton);
        PrimePikeVmPool(automaton);
        var observer = new RegexRunnerLeaseObserver(automaton);
        var sink = new RegexRunnerLeaseSink(observer);

        int count = matcher.ForEachMatch(
            "abcdefghfoo1|abcdefghbar2"u8,
            ref sink);

        AssertRetainedRunner(observer, count);
    }

    /// <summary>
    /// Verifies function-pointer matcher iteration retains its primed runner while callbacks execute.
    /// </summary>
    [Fact]
    public unsafe void RegexMatcherFunctionPointerIterationRetainsRunnerDuringCallbacks()
    {
        RegexMatcher matcher = CreateRegexMatcher(out RegexAutomaton automaton);
        PrimePikeVmPool(automaton);
        var observer = new RegexRunnerLeaseObserver(automaton);
        var handle = GCHandle.Alloc(observer);

        try
        {
            int count = matcher.ForEachMatch(
                "abcdefghfoo1|abcdefghbar2"u8,
                &ObserveMatcherLease,
                (void*)GCHandle.ToIntPtr(handle));

            AssertRetainedRunner(observer, count);
        }
        finally
        {
            handle.Free();
        }
    }

    private static long MeasureIterationAllocation(
        ByteRegex regex,
        ReadOnlySpan<byte> input,
        out int count)
    {
        int callbackCount = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        count = regex.ForEachMatch(input, ref callbackCount, CountMatch);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(count, callbackCount);
        return allocated;
    }

    private static ByteRegex CreateByteRegex()
    {
        return ByteRegex.Compile(
            "abcdefgh(?:foo|bar)[0-9]",
            new ByteRegexOptions
            {
                DfaSizeLimit = 1,
                EngineMode = ByteRegexEngineMode.General,
            });
    }

    private static RegexMatcher CreateRegexMatcher(out RegexAutomaton automaton)
    {
        automaton = RegexAutomaton.Compile(
            "abcdefgh(?:foo|bar)[0-9]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            dfaSizeLimit: 1,
            specializationMode: RegexSpecializationMode.General);
        ConstructorInfo? constructor = typeof(RegexMatcher).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(RegexAutomaton)],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<RegexMatcher>(constructor.Invoke([automaton]));
    }

    private static RegexAutomaton GetAutomaton(object facade)
    {
        FieldInfo? field = facade.GetType().GetField(
            "_automaton",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<RegexAutomaton>(field.GetValue(facade));
    }

    private static void PrimePikeVmPool(RegexAutomaton automaton)
    {
        using RegexFindRunner runner = automaton.RentFindRunner();
        Assert.True(
            runner.PikeVmLeaseVersion > 0,
            "The regression pattern must select the pooled Pike VM engine.");
    }

    private static void AssertRetainedRunner(
        RegexRunnerLeaseObserver observer,
        int matchCount)
    {
        Assert.Equal(2, matchCount);
        Assert.Equal(matchCount, observer.ObservationCount);
        Assert.Equal(1, observer.FirstLeaseVersion);
    }

    private static bool ObserveByteRegexLease(
        ReadOnlySpan<byte> input,
        ByteRegexMatch match,
        ref RegexRunnerLeaseObserver observer)
    {
        _ = input;
        _ = match;
        observer.Observe();
        return true;
    }

    private static unsafe bool ObserveMatcherLease(
        void* state,
        ReadOnlySpan<byte> input,
        MatcherMatch match)
    {
        _ = input;
        _ = match;
        RegexRunnerLeaseObserver observer = Assert.IsType<RegexRunnerLeaseObserver>(
            GCHandle.FromIntPtr((nint)state).Target);
        observer.Observe();
        return true;
    }

    private static bool CountMatch(
        ReadOnlySpan<byte> input,
        ByteRegexMatch match,
        ref int count)
    {
        _ = input;
        _ = match;
        count++;
        return true;
    }
}
