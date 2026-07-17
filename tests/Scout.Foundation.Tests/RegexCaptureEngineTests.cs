using System.Text;

namespace Scout;

/// <summary>
/// Verifies ordered Pike VM capture replay semantics independently of regex engine specialization.
/// </summary>
public sealed class RegexCaptureEngineTests()
{
    /// <summary>
    /// Verifies capture search retains a later authoritative match after dense exact-prefix
    /// false candidates.
    /// </summary>
    [Fact]
    public void FindsCapturesAfterDenseExactPrefixFalseCandidates()
    {
        const string Pattern = "(?<prefix>abcdefgh(?:foo|bar))(?<digit>[0-9])";
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(Pattern));
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        Assert.NotNull(prefilter);
        Assert.False(prefilter.UsesRequiredLiteralWindow);
        RegexNfa nfa = RegexNfaCompiler.CompileCaptures(
            tree.Root,
            options,
            tree.CaptureCount);
        var engine = new RegexCaptureEngine(nfa, prefilter);
        string falseCandidates = string.Concat(
            Enumerable.Repeat("abcdefghfooX", RegexPrefilterState.MinimumSkipCount));
        byte[] haystack = Encoding.UTF8.GetBytes(falseCandidates + "abcdefghbar7");

        RegexCaptures? captures = engine.Find(haystack, startAt: 0);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(falseCandidates.Length, 12), captures.Match);
        Assert.Equal(new RegexMatch(falseCandidates.Length, 11), captures.GetGroup(1));
        Assert.Equal(new RegexMatch(falseCandidates.Length + 11, 1), captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies capture search retains a later authoritative match after dense required-literal
    /// false candidates.
    /// </summary>
    [Fact]
    public void FindsCapturesAfterDenseRequiredLiteralFalseCandidates()
    {
        const string Pattern = "(?:Z.{99}|Q)(?<word>needle)(?<tail>.)$";
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(Pattern));
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        Assert.NotNull(prefilter);
        Assert.True(prefilter.UsesRequiredLiteralWindow);
        RegexNfa nfa = RegexNfaCompiler.CompileCaptures(
            tree.Root,
            options,
            tree.CaptureCount);
        var engine = new RegexCaptureEngine(nfa, prefilter);
        string falseCandidates = string.Concat(
            Enumerable.Repeat("needle", RegexPrefilterState.MinimumSkipCount));
        byte[] haystack = Encoding.UTF8.GetBytes(falseCandidates + "Qneedle!");

        RegexCaptures? captures = engine.Find(haystack, startAt: 0);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(falseCandidates.Length, 8), captures.Match);
        Assert.Equal(new RegexMatch(falseCandidates.Length + 1, 6), captures.GetGroup(1));
        Assert.Equal(new RegexMatch(falseCandidates.Length + 7, 1), captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies an earlier alternative wins when a later alternative reaches the same state with more captures.
    /// </summary>
    [Fact]
    public void PrefersEarlierAlternativeOverAdditionalLaterCaptures()
    {
        RegexCaptureEngine engine = Compile("(a|a())");

        RegexCaptures? captures = engine.MatchAt("a"u8, 0, 1);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 1), captures.Match);
        Assert.Equal(new RegexMatch(0, 1), captures.GetGroup(1));
        Assert.Null(captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies bounded captures honor explicit and globally swapped repetition greed.
    /// </summary>
    /// <param name="pattern">The bounded repetition pattern.</param>
    /// <param name="expectedLength">The expected match and capture length.</param>
    [Theory]
    [InlineData("(a{1,3})a", 4)]
    [InlineData("(a{1,3}?)a", 2)]
    [InlineData("(?U)(a{1,3})a", 2)]
    public void HonorsBoundedCaptureGreed(string pattern, int expectedLength)
    {
        RegexCaptureEngine engine = Compile(pattern);

        RegexCaptures? captures = engine.Find("aaaa"u8, 0);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, expectedLength), captures.Match);
        Assert.Equal(new RegexMatch(0, expectedLength - 1), captures.GetGroup(1));
    }

    /// <summary>
    /// Verifies captures from an earlier path are restored before a later path succeeds.
    /// </summary>
    [Fact]
    public void RestoresNestedCapturesWhenEarlierPathFails()
    {
        RegexCaptureEngine engine = Compile("((a)|ab)c");

        RegexCaptures? captures = engine.MatchAt("abc"u8, 0, 3);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 3), captures.Match);
        Assert.Equal(new RegexMatch(0, 2), captures.GetGroup(1));
        Assert.Null(captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies reusable runner state does not retain captures across successes and misses.
    /// </summary>
    [Fact]
    public void ClearsCaptureStateAcrossSuccessAndMissCalls()
    {
        RegexCaptureEngine engine = Compile("((a)|b)c");

        RegexCaptures? capturedBranch = engine.MatchAt("ac"u8, 0, 2);
        RegexCaptures? uncapturedBranch = engine.MatchAt("bc"u8, 0, 2);
        RegexCaptures? miss = engine.MatchAt("bd"u8, 0, 2);
        RegexCaptures? afterMiss = engine.MatchAt("bc"u8, 0, 2);

        Assert.NotNull(capturedBranch);
        Assert.Equal(new RegexMatch(0, 1), capturedBranch.GetGroup(1));
        Assert.Equal(new RegexMatch(0, 1), capturedBranch.GetGroup(2));
        Assert.NotNull(uncapturedBranch);
        Assert.Equal(new RegexMatch(0, 1), uncapturedBranch.GetGroup(1));
        Assert.Null(uncapturedBranch.GetGroup(2));
        Assert.Null(miss);
        Assert.NotNull(afterMiss);
        Assert.Equal(new RegexMatch(0, 1), afterMiss.GetGroup(1));
        Assert.Null(afterMiss.GetGroup(2));
    }

    /// <summary>
    /// Verifies closing a nullable loop retains its last consuming capture for both priorities.
    /// </summary>
    /// <param name="pattern">The nullable repetition pattern.</param>
    /// <param name="captureStart">The expected capture start.</param>
    /// <param name="captureLength">The expected capture length.</param>
    [Theory]
    [InlineData("(a?)*b", 1, 1)]
    [InlineData("(a?)*?b", 1, 1)]
    public void RetainsLastConsumingCaptureWhenNullableLoopCloses(
        string pattern,
        int captureStart,
        int captureLength)
    {
        RegexCaptureEngine engine = Compile(pattern);

        RegexCaptures? captures = engine.MatchAt("aab"u8, 0, 3);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 3), captures.Match);
        RegexMatch? capture = captures.GetGroup(1);
        Assert.NotNull(capture);
        Assert.Equal(captureStart, capture.Value.Start);
        Assert.Equal(captureLength, capture.Value.Length);
    }

    /// <summary>
    /// Verifies greedy nullable repetition consumes while lazy repetition can exit without participating.
    /// </summary>
    /// <param name="pattern">The nullable repetition pattern.</param>
    /// <param name="matchLength">The expected whole-match length.</param>
    /// <param name="captureStart">The expected capture start, or negative when it must not participate.</param>
    /// <param name="captureLength">The expected capture length.</param>
    [Theory]
    [InlineData("(a?)*", 2, 1, 1)]
    [InlineData("(a?)*?", 0, -1, 0)]
    public void HonorsNullableLoopGreed(
        string pattern,
        int matchLength,
        int captureStart,
        int captureLength)
    {
        RegexCaptureEngine engine = Compile(pattern);

        RegexCaptures? captures = engine.Find("aa"u8, 0);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, matchLength), captures.Match);
        RegexMatch? capture = captures.GetGroup(1);
        if (captureStart < 0)
        {
            Assert.Null(capture);
        }
        else
        {
            Assert.NotNull(capture);
            Assert.Equal(captureStart, capture.Value.Start);
            Assert.Equal(captureLength, capture.Value.Length);
        }
    }

    /// <summary>
    /// Verifies nullable repeated captures match regex-automata's Fowler reference cases.
    /// </summary>
    /// <param name="pattern">The Fowler nullable repetition pattern.</param>
    /// <param name="input">The haystack to match.</param>
    /// <param name="captureStart">The expected first-group start, or negative when it must not participate.</param>
    /// <param name="captureLength">The expected first-group length.</param>
    [Theory]
    [InlineData("(a*)*(x)", "x", -1, 0)]
    [InlineData("(a*)*(x)", "ax", 0, 1)]
    [InlineData("(a*)+(x)", "x", 0, 0)]
    public void MatchesFowlerNullableCaptureSemantics(
        string pattern,
        string input,
        int captureStart,
        int captureLength)
    {
        RegexCaptureEngine engine = Compile(pattern);
        byte[] haystack = Encoding.UTF8.GetBytes(input);

        RegexCaptures? captures = engine.MatchAt(haystack, 0, haystack.Length);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.Match);
        RegexMatch? repeated = captures.GetGroup(1);
        if (captureStart < 0)
        {
            Assert.Null(repeated);
        }
        else
        {
            Assert.NotNull(repeated);
            Assert.Equal(captureStart, repeated.Value.Start);
            Assert.Equal(captureLength, repeated.Value.Length);
        }

        Assert.Equal(new RegexMatch(haystack.Length - 1, 1), captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies an earlier empty loop exit wins over a later empty alternative with another capture.
    /// </summary>
    [Fact]
    public void PreservesEarlierEmptyAlternativeAtReconvergence()
    {
        RegexCaptureEngine engine = Compile("(a*|())a");

        RegexCaptures? captures = engine.MatchAt("a"u8, 0, 1);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 1), captures.Match);
        Assert.Equal(new RegexMatch(0, 0), captures.GetGroup(1));
        Assert.Null(captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies zero-width predicates use the complete haystack around a nonzero exact span.
    /// </summary>
    [Fact]
    public void EvaluatesZeroWidthPredicatesAroundExactSpan()
    {
        RegexCaptureEngine engine = Compile("(?m)(^)(a+)($)");

        RegexCaptures? captures = engine.MatchAt("x\na\nz"u8, 2, 3);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(2, 1), captures.Match);
        Assert.Equal(new RegexMatch(2, 0), captures.GetGroup(1));
        Assert.Equal(new RegexMatch(2, 1), captures.GetGroup(2));
        Assert.Equal(new RegexMatch(3, 0), captures.GetGroup(3));
    }

    /// <summary>
    /// Verifies alternatives using one-to-four-byte scalar transitions reconverge with ordered captures.
    /// </summary>
    [Fact]
    public void ReconvergesMixedUtf8TransitionWidths()
    {
        RegexCaptureEngine engine = Compile("(?:(A¢€💩)|((?-u:..........)))");
        byte[] haystack = Encoding.UTF8.GetBytes("A¢€💩");

        RegexCaptures? captures = engine.MatchAt(haystack, 0, haystack.Length);

        Assert.Equal(10, haystack.Length);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.Match);
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.GetGroup(1));
        Assert.Null(captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies exact-span replay ignores earlier lazy accepts and captures through the requested end.
    /// </summary>
    [Fact]
    public void ReplaysThroughRequestedEndInsteadOfEarlierAccept()
    {
        RegexCaptureEngine engine = Compile("(a+?)");

        RegexCaptures? longer = engine.MatchAt("zzaaa!"u8, 2, 5);
        RegexCaptures? shorter = engine.MatchAt("zzaaa!"u8, 2, 4);

        Assert.NotNull(longer);
        Assert.Equal(new RegexMatch(2, 3), longer.Match);
        Assert.Equal(new RegexMatch(2, 3), longer.GetGroup(1));
        Assert.NotNull(shorter);
        Assert.Equal(new RegexMatch(2, 2), shorter.Match);
        Assert.Equal(new RegexMatch(2, 2), shorter.GetGroup(1));
    }

    /// <summary>
    /// Verifies warmed exact-span replay writes absolute capture pairs without allocating.
    /// </summary>
    [Fact]
    public void ReplaysIntoCallerOwnedCaptureSlotsWithoutAllocating()
    {
        RegexCaptureEngine engine = Compile(@"\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*)");
        byte[] haystack = "xx struct Foo yy"u8.ToArray();
        int[] captureSlots = new int[6];

        for (int index = 0; index < 32; index++)
        {
            Assert.True(engine.TryReplayCaptures(haystack, 3, 13, captureSlots));
        }

        bool replayed = true;
        int checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_024; index++)
        {
            replayed &= engine.TryReplayCaptures(haystack, 3, 13, captureSlots);
            checksum += captureSlots[5];
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(replayed);
        Assert.Equal(13 * 1_024, checksum);
        Assert.Equal([3, 13, 3, 9, 10, 13], captureSlots);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// Verifies concurrent exact-span replay rents independent mutable engines while sharing one automaton.
    /// </summary>
    [Fact]
    public void ConcurrentCallerOwnedCaptureReplayIsIndependent()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx struct Foo yy"u8.ToArray();
        int failures = 0;

        Parallel.For(0, 512, _ =>
        {
            int[] captureSlots = new int[automaton.CaptureSlotCount];
            if (!automaton.TryReplayCaptures(haystack, 3, 13, captureSlots) ||
                !captureSlots.AsSpan().SequenceEqual([3, 13, 3, 9, 10, 13]))
            {
                System.Threading.Interlocked.Increment(ref failures);
            }
        });

        Assert.Equal(0, failures);
    }

    private static RegexCaptureEngine Compile(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        RegexNfa nfa = RegexNfaCompiler.CompileCaptures(tree.Root, options, tree.CaptureCount);
        return new RegexCaptureEngine(nfa, prefilter: null);
    }
}
