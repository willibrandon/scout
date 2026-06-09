
namespace Scout;

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
        bool stopOnNonmatch)
    {
        List<ContextLineInfo> lines = BuildLines(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, stopOnNonmatch);
        if (lines.Count == 0)
        {
            return false;
        }

        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? IncludePassthruLines(lines, included)
            : IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
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

            if (!passthru && wrote && index > previousLineIndex + 1)
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
                pattern,
                asciiCaseInsensitive,
                lineRegexp,
                wordRegexp,
                nullPathTerminator,
                ref lineSink);
            previousLineIndex = index;
            wrote = true;
        }

        return matched;
    }

    internal static List<ContextLineInfo> BuildLines(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        bool stopOnNonmatch = false)
    {
        var lines = new List<ContextLineInfo>();
        bool hasSelectedMatch = false;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            bool originalMatch = TryFindLineMatch(line, pattern, asciiCaseInsensitive, false, lineRegexp, wordRegexp, crlf, nullData, out long originalColumn);
            bool selectedMatch = originalMatch;
            long matchColumn = originalColumn;
            if (invertMatch)
            {
                selectedMatch = TryFindLineMatch(line, pattern, asciiCaseInsensitive, true, lineRegexp, wordRegexp, crlf, nullData, out matchColumn);
            }

            lines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch, matchColumn, originalMatch, originalColumn));
            if (stopOnNonmatch && hasSelectedMatch && !selectedMatch)
            {
                break;
            }

            hasSelectedMatch |= selectedMatch;
            lineStart += lineLength;
            lineNumber++;
        }

        return lines;
    }

    internal static int GetStopOnNonmatchLength(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        bool hasSelectedMatch = false;
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            bool selectedMatch = TryFindLineMatch(line, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out _);
            if (hasSelectedMatch && !selectedMatch)
            {
                return lineStart + lineLength;
            }

            hasSelectedMatch |= selectedMatch;
            lineStart += lineLength;
        }

        return bytes.Length;
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
        out long matchColumn)
    {
        var sink = new FirstLineMatchSink();
        bool matched = LiteralLineSearcher.Search(line, pattern, ref sink, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines: 1, crlf: crlf, nullData: nullData);
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

    private static void WriteContextOutputLine(
        byte[] bytes,
        ContextLineInfo line,
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
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool nullPathTerminator,
        ref StandardSearchSink lineSink)
    {
        ReadOnlySpan<byte> lineBytes = bytes.AsSpan(line.Start, line.Length);
        if (selectedMatch)
        {
            if (onlyMatching && !invertMatch)
            {
                WriteOnlyMatchesForContextLine(lineBytes, line, output, prefix, lineNumber, column, byteOffset, trim, separators.FieldMatch, replacement, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, nullPathTerminator, color, separators.LineTerminator, separators.Crlf, separators.NullData);
                return;
            }

            if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
            {
                var replacementLineSink = new ReplacementLineSink(
                    output,
                    prefix,
                    separators.FieldMatch,
                    replacementValue,
                    pattern,
                    asciiCaseInsensitive,
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
                    separators.LineTerminator);
                LiteralLineSearcher.SearchMatchLines(lineBytes, pattern, ref replacementLineSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
                replacementLineSink.Flush();
                return;
            }

            if (vimgrep && !invertMatch)
            {
                var vimgrepSink = new VimgrepSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, onlyMatching: false, trim, nullPathTerminator, lineLimit, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, color, separators.LineTerminator, line.LineNumber - 1, line.Start);
                LiteralLineSearcher.SearchMatchLines(lineBytes, pattern, ref vimgrepSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
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
                LiteralLineSearcher.SearchMatchLines(lineBytes, pattern, ref coloredSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
                coloredSink.Flush();
                return;
            }

            lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, lineBytes);
            return;
        }

        if (onlyMatching && invertMatch && line.OriginalMatch)
        {
            WriteOnlyMatchesForContextLine(lineBytes, line, output, prefix, lineNumber, column, byteOffset, trim, separators.FieldContext, replacement, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, nullPathTerminator, color, separators.LineTerminator, separators.Crlf, separators.NullData);
            return;
        }

        lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, lineBytes);
    }

    private static void WriteOnlyMatchesForContextLine(
        ReadOnlySpan<byte> lineBytes,
        ContextLineInfo line,
        RawByteWriter output,
        OutputPath? prefix,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        ReadOnlyMemory<byte> fieldSeparator,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool nullPathTerminator,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator,
        bool crlf,
        bool nullData)
    {
        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            var replacementMatchSink = new ReplacementMatchSink(
                output,
                prefix,
                fieldSeparator,
                replacementValue,
                pattern,
                asciiCaseInsensitive,
                lineNumber,
                column,
                byteOffset,
                nullPathTerminator,
                line.LineNumber - 1,
                line.Start,
                color,
                lineTerminator);
            LiteralLineSearcher.SearchMatches(lineBytes, pattern, ref replacementMatchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: crlf, nullData: nullData);
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
            LiteralLineSearcher.SearchMatches(lineBytes, pattern, ref matchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: crlf, nullData: nullData);
        }
    }
}
