using System;
using System.Collections.Generic;

namespace Scout;

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
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
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
        out bool matched)
    {
        matched = false;
        bool contextOutputRequested = (beforeContext > 0 || afterContext > 0 || passthru) &&
            searchMode == CliSearchMode.Standard;
        if (separators.Crlf ||
            separators.NullData)
        {
            return false;
        }

        if (contextOutputRequested)
        {
            matched = SearchMultilineContextBytes(searchSpan, outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multilineDotall, vimgrep, onlyMatching, replacement, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator);
            return true;
        }

        if (invertMatch)
        {
            matched = SearchMultilineInvertedBytes(searchSpan, patterns, output, prefix, separators, lineLimit, color, searchMode, lineNumber, column, byteOffset, maxCount, quiet, trim, includeZero, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
            return true;
        }

        if (!TryFindMultilineMatch(searchSpan, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, startAt: 0, out RegexMatch firstMatch) ||
            IsTrailingEmptyLineMatch(searchSpan, firstMatch))
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

        long count = CountMultilineMatches(searchSpan, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            matched = SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        matched = true;
        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatchingReplacements(outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
                return true;
            }

            WriteMultilineReplacedLines(outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        if (onlyMatching && !vimgrep)
        {
            WriteMultilineOnlyMatches(outputSpan, patterns, output, prefix, separators, color, lineNumber, column, byteOffset, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        if (vimgrep)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatches(outputSpan, patterns, output, prefix, separators, color, lineNumber: true, column: true, byteOffset, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
                return true;
            }

            WriteMultilineVimgrepMatches(outputSpan, patterns, output, prefix, separators, lineLimit, color, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        WriteMultilineMatchedLines(outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
        return true;
    }

    private static bool SearchMultilineInvertedBytes(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall)
    {
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
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
        IReadOnlyList<byte[]> patterns,
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
        bool multilineDotall,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator)
    {
        if (invertMatch)
        {
            return SearchMultilineInvertedContextBytes(searchBytes, outputBytes, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator);
        }

        List<RegexMatch> matches = CollectMultilineMatches(searchBytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
        List<ContextLineInfo> lines = BuildMultilineContextLines(outputBytes, matches);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : IncludeMultilineContextLines(outputBytes, lines, matches, included, beforeContext, afterContext, maxCount);
        var lineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
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
                replacement,
                patterns,
                asciiCaseInsensitive,
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
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator)
    {
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(searchBytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
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
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
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

        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                return WriteMultilineOnlyMatchingReplacementsForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, asciiCaseInsensitive, out consumedLineIndex);
            }

            return TryWriteMultilineContextReplacementRecord(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, asciiCaseInsensitive, out consumedLineIndex);
        }

        if (onlyMatching)
        {
            return WriteMultilineOnlyMatchesForContextLine(bytes, line, matches, renderedMatchLimit, output, separators, prefix, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
        }

        if (vimgrep)
        {
            return WriteMultilineVimgrepMatchesForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, out consumedLineIndex);
        }

        lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, outputLine);
        return true;
    }

    private static long CountMultilineMatches(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall, ulong? maxCount)
    {
        long count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            count++;
            if (maxCount is ulong limit && (ulong)count >= limit)
            {
                return count;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        return count;
    }

    internal static bool TryFindNextMultilineMatch(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ref int offset,
        ref int suppressedEmptyStart,
        out RegexMatch match)
    {
        while (offset <= bytes.Length && TryFindMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, offset, out match))
        {
            if (IsTrailingEmptyLineMatch(bytes, match))
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

    internal static int AdvanceAfterReportedMultilineMatch(RegexMatch match, int haystackLength, ref int suppressedEmptyStart)
    {
        return MatchIterator.AdvanceAfterReported(new MatcherMatch(match.Start, match.Length), haystackLength, ref suppressedEmptyStart);
    }

    private static void WriteMultilineMatchedLines(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        int lastWrittenLineStart = -1;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart > lastWrittenLineStart)
                {
                    sink.MatchedLine(firstLineNumber + CountLineFeeds(bytes[firstLineStart..lineStart]), lineStart, matchColumn, bytes[lineStart..lineEnd]);
                    lastWrittenLineStart = lineStart;
                    emitted++;
                    if (maxCount is ulong limit && emitted >= limit)
                    {
                        return;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteMultilineReplacedLines(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        int groupStart = -1;
        int groupEnd = -1;
        List<RegexMatch> groupMatches = [];
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            GetMultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
            if (groupStart >= 0 && rangeStart >= groupEnd)
            {
                WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
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

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        if (groupStart >= 0)
        {
            WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
        }
    }

    private static void WriteMultilineOnlyMatchingReplacements(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            byte[] body = ReplacementFormatter.Expand(replacement.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive);
            int lineStart = GetLineStart(bytes, match.Start);
            WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
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
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive)
    {
        List<int> starts = [];
        List<int> lengths = [];
        for (int index = 0; index < matches.Count; index++)
        {
            starts.Add(matches[index].Start - recordStart);
            lengths.Add(matches[index].Length);
        }

        List<long> replacementColumns = [];
        byte[] body = ReplacementFormatter.ReplaceLine(bytes[recordStart..recordEnd], starts, lengths, replacement.Span, patterns, asciiCaseInsensitive, replacementColumns);
        int lineStart = GetLineStart(bytes, recordStart);
        WriteMultilineReplacementBody(body, recordStart, GetLineNumber(bytes, lineStart), replacementColumns.Count > 0 ? replacementColumns[0] : 1, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
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
        bool nullPathTerminator)
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
            sink.MatchedLine(outputLineNumber, outputByteOffset, outputColumn, body[outputLineStart..outputLineEnd]);
            if (outputLineEnd >= body.Length)
            {
                break;
            }

            outputByteOffset += outputLineEnd - outputLineStart;
            outputLineNumber++;
            outputLineStart = outputLineEnd;
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
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        var sink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (IsEofEmptyMatch(bytes, match))
            {
                int lineEnd = GetLineEnd(bytes, firstLineStart);
                sink.Matched(firstLineNumber, firstLineStart, 1, bytes[firstLineStart..lineEnd]);
            }
            else if (match.Length == 0)
            {
                sink.Matched(firstLineNumber, match.Start, matchColumn, []);
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

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteMultilineVimgrepMatches(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool trim,
        bool nullPathTerminator,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber: true, column: true, byteOffset: false, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int lineStart = GetLineStart(bytes, match.Start);
            int lineEnd = GetLineEnd(bytes, lineStart);
            long lineNumber = GetLineNumber(bytes, lineStart);
            long matchColumn = match.Start - lineStart + 1L;
            sink.MatchedLine(lineNumber, lineStart, matchColumn, bytes[lineStart..lineEnd]);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static bool TryFindMultilineMatch(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall, int startAt, out RegexMatch match)
    {
        match = default;
        bool found = false;
        for (int index = 0; index < patterns.Count; index++)
        {
            var automaton = RegexAutomaton.Compile(patterns[index], asciiCaseInsensitive, multiLine: true, dotMatchesNewline: multilineDotall);
            int candidateStartAt = startAt;
            RegexMatch? candidate = null;
            while (candidateStartAt <= bytes.Length)
            {
                RegexMatch? next = automaton.Find(bytes, candidateStartAt);
                if (!next.HasValue)
                {
                    break;
                }

                int end = next.Value.Start + next.Value.Length;
                if ((!lineRegexp || IsLineMatch(bytes, next.Value.Start, end)) &&
                    (!wordRegexp || IsWordMatch(bytes, next.Value.Start, end)))
                {
                    candidate = next.Value;
                    break;
                }

                candidateStartAt = MatchIterator.AdvanceAfter(new MatcherMatch(next.Value.Start, next.Value.Length), bytes.Length);
            }

            if (candidate.HasValue &&
                (!found ||
                candidate.Value.Start < match.Start ||
                (candidate.Value.Start == match.Start && candidate.Value.Length > match.Length)))
            {
                match = candidate.Value;
                found = true;
            }
        }

        return found;
    }

    internal static List<RegexMatch> CollectMultilineMatches(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
    {
        List<RegexMatch> matches = [];
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
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
            if (maxCount is ulong limit && primaryMatches >= limit)
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
            primaryMatches++;
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

    private static List<ContextLineInfo> BuildMultilineContextLines(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
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
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            MarkMultilineContextMatch(bytes, physicalLines, matchedLines, matchColumns, match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        var lines = new List<ContextLineInfo>(physicalLines.Count);
        for (int index = 0; index < physicalLines.Count; index++)
        {
            ContextLineInfo line = physicalLines[index];
            lines.Add(new ContextLineInfo(line.Start, line.Length, line.LineNumber, matchedLines[index], matchColumns[index], matchedLines[index], contextColumn: 0));
        }

        return lines;
    }

    internal static List<ContextLineInfo> BuildMultilineInvertedLines(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
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
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            MarkMultilineMatchedLines(bytes, physicalLines, matchedLines, match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
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
        long matchColumn = match.Start - firstLineStart + 1L;
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
                sink.Matched(firstLineNumber, firstLineStart, 1, bytes[firstLineStart..lineEnd]);
                wrote = true;
            }
            else if (match.Length == 0)
            {
                sink.Matched(firstLineNumber, match.Start, matchColumn, []);
                wrote = true;
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
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
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
            if (GetLineStart(bytes, match.Start) != line.Start)
            {
                continue;
            }

            byte[] body = ReplacementFormatter.Expand(replacement.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive);
            int lineStart = GetLineStart(bytes, match.Start);
            WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
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
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
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

            if (rangeStart >= groupEnd)
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

        WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
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
            sink.MatchedLine(line.LineNumber, line.Start, match.Start - line.Start + 1L, bytes[line.Start..lineEnd]);
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

    private static bool IsLineMatch(ReadOnlySpan<byte> bytes, int start, int end)
    {
        int firstLineStart = GetLineStart(bytes, start);
        if (start != firstLineStart)
        {
            return false;
        }

        int lastLineStart = GetLineStart(bytes, end > start ? end - 1 : start);
        return end == GetLineContentEnd(bytes, lastLineStart);
    }

    private static bool IsWordMatch(ReadOnlySpan<byte> bytes, int start, int end)
    {
        bool leftIsWord = start > 0 && IsAsciiWordByte(bytes[start - 1]);
        bool rightIsWord = end < bytes.Length && IsAsciiWordByte(bytes[end]);
        return !leftIsWord && !rightIsWord;
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_'
            || (value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z');
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
