
namespace Scout;

/// <summary>
/// Builds line match state and writes context-aware search output.
/// </summary>
internal static class ContextSearchOperations
{
    internal static bool SearchBytes(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        RegexSearchPlan regexPlan)
    {
        ContextSearchResult searchResult = BuildSearchResult(
            bytes,
            pattern,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            separators.Crlf,
            separators.NullData,
            stopOnNonmatch,
            regexPlan);
        return WriteSearchResult(
            bytes,
            searchResult,
            output,
            prefix,
            separators,
            lineLimit,
            color,
            lineNumber,
            column,
            byteOffset,
            invertMatch,
            vimgrep,
            onlyMatching,
            replacement,
            maxCount,
            trim,
            beforeContext,
            afterContext,
            passthru,
            nullPathTerminator,
            regexPlan);
    }

    /// <summary>
    /// Writes a prebuilt context result by replaying its retained authoritative spans.
    /// </summary>
    /// <param name="bytes">The complete input bytes.</param>
    /// <param name="searchResult">The prebuilt context line and match state.</param>
    /// <param name="output">The output writer.</param>
    /// <param name="prefix">The optional output path prefix.</param>
    /// <param name="separators">The configured output separators.</param>
    /// <param name="lineLimit">The output line-length policy.</param>
    /// <param name="color">The configured output colors.</param>
    /// <param name="lineNumber">Whether to write line numbers.</param>
    /// <param name="column">Whether to write match columns.</param>
    /// <param name="byteOffset">Whether to write byte offsets.</param>
    /// <param name="invertMatch">Whether non-matching lines are selected.</param>
    /// <param name="vimgrep">Whether to write vimgrep records.</param>
    /// <param name="onlyMatching">Whether to write only retained match spans.</param>
    /// <param name="replacement">The optional replacement template.</param>
    /// <param name="maxCount">The optional maximum number of primary matching lines.</param>
    /// <param name="trim">Whether to trim leading ASCII whitespace.</param>
    /// <param name="beforeContext">The number of preceding context lines.</param>
    /// <param name="afterContext">The number of following context lines.</param>
    /// <param name="passthru">Whether to write every searched line.</param>
    /// <param name="nullPathTerminator">Whether path prefixes use a NUL terminator.</param>
    /// <param name="regexPlan">The authoritative plan used for capture replay.</param>
    /// <returns><see langword="true" /> when the result contains a selected match.</returns>
    internal static bool WriteSearchResult(
        byte[] bytes,
        ContextSearchResult searchResult,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool invertMatch,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator,
        RegexSearchPlan regexPlan)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        List<ContextLineInfo> lines = searchResult.Lines;
        if (lines.Count == 0)
        {
            return false;
        }

        bool[] included = new bool[lines.Count];
        bool matched = IncludeOutputLines(
            lines,
            included,
            beforeContext,
            afterContext,
            passthru,
            maxCount);
        bool passthruRequiresReportedMatch = passthru &&
            regexPlan.Options.PreserveCrlfCarriageReturn;
        if (passthruRequiresReportedMatch)
        {
            matched = false;
            for (int index = 0; index < lines.Count; index++)
            {
                matched |= lines[index].SelectedMatch && lines[index].MatchColumn > 0;
            }
        }

        var lineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int previousLineIndex = -1;
        ulong passthruPrimaryMatches = 0;
        bool wrote = false;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            ContextLineInfo line = lines[index];
            bool selectedMatch = line.SelectedMatch;
            if (passthruRequiresReportedMatch && line.MatchColumn <= 0)
            {
                selectedMatch = false;
            }

            if (passthru && selectedMatch && maxCount is ulong limit)
            {
                if (passthruPrimaryMatches >= limit)
                {
                    selectedMatch = false;
                }
                else
                {
                    passthruPrimaryMatches++;
                }
            }

            if (!passthru &&
                (beforeContext > 0 || afterContext > 0) &&
                wrote &&
                index > previousLineIndex + 1)
            {
                if (separators.ContextEnabled)
                {
                    output.Write(separators.Context.Span);
                    output.Write(separators.LineTerminator.Span);
                }
            }

            WriteContextOutputLine(
                bytes,
                line,
                searchResult.GetMatches(line),
                selectedMatch,
                output,
                prefix,
                lineNumber,
                column,
                byteOffset,
                trim,
                separators,
                lineLimit,
                color,
                vimgrep,
                onlyMatching,
                replacement,
                invertMatch,
                nullPathTerminator,
                regexPlan,
                ref lineSink);
            previousLineIndex = index;
            wrote = true;
        }

        return matched;
    }

    /// <summary>
    /// Builds context line state and retains every authoritative match span in one traversal.
    /// </summary>
    /// <param name="bytes">The complete input bytes.</param>
    /// <param name="pattern">The ordered regex patterns.</param>
    /// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
    /// <param name="invertMatch">Whether non-matching lines are selected.</param>
    /// <param name="lineRegexp">Whether patterns must match complete lines.</param>
    /// <param name="wordRegexp">Whether matches require word boundaries.</param>
    /// <param name="crlf">Whether CRLF terminates records.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <param name="stopOnNonmatch">Whether collection stops after the first non-match following a selection.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <returns>The complete context line and retained-span state.</returns>
    internal static ContextSearchResult BuildSearchResult(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        bool stopOnNonmatch,
        RegexSearchPlan regexPlan)
    {
        if (stopOnNonmatch)
        {
            return BuildStoppedSearchResult(
                bytes,
                pattern,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData,
                regexPlan);
        }

        var selectedLineStarts = new List<int>();
        var selectedLineMatchRanges = new List<ContextLineMatchRange>();
        var matches = new List<ContextLineMatch>();
        var sink = new ContextMatchCollector(
            selectedLineStarts,
            selectedLineMatchRanges,
            matches);
        LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            bytes,
            pattern,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            maxMatchingLines: null,
            crlf,
            nullData);

        var lines = new List<ContextLineInfo>();
        var lineMatchRanges = new List<ContextLineMatchRange>();
        int selectedLineIndex = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = GetLineLength(bytes.AsSpan(lineStart), nullData);
            bool originalMatch = selectedLineIndex < selectedLineStarts.Count &&
                selectedLineStarts[selectedLineIndex] == lineStart;
            ContextLineMatchRange matchRange = originalMatch
                ? selectedLineMatchRanges[selectedLineIndex++]
                : new ContextLineMatchRange(
                    selectedLineIndex < selectedLineMatchRanges.Count
                        ? selectedLineMatchRanges[selectedLineIndex].Start
                        : matches.Count,
                    count: 0);
            long originalColumn = matchRange.Count > 0
                ? matches[matchRange.Start].Column
                : 0;
            bool selectedMatch = invertMatch ? !originalMatch : originalMatch;
            long matchColumn = invertMatch ? 0 : originalColumn;
            lines.Add(new ContextLineInfo(
                lineStart,
                lineLength,
                lineNumber,
                selectedMatch,
                matchColumn,
                originalMatch,
                originalColumn));
            lineMatchRanges.Add(matchRange);
            lineStart += lineLength;
            lineNumber++;
        }

        return new ContextSearchResult(
            lines,
            lineMatchRanges.ToArray(),
            matches.ToArray());
    }

    /// <summary>
    /// Builds bounded context state in one record traversal and retains authoritative spans.
    /// </summary>
    /// <param name="bytes">The complete input bytes.</param>
    /// <param name="pattern">The ordered regex patterns.</param>
    /// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
    /// <param name="invertMatch">Whether non-matching records are selected.</param>
    /// <param name="lineRegexp">Whether patterns must match complete records.</param>
    /// <param name="wordRegexp">Whether matches require word boundaries.</param>
    /// <param name="crlf">Whether CRLF terminates records.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <returns>The bounded context line and retained-span state.</returns>
    private static ContextSearchResult BuildStoppedSearchResult(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        RegexSearchPlan regexPlan)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        LiteralLineSearcher.ValidateRegexSearchPlan(
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);

        var selectedLineStarts = new List<int>();
        var selectedLineMatchRanges = new List<ContextLineMatchRange>();
        var matches = new List<ContextLineMatch>();
        var sink = new ContextMatchCollector(
            selectedLineStarts,
            selectedLineMatchRanges,
            matches);
        var lines = new List<ContextLineInfo>();
        var lineMatchRanges = new List<ContextLineMatchRange>();
        bool hasSelectedMatch = false;
        int lineStart = 0;
        long lineNumber = 1;
        RegexFindRunner findRunner = regexPlan.Matcher.RentFindRunner();
        try
        {
            while (lineStart < bytes.Length)
            {
                int lineLength = GetLineLength(bytes.AsSpan(lineStart), nullData);
                ReadOnlySpan<byte> line = bytes.AsSpan(lineStart, lineLength);
                bool originalMatch =
                    LiteralLineSearcher.SearchRecordMatchLinesWithRegexPlan(
                        bytes,
                        line,
                        lineStart,
                        lineNumber,
                        pattern,
                        regexPlan,
                        ref findRunner,
                        ref sink,
                        asciiCaseInsensitive,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        nullData);
                ContextLineMatchRange matchRange = originalMatch
                    ? selectedLineMatchRanges[^1]
                    : new ContextLineMatchRange(matches.Count, count: 0);
                long originalColumn = matchRange.Count > 0
                    ? matches[matchRange.Start].Column
                    : 0;
                bool selectedMatch = invertMatch ? !originalMatch : originalMatch;
                long matchColumn = invertMatch ? 0 : originalColumn;
                lines.Add(new ContextLineInfo(
                    lineStart,
                    lineLength,
                    lineNumber,
                    selectedMatch,
                    matchColumn,
                    originalMatch,
                    originalColumn));
                lineMatchRanges.Add(matchRange);
                if (hasSelectedMatch && !selectedMatch)
                {
                    break;
                }

                hasSelectedMatch |= selectedMatch;
                lineStart += lineLength;
                lineNumber++;
            }
        }
        finally
        {
            findRunner.Dispose();
        }

        return new ContextSearchResult(
            lines,
            lineMatchRanges.ToArray(),
            matches.ToArray());
    }

    internal static bool IncludePassthruLines(List<ContextLineInfo> lines, bool[] included)
    {
        bool matched = false;
        for (int index = 0; index < lines.Count; index++)
        {
            included[index] = true;
            matched |= lines[index].SelectedMatch;
        }

        return matched;
    }

    /// <summary>
    /// Marks the exact physical lines emitted by context or passthru replay.
    /// </summary>
    /// <param name="lines">The physical line metadata.</param>
    /// <param name="included">The destination inclusion map.</param>
    /// <param name="beforeContext">The number of preceding context lines.</param>
    /// <param name="afterContext">The number of following context lines.</param>
    /// <param name="passthru">Whether every searched line is emitted.</param>
    /// <param name="maxCount">The optional maximum number of primary matching lines.</param>
    /// <returns><see langword="true" /> when the lines contain a selected match.</returns>
    internal static bool IncludeOutputLines(
        List<ContextLineInfo> lines,
        bool[] included,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        ulong? maxCount)
    {
        return passthru
            ? IncludePassthruLines(lines, included)
            : IncludeContextLines(
                lines,
                included,
                beforeContext,
                afterContext,
                maxCount);
    }

    internal static bool IncludeContextLines(
        List<ContextLineInfo> lines,
        bool[] included,
        ulong beforeContext,
        ulong afterContext,
        ulong? maxCount)
    {
        bool matched = false;
        ulong primaryMatches = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!lines[index].SelectedMatch)
            {
                continue;
            }

            matched = true;
            if (maxCount is ulong limit && primaryMatches >= limit)
            {
                continue;
            }

            primaryMatches++;
            IncludeContextRange(included, index, beforeContext, afterContext);
        }

        return matched;
    }

    internal static bool TryFindLineMatch(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out long matchColumn,
        RegexSearchPlan regexPlan)
    {
        var sink = new FirstLineMatchSink();
        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            line,
            pattern,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines: 1,
            crlf,
            nullData);
        matchColumn = matched ? sink.MatchColumn : 0;
        return matched;
    }

    internal static int GetLineLength(ReadOnlySpan<byte> remaining, bool nullData)
    {
        int terminator = remaining.IndexOf(nullData ? (byte)0 : (byte)'\n');
        return terminator < 0 ? remaining.Length : terminator + 1;
    }

    private static void IncludeContextRange(bool[] included, int matchIndex, ulong beforeContext, ulong afterContext)
    {
        int start = beforeContext > (ulong)matchIndex ? 0 : matchIndex - (int)beforeContext;
        int remainingAfter = included.Length - matchIndex - 1;
        int end = afterContext > (ulong)remainingAfter ? included.Length - 1 : matchIndex + (int)afterContext;
        for (int index = start; index <= end; index++)
        {
            included[index] = true;
        }
    }

    /// <summary>
    /// Writes one retained physical line without invoking the matcher again.
    /// </summary>
    /// <param name="bytes">The complete input bytes used for output.</param>
    /// <param name="line">The retained physical line metadata.</param>
    /// <param name="matches">The authoritative match spans retained for the line.</param>
    /// <param name="selectedMatch">Whether the line is emitted as a selected match.</param>
    /// <param name="output">The output writer.</param>
    /// <param name="prefix">The optional output path prefix.</param>
    /// <param name="lineNumber">Whether to write line numbers.</param>
    /// <param name="column">Whether to write match columns.</param>
    /// <param name="byteOffset">Whether to write byte offsets.</param>
    /// <param name="trim">Whether to trim leading ASCII whitespace.</param>
    /// <param name="separators">The configured output separators.</param>
    /// <param name="lineLimit">The output line-length policy.</param>
    /// <param name="color">The configured output colors.</param>
    /// <param name="vimgrep">Whether to write vimgrep records.</param>
    /// <param name="onlyMatching">Whether to write only retained match spans.</param>
    /// <param name="replacement">The optional replacement template.</param>
    /// <param name="invertMatch">Whether non-matching lines are selected.</param>
    /// <param name="nullPathTerminator">Whether path prefixes use a NUL terminator.</param>
    /// <param name="regexPlan">The authoritative plan used for capture replay.</param>
    /// <param name="lineSink">The standard line writer used for plain output.</param>
    internal static void WriteContextOutputLine(
        ReadOnlySpan<byte> bytes,
        ContextLineInfo line,
        ReadOnlySpan<ContextLineMatch> matches,
        bool selectedMatch,
        RawByteWriter output,
        OutputPath? prefix,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        bool invertMatch,
        bool nullPathTerminator,
        RegexSearchPlan regexPlan,
        ref StandardSearchSink lineSink)
    {
        ReadOnlySpan<byte> lineBytes = bytes.Slice(line.Start, line.Length);
        if (selectedMatch)
        {
            if (onlyMatching && !invertMatch)
            {
                WriteOnlyMatchesForContextLine(
                    lineBytes,
                    line,
                    matches,
                    output,
                    prefix,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    separators.FieldMatch,
                    replacement,
                    nullPathTerminator,
                    color,
                    separators.LineTerminator,
                    regexPlan);
                return;
            }

            if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
            {
                var replacementLineSink = new ReplacementLineSink(
                    output,
                    prefix,
                    separators.FieldMatch,
                    replacementValue,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    vimgrep,
                    lineLimit,
                    line.LineNumber - 1,
                    line.Start,
                    color,
                    separators.LineTerminator,
                    regexPlan);
                ReplayMatches(lineBytes, matches, ref replacementLineSink);
                replacementLineSink.Flush();
                return;
            }

            if (vimgrep && !invertMatch)
            {
                VimgrepSink vimgrepSink = CreateContextVimgrepSink(
                    output,
                    prefix,
                    separators.FieldMatch,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    lineLimit,
                    color,
                    separators.LineTerminator,
                    line);
                ReplayMatches(lineBytes, matches, ref vimgrepSink);
                return;
            }

            if (color.Enabled && !invertMatch)
            {
                var coloredSink = new ColoredSearchSink(
                    output,
                    prefix,
                    separators.FieldMatch,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    lineLimit,
                    color,
                    separators.LineTerminator,
                    line.LineNumber - 1,
                    line.Start);
                ReplayMatches(lineBytes, matches, ref coloredSink);
                coloredSink.Flush();
                return;
            }

            lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, lineBytes);
            return;
        }

        if (invertMatch && line.OriginalMatch)
        {
            if (onlyMatching)
            {
                WriteOnlyMatchesForContextLine(
                    lineBytes,
                    line,
                    matches,
                    output,
                    prefix,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    separators.FieldContext,
                    replacement,
                    nullPathTerminator,
                    color,
                    separators.LineTerminator,
                    regexPlan);
                return;
            }

            if (replacement is ReadOnlyMemory<byte> replacementValue)
            {
                var replacementLineSink = new ReplacementLineSink(
                    output,
                    prefix,
                    separators.FieldContext,
                    replacementValue,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    vimgrep,
                    lineLimit,
                    line.LineNumber - 1,
                    line.Start,
                    color,
                    separators.LineTerminator,
                    regexPlan);
                ReplayMatches(lineBytes, matches, ref replacementLineSink);
                replacementLineSink.Flush();
                return;
            }

            if (vimgrep)
            {
                VimgrepSink vimgrepSink = CreateContextVimgrepSink(
                    output,
                    prefix,
                    separators.FieldContext,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    lineLimit,
                    color,
                    separators.LineTerminator,
                    line);
                ReplayMatches(lineBytes, matches, ref vimgrepSink);
                return;
            }

            if (color.Enabled)
            {
                var coloredSink = new ColoredSearchSink(
                    output,
                    prefix,
                    separators.FieldContext,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    lineLimit,
                    color,
                    separators.LineTerminator,
                    line.LineNumber - 1,
                    line.Start);
                ReplayMatches(lineBytes, matches, ref coloredSink);
                coloredSink.Flush();
                return;
            }
        }

        lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, lineBytes);
    }

    private static void WriteOnlyMatchesForContextLine(
        ReadOnlySpan<byte> lineBytes,
        ContextLineInfo line,
        ReadOnlySpan<ContextLineMatch> matches,
        RawByteWriter output,
        OutputPath? prefix,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        ReadOnlyMemory<byte> fieldSeparator,
        ReadOnlyMemory<byte>? replacement,
        bool nullPathTerminator,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator,
        RegexSearchPlan regexPlan)
    {
        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            var replacementMatchSink = new ReplacementMatchSink(
                output,
                prefix,
                fieldSeparator,
                replacementValue,
                lineNumber,
                column,
                byteOffset,
                nullPathTerminator,
                line.LineNumber - 1,
                line.Start,
                color,
                lineTerminator,
                regexPlan);
            ReplayMatches(lineBytes, matches, ref replacementMatchSink);
        }
        else
        {
            var matchSink = new StandardMatchSink(
                output,
                prefix,
                fieldSeparator,
                lineNumber,
                column,
                byteOffset,
                trim,
                line.LineNumber - 1,
                line.Start,
                nullPathTerminator,
                color,
                lineTerminator);
            ReplayMatches(lineBytes, matches, ref matchSink);
        }
    }

    private static VimgrepSink CreateContextVimgrepSink(
        RawByteWriter output,
        OutputPath? prefix,
        ReadOnlyMemory<byte> fieldSeparator,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        OutputLineLimit lineLimit,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator,
        ContextLineInfo line)
    {
        return new VimgrepSink(
            output,
            prefix,
            fieldSeparator,
            lineNumber,
            column,
            byteOffset,
            onlyMatching: false,
            trim,
            nullPathTerminator,
            lineLimit,
            color,
            lineTerminator,
            line.LineNumber - 1,
            line.Start);
    }

    private static void ReplayMatches<TSink>(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<ContextLineMatch> matches,
        ref TSink sink)
        where TSink : struct, IMatchLineSink
    {
        for (int index = 0; index < matches.Length; index++)
        {
            ContextLineMatch match = matches[index];
            if (IsSelectionOnlyRecordEndMatch(line, match))
            {
                continue;
            }

            sink.MatchedLine(
                lineNumber: 1,
                lineByteOffset: 0,
                matchByteOffset: match.Start,
                match.Column,
                line,
                line.Slice(match.Start, match.Length));
        }

        sink.FinishLine(
            lineNumber: 1,
            lineByteOffset: 0,
            line);
    }

    /// <summary>
    /// Determines whether an empty match at the end of an unterminated record only selects that record.
    /// </summary>
    /// <param name="line">The complete record bytes, including a terminator when present.</param>
    /// <param name="match">The retained authoritative match.</param>
    /// <returns><see langword="true" /> when the match selects the record without adding a reportable span.</returns>
    internal static bool IsSelectionOnlyRecordEndMatch(
        ReadOnlySpan<byte> line,
        ContextLineMatch match)
    {
        return match.Length == 0 && match.Start == line.Length;
    }
}
