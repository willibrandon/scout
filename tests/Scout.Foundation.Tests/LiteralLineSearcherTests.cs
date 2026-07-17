using System.Text;

namespace Scout;

/// <summary>
/// Verifies literal line search behavior.
/// </summary>
public sealed class LiteralLineSearcherTests
{
    /// <summary>
    /// Verifies inverted max-count lookahead is restricted to callers requesting an inspected extent.
    /// </summary>
    [Fact]
    public void InvertedMaxCountLookaheadIsOptInForExtentSearch()
    {
        var patterns = new CountingPatternList(["foo"u8.ToArray()]);
        var plan = RegexSearchPlan.Create(
            patterns,
            asciiCaseInsensitive: false);
        var directSink = new CapturingLineSink();
        patterns.ResetCandidateChecks();

        bool directMatched = LiteralLineSearcher.SearchWithRegexPlan(
            "bar\nfoo\nlast\n"u8,
            patterns,
            plan,
            ref directSink,
            invertMatch: true,
            maxMatchingLines: 1,
            requireMatchColumn: false);

        Assert.True(directMatched);
        Assert.Equal(1UL, directSink.MatchedLines);
        Assert.Equal(1, patterns.CandidateChecks);

        var extentSink = new CapturingLineSink();
        patterns.ResetCandidateChecks();
        bool extentMatched =
            LiteralLineSearcher.SearchInvertedWithRegexPlanAndCountBytes(
                "bar\nfoo\nlast\n"u8,
                patterns,
                plan,
                ref extentSink,
                out ulong searchedBytes,
                maxMatchingLines: 1,
                requireMatchColumn: false);

        Assert.True(extentMatched);
        Assert.Equal(1UL, extentSink.MatchedLines);
        Assert.Equal(2, patterns.CandidateChecks);
        Assert.Equal(8UL, searchedBytes);
    }

    /// <summary>
    /// Verifies whole-record matching keeps the single-pattern literal contract.
    /// </summary>
    [Fact]
    public void SinglePatternLineRegexpTreatsMetacharactersLiterally()
    {
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.Search(
            "a.c\nabc\n"u8,
            "a.c"u8,
            ref sink,
            lineRegexp: true);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal("a.c\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies line feeds remain literal content in NUL-terminated whole-record matching.
    /// </summary>
    [Fact]
    public void SinglePatternLineRegexpMatchesCompleteNullTerminatedRecord()
    {
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.Search(
            "a\nb\0a\0"u8,
            "a\nb"u8,
            ref sink,
            lineRegexp: true,
            nullData: true);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal("a\nb\0"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies parsed character-class intersections remain authoritative in line-oriented search.
    /// </summary>
    [Fact]
    public void CountMatchesUsesAuthoritativeCharacterClassIntersection()
    {
        byte[][] patterns = ["[a-z&&def]+"u8.ToArray()];

        long matches = LiteralLineSearcher.CountMatches(
            "abc\ndef\nfed\nxyz\n"u8,
            patterns);

        Assert.Equal(2, matches);
    }

    /// <summary>
    /// Verifies a leading unscoped multiline flag is interpreted by the parsed automaton.
    /// </summary>
    [Fact]
    public void CountMatchesHonorsLeadingUnscopedMultilineFlag()
    {
        byte[][] patterns = ["(?m)^Scout.*$"u8.ToArray()];

        long matches = LiteralLineSearcher.CountMatches(
            "Scout one\nnot Scout\nScout two\n"u8,
            patterns);

        Assert.Equal(2, matches);
    }

    /// <summary>
    /// Verifies zero-width empty-line matches are assigned to the record that begins at the
    /// match instead of the record whose terminator precedes it.
    /// </summary>
    /// <param name="pattern">The empty-line expression.</param>
    /// <param name="haystack">The records to search.</param>
    /// <param name="crlf">Whether CRLF-aware matching is enabled.</param>
    /// <param name="expectedMatchedLines">The expected selected-record count.</param>
    /// <param name="expectedLineNumber">The expected last selected-record number.</param>
    /// <param name="expectedByteOffset">The expected last selected-record byte offset.</param>
    [Theory]
    [InlineData("^$", "abc\n\nx\n", false, 1, 2, 4)]
    [InlineData("(?m)^$", "abc\n\nx\n", false, 1, 2, 4)]
    [InlineData("^$", "abc\r\n\r\nx\r\n", false, 0, 0, 0)]
    [InlineData("(?m)^$", "abc\r\n\r\nx\r\n", false, 0, 0, 0)]
    [InlineData("^$", "abc\r\n\r\nx\r\n", true, 1, 2, 5)]
    [InlineData("(?m)^$", "abc\r\n\r\nx\r\n", true, 1, 2, 5)]
    public void SearchAssignsEmptyLineAnchorMatchesToTheirRecords(
        string pattern,
        string haystack,
        bool crlf,
        ulong expectedMatchedLines,
        long expectedLineNumber,
        long expectedByteOffset)
    {
        byte[][] patterns = [Encoding.ASCII.GetBytes(pattern)];
        byte[] bytes = Encoding.ASCII.GetBytes(haystack);
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: crlf));
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            bytes,
            patterns,
            plan,
            ref sink,
            crlf: crlf,
            requireMatchColumn: true);
        long countedLines = LiteralLineSearcher.CountMatchingLinesWithRegexPlan(
            bytes,
            patterns,
            plan,
            crlf: crlf);

        Assert.Equal(expectedMatchedLines > 0, matched);
        Assert.Equal(expectedMatchedLines, sink.MatchedLines);
        Assert.Equal((long)expectedMatchedLines, countedLines);
        if (expectedMatchedLines > 0)
        {
            Assert.Equal(expectedLineNumber, sink.LineNumber);
            Assert.Equal(expectedByteOffset, sink.ByteOffset);
            Assert.Equal(1, sink.MatchColumn);
        }
    }

    /// <summary>
    /// Verifies a possible line candidate cannot select a record without an authoritative match.
    /// </summary>
    [Fact]
    public void PossibleLineCandidateRequiresAuthoritativeVerification()
    {
        byte[][] patterns = ["^$"u8.ToArray()];
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: true,
                preserveCrlfCarriageReturn: true));
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            "abc\r\nx\r\n"u8,
            patterns,
            plan,
            ref sink,
            crlf: true,
            requireMatchColumn: true);

        Assert.False(matched);
        Assert.Equal(0UL, sink.MatchedLines);
    }

    /// <summary>
    /// Verifies a zero-width match at the end of an unterminated final record selects that record
    /// without exposing a synthetic match column.
    /// </summary>
    [Fact]
    public void EndAnchorAtHaystackEndSelectsUnterminatedFinalRecord()
    {
        byte[][] patterns = ["$"u8.ToArray()];
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            "abc\nx"u8,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: true);

        Assert.True(matched);
        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(4, sink.ByteOffset);
        Assert.Equal(0, sink.MatchColumn);
    }

    /// <summary>
    /// Verifies the authoritative matcher handles the three general regex shapes from issue 37.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="haystack">The records to search.</param>
    /// <param name="expected">The expected non-overlapping match count.</param>
    [Theory]
    [InlineData(@"\bGeneratedRecord\b", "GeneratedRecords\nGeneratedRecord\nx GeneratedRecord y\n", 2)]
    [InlineData(@"^internal sealed class GeneratedRecord\r?$", "public class Other\r\ninternal sealed class GeneratedRecord\r\n", 1)]
    public void CountMatchesUsesAuthoritativeGeneralRegexPlan(
        string pattern,
        string haystack,
        long expected)
    {
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];

        long matches = LiteralLineSearcher.CountMatches(
            Encoding.UTF8.GetBytes(haystack),
            patterns);

        Assert.Equal(expected, matches);
    }

    /// <summary>
    /// Verifies whole-record regex plans can group delayed match ends while retaining the guards
    /// for NUL records, multiline matching, preserved CRLF bytes, inversion, and absolute anchors.
    /// </summary>
    [Fact]
    public void LineRegexpUsesIndependentRecordProjectionOnlyWhenSemanticallySafe()
    {
        byte[][] patterns = [@"\b\w{5}\s+\w{5}\s+\w{5}\b"u8.ToArray()];
        byte[] haystack = "alpha bravo charl\n\nalpha bravo charl extra\nalpha bravo charl"u8.ToArray();
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                lineRegexp: true));

        Assert.NotNull(plan);
        Assert.True(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            plan,
            invertMatch: false,
            requireMatchColumn: false));
        using (RegexMatchEndRunner runner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0))
        {
            Assert.True(runner.IsAvailable);
            Assert.True(runner.UsesAsciiProjection);
        }

        Assert.Equal(2, LiteralLineSearcher.CountMatchingLines(
            haystack,
            patterns,
            lineRegexp: true));
        Assert.Equal(1, LiteralLineSearcher.CountMatchingLines(
            haystack,
            patterns,
            lineRegexp: true,
            maxMatchingLines: 1));
        Assert.Equal(2, LiteralLineSearcher.CountMatchingLines(
            haystack,
            patterns,
            invertMatch: true,
            lineRegexp: true));

        RegexSearchPlan nulPlan = Assert.IsType<RegexSearchPlan>(RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                lineRegexp: true,
                nullData: true)));
        Assert.False(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            nulPlan,
            invertMatch: false,
            requireMatchColumn: false));

        RegexSearchPlan multilinePlan = Assert.IsType<RegexSearchPlan>(RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                lineRegexp: true,
                multiline: true)));
        Assert.False(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            multilinePlan,
            invertMatch: false,
            requireMatchColumn: false));

        RegexSearchPlan preservedCrlfPlan = Assert.IsType<RegexSearchPlan>(RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                lineRegexp: true,
                crlf: true,
                preserveCrlfCarriageReturn: true)));
        Assert.False(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            preservedCrlfPlan,
            invertMatch: false,
            requireMatchColumn: false));

        RegexSearchPlan absolutePlan = Assert.IsType<RegexSearchPlan>(RegexSearchPlan.Create(
            [@"\Aalpha bravo charl"u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                lineRegexp: true)));
        Assert.False(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            absolutePlan,
            invertMatch: false,
            requireMatchColumn: false));
    }

    /// <summary>
    /// Verifies an anchored bounded class is evaluated once by the authoritative matcher.
    /// </summary>
    [Fact]
    public void CountMatchesUsesAuthoritativeBoundedClassPlan()
    {
        byte[][] patterns = ["^[A-Za-z_]{70,90}$"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes($"short\n{new string('A', 80)}\n{new string('A', 91)}\n");

        long matches = LiteralLineSearcher.CountMatches(haystack, patterns);

        Assert.Equal(1, matches);
    }

    /// <summary>
    /// Verifies syntax-derived minimum byte lengths reject short records without hiding a later
    /// Unicode-class match under LF, CRLF, or NUL record semantics.
    /// </summary>
    [Fact]
    public void MinimumMatchLengthPreservesRecordTerminatorSemantics()
    {
        byte[][] patterns = [@"\w{91}\s+\w{91}\s+\w{91}"u8.ToArray()];
        byte[] shortRecord = Encoding.UTF8.GetBytes(
            "абвгд ежзий клмно прсту фхцчш щъыьэ");
        string word = new('a', 91);
        byte[] matchingRecord = Encoding.ASCII.GetBytes($"{word} {word} {word}");
        var cases = new (byte[] Terminator, bool Crlf, bool NullData)[]
        {
            ([ (byte)'\n' ], false, false),
            ([ (byte)'\r', (byte)'\n' ], true, false),
            ([ (byte)0 ], false, true),
        };

        foreach ((byte[] terminator, bool crlf, bool nullData) in cases)
        {
            byte[] shortOnly = [.. shortRecord, .. terminator];
            byte[] mixed = [.. shortRecord, .. terminator, .. matchingRecord, .. terminator];
            var plan = RegexSearchPlan.Create(
                patterns,
                new RegexSearchPlanOptions(
                    asciiCaseInsensitive: false,
                    lineRegexp: false,
                    wordRegexp: false,
                    crlf,
                    nullData));
            Assert.NotNull(plan);
            Assert.Equal(275, plan.MinimumMatchLength);

            Assert.False(LiteralLineSearcher.HasMatch(
                shortOnly,
                patterns,
                crlf: crlf,
                nullData: nullData));
            Assert.Equal(0, LiteralLineSearcher.CountMatches(
                shortOnly,
                patterns,
                maxMatchingLines: 1,
                crlf: crlf,
                nullData: nullData));
            Assert.True(LiteralLineSearcher.HasMatch(
                mixed,
                patterns,
                crlf: crlf,
                nullData: nullData));
            Assert.Equal(1, LiteralLineSearcher.CountMatchingLines(
                mixed,
                patterns,
                crlf: crlf,
                nullData: nullData));
            Assert.Equal(1, LiteralLineSearcher.CountMatches(
                mixed,
                patterns,
                maxMatchingLines: 1,
                crlf: crlf,
                nullData: nullData));

            var sink = new CapturingMatchSink();
            Assert.True(LiteralLineSearcher.SearchMatches(
                mixed,
                patterns,
                ref sink,
                maxMatchingLines: 1,
                crlf: crlf,
                nullData: nullData));
            Assert.Equal(1UL, sink.Matches);
        }
    }

    /// <summary>
    /// Verifies complete-line segments can reuse general non-empty regex plans while observing NUL bytes.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="haystack">The complete line segment to search.</param>
    [Theory]
    [InlineData(@"\bGeneratedRecord\b", "internal sealed class GeneratedRecord\r\n")]
    [InlineData(@"\b\w{5}\s+\w{5}\s+\w{5}\b", "alpha bravo charl\r\n")]
    [InlineData(@"^internal sealed class GeneratedRecord\r?$", "internal sealed class GeneratedRecord\r\n")]
    [InlineData(@"^[A-Za-z_]{70,90}\r?$", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\r\n")]
    public void CountsIndependentCompleteLineSegmentWithGeneralRegexPlan(
        string pattern,
        string haystack)
    {
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        var plan = RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);

        bool counted = LiteralLineSearcher.TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
            Encoding.UTF8.GetBytes(haystack),
            patterns,
            plan,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            out long count,
            out bool containsNul);

        Assert.True(counted);
        Assert.Equal(1, count);
        Assert.False(containsNul);
    }

    /// <summary>
    /// Verifies regex plans whose matches depend on artificial segment boundaries are not counted independently.
    /// </summary>
    /// <param name="pattern">The boundary-dependent regex pattern.</param>
    [Theory]
    [InlineData("a*")]
    [InlineData(@"\Afoo")]
    public void IndependentCompleteLineSegmentRejectsBoundaryDependentRegexPlan(string pattern)
    {
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        var plan = RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);

        bool counted = LiteralLineSearcher.TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
            "foo\n"u8,
            patterns,
            plan,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            out _,
            out _);

        Assert.False(counted);
    }

    /// <summary>
    /// Verifies absolute anchors retain ripgrep's ordinary per-record semantics.
    /// </summary>
    [Fact]
    public void CountMatchesTreatsAbsoluteAnchorsAsRecordAnchorsInOrdinarySearch()
    {
        byte[][] patterns = [@"\Afoo\z"u8.ToArray()];

        long matches = LiteralLineSearcher.CountMatches(
            "foo\nbar\nfoo\n"u8,
            patterns);

        Assert.Equal(2, matches);
    }

    /// <summary>
    /// Verifies plain regex patterns use the literal search path through the multi-pattern API.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathForPlainPatterns()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["Sherlock Holmes"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "alpha\nSherlock Holmes\nomega\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal("Sherlock Holmes\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies the literal regex path reports each matching line once.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathOncePerLine()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["needle"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "needle needle\nhay\nneedle\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(3, sink.LineNumber);
        Assert.Equal("needle\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies the literal regex path keeps exact line metadata after skipping many nonmatching lines.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathCountsSkippedLines()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["needle"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "alpha\nbeta\ngamma\nneedle here\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(4, sink.LineNumber);
        Assert.Equal(17, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("needle here\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies the literal regex path keeps line metadata with NUL-terminated records.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathCountsSkippedNullDataLines()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["needle"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "alpha\0beta\0needle here\0"u8,
            patterns,
            ref sink,
            nullData: true);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(3, sink.LineNumber);
        Assert.Equal(11, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("needle here\0"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies the authoritative plan searches pure literal alternations.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanHandlesLiteralAlternation()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["alpha|Sherlock Holmes|omega"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "start\nSherlock Holmes here\nnone\nomega\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(4, sink.LineNumber);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("omega\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies authoritative line counting applies max-count to matching lines rather than matches.
    /// </summary>
    [Fact]
    public void CountMatchingLinesAuthoritativePlanHonorsLiteralAlternationLimit()
    {
        byte[][] patterns = ["foo|bar"u8.ToArray()];

        long count = LiteralLineSearcher.CountMatchingLines(
            "foo foo\nnone\nbar bar\nfoo\n"u8,
            patterns,
            maxMatchingLines: 2);

        Assert.Equal(2, count);
    }

    /// <summary>
    /// Verifies authoritative match counting preserves leftmost-first alternation priority.
    /// </summary>
    [Fact]
    public void CountMatchesAuthoritativePlanPreservesLiteralAlternationPriority()
    {
        byte[][] patterns = ["foo|foobar|bar"u8.ToArray()];

        long count = LiteralLineSearcher.CountMatches("foobar foo bar\n"u8, patterns);

        Assert.Equal(4, count);
    }

    /// <summary>
    /// Verifies multi-pattern match-line search reports literal regex match offsets late in a line.
    /// </summary>
    [Fact]
    public void SearchMatchLinesReportsLiteralRegexOffsets()
    {
        var sink = new CapturingMatchLineSink();
        AssertMatchLineReportsLiteralRegexOffset(["class"u8.ToArray()], ref sink);
    }

    /// <summary>
    /// Verifies multi-pattern match-line search reports global match offsets across multiple lines.
    /// </summary>
    [Fact]
    public void SearchMatchLinesReportsLiteralRegexOffsetsAcrossLines()
    {
        var sink = new CapturingMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLines(
            "public sealed class Pcre2Regex\n    /// Initializes a new instance of the <see cref=\"Pcre2Regex\" /> class.\n"u8,
            ["class"u8.ToArray()],
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.Matches);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(31, sink.LineByteOffset);
        Assert.Equal(99, sink.MatchByteOffset);
        Assert.Equal(69, sink.MatchColumn);
        Assert.Equal("class"u8.ToArray(), sink.Match.ToArray());
    }

    /// <summary>
    /// Verifies scoped group match-line search reports inner match offsets.
    /// </summary>
    [Fact]
    public void SearchMatchLinesReportsScopedRegexInnerOffsets()
    {
        var sink = new CapturingMatchLineSink();

        AssertMatchLineReportsLiteralRegexOffset(["(?:class)"u8.ToArray()], ref sink);
        sink = new CapturingMatchLineSink();
        AssertMatchLineReportsLiteralRegexOffset(["(?-u:class)"u8.ToArray()], ref sink);
    }

    private static void AssertMatchLineReportsLiteralRegexOffset(byte[][] patterns, ref CapturingMatchLineSink sink)
    {
        bool matched = LiteralLineSearcher.SearchMatchLines(
            "    /// Initializes a new instance of the <see cref=\"Pcre2Regex\" /> class.\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.Matches);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(68, sink.MatchByteOffset);
        Assert.Equal(69, sink.MatchColumn);
        Assert.Equal("class"u8.ToArray(), sink.Match.ToArray());
    }

    /// <summary>
    /// Verifies match search preserves scoped ungreedy repetition spans.
    /// </summary>
    [Fact]
    public void SearchMatchesHonorsScopedUngreedyRegexSpans()
    {
        var sink = new CapturingMatchSink();
        byte[][] patterns = [@"(?U:ab+)"u8.ToArray()];

        bool matched = LiteralLineSearcher.SearchMatches(
            "abbbbc\nab\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.Matches);
        Assert.Equal(0, sink.FirstByteOffset);
        Assert.Equal("ab"u8.ToArray(), sink.FirstMatch);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(7, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("ab"u8.ToArray(), sink.Match);
    }

    /// <summary>
    /// Verifies authoritative class-sequence matching reports line metadata for ASCII matches.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanMatchesAsciiClassSequence()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "tiny\nabcde fghij klmno\nomega\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(5, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("abcde fghij klmno\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies a conservative prefilter preserves line output for a leading alternation.
    /// </summary>
    [Fact]
    public void SearchPrefilteredAuthoritativePlanMatchesLeadingAlternation()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = [@"\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "destructor file;\nstruct file;\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(17, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("struct file;\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies plan-owning search APIs reject a missing plan instead of compiling one implicitly.
    /// </summary>
    [Fact]
    public void WithRegexPlanApiRequiresPlan()
    {
        byte[][] patterns = ["needle"u8.ToArray()];

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            LiteralLineSearcher.CountMatchesWithRegexPlan(
                "needle\n"u8,
                patterns,
                regexPlan: null!));

        Assert.Equal("regexPlan", exception.ParamName);
    }

    /// <summary>
    /// Verifies plan-owning search APIs reject incompatible semantics while convenience APIs
    /// compile the requested semantics explicitly.
    /// </summary>
    [Fact]
    public void WithRegexPlanApiRejectsIncompatiblePlan()
    {
        byte[][] patterns = ["needle"u8.ToArray()];
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            LiteralLineSearcher.CountMatchesWithRegexPlan(
                "NEEDLE\n"u8,
                patterns,
                plan,
                asciiCaseInsensitive: true,
                maxMatchingLines: 0));

        Assert.Equal("regexPlan", exception.ParamName);
        Assert.Equal(1, LiteralLineSearcher.CountMatches(
            "NEEDLE\n"u8,
            patterns,
            asciiCaseInsensitive: true));
    }

    /// <summary>
    /// Verifies Scout's prepared no-Unicode wrapper keeps one authoritative matcher with an optional prefilter.
    /// </summary>
    [Fact]
    public void RegexSearchPlanUsesAuthoritativeMatcherForPreparedLeadingAlternation()
    {
        byte[][] patterns = [@"(?-u:\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*)"u8.ToArray()];
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        Assert.NotNull(plan);
        Assert.NotEqual(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
    }

    /// <summary>
    /// Verifies the CLI's neutral regex wrapper retains the authoritative matcher in general mode.
    /// </summary>
    [Fact]
    public void RegexSearchPlanUsesAuthoritativeMatcherForCliWrappedLeadingAlternationInGeneralMode()
    {
        using RegexSpecializationModeScope scope = RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);
        byte[][] patterns = [@"(?:\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*)"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        Assert.NotNull(plan);
        Assert.NotEqual(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
    }

    /// <summary>
    /// Verifies Scout's prepared capture wrapper keeps capture semantics in the authoritative matcher.
    /// </summary>
    [Fact]
    public void RegexSearchPlanUsesAuthoritativeMatcherForPreparedCaptureLeadingAlternation()
    {
        byte[][] patterns = [@"(?-u:\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*))"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        Assert.NotNull(plan);
        Assert.Equal(2, plan.CaptureCount);
    }

    /// <summary>
    /// Verifies scoped flags that change match spans still use automaton spans.
    /// </summary>
    [Fact]
    public void RegexSearchPlanKeepsScopedUngreedySpansOnAutomatonPath()
    {
        byte[][] patterns = [@"(?U:ab+)"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

        Assert.NotNull(plan);
        RegexMatch? match = plan.Matcher.Find("abbbb"u8);
        Assert.Equal(new RegexMatch(0, 2), match);
    }

    /// <summary>
    /// Verifies conservative candidate discovery preserves line-oriented matching.
    /// </summary>
    [Fact]
    public void SearchPrefilteredAuthoritativePlanDoesNotMatchAcrossLines()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = [@"\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "struct\nfile\n"u8,
            patterns,
            ref sink);

        Assert.False(matched);
        Assert.Equal(0UL, sink.MatchedLines);
    }

    /// <summary>
    /// Verifies authoritative verification continues after a false candidate prefix.
    /// </summary>
    [Fact]
    public void SearchPrefilteredAuthoritativePlanContinuesAfterFalseCandidate()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = [@"\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "destructor struct file;\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(0, sink.ByteOffset);
        Assert.Equal(12, sink.MatchColumn);
        Assert.Equal("destructor struct file;\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies dot-star alternatives with one shared literal prefix use one authoritative matcher and conservative candidates.
    /// </summary>
    [Fact]
    public void SearchSharedDelegatePrefixAlternationUsesAuthoritativePlan()
    {
        const string pattern =
            "delegate .*ShowMessageBoxHandler|delegate .*UpdateEDIEvent|" +
            "delegate .*SetProgressBarValue|delegate .*ShowCheckboxMessageBoxHandler";
        const string unrelatedLine =
            "internal sealed class TransactionRecord { private readonly int _state; }\n";
        const int unrelatedLineCount = 1_980;
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        byte[] haystack = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat(unrelatedLine, unrelatedLineCount)) +
            "    public delegate bool ShowMessageBoxHandler(string message, string caption, bool buttons);\n" +
            "    public delegate bool ShowCheckboxMessageBoxHandler(string message, string caption, bool buttons);\n" +
            "    public delegate void SetProgressBarValue(int percentComplete, int currentValue);\n" +
            "    public delegate void UpdateEDIEvent(string eventString);\n");
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink);
        LiteralLineSearcher.CountMatchesAndMatchingLinesWithRegexPlan(
            haystack,
            patterns,
            plan,
            asciiCaseInsensitive: false,
            lineRegexp: false,
            wordRegexp: false,
            maxMatchingLines: null,
            crlf: false,
            nullData: false,
            out long matchingLines,
            out long matches);

        Assert.True(matched);
        Assert.Equal(4UL, sink.Matches);
        Assert.Equal(unrelatedLineCount + 4, sink.LineNumber);
        Assert.Equal(12, sink.MatchColumn);
        Assert.Equal("delegate void UpdateEDIEvent"u8.ToArray(), sink.Match);
        Assert.Equal(4, matchingLines);
        Assert.Equal(4, matches);
        Assert.NotNull(plan);
        Assert.NotEqual(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
    }

    /// <summary>
    /// Verifies authoritative match-line traversal retains an unterminated record selected only by its end.
    /// </summary>
    [Fact]
    public void SearchMatchLinesRetainsEndEmptySelectionWithoutReportingSpan()
    {
        byte[][] patterns = ["$"u8.ToArray()];
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingSelectionMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            "value"u8,
            patterns,
            plan,
            ref sink);

        Assert.True(matched);
        Assert.Equal(0UL, sink.Matches);
        Assert.Equal(1UL, sink.SelectedLines);
        Assert.Equal(1, sink.LastSelectedLineNumber);
    }

    /// <summary>
    /// Verifies absolute-start anchors select every record while retaining only spans valid in the original prefix.
    /// </summary>
    [Fact]
    public void SearchMatchLinesRetainsAbsoluteStartSelectionWithoutSyntheticSpans()
    {
        byte[][] patterns = [@"\A"u8.ToArray()];
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingSelectionMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            "a\nb\n"u8,
            patterns,
            plan,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.Matches);
        Assert.Equal(2UL, sink.SelectedLines);
        Assert.Equal(1, sink.LastMatchedLineNumber);
        Assert.Equal(2, sink.LastSelectedLineNumber);
    }

    /// <summary>
    /// Verifies occurrence counting includes a physical-EOF match that output replay treats as selection-only.
    /// </summary>
    /// <param name="pattern">The end-asserted expression.</param>
    [Theory]
    [InlineData("$")]
    [InlineData(@"\z")]
    public void CountMatchesIncludesSelectionOnlyPhysicalEnd(string pattern)
    {
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

        long count = LiteralLineSearcher.CountMatchesWithRegexPlan(
            "a\nb"u8,
            patterns,
            plan);
        long singleRecordCount = LiteralLineSearcher.CountMatchesWithRegexPlan(
            "abc"u8,
            patterns,
            plan);
        LiteralLineSearcher.CountMatchesAndMatchingLinesWithRegexPlan(
            "a\nb"u8,
            patterns,
            plan,
            asciiCaseInsensitive: false,
            lineRegexp: false,
            wordRegexp: false,
            maxMatchingLines: null,
            crlf: false,
            nullData: false,
            out long matchingLines,
            out long reportableMatches);

        Assert.Equal(2, count);
        Assert.Equal(1, singleRecordCount);
        Assert.Equal(2, matchingLines);
        Assert.Equal(1, reportableMatches);
    }

    /// <summary>
    /// Verifies occurrence counting retains the reportable match count when physical EOF also
    /// selects the final record.
    /// </summary>
    /// <param name="pattern">The expression to count.</param>
    /// <param name="contents">The complete input contents.</param>
    /// <param name="expected">The expected occurrence count.</param>
    [Theory]
    [InlineData(@"\A|\z", "a", 1)]
    [InlineData(@"(?:\A)?", "a", 1)]
    [InlineData(@"(?:\z)?", "a", 1)]
    [InlineData("(?:)*", "a", 1)]
    [InlineData(@"\A|\z", "a\nb", 3)]
    public void CountMatchesRetainsReportableCountAtPhysicalEnd(
        string pattern,
        string contents,
        long expected)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(contents);
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

        long count = LiteralLineSearcher.CountMatchesWithRegexPlan(
            Encoding.UTF8.GetBytes(contents),
            patterns,
            plan);

        Assert.Equal(expected, count);
    }

    /// <summary>
    /// Verifies authoritative match-line traversal does not create a record after a trailing terminator.
    /// </summary>
    [Fact]
    public void SearchMatchLinesDoesNotReportPostTerminatorEmptyMatch()
    {
        byte[][] patterns = ["^"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            "one\ntwo\n"u8,
            patterns,
            plan,
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.Matches);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(4, sink.MatchByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Empty(sink.Match);
    }

    /// <summary>
    /// Verifies one-pass statistics counting agrees with the independent match and line counters.
    /// </summary>
    [Theory]
    [InlineData("foo|bar.", "foo bar1\nnone\nbar2 foo\n", false, false, false, false)]
    [InlineData(@"\bfoo\b|bar", "food foo\nbar\n", false, false, false, false)]
    [InlineData("foo|bar", "foo\r\nbar\r\n", false, false, true, false)]
    [InlineData("foo|bar", "foo\0bar\0", false, false, false, true)]
    [InlineData("foo.*", "foo\nfoobar\nnone\n", true, false, false, false)]
    public void CountMatchesAndMatchingLinesAgreesWithIndependentCounters(
        string pattern,
        string haystack,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        byte[] bytes = Encoding.UTF8.GetBytes(haystack);
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData));

        LiteralLineSearcher.CountMatchesAndMatchingLinesWithRegexPlan(
            bytes,
            patterns,
            plan,
            asciiCaseInsensitive: false,
            lineRegexp,
            wordRegexp,
            maxMatchingLines: null,
            crlf,
            nullData,
            out long matchingLines,
            out long matches);

        Assert.Equal(
            LiteralLineSearcher.CountMatchingLines(
                bytes,
                patterns,
                asciiCaseInsensitive: false,
                invertMatch: false,
                lineRegexp,
                wordRegexp,
                maxMatchingLines: null,
                crlf,
                nullData),
            matchingLines);
        Assert.Equal(
            LiteralLineSearcher.CountMatches(
                bytes,
                patterns,
                asciiCaseInsensitive: false,
                invertMatch: false,
                lineRegexp,
                wordRegexp,
                maxMatchingLines: null,
                crlf,
                nullData),
            matches);
    }

    /// <summary>
    /// Verifies combined statistics exclude a line terminator from authoritative regex matching.
    /// </summary>
    [Fact]
    public void CountMatchesAndMatchingLinesExcludesLineTerminator()
    {
        byte[][] patterns = ["foo[^x]"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

        LiteralLineSearcher.CountMatchesAndMatchingLinesWithRegexPlan(
            "foo\n"u8,
            patterns,
            plan,
            asciiCaseInsensitive: false,
            lineRegexp: false,
            wordRegexp: false,
            maxMatchingLines: null,
            crlf: false,
            nullData: false,
            out long matchingLines,
            out long matches);

        Assert.Equal(0, matchingLines);
        Assert.Equal(0, matches);
    }

    /// <summary>
    /// Verifies forward match-end iteration emits each selected line once while counting every
    /// non-overlapping match on the last line admitted by the match-line limit.
    /// </summary>
    [Fact]
    public void MatchEndIterationCountsEveryMatchOnLimitedLine()
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "\nabcde fghij klmno pqrst uvwxy zabcd\nzzzzz yyyyy xxxxx\n");
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingLineSink();

        Assert.NotNull(plan);
        Assert.True(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            plan,
            invertMatch: false,
            requireMatchColumn: false));
        RegexMatchEndRunner runner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0);
        Assert.True(runner.IsAvailable);
        Assert.True(runner.UsesAsciiProjection);
        runner.Dispose();

        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            maxMatchingLines: 1,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, matchedLines);
        Assert.Equal(2, matches);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(4_097, sink.ByteOffset);
        Assert.Equal(0, sink.MatchColumn);
        Assert.Equal(
            "abcde fghij klmno pqrst uvwxy zabcd\n"u8.ToArray(),
            sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies forward match-end line grouping preserves CRLF and NUL record boundaries.
    /// </summary>
    /// <param name="terminator">The record terminator text.</param>
    /// <param name="crlf">Whether CRLF-aware matching is enabled.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    [Theory]
    [InlineData("\r\n", true, false)]
    [InlineData("\0", false, true)]
    public void MatchEndIterationPreservesRecordTerminators(
        string terminator,
        bool crlf,
        bool nullData)
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        string selectedRecord = "abcde fghij klmno pqrst uvwxy zabcd";
        byte[] haystack = Encoding.UTF8.GetBytes(
            new string('!', 4_096) + terminator + selectedRecord + terminator +
            "zzzzz yyyyy xxxxx" + terminator);
        var options = new RegexSearchPlanOptions(
            asciiCaseInsensitive: false,
            crlf: crlf,
            nullData: nullData);
        var plan = RegexSearchPlan.Create(patterns, options);
        var sink = new CapturingLineSink();

        Assert.NotNull(plan);
        RegexMatchEndRunner runner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0);
        Assert.True(runner.IsAvailable);
        Assert.True(runner.UsesAsciiProjection);
        runner.Dispose();

        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            maxMatchingLines: 1,
            crlf: crlf,
            nullData: nullData,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, matchedLines);
        Assert.Equal(2, matches);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(4_096 + Encoding.UTF8.GetByteCount(terminator), sink.ByteOffset);
        Assert.Equal(
            Encoding.UTF8.GetBytes(selectedRecord + terminator),
            sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies plain line selection and searched-line accounting retain one forward runner
    /// across a complete haystack larger than the runner activation threshold.
    /// </summary>
    [Fact]
    public void MatchEndIterationSupportsPlainSearchAndSearchedLineCounting()
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "\nabcde fghij klmno\npqrst uvwxy zabcd\n");
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var searchSink = new CapturingLineSink();

        Assert.NotNull(plan);
        RegexMatchEndRunner runner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0);
        Assert.True(runner.IsAvailable);
        Assert.True(runner.UsesAsciiProjection);
        runner.Dispose();

        bool searched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref searchSink,
            requireMatchColumn: false);

        Assert.True(searched);
        Assert.Equal(2UL, searchSink.MatchedLines);
        Assert.Equal(3, searchSink.LineNumber);
        Assert.Equal("pqrst uvwxy zabcd\n"u8.ToArray(), searchSink.Line.ToArray());

        var countingSink = new CapturingLineSink();
        bool counted = LiteralLineSearcher.SearchWithRegexPlanAndCountLines(
            haystack,
            patterns,
            plan,
            ref countingSink,
            out long searchedLines,
            requireMatchColumn: false);

        Assert.True(counted);
        Assert.Equal(3, searchedLines);
        Assert.Equal(2UL, countingSink.MatchedLines);
        Assert.Equal(3, countingSink.LineNumber);
        Assert.Equal("pqrst uvwxy zabcd\n"u8.ToArray(), countingSink.Line.ToArray());
    }

    /// <summary>
    /// Verifies a large general no-prefilter plan uses its ASCII projection while preserving
    /// selected-line and match statistics on a haystack larger than the activation threshold.
    /// </summary>
    [Fact]
    public void LargeGeneralPlanUsesAsciiProjectionForLineStatistics()
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "\nabcde fghij klmno pqrst uvwxy zabcd\nzzzzz yyyyy xxxxx\n");
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingLineSink();

        Assert.NotNull(plan);
        Assert.True(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            plan,
            invertMatch: false,
            requireMatchColumn: false));
        RegexMatchEndRunner runner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0);
        Assert.True(runner.IsAvailable);
        Assert.True(runner.UsesAsciiProjection);
        runner.Dispose();

        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            maxMatchingLines: 1,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, matchedLines);
        Assert.Equal(2, matches);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(
            "abcde fghij klmno pqrst uvwxy zabcd\n"u8.ToArray(),
            sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies an end-only projected runner does not select a whole-segment path when reporting
    /// a match column requires the authoritative match start.
    /// </summary>
    [Fact]
    public void MatchEndOnlyRunnerKeepsColumnSearchPerRecord()
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        var source = new StringBuilder();
        for (int index = 0; index < 600; index++)
        {
            source.Append("!!!!!!!\n");
        }

        source.Append("alpha bravo charl\n");
        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingLineSink();

        Assert.NotNull(plan);
        Assert.False(plan.Matcher.CanSearchWholeHaystackWithFullMatches);
        Assert.False(HasActivatedAsciiProjection(plan.Matcher));

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            requireMatchColumn: true);

        Assert.True(matched);
        Assert.Equal(601, sink.LineNumber);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("alpha bravo charl\n"u8.ToArray(), sink.Line.ToArray());
        Assert.False(HasActivatedAsciiProjection(plan.Matcher));
    }

    /// <summary>
    /// Verifies a forward-only generic runner handles mixed records authoritatively when its
    /// ASCII projection cannot cover the complete segment.
    /// </summary>
    [Fact]
    public void ForwardOnlyGenericRunnerHandlesMixedRecords()
    {
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes(
            new string('!', 4_096) +
            "\nalpha bravo charl\nαβγδε ζηθικ λμνξο\npqrst uvwxy zabcd\n");
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingLineSink();

        Assert.NotNull(plan);
        RegexMatchEndRunner runner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0);
        Assert.True(runner.IsAvailable);
        Assert.False(runner.UsesAsciiProjection);
        runner.Dispose();
        int asciiTailStart = haystack.AsSpan().IndexOf("pqrst uvwxy zabcd"u8);
        Assert.True(asciiTailStart > 0);
        RegexMatchEndRunner tailRunner = plan.Matcher.RentMatchEndRunner(
            haystack,
            asciiTailStart);
        Assert.True(tailRunner.IsAvailable);
        Assert.False(tailRunner.UsesAsciiProjection);
        tailRunner.Dispose();

        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(3UL, matchedLines);
        Assert.Equal(3, matches);
        Assert.Equal(3UL, sink.MatchedLines);
        Assert.Equal(4, sink.LineNumber);
        Assert.Equal("pqrst uvwxy zabcd\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies concurrent operations sharing one Pike VM plan retain independent mutable
    /// runners across line selection, match counting, and match enumeration.
    /// </summary>
    [Fact]
    public void SharedPikeVmPlanUsesIndependentOperationRunners()
    {
        byte[][] patterns = [@"\b\w{5}\s+\w{5}\s+\w{5}\b"u8.ToArray()];
        var source = new StringBuilder();
        for (int index = 0; index < 64; index++)
        {
            source.Append("alpha bravo charl delta eagle foxtt\n");
        }

        byte[] haystack = Encoding.ASCII.GetBytes(source.ToString());
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

        Assert.NotNull(plan);
        Assert.Equal(RegexEngineKind.PikeVm, plan.Matcher.EngineKind);
        Parallel.For(0, 32, _ =>
        {
            var lineSink = new CapturingLineSink();
            Assert.True(LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
                haystack,
                patterns,
                plan,
                ref lineSink,
                out ulong matchedLines,
                out long matches,
                requireMatchColumn: false));
            Assert.Equal(64UL, matchedLines);
            Assert.Equal(128, matches);

            var matchSink = new CapturingMatchSink();
            Assert.True(LiteralLineSearcher.SearchMatches(
                haystack,
                patterns,
                ref matchSink));
            Assert.Equal(128UL, matchSink.Matches);

            var matchLineSink = new CapturingMatchLineSink();
            Assert.True(LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
                haystack,
                patterns,
                plan,
                ref matchLineSink));
            Assert.Equal(128UL, matchLineSink.Matches);
            Assert.Equal(128, LiteralLineSearcher.CountMatchesWithRegexPlan(
                haystack,
                patterns,
                plan));
        });
    }

    /// <summary>
    /// Verifies syntax that explicitly controls Unicode semantics cannot select an ASCII
    /// projection even when the complete search segment contains only ASCII bytes.
    /// </summary>
    [Fact]
    public void UnsafeInlineUnicodeProjectionUsesGenericRunner()
    {
        byte[][] patterns = [@"(?u:\w{5}\s+\w{5}\s+\w{5})"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes(
            new string('!', 4_096) + "\nalpha bravo charl\n");
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

        Assert.NotNull(plan);
        RegexMatchEndRunner runner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0);
        Assert.True(runner.IsAvailable);
        Assert.False(runner.UsesAsciiProjection);
        runner.Dispose();
        Assert.Equal(1, plan.Matcher.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies projected execution requires at least one coalesced four-KiB ASCII record run
    /// for LF and NUL record modes.
    /// </summary>
    /// <param name="terminator">The record terminator.</param>
    [Theory]
    [InlineData((byte)'\n')]
    [InlineData((byte)0)]
    public void ProjectedRecordRunEligibilityHonorsCoalescing(byte terminator)
    {
        byte[] longAsciiRun = CreateMixedRecordCorpus(
            asciiLength: 9_000,
            nonAsciiLength: 1_000,
            terminator);
        byte[] slightlyShorterLongAsciiRun = CreateMixedRecordCorpus(
            asciiLength: 8_999,
            nonAsciiLength: 1_001,
            terminator);
        byte[] minimumAsciiRun = CreateMixedRecordCorpus(
            asciiLength: 4_096,
            nonAsciiLength: 100,
            terminator);
        byte[] belowMinimumAsciiRun = CreateMixedRecordCorpus(
            asciiLength: 4_095,
            nonAsciiLength: 100,
            terminator);
        byte[] allAscii = Enumerable.Repeat((byte)'a', 8_192).ToArray();
        byte[] allNonAscii = Enumerable.Repeat((byte)0xFF, 8_192).ToArray();
        byte[] fragmented = CreateFragmentedMixedRecordCorpus(terminator);
        bool nullData = terminator == 0;

        Assert.True(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            longAsciiRun,
            nullData));
        Assert.True(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            slightlyShorterLongAsciiRun,
            nullData));
        Assert.True(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            minimumAsciiRun,
            nullData));
        Assert.False(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            belowMinimumAsciiRun,
            nullData));
        Assert.True(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            allAscii,
            nullData));
        Assert.False(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            allNonAscii,
            nullData));
        Assert.False(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            fragmented,
            nullData));
    }

    /// <summary>
    /// Verifies a projection-only mixed LF segment enters retained ASCII record-run search,
    /// verifies Unicode records authoritatively, and preserves the separate NUL-count contract.
    /// </summary>
    [Fact]
    public void ProjectionOnlyMixedSegmentUsesRecordRunsAcrossEndAndCountLanes()
    {
        const string sourcePattern = @"\w{5}\s+\w{5}\s+\w{5}";
        byte[][] patterns = [Encoding.ASCII.GetBytes(sourcePattern)];
        byte[] haystack = Encoding.UTF8.GetBytes(
            new string('!', 16 * 1024) +
            "\nalpha bravo charl\nαβγδε ζηθικ λμνξο\nnoise\0noise\npqrst uvwxy zabcd\n");
        RegexSearchPlan plan = CreateProjectionOnlyGeneralPlan(sourcePattern);
        var sink = new CapturingLineSink();

        Assert.True(plan.Matcher.HasAsciiProjectedMatchEndRunner);
        Assert.True(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            haystack,
            nullData: false));
        Assert.False(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            plan,
            invertMatch: false,
            requireMatchColumn: true));
        RegexMatchEndRunner ordinaryRunner = plan.Matcher.RentMatchEndRunner(haystack, startAt: 0);
        Assert.False(ordinaryRunner.IsAvailable);
        ordinaryRunner.Dispose();
        Assert.False(HasActivatedAsciiProjection(plan.Matcher));

        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(3UL, matchedLines);
        Assert.Equal(3, matches);
        Assert.Equal(5, sink.LineNumber);
        Assert.Equal("pqrst uvwxy zabcd\n"u8.ToArray(), sink.Line.ToArray());
        Assert.True(HasActivatedAsciiProjection(plan.Matcher));

        var lineSink = new CapturingLineSink();
        Assert.True(LiteralLineSearcher.SearchWithRegexPlanAndCountLines(
            haystack,
            patterns,
            plan,
            ref lineSink,
            out long searchedLines,
            requireMatchColumn: false));
        Assert.Equal(5, searchedLines);
        Assert.Equal(3UL, lineSink.MatchedLines);
        Assert.Equal(3, LiteralLineSearcher.CountMatchesWithRegexPlan(
            haystack,
            patterns,
            plan));

        RegexSearchPlan countPlan = CreateProjectionOnlyGeneralPlan(sourcePattern);
        Assert.False(LiteralLineSearcher.TryCountMatchesAndDetectNulWithRegexPlan(
            haystack,
            patterns,
            countPlan,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            maxMatchingLines: null,
            crlf: false,
            nullData: false,
            out long sharedCount,
            out bool sharedContainsNul));
        Assert.Equal(0, sharedCount);
        Assert.False(sharedContainsNul);
        Assert.True(LiteralLineSearcher.TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
            haystack,
            patterns,
            countPlan,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            out long separateCount,
            out bool separateContainsNul));
        Assert.Equal(3, separateCount);
        Assert.True(separateContainsNul);
    }

    /// <summary>
    /// Verifies a profitable all-ASCII segment uses one projected record-run search without
    /// changing line or match counts.
    /// </summary>
    [Fact]
    public void ProjectionOnlyAsciiSegmentUsesRecordRunsAcrossEndAndCountLanes()
    {
        const string sourcePattern = @"\w{5}\s+\w{5}\s+\w{5}";
        byte[][] patterns = [Encoding.ASCII.GetBytes(sourcePattern)];
        byte[] haystack = Encoding.ASCII.GetBytes(
            new string('!', 16 * 1024) +
            "\nalpha bravo charl\nnoise\npqrst uvwxy zabcd\n");
        RegexSearchPlan plan = CreateProjectionOnlyGeneralPlan(sourcePattern);
        var sink = new CapturingLineSink();

        Assert.True(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            haystack,
            nullData: false));
        Assert.True(LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            requireMatchColumn: false));

        Assert.Equal(2UL, matchedLines);
        Assert.Equal(2, matches);
        Assert.Equal(4, sink.LineNumber);
        Assert.Equal("pqrst uvwxy zabcd\n"u8.ToArray(), sink.Line.ToArray());
        Assert.Equal(2, LiteralLineSearcher.CountMatchesWithRegexPlan(
            haystack,
            patterns,
            plan));
    }

    /// <summary>
    /// Verifies the shared dense projection handles short fragmented ASCII runs while
    /// non-ASCII records remain authoritative without activating the primary DFA.
    /// </summary>
    [Fact]
    public void DenseProjectionHandlesFragmentedShortAsciiRuns()
    {
        const string sourcePattern = @"\w{5}\s+\w{5}\s+\w{5}";
        byte[][] patterns = [Encoding.ASCII.GetBytes(sourcePattern)];
        byte[] asciiRecord = "abcde fghij klmno\n"u8.ToArray();
        byte[] nonAsciiRecord = [0xFF, (byte)'\n'];
        byte[] haystack = new byte[(asciiRecord.Length + nonAsciiRecord.Length) * 256];
        int offset = 0;
        for (int index = 0; index < 256; index++)
        {
            asciiRecord.CopyTo(haystack, offset);
            offset += asciiRecord.Length;
            nonAsciiRecord.CopyTo(haystack, offset);
            offset += nonAsciiRecord.Length;
        }

        RegexSearchPlan plan = CreateProjectionOnlyGeneralPlan(sourcePattern);
        var sink = new CapturingLineSink();

        Assert.True(plan.Matcher.HasAsciiProjectedMatchEndRunner);
        Assert.False(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            haystack,
            nullData: false));
        Assert.False(HasActivatedAsciiProjection(plan.Matcher));
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.True(RegexProjectedRecordRunSearcher.TryCountMatches(
            haystack,
            plan,
            nullData: false,
            out long projectedCount));
        Assert.Equal(256, projectedCount);
        var projectedSink = new CapturingLineSink();
        Assert.True(RegexProjectedRecordRunSearcher.TrySearchLinesAndCountMatches(
            haystack,
            plan,
            ref projectedSink,
            out bool projectedMatched,
            out ulong projectedMatchedLines,
            out long projectedMatches,
            maxMatchingLines: null,
            nullData: false));
        Assert.True(projectedMatched);
        Assert.Equal(256UL, projectedMatchedLines);
        Assert.Equal(256, projectedMatches);
        Assert.Equal(256UL, projectedSink.MatchedLines);
        Assert.True(HasActivatedAsciiProjection(plan.Matcher));
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
        Assert.True(LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            requireMatchColumn: false));

        Assert.Equal(256UL, matchedLines);
        Assert.Equal(256, matches);
        Assert.Equal(511, sink.LineNumber);
        Assert.True(HasActivatedAsciiProjection(plan.Matcher));
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
        Assert.Equal(256, LiteralLineSearcher.CountMatchesWithRegexPlan(
            haystack,
            patterns,
            plan));
        Assert.True(HasActivatedAsciiProjection(plan.Matcher));
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
    }

    /// <summary>
    /// Verifies a segment without any ASCII record run stays on the compact authoritative
    /// record runner instead of activating the primary expanded DFA.
    /// </summary>
    [Fact]
    public void NonAsciiSegmentUsesCompactAuthoritativeRecordRunner()
    {
        const string sourcePattern = @"\w{5}\s+\w{5}\s+\w{5}";
        byte[][] patterns = [Encoding.ASCII.GetBytes(sourcePattern)];
        byte[] record = Encoding.UTF8.GetBytes("αβγδε ζηθικ λμνξο\n");
        byte[] haystack = new byte[record.Length * 256];
        for (int index = 0; index < 256; index++)
        {
            record.CopyTo(haystack, index * record.Length);
        }

        RegexSearchPlan plan = CreateProjectionOnlyGeneralPlan(sourcePattern);
        var sink = new CapturingLineSink();

        Assert.False(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            haystack,
            nullData: false,
            minimumRunLength: plan.Matcher.AsciiProjectedMatchEndActivationLength));
        Assert.True(RegexProjectedRecordRunSearcher.TryCountMatches(
            haystack,
            plan,
            nullData: false,
            out long count));
        Assert.Equal(256, count);
        Assert.True(RegexProjectedRecordRunSearcher.TrySearchLinesAndCountMatches(
            haystack,
            plan,
            ref sink,
            out bool matched,
            out ulong matchedLines,
            out long matches,
            maxMatchingLines: null,
            nullData: false));
        Assert.True(matched);
        Assert.Equal(256UL, matchedLines);
        Assert.Equal(256, matches);
        Assert.False(HasActivatedAsciiProjection(plan.Matcher));
        Assert.False(HasCreatedAsciiFastUnanchoredDfaPool(plan.Matcher));
        Assert.False(HasActivatedPrimaryUnanchoredDfa(plan.Matcher));
    }

    /// <summary>
    /// Verifies profitable mixed record runs preserve authoritative Unicode and invalid UTF-8
    /// semantics, global metadata, line limits, and complete counting across each record mode.
    /// </summary>
    /// <param name="terminator">The record terminator text.</param>
    /// <param name="crlf">Whether CRLF-aware matching is enabled.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    [Theory]
    [InlineData("\n", false, false)]
    [InlineData("\r\n", true, false)]
    [InlineData("\0", false, true)]
    public void ProjectedMixedRunsPreserveRecordSemanticsAndLimits(
        string terminator,
        bool crlf,
        bool nullData)
    {
        const string sourcePattern = @"(?:\w{5}\s+\w{5}\s+\w{5}|[A-Z]\w[A-Z])";
        byte[][] patterns = [Encoding.ASCII.GetBytes(sourcePattern)];
        byte[] haystack = CreateProjectedMixedSemanticsCorpus(terminator);
        RegexSearchPlan plan = CreateProjectionOnlyGeneralPlan(sourcePattern, crlf, nullData);
        var sink = new CapturingLineSink();

        Assert.True(RegexProjectedRecordRunSearcher.HasEligibleProjectedRecordRun(
            haystack,
            nullData));
        bool matched = LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref sink,
            out ulong matchedLines,
            out long matches,
            crlf: crlf,
            nullData: nullData,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(5UL, matchedLines);
        Assert.Equal(6, matches);
        Assert.Equal(9, sink.LineNumber);
        Assert.Equal(haystack.Length - 3, sink.ByteOffset);
        Assert.Equal("XcY"u8.ToArray(), sink.Line.ToArray());

        var twoLineSink = new CapturingLineSink();
        Assert.True(LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref twoLineSink,
            out ulong twoMatchedLines,
            out long twoMatches,
            maxMatchingLines: 2,
            crlf: crlf,
            nullData: nullData,
            requireMatchColumn: false));
        Assert.Equal(2UL, twoMatchedLines);
        Assert.Equal(2, twoMatches);
        Assert.Equal(4, twoLineSink.LineNumber);

        var threeLineSink = new CapturingLineSink();
        Assert.True(LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
            haystack,
            patterns,
            plan,
            ref threeLineSink,
            out ulong threeMatchedLines,
            out long threeMatches,
            maxMatchingLines: 3,
            crlf: crlf,
            nullData: nullData,
            requireMatchColumn: false));
        Assert.Equal(3UL, threeMatchedLines);
        Assert.Equal(4, threeMatches);
        Assert.Equal(5, threeLineSink.LineNumber);

        var lineSink = new CapturingLineSink();
        Assert.True(LiteralLineSearcher.SearchWithRegexPlanAndCountLines(
            haystack,
            patterns,
            plan,
            ref lineSink,
            out long searchedLines,
            crlf: crlf,
            nullData: nullData,
            requireMatchColumn: false));
        Assert.Equal(9, searchedLines);
        Assert.Equal(6, LiteralLineSearcher.CountMatchesWithRegexPlan(
            haystack,
            patterns,
            plan,
            crlf: crlf,
            nullData: nullData));

        RegexSearchPlan countPlan = CreateProjectionOnlyGeneralPlan(
            sourcePattern,
            crlf,
            nullData);
        Assert.True(LiteralLineSearcher.TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
            haystack,
            patterns,
            countPlan,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: crlf,
            nullData: nullData,
            out long separateCount,
            out bool containsNul));
        Assert.Equal(6, separateCount);
        Assert.Equal(haystack.Contains((byte)0), containsNul);
    }

    /// <summary>
    /// Verifies multiline expressions that may cross records cannot map a match from only its end.
    /// </summary>
    [Fact]
    public void MatchEndIterationDeclinesCrossRecordMultilineMatches()
    {
        byte[][] patterns = ["alpha.*omega"u8.ToArray()];
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                multiline: true,
                multilineDotall: true));

        Assert.NotNull(plan);
        Assert.Equal(new RegexMatch(0, 11), plan.Matcher.Find("alpha\nomega"u8));
        Assert.False(LiteralLineSearcher.CanGroupAuthoritativeMatchesByEnd(
            plan,
            invertMatch: false,
            requireMatchColumn: false));
    }

    /// <summary>
    /// Verifies match-line candidate scanning excludes the line terminator from match content.
    /// </summary>
    [Fact]
    public void SearchMatchLinesExcludesLineTerminatorFromCandidateMatches()
    {
        var sink = new CapturingMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLines(
            "foo\n"u8,
            ["foo[^x]"u8.ToArray()],
            ref sink);

        Assert.False(matched);
        Assert.Equal(0UL, sink.Matches);
    }

    /// <summary>
    /// Verifies authoritative matching emits match-line records for prepared captures.
    /// </summary>
    [Fact]
    public void SearchMatchLinesUsesAuthoritativePlanForPreparedCaptures()
    {
        var sink = new CapturingMatchLineSink();
        byte[][] patterns = [@"(?-u:\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*))"u8.ToArray()];

        bool matched = LiteralLineSearcher.SearchMatchLines(
            "struct file; enum mode;\nstruct\nfile\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.Matches);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(13, sink.MatchByteOffset);
        Assert.Equal(14, sink.MatchColumn);
        Assert.Equal("enum mode"u8.ToArray(), sink.Match);
    }

    /// <summary>
    /// Verifies authoritative line search can skip exact match-column work when the sink does not need it.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanCanSkipMatchColumn()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = [@"\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "destructor struct file;\n"u8,
            patterns,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(0, sink.ByteOffset);
        Assert.Equal(0, sink.MatchColumn);
        Assert.Equal("destructor struct file;\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies authoritative class-sequence matching is Unicode-aware.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanMatchesUnicodeWords()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("tiny\nabcde caf\u00e9x klmno\nomega\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(5, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal(Encoding.UTF8.GetBytes("abcde caf\u00e9x klmno\n"), sink.Line);
    }

    /// <summary>
    /// Verifies authoritative class-sequence matching recognizes Unicode whitespace separators.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanMatchesUnicodeWhitespace()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("tiny\nabcde\u00a0fghij klmno\nomega\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(5, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal(Encoding.UTF8.GetBytes("abcde\u00a0fghij klmno\n"), sink.Line);
    }

    /// <summary>
    /// Verifies authoritative whole-haystack search can omit the column when only line selection is needed.
    /// </summary>
    [Fact]
    public void SearchAuthoritativeMatcherCanSkipUnicodeMatchColumn()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("\u00e9\u00e9\u00e9\u00e9\u00e9 fghij klmno abcde fghij klmno\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(0, sink.MatchColumn);
        Assert.Equal(haystack, sink.Line);
    }

    /// <summary>
    /// Verifies no-column authoritative matching still checks earlier Unicode-only matching lines.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanChecksEarlierUnicodeLineWhenColumnSkipped()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("\u00e9\u00e9\u00e9\u00e9\u00e9 fghij klmno\nabcde fghij klmno\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(Encoding.UTF8.GetBytes("abcde fghij klmno\n"), sink.Line);
    }

    /// <summary>
    /// Verifies authoritative Unicode matching stays on UTF-8 scalar boundaries while backtracking.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanBacktracksUnicodeScalars()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w+\\w\\s"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("caf\u00e9 \n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal(haystack, sink.Line);
    }

    /// <summary>
    /// Verifies repeated capturing groups can backtrack inside each repeated group body.
    /// </summary>
    [Fact]
    public void SearchBacktracksRepeatedCapturingGroups()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = [@"\w+\s*\([^)]*(,[^)]*){8,}\)"u8.ToArray()];
        byte[] haystack = "ApplyFlag(enabledFlags[index], enabled: true, ref caseInsensitive, ref swapGreed, ref multiLine, ref dotMatchesNewline, ref crlf, ref utf8, ref unicodeClasses);\n"u8.ToArray();
        var plan = RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);

        Assert.NotNull(plan);
        Assert.NotNull(plan.Matcher.Find(haystack));

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal(haystack, sink.Line);
    }

    /// <summary>
    /// Verifies repeated capturing groups use the regex plan when emitting match-line records.
    /// </summary>
    [Fact]
    public void SearchMatchLinesBacktracksRepeatedCapturingGroups()
    {
        var sink = new CapturingMatchLineSink();
        byte[][] patterns = [@"\w+\s*\([^)]*(,[^)]*){8,}\)"u8.ToArray()];
        byte[] haystack = "ApplyFlag(enabledFlags[index], enabled: true, ref caseInsensitive, ref swapGreed, ref multiLine, ref dotMatchesNewline, ref crlf, ref utf8, ref unicodeClasses);\n"u8.ToArray();

        bool matched = LiteralLineSearcher.SearchMatchLines(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.Matches);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(0, sink.MatchByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("ApplyFlag(enabledFlags[index], enabled: true, ref caseInsensitive, ref swapGreed, ref multiLine, ref dotMatchesNewline, ref crlf, ref utf8, ref unicodeClasses)"u8.ToArray(), sink.Match);
    }

    /// <summary>
    /// Verifies authoritative class-sequence matching honors max-count limiting.
    /// </summary>
    [Fact]
    public void SearchAuthoritativePlanHonorsMaxMatchingLines()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "abcde fghij klmno\npqrst uvwxy zabcd\n"u8,
            patterns,
            ref sink,
            maxMatchingLines: 1);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal("abcde fghij klmno\n"u8.ToArray(), sink.Line.ToArray());
    }

    private static byte[] CreateMixedRecordCorpus(
        int asciiLength,
        int nonAsciiLength,
        byte terminator)
    {
        byte[] corpus = new byte[asciiLength + nonAsciiLength];
        corpus.AsSpan(0, asciiLength).Fill((byte)'a');
        corpus[asciiLength - 1] = terminator;
        corpus.AsSpan(asciiLength, nonAsciiLength).Fill((byte)'x');
        corpus[asciiLength] = 0xFF;
        corpus[^1] = terminator;
        return corpus;
    }

    private static byte[] CreateFragmentedMixedRecordCorpus(byte terminator)
    {
        byte[] corpus = new byte[64 * 102];
        int offset = 0;
        for (int index = 0; index < 64; index++)
        {
            corpus.AsSpan(offset, 100).Fill((byte)'a');
            corpus[offset + 99] = terminator;
            corpus[offset + 100] = 0xFF;
            corpus[offset + 101] = terminator;
            offset += 102;
        }

        return corpus;
    }

    private static byte[] CreateProjectedMixedSemanticsCorpus(string terminator)
    {
        byte[] terminatorBytes = Encoding.UTF8.GetBytes(terminator);
        var corpus = new List<byte>();

        AddRecord(Enumerable.Repeat((byte)'!', 16 * 1024).ToArray());
        AddRecord([(byte)'X', 0xFF, (byte)'Y']);
        AddRecord("XaY"u8.ToArray());
        AddRecord(Encoding.UTF8.GetBytes("αβγδε ζηθικ λμνξο"));
        AddRecord("alpha bravo charl pqrst uvwxy zabcd"u8.ToArray());
        AddRecord([(byte)'X', 0xFF, (byte)'Y']);
        AddRecord(Encoding.UTF8.GetBytes("πρστυ φχψωα βγδεζ"));
        AddRecord([(byte)'X', 0xFF, (byte)'Y']);
        corpus.AddRange("XcY"u8.ToArray());
        return corpus.ToArray();

        void AddRecord(byte[] content)
        {
            corpus.AddRange(content);
            corpus.AddRange(terminatorBytes);
        }
    }

    private static RegexSearchPlan CreateProjectionOnlyGeneralPlan(
        string sourcePattern,
        bool crlf = false,
        bool nullData = false)
    {
        byte[] combinedPattern = Encoding.ASCII.GetBytes($"(?:{sourcePattern})");
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(combinedPattern);
        var compileOptions = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            crlf: crlf && !nullData,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General,
            excludeLineTerminators: true,
            excludeCrLf: crlf && !nullData,
            excludedLineTerminator: nullData ? (byte)0 : (byte)'\n');
        var matcher = RegexAutomaton.CompileParsed(
            tree,
            compileOptions,
            dfaSizeLimit: 1024 * 1024,
            compilePrefilter: false);
        var planOptions = new RegexSearchPlanOptions(
            asciiCaseInsensitive: false,
            crlf: crlf,
            nullData: nullData);
        return new RegexSearchPlan(
            matcher,
            combinedPattern,
            patternCount: 1,
            planOptions,
            captureCount: 0,
            new Dictionary<string, int>(StringComparer.Ordinal),
            hasAbsoluteAnchors: false,
            hasLineAnchors: false,
            hasHaystackAnchors: false,
            canMatchEmpty: false,
            emptyMatchRequiresEndAssertion: false);
    }

    private static bool HasActivatedAsciiProjection(RegexAutomaton matcher)
    {
        var engine = (RegexMetaEngine)typeof(RegexAutomaton)
            .GetField(
                "engine",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(matcher)!;
        return (int)typeof(RegexMetaEngine)
            .GetField(
                "_asciiFastUnanchoredDfaActivated",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(engine)! != 0;
    }

    private static bool HasCreatedAsciiFastUnanchoredDfaPool(RegexAutomaton matcher)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var engine = (RegexMetaEngine)typeof(RegexAutomaton)
            .GetField("engine", Flags)!
            .GetValue(matcher)!;
        return typeof(RegexMetaEngine)
            .GetField("_asciiFastUnanchoredDfaPool", Flags)!
            .GetValue(engine) is not null;
    }

    private static bool HasActivatedPrimaryUnanchoredDfa(RegexAutomaton matcher)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var engine = (RegexMetaEngine)typeof(RegexAutomaton)
            .GetField("engine", Flags)!
            .GetValue(matcher)!;
        return (int)typeof(RegexMetaEngine)
            .GetField("_unanchoredLazyDfaActivated", Flags)!
            .GetValue(engine)! != 0;
    }
}
