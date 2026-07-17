namespace Scout;

/// <summary>
/// Implements multiline searching with one authoritative matcher for the ordered pattern set.
/// </summary>
internal static class MultilineSearchOperations
{
    internal static bool TrySearchBytes(
        ReadOnlySpan<byte> searchSpan,
        ReadOnlySpan<byte> outputSpan,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        RegexSearchPlan plan,
        out bool matched)
    {
        if (separators.NullData)
        {
            matched = false;
            return false;
        }

        MultilineSearchResult searchResult = CreateSearchResult(searchSpan, plan);
        return TryWriteSearchResult(
            searchSpan,
            outputSpan,
            patterns,
            output,
            prefix,
            separators,
            lineLimit,
            color,
            searchMode,
            vimgrep,
            lineNumber,
            column,
            byteOffset,
            asciiCaseInsensitive,
            invertMatch,
            onlyMatching,
            replacement,
            maxCount,
            quiet,
            trim,
            beforeContext,
            afterContext,
            passthru,
            includeZero,
            nullPathTerminator,
            plan,
            searchResult,
            out matched);
    }

    /// <summary>
    /// Executes one authoritative multiline traversal and retains its ordered matches.
    /// </summary>
    /// <param name="bytes">The bytes to search.</param>
    /// <param name="plan">The operation-scoped multiline plan.</param>
    /// <returns>The retained search result.</returns>
    internal static MultilineSearchResult CreateSearchResult(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan plan)
    {
        return new MultilineSearchResult(CollectMultilineMatches(bytes, plan));
    }

    /// <summary>
    /// Limits positive multiline discovery hits and retains every replay match in their coalesced line blocks.
    /// </summary>
    /// <param name="bytes">The complete searched bytes.</param>
    /// <param name="searchResult">The complete authoritative match result.</param>
    /// <param name="maxCount">The optional maximum number of discovery hits.</param>
    /// <returns>The authoritative matches replayed from the retained physical-line blocks.</returns>
    internal static MultilineSearchResult LimitPositiveMatches(
        ReadOnlySpan<byte> bytes,
        MultilineSearchResult searchResult,
        ulong? maxCount)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        if (maxCount is null || (ulong)searchResult.Matches.Count <= maxCount.Value)
        {
            return searchResult;
        }

        if (maxCount == 0)
        {
            return new MultilineSearchResult([]);
        }

        var blockStarts = new List<int>();
        var blockEnds = new List<int>();
        int discoveryCount = checked((int)Math.Min(
            maxCount.Value,
            (ulong)searchResult.Matches.Count));
        for (int index = 0; index < discoveryCount; index++)
        {
            RegexMatch match = searchResult.Matches[index];
            int blockStart = GetLineStart(bytes, match.Start);
            int blockEnd = GetLineEnd(bytes, GetInclusiveMatchEnd(match));
            if (blockStarts.Count > 0 && blockStart <= blockEnds[^1])
            {
                blockEnds[^1] = Math.Max(blockEnds[^1], blockEnd);
                continue;
            }

            blockStarts.Add(blockStart);
            blockEnds.Add(blockEnd);
        }

        var matches = new List<RegexMatch>();
        int blockIndex = 0;
        for (int index = 0;
            index < searchResult.Matches.Count && blockIndex < blockStarts.Count;
            index++)
        {
            RegexMatch match = searchResult.Matches[index];
            int matchEnd = checked(match.Start + match.Length);
            while (blockIndex < blockStarts.Count &&
                (match.Start > blockEnds[blockIndex] ||
                    (match.Start == blockEnds[blockIndex] && match.Length > 0)))
            {
                blockIndex++;
            }

            if (blockIndex >= blockStarts.Count)
            {
                break;
            }

            if (match.Start >= blockStarts[blockIndex] &&
                matchEnd <= blockEnds[blockIndex])
            {
                matches.Add(match);
            }
        }

        return new MultilineSearchResult(matches);
    }

    /// <summary>
    /// Limits positive multiline discovery hits while retaining replay matches in their context ranges.
    /// </summary>
    /// <param name="bytes">The complete searched bytes.</param>
    /// <param name="searchResult">The complete authoritative match result.</param>
    /// <param name="maxCount">The optional maximum number of discovery hits.</param>
    /// <param name="beforeContext">The number of preceding context lines.</param>
    /// <param name="afterContext">The number of following context lines.</param>
    /// <returns>The authoritative matches replayed from the retained match and context lines.</returns>
    internal static MultilineSearchResult LimitPositiveContextMatches(
        ReadOnlySpan<byte> bytes,
        MultilineSearchResult searchResult,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        if (maxCount is null || (ulong)searchResult.Matches.Count <= maxCount.Value)
        {
            return searchResult;
        }

        List<ContextLineInfo> lines = BuildMultilineContextLines(
            bytes,
            searchResult.Matches);
        bool[] included = new bool[lines.Count];
        _ = IncludeMultilineContextLines(
            bytes,
            lines,
            searchResult.Matches,
            included,
            beforeContext,
            afterContext,
            maxCount);

        var matches = new List<RegexMatch>();
        for (int index = 0; index < searchResult.Matches.Count; index++)
        {
            RegexMatch match = searchResult.Matches[index];
            int firstLineIndex = GetMultilineLineIndex(
                lines,
                GetLineStart(bytes, match.Start));
            int lastLineIndex = GetMultilineLineIndex(
                lines,
                GetLineStart(bytes, GetInclusiveMatchEnd(match)));
            if (firstLineIndex < 0 || lastLineIndex < 0)
            {
                continue;
            }

            bool fullyIncluded = true;
            for (int lineIndex = firstLineIndex;
                lineIndex <= lastLineIndex;
                lineIndex++)
            {
                if (!included[lineIndex])
                {
                    fullyIncluded = false;
                    break;
                }
            }

            if (fullyIncluded)
            {
                matches.Add(match);
            }
        }

        return new MultilineSearchResult(matches);
    }

    /// <summary>
    /// Applies CLI mode and output semantics to a retained multiline result.
    /// </summary>
    /// <returns><see langword="true" /> when multiline handling applies.</returns>
    internal static bool TryWriteSearchResult(
        ReadOnlySpan<byte> searchSpan,
        ReadOnlySpan<byte> outputSpan,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        RegexSearchPlan plan,
        MultilineSearchResult searchResult,
        out bool matched)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        bool contextOutputRequested =
            (beforeContext > 0 || afterContext > 0 || passthru) &&
            searchMode == CliSearchMode.Standard;
        ulong? rendererMaxCount = maxCount;
        if (!invertMatch && contextOutputRequested && !passthru)
        {
            searchResult = LimitPositiveContextMatches(
                searchSpan,
                searchResult,
                maxCount,
                beforeContext,
                afterContext);
        }
        else if (!invertMatch)
        {
            searchResult = LimitPositiveMatches(
                searchSpan,
                searchResult,
                maxCount);
            rendererMaxCount = null;
        }

        List<RegexMatch> matches = searchResult.Matches;
        matched = false;

        if (contextOutputRequested)
        {
            matched = SearchMultilineContextBytes(searchSpan, outputSpan, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, invertMatch, vimgrep, onlyMatching, replacement, rendererMaxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator, plan, matches);
            return true;
        }

        if (invertMatch)
        {
            matched = SearchMultilineInvertedBytes(searchSpan, output, prefix, separators, lineLimit, color, searchMode, lineNumber, column, byteOffset, rendererMaxCount, quiet, trim, includeZero, nullPathTerminator, matches);
            return true;
        }

        if (!HasReportableMultilineMatch(searchSpan, matches))
        {
            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
            {
                matched = SearchOutputFormatting.WriteCount(output, prefix, color, 0, includeZero, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            return true;
        }

        if (quiet)
        {
            matched = searchMode != CliSearchMode.FilesWithoutMatch;
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, false, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            long count = CountMultilineMatches(searchSpan, matches, rendererMaxCount);
            matched = SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        matched = true;
        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatchingReplacements(outputSpan, matches, plan, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, rendererMaxCount);
                return true;
            }

            WriteMultilineReplacedLines(outputSpan, matches, plan, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, rendererMaxCount);
            return true;
        }

        if (onlyMatching && !vimgrep)
        {
            WriteMultilineOnlyMatches(outputSpan, matches, output, prefix, separators, color, lineNumber, column, byteOffset, trim, nullPathTerminator, rendererMaxCount);
            return true;
        }

        if (vimgrep)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatches(outputSpan, matches, output, prefix, separators, color, lineNumber: true, column: true, byteOffset, trim, nullPathTerminator, rendererMaxCount);
                return true;
            }

            WriteMultilineVimgrepMatches(outputSpan, matches, output, prefix, separators, lineLimit, color, trim, nullPathTerminator, rendererMaxCount);
            return true;
        }

        WriteMultilineMatchedLines(outputSpan, matches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, rendererMaxCount);
        return true;
    }

    private static bool SearchMultilineInvertedBytes(
        ReadOnlySpan<byte> bytes,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool lineNumber,
        bool column,
        bool byteOffset,
        ulong? maxCount,
        bool quiet,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        List<RegexMatch> matches)
    {
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(bytes, matches);
        long count = CountSelectedMultilineLines(lines, maxCount);
        bool hasSelectedMatch = count > 0;

        if (quiet)
        {
            return searchMode == CliSearchMode.FilesWithoutMatch ? !hasSelectedMatch : hasSelectedMatch;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        ulong emitted = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            ContextLineInfo line = lines[index];
            if (!line.SelectedMatch)
            {
                continue;
            }

            sink.MatchedLine(line.LineNumber, line.Start, matchColumn: 0, bytes.Slice(line.Start, line.Length));
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }
        }

        return hasSelectedMatch;
    }

    private static bool SearchMultilineContextBytes(
        ReadOnlySpan<byte> searchBytes,
        ReadOnlySpan<byte> outputBytes,
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
        RegexSearchPlan plan,
        List<RegexMatch> matches)
    {
        if (invertMatch)
        {
            return SearchMultilineInvertedContextBytes(searchBytes, outputBytes, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator, matches);
        }

        List<ContextLineInfo> lines = BuildMultilineContextLines(outputBytes, matches);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : IncludeMultilineContextLines(outputBytes, lines, matches, included, beforeContext, afterContext, maxCount);
        var lineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        using RegexReplacementSession? replacementSession =
            replacement is ReadOnlyMemory<byte> replacementValue
                ? new RegexReplacementSession(replacementValue, plan)
                : null;
        int previousLineIndex = -1;
        bool wrote = false;
        ulong? renderedMatchLimit = passthru ? maxCount : null;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            if (!passthru && wrote && index > previousLineIndex + 1 && separators.ContextEnabled)
            {
                output.Write(separators.Context.Span);
                output.Write(separators.LineTerminator.Span);
            }

            bool wroteLine = WriteMultilineContextOutputLine(
                outputBytes,
                lines,
                index,
                matches,
                renderedMatchLimit,
                maxCount,
                output,
                separators,
                prefix,
                lineLimit,
                color,
                lineNumber,
                column,
                byteOffset,
                trim,
                nullPathTerminator,
                vimgrep,
                onlyMatching,
                replacementSession,
                ref lineSink,
                out int consumedLineIndex);
            previousLineIndex = Math.Max(previousLineIndex, consumedLineIndex);
            if (wroteLine)
            {
                wrote = true;
            }

            index = Math.Max(index, consumedLineIndex);
        }

        return matched;
    }

    private static bool SearchMultilineInvertedContextBytes(
        ReadOnlySpan<byte> searchBytes,
        ReadOnlySpan<byte> outputBytes,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator,
        List<RegexMatch> matches)
    {
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(searchBytes, matches);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : ContextSearchOperations.IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
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

            if (!passthru && wrote && index > previousLineIndex + 1 && separators.ContextEnabled)
            {
                output.Write(separators.Context.Span);
                output.Write(separators.LineTerminator.Span);
            }

            ReadOnlySpan<byte> lineBytes = outputBytes.Slice(line.Start, line.Length);
            if (selectedMatch)
            {
                lineSink.MatchedLine(line.LineNumber, line.Start, matchColumn: 0, lineBytes);
            }
            else
            {
                lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, lineBytes);
            }

            previousLineIndex = index;
            wrote = true;
        }

        return matched;
    }

    private static bool WriteMultilineContextOutputLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        ulong? discoveryMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        bool onlyMatching,
        RegexReplacementSession? replacementSession,
        ref StandardSearchSink lineSink,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        bool selectedMatch = line.SelectedMatch &&
            MultilineLineHasRenderedMatch(bytes, line, matches, renderedMatchLimit);
        ReadOnlySpan<byte> outputLine = bytes.Slice(line.Start, line.Length);
        if (!selectedMatch)
        {
            lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, outputLine);
            return true;
        }

        if (replacementSession is not null)
        {
            if (onlyMatching)
            {
                return WriteMultilineOnlyMatchingReplacementsForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementSession, out consumedLineIndex);
            }

            return TryWriteMultilineContextReplacementRecord(bytes, lines, lineIndex, matches, renderedMatchLimit, discoveryMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementSession, out consumedLineIndex);
        }

        if (onlyMatching)
        {
            return WriteMultilineOnlyMatchesForContextLine(bytes, line, matches, renderedMatchLimit, output, separators, prefix, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
        }

        if (vimgrep)
        {
            return WriteMultilineVimgrepMatchesForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, out consumedLineIndex);
        }

        if (color.Enabled &&
            TryGetMultilineLineHighlights(bytes, line, matches, renderedMatchLimit, out List<int> highlightStarts, out List<int> highlightLengths))
        {
            lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, outputLine, highlightStarts, highlightLengths);
            return true;
        }

        lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, outputLine);
        return true;
    }

    private static bool HasReportableMultilineMatch(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches)
    {
        return matches.Count > 0 && !IsTrailingEmptyLineMatch(bytes, matches[0]);
    }

    private static long CountMultilineMatches(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        ulong? maxCount)
    {
        long count = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!(index > 0 && IsEofEmptyMatch(bytes, matches[index])))
            {
                count++;
                if (maxCount is ulong limit && (ulong)count >= limit)
                {
                    return count;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Finds the next reportable match with an already compiled multiline plan.
    /// </summary>
    /// <param name="bytes">The bytes being searched.</param>
    /// <param name="plan">The authoritative multiline search plan.</param>
    /// <param name="offset">The first byte offset to inspect.</param>
    /// <param name="suppressedEmptyStart">The empty-match offset suppressed by iteration semantics.</param>
    /// <param name="match">Receives the next reportable match.</param>
    /// <returns><see langword="true" /> when a match was found.</returns>
    internal static bool TryFindNextMultilineMatch(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan plan,
        ref int offset,
        ref int suppressedEmptyStart,
        out RegexMatch match)
    {
        while (offset <= bytes.Length && TryFindMultilineMatch(bytes, plan, offset, out match))
        {
            if (IsTrailingEmptyLineMatch(bytes, match))
            {
                offset = bytes.Length + 1;
                break;
            }

            if (IsEofEmptyMatch(bytes, match) &&
                !plan.EmptyMatchRequiresEndAssertion)
            {
                offset = bytes.Length + 1;
                break;
            }

            var matcherMatch = new MatcherMatch(match.Start, match.Length);
            if (!MatchIterator.IsSuppressedEmpty(matcherMatch, suppressedEmptyStart))
            {
                return true;
            }

            offset = MatchIterator.AdvanceAfterSuppressedEmpty(matcherMatch, bytes.Length);
            suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        }

        match = default;
        return false;
    }

    private static bool IsTrailingEmptyLineMatch(ReadOnlySpan<byte> bytes, RegexMatch match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            (bytes.IsEmpty || bytes[^1] == (byte)'\n');
    }

    private static bool IsEofEmptyMatch(ReadOnlySpan<byte> bytes, RegexMatch match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            !bytes.IsEmpty &&
            bytes[^1] != (byte)'\n';
    }

    /// <summary>
    /// Determines whether an EOF empty match only extends the selected record range after an earlier match.
    /// </summary>
    /// <param name="bytes">The complete haystack.</param>
    /// <param name="match">The candidate EOF match.</param>
    /// <param name="searchOffset">The offset used to find the candidate.</param>
    /// <returns><see langword="true" /> when the match selects a record without adding a reportable occurrence.</returns>
    internal static bool IsSelectionOnlyEofMatch(
        ReadOnlySpan<byte> bytes,
        RegexMatch match,
        int searchOffset)
    {
        return searchOffset != 0 && IsEofEmptyMatch(bytes, match);
    }

    internal static int AdvanceAfterReportedMultilineMatch(RegexMatch match, int haystackLength, ref int suppressedEmptyStart)
    {
        return MatchIterator.AdvanceAfterReported(new MatcherMatch(match.Start, match.Length), haystackLength, ref suppressedEmptyStart);
    }

    private static void WriteMultilineMatchedLines(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount)
    {
        if (color.Enabled)
        {
            WriteColoredMultilineMatchedLines(bytes, matches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, maxCount);
            return;
        }

        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int lastWrittenLineStart = -1;
        int currentLineStart = 0;
        long currentLineNumber = 1;
        ulong emitted = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            RegexMatch match = matches[index];
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            AdvanceLineNumber(bytes, firstLineStart, ref currentLineStart, ref currentLineNumber);
            long outputLineNumber = currentLineNumber;
            long matchColumn = IsEofEmptyMatch(bytes, match)
                ? 0
                : match.Start - firstLineStart + 1L;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart > lastWrittenLineStart)
                {
                    sink.MatchedLine(outputLineNumber, lineStart, matchColumn, bytes[lineStart..lineEnd]);
                    lastWrittenLineStart = lineStart;
                    emitted++;
                    if (maxCount is ulong limit && emitted >= limit)
                    {
                        return;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
                outputLineNumber++;
            }

            currentLineStart = Math.Max(currentLineStart, GetNextLineStart(GetLineEnd(bytes, lastLineStart), bytes.Length));
            currentLineNumber = Math.Max(currentLineNumber, outputLineNumber);
        }
    }

    private static void WriteColoredMultilineMatchedLines(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount)
    {
        var sink = new ColoredSearchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int lastWrittenLineStart = -1;
        int currentLineStart = 0;
        long currentLineNumber = 1;
        ulong emitted = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            RegexMatch match = matches[index];
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            AdvanceLineNumber(bytes, firstLineStart, ref currentLineStart, ref currentLineNumber);
            long outputLineNumber = currentLineNumber;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart >= lastWrittenLineStart)
                {
                    bool eofEmptyMatch = IsEofEmptyMatch(bytes, match);
                    int matchByteOffset = eofEmptyMatch
                        ? lineStart
                        : Math.Max(match.Start, lineStart);
                    int matchEnd = eofEmptyMatch
                        ? matchByteOffset
                        : Math.Min(match.Start + match.Length, GetLineContentEnd(bytes, lineStart));
                    ReadOnlySpan<byte> lineBytes = bytes[lineStart..lineEnd];
                    ReadOnlySpan<byte> matchedBytes = bytes[matchByteOffset..matchEnd];
                    long matchColumn = eofEmptyMatch
                        ? 0
                        : matchByteOffset - lineStart + 1L;
                    sink.MatchedLine(outputLineNumber, lineStart, matchByteOffset, matchColumn, lineBytes, matchedBytes);
                    if (lineStart > lastWrittenLineStart)
                    {
                        lastWrittenLineStart = lineStart;
                        emitted++;
                        if (maxCount is ulong limit && emitted >= limit)
                        {
                            sink.Flush();
                            return;
                        }
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
                outputLineNumber++;
            }

            currentLineStart = Math.Max(currentLineStart, GetNextLineStart(GetLineEnd(bytes, lastLineStart), bytes.Length));
            currentLineNumber = Math.Max(currentLineNumber, outputLineNumber);
        }

        sink.Flush();
    }

    private static void AdvanceLineNumber(ReadOnlySpan<byte> bytes, int targetLineStart, ref int currentLineStart, ref long currentLineNumber)
    {
        if (targetLineStart < currentLineStart)
        {
            currentLineNumber = GetLineNumber(bytes, targetLineStart);
            currentLineStart = targetLineStart;
            return;
        }

        while (currentLineStart < targetLineStart)
        {
            int lineEnd = GetLineEnd(bytes, currentLineStart);
            currentLineStart = GetNextLineStart(lineEnd, bytes.Length);
            currentLineNumber++;
        }
    }

    private static void WriteMultilineReplacedLines(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        RegexSearchPlan plan,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        ulong? maxCount)
    {
        using var replacementSession = new RegexReplacementSession(replacement, plan);
        ulong emitted = 0;
        int groupStart = -1;
        int groupEnd = -1;
        List<RegexMatch> groupMatches = [];
        for (int index = 0; index < matches.Count; index++)
        {
            RegexMatch match = matches[index];
            GetMultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
            if (groupStart >= 0 && rangeStart > groupEnd)
            {
                WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementSession);
                groupMatches.Clear();
                groupStart = -1;
                groupEnd = -1;
            }

            if (groupStart < 0)
            {
                groupStart = rangeStart;
                groupEnd = rangeEnd;
            }
            else if (rangeEnd > groupEnd)
            {
                groupEnd = rangeEnd;
            }

            groupMatches.Add(match);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }

        }

        if (groupStart >= 0)
        {
            WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementSession);
        }
    }

    private static void WriteMultilineOnlyMatchingReplacements(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        RegexSearchPlan plan,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        ulong? maxCount)
    {
        using var replacementSession = new RegexReplacementSession(replacement, plan);
        ulong emitted = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            RegexMatch match = matches[index];
            bool selectionOnly = index > 0 && IsEofEmptyMatch(bytes, match);
            int lineStart = GetLineStart(bytes, match.Start);
            if (selectionOnly)
            {
                continue;
            }

            if (IsEofEmptyMatch(bytes, match))
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                WriteMultilineReplacementBody(bytes[lineStart..lineEnd], lineStart, GetLineNumber(bytes, lineStart), firstColumn: 0, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            }
            else
            {
                byte[] body = replacementSession.Expand(
                    bytes,
                    match.Start,
                    match.Length);
                WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            }

            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }

        }
    }

    private static void WriteMultilineReplacementRecord(
        ReadOnlySpan<byte> bytes,
        int recordStart,
        int recordEnd,
        List<RegexMatch> matches,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        RegexReplacementSession replacementSession)
    {
        List<int> starts = [];
        List<int> lengths = [];
        bool selectionOnly = false;
        for (int index = 0; index < matches.Count; index++)
        {
            if (IsEofEmptyMatch(bytes, matches[index]))
            {
                selectionOnly = true;
                continue;
            }

            starts.Add(matches[index].Start - recordStart);
            lengths.Add(matches[index].Length);
        }

        List<long> replacementColumns = [];
        List<int> replacementLengths = [];
        byte[] body = replacementSession.ReplaceLine(
            bytes[recordStart..recordEnd],
            bytes,
            recordStart,
            starts,
            lengths,
            replacementColumns,
            replacementLengths);
        int lineStart = GetLineStart(bytes, recordStart);
        long firstColumn = replacementColumns.Count > 0
            ? replacementColumns[0]
            : selectionOnly ? 0 : 1;
        WriteMultilineReplacementBody(body, recordStart, GetLineNumber(bytes, lineStart), firstColumn, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementColumns, replacementLengths);
    }

    private static void WriteMultilineReplacementBody(
        ReadOnlySpan<byte> body,
        long byteOffsetStart,
        long lineNumberStart,
        long firstColumn,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        IReadOnlyList<long>? replacementColumns = null,
        IReadOnlyList<int>? replacementLengths = null)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        if (body.IsEmpty)
        {
            sink.MatchedLine(lineNumberStart, byteOffsetStart, firstColumn, []);
            return;
        }

        int outputLineStart = 0;
        long outputLineNumber = lineNumberStart;
        long outputByteOffset = byteOffsetStart;
        while (outputLineStart <= body.Length)
        {
            int outputLineEnd = GetLineEnd(body, outputLineStart);
            long outputColumn = outputLineStart == 0 ? firstColumn : 1;
            if (replacementColumns is not null && replacementLengths is not null)
            {
                List<int> lineStarts = [];
                List<int> lineLengths = [];
                AddLineReplacementSpans(outputLineStart, outputLineEnd, replacementColumns, replacementLengths, lineStarts, lineLengths);
                sink.MatchedLine(outputLineNumber, outputByteOffset, outputColumn, body[outputLineStart..outputLineEnd], lineStarts, lineLengths);
            }
            else
            {
                sink.MatchedLine(outputLineNumber, outputByteOffset, outputColumn, body[outputLineStart..outputLineEnd]);
            }

            if (outputLineEnd >= body.Length)
            {
                break;
            }

            outputByteOffset += outputLineEnd - outputLineStart;
            outputLineNumber++;
            outputLineStart = outputLineEnd;
        }
    }

    private static void AddLineReplacementSpans(
        int lineStart,
        int lineEnd,
        IReadOnlyList<long> replacementColumns,
        IReadOnlyList<int> replacementLengths,
        List<int> lineStarts,
        List<int> lineLengths)
    {
        for (int index = 0; index < replacementColumns.Count; index++)
        {
            int replacementStart = checked((int)replacementColumns[index] - 1);
            int replacementEnd = replacementStart + replacementLengths[index];
            if (replacementEnd <= lineStart || replacementStart >= lineEnd)
            {
                continue;
            }

            int start = Math.Max(replacementStart, lineStart);
            int end = Math.Min(replacementEnd, lineEnd);
            lineStarts.Add(start - lineStart);
            lineLengths.Add(end - start);
        }
    }

    private static void GetMultilineReplacementRange(ReadOnlySpan<byte> bytes, RegexMatch match, out int start, out int end)
    {
        start = GetLineStart(bytes, match.Start);
        int endAnchor = match.Length == 0 ? match.Start : match.Start + match.Length;
        int lastLineStart = GetLineStart(bytes, endAnchor);
        end = GetLineEnd(bytes, lastLineStart);
    }

    private static void WriteMultilineOnlyMatches(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount)
    {
        var sink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
        ulong emitted = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            RegexMatch match = matches[index];
            bool selectionOnly = index > 0 && IsEofEmptyMatch(bytes, match);
            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (selectionOnly)
            {
                continue;
            }

            if (IsEofEmptyMatch(bytes, match))
            {
                int lineEnd = GetLineEnd(bytes, firstLineStart);
                var selectionSink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column: false, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
                selectionSink.Matched(firstLineNumber, firstLineStart, 0, bytes[firstLineStart..lineEnd]);
            }
            else if (match.Length == 0)
            {
                // Multiline-only output does not render zero-width submatches.
            }
            else
            {
                for (int lineStart = firstLineStart; lineStart < matchEnd;)
                {
                    int lineEnd = GetLineEnd(bytes, lineStart);
                    int segmentStart = Math.Max(lineStart, match.Start);
                    int segmentEnd = Math.Min(lineEnd, matchEnd);
                    if (segmentStart < segmentEnd)
                    {
                        sink.Matched(firstLineNumber + CountLineFeeds(bytes[firstLineStart..lineStart]), match.Start, matchColumn, bytes[segmentStart..segmentEnd]);
                    }

                    lineStart = GetNextLineStart(lineEnd, bytes.Length);
                }
            }

            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return;
            }

        }
    }

    private static void WriteMultilineVimgrepMatches(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber: true, column: true, byteOffset: false, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        ulong emitted = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            RegexMatch match = matches[index];
            int lineStart = GetLineStart(bytes, match.Start);
            int lineEnd = GetLineEnd(bytes, lineStart);
            long lineNumber = GetLineNumber(bytes, lineStart);
            long matchColumn = IsEofEmptyMatch(bytes, match)
                ? 0
                : match.Start - lineStart + 1L;
            sink.MatchedLine(lineNumber, lineStart, matchColumn, bytes[lineStart..lineEnd], match.Start - lineStart, match.Length);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return;
            }

        }
    }

    private static bool TryFindMultilineMatch(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan plan,
        int startAt,
        out RegexMatch match)
    {
        RegexMatch? candidate = plan.Matcher.Find(bytes, startAt);
        if (candidate.HasValue)
        {
            match = candidate.Value;
            return true;
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Collects non-overlapping matches with an already compiled multiline plan.
    /// </summary>
    /// <param name="bytes">The bytes being searched.</param>
    /// <param name="plan">The authoritative multiline search plan.</param>
    /// <returns>The reportable matches in search order.</returns>
    internal static List<RegexMatch> CollectMultilineMatches(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan plan)
    {
        List<RegexMatch> matches = [];
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, plan, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            matches.Add(match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        return matches;
    }

    internal static bool IncludeMultilineContextLines(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        List<RegexMatch> matches,
        bool[] included,
        ulong beforeContext,
        ulong afterContext,
        ulong? maxCount)
    {
        bool matched = false;
        ulong primaryMatches = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            bool selectionOnly = index > 0 && IsEofEmptyMatch(bytes, matches[index]);
            if (!selectionOnly && maxCount is ulong limit && primaryMatches >= limit)
            {
                break;
            }

            int firstLineStart = GetLineStart(bytes, matches[index].Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(matches[index]));
            int firstLineIndex = GetMultilineLineIndex(lines, firstLineStart);
            int lastLineIndex = GetMultilineLineIndex(lines, lastLineStart);
            if (firstLineIndex < 0 || lastLineIndex < 0)
            {
                continue;
            }

            matched = true;
            if (!selectionOnly)
            {
                primaryMatches++;
            }

            IncludeMultilineContextRange(included, firstLineIndex, lastLineIndex, beforeContext, afterContext);
        }

        return matched;
    }

    private static void IncludeMultilineContextRange(bool[] included, int firstLineIndex, int lastLineIndex, ulong beforeContext, ulong afterContext)
    {
        int startIndex = beforeContext >= (ulong)firstLineIndex
            ? 0
            : firstLineIndex - (int)beforeContext;
        ulong requestedEnd = (ulong)lastLineIndex + afterContext;
        int endIndex = requestedEnd >= (ulong)included.Length
            ? included.Length - 1
            : (int)requestedEnd;
        for (int index = startIndex; index <= endIndex; index++)
        {
            included[index] = true;
        }
    }

    internal static List<ContextLineInfo> BuildMultilineContextLines(ReadOnlySpan<byte> bytes, List<RegexMatch> matches, bool stopOnNonmatch = false)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes[lineStart..], nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        long[] matchColumns = new long[physicalLines.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            MarkMultilineContextMatch(bytes, physicalLines, matchedLines, matchColumns, matches[index]);
        }

        var lines = new List<ContextLineInfo>(physicalLines.Count);
        bool hasSelectedMatch = false;
        for (int index = 0; index < physicalLines.Count; index++)
        {
            ContextLineInfo line = physicalLines[index];
            bool selected = matchedLines[index];
            lines.Add(new ContextLineInfo(line.Start, line.Length, line.LineNumber, selected, matchColumns[index], selected, matchColumns[index]));
            if (stopOnNonmatch && hasSelectedMatch && !selected)
            {
                break;
            }

            hasSelectedMatch |= selected;
        }

        return lines;
    }

    /// <summary>
    /// Builds physical-line metadata with lines outside matches from an existing multiline plan selected.
    /// </summary>
    /// <param name="bytes">The complete haystack.</param>
    /// <param name="plan">The operation-scoped authoritative multiline plan.</param>
    /// <returns>The physical lines with multiline matches inverted.</returns>
    internal static List<ContextLineInfo> BuildMultilineInvertedLines(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan plan)
    {
        return BuildMultilineInvertedLines(bytes, CollectMultilineMatches(bytes, plan));
    }

    /// <summary>
    /// Builds physical-line metadata with lines outside retained multiline matches selected.
    /// </summary>
    /// <param name="bytes">The complete haystack.</param>
    /// <param name="matches">The retained authoritative multiline matches.</param>
    /// <returns>The physical lines with the retained matches inverted.</returns>
    internal static List<ContextLineInfo> BuildMultilineInvertedLines(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes[lineStart..], nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            MarkMultilineMatchedLines(bytes, physicalLines, matchedLines, matches[index]);
        }

        var lines = new List<ContextLineInfo>(physicalLines.Count);
        for (int index = 0; index < physicalLines.Count; index++)
        {
            ContextLineInfo line = physicalLines[index];
            bool originalMatch = matchedLines[index];
            lines.Add(new ContextLineInfo(line.Start, line.Length, line.LineNumber, selectedMatch: !originalMatch, matchColumn: 0, originalMatch, contextColumn: 0));
        }

        return lines;
    }

    private static void MarkMultilineContextMatch(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, long[] matchColumns, RegexMatch match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
        long matchColumn = IsEofEmptyMatch(bytes, match)
            ? 0
            : match.Start - firstLineStart + 1L;
        for (int index = 0; index < lines.Count; index++)
        {
            int lineStart = lines[index].Start;
            if (lineStart < firstLineStart)
            {
                continue;
            }

            if (lineStart > lastLineStart)
            {
                break;
            }

            matchedLines[index] = true;
            if (matchColumns[index] == 0)
            {
                matchColumns[index] = matchColumn;
            }
        }
    }

    private static void MarkMultilineMatchedLines(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, RegexMatch match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
        for (int index = 0; index < lines.Count; index++)
        {
            int lineStart = lines[index].Start;
            if (lineStart < firstLineStart)
            {
                continue;
            }

            if (lineStart > lastLineStart)
            {
                break;
            }

            matchedLines[index] = true;
        }
    }

    internal static int GetMultilineLineIndex(List<ContextLineInfo> lines, int lineStart)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].Start == lineStart)
            {
                return index;
            }
        }

        return -1;
    }

    internal static bool MultilineLineHasRenderedMatch(ReadOnlySpan<byte> bytes, ContextLineInfo line, List<RegexMatch> matches, ulong? renderedMatchLimit)
    {
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            if (MultilineMatchTouchesLine(bytes, matches[index], line))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MultilineMatchTouchesLine(ReadOnlySpan<byte> bytes, RegexMatch match, ContextLineInfo line)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
        return line.Start >= firstLineStart && line.Start <= lastLineStart;
    }

    private static bool TryGetMultilineLineHighlights(
        ReadOnlySpan<byte> bytes,
        ContextLineInfo line,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        out List<int> starts,
        out List<int> lengths)
    {
        starts = [];
        lengths = [];
        int lineContentEnd = GetLineContentEnd(bytes, line.Start);
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (index > 0 && IsEofEmptyMatch(bytes, match))
            {
                continue;
            }

            if (!MultilineMatchTouchesLine(bytes, match, line))
            {
                continue;
            }

            int start = Math.Max(match.Start, line.Start);
            int end = Math.Min(match.Start + match.Length, lineContentEnd);
            if (end <= start)
            {
                continue;
            }

            starts.Add(start - line.Start);
            lengths.Add(end - start);
        }

        return starts.Count > 0;
    }

    internal static bool IsMultilineContextMatchRendered(int matchIndex, ulong? renderedMatchLimit)
    {
        return renderedMatchLimit is not ulong limit || (ulong)matchIndex < limit;
    }

    private static bool WriteMultilineOnlyMatchesForContextLine(
        ReadOnlySpan<byte> bytes,
        ContextLineInfo line,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator)
    {
        var sink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
        bool wrote = false;
        int lineEnd = line.Start + line.Length;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (!MultilineMatchTouchesLine(bytes, match, line))
            {
                continue;
            }

            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (IsEofEmptyMatch(bytes, match))
            {
                var selectionSink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column: false, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
                selectionSink.Matched(firstLineNumber, firstLineStart, 0, bytes[firstLineStart..lineEnd]);
                wrote = true;
            }
            else if (match.Length == 0)
            {
                continue;
            }
            else
            {
                int segmentStart = Math.Max(line.Start, match.Start);
                int segmentEnd = Math.Min(lineEnd, matchEnd);
                if (segmentStart < segmentEnd)
                {
                    sink.Matched(
                        firstLineNumber + CountLineFeeds(bytes[firstLineStart..line.Start]),
                        match.Start,
                        matchColumn,
                        bytes[segmentStart..segmentEnd]);
                    wrote = true;
                }
            }
        }

        return wrote;
    }

    private static bool WriteMultilineOnlyMatchingReplacementsForContextLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        RegexReplacementSession replacementSession,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        bool wrote = false;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (index > 0 && IsEofEmptyMatch(bytes, match))
            {
                continue;
            }

            if (GetLineStart(bytes, match.Start) != line.Start)
            {
                continue;
            }

            int lineStart = GetLineStart(bytes, match.Start);
            if (IsEofEmptyMatch(bytes, match))
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                WriteMultilineReplacementBody(bytes[lineStart..lineEnd], lineStart, GetLineNumber(bytes, lineStart), firstColumn: 0, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            }
            else
            {
                byte[] body = replacementSession.Expand(
                    bytes,
                    match.Start,
                    match.Length);
                WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            }

            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            consumedLineIndex = Math.Max(consumedLineIndex, GetMultilineLineIndex(lines, lastLineStart));
            wrote = true;
        }

        return wrote;
    }

    private static bool TryWriteMultilineContextReplacementRecord(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        ulong? discoveryMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        RegexReplacementSession replacementSession,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        int groupStart = -1;
        int groupEnd = -1;
        List<RegexMatch> groupMatches = [];
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            GetMultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
            if (groupStart < 0)
            {
                if (rangeStart != line.Start)
                {
                    continue;
                }

                groupStart = rangeStart;
                groupEnd = rangeEnd;
                groupMatches.Add(match);
                continue;
            }

            if (rangeStart == groupEnd &&
                discoveryMatchLimit is ulong limit &&
                (ulong)index >= limit)
            {
                break;
            }

            if (rangeStart > groupEnd)
            {
                break;
            }

            if (rangeEnd > groupEnd)
            {
                groupEnd = rangeEnd;
            }

            groupMatches.Add(match);
        }

        if (groupStart < 0)
        {
            return false;
        }

        WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementSession);
        int lastLineStart = GetLineStart(bytes, groupEnd > groupStart ? groupEnd - 1 : groupEnd);
        consumedLineIndex = Math.Max(consumedLineIndex, GetMultilineLineIndex(lines, lastLineStart));
        return true;
    }

    private static bool WriteMultilineVimgrepMatchesForContextLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        bool wrote = false;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (GetLineStart(bytes, match.Start) != line.Start)
            {
                continue;
            }

            int lineEnd = GetLineEnd(bytes, line.Start);
            long matchColumn = IsEofEmptyMatch(bytes, match)
                ? 0
                : match.Start - line.Start + 1L;
            sink.MatchedLine(line.LineNumber, line.Start, matchColumn, bytes[line.Start..lineEnd], match.Start - line.Start, match.Length);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            consumedLineIndex = Math.Max(consumedLineIndex, GetMultilineLineIndex(lines, lastLineStart));
            wrote = true;
        }

        return wrote;
    }

    private static long CountSelectedMultilineLines(List<ContextLineInfo> lines, ulong? maxCount)
    {
        long count = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!lines[index].SelectedMatch)
            {
                continue;
            }

            count++;
            if (maxCount is ulong limit && (ulong)count >= limit)
            {
                return count;
            }
        }

        return count;
    }

    internal static int GetInclusiveMatchEnd(RegexMatch match)
    {
        return match.Length == 0 ? match.Start : match.Start + match.Length - 1;
    }

    internal static int GetLineStart(ReadOnlySpan<byte> bytes, int offset)
    {
        int boundedOffset = Math.Clamp(offset, 0, bytes.Length);
        for (int index = boundedOffset - 1; index >= 0; index--)
        {
            if (bytes[index] == (byte)'\n')
            {
                return index + 1;
            }
        }

        return 0;
    }

    internal static int GetLineEnd(ReadOnlySpan<byte> bytes, int lineStart)
    {
        int boundedStart = Math.Clamp(lineStart, 0, bytes.Length);
        int relativeEnd = bytes[boundedStart..].IndexOf((byte)'\n');
        return relativeEnd < 0 ? bytes.Length : boundedStart + relativeEnd + 1;
    }

    private static int GetLineContentEnd(ReadOnlySpan<byte> bytes, int lineStart)
    {
        int lineEnd = GetLineEnd(bytes, lineStart);
        return lineEnd > lineStart && bytes[lineEnd - 1] == (byte)'\n'
            ? lineEnd - 1
            : lineEnd;
    }

    internal static int GetNextLineStart(int lineEnd, int length)
    {
        return lineEnd < length ? lineEnd : length + 1;
    }

    internal static long GetLineNumber(ReadOnlySpan<byte> bytes, int lineStart)
    {
        return 1 + CountLineFeeds(bytes[..Math.Clamp(lineStart, 0, bytes.Length)]);
    }

    internal static long CountLineFeeds(ReadOnlySpan<byte> bytes)
    {
        return ByteCounter.Count(bytes, (byte)'\n');
    }

}
