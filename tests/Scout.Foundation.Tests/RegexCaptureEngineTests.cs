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
        int[] captureSlots = new int[6];

        RegexCaptures? captures = engine.MatchAt("abc"u8, 0, 3);
        bool replayed = engine.TryReplayCaptures("abc"u8, 0, 3, captureSlots);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 3), captures.Match);
        Assert.Equal(new RegexMatch(0, 2), captures.GetGroup(1));
        Assert.Null(captures.GetGroup(2));
        Assert.True(replayed);
        Assert.Equal([0, 3, 0, 2, -1, -1], captureSlots);
        Assert.False(engine.IsOnePassReplayEnabled);
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
        int[] captureSlots = new int[4];

        RegexCaptures? captures = engine.MatchAt("aab"u8, 0, 3);
        bool replayed = engine.TryReplayCaptures("aab"u8, 0, 3, captureSlots);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 3), captures.Match);
        RegexMatch? capture = captures.GetGroup(1);
        Assert.NotNull(capture);
        Assert.Equal(captureStart, capture.Value.Start);
        Assert.Equal(captureLength, capture.Value.Length);
        Assert.True(replayed);
        Assert.Equal(
            [0, 3, captureStart, captureStart + captureLength],
            captureSlots);
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
        int[] captureSlots = new int[8];

        RegexCaptures? captures = engine.MatchAt("x\na\nz"u8, 2, 3);
        bool replayed = engine.TryReplayCaptures(
            "x\na\nz"u8,
            startAt: 2,
            endAt: 3,
            captureSlots);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(2, 1), captures.Match);
        Assert.Equal(new RegexMatch(2, 0), captures.GetGroup(1));
        Assert.Equal(new RegexMatch(2, 1), captures.GetGroup(2));
        Assert.Equal(new RegexMatch(3, 0), captures.GetGroup(3));
        Assert.True(replayed);
        Assert.Equal([2, 3, 2, 2, 2, 3, 3, 3], captureSlots);
        Assert.Equal(1, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies alternatives using one-to-four-byte scalar transitions reconverge with ordered captures.
    /// </summary>
    [Fact]
    public void ReconvergesMixedUtf8TransitionWidths()
    {
        RegexCaptureEngine engine = Compile("(?:(A¢€💩)|((?-u:..........)))");
        byte[] haystack = Encoding.UTF8.GetBytes("A¢€💩");
        int[] captureSlots = new int[6];

        RegexCaptures? captures = engine.MatchAt(haystack, 0, haystack.Length);
        bool replayed = engine.TryReplayCaptures(
            haystack,
            startAt: 0,
            endAt: haystack.Length,
            captureSlots);

        Assert.Equal(10, haystack.Length);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.Match);
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.GetGroup(1));
        Assert.Null(captures.GetGroup(2));
        Assert.True(replayed);
        Assert.Equal([0, haystack.Length, 0, haystack.Length, -1, -1], captureSlots);
        Assert.False(engine.IsOnePassReplayEnabled);
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
        Assert.Equal(1_056, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// Verifies one-pass replay clears caller-owned slots beyond the compiled capture count.
    /// </summary>
    [Fact]
    public void OnePassReplayClearsOversizedCaptureSlotBuffer()
    {
        RegexCaptureEngine engine = Compile(
            @"\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*)");
        int[] captureSlots = Enumerable.Repeat(42, 20).ToArray();

        bool replayed = engine.TryReplayCaptures(
            "xx struct Foo yy"u8,
            startAt: 3,
            endAt: 13,
            captureSlots);

        Assert.True(replayed);
        Assert.Equal([3, 13, 3, 9, 10, 13], captureSlots[..6]);
        Assert.All(captureSlots[6..], value => Assert.Equal(-1, value));
        Assert.Equal(1, engine.OnePassReplayCount);
    }

    /// <summary>
    /// Verifies NFAs above the explicit capture-slot budget use the general replay engine.
    /// </summary>
    [Fact]
    public void FallsBackWhenOnePassCaptureSlotBudgetIsExceeded()
    {
        string pattern = string.Concat(Enumerable.Repeat("()", 15)) + "(a)";
        RegexCaptureEngine engine = Compile(pattern);
        int[] captureSlots = new int[34];

        bool replayed = engine.TryReplayCaptures("a"u8, 0, 1, captureSlots);

        Assert.True(replayed);
        Assert.Equal([0, 1], captureSlots[..2]);
        for (int captureIndex = 1; captureIndex <= 15; captureIndex++)
        {
            Assert.Equal(0, captureSlots[2 * captureIndex]);
            Assert.Equal(0, captureSlots[(2 * captureIndex) + 1]);
        }

        Assert.Equal([0, 1], captureSlots[32..]);
        Assert.Equal(0, engine.OnePassReplayCount);
        Assert.False(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies a second matching consumer permanently yields exact replay to the ordered NFA.
    /// </summary>
    [Fact]
    public void FallsBackWhenCaptureReplayIsNotOnePass()
    {
        RegexCaptureEngine engine = Compile("(a|a())");
        int[] captureSlots = new int[6];

        bool replayed = engine.TryReplayCaptures("a"u8, 0, 1, captureSlots);

        Assert.True(replayed);
        Assert.Equal([0, 1, 0, 1, -1, -1], captureSlots);
        Assert.Equal(0, engine.OnePassReplayCount);
        Assert.False(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies an ambiguous prefix falls back before a shorter alternative can override exact bounds.
    /// </summary>
    [Fact]
    public void FallsBackBeforeResolvingAmbiguousExactBounds()
    {
        RegexCaptureEngine engine = Compile("(a|ab)");
        int[] captureSlots = new int[4];

        bool replayed = engine.TryReplayCaptures("ab"u8, 0, 2, captureSlots);

        Assert.True(replayed);
        Assert.Equal([0, 2, 0, 2], captureSlots);
        Assert.Equal(0, engine.OnePassReplayCount);
        Assert.False(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies one-pass replay supports deterministic variable-width Unicode atoms.
    /// </summary>
    [Fact]
    public void ReplaysVariableWidthUnicodeCapturesInOnePass()
    {
        RegexCaptureEngine engine = Compile(@"(\s+)([A-Za-z_][A-Za-z0-9_]*)");
        byte[] haystack = Encoding.UTF8.GetBytes("xx\u00A0Name");
        int[] captureSlots = new int[6];

        bool replayed = engine.TryReplayCaptures(haystack, 2, haystack.Length, captureSlots);

        Assert.True(replayed);
        Assert.Equal([2, 8, 2, 4, 4, 8], captureSlots);
        Assert.Equal(1, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies a deterministic capture replay follows authoritative ends past earlier lazy accepts.
    /// </summary>
    [Fact]
    public void ReplaysLazyCaptureThroughEachRequestedEndInOnePass()
    {
        RegexCaptureEngine engine = Compile("(a+?)");
        int[] captureSlots = new int[4];

        Assert.True(engine.TryReplayCaptures("zzaaa!"u8, 2, 5, captureSlots));
        Assert.Equal([2, 5, 2, 5], captureSlots);
        Assert.True(engine.TryReplayCaptures("zzaaa!"u8, 2, 4, captureSlots));
        Assert.Equal([2, 4, 2, 4], captureSlots);
        Assert.Equal(2, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies deterministic one-pass replay agrees with the ordered NFA across small haystacks.
    /// </summary>
    [Fact]
    public void OnePassReplayMatchesGeneralEngineAcrossSmallHaystacks()
    {
        string[] patterns =
        [
            "((a+?))(b*)",
            "(a*)(b?)",
            "(a?)(b+)",
            "(a|b)((?:a|b)*)",
        ];

        foreach (string pattern in patterns)
        {
            foreach (byte[] haystack in GenerateSmallHaystacks())
            {
                RegexCaptureEngine engine = Compile(pattern);
                for (int start = 0; start <= haystack.Length; start++)
                {
                    for (int end = start; end <= haystack.Length; end++)
                    {
                        RegexCaptures? expected = engine.MatchAt(haystack, start, end);
                        int groupCount = expected?.GroupCount ??
                            RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern)).CaptureCount + 1;
                        int[] actualSlots = new int[checked(2 * groupCount)];

                        bool replayed = engine.TryReplayCaptures(
                            haystack,
                            start,
                            end,
                            actualSlots);

                        Assert.Equal(expected is not null, replayed);
                        if (expected is not null)
                        {
                            AssertCaptureSlots(expected, actualSlots);
                        }
                    }
                }

                Assert.True(engine.IsOnePassReplayEnabled);
            }
        }
    }

    /// <summary>
    /// Verifies predicate-bearing closures are reevaluated for every authoritative span.
    /// </summary>
    [Fact]
    public void ReevaluatesPredicateClosuresAcrossExactReplays()
    {
        RegexCaptureEngine engine = Compile(@"\b(foo)");
        byte[] haystack = "foo xfoo foo"u8.ToArray();
        int[] captureSlots = new int[4];

        Assert.True(engine.TryReplayCaptures(haystack, 0, 3, captureSlots));
        Assert.Equal([0, 3, 0, 3], captureSlots);
        Assert.False(engine.TryReplayCaptures(haystack, 5, 8, captureSlots));
        Assert.True(engine.TryReplayCaptures(haystack, 9, 12, captureSlots));
        Assert.Equal([9, 12, 9, 12], captureSlots);
        Assert.Equal(2, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies cached literal branches retain independent participating-capture actions.
    /// </summary>
    [Fact]
    public void CachedLiteralBranchesPreserveParticipatingCaptures()
    {
        RegexCaptureEngine engine = Compile("((foo)|(bar))");
        int[] captureSlots = new int[8];

        Assert.True(engine.TryReplayCaptures("foo"u8, 0, 3, captureSlots));
        Assert.Equal([0, 3, 0, 3, 0, 3, -1, -1], captureSlots);
        Assert.True(engine.TryReplayCaptures("bar"u8, 0, 3, captureSlots));
        Assert.Equal([0, 3, 0, 3, -1, -1, 0, 3], captureSlots);
        Assert.True(engine.TryReplayCaptures("foo"u8, 0, 3, captureSlots));
        Assert.Equal([0, 3, 0, 3, 0, 3, -1, -1], captureSlots);
        Assert.Equal(3, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies a compiled literal run still observes the authoritative exclusive end.
    /// </summary>
    [Fact]
    public void LiteralRunReplayHonorsAuthoritativeEnd()
    {
        RegexCaptureEngine engine = Compile("(foobar)");
        int[] captureSlots = new int[4];

        Assert.False(engine.TryReplayCaptures("foobar"u8, 0, 4, captureSlots));
        Assert.False(engine.TryReplayCaptures("fooxar"u8, 0, 6, captureSlots));
        Assert.True(engine.TryReplayCaptures("foobar"u8, 0, 6, captureSlots));
        Assert.Equal([0, 6, 0, 6], captureSlots);
        Assert.Equal(1, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
    }

    /// <summary>
    /// Verifies the bounded one-pass engine applies capture actions through mask bit 31.
    /// </summary>
    [Fact]
    public void OnePassReplaySupportsHighestCaptureActionBit()
    {
        string pattern = string.Concat(Enumerable.Repeat("()", 14)) + "(a)";
        RegexCaptureEngine engine = Compile(pattern);
        int[] captureSlots = new int[32];

        Assert.True(engine.TryReplayCaptures("a"u8, 0, 1, captureSlots));
        Assert.Equal([0, 1], captureSlots[..2]);
        for (int captureIndex = 1; captureIndex <= 14; captureIndex++)
        {
            Assert.Equal(0, captureSlots[2 * captureIndex]);
            Assert.Equal(0, captureSlots[(2 * captureIndex) + 1]);
        }

        Assert.Equal([0, 1], captureSlots[30..]);
        Assert.Equal(1, engine.OnePassReplayCount);
        Assert.True(engine.IsOnePassReplayEnabled);
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

    /// <summary>
    /// Verifies one operation-scoped runner replays many exact spans without allocation.
    /// </summary>
    [Fact]
    public void OperationScopedCaptureRunnerReplaysWithoutAllocating()
    {
        RegexAutomaton automaton = CompileAutomaton(
            @"\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*)");
        byte[] haystack = "xx struct Foo yy"u8.ToArray();
        int[] captureSlots = new int[automaton.CaptureSlotCount];
        RegexCaptureRunner runner = automaton.RentCaptureRunner();
        try
        {
            for (int index = 0; index < 32; index++)
            {
                Assert.True(runner.TryReplayCaptures(haystack, 3, 13, captureSlots));
            }

            bool replayed = true;
            int checksum = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1_024; index++)
            {
                replayed &= runner.TryReplayCaptures(haystack, 3, 13, captureSlots);
                checksum += captureSlots[5];
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(replayed);
            Assert.Equal(13 * 1_024, checksum);
            Assert.Equal([3, 13, 3, 9, 10, 13], captureSlots);
            Assert.Equal(0, allocated);
        }
        finally
        {
            runner.Dispose();
        }
    }

    /// <summary>
    /// Verifies copied or disposed capture leases cannot return or reuse pooled state twice.
    /// </summary>
    [Fact]
    public void CopiedCaptureRunnerLeaseReturnsPooledStateOnce()
    {
        RegexAutomaton automaton = CompileAutomaton("(a+)(b)");
        RegexCaptureRunner runner = automaton.RentCaptureRunner();
        RegexCaptureRunner copy = runner;
        long leaseVersion = runner.LeaseVersion;

        Assert.True(runner.IsInitialized);
        Assert.True(copy.IsInitialized);
        Assert.True(runner.SharesPooledStateWith(in copy));

        runner.Dispose();

        Assert.False(copy.IsInitialized);
        Assert.Throws<ObjectDisposedException>(() =>
            copy.TryReplayCaptures("aaab"u8, 0, 4, new int[6]));
        copy.Dispose();

        RegexCaptureRunner reused = automaton.RentCaptureRunner();
        try
        {
            Assert.True(reused.IsInitialized);
            Assert.True(reused.LeaseVersion > leaseVersion);
            Assert.True(reused.TryReplayCaptures("aaab"u8, 0, 4, new int[6]));
        }
        finally
        {
            reused.Dispose();
        }
    }

    /// <summary>
    /// Verifies concurrent operation leases never share one mutable capture engine.
    /// </summary>
    [Fact]
    public void ConcurrentCaptureRunnerLeasesUseIndependentState()
    {
        RegexAutomaton automaton = CompileAutomaton("(a+)(b)");
        RegexCaptureRunner first = automaton.RentCaptureRunner();
        RegexCaptureRunner second = automaton.RentCaptureRunner();
        try
        {
            Assert.False(first.SharesPooledStateWith(in second));
            Assert.True(first.TryReplayCaptures("aaab"u8, 0, 4, new int[6]));
            Assert.True(second.TryReplayCaptures("aab"u8, 0, 3, new int[6]));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    /// <summary>
    /// Verifies simultaneous long-lived capture leases execute on independent mutable engines.
    /// </summary>
    [Fact]
    public async Task ConcurrentCaptureRunnerLeasesReplaySimultaneouslyAsync()
    {
        RegexAutomaton automaton = CompileAutomaton("(a+)(b)");
        RegexCaptureRunner first = automaton.RentCaptureRunner();
        RegexCaptureRunner second = automaton.RentCaptureRunner();
        using var barrier = new Barrier(participantCount: 2);
        try
        {
            Task<int[]> firstTask = Task.Run(() => Replay(first, "aaab"u8.ToArray()));
            Task<int[]> secondTask = Task.Run(() => Replay(second, "aab"u8.ToArray()));

            int[][] results = await Task.WhenAll(firstTask, secondTask).ConfigureAwait(true);

            Assert.Equal([0, 4, 0, 3, 3, 4], results[0]);
            Assert.Equal([0, 3, 0, 2, 2, 3], results[1]);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }

        int[] Replay(RegexCaptureRunner runner, byte[] haystack)
        {
            int[] captureSlots = new int[6];
            barrier.SignalAndWait();
            for (int index = 0; index < 1_024; index++)
            {
                Assert.True(runner.TryReplayCaptures(
                    haystack,
                    startAt: 0,
                    endAt: haystack.Length,
                    captureSlots));
            }

            return captureSlots;
        }
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

    private static RegexAutomaton CompileAutomaton(string pattern)
    {
        return RegexAutomaton.Compile(
            Encoding.UTF8.GetBytes(pattern),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true);
    }

    private static void AssertCaptureSlots(RegexCaptures expected, ReadOnlySpan<int> actual)
    {
        Assert.Equal(checked(2 * expected.GroupCount), actual.Length);
        for (int index = 0; index < expected.GroupCount; index++)
        {
            RegexMatch? group = expected.GetGroup(index);
            if (group.HasValue)
            {
                Assert.Equal(group.Value.Start, actual[2 * index]);
                Assert.Equal(group.Value.End, actual[(2 * index) + 1]);
            }
            else
            {
                Assert.Equal(-1, actual[2 * index]);
                Assert.Equal(-1, actual[(2 * index) + 1]);
            }
        }
    }

    private static IEnumerable<byte[]> GenerateSmallHaystacks()
    {
        yield return [];
        for (int length = 1; length <= 4; length++)
        {
            int count = 1 << length;
            for (int bits = 0; bits < count; bits++)
            {
                byte[] haystack = new byte[length];
                for (int index = 0; index < length; index++)
                {
                    haystack[index] = (bits & (1 << index)) == 0 ? (byte)'a' : (byte)'b';
                }

                yield return haystack;
            }
        }
    }
}
