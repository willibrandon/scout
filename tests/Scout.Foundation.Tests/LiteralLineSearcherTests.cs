using System.Text;

namespace Scout;

/// <summary>
/// Verifies literal line search behavior.
/// </summary>
public sealed class LiteralLineSearcherTests
{
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
    /// Verifies pure literal alternations use the literal-set regex path for line search.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralSetRegexFastPathForLiteralAlternation()
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
    /// Verifies literal-set regex line counting honors max-count as matching lines, not matches.
    /// </summary>
    [Fact]
    public void CountMatchingLinesUsesLiteralSetRegexFastPathForLiteralAlternation()
    {
        byte[][] patterns = ["foo|bar"u8.ToArray()];

        long count = LiteralLineSearcher.CountMatchingLines(
            "foo foo\nnone\nbar bar\nfoo\n"u8,
            patterns,
            maxMatchingLines: 2);

        Assert.Equal(2, count);
    }

    /// <summary>
    /// Verifies literal-set regex match counting preserves leftmost-first alternation priority.
    /// </summary>
    [Fact]
    public void CountMatchesUsesLiteralSetRegexFastPathForLiteralAlternation()
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
    /// Verifies class-sequence regex acceleration reports line metadata for ASCII matches.
    /// </summary>
    [Fact]
    public void SearchUsesClassSequenceAcceleratorForAsciiWords()
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
    /// Verifies leading literal regex candidates can search whole haystacks while preserving line output.
    /// </summary>
    [Fact]
    public void SearchUsesCandidateLineAcceleratorForLeadingAlternation()
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
    /// Verifies Scout's prepared no-Unicode wrapper still allows candidate-line acceleration.
    /// </summary>
    [Fact]
    public void RegexSearchPlanUsesCandidateLineAcceleratorForPreparedLeadingAlternation()
    {
        byte[][] patterns = [@"(?-u:\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*)"u8.ToArray()];
        object? plan = typeof(LiteralLineSearcher)
            .GetMethod("CreateRegexSearchPlan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [patterns, false, true]);

        object? accelerator = plan!
            .GetType()
            .GetMethod("GetCandidateLineAccelerator")!
            .Invoke(plan, [0]);

        Assert.NotNull(accelerator);
        AssertRegexSearchPlanAvoidsAutomata(plan);
    }

    /// <summary>
    /// Verifies the CLI's neutral regex wrapper still allows general-mode candidate-line acceleration.
    /// </summary>
    [Fact]
    public void RegexSearchPlanUsesCandidateLineAcceleratorForCliWrappedLeadingAlternationInGeneralMode()
    {
        using RegexSpecializationModeScope scope = RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);
        byte[][] patterns = [@"(?:\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*)"u8.ToArray()];
        object? plan = typeof(LiteralLineSearcher)
            .GetMethod("CreateRegexSearchPlan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [patterns, false, true]);

        object? accelerator = plan!
            .GetType()
            .GetMethod("GetCandidateLineAccelerator")!
            .Invoke(plan, [0]);

        Assert.NotNull(accelerator);
        Assert.True((bool)accelerator.GetType().GetProperty("HasVerifier")!.GetValue(accelerator)!);
        AssertRegexSearchPlanAvoidsAutomata(plan);
    }

    /// <summary>
    /// Verifies Scout's prepared capture wrapper still allows candidate-line acceleration.
    /// </summary>
    [Fact]
    public void RegexSearchPlanUsesCandidateLineAcceleratorForPreparedCaptureLeadingAlternation()
    {
        byte[][] patterns = [@"(?-u:\b(struct|enum|union)\s+([A-Za-z_][A-Za-z0-9_]*))"u8.ToArray()];
        object? plan = typeof(LiteralLineSearcher)
            .GetMethod("CreateRegexSearchPlan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [patterns, false, true]);

        object? accelerator = plan!
            .GetType()
            .GetMethod("GetCandidateLineAccelerator")!
            .Invoke(plan, [0]);

        Assert.NotNull(accelerator);
        AssertRegexSearchPlanAvoidsAutomata(plan);
    }

    private static void AssertRegexSearchPlanAvoidsAutomata(object plan)
    {
        var automata = (RegexAutomaton?[])plan
            .GetType()
            .GetField("automata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(plan)!;
        Assert.All(automata, Assert.Null);
    }

    /// <summary>
    /// Verifies scoped flags that change match spans still use automaton spans.
    /// </summary>
    [Fact]
    public void RegexSearchPlanKeepsScopedUngreedySpansOnAutomatonPath()
    {
        byte[][] patterns = [@"(?U:ab+)"u8.ToArray()];
        object? plan = typeof(LiteralLineSearcher)
            .GetMethod("CreateRegexSearchPlan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [patterns, false, true]);

        object? accelerator = plan!
            .GetType()
            .GetMethod("GetCandidateLineAccelerator")!
            .Invoke(plan, [0]);

        Assert.Null(accelerator);
    }

    /// <summary>
    /// Verifies whole-haystack regex candidate scanning still preserves line-oriented matching.
    /// </summary>
    [Fact]
    public void SearchCandidateLineAcceleratorDoesNotMatchAcrossLines()
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
    /// Verifies candidate-line scanning keeps looking within a line after a false prefix.
    /// </summary>
    [Fact]
    public void SearchCandidateLineAcceleratorContinuesAfterFalsePrefix()
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
    /// Verifies candidate-line scanning emits match-line records for prepared captures.
    /// </summary>
    [Fact]
    public void SearchMatchLinesUsesCandidateLineAcceleratorForPreparedCaptures()
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
    /// Verifies candidate-line scanning can skip exact match-column work when the sink does not need it.
    /// </summary>
    [Fact]
    public void SearchCandidateLineAcceleratorCanSkipMatchColumn()
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
    /// Verifies class-sequence regex acceleration falls back to Unicode-aware matching when needed.
    /// </summary>
    [Fact]
    public void SearchUsesClassSequenceAcceleratorForUnicodeWords()
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
    /// Verifies class-sequence regex acceleration recognizes Unicode whitespace separators.
    /// </summary>
    [Fact]
    public void SearchUsesClassSequenceAcceleratorForUnicodeWhitespace()
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
    /// Verifies class-sequence regex acceleration can skip earliest-column Unicode work when only line selection is needed.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorCanSkipUnicodeColumnFallback()
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
        Assert.True(sink.MatchColumn > 1);
        Assert.Equal(haystack, sink.Line);
    }

    /// <summary>
    /// Verifies no-column class-sequence acceleration still checks earlier Unicode-only matching lines.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorChecksEarlierUnicodeLineWhenColumnSkipped()
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
    /// Verifies Unicode class-sequence backtracking stays on UTF-8 scalar boundaries.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorBacktracksUnicodeScalars()
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
    /// Verifies class-sequence regex acceleration honors max-count limiting.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorHonorsMaxMatchingLines()
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
