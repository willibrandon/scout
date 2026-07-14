using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade reuses capture state for bounded URL matches.
/// </summary>
[Collection(BoundedUrlCaptureTestGroup.Name)]
public sealed class BoundedUrlCaptureApiTests()
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
    [Theory(Timeout = CaptureSearchTimeoutMilliseconds)]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void ReusesCaptureStateForBoundedUrlMatch(ByteRegexEngineMode engineMode)
    {
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions { EngineMode = engineMode });
        byte[] input = Encoding.UTF8.GetBytes(Input);

        Assert.Equal(104, input.Length);
        Assert.Equal(new ByteRegexMatch(14, 90), regex.Find(input));
        AssertBoundedUrlCaptures(regex.FindCaptures(input), input);

        long findBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexMatch? match = regex.Find(input);
        long findAllocated = GC.GetAllocatedBytesForCurrentThread() - findBefore;

        long capturesBefore = GC.GetAllocatedBytesForCurrentThread();
        ByteRegexCaptures? captures = regex.FindCaptures(input);
        long capturesAllocated = GC.GetAllocatedBytesForCurrentThread() - capturesBefore;

        Assert.Equal(new ByteRegexMatch(14, 90), match);
        AssertBoundedUrlCaptures(captures, input);
        Assert.InRange(findAllocated, 0, CaptureAllocationLimit);
        Assert.InRange(capturesAllocated, 0, CaptureAllocationLimit);
    }

    private static void AssertBoundedUrlCaptures(ByteRegexCaptures? captures, byte[] input)
    {
        Assert.NotNull(captures);
        Assert.Equal(2, captures.GroupCount);
        Assert.Equal(new ByteRegexMatch(14, 90), captures.Match);
        Assert.Equal(captures.Match, captures.GetGroup(0));
        ByteRegexMatch? secret = captures.GetGroup(1);
        Assert.Equal(new ByteRegexMatch(14, 89), secret);
        Assert.Equal(ConnectionUrl, Encoding.UTF8.GetString(secret!.Value.Value(input)));
    }
}
