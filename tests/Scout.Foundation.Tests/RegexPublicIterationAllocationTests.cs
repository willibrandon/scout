using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies adaptive prefilter state has operation scope without per-match heap allocation.
/// </summary>
[TestClass]
public sealed class RegexPublicIterationAllocationTests
{
    private const int BaselineMeasurementIterations = 128;
    private const int ScaledMeasurementIterations = 1_024;
    private const int MeasurementSampleCount = 3;
    private const long AllocationNoiseAllowance = 256;

    /// <summary>
    /// Verifies repeated public finds retain stack-backed adaptive prefilter state.
    /// </summary>
    [TestMethod]
    public void OneShotFindPrefilterStateAllocationDoesNotGrowWithCallCount()
    {
        RegexMatcher matcher = CreateRegexMatcher(out RegexAutomaton automaton);
        byte[] haystack = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("abcdefghfooX0", RegexPrefilterState.MinimumSkipCount)));

        Assert.AreNotEqual(RegexPrefilterKind.None, automaton.PrefilterKind);
        using (RegexFindRunner runner = automaton.RentRecordFindRunner())
        {
            Assert.IsGreaterThan(
                0, runner.PikeVmLeaseVersion,
                "The allocation guard must exercise the pooled Pike VM engine.");
            Assert.IsNull(runner.Find(haystack, startAt: 0));
            Assert.IsTrue(
                runner.IsPrefilterInert,
                $"Expected dense false candidates to exercise adaptive prefilter state, but observed {runner.PrefilterSkipCount} scans without disabling it.");
            Assert.IsGreaterThanOrEqualTo(
                RegexPrefilterState.MinimumSkipCount, runner.PrefilterSkipCount,
                $"Expected at least {RegexPrefilterState.MinimumSkipCount} prefilter scans, but observed {runner.PrefilterSkipCount}.");
        }

        _ = MeasureOneShotFindAllocation(
            matcher,
            haystack,
            ScaledMeasurementIterations);
        _ = MeasureOneShotFindAllocation(
            matcher,
            haystack,
            ScaledMeasurementIterations);

        long baselineAllocation = long.MaxValue;
        long scaledAllocation = long.MaxValue;
        for (int sample = 0; sample < MeasurementSampleCount; sample++)
        {
            bool measureScaledFirst = (sample & 1) != 0;
            long firstAllocation = MeasureOneShotFindAllocation(
                matcher,
                haystack,
                measureScaledFirst
                    ? ScaledMeasurementIterations
                    : BaselineMeasurementIterations);
            long secondAllocation = MeasureOneShotFindAllocation(
                matcher,
                haystack,
                measureScaledFirst
                    ? BaselineMeasurementIterations
                    : ScaledMeasurementIterations);
            long baselineSample = measureScaledFirst ? secondAllocation : firstAllocation;
            long scaledSample = measureScaledFirst ? firstAllocation : secondAllocation;
            baselineAllocation = Math.Min(baselineAllocation, baselineSample);
            scaledAllocation = Math.Min(scaledAllocation, scaledSample);
        }

        Assert.IsLessThanOrEqualTo(
            baselineAllocation + AllocationNoiseAllowance, scaledAllocation,
            $"Expected stack-backed one-shot state: the minimum of {MeasurementSampleCount} samples was {baselineAllocation} bytes for {BaselineMeasurementIterations} finds and {scaledAllocation} bytes for {ScaledMeasurementIterations} finds.");
    }

    /// <summary>
    /// Verifies public match iteration retains one runner and one adaptive state regardless of match count.
    /// </summary>
    [TestMethod]
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

        for (int warmup = 0; warmup < 2; warmup++)
        {
            _ = MeasureIterationAllocation(regex, oneMatch, out _);
            _ = MeasureIterationAllocation(regex, manyMatches, out _);
        }

        int expectedManyCount = RegexPrefilterState.MinimumSkipCount * 2;
        long oneMatchAllocation = long.MaxValue;
        long manyMatchAllocation = long.MaxValue;
        for (int sample = 0; sample < MeasurementSampleCount; sample++)
        {
            bool measureManyFirst = (sample & 1) != 0;
            long firstAllocation = MeasureIterationAllocation(
                regex,
                measureManyFirst ? manyMatches : oneMatch,
                out int firstCount);
            long secondAllocation = MeasureIterationAllocation(
                regex,
                measureManyFirst ? oneMatch : manyMatches,
                out int secondCount);
            int oneCount = measureManyFirst ? secondCount : firstCount;
            int manyCount = measureManyFirst ? firstCount : secondCount;
            long oneMatchSample = measureManyFirst ? secondAllocation : firstAllocation;
            long manyMatchSample = measureManyFirst ? firstAllocation : secondAllocation;

            Assert.AreEqual(1, oneCount);
            Assert.AreEqual(expectedManyCount, manyCount);
            oneMatchAllocation = Math.Min(oneMatchAllocation, oneMatchSample);
            manyMatchAllocation = Math.Min(manyMatchAllocation, manyMatchSample);
        }

        Assert.IsLessThanOrEqualTo(
            oneMatchAllocation + AllocationNoiseAllowance, manyMatchAllocation,
            $"Expected operation-scoped iteration state: the minimum of {MeasurementSampleCount} samples was {oneMatchAllocation} bytes for one match and {manyMatchAllocation} bytes for {expectedManyCount} matches.");
    }

    /// <summary>
    /// Verifies byte-regex iteration retains its primed runner while callbacks execute.
    /// </summary>
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
        Assert.AreEqual(count, callbackCount);
        return allocated;
    }

    private static long MeasureOneShotFindAllocation(
        RegexMatcher matcher,
        ReadOnlySpan<byte> input,
        int iterationCount)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        MatcherMatch? match = null;
        for (int index = 0; index < iterationCount; index++)
        {
            match = matcher.Find(input);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsNull(match);
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
        Assert.IsNotNull(constructor);
        return Assert.IsExactInstanceOfType<RegexMatcher>(constructor.Invoke([automaton]));
    }

    private static RegexAutomaton GetAutomaton(object facade)
    {
        FieldInfo? field = facade.GetType().GetField(
            "_automaton",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return Assert.IsExactInstanceOfType<RegexAutomaton>(field.GetValue(facade));
    }

    private static void PrimePikeVmPool(RegexAutomaton automaton)
    {
        using RegexFindRunner runner = automaton.RentFindRunner();
        Assert.IsGreaterThan(
            0, runner.PikeVmLeaseVersion,
            "The regression pattern must select the pooled Pike VM engine.");
    }

    private static void AssertRetainedRunner(
        RegexRunnerLeaseObserver observer,
        int matchCount)
    {
        Assert.AreEqual(2, matchCount);
        Assert.AreEqual(matchCount, observer.ObservationCount);
        Assert.AreEqual(1, observer.FirstLeaseVersion);
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
        RegexRunnerLeaseObserver observer = Assert.IsExactInstanceOfType<RegexRunnerLeaseObserver>(
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
