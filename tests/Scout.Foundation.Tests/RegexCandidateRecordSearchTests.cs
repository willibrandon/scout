using System.Text;

namespace Scout;

/// <summary>
/// Verifies syntax-derived regex prefilters discover candidate records that are then matched by
/// the authoritative regex engine.
/// </summary>
public sealed class RegexCandidateRecordSearchTests
{
    private const string HeldOutPattern =
        @"\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*";

    /// <summary>
    /// Verifies the held-out workload uses the CLI-equivalent General plan with its Teddy
    /// prefilter and preserves matching-line output on a large segment.
    /// </summary>
    [Fact]
    public void HeldOutPatternUsesGeneralPrefilteredSearchPlan()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        for (int index = 0; index < 64; index++)
        {
            source.Append('x', 80).Append('\n');
        }

        source.Append("enum Ready");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        Assert.False(HasActivatedAsciiProjection(plan.Matcher));

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: false);

        Assert.Equal(RegexEngineKind.OnePassDfa, plan.Matcher.EngineKind);
        Assert.Equal(RegexPrefilterKind.Teddy, plan.Matcher.PrefilterKind);
        Assert.True(plan.Matcher.HasAsciiProjectedMatchEndRunner);
        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(65, sink.LineNumber);
        Assert.Equal("enum Ready"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies record slicing declines plans whose matches may cross record boundaries.
    /// </summary>
    [Fact]
    public void CandidateRecordSearchDeclinesMultilinePlans()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        using RegexSpecializationModeScope scope =
            RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                multiline: true));
        byte[] haystack = Encoding.ASCII.GetBytes(
            new string('x', 4_096) + "\nstruct Ready\n");
        var sink = new CapturingLineSink();

        bool handled = RegexPrefilterRecordSearcher.TrySearchLines(
            haystack,
            plan,
            ref sink,
            out bool matched,
            out long searchedLines,
            countSearchedLines: true,
            maxMatchingLines: null,
            nullData: false,
            requireMatchColumn: false);

        Assert.False(handled);
        Assert.False(matched);
        Assert.Equal(0, searchedLines);
        Assert.Equal(0UL, sink.MatchedLines);
    }

    /// <summary>
    /// Verifies candidate-record search leaves engines without retained verifier state on their
    /// existing authoritative paths.
    /// </summary>
    [Fact]
    public void CandidateRecordSearchDeclinesEnginesWithoutRetainedVerifierState()
    {
        byte[][] patterns = [@"\babc\b"u8.ToArray()];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        RegexPrefilterRunner runner =
            plan.Matcher.CreateCandidateRecordPrefilterRunner(haystackLength: 64 * 1024);

        Assert.Equal(RegexEngineKind.BoundedBacktracker, plan.Matcher.EngineKind);
        Assert.Equal(RegexPrefilterKind.RequiredLiteral, plan.Matcher.PrefilterKind);
        Assert.False(runner.IsAvailable);
    }

    /// <summary>
    /// Verifies sparse candidate records preserve selected-line counts, line numbers, and byte
    /// offsets after large candidate-free ranges.
    /// </summary>
    [Fact]
    public void SparseCandidatesPreserveSelectedRecordMetadata()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        int lastMatchOffset = 0;
        for (int lineIndex = 0; lineIndex < 64; lineIndex++)
        {
            if (lineIndex == 20)
            {
                source.Append("struct First\n");
            }
            else if (lineIndex == 63)
            {
                lastMatchOffset = source.Length;
                source.Append("union Last\n");
            }
            else
            {
                source.Append('x', 80).Append('\n');
            }
        }

        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(64, sink.LineNumber);
        Assert.Equal(lastMatchOffset, sink.ByteOffset);
        Assert.Equal("union Last\n"u8.ToArray(), sink.Line.ToArray());
        Assert.False(HasActivatedAsciiProjection(plan.Matcher));
    }

    /// <summary>
    /// Verifies prefix hits that do not satisfy the regex cannot select a record and do not hide
    /// a later authoritative match.
    /// </summary>
    [Fact]
    public void FalseCandidatesRequireAuthoritativeRecordMatches()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        for (int index = 0; index < 48; index++)
        {
            source.Append("struct!\n");
            source.Append('x', 96).Append('\n');
        }

        int matchOffset = source.Length;
        source.Append("enum Accepted\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(97, sink.LineNumber);
        Assert.Equal(matchOffset, sink.ByteOffset);
        Assert.Equal("enum Accepted\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies a false exact-prefix candidate cannot hide a later authoritative match in the
    /// same record.
    /// </summary>
    [Fact]
    public void FalseExactCandidateContinuesWithinTheSameRecord()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        source.Append('x', 4_096).Append('\n');
        source.Append("struct! enum Accepted\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        RegexPrefilterRunner runner =
            plan.Matcher.CreateCandidateRecordPrefilterRunner(haystack.Length);
        Assert.True(runner.UsesExactStartCandidates);

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: true);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(9, sink.MatchColumn);
        Assert.Equal("struct! enum Accepted\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies several prefilter hits and authoritative matches in one record emit that record
    /// exactly once.
    /// </summary>
    [Fact]
    public void MultipleCandidatesInOneRecordEmitTheRecordOnce()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        source.Append('x', 4_096).Append('\n');
        source.Append("struct First; enum Second; union Third;\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(
            "struct First; enum Second; union Third;\n"u8.ToArray(),
            sink.Line.ToArray());
        Assert.Equal(3, LiteralLineSearcher.CountMatchesWithRegexPlan(
            haystack,
            patterns,
            plan));
    }

    /// <summary>
    /// Verifies a required literal found inside a possible match selects the containing record
    /// for full authoritative verification.
    /// </summary>
    [Fact]
    public void RequiredInnerLiteralCandidatesVerifyCompleteRecords()
    {
        byte[][] patterns = [@"\w+GeneratedRecord"u8.ToArray()];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        for (int index = 0; index < 48; index++)
        {
            source.Append("---GeneratedRecord---\n");
            source.Append('!', 96).Append('\n');
        }

        source.Append("abcGeneratedRecord\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        RegexPrefilterRunner runner =
            plan.Matcher.CreateCandidateRecordPrefilterRunner(haystack.Length);
        Assert.False(runner.UsesExactStartCandidates);

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: false);

        Assert.Equal(RegexPrefilterKind.RequiredLiteral, plan.Matcher.PrefilterKind);
        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(97, sink.LineNumber);
        Assert.Equal("abcGeneratedRecord\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies dense ineffective candidates hand off at a record boundary without missing the
    /// boundary record or emitting an earlier record twice.
    /// </summary>
    [Fact]
    public void DenseCandidatesHandOffWithoutMissingOrDuplicatingRecords()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder("struct First\n");
        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount - 2; index++)
        {
            source.Append("struct!\n");
        }

        source.Append("union Fortieth\n");
        source.Append("enum Boundary\n");
        for (int index = 0; index < 600; index++)
        {
            source.Append("union!\n");
        }

        source.Append("union Last\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        string[] expectedSelectedRecords =
        [
            "struct First\n",
            "union Fortieth\n",
            "enum Boundary\n",
            "union Last\n",
        ];

        Assert.False(HasActivatedAsciiProjection(plan.Matcher));

        for (int index = 0; index < expectedSelectedRecords.Length; index++)
        {
            var sink = new CapturingLineSink();
            bool matched = LiteralLineSearcher.SearchWithRegexPlan(
                haystack,
                patterns,
                plan,
                ref sink,
                maxMatchingLines: (ulong)index + 1,
                requireMatchColumn: false);

            Assert.True(matched);
            Assert.Equal((ulong)index + 1, sink.MatchedLines);
            Assert.Equal(
                Encoding.ASCII.GetBytes(expectedSelectedRecords[index]),
                sink.Line.ToArray());
        }

        Assert.True(HasActivatedAsciiProjection(plan.Matcher));
    }

    /// <summary>
    /// Verifies candidate-record search preserves CRLF and NUL record boundaries.
    /// </summary>
    /// <param name="terminator">The record terminator text.</param>
    /// <param name="crlf">Whether CRLF-aware matching is enabled.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    [Theory]
    [InlineData("\r\n", true, false)]
    [InlineData("\0", false, true)]
    public void CandidateRecordSearchPreservesConfiguredRecordBoundaries(
        string terminator,
        bool crlf,
        bool nullData)
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        using RegexSpecializationModeScope scope =
            RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                lineRegexp: false,
                wordRegexp: false,
                crlf,
                nullData));
        var source = new StringBuilder();
        for (int index = 0; index < 64; index++)
        {
            source.Append('x', 80).Append(terminator);
        }

        string selectedRecord = "enum Ready" + terminator;
        source.Append(selectedRecord);
        byte[] haystack = Encoding.UTF8.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            crlf: crlf,
            nullData: nullData,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(65, sink.LineNumber);
        Assert.Equal(Encoding.UTF8.GetBytes(selectedRecord), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies a matching-line limit reports the number of records searched through the first
    /// selected candidate record.
    /// </summary>
    [Fact]
    public void CandidateRecordSearchHonorsMatchingLineLimit()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        for (int index = 0; index < 64; index++)
        {
            source.Append('x', 80).Append('\n');
        }

        source.Append("struct First\n");
        source.Append('x', 4_096).Append('\n');
        source.Append("enum Second\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountLines(
            haystack,
            patterns,
            plan,
            ref sink,
            out long searchedLines,
            maxMatchingLines: 1,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(65, sink.LineNumber);
        Assert.Equal(65, searchedLines);
        Assert.Equal("struct First\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies definitive prefilter exhaustion reports every searched record, including a final
    /// unterminated record.
    /// </summary>
    [Fact]
    public void CandidateRecordSearchCountsRecordsAfterPrefilterExhaustion()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        for (int index = 0; index < 64; index++)
        {
            source.Append('x', 80).Append('\n');
        }

        source.Append('x', 80);
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountLines(
            haystack,
            patterns,
            plan,
            ref sink,
            out long searchedLines,
            requireMatchColumn: false);

        Assert.False(matched);
        Assert.Equal(0UL, sink.MatchedLines);
        Assert.Equal(65, searchedLines);
        Assert.False(HasActivatedAsciiProjection(plan.Matcher));
    }

    /// <summary>
    /// Verifies a dense candidate stream makes the operation-scoped prefilter inert and signals
    /// that authoritative search must resume without the filter.
    /// </summary>
    [Fact]
    public void DenseCandidateRunnerSignalsUnfilteredHandoff()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        byte[] haystack = Encoding.ASCII.GetBytes(string.Concat(
            Enumerable.Repeat("struct!\n", 600)));
        RegexPrefilterRunner runner =
            plan.Matcher.CreateCandidateRecordPrefilterRunner(haystack.Length);
        int searchOffset = 0;

        Assert.True(runner.IsAvailable);
        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            Assert.True(runner.TryFindCandidate(haystack, searchOffset, out int candidate));
            searchOffset = candidate + 1;
        }

        Assert.False(runner.IsInert);
        Assert.False(runner.TryFindCandidate(haystack, searchOffset, out int ignoredCandidate));
        Assert.Equal(-1, ignoredCandidate);
        Assert.True(runner.IsInert);
        Assert.Equal(RegexPrefilterState.MinimumSkipCount, runner.SkipCount);
    }

    /// <summary>
    /// Verifies a sparse candidate stream remains effective and a failed scan authoritatively
    /// reports that no candidate remains.
    /// </summary>
    [Fact]
    public void SparseCandidateRunnerReportsAuthoritativeExhaustion()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            source.Append('x', 128).Append("struct!\n");
        }

        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        RegexPrefilterRunner runner =
            plan.Matcher.CreateCandidateRecordPrefilterRunner(haystack.Length);
        int searchOffset = 0;

        Assert.True(runner.IsAvailable);
        for (int index = 0; index < RegexPrefilterState.MinimumSkipCount; index++)
        {
            Assert.True(runner.TryFindCandidate(haystack, searchOffset, out int candidate));
            searchOffset = candidate + 1;
        }

        Assert.False(runner.TryFindCandidate(haystack, searchOffset, out int ignoredCandidate));
        Assert.Equal(-1, ignoredCandidate);
        Assert.False(runner.IsInert);
        Assert.Equal(RegexPrefilterState.MinimumSkipCount + 1, runner.SkipCount);
    }

    /// <summary>
    /// Verifies candidate-record search reports the authoritative match start when the caller
    /// requests a column.
    /// </summary>
    [Fact]
    public void CandidateRecordSearchReportsAuthoritativeMatchColumn()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        source.Append('x', 4_096).Append('\n');
        source.Append("padding padding enum Result\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: true);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(17, sink.MatchColumn);
        Assert.Equal("padding padding enum Result\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies candidate discovery across non-ASCII records leaves Unicode word-boundary
    /// decisions to the authoritative matcher.
    /// </summary>
    [Fact]
    public void NonAsciiCandidateRecordsUseAuthoritativeWordBoundaries()
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(HeldOutPattern)];
        RegexSearchPlan plan = CreateGeneralPlan(patterns);
        var source = new StringBuilder();
        source.Append('x', 4_096).Append('\n');
        source.Append("\u03BBstruct Rejected\n");
        source.Append("\u03BB struct Accepted\n");
        byte[] haystack = Encoding.UTF8.GetBytes(source.ToString());
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(3, sink.LineNumber);
        Assert.Equal("\u03BB struct Accepted\n", Encoding.UTF8.GetString(sink.Line));
    }

    private static RegexSearchPlan CreateGeneralPlan(byte[][] patterns)
    {
        using RegexSpecializationModeScope scope =
            RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);
        return LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
    }

    private static bool HasActivatedAsciiProjection(RegexAutomaton matcher)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var engine = (RegexMetaEngine)typeof(RegexAutomaton)
            .GetField("engine", Flags)!
            .GetValue(matcher)!;
        return (int)typeof(RegexMetaEngine)
            .GetField("_asciiFastUnanchoredDfaActivated", Flags)!
            .GetValue(engine)! != 0;
    }
}
