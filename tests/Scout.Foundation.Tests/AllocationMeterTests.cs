using System.Diagnostics;

namespace Scout;

/// <summary>
/// Verifies the allocation meter reports exact figures and never stalls its caller.
/// </summary>
[TestClass]
public sealed class AllocationMeterTests
{
    private const int ArrayLength = 64 * 1024;
    private const int ArrayOverhead = 1024;

    /// <summary>
    /// Verifies an action that allocates nothing measures as zero bytes.
    /// </summary>
    [TestMethod]
    public void MeasureReportsNothingForAnActionThatDoesNotAllocate()
    {
        Assert.AreEqual(0, AllocationMeter.Measure(static () => { }));
    }

    /// <summary>
    /// Verifies a single array allocation measures as its length plus object overhead.
    /// </summary>
    [TestMethod]
    public void MeasureReportsTheBytesAnActionAllocates()
    {
        byte[]? sink = null;
        long measured = AllocationMeter.Measure(() => sink = new byte[ArrayLength]);
        Assert.IsNotNull(sink);
        Assert.IsInRange(ArrayLength, ArrayLength + ArrayOverhead, measured);
    }

    /// <summary>
    /// Verifies the meter gives up within its time limit when a collection overlaps every window.
    /// </summary>
    [TestMethod]
    public void MeasureReturnsPromptlyWhenEveryWindowOverlapsACollection()
    {
        long started = Stopwatch.GetTimestamp();
        long measured = AllocationMeter.Measure(static () => GC.Collect(0));
        Assert.IsGreaterThanOrEqualTo(0, measured);
        Assert.IsLessThan(TimeSpan.FromSeconds(10), Stopwatch.GetElapsedTime(started));
    }
}
