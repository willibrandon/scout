using System.Reflection;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the generic API-key rule with its trailing end assertion.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[TestClass]
public sealed class GitleaksEndAssertionTests(TestContext testContext)
{
    private const int InputLength = 4 * 1024 * 1024;
    private const int CandidateSpacing = 64;
    private const string Pattern = "(?i)[0-9A-Z_a-z.-]{0,50}?(?:access|auth|(?-i:[Aa]pi|API)|credential|creds|key|passw(?:or)?d|secret|token)(?:[ \\t0-9A-Z_a-z.-]{0,20})[\\t\\n\\f\\r '\"]{0,3}(?:=|>|:{1,3}=|\\|\\||:|=>|\\?=|,)[\\x60'\"\\t\\n\\f\\r =]{0,5}([0-9A-Z_a-z.=-]{10,150}|[a-z0-9][a-z0-9+/]{11,}={0,3})(?:[\\x60'\"\\t\\n\\f\\r ;]|\\\\[nr]|$)";

    /// <summary>
    /// Verifies the exact rule preserves delimiter, end-of-input, and capture semantics.
    /// </summary>
    /// <param name="engineMode">The public engine mode under test.</param>
    [TestMethod]
    [DataRow(ByteRegexEngineMode.Optimized)]
    [DataRow(ByteRegexEngineMode.General)]
    [DataRow(ByteRegexEngineMode.AutomataOnly)]
    public void ExactRulePreservesEndAssertionCaptures(ByteRegexEngineMode engineMode)
    {
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions
            {
                EngineMode = engineMode,
                MatchInvalidUtf8 = true,
            });
        byte[] atEnd = "api_key = abcdefghijkl"u8.ToArray();
        byte[] beforeDelimiter = "api_key = abcdefghijkl;"u8.ToArray();

        AssertCaptures(regex.FindCaptures(atEnd), atEnd, expectedMatchLength: atEnd.Length);
        AssertCaptures(
            regex.FindCaptures(beforeDelimiter),
            beforeDelimiter,
            expectedMatchLength: beforeDelimiter.Length);
    }

    /// <summary>
    /// Verifies the issue corpus completes through the primary contextual lazy DFA.
    /// </summary>
    /// <param name="invalid">Whether each candidate record ends in malformed UTF-8.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void ExactRuleRejectsFourMiBCandidateCorpusThroughDfaFastPath(bool invalid)
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions
            {
                EngineMode = ByteRegexEngineMode.General,
                MatchInvalidUtf8 = true,
            });
        byte[] input = CreateCandidateCorpus(invalid);
        RegexAutomaton automaton = GetAutomaton(regex);

        cancellationToken.ThrowIfCancellationRequested();
        Assert.AreEqual(RegexEngineKind.PikeVm, automaton.EngineKind);
        Assert.IsTrue(HasPrimaryUnanchoredDfaRunner(automaton));
        Assert.IsNull(regex.FindCaptures(input));
        Assert.AreEqual(invalid, HasActivatedPrimaryUnanchoredDfa(automaton));
        Assert.IsTrue(HasActivatedAnyUnanchoredDfa(automaton));
    }

    private static byte[] CreateCandidateCorpus(bool invalid)
    {
        byte[] input = GC.AllocateUninitializedArray<byte>(InputLength);
        input.AsSpan().Fill((byte)'x');
        ReadOnlySpan<byte> candidate = "api_key candidate "u8;
        for (int offset = 0; offset + CandidateSpacing <= input.Length; offset += CandidateSpacing)
        {
            candidate.CopyTo(input.AsSpan(offset));
            if (invalid)
            {
                input[offset + CandidateSpacing - 1] = 0xff;
            }
        }

        return input;
    }

    private static void AssertCaptures(
        ByteRegexCaptures? captures,
        byte[] input,
        int expectedMatchLength)
    {
        Assert.IsNotNull(captures);
        Assert.AreEqual(new ByteRegexMatch(0, expectedMatchLength), captures.Match);
        ByteRegexMatch secret = Assert.IsExactInstanceOfType<ByteRegexMatch>(captures.GetGroup(1));
        Assert.IsTrue(secret.Value(input).SequenceEqual("abcdefghijkl"u8));
    }

    private static RegexAutomaton GetAutomaton(ByteRegex regex)
    {
        return (RegexAutomaton)typeof(ByteRegex)
            .GetField("_automaton", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(regex)!;
    }

    private static bool HasPrimaryUnanchoredDfaRunner(RegexAutomaton automaton)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        RegexMetaEngine engine = GetMetaEngine(automaton);
        return typeof(RegexMetaEngine).GetField("_unanchoredLazyDfaPool", Flags)!.GetValue(engine) is not null ||
            typeof(RegexMetaEngine).GetField("_unanchoredLazyDfaFactory", Flags)!.GetValue(engine) is not null;
    }

    private static bool HasActivatedPrimaryUnanchoredDfa(RegexAutomaton automaton)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        RegexMetaEngine engine = GetMetaEngine(automaton);
        return (int)typeof(RegexMetaEngine)
            .GetField("_unanchoredLazyDfaActivated", Flags)!
            .GetValue(engine)! != 0;
    }

    private static bool HasActivatedAnyUnanchoredDfa(RegexAutomaton automaton)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        RegexMetaEngine engine = GetMetaEngine(automaton);
        return HasActivatedPrimaryUnanchoredDfa(automaton) ||
            (int)typeof(RegexMetaEngine)
                .GetField("_asciiFastUnanchoredDfaActivated", Flags)!
                .GetValue(engine)! != 0;
    }

    private static RegexMetaEngine GetMetaEngine(RegexAutomaton automaton)
    {
        return (RegexMetaEngine)typeof(RegexAutomaton)
            .GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(automaton)!;
    }
}
