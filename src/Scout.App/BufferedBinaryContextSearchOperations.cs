namespace Scout;

/// <summary>
/// Replays a buffered line search with ripgrep-compatible binary and context
/// event ordering from one authoritative regex traversal.
/// </summary>
internal static class BufferedBinaryContextSearchOperations
{
    private const int InitialBufferCapacity = 64 * 1024;

    /// <summary>
    /// Searches converted binary input once and replays the standard printer
    /// events produced by ripgrep's rolling line buffer.
    /// </summary>
    /// <param name="originalBytes">The complete original input bytes.</param>
    /// <param name="binaryOffset">The zero-based offset of the first converted NUL byte.</param>
    /// <param name="pattern">The ordered regex patterns.</param>
    /// <param name="output">The output writer.</param>
    /// <param name="prefix">The optional output path prefix.</param>
    /// <param name="separators">The configured output separators.</param>
    /// <param name="lineLimit">The output line-length policy.</param>
    /// <param name="color">The configured output colors.</param>
    /// <param name="vimgrep">Whether to write vimgrep records.</param>
    /// <param name="lineNumber">Whether to write line numbers.</param>
    /// <param name="column">Whether to write match columns.</param>
    /// <param name="byteOffset">Whether to write byte offsets.</param>
    /// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
    /// <param name="invertMatch">Whether non-matching lines are selected.</param>
    /// <param name="lineRegexp">Whether patterns must match complete lines.</param>
    /// <param name="wordRegexp">Whether matches require word boundaries.</param>
    /// <param name="onlyMatching">Whether to write only retained match spans.</param>
    /// <param name="replacement">The optional replacement template.</param>
    /// <param name="maxCount">The optional maximum number of primary matching lines.</param>
    /// <param name="trim">Whether to trim leading ASCII whitespace.</param>
    /// <param name="beforeContext">The number of preceding context lines.</param>
    /// <param name="afterContext">The number of following context lines.</param>
    /// <param name="passthru">Whether to write every searched line.</param>
    /// <param name="nullPathTerminator">Whether path prefixes use a NUL terminator.</param>
    /// <param name="stopOnNonmatch">Whether to stop after the first non-match following a selection.</param>
    /// <param name="regexPlan">The authoritative regex search plan.</param>
    /// <param name="metrics">The optional search statistics accumulator.</param>
    /// <returns><see langword="true" /> when the printer observed a selected line.</returns>
    internal static bool Search(
        ReadOnlySpan<byte> originalBytes,
        int binaryOffset,
        IReadOnlyList<byte[]> pattern,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool vimgrep,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        RegexSearchPlan regexPlan,
        StandardSearchMetrics? metrics)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if ((uint)binaryOffset >= (uint)originalBytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(binaryOffset));
        }

        byte[] convertedBytes = BinaryDetection.ConvertNulToLineFeed(originalBytes);
        metrics?.BeginTraversal();
        ContextSearchResult searchResult = ContextSearchOperations.BuildSearchResult(
            convertedBytes,
            pattern,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            separators.Crlf,
            separators.NullData,
            stopOnNonmatch: false,
            regexPlan);
        List<ContextLineInfo> lines = searchResult.Lines;
        var outputLineIndexes = new List<int>();
        var outputSelected = new List<bool>();
        var separatorBefore = new List<bool>();

        int capacity = InitialBufferCapacity;
        int bufferStart = 0;
        int loadedEnd = 0;
        int exposedEnd = 0;
        int corePosition = 0;
        int lastLineVisited = 0;
        ulong afterContextLeft = 0;
        ulong primaryMatchCount = 0;
        ulong reportedMatchedLines = 0;
        ulong reportedMatches = 0;
        bool hasSunk = false;
        bool hasMatched = false;
        bool binaryNotified = false;
        bool pendingSeparator = false;
        bool stopped = false;
        bool anyContext = beforeContext > 0 || afterContext > 0;

        bool HasExceededMatchLimit()
        {
            return maxCount is ulong limit && primaryMatchCount >= limit;
        }

        int FindLineAtOrAfter(int absoluteOffset)
        {
            int low = 0;
            int high = lines.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (lines[middle].Start < absoluteOffset)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        void RecordOutputLine(int lineIndex, bool selectedMatch)
        {
            outputLineIndexes.Add(lineIndex);
            outputSelected.Add(selectedMatch);
            separatorBefore.Add(pendingSeparator);
            pendingSeparator = false;
        }

        void SinkContextBreak(int lineStart)
        {
            if (anyContext && hasSunk && lastLineVisited < lineStart &&
                separators.ContextEnabled)
            {
                pendingSeparator = true;
            }
        }

        bool SinkMatched(int lineIndex)
        {
            ContextLineInfo line = lines[lineIndex];
            SinkContextBreak(line.Start);
            reportedMatchedLines++;
            reportedMatches += checked((ulong)searchResult.GetMatches(line).Length);
            if (binaryNotified)
            {
                return false;
            }

            RecordOutputLine(lineIndex, selectedMatch: true);
            lastLineVisited = checked(line.Start + line.Length);
            afterContextLeft = afterContext;
            hasSunk = true;
            return true;
        }

        bool SinkBeforeContext(int lineIndex)
        {
            ContextLineInfo line = lines[lineIndex];
            SinkContextBreak(line.Start);
            if (binaryNotified)
            {
                return false;
            }

            RecordOutputLine(lineIndex, selectedMatch: false);
            lastLineVisited = checked(line.Start + line.Length);
            hasSunk = true;
            return true;
        }

        bool SinkAfterContext(int lineIndex)
        {
            if (binaryNotified)
            {
                return false;
            }

            ContextLineInfo line = lines[lineIndex];
            RecordOutputLine(lineIndex, selectedMatch: false);
            lastLineVisited = checked(line.Start + line.Length);
            afterContextLeft--;
            hasSunk = true;
            return true;
        }

        bool SinkOtherContext(int lineIndex)
        {
            if (binaryNotified)
            {
                return false;
            }

            ContextLineInfo line = lines[lineIndex];
            RecordOutputLine(lineIndex, selectedMatch: false);
            lastLineVisited = checked(line.Start + line.Length);
            hasSunk = true;
            return true;
        }

        bool WriteBeforeContext(int matchLineIndex)
        {
            if (beforeContext == 0)
            {
                return true;
            }

            int firstLineIndex = matchLineIndex;
            ulong remaining = beforeContext;
            while (firstLineIndex > 0 && remaining > 0)
            {
                ContextLineInfo precedingLine = lines[firstLineIndex - 1];
                if (precedingLine.Start < lastLineVisited)
                {
                    break;
                }

                firstLineIndex--;
                remaining--;
            }

            for (int index = firstLineIndex; index < matchLineIndex; index++)
            {
                ContextLineInfo line = lines[index];
                if (line.Start < lastLineVisited)
                {
                    continue;
                }

                if (!SinkBeforeContext(index))
                {
                    return false;
                }
            }

            return true;
        }

        bool WriteAfterContext(int absoluteEnd)
        {
            if (afterContextLeft == 0)
            {
                return true;
            }

            bool exceededMatchLimit = HasExceededMatchLimit();
            int lineIndex = FindLineAtOrAfter(lastLineVisited);
            while (lineIndex < lines.Count && afterContextLeft > 0)
            {
                ContextLineInfo line = lines[lineIndex];
                int lineEnd = checked(line.Start + line.Length);
                if (lineEnd > absoluteEnd)
                {
                    break;
                }

                if (exceededMatchLimit && line.SelectedMatch)
                {
                    ulong savedAfterContext = afterContextLeft;
                    corePosition = lineEnd;
                    if (!SinkMatched(lineIndex))
                    {
                        return false;
                    }

                    afterContextLeft = savedAfterContext - 1;
                }
                else if (!SinkAfterContext(lineIndex))
                {
                    return false;
                }

                lineIndex++;
            }

            return true;
        }

        bool SearchVisibleSlow(int absoluteEnd)
        {
            int lineIndex = FindLineAtOrAfter(corePosition);
            while (lineIndex < lines.Count)
            {
                ContextLineInfo line = lines[lineIndex];
                int lineEnd = checked(line.Start + line.Length);
                if (lineEnd > absoluteEnd)
                {
                    break;
                }

                if (HasExceededMatchLimit() && !passthru &&
                    afterContextLeft == 0)
                {
                    return false;
                }

                bool selectedMatch = line.SelectedMatch &&
                    !HasExceededMatchLimit();
                corePosition = lineEnd;
                if (selectedMatch)
                {
                    hasMatched = true;
                    primaryMatchCount++;
                    if (!WriteBeforeContext(lineIndex) ||
                        !SinkMatched(lineIndex))
                    {
                        return false;
                    }
                }
                else if (afterContextLeft >= 1)
                {
                    if (!SinkAfterContext(lineIndex))
                    {
                        return false;
                    }
                }
                else if (passthru && !SinkOtherContext(lineIndex))
                {
                    return false;
                }

                if (stopOnNonmatch && !selectedMatch && hasMatched)
                {
                    return false;
                }

                lineIndex++;
            }

            corePosition = absoluteEnd;
            return true;
        }

        bool SearchVisibleFast(int absoluteEnd)
        {
            if (invertMatch)
            {
                return SearchVisibleFastInverted(absoluteEnd);
            }

            while (corePosition < absoluteEnd)
            {
                if (stopOnNonmatch && hasMatched)
                {
                    return SearchVisibleSlow(absoluteEnd);
                }

                int matchLineIndex = -1;
                if (!HasExceededMatchLimit())
                {
                    int lineIndex = FindLineAtOrAfter(corePosition);
                    while (lineIndex < lines.Count)
                    {
                        ContextLineInfo line = lines[lineIndex];
                        if (checked(line.Start + line.Length) > absoluteEnd)
                        {
                            break;
                        }

                        if (line.SelectedMatch)
                        {
                            matchLineIndex = lineIndex;
                            break;
                        }

                        lineIndex++;
                    }
                }

                if (matchLineIndex < 0)
                {
                    break;
                }

                ContextLineInfo matchLine = lines[matchLineIndex];
                hasMatched = true;
                primaryMatchCount++;
                if (anyContext &&
                    (!WriteAfterContext(matchLine.Start) ||
                     !WriteBeforeContext(matchLineIndex)))
                {
                    return false;
                }

                corePosition = checked(matchLine.Start + matchLine.Length);
                if (!SinkMatched(matchLineIndex))
                {
                    return false;
                }
            }

            if (!WriteAfterContext(absoluteEnd))
            {
                return false;
            }

            if (HasExceededMatchLimit() && afterContextLeft == 0)
            {
                return false;
            }

            corePosition = absoluteEnd;
            return true;
        }

        bool SearchVisibleFastInverted(int absoluteEnd)
        {
            while (corePosition < absoluteEnd)
            {
                if (stopOnNonmatch && hasMatched)
                {
                    return SearchVisibleSlow(absoluteEnd);
                }

                int invertedStart = corePosition;
                int invertedEnd = absoluteEnd;
                int originalMatchLineIndex = -1;
                if (!HasExceededMatchLimit())
                {
                    int lineIndex = FindLineAtOrAfter(corePosition);
                    while (lineIndex < lines.Count)
                    {
                        ContextLineInfo line = lines[lineIndex];
                        if (checked(line.Start + line.Length) > absoluteEnd)
                        {
                            break;
                        }

                        if (line.OriginalMatch)
                        {
                            originalMatchLineIndex = lineIndex;
                            invertedEnd = line.Start;
                            break;
                        }

                        lineIndex++;
                    }
                }

                corePosition = originalMatchLineIndex < 0
                    ? absoluteEnd
                    : checked(
                        lines[originalMatchLineIndex].Start +
                        lines[originalMatchLineIndex].Length);
                if (invertedStart == invertedEnd)
                {
                    continue;
                }

                int firstInvertedLineIndex = FindLineAtOrAfter(invertedStart);
                hasMatched = true;
                if (!WriteAfterContext(invertedStart) ||
                    !WriteBeforeContext(firstInvertedLineIndex))
                {
                    return false;
                }

                for (int lineIndex = firstInvertedLineIndex;
                     lineIndex < lines.Count;
                     lineIndex++)
                {
                    ContextLineInfo line = lines[lineIndex];
                    if (checked(line.Start + line.Length) > invertedEnd)
                    {
                        break;
                    }

                    primaryMatchCount++;
                    if (!SinkMatched(lineIndex) || HasExceededMatchLimit())
                    {
                        return false;
                    }
                }
            }

            if (!WriteAfterContext(absoluteEnd))
            {
                return false;
            }

            if (HasExceededMatchLimit() && afterContextLeft == 0)
            {
                return false;
            }

            corePosition = absoluteEnd;
            return true;
        }

        while (!stopped)
        {
            int oldBufferStart = bufferStart;
            int oldVisibleLength = exposedEnd - bufferStart;
            int consumedEnd = anyContext
                ? Math.Max(
                    GetPrecedingLineStart(
                        convertedBytes,
                        bufferStart,
                        exposedEnd,
                        beforeContext),
                    lastLineVisited)
                : exposedEnd;
            bufferStart = consumedEnd;
            lastLineVisited = bufferStart;

            bool filled = false;
            while (!filled)
            {
                int retainedLength = loadedEnd - bufferStart;
                if (retainedLength == capacity)
                {
                    capacity = checked(capacity + (capacity * 2));
                }

                if (loadedEnd == convertedBytes.Length)
                {
                    exposedEnd = loadedEnd;
                    filled = true;
                    break;
                }

                int readEnd = Math.Min(
                    convertedBytes.Length,
                    checked(bufferStart + capacity));
                int readStart = loadedEnd;
                loadedEnd = readEnd;
                if (!binaryNotified &&
                    binaryOffset >= readStart && binaryOffset < readEnd)
                {
                    binaryNotified = true;
                }

                int lastLineFeed = convertedBytes
                    .AsSpan(readStart, readEnd - readStart)
                    .LastIndexOf((byte)'\n');
                if (lastLineFeed >= 0)
                {
                    exposedEnd = checked(readStart + lastLineFeed + 1);
                    filled = true;
                }
            }

            int visibleLength = exposedEnd - bufferStart;
            if (visibleLength == 0)
            {
                corePosition = bufferStart;
                break;
            }

            int consumedLength = consumedEnd - oldBufferStart;
            if (consumedLength == 0 && oldVisibleLength == visibleLength)
            {
                bufferStart = checked(bufferStart + oldVisibleLength);
                corePosition = bufferStart;
                break;
            }

            bool keepGoing = passthru
                ? SearchVisibleSlow(exposedEnd)
                : SearchVisibleFast(exposedEnd);
            if (!keepGoing)
            {
                stopped = true;
            }
        }

        metrics?.Record(
            reportedMatchedLines,
            reportedMatches,
            checked((ulong)corePosition));

        var lineSink = new StandardSearchSink(
            output,
            prefix,
            separators.FieldMatch,
            separators.FieldContext,
            lineNumber,
            column,
            byteOffset,
            trim,
            nullPathTerminator,
            lineLimit,
            color,
            separators.LineTerminator);
        for (int index = 0; index < outputLineIndexes.Count; index++)
        {
            if (separatorBefore[index])
            {
                output.Write(separators.Context.Span);
                output.Write(separators.LineTerminator.Span);
            }

            int lineIndex = outputLineIndexes[index];
            ContextLineInfo line = lines[lineIndex];
            ContextSearchOperations.WriteContextOutputLine(
                originalBytes,
                line,
                searchResult.GetMatches(line),
                outputSelected[index],
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
        }

        if (pendingSeparator)
        {
            output.Write(separators.Context.Span);
            output.Write(separators.LineTerminator.Span);
        }

        if (binaryNotified && reportedMatchedLines > 0)
        {
            StandardSearchByteOperations.WriteBinaryFileMatches(
                output,
                prefix,
                color,
                binaryOffset);
        }

        return reportedMatchedLines > 0;
    }

    private static int GetPrecedingLineStart(
        byte[] bytes,
        int start,
        int end,
        ulong count)
    {
        int position = end;
        if (position == start)
        {
            return start;
        }

        if (bytes[position - 1] == (byte)'\n')
        {
            position--;
        }

        while (position > start)
        {
            int relativeLineFeed = bytes
                .AsSpan(start, position - start)
                .LastIndexOf((byte)'\n');
            if (relativeLineFeed < 0)
            {
                return start;
            }

            int lineFeed = start + relativeLineFeed;
            if (count == 0)
            {
                return lineFeed + 1;
            }

            if (lineFeed == start)
            {
                return start;
            }

            count--;
            position = lineFeed;
        }

        return start;
    }
}
