
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
        RegexSearchPlan? regexPlan = null)
    {
        regexPlan ??= CreateRegexPlan(
            pattern,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            separators.Crlf,
            separators.NullData);
        List<ContextLineInfo> lines = BuildLines(
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
        if (lines.Count == 0)
        {
            return false;
        }

        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? IncludePassthruLines(lines, included)
            : IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
        bool passthruRequiresReportedMatch = passthru &&
            regexPlan?.Options.PreserveCrlfCarriageReturn == true;
        if (passthruRequiresReportedMatch)
        {
            matched = false;
            for (int index = 0; index < lines.Count; index++)
            {
                matched |= lines[index].SelectedMatch && lines[index].MatchColumn > 0;
            }
        }

        var lineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        ReplacementCapturePlan? replacementCapturePlan = replacement.HasValue
            ? ReplacementCapturePlan.TryCreate(
                pattern,
                new RegexSearchPlanOptions(
                    asciiCaseInsensitive,
                    lineRegexp,
                    wordRegexp,
                    separators.Crlf,
                    separators.NullData),
                regexPlan)
            : null;
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
                replacementCapturePlan,
                invertMatch,
                pattern,
                asciiCaseInsensitive,
                lineRegexp,
                wordRegexp,
                nullPathTerminator,
                regexPlan,
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
        bool stopOnNonmatch = false,
        RegexSearchPlan? regexPlan = null)
    {
        regexPlan ??= CreateRegexPlan(
            pattern,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        if (!stopOnNonmatch)
        {
            return BuildLinesFromMatches(
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

        var lines = new List<ContextLineInfo>();
        bool hasSelectedMatch = false;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            bool originalMatch = TryFindLineMatch(
                line,
                pattern,
                asciiCaseInsensitive,
                invertMatch: false,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData,
                out long originalColumn,
                regexPlan);
            bool selectedMatch = originalMatch;
            long matchColumn = originalColumn;
            if (invertMatch)
            {
                selectedMatch = TryFindLineMatch(
                    line,
                    pattern,
                    asciiCaseInsensitive,
                    invertMatch: true,
                    lineRegexp,
                    wordRegexp,
                    crlf,
                    nullData,
                    out matchColumn,
                    regexPlan);
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

    private static List<ContextLineInfo> BuildLinesFromMatches(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        RegexSearchPlan? regexPlan)
    {
        var matchingLines = new List<ContextLineInfo>();
        var sink = new ContextLineMatchSink(matchingLines);
        LiteralLineSearcher.SearchWithRegexPlan(
            bytes,
            pattern,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            invertMatch: false,
            lineRegexp,
            wordRegexp,
            maxMatchingLines: null,
            crlf,
            nullData);

        var lines = new List<ContextLineInfo>();
        int matchingIndex = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = GetLineLength(bytes.AsSpan(lineStart), nullData);
            bool originalMatch = matchingIndex < matchingLines.Count &&
                matchingLines[matchingIndex].Start == lineStart;
            long originalColumn = originalMatch
                ? matchingLines[matchingIndex++].ContextColumn
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
        return GetStopOnNonmatchLength(
            bytes.AsSpan(),
            pattern,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
    }

    internal static int GetStopOnNonmatchLength(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        RegexSearchPlan? regexPlan = null)
    {
        regexPlan ??= CreateRegexPlan(
            pattern,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData);
        bool hasSelectedMatch = false;
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            bool selectedMatch = TryFindLineMatch(
                line,
                pattern,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData,
                out _,
                regexPlan);
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
        out long matchColumn,
        RegexSearchPlan? regexPlan = null)
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

    private static RegexSearchPlan? CreateRegexPlan(
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        return RegexSearchPlan.Create(
            pattern,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData));
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
        ReplacementCapturePlan? replacementCapturePlan,
        bool invertMatch,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool nullPathTerminator,
        RegexSearchPlan? regexPlan,
        ref StandardSearchSink lineSink)
    {
        ReadOnlySpan<byte> lineBytes = bytes.AsSpan(line.Start, line.Length);
        if (selectedMatch)
        {
            if (onlyMatching && !invertMatch)
            {
                WriteOnlyMatchesForContextLine(lineBytes, line, output, prefix, lineNumber, column, byteOffset, trim, separators.FieldMatch, replacement, replacementCapturePlan, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, nullPathTerminator, color, separators.LineTerminator, separators.Crlf, separators.NullData, regexPlan);
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
                    separators.LineTerminator,
                    replacementCapturePlan);
                LiteralLineSearcher.SearchMatchLinesWithRegexPlan(lineBytes, pattern, regexPlan, ref replacementLineSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
                replacementLineSink.Flush();
                return;
            }

            if (vimgrep && !invertMatch)
            {
                var vimgrepSink = new VimgrepSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, onlyMatching: false, trim, nullPathTerminator, lineLimit, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, color, separators.LineTerminator, line.LineNumber - 1, line.Start);
                LiteralLineSearcher.SearchMatchLinesWithRegexPlan(lineBytes, pattern, regexPlan, ref vimgrepSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
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
                LiteralLineSearcher.SearchMatchLinesWithRegexPlan(lineBytes, pattern, regexPlan, ref coloredSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
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
                WriteOnlyMatchesForContextLine(lineBytes, line, output, prefix, lineNumber, column, byteOffset, trim, separators.FieldContext, replacement, replacementCapturePlan, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, nullPathTerminator, color, separators.LineTerminator, separators.Crlf, separators.NullData, regexPlan);
                return;
            }

            if (vimgrep)
            {
                var vimgrepSink = new VimgrepSink(output, prefix, separators.FieldContext, lineNumber, column, byteOffset, onlyMatching: false, trim, nullPathTerminator, lineLimit, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, color, separators.LineTerminator, line.LineNumber - 1, line.Start);
                LiteralLineSearcher.SearchMatchLinesWithRegexPlan(lineBytes, pattern, regexPlan, ref vimgrepSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
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
                LiteralLineSearcher.SearchMatchLinesWithRegexPlan(lineBytes, pattern, regexPlan, ref coloredSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
                coloredSink.Flush();
                return;
            }
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
        ReplacementCapturePlan? replacementCapturePlan,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool nullPathTerminator,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator,
        bool crlf,
        bool nullData,
        RegexSearchPlan? regexPlan)
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
                lineTerminator,
                replacementCapturePlan);
            LiteralLineSearcher.SearchMatchLinesWithRegexPlan(lineBytes, pattern, regexPlan, ref replacementMatchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: crlf, nullData: nullData);
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
            LiteralLineSearcher.SearchMatchLinesWithRegexPlan(lineBytes, pattern, regexPlan, ref matchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: crlf, nullData: nullData);
        }
    }
}
