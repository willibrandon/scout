using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the public byte regex facade handles large bounded Unicode classes without stalling.
/// </summary>
[Collection(LargeBoundedUnicodeClassTestGroup.Name)]
public sealed class LargeBoundedUnicodeClassApiTests
{
    private const int CandidateCount = 5000;
    private const int SearchTimeoutMilliseconds = 5000;
    private const string Pattern = "x[\\w-]{50,1000}";

    /// <summary>
    /// Verifies a large bounded Unicode class compiles and rejects the issue 32 candidate set without stalling.
    /// </summary>
    [Fact(Timeout = SearchTimeoutMilliseconds)]
    public void RejectsLargeBoundedUnicodeClassCandidatesWithoutStalling()
    {
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat(Pattern + "\n", CandidateCount)));

        Assert.Null(regex.Find(input));
    }
}
