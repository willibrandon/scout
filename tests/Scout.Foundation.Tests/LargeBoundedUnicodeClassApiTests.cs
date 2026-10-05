using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade handles large bounded Unicode classes without stalling.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[DoNotParallelize]
[TestClass]
public sealed class LargeBoundedUnicodeClassApiTests(TestContext testContext)
{
    private const int CandidateCount = 5000;
    private const int SearchTimeoutMilliseconds = 5000;
    private const string Pattern = "x[\\w-]{50,1000}";

    /// <summary>
    /// Verifies a large bounded Unicode class compiles and rejects the issue 32 candidate set without stalling.
    /// </summary>
    [TestMethod]
    [Timeout(SearchTimeoutMilliseconds, CooperativeCancellation = true)]
    public void RejectsLargeBoundedUnicodeClassCandidatesWithoutStalling()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat(Pattern + "\n", CandidateCount)));

        cancellationToken.ThrowIfCancellationRequested();
        Assert.IsNull(regex.Find(input));
    }
}
