namespace Scout;

/// <summary>
/// Searches independent ASCII record runs with one projected DFA while reserving the
/// authoritative matcher for records that contain non-ASCII bytes.
/// </summary>
internal static class RegexProjectedRecordRunSearcher
{
    private const int DefaultMinimumProjectedRunLength =
        RegexMetaEngine.UnanchoredLazyDfaHaystackThreshold;

    /// <summary>
    /// Determines whether independent records contain an ASCII run long enough to amortize
    /// projected execution.
    /// </summary>
    /// <param name="haystack">The complete independent-record segment.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <param name="minimumRunLength">The minimum qualifying ASCII run length.</param>
    /// <returns><see langword="true" /> when at least one qualifying ASCII run is present.</returns>
    internal static bool HasEligibleProjectedRecordRun(
        ReadOnlySpan<byte> haystack,
        bool nullData,
        int minimumRunLength = DefaultMinimumProjectedRunLength)
    {
        byte terminator = GetTerminator(nullData);
        var runs = new AsciiRecordRunEnumerator(haystack, terminator);
        while (runs.MoveNext())
        {
            AsciiRecordRun run = runs.Current;
            if (run.IsAscii && run.Length >= minimumRunLength)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to emit matching records through retained projected and authoritative runners.
    /// </summary>
    /// <typeparam name="TSink">The line-sink type.</typeparam>
    /// <param name="haystack">The complete independent-record segment.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="sink">The sink that receives matching records.</param>
    /// <param name="matched">Receives whether at least one record matched.</param>
    /// <param name="searchedLines">Receives the number of records searched when requested.</param>
    /// <param name="countSearchedLines">Whether to populate <paramref name="searchedLines" />.</param>
    /// <param name="maxMatchingLines">The optional matching-record limit.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <returns><see langword="true" /> when the segment was handled.</returns>
    internal static bool TrySearchLines<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        out bool matched,
        out long searchedLines,
        bool countSearchedLines,
        ulong? maxMatchingLines,
        bool nullData)
        where TSink : struct, ILineSink
    {
        return TrySearchCore(
            haystack,
            regexPlan,
            ref sink,
            countEveryMatch: false,
            maxMatchingLines,
            nullData,
            out matched,
            out _,
            out _,
            out searchedLines,
            countSearchedLines);
    }

    /// <summary>
    /// Attempts to emit matching records and count all non-overlapping matches through retained
    /// projected and authoritative runners.
    /// </summary>
    /// <typeparam name="TSink">The line-sink type.</typeparam>
    /// <param name="haystack">The complete independent-record segment.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="sink">The sink that receives matching records.</param>
    /// <param name="matched">Receives whether at least one record matched.</param>
    /// <param name="matchedLines">Receives the number of matching records.</param>
    /// <param name="matches">Receives the non-overlapping match count.</param>
    /// <param name="maxMatchingLines">The optional matching-record limit.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <returns><see langword="true" /> when the segment was handled.</returns>
    internal static bool TrySearchLinesAndCountMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        out bool matched,
        out ulong matchedLines,
        out long matches,
        ulong? maxMatchingLines,
        bool nullData)
        where TSink : struct, ILineSink
    {
        return TrySearchCore(
            haystack,
            regexPlan,
            ref sink,
            countEveryMatch: true,
            maxMatchingLines,
            nullData,
            out matched,
            out matchedLines,
            out matches,
            out _,
            countSearchedLines: false);
    }

    /// <summary>
    /// Attempts to count non-overlapping matches across independent ASCII and non-ASCII records.
    /// </summary>
    /// <param name="haystack">The complete independent-record segment.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <param name="count">Receives the complete match count, or zero when the segment cannot be handled.</param>
    /// <returns><see langword="true" /> when the segment was handled.</returns>
    internal static bool TryCountMatches(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        bool nullData,
        out long count)
    {
        count = 0;
        int minimumProjectedRunLength =
            regexPlan.Matcher.AsciiProjectedMatchEndActivationLength;
        if (!regexPlan.Matcher.HasAsciiProjectedMatchEndRunner ||
            haystack.Length < minimumProjectedRunLength)
        {
            return false;
        }

        byte terminator = GetTerminator(nullData);
        RegexMatchEndRunner runner = default;
        bool useProjection = true;
        RegexFindRunner findRunner = default;
        try
        {
            var runs = new AsciiRecordRunEnumerator(haystack, terminator);
            if (!MoveToFirstProjectedRun(ref runs, minimumProjectedRunLength))
            {
                EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                count = CountAuthoritativeRecords(
                    haystack,
                    regexPlan.Matcher,
                    ref findRunner,
                    terminator,
                    startAt: 0);
                return true;
            }

            AsciiRecordRun firstProjectedRun = runs.Current;
            if (firstProjectedRun.Offset > 0)
            {
                EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                count += CountAuthoritativeRecords(
                    haystack[..firstProjectedRun.Offset],
                    regexPlan.Matcher,
                    ref findRunner,
                    terminator,
                    startAt: 0);
            }

            do
            {
                AsciiRecordRun run = runs.Current;
                ReadOnlySpan<byte> records = haystack.Slice(run.Offset, run.Length);
                if (!run.IsAscii ||
                    !useProjection ||
                    run.Length < minimumProjectedRunLength)
                {
                    EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                    count += CountAuthoritativeRecords(
                        records,
                        regexPlan.Matcher,
                        ref findRunner,
                        terminator,
                        startAt: 0);
                    continue;
                }

                if (!runner.IsAvailable)
                {
                    runner = regexPlan.Matcher.RentAsciiProjectedMatchEndRunner(run.Length);
                    if (!runner.IsAvailable)
                    {
                        runner.Dispose();
                        useProjection = false;
                        EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                        count += CountAuthoritativeRecords(
                            records,
                            regexPlan.Matcher,
                            ref findRunner,
                            terminator,
                            startAt: 0);
                        continue;
                    }
                }

                int searchOffset = 0;
                while (searchOffset < records.Length)
                {
                    bool found = runner.TryFindEnd(
                        records,
                        searchOffset,
                        out int matchEnd,
                        out bool completed);
                    if (!completed)
                    {
                        runner.Dispose();
                        useProjection = false;
                        EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                        count += CountAuthoritativeRecords(
                            records,
                            regexPlan.Matcher,
                            ref findRunner,
                            terminator,
                            searchOffset);
                        break;
                    }

                    if (!found)
                    {
                        break;
                    }

                    count++;
                    searchOffset = matchEnd;
                }
            }
            while (runs.MoveNext());

            return true;
        }
        finally
        {
            runner.Dispose();
            findRunner.Dispose();
        }
    }

    private static bool TrySearchCore<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        bool countEveryMatch,
        ulong? maxMatchingLines,
        bool nullData,
        out bool matched,
        out ulong matchedLines,
        out long matches,
        out long searchedLines,
        bool countSearchedLines)
        where TSink : struct, ILineSink
    {
        matched = false;
        matchedLines = 0;
        matches = 0;
        searchedLines = 0;
        int minimumProjectedRunLength =
            regexPlan.Matcher.AsciiProjectedMatchEndActivationLength;
        if (!regexPlan.Matcher.HasAsciiProjectedMatchEndRunner ||
            haystack.Length < minimumProjectedRunLength)
        {
            return false;
        }

        byte terminator = GetTerminator(nullData);
        RegexMatchEndRunner runner = default;
        bool useProjection = true;
        long runLineNumber = 1;
        RegexFindRunner findRunner = default;
        try
        {
            var runs = new AsciiRecordRunEnumerator(haystack, terminator);
            if (!MoveToFirstProjectedRun(ref runs, minimumProjectedRunLength))
            {
                EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                bool stopped = SearchAuthoritativeRecords(
                    haystack,
                    runOffset: 0,
                    runLineNumber,
                    regexPlan.Matcher,
                    ref findRunner,
                    ref sink,
                    countEveryMatch,
                    maxMatchingLines,
                    terminator,
                    startAt: 0,
                    reportedRecordStart: -1,
                    ref matched,
                    ref matchedLines,
                    ref matches,
                    out long stoppingLine);
                if (countSearchedLines)
                {
                    searchedLines = stopped
                        ? stoppingLine
                        : CountRecords(haystack, terminator);
                }

                return true;
            }

            AsciiRecordRun firstProjectedRun = runs.Current;
            if (firstProjectedRun.Offset > 0)
            {
                ReadOnlySpan<byte> prefixRecords = haystack[..firstProjectedRun.Offset];
                EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                bool prefixStopped = SearchAuthoritativeRecords(
                    prefixRecords,
                    0,
                    runLineNumber,
                    regexPlan.Matcher,
                    ref findRunner,
                    ref sink,
                    countEveryMatch,
                    maxMatchingLines,
                    terminator,
                    startAt: 0,
                    reportedRecordStart: -1,
                    ref matched,
                    ref matchedLines,
                    ref matches,
                    out long prefixStoppingLine);
                if (prefixStopped)
                {
                    if (countSearchedLines)
                    {
                        searchedLines = prefixStoppingLine;
                    }

                    return true;
                }

                runLineNumber += CountRecords(prefixRecords, terminator);
            }

            bool hasRun = true;
            while (hasRun)
            {
                AsciiRecordRun run = runs.Current;
                ReadOnlySpan<byte> records = haystack.Slice(run.Offset, run.Length);
                bool stopped;
                long stoppingLine;
                if (run.IsAscii &&
                    useProjection &&
                    run.Length >= minimumProjectedRunLength)
                {
                    if (!runner.IsAvailable)
                    {
                        runner = regexPlan.Matcher.RentAsciiProjectedMatchEndRunner(run.Length);
                        if (!runner.IsAvailable)
                        {
                            runner.Dispose();
                            useProjection = false;
                        }
                    }

                    if (useProjection)
                    {
                        stopped = SearchProjectedRun(
                            records,
                            run.Offset,
                            runLineNumber,
                            regexPlan.Matcher,
                            ref runner,
                            ref findRunner,
                            ref useProjection,
                            ref sink,
                            countEveryMatch,
                            maxMatchingLines,
                            terminator,
                            ref matched,
                            ref matchedLines,
                            ref matches,
                            out stoppingLine);
                    }
                    else
                    {
                        EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                        stopped = SearchAuthoritativeRecords(
                            records,
                            run.Offset,
                            runLineNumber,
                            regexPlan.Matcher,
                            ref findRunner,
                            ref sink,
                            countEveryMatch,
                            maxMatchingLines,
                            terminator,
                            startAt: 0,
                            reportedRecordStart: -1,
                            ref matched,
                            ref matchedLines,
                            ref matches,
                            out stoppingLine);
                    }
                }
                else
                {
                    EnsureFindRunner(regexPlan.Matcher, ref findRunner);
                    stopped = SearchAuthoritativeRecords(
                        records,
                        run.Offset,
                        runLineNumber,
                        regexPlan.Matcher,
                        ref findRunner,
                        ref sink,
                        countEveryMatch,
                        maxMatchingLines,
                        terminator,
                        startAt: 0,
                        reportedRecordStart: -1,
                        ref matched,
                        ref matchedLines,
                        ref matches,
                        out stoppingLine);
                }

                if (stopped)
                {
                    if (countSearchedLines)
                    {
                        searchedLines = stoppingLine;
                    }

                    return true;
                }

                hasRun = runs.MoveNext();
                if (hasRun)
                {
                    runLineNumber += CountRecords(records, terminator);
                }
            }

            if (countSearchedLines)
            {
                searchedLines = CountRecords(haystack, terminator);
            }

            return true;
        }
        finally
        {
            runner.Dispose();
            findRunner.Dispose();
        }
    }

    private static bool SearchProjectedRun<TSink>(
        ReadOnlySpan<byte> records,
        int runOffset,
        long runLineNumber,
        RegexAutomaton matcher,
        ref RegexMatchEndRunner runner,
        ref RegexFindRunner findRunner,
        ref bool useProjection,
        ref TSink sink,
        bool countEveryMatch,
        ulong? maxMatchingLines,
        byte terminator,
        ref bool matched,
        ref ulong matchedLines,
        ref long matches,
        out long stoppingLine)
        where TSink : struct, ILineSink
    {
        stoppingLine = 0;
        int searchOffset = 0;
        int searchLimit = records.Length;
        int recordStart = 0;
        int recordEnd = GetRecordLength(records, terminator);
        int reportedRecordStart = -1;
        long recordIndex = 0;
        bool stopAfterCurrentRecord = false;
        while (searchOffset < searchLimit)
        {
            bool found = runner.TryFindEnd(
                records[..searchLimit],
                searchOffset,
                out int matchEnd,
                out bool completed);
            if (!completed)
            {
                runner.Dispose();
                useProjection = false;
                EnsureFindRunner(matcher, ref findRunner);
                return SearchAuthoritativeRecords(
                    records[..searchLimit],
                    runOffset,
                    runLineNumber,
                    matcher,
                    ref findRunner,
                    ref sink,
                    countEveryMatch,
                    maxMatchingLines,
                    terminator,
                    searchOffset,
                    reportedRecordStart,
                    ref matched,
                    ref matchedLines,
                    ref matches,
                    out stoppingLine);
            }

            if (!found)
            {
                break;
            }

            int matchPosition = matchEnd - 1;
            while (matchPosition >= recordEnd && recordEnd < searchLimit)
            {
                recordStart = recordEnd;
                recordEnd += GetRecordLength(records[recordStart..searchLimit], terminator);
                recordIndex++;
            }

            if (reportedRecordStart != recordStart)
            {
                sink.MatchedLine(
                    runLineNumber + recordIndex,
                    runOffset + recordStart,
                    matchColumn: 0,
                    records.Slice(recordStart, recordEnd - recordStart));
                reportedRecordStart = recordStart;
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    if (!countEveryMatch)
                    {
                        stoppingLine = runLineNumber + recordIndex;
                        return true;
                    }

                    searchLimit = recordEnd;
                    stopAfterCurrentRecord = true;
                }
            }

            matches++;
            searchOffset = countEveryMatch ? matchEnd : recordEnd;
        }

        if (stopAfterCurrentRecord)
        {
            stoppingLine = runLineNumber + recordIndex;
            return true;
        }

        return false;
    }

    private static bool SearchAuthoritativeRecords<TSink>(
        ReadOnlySpan<byte> records,
        int runOffset,
        long runLineNumber,
        RegexAutomaton matcher,
        ref RegexFindRunner findRunner,
        ref TSink sink,
        bool countEveryMatch,
        ulong? maxMatchingLines,
        byte terminator,
        int startAt,
        int reportedRecordStart,
        ref bool matched,
        ref ulong matchedLines,
        ref long matches,
        out long stoppingLine)
        where TSink : struct, ILineSink
    {
        stoppingLine = 0;
        int boundedStart = Math.Clamp(startAt, 0, records.Length);
        int recordStart = FindRecordStart(records, boundedStart, terminator);
        long recordIndex = ByteCounter.Count(records[..recordStart], terminator);
        bool firstRecord = true;
        while (recordStart < records.Length)
        {
            int recordLength = GetRecordLength(records[recordStart..], terminator);
            ReadOnlySpan<byte> record = records.Slice(recordStart, recordLength);
            int recordStartAt = firstRecord ? boundedStart - recordStart : 0;
            long recordMatches = 0;
            if (record.Length - recordStartAt >= matcher.MinimumMatchLength)
            {
                if (countEveryMatch)
                {
                    recordMatches = CountMatches(record, recordStartAt, ref findRunner);
                }
                else
                {
                    recordMatches = findRunner.Find(record, recordStartAt).HasValue ? 1 : 0;
                }
            }

            if (recordMatches > 0)
            {
                matches += recordMatches;
                if (reportedRecordStart != recordStart)
                {
                    sink.MatchedLine(
                        runLineNumber + recordIndex,
                        runOffset + recordStart,
                        matchColumn: 0,
                        record);
                    reportedRecordStart = recordStart;
                    matched = true;
                    matchedLines++;
                }
            }

            if (maxMatchingLines is ulong limit && matchedLines >= limit)
            {
                stoppingLine = runLineNumber + recordIndex;
                return true;
            }

            recordStart += recordLength;
            recordIndex++;
            firstRecord = false;
        }

        return false;
    }

    private static long CountAuthoritativeRecords(
        ReadOnlySpan<byte> records,
        RegexAutomaton matcher,
        ref RegexFindRunner findRunner,
        byte terminator,
        int startAt)
    {
        long count = 0;
        int boundedStart = Math.Clamp(startAt, 0, records.Length);
        int recordStart = FindRecordStart(records, boundedStart, terminator);
        bool firstRecord = true;
        while (recordStart < records.Length)
        {
            int recordLength = GetRecordLength(records[recordStart..], terminator);
            ReadOnlySpan<byte> record = records.Slice(recordStart, recordLength);
            int recordStartAt = firstRecord ? boundedStart - recordStart : 0;
            if (record.Length - recordStartAt >= matcher.MinimumMatchLength)
            {
                count += CountMatches(record, recordStartAt, ref findRunner);
            }

            recordStart += recordLength;
            firstRecord = false;
        }

        return count;
    }

    private static void EnsureFindRunner(
        RegexAutomaton matcher,
        ref RegexFindRunner findRunner)
    {
        if (!findRunner.IsInitialized)
        {
            findRunner = matcher.RentRecordFindRunner();
        }
    }

    private static bool MoveToFirstProjectedRun(
        ref AsciiRecordRunEnumerator runs,
        int minimumRunLength)
    {
        while (runs.MoveNext())
        {
            AsciiRecordRun run = runs.Current;
            if (run.IsAscii && run.Length >= minimumRunLength)
            {
                return true;
            }
        }

        return false;
    }

    private static long CountMatches(
        ReadOnlySpan<byte> haystack,
        int startAt,
        ref RegexFindRunner findRunner)
    {
        long count = 0;
        int offset = startAt;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= haystack.Length)
        {
            RegexMatch? found = findRunner.Find(haystack, offset);
            if (!found.HasValue)
            {
                return count;
            }

            var match = new MatcherMatch(found.Value.Start, found.Value.Length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, haystack.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            count++;
            offset = MatchIterator.AdvanceAfterReported(
                match,
                haystack.Length,
                ref suppressedEmptyStart);
            if (offset > haystack.Length)
            {
                return count;
            }
        }

        return count;
    }

    private static int FindRecordStart(ReadOnlySpan<byte> records, int startAt, byte terminator)
    {
        if (startAt == 0)
        {
            return 0;
        }

        int previousTerminator = records[..startAt].LastIndexOf(terminator);
        return previousTerminator + 1;
    }

    private static int GetRecordLength(ReadOnlySpan<byte> records, byte terminator)
    {
        int terminatorOffset = records.IndexOf(terminator);
        return terminatorOffset < 0 ? records.Length : terminatorOffset + 1;
    }

    private static long CountRecords(ReadOnlySpan<byte> records, byte terminator)
    {
        if (records.IsEmpty)
        {
            return 0;
        }

        long terminators = ByteCounter.Count(records, terminator);
        return records[^1] == terminator ? terminators : terminators + 1;
    }

    private static byte GetTerminator(bool nullData)
    {
        return nullData ? (byte)0 : (byte)'\n';
    }
}
