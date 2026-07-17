using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Scout;

/// <summary>
/// Searches decoded byte buffers and records standard search statistics.
/// </summary>
internal static class StandardSearchByteOperations
{
    private static readonly byte[] s_lineFeed = [(byte)'\n'];
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);
    internal const int BinaryDetectionBufferLength = 65_536;

    internal static bool SearchBytesWithOptionalHeading(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        bool heading,
        ref bool wroteHeadingOutput,
        bool memoryMapped,
        RegexSearchPlan regexPlan,
        StandardBinaryDetectionScope binaryDetectionScope = StandardBinaryDetectionScope.WholeInput,
        StandardSearchMetrics? metrics = null)
    {
        return SearchBytesWithOptionalHeading(
            bytes,
            bytes.Length,
            pattern,
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
            lineRegexp,
            wordRegexp,
            multiline,
            multilineDotall,
            onlyMatching,
            replacement,
            maxCount,
            textMode,
            quiet,
            trim,
            beforeContext,
            afterContext,
            passthru,
            includeZero,
            nullPathTerminator,
            stopOnNonmatch,
            quitOnBinary,
            heading,
            ref wroteHeadingOutput,
            memoryMapped,
            regexPlan,
            binaryDetectionScope,
            metrics);
    }

    internal static bool SearchBytesWithOptionalHeading(
        byte[] bytes,
        int byteLength,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        bool heading,
        ref bool wroteHeadingOutput,
        bool memoryMapped,
        RegexSearchPlan regexPlan,
        StandardBinaryDetectionScope binaryDetectionScope = StandardBinaryDetectionScope.WholeInput,
        StandardSearchMetrics? metrics = null)
    {
        if (maxCount == 0)
        {
            metrics?.BeginTraversal();
            return false;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteLength, bytes.Length);
        if (binaryDetectionScope == StandardBinaryDetectionScope.SelectedLines)
        {
            return SearchMemoryMappedSelectedLines(
                bytes,
                byteLength,
                pattern,
                output,
                prefix,
                separators,
                lineLimit,
                color,
                lineNumber,
                column,
                byteOffset,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                vimgrep,
                onlyMatching,
                replacement,
                maxCount,
                quiet,
                trim,
                beforeContext,
                afterContext,
                passthru,
                nullPathTerminator,
                stopOnNonmatch,
                heading,
                ref wroteHeadingOutput,
                regexPlan,
                metrics);
        }

        ReadOnlySpan<byte> inputSpan = bytes.AsSpan(0, byteLength);
        if (!heading)
        {
            return SearchBytes(inputSpan, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped, regexPlan, byteLength == bytes.Length ? bytes : null, metrics);
        }

        if (TrySearchBinarySuppressed(inputSpan, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped, regexPlan, metrics, out bool binaryMatched, out _))
        {
            return binaryMatched;
        }

        using MemoryStream bufferedOutput = new();
        var bufferedWriter = new RawByteWriter(bufferedOutput);
        bool matched = SearchBytes(inputSpan, pattern, bufferedWriter, prefix: null, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped, regexPlan, byteLength == bytes.Length ? bytes : null, metrics);
        bufferedWriter.Flush();
        byte[] body = bufferedOutput.ToArray();
        if (body.Length == 0)
        {
            return matched;
        }

        WriteHeadingBody(
            body,
            output,
            prefix,
            separators,
            color,
            nullPathTerminator,
            ref wroteHeadingOutput);
        return matched;
    }

    /// <summary>
    /// Searches a zero-copy memory-mapped byte span.
    /// </summary>
    /// <param name="bytes">The mapped bytes whose lifetime covers this call.</param>
    /// <param name="pattern">The prepared patterns.</param>
    /// <param name="output">The output writer.</param>
    /// <param name="prefix">The optional output path prefix.</param>
    /// <param name="separators">The configured output and record separators.</param>
    /// <param name="lineLimit">The output line limit.</param>
    /// <param name="color">The output color configuration.</param>
    /// <param name="searchMode">The requested search mode.</param>
    /// <param name="vimgrep">Whether vimgrep output is enabled.</param>
    /// <param name="lineNumber">Whether line numbers are printed.</param>
    /// <param name="column">Whether columns are printed.</param>
    /// <param name="byteOffset">Whether byte offsets are printed.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII case-insensitive matching is enabled.</param>
    /// <param name="invertMatch">Whether non-matching lines are selected.</param>
    /// <param name="lineRegexp">Whether matches must span a complete line.</param>
    /// <param name="wordRegexp">Whether matches must satisfy word boundaries.</param>
    /// <param name="multiline">Whether multiline matching is enabled.</param>
    /// <param name="multilineDotall">Whether dot matches line terminators in multiline mode.</param>
    /// <param name="onlyMatching">Whether only matching spans are printed.</param>
    /// <param name="replacement">The optional replacement template.</param>
    /// <param name="maxCount">The optional maximum selected match count.</param>
    /// <param name="textMode">Whether input is searched as text.</param>
    /// <param name="quiet">Whether output is suppressed.</param>
    /// <param name="trim">Whether leading ASCII whitespace is trimmed.</param>
    /// <param name="beforeContext">The number of preceding context lines.</param>
    /// <param name="afterContext">The number of following context lines.</param>
    /// <param name="passthru">Whether every input line is printed.</param>
    /// <param name="includeZero">Whether zero counts are printed.</param>
    /// <param name="nullPathTerminator">Whether paths are NUL terminated.</param>
    /// <param name="stopOnNonmatch">Whether searching stops at the first non-match.</param>
    /// <param name="quitOnBinary">Whether binary detection stops searching.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <returns><see langword="true" /> when the input satisfies the selected search mode.</returns>
    internal static bool SearchMemoryMappedBytes(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        RegexSearchPlan regexPlan)
    {
        return SearchBytes(
            bytes,
            pattern,
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
            lineRegexp,
            wordRegexp,
            multiline,
            multilineDotall,
            onlyMatching,
            replacement,
            maxCount,
            textMode,
            quiet,
            trim,
            beforeContext,
            afterContext,
            passthru,
            includeZero,
            nullPathTerminator,
            stopOnNonmatch,
            quitOnBinary,
            memoryMapped: true,
            regexPlan);
    }

    internal static bool SearchBytesWithStats(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        bool heading,
        ref bool wroteHeadingOutput,
        ref SearchStats stats,
        bool memoryMapped,
        RegexSearchPlan regexPlan,
        StandardBinaryDetectionScope binaryDetectionScope = StandardBinaryDetectionScope.WholeInput)
    {
        var metrics = new StandardSearchMetrics();
        long started = Stopwatch.GetTimestamp();
        using MemoryStream buffer = new();
        var bufferedWriter = new RawByteWriter(buffer);
        bool matched = SearchBytesWithOptionalHeading(bytes, pattern, bufferedWriter, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, heading, ref wroteHeadingOutput, memoryMapped, regexPlan, binaryDetectionScope, metrics);
        bufferedWriter.Flush();
        byte[] body = buffer.ToArray();
        TimeSpan searchElapsed = Stopwatch.GetElapsedTime(started);
        output.Write(body);
        metrics.ValidateCompletedTraversal();

        var fileStats = new SearchStats();
        fileStats.AddElapsed(searchElapsed);
        fileStats.AddSearch();
        fileStats.AddBytesPrinted(searchMode == CliSearchMode.Standard
            ? CountPrintedBodyBytesForStats(body, color)
            : 0);
        fileStats.AddBytesSearched(metrics.BytesSearched);
        if (metrics.MatchedLines > 0)
        {
            fileStats.AddMatchedLines(metrics.MatchedLines);
            fileStats.AddSearchWithMatch();
        }

        fileStats.AddMatches(metrics.Matches);
        stats.Add(fileStats);
        return matched;
    }

    /// <summary>
    /// Searches an explicit memory map once and derives binary handling from selected lines.
    /// </summary>
    private static bool SearchMemoryMappedSelectedLines(
        byte[] bytes,
        int byteLength,
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
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool heading,
        ref bool wroteHeadingOutput,
        RegexSearchPlan regexPlan,
        StandardSearchMetrics? metrics)
    {
        byte[] searchBytes = byteLength == bytes.Length
            ? bytes
            : bytes.AsSpan(0, byteLength).ToArray();
        metrics?.BeginTraversal();
        ContextSearchResult searchResult = ContextSearchOperations.BuildSearchResult(
            searchBytes,
            pattern,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            separators.Crlf,
            separators.NullData,
            stopOnNonmatch,
            regexPlan);
        int binaryOffset = GetSelectedBinaryOffset(
            searchBytes,
            searchResult,
            beforeContext,
            afterContext,
            passthru,
            maxCount);
        int searchLength = stopOnNonmatch
            ? GetContextSearchLength(searchResult)
            : byteLength;
        ContextSearchResult effectiveSearchResult = searchResult;
        bool passthruRequiresReportedMatch = passthru &&
            regexPlan.Options.PreserveCrlfCarriageReturn;
        bool binaryStopsPrefixReplay = binaryOffset >= 0 &&
            (passthru || beforeContext > 0) &&
            !HasSelectedLineBeforeOffset(
                searchResult,
                maxCount,
                binaryOffset,
                passthruRequiresReportedMatch);
        if (binaryStopsPrefixReplay)
        {
            searchLength = GetBinarySafePrefixLength(searchBytes, binaryOffset);
            effectiveSearchResult = SliceContextResult(searchResult, searchLength);
        }

        if (metrics is not null)
        {
            int metricsSearchLength = binaryOffset >= 0 && !binaryStopsPrefixReplay
                ? binaryOffset
                : searchLength;
            CountContextResult(
                effectiveSearchResult,
                invertMatch,
                maxCount,
                checked((ulong)metricsSearchLength),
                metrics);
        }

        bool matched = HasSelectedContextLine(
            effectiveSearchResult,
            maxCount,
            passthruRequiresReportedMatch);
        if (quiet)
        {
            return matched;
        }

        if (binaryStopsPrefixReplay)
        {
            WriteMemoryMappedSelectedLinesResult(
                searchBytes,
                effectiveSearchResult,
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
            if (matched)
            {
                WriteBinaryFileStoppedWarning(output, prefix, color, binaryOffset);
            }

            return matched;
        }

        if (binaryOffset >= 0)
        {
            int outputLength = binaryOffset < BinaryDetectionBufferLength
                ? 0
                : GetLineStart(searchResult, binaryOffset);
            WriteMemoryMappedSelectedLinesResult(
                searchBytes,
                SliceContextResult(searchResult, outputLength),
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
            if (matched)
            {
                WriteBinaryFileMatches(output, prefix, color, binaryOffset);
            }

            return matched;
        }

        if (!heading)
        {
            WriteMemoryMappedSelectedLinesResult(
                searchBytes,
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
            return matched;
        }

        using MemoryStream bufferedOutput = new();
        var bufferedWriter = new RawByteWriter(bufferedOutput);
        WriteMemoryMappedSelectedLinesResult(
            searchBytes,
            searchResult,
            bufferedWriter,
            prefix: null,
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
        bufferedWriter.Flush();
        byte[] body = bufferedOutput.ToArray();
        if (body.Length > 0)
        {
            WriteHeadingBody(
                body,
                output,
                prefix,
                separators,
                color,
                nullPathTerminator,
                ref wroteHeadingOutput);
        }

        return matched;
    }

    private static void WriteMemoryMappedSelectedLinesResult(
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
        OutputSeparators replaySeparators = passthru ||
            beforeContext > 0 ||
            afterContext > 0
                ? separators
                : new OutputSeparators(
                    separators.FieldMatch,
                    separators.FieldContext,
                    separators.Context,
                    contextEnabled: false,
                    separators.LineTerminator);
        _ = ContextSearchOperations.WriteSearchResult(
            bytes,
            searchResult,
            output,
            prefix,
            replaySeparators,
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

    private static int GetSelectedBinaryOffset(
        byte[] bytes,
        ContextSearchResult searchResult,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        ulong? maxCount)
    {
        int initialLength = Math.Min(BinaryDetectionBufferLength, bytes.Length);
        int initialOffset = bytes.AsSpan(0, initialLength).IndexOf((byte)0);
        if (initialOffset >= 0)
        {
            return initialOffset;
        }

        bool[] included = new bool[searchResult.Lines.Count];
        ContextSearchOperations.IncludeOutputLines(
            searchResult.Lines,
            included,
            beforeContext,
            afterContext,
            passthru,
            maxCount);
        for (int index = 0; index < searchResult.Lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            ContextLineInfo line = searchResult.Lines[index];
            int nulOffset = bytes.AsSpan(line.Start, line.Length).IndexOf((byte)0);
            if (nulOffset >= 0)
            {
                return checked(line.Start + nulOffset);
            }
        }

        return -1;
    }

    private static int GetLineStart(
        ContextSearchResult searchResult,
        int offset)
    {
        for (int index = 0; index < searchResult.Lines.Count; index++)
        {
            ContextLineInfo line = searchResult.Lines[index];
            if (offset >= line.Start && offset < line.Start + line.Length)
            {
                return line.Start;
            }
        }

        return offset;
    }

    private static bool HasSelectedLineBeforeOffset(
        ContextSearchResult searchResult,
        ulong? maxCount,
        int offset,
        bool requireReportedMatch)
    {
        ulong selectedLines = 0;
        for (int index = 0; index < searchResult.Lines.Count; index++)
        {
            ContextLineInfo line = searchResult.Lines[index];
            if (!line.SelectedMatch ||
                (requireReportedMatch && line.MatchColumn <= 0))
            {
                continue;
            }

            if (maxCount is ulong limit && selectedLines >= limit)
            {
                break;
            }

            selectedLines++;
            if (line.Start + line.Length <= offset)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteHeadingBody(
        byte[] body,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputColor color,
        bool nullPathTerminator,
        ref bool wroteHeadingOutput)
    {
        if (wroteHeadingOutput)
        {
            output.Write("\n"u8);
        }

        if (prefix is not null)
        {
            prefix.WriteLabel(output, color);
            SearchOutputFormatting.WriteSearchPathTerminator(
                output,
                nullPathTerminator,
                separators.LineTerminator);
        }

        output.Write(body);
        wroteHeadingOutput = true;
    }

    internal static ulong CountPrintedBodyBytesForStats(byte[] body, OutputColor color)
    {
        if (!color.Enabled)
        {
            return (ulong)body.Length;
        }

        ulong count = 0;
        int index = 0;
        while (index < body.Length)
        {
            if (TrySkipAnsiSequence(body, index, out int nextIndex))
            {
                index = nextIndex;
                continue;
            }

            count++;
            index++;
        }

        return count;
    }

    private static bool TrySkipAnsiSequence(ReadOnlySpan<byte> bytes, int start, out int nextIndex)
    {
        nextIndex = start;
        if (start + 1 >= bytes.Length ||
            bytes[start] != 0x1B)
        {
            return false;
        }

        byte introducer = bytes[start + 1];
        if (introducer == (byte)'[')
        {
            for (int index = start + 2; index < bytes.Length; index++)
            {
                byte value = bytes[index];
                if (value is >= 0x40 and <= 0x7E)
                {
                    nextIndex = index + 1;
                    return true;
                }
            }

            return false;
        }

        if (introducer == (byte)']')
        {
            for (int index = start + 2; index < bytes.Length; index++)
            {
                byte value = bytes[index];
                if (value == 0x07)
                {
                    nextIndex = index + 1;
                    return true;
                }

                if (value == 0x1B &&
                    index + 1 < bytes.Length &&
                    bytes[index + 1] == (byte)'\\')
                {
                    nextIndex = index + 2;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SearchBytes(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        bool memoryMapped,
        RegexSearchPlan regexPlan,
        byte[]? sourceBytes = null,
        StandardSearchMetrics? metrics = null)
    {
        return SearchBytes(
            bytes.AsSpan(),
            pattern,
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
            lineRegexp,
            wordRegexp,
            multiline,
            multilineDotall,
            onlyMatching,
            replacement,
            maxCount,
            textMode,
            quiet,
            trim,
            beforeContext,
            afterContext,
            passthru,
            includeZero,
            nullPathTerminator,
            stopOnNonmatch,
            quitOnBinary,
            memoryMapped,
            regexPlan,
            sourceBytes ?? bytes,
            metrics);
    }

    private static bool SearchBytes(
        ReadOnlySpan<byte> inputSpan,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        bool memoryMapped,
        RegexSearchPlan regexPlan,
        byte[]? sourceBytes = null,
        StandardSearchMetrics? metrics = null)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (TrySearchBinarySuppressed(inputSpan, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped, regexPlan, metrics, out bool binaryMatched, out bool convertBinaryNuls))
        {
            return binaryMatched;
        }

        byte[]? convertedBytes = convertBinaryNuls ? BinaryDetection.ConvertNulToLineFeed(inputSpan) : null;
        ReadOnlySpan<byte> activeSpan = convertedBytes is null ? inputSpan : convertedBytes;
        if (metrics is not null || stopOnNonmatch)
        {
            ReadOnlySpan<byte> metricsOutputSpan = convertedBytes is null
                ? activeSpan
                : inputSpan;
            return SearchBytesWithMetrics(
                activeSpan,
                metricsOutputSpan,
                pattern,
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
                lineRegexp,
                wordRegexp,
                multiline,
                multilineDotall,
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
                stopOnNonmatch,
                regexPlan,
                metrics ?? new StandardSearchMetrics());
        }

        ReadOnlySpan<byte> searchSpan = activeSpan;
        ReadOnlySpan<byte> outputSpan = convertedBytes is null
            ? searchSpan
            : inputSpan;

        if (multiline &&
            regexPlan.Options.Multiline &&
            MultilineSearchOperations.TrySearchBytes(
                searchSpan,
                outputSpan,
                pattern,
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
                regexPlan!,
                out bool multilineMatched))
        {
            return multilineMatched;
        }

        if (quiet)
        {
            return SearchModeEvaluation.SearchQuiet(
                searchSpan,
                pattern,
                searchMode,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxCount,
                separators.Crlf,
                separators.NullData,
                regexPlan);
        }

        if (searchMode == CliSearchMode.Count)
        {
            long count;
            if (onlyMatching && !invertMatch)
            {
                count = LiteralLineSearcher.CountMatchesWithRegexPlan(searchSpan, pattern, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            }
            else
            {
                count = LiteralLineSearcher.CountMatchingLinesWithRegexPlan(searchSpan, pattern, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            }

            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            long count = LiteralLineSearcher.CountMatchesWithRegexPlan(searchSpan, pattern, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            bool hasMatch = LiteralLineSearcher.HasMatchWithRegexPlan(searchSpan, pattern, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            return SearchOutputFormatting.WritePathIf(output, prefix, color, hasMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            bool hasMatch = LiteralLineSearcher.HasMatchWithRegexPlan(searchSpan, pattern, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !hasMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (passthru || beforeContext > 0 || afterContext > 0)
        {
            byte[] contextBytes = sourceBytes ?? inputSpan.ToArray();
            return ContextSearchOperations.SearchBytes(contextBytes, pattern, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, vimgrep, onlyMatching, replacement, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator, stopOnNonmatch, regexPlan);
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
        {
            if (onlyMatching)
            {
                var replacementMatchSink = new ReplacementMatchSink(output, prefix, separators.FieldMatch, replacementValue, lineNumber, column, byteOffset, nullPathTerminator, color: color, lineTerminator: separators.LineTerminator, searchPlan: regexPlan);
                try
                {
                    return LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref replacementMatchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
                }
                finally
                {
                    replacementMatchSink.Dispose();
                }
            }

            var replacementLineSink = new ReplacementLineSink(output, prefix, separators.FieldMatch, replacementValue, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, lineLimit, color: color, lineTerminator: separators.LineTerminator, searchPlan: regexPlan, streamPlainBodyDirectly: true);
            try
            {
                bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref replacementLineSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
                replacementLineSink.Flush();
                return matched;
            }
            finally
            {
                replacementLineSink.Dispose();
            }
        }

        if (vimgrep && !invertMatch)
        {
            var vimgrepSink = new VimgrepSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, onlyMatching, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
            return LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref vimgrepSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
        }

        if (onlyMatching && !invertMatch)
        {
            var matchSink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
            return LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref matchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
        }

        if (color.Enabled && !invertMatch)
        {
            var coloredSink = new ColoredSearchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
            bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref coloredSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            coloredSink.Flush();
            return matched;
        }

        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        bool requireMatchColumn = column || prefix?.HasHyperlink == true;
        return LiteralLineSearcher.SearchWithRegexPlan(outputSpan, pattern, regexPlan, ref sink, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData, requireMatchColumn);
    }

    private static bool SearchBytesWithMetrics(
        ReadOnlySpan<byte> searchSpan,
        ReadOnlySpan<byte> outputSpan,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
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
        bool stopOnNonmatch,
        RegexSearchPlan regexPlan,
        StandardSearchMetrics metrics)
    {
        if (multiline &&
            !separators.NullData &&
            regexPlan.Options.Multiline)
        {
            return SearchMultilineBytesWithMetrics(
                searchSpan,
                outputSpan,
                pattern,
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
                stopOnNonmatch,
                regexPlan,
                metrics);
        }

        if (passthru || beforeContext > 0 || afterContext > 0 || stopOnNonmatch)
        {
            byte[] contextBytes = searchSpan.ToArray();
            metrics.BeginTraversal();
            ContextSearchResult searchResult = ContextSearchOperations.BuildSearchResult(
                contextBytes,
                pattern,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                separators.Crlf,
                separators.NullData,
                stopOnNonmatch,
                regexPlan);
            CountContextResult(
                searchResult,
                invertMatch,
                maxCount,
                stopOnNonmatch ? (ulong)GetContextSearchLength(searchResult) : (ulong)searchSpan.Length,
                metrics);
            bool contextMatched = HasSelectedContextLine(searchResult, maxCount);
            if (quiet)
            {
                return contextMatched;
            }

            if (searchMode is CliSearchMode.Count or CliSearchMode.CountMatches)
            {
                ulong count = searchMode == CliSearchMode.CountMatches && !invertMatch
                    ? metrics.Matches
                    : onlyMatching && !invertMatch
                        ? metrics.Matches
                        : metrics.MatchedLines;
                return SearchOutputFormatting.WriteCount(output, prefix, color, checked((long)count), includeZero, nullPathTerminator, separators.LineTerminator);
            }

            if (searchMode == CliSearchMode.FilesWithMatches)
            {
                return SearchOutputFormatting.WritePathIf(output, prefix, color, contextMatched, nullPathTerminator, separators.LineTerminator);
            }

            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                return SearchOutputFormatting.WritePathIf(output, prefix, color, !contextMatched, nullPathTerminator, separators.LineTerminator);
            }

            return ContextSearchOperations.WriteSearchResult(
                contextBytes,
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

        metrics.BeginTraversal();
        if (quiet || searchMode is CliSearchMode.Count or CliSearchMode.CountMatches or CliSearchMode.FilesWithMatches or CliSearchMode.FilesWithoutMatch)
        {
            bool counted = CountSearchResult(
                searchSpan,
                pattern,
                regexPlan,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxCount,
                separators.Crlf,
                separators.NullData,
                metrics);
            if (quiet)
            {
                return counted;
            }

            if (searchMode is CliSearchMode.Count or CliSearchMode.CountMatches)
            {
                ulong count = searchMode == CliSearchMode.CountMatches && !invertMatch
                    ? metrics.Matches
                    : onlyMatching && !invertMatch
                        ? metrics.Matches
                        : metrics.MatchedLines;
                return SearchOutputFormatting.WriteCount(output, prefix, color, checked((long)count), includeZero, nullPathTerminator, separators.LineTerminator);
            }

            bool writePath = searchMode == CliSearchMode.FilesWithMatches
                ? counted
                : !counted;
            return SearchOutputFormatting.WritePathIf(output, prefix, color, writePath, nullPathTerminator, separators.LineTerminator);
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
        {
            if (onlyMatching)
            {
                var inner = new ReplacementMatchSink(output, prefix, separators.FieldMatch, replacementValue, lineNumber, column, byteOffset, nullPathTerminator, color: color, lineTerminator: separators.LineTerminator, searchPlan: regexPlan);
                var sink = new RegexPlanCountingMatchLineSink<ReplacementMatchSink>(inner);
                try
                {
                    bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
                    RecordMatchSinkMetrics(searchSpan.Length, maxCount, sink.MatchedLines, sink.Matches, sink.LastMatchedLineEnd, metrics);
                    return matched;
                }
                finally
                {
                    ReplacementMatchSink completedSink = sink.Inner;
                    completedSink.Dispose();
                }
            }

            var replacementLineSink = new ReplacementLineSink(output, prefix, separators.FieldMatch, replacementValue, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, lineLimit, color: color, lineTerminator: separators.LineTerminator, searchPlan: regexPlan, streamPlainBodyDirectly: true);
            var countingReplacementSink = new RegexPlanCountingMatchLineSink<ReplacementLineSink>(replacementLineSink);
            try
            {
                bool replacementMatched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref countingReplacementSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
                ReplacementLineSink completedSink = countingReplacementSink.Inner;
                completedSink.Flush();
                RecordMatchSinkMetrics(searchSpan.Length, maxCount, countingReplacementSink.MatchedLines, countingReplacementSink.Matches, countingReplacementSink.LastMatchedLineEnd, metrics);
                return replacementMatched;
            }
            finally
            {
                ReplacementLineSink completedSink = countingReplacementSink.Inner;
                completedSink.Dispose();
            }
        }

        if (vimgrep && !invertMatch)
        {
            var inner = new VimgrepSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, onlyMatching, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
            var sink = new RegexPlanCountingMatchLineSink<VimgrepSink>(inner);
            bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            RecordMatchSinkMetrics(searchSpan.Length, maxCount, sink.MatchedLines, sink.Matches, sink.LastMatchedLineEnd, metrics);
            return matched;
        }

        if (onlyMatching && !invertMatch)
        {
            var inner = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
            var sink = new RegexPlanCountingMatchLineSink<StandardMatchSink>(inner);
            bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            RecordMatchSinkMetrics(searchSpan.Length, maxCount, sink.MatchedLines, sink.Matches, sink.LastMatchedLineEnd, metrics);
            return matched;
        }

        if (color.Enabled && !invertMatch)
        {
            var inner = new ColoredSearchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
            var sink = new RegexPlanCountingMatchLineSink<ColoredSearchSink>(inner);
            bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            sink.Inner.Flush();
            RecordMatchSinkMetrics(searchSpan.Length, maxCount, sink.MatchedLines, sink.Matches, sink.LastMatchedLineEnd, metrics);
            return matched;
        }

        var standardSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        if (invertMatch)
        {
            var sink = new RegexPlanCountingLineSink<StandardSearchSink>(standardSink);
            bool matched = LiteralLineSearcher.SearchInvertedWithRegexPlanAndCountBytes(outputSpan, pattern, regexPlan, ref sink, out ulong searchedBytes, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData, requireMatchColumn: false);
            metrics.Record(sink.MatchedLines, matches: 0, searchedBytes);
            return matched;
        }

        var outputSink = new RegexPlanLineOutputMatchSink<StandardSearchSink>(standardSink);
        bool outputMatched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(outputSpan, pattern, regexPlan, ref outputSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
        RecordMatchSinkMetrics(searchSpan.Length, maxCount, outputSink.MatchedLines, checked((long)outputSink.Matches), outputSink.LastMatchedLineEnd, metrics);
        return outputMatched;
    }

    private static bool CountSearchResult(
        ReadOnlySpan<byte> searchSpan,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        bool crlf,
        bool nullData,
        StandardSearchMetrics metrics)
    {
        if (invertMatch)
        {
            var inner = new CountingLineSink();
            var sink = new RegexPlanCountingLineSink<CountingLineSink>(inner);
            bool matched = LiteralLineSearcher.SearchInvertedWithRegexPlanAndCountBytes(searchSpan, pattern, regexPlan, ref sink, out ulong searchedBytes, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, crlf, nullData, requireMatchColumn: false);
            metrics.Record(sink.MatchedLines, matches: 0, searchedBytes);
            return matched;
        }

        var countingSink = new RegexPlanLineOutputMatchSink<CountingLineSink>(new CountingLineSink());
        bool counted = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(searchSpan, pattern, regexPlan, ref countingSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, crlf, nullData);
        RecordMatchSinkMetrics(searchSpan.Length, maxCount, countingSink.MatchedLines, checked((long)countingSink.Matches), countingSink.LastMatchedLineEnd, metrics);
        return counted;
    }

    private static void CountContextResult(
        ContextSearchResult searchResult,
        bool invertMatch,
        ulong? maxCount,
        ulong bytesSearched,
        StandardSearchMetrics metrics)
    {
        ulong matchedLines = 0;
        ulong matches = 0;
        ulong lastMatchedLineEnd = 0;
        foreach (ContextLineInfo line in searchResult.Lines)
        {
            if (!line.SelectedMatch)
            {
                continue;
            }

            matchedLines++;
            lastMatchedLineEnd = checked((ulong)(line.Start + line.Length));
            if (!invertMatch)
            {
                matches += checked((ulong)searchResult.GetMatches(line).Length);
            }

            if (maxCount is ulong limit && matchedLines >= limit)
            {
                if (!invertMatch)
                {
                    bytesSearched = Math.Min(bytesSearched, lastMatchedLineEnd);
                }

                break;
            }
        }

        metrics.Record(matchedLines, matches, bytesSearched);
    }

    private static bool HasSelectedContextLine(
        ContextSearchResult searchResult,
        ulong? maxCount,
        bool requireReportedMatch = false)
    {
        if (maxCount == 0)
        {
            return false;
        }

        for (int index = 0; index < searchResult.Lines.Count; index++)
        {
            ContextLineInfo line = searchResult.Lines[index];
            if (line.SelectedMatch &&
                (!requireReportedMatch || line.MatchColumn > 0))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetContextSearchLength(ContextSearchResult searchResult)
    {
        if (searchResult.Lines.Count == 0)
        {
            return 0;
        }

        ContextLineInfo last = searchResult.Lines[^1];
        return checked(last.Start + last.Length);
    }

    private static void RecordMatchSinkMetrics(
        int searchLength,
        ulong? maxCount,
        ulong matchedLines,
        long matches,
        ulong lastMatchedLineEnd,
        StandardSearchMetrics metrics)
    {
        ulong bytesSearched = maxCount is ulong limit && matchedLines >= limit
            ? lastMatchedLineEnd
            : checked((ulong)searchLength);
        metrics.Record(matchedLines, checked((ulong)matches), bytesSearched);
    }

    private static bool SearchMultilineBytesWithMetrics(
        ReadOnlySpan<byte> searchSpan,
        ReadOnlySpan<byte> outputSpan,
        IReadOnlyList<byte[]> pattern,
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
        bool stopOnNonmatch,
        RegexSearchPlan regexPlan,
        StandardSearchMetrics metrics)
    {
        metrics.BeginTraversal();
        MultilineSearchResult completeResult =
            MultilineSearchOperations.CreateSearchResult(searchSpan, regexPlan);
        int searchLength = searchSpan.Length;
        MultilineSearchResult outputResult = completeResult;
        if (stopOnNonmatch)
        {
            List<ContextLineInfo> retainedLines =
                MultilineSearchOperations.BuildMultilineContextLines(
                    searchSpan,
                    completeResult.Matches,
                    stopOnNonmatch: true);
            if (retainedLines.Count == 0)
            {
                searchLength = 0;
            }
            else
            {
                ContextLineInfo last = retainedLines[^1];
                searchLength = checked(last.Start + last.Length);
            }

            outputResult = SliceMultilineSearchResult(
                completeResult,
                searchLength);
        }

        bool statsInvertMatch = searchMode == CliSearchMode.FilesWithoutMatch
            ? false
            : invertMatch;
        bool contextOutputRequested = searchMode == CliSearchMode.Standard &&
            (beforeContext > 0 || afterContext > 0 || passthru);
        ulong? rendererMaxCount = maxCount;
        ulong? metricsMaxCount = maxCount;
        if (!invertMatch && contextOutputRequested && !passthru)
        {
            outputResult = MultilineSearchOperations.LimitPositiveContextMatches(
                searchSpan[..searchLength],
                outputResult,
                maxCount,
                beforeContext,
                afterContext);
            metricsMaxCount = null;
        }
        else if (!invertMatch)
        {
            outputResult = MultilineSearchOperations.LimitPositiveMatches(
                searchSpan[..searchLength],
                outputResult,
                maxCount);
            rendererMaxCount = null;
            metricsMaxCount = null;
        }

        List<ContextLineInfo> metricLines = statsInvertMatch
            ? MultilineSearchOperations.BuildMultilineInvertedLines(
                searchSpan[..searchLength],
                outputResult.Matches)
            : MultilineSearchOperations.BuildMultilineContextLines(
                searchSpan[..searchLength],
                outputResult.Matches,
                stopOnNonmatch: false);
        ulong matchedLines = 0;
        for (int index = 0; index < metricLines.Count; index++)
        {
            if (!metricLines[index].SelectedMatch)
            {
                continue;
            }

            matchedLines++;
            if (metricsMaxCount is ulong lineLimitValue &&
                matchedLines >= lineLimitValue)
            {
                break;
            }
        }

        ulong matches = statsInvertMatch
            ? 0
            : CountRetainedMultilineMatches(
                searchSpan[..searchLength],
                outputResult.Matches,
                metricLines,
                metricsMaxCount);

        metrics.Record(matchedLines, matches, checked((ulong)searchLength));
        bool handled = MultilineSearchOperations.TryWriteSearchResult(
            searchSpan[..searchLength],
            outputSpan[..Math.Min(searchLength, outputSpan.Length)],
            pattern,
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
            rendererMaxCount,
            quiet,
            trim,
            beforeContext,
            afterContext,
            passthru,
            includeZero,
            nullPathTerminator,
            regexPlan,
            outputResult,
            out bool matched);
        return handled && matched;
    }

    private static bool TrySearchBinarySuppressed(
        ReadOnlySpan<byte> inputSpan,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        bool memoryMapped,
        RegexSearchPlan regexPlan,
        StandardSearchMetrics? metrics,
        out bool matched,
        out bool convertBinaryNuls)
    {
        matched = false;
        convertBinaryNuls = false;
        BinaryDetectionResult binaryDetection = BinaryDetection.Detect(inputSpan, textMode, separators.NullData, quitOnBinary);
        if (!binaryDetection.IsBinary)
        {
            return false;
        }

        if (binaryDetection.Kind == BinaryDetectionKind.Quit)
        {
            ulong binaryStatsLength = GetBinaryQuitStatsLength(
                inputSpan,
                binaryDetection.Offset,
                multiline,
                memoryMapped);
            if (quiet)
            {
                matched = SearchBinarySafePrefix(inputSpan, binaryDetection.Offset, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, regexPlan, metrics);
                RecordBinarySearchExtent(metrics, binaryStatsLength);
                return true;
            }

            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                matched = true;
                RecordBinarySearchExtent(metrics, binaryStatsLength);
                return true;
            }

            if (searchMode is not (CliSearchMode.Standard or CliSearchMode.FilesWithMatches))
            {
                RecordBinarySearchExtent(metrics, binaryStatsLength);
                return true;
            }

            matched = SearchBinarySafePrefix(inputSpan, binaryDetection.Offset, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, regexPlan, metrics);
            RecordBinarySearchExtent(metrics, binaryStatsLength);
            if (matched && searchMode == CliSearchMode.Standard)
            {
                WriteBinaryFileStoppedWarning(output, prefix, color, binaryDetection.Offset);
            }

            return true;
        }

        if (quiet)
        {
            byte[] convertedBytes = BinaryDetection.ConvertNulToLineFeed(inputSpan);
            matched = SearchBytes(convertedBytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode: true, quiet: true, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary: false, memoryMapped: false, regexPlan, metrics: metrics);
            return true;
        }

        if (searchMode != CliSearchMode.Standard)
        {
            convertBinaryNuls = true;
            return false;
        }

        if (!memoryMapped && !multiline)
        {
            matched = BufferedBinaryContextSearchOperations.Search(
                inputSpan,
                binaryDetection.Offset,
                pattern,
                output,
                prefix,
                separators,
                lineLimit,
                color,
                vimgrep,
                lineNumber,
                column,
                byteOffset,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                onlyMatching,
                replacement,
                maxCount,
                trim,
                beforeContext,
                afterContext,
                passthru,
                nullPathTerminator,
                stopOnNonmatch,
                regexPlan,
                metrics);
            return true;
        }

        if (passthru || beforeContext > 0 || afterContext > 0)
        {
            if (memoryMapped)
            {
                byte[] convertedBytes = BinaryDetection.ConvertNulToLineFeed(inputSpan);
                matched = SearchBytes(convertedBytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode: true, quiet: true, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary: false, memoryMapped: false, regexPlan, metrics: metrics);
            }
            else
            {
                int safeLength = GetBinarySafePrefixLength(
                    inputSpan,
                    binaryDetection.Offset);
                matched = SearchBytes(inputSpan[..safeLength], pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode: true, quiet: false, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary: false, memoryMapped: false, regexPlan, metrics: metrics);
            }
        }
        else
        {
            byte[] convertedBytes = BinaryDetection.ConvertNulToLineFeed(inputSpan);
            matched = SearchConvertedBinaryStandard(
                convertedBytes,
                inputSpan,
                binaryDetection.Offset,
                pattern,
                output,
                prefix,
                separators,
                lineLimit,
                color,
                vimgrep,
                lineNumber,
                column,
                byteOffset,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                onlyMatching,
                replacement,
                maxCount,
                trim,
                nullPathTerminator,
                stopOnNonmatch,
                regexPlan,
                metrics);
        }

        if (matched)
        {
            WriteBinaryFileMatches(output, prefix, color, binaryDetection.Offset);
        }

        return true;
    }

    private static bool SearchConvertedBinaryStandard(
        byte[] convertedBytes,
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
        bool nullPathTerminator,
        bool stopOnNonmatch,
        RegexSearchPlan regexPlan,
        StandardSearchMetrics? metrics)
    {
        StandardSearchMetrics operationMetrics = metrics ?? new StandardSearchMetrics();
        int safeLength = GetBinarySafePrefixLength(originalBytes, binaryOffset);
        if (regexPlan.Options.Multiline)
        {
            operationMetrics.BeginTraversal();
            MultilineSearchResult searchResult =
                MultilineSearchOperations.CreateSearchResult(
                    convertedBytes,
                    regexPlan);
            MultilineSearchResult retainedResult = searchResult;
            ulong? rendererMaxCount = maxCount;
            if (!invertMatch)
            {
                if (stopOnNonmatch)
                {
                    List<ContextLineInfo> stoppedLines =
                        MultilineSearchOperations.BuildMultilineContextLines(
                            convertedBytes,
                            searchResult.Matches,
                            stopOnNonmatch: true);
                    int retainedSearchLength = stoppedLines.Count == 0
                        ? 0
                        : checked(stoppedLines[^1].Start + stoppedLines[^1].Length);
                    retainedResult = SliceMultilineSearchResult(
                        retainedResult,
                        retainedSearchLength);
                }

                retainedResult = MultilineSearchOperations.LimitPositiveMatches(
                    convertedBytes,
                    retainedResult,
                    maxCount);
                rendererMaxCount = null;
            }

            List<ContextLineInfo> metricLines = invertMatch
                ? MultilineSearchOperations.BuildMultilineInvertedLines(
                    convertedBytes,
                    retainedResult.Matches)
                : MultilineSearchOperations.BuildMultilineContextLines(
                    convertedBytes,
                    retainedResult.Matches,
                    stopOnNonmatch: false);
            ulong matchedLines = CountSelectedLines(
                metricLines,
                rendererMaxCount);
            ulong matchCount = invertMatch
                ? 0
                : CountRetainedMultilineMatches(
                    convertedBytes,
                    retainedResult.Matches,
                    metricLines,
                    rendererMaxCount);

            operationMetrics.Record(
                matchedLines,
                matchCount,
                checked((ulong)convertedBytes.Length));
            if (safeLength > 0)
            {
                var safeMatches = new List<RegexMatch>();
                for (int index = 0; index < retainedResult.Matches.Count; index++)
                {
                    RegexMatch match = retainedResult.Matches[index];
                    if (MultilineSearchOperations.GetInclusiveMatchEnd(match) >= safeLength)
                    {
                        continue;
                    }

                    safeMatches.Add(match);
                }

                MultilineSearchOperations.TryWriteSearchResult(
                    convertedBytes.AsSpan(0, safeLength),
                    originalBytes[..safeLength],
                    pattern,
                    output,
                    prefix,
                    separators,
                    lineLimit,
                    color,
                    CliSearchMode.Standard,
                    vimgrep,
                    lineNumber,
                    column,
                    byteOffset,
                    asciiCaseInsensitive,
                    invertMatch,
                    onlyMatching,
                    replacement,
                    rendererMaxCount,
                    quiet: false,
                    trim,
                    beforeContext: 0,
                    afterContext: 0,
                    passthru: false,
                    includeZero: false,
                    nullPathTerminator,
                    regexPlan,
                    new MultilineSearchResult(safeMatches),
                    out _);
            }

            return matchedLines > 0;
        }

        operationMetrics.BeginTraversal();
        ContextSearchResult contextResult = ContextSearchOperations.BuildSearchResult(
            convertedBytes,
            pattern,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            separators.Crlf,
            separators.NullData,
            stopOnNonmatch,
            regexPlan);
        CountContextResult(
            contextResult,
            invertMatch,
            maxCount,
            checked((ulong)convertedBytes.Length),
            operationMetrics);
        bool matched = HasSelectedContextLine(contextResult, maxCount);
        if (safeLength > 0)
        {
            ContextSearchResult safeResult = SliceContextResult(
                contextResult,
                safeLength);
            ContextSearchOperations.WriteSearchResult(
                originalBytes[..safeLength].ToArray(),
                safeResult,
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
                beforeContext: 0,
                afterContext: 0,
                passthru: false,
                nullPathTerminator,
                regexPlan);
        }

        return matched;
    }

    private static ulong CountSelectedLines(
        List<ContextLineInfo> lines,
        ulong? maxCount)
    {
        ulong count = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!lines[index].SelectedMatch)
            {
                continue;
            }

            count++;
            if (maxCount is ulong limit && count >= limit)
            {
                break;
            }
        }

        return count;
    }

    private static ulong CountRetainedMultilineMatches(
        ReadOnlySpan<byte> bytes,
        List<RegexMatch> matches,
        List<ContextLineInfo> lines,
        ulong? maxCount)
    {
        int lastRetainedLineStart = -1;
        ulong retainedLines = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            ContextLineInfo line = lines[index];
            if (!line.SelectedMatch)
            {
                continue;
            }

            if (maxCount is ulong limit && retainedLines >= limit)
            {
                break;
            }

            retainedLines++;
            lastRetainedLineStart = line.Start;
        }

        if (lastRetainedLineStart < 0)
        {
            return 0;
        }

        ulong count = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            RegexMatch match = matches[index];
            if (index > 0 && IsUnterminatedEofEmptyMatch(bytes, match))
            {
                continue;
            }

            int lineStart = MultilineSearchOperations.GetLineStart(
                bytes,
                match.Start);
            if (lineStart > lastRetainedLineStart)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool IsUnterminatedEofEmptyMatch(
        ReadOnlySpan<byte> bytes,
        RegexMatch match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            !bytes.IsEmpty &&
            bytes[^1] != (byte)'\n';
    }

    private static MultilineSearchResult SliceMultilineSearchResult(
        MultilineSearchResult searchResult,
        int exclusiveLength)
    {
        var matches = new List<RegexMatch>();
        for (int index = 0; index < searchResult.Matches.Count; index++)
        {
            RegexMatch match = searchResult.Matches[index];
            if (match.Start > exclusiveLength ||
                (match.Start == exclusiveLength && match.Length > 0))
            {
                break;
            }

            matches.Add(match);
        }

        return new MultilineSearchResult(matches);
    }

    private static ContextSearchResult SliceContextResult(
        ContextSearchResult searchResult,
        int byteLength)
    {
        var lines = new List<ContextLineInfo>();
        var ranges = new List<ContextLineMatchRange>();
        var matches = new List<ContextLineMatch>();
        for (int index = 0; index < searchResult.Lines.Count; index++)
        {
            ContextLineInfo line = searchResult.Lines[index];
            if (line.Start + line.Length > byteLength)
            {
                break;
            }

            lines.Add(line);
            int matchStart = matches.Count;
            ReadOnlySpan<ContextLineMatch> lineMatches = searchResult.GetMatches(line);
            for (int matchIndex = 0; matchIndex < lineMatches.Length; matchIndex++)
            {
                matches.Add(lineMatches[matchIndex]);
            }

            ranges.Add(new ContextLineMatchRange(
                matchStart,
                matches.Count - matchStart));
        }

        return new ContextSearchResult(
            lines,
            ranges.ToArray(),
            matches.ToArray());
    }

    private static ulong GetBinaryQuitStatsLength(
        ReadOnlySpan<byte> bytes,
        int binaryOffset,
        bool multiline,
        bool memoryMapped)
    {
        if (multiline || memoryMapped)
        {
            return binaryOffset < BinaryDetectionBufferLength
                ? 0
                : checked((ulong)bytes.Length);
        }

        return checked((ulong)GetBinarySafePrefixLength(bytes, binaryOffset));
    }

    private static void RecordBinarySearchExtent(
        StandardSearchMetrics? metrics,
        ulong bytesSearched)
    {
        if (metrics is null || bytesSearched <= metrics.BytesSearched)
        {
            if (metrics?.AuthoritativeTraversalCount == 0)
            {
                metrics.BeginTraversal();
            }

            return;
        }

        if (metrics.AuthoritativeTraversalCount == 0)
        {
            metrics.BeginTraversal();
        }

        metrics.Record(0, 0, bytesSearched - metrics.BytesSearched);
    }

    private static bool SearchBinarySafePrefix(
        ReadOnlySpan<byte> bytes,
        int binaryOffset,
        IReadOnlyList<byte[]> pattern,
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
        bool multiline,
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
        bool stopOnNonmatch,
        RegexSearchPlan regexPlan,
        StandardSearchMetrics? metrics)
    {
        int safeLength = GetBinarySafePrefixLength(bytes, binaryOffset);
        if (safeLength == 0)
        {
            return false;
        }

        byte[] safeBytes = bytes[..safeLength].ToArray();
        return SearchBytes(safeBytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode: true, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary: false, memoryMapped: false, regexPlan, metrics: metrics);
    }

    internal static int GetBinarySafePrefixLength(byte[] bytes, int binaryOffset)
    {
        return GetBinarySafePrefixLength(bytes.AsSpan(), binaryOffset);
    }

    private static int GetBinarySafePrefixLength(ReadOnlySpan<byte> bytes, int binaryOffset)
    {
        int length = binaryOffset - (binaryOffset % BinaryDetectionBufferLength);
        if (length <= 0)
        {
            return 0;
        }

        length = Math.Min(length, bytes.Length);
        int lastLineFeed = bytes[..length].LastIndexOf((byte)'\n');
        return lastLineFeed < 0 ? 0 : lastLineFeed + 1;
    }

    internal static void WriteBinaryFileMatches(RawByteWriter output, OutputPath? prefix, OutputColor color, long binaryOffset)
    {
        if (prefix is not null)
        {
            prefix.WriteLabel(output, color);
            output.Write(": "u8);
        }

        output.Write("binary file matches (found \"\\0\" byte around offset "u8);
        output.Write(s_utf8.GetBytes(binaryOffset.ToString(CultureInfo.InvariantCulture)));
        output.Write(")"u8);
        output.Write(s_lineFeed);
    }

    internal static void WriteBinaryFileStoppedWarning(RawByteWriter output, OutputPath? prefix, OutputColor color, long binaryOffset)
    {
        if (prefix is not null)
        {
            prefix.WriteLabel(output, color);
            output.Write(": "u8);
        }

        output.Write("WARNING: stopped searching binary file after match (found \"\\0\" byte around offset "u8);
        output.Write(s_utf8.GetBytes(binaryOffset.ToString(CultureInfo.InvariantCulture)));
        output.Write(")"u8);
        output.Write(s_lineFeed);
    }
}
