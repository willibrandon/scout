using System.Text;

namespace Scout;

/// <summary>
/// Verifies literal line search behavior.
/// </summary>
public sealed class LiteralLineSearcherTests
{
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
    /// Verifies complete-line segments can reuse general non-empty regex plans while observing NUL bytes.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="haystack">The complete line segment to search.</param>
    [Theory]
    [InlineData(@"\bGeneratedRecord\b", "internal sealed class GeneratedRecord\r\n")]
    [InlineData(@"^internal sealed class GeneratedRecord\r?$", "internal sealed class GeneratedRecord\r\n")]
    [InlineData(@"^[A-Za-z_]{70,90}\r?$", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\r\n")]
    public void CountsIndependentCompleteLineSegmentWithGeneralRegexPlan(
        string pattern,
        string haystack)
    {
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        RegexSearchPlan? plan = null;

        bool counted = LiteralLineSearcher.TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
            Encoding.UTF8.GetBytes(haystack),
            patterns,
            ref plan,
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
        Assert.NotNull(plan);
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
        RegexSearchPlan? plan = null;

        bool counted = LiteralLineSearcher.TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
            "foo\n"u8,
            patterns,
            ref plan,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            out _,
            out _);

        Assert.False(counted);
        Assert.NotNull(plan);
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
    /// Verifies Scout's prepared no-Unicode wrapper keeps one authoritative matcher with an optional prefilter.
    /// </summary>
    [Fact]
    public void RegexSearchPlanUsesAuthoritativeMatcherForPreparedLeadingAlternation()
    {
        byte[][] patterns = [@"(?-u:\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*)"u8.ToArray()];
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
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
    /// Verifies mixed literal and regex alternatives use a conservative prefilter and authoritative verification.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void SearchMixedAlternationUsesConservativeCandidatesWithAuthoritativeVerification()
    {
        const string pattern =
            "CollectExtensionSuffixCandidates|extensionSuffixPatterns|CreateAhoCorasick|GlobSet.cs";
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        byte[] unrelatedLine = "private readonly byte[][] unrelatedPatterns;\n"u8.ToArray();
        byte[] matchingLine = "CreateAhoCorasick(copy);\n"u8.ToArray();
        byte[] haystack = new byte[(unrelatedLine.Length * 350_000) + matchingLine.Length];
        for (int offset = 0; offset < haystack.Length - matchingLine.Length; offset += unrelatedLine.Length)
        {
            unrelatedLine.CopyTo(haystack, offset);
        }

        matchingLine.CopyTo(haystack, haystack.Length - matchingLine.Length);
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

        Assert.NotNull(plan);
        Assert.NotEqual(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
        Assert.True(matched);
        Assert.Equal(1UL, sink.Matches);
        Assert.Equal(350_001, sink.LineNumber);
        Assert.Equal("CreateAhoCorasick"u8.ToArray(), sink.Match);
        Assert.Equal(1, matchingLines);
        Assert.Equal(1, matches);

        var rejectedSink = new CapturingMatchLineSink();
        Assert.False(LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            "CreateButNotAhoCorasick\n"u8,
            patterns,
            plan,
            ref rejectedSink));
        Assert.Equal(0UL, rejectedSink.Matches);
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
    [InlineData("$", "value", false, false, false, false)]
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
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);

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
}
