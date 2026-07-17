namespace Scout;

/// <summary>
/// Uses a compiled syntax-derived prefilter to select independent records and verifies each
/// selected record with the authoritative matcher.
/// </summary>
internal static class RegexPrefilterRecordSearcher
{
    /// <summary>
    /// Attempts to search a complete independent-record segment through an adaptive prefilter.
    /// </summary>
    /// <typeparam name="TSink">The line-sink type.</typeparam>
    /// <param name="haystack">The complete record-aligned search segment.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="sink">The sink that receives matching records.</param>
    /// <param name="matched">Receives whether at least one record matched.</param>
    /// <param name="searchedLines">Receives the number of searched records when requested.</param>
    /// <param name="countSearchedLines">Whether to populate <paramref name="searchedLines" />.</param>
    /// <param name="maxMatchingLines">The optional matching-record limit.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <param name="requireMatchColumn">Whether to report the first match column.</param>
    /// <returns>
    /// <see langword="true" /> when this searcher handled the complete segment. An unavailable
    /// runner returns <see langword="false" /> before producing any output.
    /// </returns>
    internal static bool TrySearchLines<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        out bool matched,
        out long searchedLines,
        bool countSearchedLines,
        ulong? maxMatchingLines,
        bool nullData,
        bool requireMatchColumn)
        where TSink : struct, ILineSink
    {
        matched = false;
        searchedLines = 0;
        if (regexPlan.Options.Multiline)
        {
            return false;
        }

        RegexPrefilterRunner prefilterRunner =
            regexPlan.Matcher.CreateCandidateRecordPrefilterRunner(haystack.Length);
        if (!prefilterRunner.IsAvailable)
        {
            return false;
        }

        byte terminator = nullData ? (byte)0 : (byte)'\n';
        ulong matchedLines = 0;
        int recordBoundary = 0;
        long lineNumber = 1;
        bool usePrefilter = true;
        bool useForwardMatchEnds = false;
        RegexMatchEndRunner matchEndRunner = default;
        RegexFindRunner findRunner =
            regexPlan.Matcher.RentCandidateRecordFindRunner();
        try
        {
            while (recordBoundary < haystack.Length)
            {
                if (usePrefilter)
                {
                    if (!prefilterRunner.TryFindCandidate(
                            haystack,
                            recordBoundary,
                            out int candidate))
                    {
                        if (!prefilterRunner.IsInert)
                        {
                            break;
                        }

                        usePrefilter = false;
                        if (!requireMatchColumn)
                        {
                            matchEndRunner = regexPlan.Matcher.RentUnfilteredMatchEndRunner(
                                haystack,
                                recordBoundary);
                            useForwardMatchEnds = matchEndRunner.IsAvailable;
                        }

                        continue;
                    }

                    System.Diagnostics.Debug.Assert(candidate >= recordBoundary);
                    System.Diagnostics.Debug.Assert(candidate < haystack.Length);
                    int recordStart = FindCandidateRecordStart(
                        haystack,
                        recordBoundary,
                        candidate,
                        terminator);
                    lineNumber += ByteCounter.Count(
                        haystack.Slice(recordBoundary, recordStart - recordBoundary),
                        terminator);
                    int recordLength = GetRecordLength(haystack[recordStart..], terminator);
                    ReadOnlySpan<byte> record = haystack.Slice(recordStart, recordLength);
                    if (TryEmitMatchingRecord(
                            record,
                            recordStart,
                            lineNumber,
                            candidate - recordStart,
                            prefilterRunner.UsesExactStartCandidates,
                            regexPlan.Matcher,
                            ref findRunner,
                            ref sink,
                            requireMatchColumn,
                            maxMatchingLines,
                            ref matched,
                            ref matchedLines))
                    {
                        if (countSearchedLines)
                        {
                            searchedLines = lineNumber;
                        }

                        return true;
                    }

                    recordBoundary = recordStart + recordLength;
                    lineNumber++;
                    continue;
                }

                if (useForwardMatchEnds)
                {
                    bool found = matchEndRunner.TryFindEnd(
                        haystack,
                        recordBoundary,
                        out int matchEnd,
                        out bool completed);
                    if (!completed)
                    {
                        matchEndRunner.Dispose();
                        useForwardMatchEnds = false;
                        continue;
                    }

                    if (!found)
                    {
                        break;
                    }

                    System.Diagnostics.Debug.Assert(matchEnd > recordBoundary);
                    int recordStart = FindCandidateRecordStart(
                        haystack,
                        recordBoundary,
                        matchEnd - 1,
                        terminator);
                    lineNumber += ByteCounter.Count(
                        haystack.Slice(recordBoundary, recordStart - recordBoundary),
                        terminator);
                    int recordLength = GetRecordLength(haystack[recordStart..], terminator);
                    sink.MatchedLine(
                        lineNumber,
                        recordStart,
                        matchColumn: 0,
                        haystack.Slice(recordStart, recordLength));
                    matched = true;
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        if (countSearchedLines)
                        {
                            searchedLines = lineNumber;
                        }

                        return true;
                    }

                    recordBoundary = recordStart + recordLength;
                    lineNumber++;
                    continue;
                }

                int remainingRecordLength = GetRecordLength(
                    haystack[recordBoundary..],
                    terminator);
                ReadOnlySpan<byte> remainingRecord = haystack.Slice(
                    recordBoundary,
                    remainingRecordLength);
                if (TryEmitMatchingRecord(
                        remainingRecord,
                        recordBoundary,
                        lineNumber,
                        candidateStart: 0,
                        candidateIsExact: false,
                        regexPlan.Matcher,
                        ref findRunner,
                        ref sink,
                        requireMatchColumn,
                        maxMatchingLines,
                        ref matched,
                        ref matchedLines))
                {
                    if (countSearchedLines)
                    {
                        searchedLines = lineNumber;
                    }

                    return true;
                }

                recordBoundary += remainingRecordLength;
                lineNumber++;
            }
        }
        finally
        {
            matchEndRunner.Dispose();
            findRunner.Dispose();
        }

        if (countSearchedLines)
        {
            searchedLines = CountRecords(haystack, terminator);
        }

        return true;
    }

    /// <summary>
    /// Verifies one complete record and emits it when the authoritative matcher succeeds.
    /// </summary>
    private static bool TryEmitMatchingRecord<TSink>(
        ReadOnlySpan<byte> record,
        int recordStart,
        long lineNumber,
        int candidateStart,
        bool candidateIsExact,
        RegexAutomaton matcher,
        ref RegexFindRunner findRunner,
        ref TSink sink,
        bool requireMatchColumn,
        ulong? maxMatchingLines,
        ref bool matched,
        ref ulong matchedLines)
        where TSink : struct, ILineSink
    {
        if (record.Length < matcher.MinimumMatchLength)
        {
            return false;
        }

        RegexMatch? found;
        if (candidateIsExact)
        {
            found = findRunner.TryMatchAt(record, candidateStart, out int length)
                ? new RegexMatch(candidateStart, length)
                : findRunner.Find(record, candidateStart + 1);
        }
        else
        {
            found = findRunner.Find(record, startAt: 0);
        }

        if (!found.HasValue)
        {
            return false;
        }

        sink.MatchedLine(
            lineNumber,
            recordStart,
            requireMatchColumn ? found.Value.Start + 1 : 0,
            record);
        matched = true;
        matchedLines++;
        return maxMatchingLines is ulong limit && matchedLines >= limit;
    }

    /// <summary>
    /// Finds the record boundary at or before one prefilter or match-end candidate.
    /// </summary>
    private static int FindCandidateRecordStart(
        ReadOnlySpan<byte> haystack,
        int recordBoundary,
        int candidate,
        byte terminator)
    {
        ReadOnlySpan<byte> skipped = haystack.Slice(
            recordBoundary,
            candidate - recordBoundary);
        int previousTerminator = skipped.LastIndexOf(terminator);
        return previousTerminator < 0
            ? recordBoundary
            : recordBoundary + previousTerminator + 1;
    }

    /// <summary>
    /// Gets the length of the first complete or final unterminated record in a byte span.
    /// </summary>
    private static int GetRecordLength(ReadOnlySpan<byte> records, byte terminator)
    {
        int terminatorOffset = records.IndexOf(terminator);
        return terminatorOffset < 0 ? records.Length : terminatorOffset + 1;
    }

    /// <summary>
    /// Counts complete and final unterminated records in a byte span.
    /// </summary>
    private static long CountRecords(ReadOnlySpan<byte> records, byte terminator)
    {
        if (records.IsEmpty)
        {
            return 0;
        }

        long terminators = ByteCounter.Count(records, terminator);
        return records[^1] == terminator ? terminators : terminators + 1;
    }
}
