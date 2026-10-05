using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade reuses capture state for bounded URL matches.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[DoNotParallelize]
[TestClass]
public sealed class BoundedUrlCaptureApiTests(TestContext testContext)
{
    private const long CaptureAllocationLimit = 64 * 1024;
    private const int CaptureSearchTimeoutMilliseconds = 30_000;
    private const string ConnectionUrl = "postgresql://app_user:picket-db-password-123@db.internal.local:5432/appdb?sslmode=require";
    private const string Input = "DATABASE_URL=\"postgresql://app_user:picket-db-password-123@db.internal.local:5432/appdb?sslmode=require\"";
    private const string Pattern = """(?i)\b((?:postgres(?:ql)?|mysql|mariadb|sqlserver|mongodb(?:\+srv)?|redis)://[^:/?#@\s'"\x60;]{1,128}:[^@\s'"\x60;]{8,256}@[^\s'"\x60<>;]{3,512})(?:[\x60'"\s;]|\\[nr]|$)""";

    /// <summary>
    /// Verifies warmed bounded URL capture searches do not clone capture state for every NFA transition.
    /// </summary>
    /// <param name="engineMode">The public regex engine mode under test.</param>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    [Timeout(CaptureSearchTimeoutMilliseconds, CooperativeCancellation = true)]
    public void ReusesCaptureStateForBoundedUrlMatch(ByteRegexEngineMode engineMode)
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions { EngineMode = engineMode });
        byte[] input = Encoding.UTF8.GetBytes(Input);

        cancellationToken.ThrowIfCancellationRequested();
        Assert.HasCount(104, input);
        Assert.AreEqual(new ByteRegexMatch(14, 90), regex.Find(input));
        AssertBoundedUrlCaptures(regex.FindCaptures(input), input);

        cancellationToken.ThrowIfCancellationRequested();
        long findBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexMatch? match = regex.Find(input);
        long findAllocated = GC.GetAllocatedBytesForCurrentThread() - findBefore;

        cancellationToken.ThrowIfCancellationRequested();
        long capturesBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexCaptures? captures = regex.FindCaptures(input);
        long capturesAllocated = GC.GetAllocatedBytesForCurrentThread() - capturesBefore;

        cancellationToken.ThrowIfCancellationRequested();
        Assert.AreEqual(new ByteRegexMatch(14, 90), match);
        AssertBoundedUrlCaptures(captures, input);
        Assert.IsInRange(0, CaptureAllocationLimit, findAllocated);
        Assert.IsInRange(0, CaptureAllocationLimit, capturesAllocated);
    }

    private static void AssertBoundedUrlCaptures(ByteRegexCaptures? captures, byte[] input)
    {
        Assert.IsNotNull(captures);
        Assert.AreEqual(2, captures.GroupCount);
        Assert.AreEqual(new ByteRegexMatch(14, 90), captures.Match);
        Assert.AreEqual(captures.Match, captures.GetGroup(0));
        ByteRegexMatch? secret = captures.GetGroup(1);
        Assert.AreEqual(new ByteRegexMatch(14, 89), secret);
        Assert.AreEqual(ConnectionUrl, Encoding.UTF8.GetString(secret!.Value.Value(input)));
    }
}
