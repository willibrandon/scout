using System.Text;

namespace Scout;

/// <summary>
/// Searches byte slices line-by-line for byte patterns.
/// </summary>
public static class LiteralLineSearcher
{
    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Searches <paramref name="haystack" /> for lines containing <paramref name="needle" />.
    /// </summary>
    /// <typeparam name="TSink">The line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="sink">The sink that receives matching lines.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should match.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one line matched.</returns>
    public static bool Search<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, ILineSink
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            needle.Length != 0)
        {
            return SearchLiteralRegexLines(haystack, needle, ref sink, asciiCaseInsensitive, maxMatchingLines, nullData);
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (TryMatchLine(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out int matchStart))
            {
                sink.MatchedLine(lineNumber, lineStart, matchStart < 0 ? 0 : matchStart + 1, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> for lines containing any pattern in <paramref name="needles" />.
    /// </summary>
    /// <typeparam name="TSink">The line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="sink">The sink that receives matching lines.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should match.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <param name="requireMatchColumn">Whether the sink requires the earliest match column for selected lines.</param>
    /// <returns><see langword="true" /> when at least one line matched.</returns>
    public static bool Search<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            needles.Count == 1 &&
            IsLiteralRegex(needles[0]) &&
            needles[0].Length != 0)
        {
            return SearchLiteralRegexLines(haystack, needles[0], ref sink, asciiCaseInsensitive, maxMatchingLines, nullData);
        }

        RegexSearchPlan? regexPlan = CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        return SearchWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData,
            requireMatchColumn);
    }

    internal static RegexSearchPlan? CreateRegexSearchPlan(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive)
    {
        return RegexSearchPlan.Create(needles, asciiCaseInsensitive);
    }

    internal static bool SearchWithRegexPlan<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!invertMatch &&
            !CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            return false;
        }

        if (CanSearchWholeHaystackWithFullMatches(regexPlan, invertMatch) ||
            regexPlan is not null && CanGroupAuthoritativeMatchesByEnd(
                haystack,
                regexPlan,
                invertMatch,
                requireMatchColumn))
        {
            bool authoritativeMatched = SearchAuthoritativeRegexLines(
                haystack,
                regexPlan!,
                ref sink,
                out _,
                out bool completedEfficiently,
                countSearchedLines: false,
                maxMatchingLines,
                nullData,
                requireMatchColumn);
            if (completedEfficiently)
            {
                return authoritativeMatched;
            }
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            while (lineStart < haystack.Length)
            {
                ReadOnlySpan<byte> remaining = haystack[lineStart..];
                int lineLength = GetLineLength(remaining, nullData);
                ReadOnlySpan<byte> line = remaining[..lineLength];
                if (TryMatchLine(
                        line,
                        needles,
                        regexPlan,
                        ref findRunner,
                        asciiCaseInsensitive,
                        invertMatch,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        nullData,
                        out int matchStart))
                {
                    sink.MatchedLine(
                        lineNumber,
                        lineStart,
                        requireMatchColumn && matchStart >= 0 ? matchStart + 1 : 0,
                        line);
                    matched = true;
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        return true;
                    }
                }

                lineStart += lineLength;
                lineNumber++;
            }
        }
        finally
        {
            findRunner.Dispose();
        }

        return matched;
    }

    internal static bool SearchWithRegexPlanAndCountMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        out ulong matchedLines,
        out long matches,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        if (maxMatchingLines == 0)
        {
            matchedLines = 0;
            matches = 0;
            return false;
        }

        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!invertMatch &&
            !CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            matchedLines = 0;
            matches = 0;
            return false;
        }

        if (CanGroupAuthoritativeMatchesByEnd(
                haystack,
                regexPlan!,
                invertMatch,
                requireMatchColumn))
        {
            bool authoritativeMatched = SearchAuthoritativeRegexLinesAndCountMatches(
                haystack,
                regexPlan!,
                ref sink,
                out matchedLines,
                out matches,
                out bool completedEfficiently,
                maxMatchingLines,
                nullData);
            if (completedEfficiently)
            {
                return authoritativeMatched;
            }
        }

        bool matched = false;
        matchedLines = 0;
        matches = 0;
        int lineStart = 0;
        long lineNumber = 1;
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            while (lineStart < haystack.Length)
            {
                ReadOnlySpan<byte> remaining = haystack[lineStart..];
                int lineLength = GetLineLength(remaining, nullData);
                ReadOnlySpan<byte> line = remaining[..lineLength];
                if (TryMatchLine(
                        line,
                        needles,
                        regexPlan,
                        ref findRunner,
                        asciiCaseInsensitive,
                        invertMatch,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        nullData,
                        out int matchStart))
                {
                    sink.MatchedLine(
                        lineNumber,
                        lineStart,
                        requireMatchColumn && matchStart >= 0 ? matchStart + 1 : 0,
                        line);
                    matched = true;
                    matchedLines++;
                    if (!invertMatch)
                    {
                        matches += CountReportedLineMatchesWithRegexPlan(
                            line,
                            needles,
                            regexPlan,
                            ref findRunner,
                            asciiCaseInsensitive,
                            lineRegexp,
                            wordRegexp,
                            crlf,
                            nullData);
                    }

                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        return true;
                    }
                }

                lineStart += lineLength;
                lineNumber++;
            }
        }
        finally
        {
            findRunner.Dispose();
        }

        return matched;
    }

    internal static bool SearchWithRegexPlanAndCountLines<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        out long searchedLines,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        searchedLines = 0;
        if (maxMatchingLines == 0)
        {
            return false;
        }

        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!invertMatch &&
            !CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            searchedLines = CountSearchLines(haystack, nullData);
            return false;
        }

        if (CanSearchWholeHaystackWithFullMatches(regexPlan, invertMatch) ||
            regexPlan is not null && CanGroupAuthoritativeMatchesByEnd(
                haystack,
                regexPlan,
                invertMatch,
                requireMatchColumn))
        {
            bool authoritativeMatched = SearchAuthoritativeRegexLines(
                haystack,
                regexPlan!,
                ref sink,
                out searchedLines,
                out bool completedEfficiently,
                countSearchedLines: true,
                maxMatchingLines,
                nullData,
                requireMatchColumn);
            if (completedEfficiently)
            {
                return authoritativeMatched;
            }
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            while (lineStart < haystack.Length)
            {
                ReadOnlySpan<byte> remaining = haystack[lineStart..];
                int lineLength = GetLineLength(remaining, nullData);
                ReadOnlySpan<byte> line = remaining[..lineLength];
                if (TryMatchLine(
                        line,
                        needles,
                        regexPlan,
                        ref findRunner,
                        asciiCaseInsensitive,
                        invertMatch,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        nullData,
                        out int matchStart))
                {
                    sink.MatchedLine(
                        lineNumber,
                        lineStart,
                        requireMatchColumn && matchStart >= 0 ? matchStart + 1 : 0,
                        line);
                    matched = true;
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        searchedLines = lineNumber;
                        return true;
                    }
                }

                lineStart += lineLength;
                lineNumber++;
            }
        }
        finally
        {
            findRunner.Dispose();
        }

        searchedLines = lineNumber - 1;
        return matched;
    }

    internal static long CountMatchingLinesWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        var sink = new CountingLineSink();
        _ = SearchWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData,
            requireMatchColumn: false);
        return (long)sink.MatchedLines;
    }

    private static bool CanSearchWholeHaystackWithFullMatches(
        RegexSearchPlan? regexPlan,
        bool invertMatch)
    {
        return CanSearchIndependentRecords(regexPlan, invertMatch) &&
            regexPlan!.Matcher.CanSearchWholeHaystackWithFullMatches;
    }

    /// <summary>
    /// Attempts to count a complete segment with one concrete forward match-end runner.
    /// </summary>
    /// <param name="haystack">The complete search segment.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="count">Receives the match count when the runner completes.</param>
    /// <returns><see langword="true" /> when a runner was available and completed.</returns>
    private static bool TryCountWholeHaystackWithMatchEnds(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        out long count)
    {
        count = 0;
        RegexMatchEndRunner runner = regexPlan.Matcher.RentMatchEndRunner(
            haystack,
            startAt: 0);
        try
        {
            return runner.IsAvailable && runner.TryCountMatches(
                haystack,
                startAt: 0,
                out count);
        }
        finally
        {
            runner.Dispose();
        }
    }

    private static bool CanSearchIndependentRecords(
        RegexSearchPlan? regexPlan,
        bool invertMatch)
    {
        return !invertMatch &&
            regexPlan is not null &&
            (!regexPlan.Options.LineRegexp ||
                !regexPlan.Options.Multiline && !regexPlan.Options.NullData) &&
            !regexPlan.Options.PreserveCrlfCarriageReturn &&
            !regexPlan.HasHaystackAnchors &&
            (!regexPlan.Options.NullData || !regexPlan.HasLineAnchors) &&
            !regexPlan.CanMatchEmpty;
    }

    /// <summary>
    /// Determines whether any independent record is large enough to satisfy the syntax-derived
    /// minimum match length.
    /// </summary>
    /// <param name="haystack">The complete record-aligned search segment.</param>
    /// <param name="regexPlan">The optional authoritative regex plan.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <returns>
    /// <see langword="true" /> when a record may be long enough or independent-record reasoning
    /// is not valid for the plan.
    /// </returns>
    private static bool CanAnyIndependentRecordMatch(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan? regexPlan,
        bool nullData)
    {
        if (!CanSearchIndependentRecords(regexPlan, invertMatch: false) ||
            regexPlan!.MinimumMatchLength <= 0)
        {
            return true;
        }

        int minimumMatchLength = regexPlan.MinimumMatchLength;
        byte terminator = nullData ? (byte)0 : (byte)'\n';
        int recordStart = 0;
        while (recordStart < haystack.Length)
        {
            int terminatorOffset = haystack[recordStart..].IndexOf(terminator);
            int recordLength = terminatorOffset < 0
                ? haystack.Length - recordStart
                : terminatorOffset + 1;
            if (recordLength >= minimumMatchLength)
            {
                return true;
            }

            recordStart += recordLength;
        }

        return false;
    }

    /// <summary>
    /// Determines whether match ends identify selected records without reconstructing match starts.
    /// </summary>
    /// <param name="haystack">The complete search segment.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="invertMatch">Whether non-matching records are selected.</param>
    /// <param name="requireMatchColumn">Whether the first match start must be reported.</param>
    /// <returns><see langword="true" /> when forward match ends preserve record selection semantics.</returns>
    internal static bool CanGroupAuthoritativeMatchesByEnd(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        bool invertMatch,
        bool requireMatchColumn)
    {
        return !requireMatchColumn &&
            !regexPlan.Options.Multiline &&
            CanSearchIndependentRecords(regexPlan, invertMatch);
    }

    private static bool SearchAuthoritativeRegexLines<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        out long searchedLines,
        out bool completedEfficiently,
        bool countSearchedLines,
        ulong? maxMatchingLines,
        bool nullData,
        bool requireMatchColumn)
        where TSink : struct, ILineSink
    {
        searchedLines = 0;
        completedEfficiently = true;
        bool matched = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int lineStart = 0;
        int lineEnd = haystack.IsEmpty ? 0 : GetLineLength(haystack, nullData);
        long lineNumber = 1;
        RegexMatchEndRunner matchEndRunner = default;
        bool useForwardMatchEnds = CanGroupAuthoritativeMatchesByEnd(
            haystack,
            regexPlan,
            invertMatch: false,
            requireMatchColumn);
        if (useForwardMatchEnds &&
            !regexPlan.Matcher.CanSearchWholeHaystackWithFullMatches)
        {
            if (RegexProjectedRecordRunSearcher.TrySearchLines(
                    haystack,
                    regexPlan,
                    ref sink,
                    out bool projectedMatched,
                    out long projectedSearchedLines,
                    countSearchedLines,
                    maxMatchingLines,
                    nullData))
            {
                searchedLines = projectedSearchedLines;
                return projectedMatched;
            }
        }

        if (useForwardMatchEnds)
        {
            matchEndRunner = regexPlan.Matcher.RentMatchEndRunner(haystack, searchOffset);
            if (!matchEndRunner.IsAvailable &&
                !regexPlan.Matcher.CanSearchWholeHaystackWithFullMatches)
            {
                completedEfficiently = false;
                return false;
            }

            useForwardMatchEnds = matchEndRunner.IsAvailable;
        }

        RegexFindRunner findRunner = regexPlan.Matcher.RentFindRunner();
        try
        {
            while (searchOffset < haystack.Length)
            {
                int matchStart;
                int matchPosition;
                if (useForwardMatchEnds)
                {
                    bool found = matchEndRunner.TryFindEnd(
                        haystack,
                        searchOffset,
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

                    matchStart = -1;
                    matchPosition = matchEnd - 1;
                }
                else
                {
                    RegexMatch? match = findRunner.Find(haystack, searchOffset);
                    if (!match.HasValue)
                    {
                        break;
                    }

                    matchStart = match.Value.Start;
                    matchPosition = matchStart;
                }

                while (matchPosition >= lineEnd && lineEnd < haystack.Length)
                {
                    lineStart = lineEnd;
                    lineEnd += GetLineLength(haystack[lineStart..], nullData);
                    lineNumber++;
                }

                int matchColumn = requireMatchColumn ? matchStart - lineStart + 1 : 0;
                sink.MatchedLine(
                    lineNumber,
                    lineStart,
                    matchColumn,
                    haystack.Slice(lineStart, lineEnd - lineStart));
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

                searchOffset = lineEnd;
            }
        }
        finally
        {
            matchEndRunner.Dispose();
            findRunner.Dispose();
        }

        if (countSearchedLines)
        {
            searchedLines = CountSearchLines(haystack, nullData);
        }

        return matched;
    }

    /// <summary>
    /// Emits authoritative matching lines and counts non-overlapping matches in one forward scan.
    /// </summary>
    /// <typeparam name="TSink">The line-sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="sink">The sink that receives each selected line once.</param>
    /// <param name="matchedLines">Receives the selected-line count.</param>
    /// <param name="matches">Receives the non-overlapping match count.</param>
    /// <param name="completedEfficiently">
    /// Receives whether a concrete forward runner was available before any output was produced.
    /// </param>
    /// <param name="maxMatchingLines">The optional selected-line limit.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <returns><see langword="true" /> when at least one line matches.</returns>
    private static bool SearchAuthoritativeRegexLinesAndCountMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        out ulong matchedLines,
        out long matches,
        out bool completedEfficiently,
        ulong? maxMatchingLines,
        bool nullData)
        where TSink : struct, ILineSink
    {
        matchedLines = 0;
        matches = 0;
        completedEfficiently = true;
        bool matched = false;
        int searchOffset = 0;
        int searchLimit = haystack.Length;
        int lineStart = 0;
        int lineEnd = haystack.IsEmpty ? 0 : GetLineLength(haystack, nullData);
        int reportedLineStart = -1;
        long lineNumber = 1;
        if (!regexPlan.Matcher.CanSearchWholeHaystackWithFullMatches)
        {
            if (RegexProjectedRecordRunSearcher.TrySearchLinesAndCountMatches(
                    haystack,
                    regexPlan,
                    ref sink,
                    out bool projectedMatched,
                    out ulong projectedMatchedLines,
                    out long projectedMatches,
                    maxMatchingLines,
                    nullData))
            {
                matchedLines = projectedMatchedLines;
                matches = projectedMatches;
                return projectedMatched;
            }
        }

        RegexMatchEndRunner matchEndRunner = regexPlan.Matcher.RentMatchEndRunner(
            haystack,
            searchOffset);
        if (!matchEndRunner.IsAvailable &&
            !regexPlan.Matcher.CanSearchWholeHaystackWithFullMatches)
        {
            completedEfficiently = false;
            return false;
        }

        bool useForwardMatchEnds = matchEndRunner.IsAvailable;
        RegexFindRunner findRunner = regexPlan.Matcher.RentFindRunner();
        try
        {
            while (searchOffset < searchLimit)
            {
                ReadOnlySpan<byte> searchHaystack = haystack[..searchLimit];
                int matchEnd;
                if (useForwardMatchEnds)
                {
                    bool found = matchEndRunner.TryFindEnd(
                        searchHaystack,
                        searchOffset,
                        out matchEnd,
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
                }
                else
                {
                    RegexMatch? match = findRunner.Find(searchHaystack, searchOffset);
                    if (!match.HasValue)
                    {
                        break;
                    }

                    matchEnd = match.Value.End;
                }

                System.Diagnostics.Debug.Assert(matchEnd > searchOffset);
                int matchPosition = matchEnd - 1;
                while (matchPosition >= lineEnd && lineEnd < searchLimit)
                {
                    lineStart = lineEnd;
                    lineEnd += GetLineLength(haystack[lineStart..searchLimit], nullData);
                    lineNumber++;
                }

                if (reportedLineStart != lineStart)
                {
                    sink.MatchedLine(
                        lineNumber,
                        lineStart,
                        matchColumn: 0,
                        haystack.Slice(lineStart, lineEnd - lineStart));
                    reportedLineStart = lineStart;
                    matched = true;
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        searchLimit = lineEnd;
                    }
                }

                matches++;
                searchOffset = matchEnd;
            }
        }
        finally
        {
            matchEndRunner.Dispose();
            findRunner.Dispose();
        }

        return matched;
    }

    private static bool SearchLiteralRegexLines<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool nullData)
        where TSink : struct, ILineSink
    {
        bool matched = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int lineStart = 0;
        long lineNumber = 1;
        byte terminator = GetLineTerminator(nullData);
        while (searchOffset < haystack.Length)
        {
            int found = Find(haystack[searchOffset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matched;
            }

            int matchStart = searchOffset + found;
            ReadOnlySpan<byte> skipped = haystack.Slice(lineStart, matchStart - lineStart);
            int previousTerminator = skipped.LastIndexOf(terminator);
            if (previousTerminator >= 0)
            {
                lineNumber += ByteCounter.Count(skipped, terminator);
                lineStart += previousTerminator + 1;
            }

            int lineLength = GetLineLength(haystack[lineStart..], nullData);
            sink.MatchedLine(lineNumber, lineStart, matchStart - lineStart + 1, haystack.Slice(lineStart, lineLength));
            matched = true;
            matchedLines++;
            if (maxMatchingLines is ulong limit && matchedLines >= limit)
            {
                return true;
            }

            searchOffset = lineStart + lineLength;
            lineStart = searchOffset;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Tests whether any line contains <paramref name="needle" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should match.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to consider, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when any line matches.</returns>
    public static bool HasMatch(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (invertMatch || lineRegexp || wordRegexp)
        {
            return HasMatchingLine(haystack, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData);
        }

        return Find(haystack, needle, asciiCaseInsensitive) >= 0;
    }

    /// <summary>
    /// Tests whether any line contains any pattern in <paramref name="needles" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should match.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to consider, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when any line matches.</returns>
    public static bool HasMatch(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        RegexSearchPlan? regexPlan = CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        return HasMatchWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    internal static bool HasMatchWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        var sink = new CountingLineSink();
        return SearchWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines: 1,
            crlf,
            nullData,
            requireMatchColumn: false);
    }

    /// <summary>
    /// Counts lines containing <paramref name="needle" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should match.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matching lines.</returns>
    public static long CountMatchingLines(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (LineMatches(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData))
            {
                count++;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    /// <summary>
    /// Counts lines containing any pattern in <paramref name="needles" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should match.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matching lines.</returns>
    public static long CountMatchingLines(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        RegexSearchPlan? regexPlan = CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        return CountMatchingLinesWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="needle" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should be counted instead of occurrences.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count within, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public static long CountMatches(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        if (invertMatch || lineRegexp)
        {
            return CountMatchingLines(haystack, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
        }

        if (wordRegexp)
        {
            return CountWordMatchesByLine(haystack, needle, asciiCaseInsensitive, maxMatchingLines, crlf, nullData);
        }

        if (maxMatchingLines is not null)
        {
            return CountLiteralMatchesByLine(haystack, needle, asciiCaseInsensitive, maxMatchingLines.Value, nullData);
        }

        if (needle.IsEmpty)
        {
            return haystack.Length;
        }

        long count = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return count;
            }

            count++;
            offset += found + needle.Length;
        }

        return count;
    }

    /// <summary>
    /// Counts non-overlapping occurrences of any pattern in <paramref name="needles" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should be counted instead of occurrences.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count within, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public static long CountMatches(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        RegexSearchPlan? regexPlan = CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        return CountMatchesWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    internal static long CountMatchesWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!invertMatch &&
            !CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            return 0;
        }

        if (invertMatch || lineRegexp)
        {
            return CountMatchingLinesWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
        }

        if (maxMatchingLines is null &&
            CanSearchIndependentRecords(regexPlan, invertMatch))
        {
            if (regexPlan!.Matcher.CanSearchWholeHaystackWithFullMatches)
            {
                return regexPlan.Matcher.CountMatches(haystack);
            }

            if (RegexProjectedRecordRunSearcher.TryCountMatches(
                    haystack,
                    regexPlan,
                    nullData,
                    out long projectedCount))
            {
                return projectedCount;
            }

            if (TryCountWholeHaystackWithMatchEnds(
                    haystack,
                    regexPlan,
                    out long matchEndCount))
            {
                return matchEndCount;
            }
        }

        return CountPatternMatchesByLine(haystack, needles, regexPlan, asciiCaseInsensitive, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    /// <summary>
    /// Attempts to count regex matches while its required-literal scan detects NUL bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns.</param>
    /// <param name="regexPlan">The optional reusable regex plan, populated when compilation is required.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII matching is case-insensitive.</param>
    /// <param name="invertMatch">Whether non-matching lines are selected.</param>
    /// <param name="lineRegexp">Whether matches must span complete lines.</param>
    /// <param name="wordRegexp">Whether matches must satisfy word boundaries.</param>
    /// <param name="maxMatchingLines">The optional maximum number of matching lines.</param>
    /// <param name="crlf">Whether CRLF is a line terminator.</param>
    /// <param name="nullData">Whether NUL is the record terminator.</param>
    /// <param name="count">Receives the number of non-overlapping matches.</param>
    /// <param name="containsNul">Receives whether the complete haystack contains a NUL byte.</param>
    /// <returns><see langword="true" /> when matching and NUL detection shared one complete scan.</returns>
    internal static bool TryCountMatchesAndDetectNulWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxMatchingLines,
        bool crlf,
        bool nullData,
        out long count,
        out bool containsNul)
    {
        count = 0;
        containsNul = false;
        if (invertMatch || lineRegexp || maxMatchingLines is not null)
        {
            return false;
        }

        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (CanSearchIndependentRecords(regexPlan, invertMatch) &&
            !CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            containsNul = haystack.Contains((byte)0);
            return true;
        }

        return CanSearchWholeHaystackWithFullMatches(regexPlan, invertMatch) &&
            regexPlan!.Matcher.TryCountMatchesAndDetectNul(
                haystack,
                out count,
                out containsNul);
    }

    /// <summary>
    /// Attempts to count non-empty regex matches while separately detecting NUL bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns.</param>
    /// <param name="regexPlan">The optional reusable regex plan, populated when compilation is required.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII matching is case-insensitive.</param>
    /// <param name="invertMatch">Whether non-matching lines are selected.</param>
    /// <param name="lineRegexp">Whether matches must span complete lines.</param>
    /// <param name="wordRegexp">Whether matches must satisfy word boundaries.</param>
    /// <param name="crlf">Whether CRLF is a line terminator.</param>
    /// <param name="nullData">Whether NUL is the record terminator.</param>
    /// <param name="count">Receives the number of non-overlapping matches.</param>
    /// <param name="containsNul">Receives whether the complete haystack contains a NUL byte.</param>
    /// <returns><see langword="true" /> when the plan can search an independent complete-line segment.</returns>
    internal static bool TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out long count,
        out bool containsNul)
    {
        count = 0;
        containsNul = false;
        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!CanSearchIndependentRecords(regexPlan, invertMatch))
        {
            return false;
        }

        if (!CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            containsNul = haystack.Contains((byte)0);
            return true;
        }

        if (regexPlan!.Matcher.CanSearchWholeHaystackWithFullMatches)
        {
            count = regexPlan.Matcher.CountMatches(haystack);
        }
        else if (RegexProjectedRecordRunSearcher.TryCountMatches(
                     haystack,
                     regexPlan,
                     nullData,
                     out long projectedCount))
        {
            count = projectedCount;
        }
        else if (TryCountWholeHaystackWithMatchEnds(
                     haystack,
                     regexPlan,
                     out long matchEndCount))
        {
            count = matchEndCount;
        }
        else
        {
            return false;
        }

        containsNul = haystack.Contains((byte)0);
        return true;
    }

    /// <summary>
    /// Attempts to count matching records while separately detecting NUL bytes in one complete,
    /// independently searchable record segment.
    /// </summary>
    /// <param name="haystack">The complete record-aligned segment to search.</param>
    /// <param name="needles">The ordered byte regex patterns.</param>
    /// <param name="regexPlan">The optional reusable regex plan, populated when compilation is required.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII matching is case-insensitive.</param>
    /// <param name="invertMatch">Whether non-matching records are selected.</param>
    /// <param name="lineRegexp">Whether matches must span complete records.</param>
    /// <param name="wordRegexp">Whether matches must satisfy word boundaries.</param>
    /// <param name="crlf">Whether CRLF is a record terminator.</param>
    /// <param name="nullData">Whether NUL is the record terminator.</param>
    /// <param name="count">Receives the number of matching records.</param>
    /// <param name="containsNul">Receives whether the complete segment contains a NUL byte.</param>
    /// <returns>
    /// <see langword="true" /> when artificial segment boundaries cannot change record-selection
    /// semantics; otherwise, <see langword="false" />.
    /// </returns>
    internal static bool TryCountMatchingLinesAndDetectNulWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out long count,
        out bool containsNul)
    {
        count = 0;
        containsNul = false;
        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!CanSearchIndependentRecords(regexPlan, invertMatch))
        {
            return false;
        }

        count = CountMatchingLinesWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines: null,
            crlf,
            nullData);
        containsNul = haystack.Contains((byte)0);
        return true;
    }

    /// <summary>
    /// Counts non-overlapping regex matches and their distinct containing lines in one line-selection pass.
    /// </summary>
    internal static void CountMatchesAndMatchingLinesWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxMatchingLines,
        bool crlf,
        bool nullData,
        out long matchingLines,
        out long matches)
    {
        var sink = new CountingLineSink();
        SearchWithRegexPlanAndCountMatches(
            haystack,
            needles,
            regexPlan,
            ref sink,
            out ulong matchedLineCount,
            out matches,
            asciiCaseInsensitive,
            invertMatch: false,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData,
            requireMatchColumn: false);
        matchingLines = checked((long)matchedLineCount);
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match.
    /// </summary>
    /// <typeparam name="TSink">The match sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="sink">The sink that receives matching bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchSink
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (SearchLineMatches(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData))
            {
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match for any pattern in <paramref name="needles" />.
    /// </summary>
    /// <typeparam name="TSink">The match sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="sink">The sink that receives matching bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        RegexSearchPlan? regexPlan = CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            return false;
        }

        if (CanSearchWholeHaystackWithFullMatches(regexPlan, invertMatch: false))
        {
            return SearchAuthoritativeRegexMatches(
                haystack,
                regexPlan!,
                ref sink,
                maxMatchingLines,
                nullData);
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            while (lineStart < haystack.Length)
            {
                ReadOnlySpan<byte> remaining = haystack[lineStart..];
                int lineLength = GetLineLength(remaining, nullData);
                ReadOnlySpan<byte> line = remaining[..lineLength];
                if (SearchLineMatches(
                        line,
                        lineStart,
                        lineNumber,
                        needles,
                        regexPlan,
                        ref findRunner,
                        ref sink,
                        asciiCaseInsensitive,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        nullData))
                {
                    matched = true;
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        return true;
                    }
                }

                lineStart += lineLength;
                lineNumber++;
            }
        }
        finally
        {
            findRunner.Dispose();
        }

        return matched;
    }

    private static bool SearchAuthoritativeRegexMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        ulong? maxMatchingLines,
        bool nullData)
        where TSink : struct, IMatchSink
    {
        bool matched = false;
        bool matchedCurrentLine = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int lineStart = 0;
        int lineEnd = haystack.IsEmpty ? 0 : GetLineLength(haystack, nullData);
        long lineNumber = 1;
        using RegexFindRunner findRunner = regexPlan.Matcher.RentFindRunner();
        while (searchOffset < haystack.Length)
        {
            RegexMatch? match = findRunner.Find(haystack, searchOffset);
            if (!match.HasValue)
            {
                break;
            }

            while (match.Value.Start >= lineEnd && lineEnd < haystack.Length)
            {
                lineStart = lineEnd;
                lineEnd += GetLineLength(haystack[lineStart..], nullData);
                lineNumber++;
                matchedCurrentLine = false;
            }

            if (!matchedCurrentLine)
            {
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }

                matchedLines++;
                matchedCurrentLine = true;
            }

            sink.Matched(
                lineNumber,
                match.Value.Start,
                match.Value.Start - lineStart + 1,
                haystack.Slice(match.Value.Start, match.Value.Length));
            matched = true;
            searchOffset = match.Value.End;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match with its containing line.
    /// </summary>
    /// <typeparam name="TSink">The match-line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="sink">The sink that receives matching lines and bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatchLines<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchLineSink
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (SearchLineMatchLines(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData))
            {
                sink.FinishLine(lineNumber, lineStart, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match with its containing line.
    /// </summary>
    /// <typeparam name="TSink">The match-line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="sink">The sink that receives matching lines and bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatchLines<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchLineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        RegexSearchPlan? regexPlan = CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        return SearchMatchLinesWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData);
    }

    internal static bool SearchMatchLinesWithRegexPlan<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchLineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!CanAnyIndependentRecordMatch(haystack, regexPlan, nullData))
        {
            return false;
        }

        if (CanSearchWholeHaystackWithFullMatches(regexPlan, invertMatch: false))
        {
            return SearchAuthoritativeRegexMatchLines(
                haystack,
                regexPlan!,
                ref sink,
                maxMatchingLines,
                nullData);
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            while (lineStart < haystack.Length)
            {
                ReadOnlySpan<byte> remaining = haystack[lineStart..];
                int lineLength = GetLineLength(remaining, nullData);
                ReadOnlySpan<byte> line = remaining[..lineLength];
                if (SearchLineMatchLines(
                        line,
                        lineStart,
                        lineNumber,
                        needles,
                        regexPlan,
                        ref findRunner,
                        ref sink,
                        asciiCaseInsensitive,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        nullData))
                {
                    sink.FinishLine(lineNumber, lineStart, line);
                    matched = true;
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        return true;
                    }
                }

                lineStart += lineLength;
                lineNumber++;
            }
        }
        finally
        {
            findRunner.Dispose();
        }

        return matched;
    }

    private static bool SearchAuthoritativeRegexMatchLines<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexSearchPlan regexPlan,
        ref TSink sink,
        ulong? maxMatchingLines,
        bool nullData)
        where TSink : struct, IMatchLineSink
    {
        bool matched = false;
        bool matchedCurrentLine = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int lineStart = 0;
        int lineEnd = haystack.IsEmpty ? 0 : GetLineLength(haystack, nullData);
        long lineNumber = 1;
        using RegexFindRunner findRunner = regexPlan.Matcher.RentFindRunner();
        while (searchOffset < haystack.Length)
        {
            RegexMatch? match = findRunner.Find(haystack, searchOffset);
            if (!match.HasValue)
            {
                break;
            }

            while (match.Value.Start >= lineEnd && lineEnd < haystack.Length)
            {
                if (matchedCurrentLine)
                {
                    sink.FinishLine(
                        lineNumber,
                        lineStart,
                        haystack.Slice(lineStart, lineEnd - lineStart));
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        return true;
                    }

                    matchedCurrentLine = false;
                }

                lineStart = lineEnd;
                lineEnd += GetLineLength(haystack[lineStart..], nullData);
                lineNumber++;
            }

            sink.MatchedLine(
                lineNumber,
                lineStart,
                match.Value.Start,
                match.Value.Start - lineStart + 1,
                haystack.Slice(lineStart, lineEnd - lineStart),
                haystack.Slice(match.Value.Start, match.Value.Length));
            matched = true;
            matchedCurrentLine = true;
            searchOffset = match.Value.End;
        }

        if (matchedCurrentLine)
        {
            sink.FinishLine(
                lineNumber,
                lineStart,
                haystack.Slice(lineStart, lineEnd - lineStart));
        }

        return matched;
    }

    /// <summary>
    /// Counts non-overlapping matches in a single line.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches in the line.</returns>
    public static long CountLineMatches(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        return CountLineMatchesAfterColumn(line, needle, column: 0, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
    }

    /// <summary>
    /// Counts non-overlapping matches for any pattern in <paramref name="needles" /> in a single line.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches in the line.</returns>
    public static long CountLineMatches(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        return CountLineMatchesAfterColumn(line, needles, column: 0, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
    }

    /// <summary>
    /// Counts non-overlapping matches in a single line whose one-based column is greater than <paramref name="column" />.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="column">The one-based column threshold.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches starting after the column threshold.</returns>
    public static long CountLineMatchesAfterColumn(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        ulong column,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        if (lineRegexp)
        {
            return TryFindFullLineLiteralMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out int matchStart, out _) &&
                IsMatchStartAfterColumn(matchStart, column)
                ? 1
                : 0;
        }

        if (wordRegexp)
        {
            return CountWordMatchesAfterColumn(LineContent(line, crlf, nullData), needle, column, asciiCaseInsensitive);
        }

        return CountLiteralMatchesAfterColumn(line, needle, column, asciiCaseInsensitive);
    }

    /// <summary>
    /// Counts non-overlapping matches in a single line whose one-based column is greater than <paramref name="column" />.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="column">The one-based column threshold.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches starting after the column threshold.</returns>
    public static long CountLineMatchesAfterColumn(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        ulong column,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (lineRegexp)
        {
            return TryFindPatternMatch(line, needles, offset: 0, asciiCaseInsensitive, lineRegexp: true, wordRegexp: false, crlf, nullData, out int matchStart, out _) &&
                IsMatchStartAfterColumn(matchStart, column)
                ? 1
                : 0;
        }

        RegexSearchPlan? regexPlan = CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp: false,
            wordRegexp,
            crlf,
            nullData);
        ReadOnlySpan<byte> haystack = RegexPatternContent(
            line,
            regexPlan,
            wordRegexp,
            crlf,
            nullData);
        return CountPatternMatchesAfterColumn(
            haystack,
            needles,
            regexPlan,
            column,
            asciiCaseInsensitive,
            wordRegexp,
            allowEndEmptyMatch: haystack.Length < line.Length);
    }

    internal static long CountLineMatchesWithRegexPlan(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (lineRegexp)
        {
            return TryFindPatternMatch(line, needles, offset: 0, asciiCaseInsensitive, lineRegexp: true, wordRegexp: false, crlf, nullData, regexPlan, out _, out _)
                ? 1
                : 0;
        }

        ReadOnlySpan<byte> haystack = RegexPatternContent(
            line,
            regexPlan,
            wordRegexp,
            crlf,
            nullData);
        return CountPatternMatches(
            haystack,
            needles,
            regexPlan,
            asciiCaseInsensitive,
            wordRegexp,
            allowEndEmptyMatch: haystack.Length < line.Length);
    }

    /// <summary>
    /// Counts reportable non-overlapping matches in one selected line.
    /// </summary>
    /// <param name="line">The selected line, including its record terminator when present.</param>
    /// <param name="needles">The ordered regex patterns.</param>
    /// <param name="regexPlan">The reusable regex search plan.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII matching is case-insensitive.</param>
    /// <param name="lineRegexp">Whether patterns must match complete lines.</param>
    /// <param name="wordRegexp">Whether matches require word boundaries.</param>
    /// <param name="crlf">Whether CRLF terminates records.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <returns>The number of reportable matches.</returns>
    internal static long CountReportedLineMatchesWithRegexPlan(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        regexPlan = EnsureRegexSearchPlan(
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            return CountReportedLineMatchesWithRegexPlan(
                line,
                needles,
                regexPlan,
                ref findRunner,
                asciiCaseInsensitive,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData);
        }
        finally
        {
            findRunner.Dispose();
        }
    }

    private static long CountReportedLineMatchesWithRegexPlan(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        if (lineRegexp)
        {
            return TryFindPatternMatch(
                    line,
                    needles,
                    offset: 0,
                    asciiCaseInsensitive,
                    lineRegexp: true,
                    wordRegexp: false,
                    crlf,
                    nullData,
                    regexPlan,
                    ref findRunner,
                    out _,
                    out _)
                ? 1
                : 0;
        }

        ReadOnlySpan<byte> content = RegexReportingContent(
            line,
            regexPlan,
            wordRegexp,
            crlf,
            nullData);
        return CountPatternMatches(
            content,
            needles,
            regexPlan,
            ref findRunner,
            asciiCaseInsensitive,
            wordRegexp,
            allowEndEmptyMatch: content.Length < line.Length);
    }

    private static bool HasMatchingLine(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (LineMatches(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData))
            {
                return true;
            }

            lineStart += lineLength;
        }

        return false;
    }

    private static bool SearchLineMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchSink
    {
        if (lineRegexp)
        {
            if (!TryFindFullLineLiteralMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out int matchStart, out int matchLength))
            {
                return false;
            }

            sink.Matched(lineNumber, lineStart + matchStart, matchStart + 1, line.Slice(matchStart, matchLength));
            return true;
        }

        if (wordRegexp)
        {
            return SearchWordMatches(LineContent(line, crlf, nullData), lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
        }

        return SearchLiteralMatches(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
    }

    private static bool SearchLineMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchSink
    {
        if (lineRegexp)
        {
            if (!TryFindPatternMatch(
                    line,
                    needles,
                    offset: 0,
                    asciiCaseInsensitive,
                    lineRegexp: true,
                    wordRegexp: false,
                    crlf,
                    nullData,
                    regexPlan,
                    ref findRunner,
                    out int matchStart,
                    out int matchLength))
            {
                return false;
            }

            sink.Matched(lineNumber, lineStart + matchStart, matchStart + 1, line.Slice(matchStart, matchLength));
            return true;
        }

        ReadOnlySpan<byte> content = RegexPatternContent(line, regexPlan, wordRegexp, crlf, nullData);
        return SearchPatternMatches(
            content,
            lineStart,
            lineNumber,
            needles,
            regexPlan,
            ref findRunner,
            ref sink,
            asciiCaseInsensitive,
            wordRegexp,
            allowEndEmptyMatch: content.Length < line.Length);
    }

    private static bool SearchLineMatchLines<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchLineSink
    {
        if (lineRegexp)
        {
            if (!TryFindFullLineLiteralMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out int matchStart, out int matchLength))
            {
                return false;
            }

            sink.MatchedLine(lineNumber, lineStart, lineStart + matchStart, matchStart + 1, line, line.Slice(matchStart, matchLength));
            return true;
        }

        if (wordRegexp)
        {
            return SearchWordMatchLines(LineContent(line, crlf, nullData), line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
        }

        return SearchLiteralMatchLines(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
    }

    private static bool SearchLineMatchLines<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchLineSink
    {
        if (lineRegexp)
        {
            if (!TryFindPatternMatch(
                    line,
                    needles,
                    offset: 0,
                    asciiCaseInsensitive,
                    lineRegexp: true,
                    wordRegexp: false,
                    crlf,
                    nullData,
                    regexPlan,
                    ref findRunner,
                    out int matchStart,
                    out int matchLength))
            {
                return false;
            }

            sink.MatchedLine(lineNumber, lineStart, lineStart + matchStart, matchStart + 1, line, line.Slice(matchStart, matchLength));
            return true;
        }

        ReadOnlySpan<byte> selectionContent = RegexPatternContent(
            line,
            regexPlan,
            wordRegexp,
            crlf,
            nullData);
        if (regexPlan?.Options.PreserveCrlfCarriageReturn == true &&
            !TryFindPatternMatch(
                selectionContent,
                needles,
                offset: 0,
                asciiCaseInsensitive,
                lineRegexp: false,
                wordRegexp,
                crlf: false,
                nullData: false,
                regexPlan,
                ref findRunner,
                out _,
                out _))
        {
            return false;
        }

        ReadOnlySpan<byte> reportingContent = RegexReportingContent(
            line,
            regexPlan,
            wordRegexp,
            crlf,
            nullData);
        bool reported = SearchPatternMatchLines(
            reportingContent,
            line,
            lineStart,
            lineNumber,
            needles,
            regexPlan,
            ref findRunner,
            ref sink,
            asciiCaseInsensitive,
            wordRegexp);
        return reported || regexPlan?.Options.PreserveCrlfCarriageReturn == true;
    }

    private static bool SearchLiteralMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchSink
    {
        if (needle.IsEmpty)
        {
            for (int index = 0; index < line.Length; index++)
            {
                sink.Matched(lineNumber, lineStart + index, index + 1, []);
            }

            return !line.IsEmpty;
        }

        bool matched = false;
        int offset = 0;
        while (offset <= line.Length)
        {
            int found = Find(line[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matched;
            }

            int start = offset + found;
            sink.Matched(lineNumber, lineStart + start, start + 1, line.Slice(start, needle.Length));
            matched = true;
            offset = start + needle.Length;
        }

        return matched;
    }

    private static bool SearchLiteralMatchLines<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchLineSink
    {
        if (needle.IsEmpty)
        {
            for (int index = 0; index < line.Length; index++)
            {
                sink.MatchedLine(lineNumber, lineStart, lineStart + index, index + 1, line, []);
            }

            return !line.IsEmpty;
        }

        bool matched = false;
        int offset = 0;
        while (offset <= line.Length)
        {
            int found = Find(line[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matched;
            }

            int start = offset + found;
            sink.MatchedLine(lineNumber, lineStart, lineStart + start, start + 1, line, line.Slice(start, needle.Length));
            matched = true;
            offset = start + needle.Length;
        }

        return matched;
    }

    private static bool SearchWordMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchSink
    {
        if (needle.IsEmpty)
        {
            bool matched = false;
            for (int index = 0; index <= line.Length; index++)
            {
                if (IsWordBoundary(line, index, index))
                {
                    sink.Matched(lineNumber, lineStart + index, index + 1, []);
                    matched = true;
                }
            }

            return matched;
        }

        bool foundMatch = false;
        int offset = 0;
        while (offset <= line.Length)
        {
            int found = FindWord(line[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return foundMatch;
            }

            int start = offset + found;
            sink.Matched(lineNumber, lineStart + start, start + 1, line.Slice(start, needle.Length));
            foundMatch = true;
            offset = start + needle.Length;
        }

        return foundMatch;
    }

    private static bool SearchWordMatchLines<TSink>(
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchLineSink
    {
        if (needle.IsEmpty)
        {
            bool matched = false;
            for (int index = 0; index <= content.Length; index++)
            {
                if (IsWordBoundary(content, index, index))
                {
                    sink.MatchedLine(lineNumber, lineStart, lineStart + index, index + 1, line, []);
                    matched = true;
                }
            }

            return matched;
        }

        bool foundMatch = false;
        int offset = 0;
        while (offset <= content.Length)
        {
            int found = FindWord(content[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return foundMatch;
            }

            int start = offset + found;
            sink.MatchedLine(lineNumber, lineStart, lineStart + start, start + 1, line, content.Slice(start, needle.Length));
            foundMatch = true;
            offset = start + needle.Length;
        }

        return foundMatch;
    }

    private static bool SearchPatternMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        bool allowEndEmptyMatch)
        where TSink : struct, IMatchSink
    {
        bool matched = false;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= line.Length)
        {
            if (!TryFindPatternMatch(
                    line,
                    needles,
                    offset,
                    asciiCaseInsensitive,
                    lineRegexp: false,
                    wordRegexp,
                    crlf: false,
                    nullData: false,
                    regexPlan,
                    ref findRunner,
                    out int start,
                    out int length))
            {
                return matched;
            }

            var match = new MatcherMatch(start, length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, line.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!allowEndEmptyMatch && IsLineEndEmptyMatch(line, start, length))
            {
                return matched;
            }

            sink.Matched(lineNumber, lineStart + start, start + 1, line.Slice(start, length));
            matched = true;
            offset = MatchIterator.AdvanceAfterReported(match, line.Length, ref suppressedEmptyStart);
            if (offset > line.Length)
            {
                return matched;
            }
        }

        return matched;
    }

    private static bool SearchPatternMatchLines<TSink>(
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool wordRegexp)
        where TSink : struct, IMatchLineSink
    {
        bool matched = false;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= content.Length)
        {
            if (!TryFindPatternMatch(
                    content,
                    needles,
                    offset,
                    asciiCaseInsensitive,
                    lineRegexp: false,
                    wordRegexp,
                    crlf: false,
                    nullData: false,
                    regexPlan,
                    ref findRunner,
                    out int start,
                    out int length))
            {
                return matched;
            }

            var match = new MatcherMatch(start, length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, content.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (content.Length == line.Length && IsLineEndEmptyMatch(content, start, length))
            {
                return matched;
            }

            sink.MatchedLine(lineNumber, lineStart, lineStart + start, start + 1, line, content.Slice(start, length));
            matched = true;
            offset = MatchIterator.AdvanceAfterReported(match, content.Length, ref suppressedEmptyStart);
            if (offset > content.Length)
            {
                return matched;
            }
        }

        return matched;
    }

    private static bool LineMatches(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        return TryMatchLine(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out _);
    }

    private static bool TryMatchLine(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out int matchStart)
    {
        bool matched;
        if (lineRegexp)
        {
            matched = TryFindFullLineLiteralMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out matchStart, out _);
        }
        else if (wordRegexp)
        {
            matchStart = FindWord(LineContent(line, crlf, nullData), needle, asciiCaseInsensitive);
            matched = matchStart >= 0;
        }
        else
        {
            matchStart = Find(line, needle, asciiCaseInsensitive);
            matched = matchStart >= 0;
        }

        bool lineMatched = matched != invertMatch;
        if (!lineMatched || invertMatch)
        {
            matchStart = -1;
        }

        return lineMatched;
    }

    private static bool TryMatchLine(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out int matchStart)
    {
        ReadOnlySpan<byte> selectionContent = lineRegexp
            ? LineContent(line, crlf, nullData)
            : RegexPatternContent(line, regexPlan, wordRegexp, crlf, nullData);
        bool matched = TryFindPatternMatch(
            selectionContent,
            needles,
            offset: 0,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            regexPlan,
            ref findRunner,
            out matchStart,
            out _);

        if (matched &&
            !invertMatch &&
            !lineRegexp &&
            regexPlan?.Options.PreserveCrlfCarriageReturn == true)
        {
            ReadOnlySpan<byte> reportingContent = RegexReportingContent(
                line,
                regexPlan,
                wordRegexp,
                crlf,
                nullData);
            if (!TryFindPatternMatch(
                    reportingContent,
                    needles,
                    offset: 0,
                    asciiCaseInsensitive,
                    lineRegexp: false,
                    wordRegexp,
                    crlf: false,
                    nullData: false,
                    regexPlan,
                    ref findRunner,
                    out matchStart,
                    out _))
            {
                matchStart = -1;
            }
        }

        bool lineMatched = matched != invertMatch;
        if (!lineMatched || invertMatch)
        {
            matchStart = -1;
        }

        return lineMatched;
    }

    private static bool TryFindFullLineLiteralMatch(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool crlf,
        bool nullData,
        out int matchStart,
        out int matchLength)
    {
        ReadOnlySpan<byte> content = LineContent(line, crlf, nullData);
        bool matched = content.Length == needle.Length &&
            (!asciiCaseInsensitive
                ? content.SequenceEqual(needle)
                : EqualsAsciiIgnoreCase(content, needle));
        matchStart = matched ? 0 : -1;
        matchLength = matched ? content.Length : 0;
        return matched;
    }

    private static long CountLiteralMatchesByLine(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        ulong maxMatchingLines,
        bool nullData)
    {
        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            long lineMatches = CountLiteralMatches(line, needle, asciiCaseInsensitive);
            if (lineMatches > 0)
            {
                count += lineMatches;
                matchedLines++;
                if (matchedLines >= maxMatchingLines)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    private static long CountLiteralMatches(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            return haystack.Length;
        }

        long count = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return count;
            }

            count++;
            offset += found + needle.Length;
        }

        return count;
    }

    private static long CountLiteralMatchesAfterColumn(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ulong column,
        bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            long emptyCount = 0;
            for (int index = 0; index < haystack.Length; index++)
            {
                if (IsMatchStartAfterColumn(index, column))
                {
                    emptyCount++;
                }
            }

            return emptyCount;
        }

        long count = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return count;
            }

            int start = offset + found;
            if (IsMatchStartAfterColumn(start, column))
            {
                count++;
            }

            offset = start + needle.Length;
        }

        return count;
    }

    private static long CountWordMatchesByLine(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool crlf,
        bool nullData)
    {
        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = LineContent(remaining[..lineLength], crlf, nullData);
            long lineMatches = CountWordMatches(line, needle, asciiCaseInsensitive);
            if (lineMatches > 0)
            {
                count += lineMatches;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    private static long CountWordMatches(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            long count = 0;
            for (int index = 0; index <= haystack.Length; index++)
            {
                if (IsWordBoundary(haystack, index, index))
                {
                    count++;
                }
            }

            return count;
        }

        long matches = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = FindWord(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matches;
            }

            matches++;
            offset += found + needle.Length;
        }

        return matches;
    }

    private static long CountWordMatchesAfterColumn(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ulong column,
        bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            long count = 0;
            for (int index = 0; index <= haystack.Length; index++)
            {
                if (IsWordBoundary(haystack, index, index) && IsMatchStartAfterColumn(index, column))
                {
                    count++;
                }
            }

            return count;
        }

        long matches = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = FindWord(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matches;
            }

            int start = offset + found;
            if (IsMatchStartAfterColumn(start, column))
            {
                matches++;
            }

            offset = start + needle.Length;
        }

        return matches;
    }

    private static long CountPatternMatchesByLine(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        ulong? maxMatchingLines,
        bool crlf,
        bool nullData)
    {
        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            while (lineStart < haystack.Length)
            {
                ReadOnlySpan<byte> remaining = haystack[lineStart..];
                int lineLength = GetLineLength(remaining, nullData);
                ReadOnlySpan<byte> record = remaining[..lineLength];
                ReadOnlySpan<byte> line = RegexPatternContent(
                    record,
                    regexPlan,
                    wordRegexp,
                    crlf,
                    nullData);
                long lineMatches = CountPatternMatches(
                    line,
                    needles,
                    regexPlan,
                    ref findRunner,
                    asciiCaseInsensitive,
                    wordRegexp,
                    allowEndEmptyMatch: line.Length < record.Length);
                if (lineMatches > 0)
                {
                    count += lineMatches;
                    matchedLines++;
                    if (maxMatchingLines is ulong limit && matchedLines >= limit)
                    {
                        return count;
                    }
                }

                lineStart += lineLength;
            }
        }
        finally
        {
            findRunner.Dispose();
        }

        return count;
    }

    private static long CountPatternMatches(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        bool allowEndEmptyMatch)
    {
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            return CountPatternMatches(
                haystack,
                needles,
                regexPlan,
                ref findRunner,
                asciiCaseInsensitive,
                wordRegexp,
                allowEndEmptyMatch);
        }
        finally
        {
            findRunner.Dispose();
        }
    }

    private static long CountPatternMatches(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        bool allowEndEmptyMatch)
    {
        long count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= haystack.Length)
        {
            if (!TryFindPatternMatch(
                    haystack,
                    needles,
                    offset,
                    asciiCaseInsensitive,
                    lineRegexp: false,
                    wordRegexp,
                    crlf: false,
                    nullData: false,
                    regexPlan,
                    ref findRunner,
                    out int start,
                    out int length))
            {
                return count;
            }

            var match = new MatcherMatch(start, length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, haystack.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!allowEndEmptyMatch && IsLineEndEmptyMatch(haystack, start, length))
            {
                return count;
            }

            count++;
            offset = MatchIterator.AdvanceAfterReported(match, haystack.Length, ref suppressedEmptyStart);
            if (offset > haystack.Length)
            {
                return count;
            }
        }

        return count;
    }

    private static long CountPatternMatchesAfterColumn(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ulong column,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        bool allowEndEmptyMatch)
    {
        RegexFindRunner findRunner = regexPlan is null
            ? default
            : regexPlan.Matcher.RentFindRunner();
        try
        {
            long count = 0;
            int offset = 0;
            int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
            while (offset <= haystack.Length)
            {
                if (!TryFindPatternMatch(
                        haystack,
                        needles,
                        offset,
                        asciiCaseInsensitive,
                        lineRegexp: false,
                        wordRegexp,
                        crlf: false,
                        nullData: false,
                        regexPlan,
                        ref findRunner,
                        out int start,
                        out int length))
                {
                    return count;
                }

                var match = new MatcherMatch(start, length);
                if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
                {
                    offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, haystack.Length);
                    suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                    continue;
                }

                if (!allowEndEmptyMatch && IsLineEndEmptyMatch(haystack, start, length))
                {
                    return count;
                }

                if (IsMatchStartAfterColumn(start, column))
                {
                    count++;
                }

                offset = MatchIterator.AdvanceAfterReported(match, haystack.Length, ref suppressedEmptyStart);
                if (offset > haystack.Length)
                {
                    return count;
                }
            }

            return count;
        }
        finally
        {
            findRunner.Dispose();
        }
    }

    private static bool IsMatchStartAfterColumn(int start, ulong column)
    {
        return (ulong)start >= column;
    }

    private static bool TryFindPatternMatch(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        int offset,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out int matchStart,
        out int matchLength)
    {
        return TryFindPatternMatch(
            haystack,
            needles,
            offset,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            regexPlan: null,
            out matchStart,
            out matchLength);
    }

    private static bool TryFindPatternMatch(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        int offset,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        RegexSearchPlan? regexPlan,
        out int matchStart,
        out int matchLength)
    {
        RegexFindRunner findRunner = default;
        return TryFindPatternMatch(
            haystack,
            needles,
            offset,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            regexPlan,
            ref findRunner,
            out matchStart,
            out matchLength);
    }

    private static bool TryFindPatternMatch(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        int offset,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        RegexSearchPlan? regexPlan,
        ref RegexFindRunner findRunner,
        out int matchStart,
        out int matchLength)
    {
        matchStart = -1;
        matchLength = 0;
        if (needles.Count == 0)
        {
            return false;
        }

        if (offset > haystack.Length)
        {
            return false;
        }

        if (lineRegexp)
        {
            haystack = LineContent(haystack, crlf, nullData);
        }

        regexPlan ??= CreateRegexSearchPlan(
            needles,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (regexPlan is not null &&
            haystack.Length - offset < regexPlan.MinimumMatchLength)
        {
            return false;
        }

        RegexMatch? match = regexPlan is null
            ? null
            : findRunner.IsInitialized
                ? findRunner.Find(haystack, offset)
                : regexPlan.Matcher.Find(haystack, offset);
        if (!match.HasValue)
        {
            return false;
        }

        matchStart = match.Value.Start;
        matchLength = match.Value.Length;
        return true;
    }

    private static RegexSearchPlan? CreateRegexSearchPlan(
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        return RegexSearchPlan.Create(
            needles,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData));
    }

    private static RegexSearchPlan? EnsureRegexSearchPlan(
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        return regexPlan?.IsCompatible(
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            multiline: false,
            multilineDotall: false) == true
            ? regexPlan
            : CreateRegexSearchPlan(
                needles,
                asciiCaseInsensitive,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData);
    }

    private static int FindWord(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            for (int index = 0; index <= haystack.Length; index++)
            {
                if (IsWordBoundary(haystack, index, index))
                {
                    return index;
                }
            }

            return -1;
        }

        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return -1;
            }

            int start = offset + found;
            int end = start + needle.Length;
            if (IsWordBoundary(haystack, start, end))
            {
                return start;
            }

            offset = start + 1;
        }

        return -1;
    }

    private static bool IsWordBoundary(ReadOnlySpan<byte> haystack, int start, int end)
    {
        bool leftIsWord = start > 0 && IsAsciiWordByte(haystack[start - 1]);
        bool rightIsWord = end < haystack.Length && IsAsciiWordByte(haystack[end]);
        return !leftIsWord && !rightIsWord;
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_'
            || (value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z');
    }

    private static ReadOnlySpan<byte> LineContent(ReadOnlySpan<byte> line, bool crlf, bool nullData)
    {
        if (!line.IsEmpty && line[^1] == GetLineTerminator(nullData))
        {
            line = line[..^1];
            if (!nullData && crlf && !line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }
        }

        return line;
    }

    private static ReadOnlySpan<byte> PatternContent(ReadOnlySpan<byte> line, bool wordRegexp, bool crlf, bool nullData)
    {
        return wordRegexp || crlf || nullData ? LineContent(line, crlf, nullData) : line;
    }

    private static ReadOnlySpan<byte> RegexPatternContent(
        ReadOnlySpan<byte> line,
        RegexSearchPlan? regexPlan,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        if (regexPlan?.Options.PreserveCrlfCarriageReturn == true)
        {
            return line;
        }

        return regexPlan?.HasHaystackAnchors == true
            ? LineContent(line, crlf, nullData)
            : PatternContent(line, wordRegexp, crlf, nullData);
    }

    private static ReadOnlySpan<byte> RegexReportingContent(
        ReadOnlySpan<byte> line,
        RegexSearchPlan? regexPlan,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        return regexPlan?.Options.PreserveCrlfCarriageReturn == true
            ? LineContent(line, crlf, nullData)
            : RegexPatternContent(line, regexPlan, wordRegexp, crlf, nullData);
    }

    private static int GetLineLength(ReadOnlySpan<byte> remaining, bool nullData)
    {
        int terminator = remaining.IndexOf(GetLineTerminator(nullData));
        return terminator < 0 ? remaining.Length : terminator + 1;
    }

    private static long CountSearchLines(ReadOnlySpan<byte> bytes)
    {
        return CountSearchLines(bytes, nullData: false);
    }

    private static long CountSearchLines(ReadOnlySpan<byte> bytes, bool nullData)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        byte terminator = GetLineTerminator(nullData);
        long terminators = ByteCounter.Count(bytes, terminator);
        return bytes[^1] == terminator ? terminators : terminators + 1;
    }

    private static byte GetLineTerminator(bool nullData)
    {
        return nullData ? (byte)0 : (byte)'\n';
    }

    internal static int Find(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (!asciiCaseInsensitive)
        {
            return MemmemSearch.Find(haystack, needle);
        }

        if (needle.IsEmpty)
        {
            return 0;
        }

        if (needle.Length > haystack.Length)
        {
            return -1;
        }

        if (ContainsNonAscii(needle) && TryFindUtf8IgnoreCase(haystack, needle, out int utf8Start, out _))
        {
            return utf8Start;
        }

        int lastStart = haystack.Length - needle.Length;
        for (int index = 0; index <= lastStart; index++)
        {
            if (EqualsAsciiIgnoreCase(haystack.Slice(index, needle.Length), needle))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (int index = 0; index < left.Length; index++)
        {
            if (FoldAscii(left[index]) != FoldAscii(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLineEndEmptyMatch(ReadOnlySpan<byte> line, int start, int length)
    {
        return line.Length > 0 && length == 0 && start == line.Length;
    }

    private static bool ContainsNonAscii(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] >= 0x80)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsLiteralRegex(ReadOnlySpan<byte> pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] is (byte)'\\'
                or (byte)'.'
                or (byte)'['
                or (byte)']'
                or (byte)'('
                or (byte)')'
                or (byte)'{'
                or (byte)'}'
                or (byte)'*'
                or (byte)'+'
                or (byte)'?'
                or (byte)'|'
                or (byte)'^'
                or (byte)'$')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindUtf8IgnoreCase(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, out int start, out int length)
    {
        start = -1;
        length = 0;
        try
        {
            string haystackText = s_strictUtf8.GetString(haystack);
            string needleText = s_strictUtf8.GetString(needle);
            int charStart = haystackText.IndexOf(needleText, StringComparison.OrdinalIgnoreCase);
            if (charStart < 0)
            {
                return false;
            }

            start = s_strictUtf8.GetByteCount(haystackText.AsSpan(0, charStart));
            length = s_strictUtf8.GetByteCount(haystackText.AsSpan(charStart, needleText.Length));
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }
}
