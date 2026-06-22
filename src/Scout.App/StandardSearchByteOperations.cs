using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Scout;

internal static class StandardSearchByteOperations
{
    private static readonly byte[] LineFeed = [(byte)'\n'];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
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
        bool memoryMapped = false)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (!heading)
        {
            return SearchBytes(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped);
        }

        if (TrySearchBinarySuppressed(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped, out bool binaryMatched, out _))
        {
            return binaryMatched;
        }

        using MemoryStream bufferedOutput = new();
        var bufferedWriter = new RawByteWriter(bufferedOutput);
        bool matched = SearchBytes(bytes, pattern, bufferedWriter, prefix: null, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped);
        bufferedWriter.Flush();
        byte[] body = bufferedOutput.ToArray();
        if (body.Length == 0)
        {
            return matched;
        }

        if (wroteHeadingOutput)
        {
            output.Write("\n"u8);
        }

        if (prefix is not null)
        {
            prefix.WriteLabel(output, color);
            SearchOutputFormatting.WriteSearchPathTerminator(output, nullPathTerminator, separators.LineTerminator);
        }

        output.Write(body);
        wroteHeadingOutput = true;
        return matched;
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
        bool memoryMapped = false)
    {
        if (TrySearchBytesWithStatsFastPath(
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
            onlyMatching,
            replacement,
            maxCount,
            textMode,
            quiet,
            trim,
            beforeContext,
            afterContext,
            passthru,
            nullPathTerminator,
            stopOnNonmatch,
            quitOnBinary,
            heading,
            ref stats,
            out bool fastMatched))
        {
            return fastMatched;
        }

        long started = Stopwatch.GetTimestamp();
        using MemoryStream buffer = new();
        var bufferedWriter = new RawByteWriter(buffer);
        bool matched = SearchBytesWithOptionalHeading(bytes, pattern, bufferedWriter, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, heading, ref wroteHeadingOutput, memoryMapped);
        bufferedWriter.Flush();
        byte[] body = buffer.ToArray();
        TimeSpan searchElapsed = Stopwatch.GetElapsedTime(started);
        output.Write(body);

        SearchStats fileStats = CollectSearchStats(bytes, pattern, searchMode, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, separators.Crlf, separators.NullData, maxCount, stopOnNonmatch, searchMode == CliSearchMode.Standard ? CountPrintedBodyBytesForStats(body, color) : 0, searchElapsed);
        stats.Add(fileStats);
        return matched;
    }

    private static bool TrySearchBytesWithStatsFastPath(
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
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool quitOnBinary,
        bool heading,
        ref SearchStats stats,
        out bool matched)
    {
        matched = false;
        if (searchMode != CliSearchMode.Standard ||
            vimgrep ||
            invertMatch ||
            lineRegexp ||
            wordRegexp ||
            multiline ||
            onlyMatching ||
            replacement is not null ||
            maxCount is not null ||
            textMode ||
            quiet ||
            beforeContext > 0 ||
            afterContext > 0 ||
            passthru ||
            stopOnNonmatch ||
            heading ||
            separators.Crlf ||
            separators.NullData)
        {
            return false;
        }

        BinaryDetectionResult binaryDetection = BinaryDetection.Detect(bytes, textMode, separators.NullData, quitOnBinary);
        if (binaryDetection.IsBinary)
        {
            return false;
        }

        RegexSearchPlan? regexPlan = LiteralLineSearcher.CreateRegexSearchPlan(pattern, asciiCaseInsensitive, compileAutomata: true);
        if (regexPlan is null)
        {
            return false;
        }

        long started = Stopwatch.GetTimestamp();
        using MemoryStream buffer = new();
        var bufferedWriter = new RawByteWriter(buffer);
        ulong matchedLines;
        long matches;
        if (color.Enabled)
        {
            var coloredSink = new ColoredSearchSink(
                bufferedWriter,
                prefix,
                separators.FieldMatch,
                lineNumber,
                column,
                byteOffset,
                trim,
                nullPathTerminator,
                lineLimit,
                color,
                separators.LineTerminator);
            var countingSink = new RegexPlanCountingMatchLineSink<ColoredSearchSink>(coloredSink);
            matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
                bytes,
                pattern,
                regexPlan,
                ref countingSink,
                asciiCaseInsensitive,
                lineRegexp: false,
                wordRegexp: false,
                maxMatchingLines: null,
                crlf: false,
                nullData: false);
            coloredSink = countingSink.Inner;
            coloredSink.Flush();
            matchedLines = countingSink.MatchedLines;
            matches = countingSink.Matches;
        }
        else
        {
            var sink = new StandardSearchSink(
                bufferedWriter,
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
            bool requireMatchColumn = column || prefix?.HasHyperlink == true;
            matched = LiteralLineSearcher.SearchWithRegexPlanAndCountMatches(
                bytes,
                pattern,
                regexPlan,
                ref sink,
                out matchedLines,
                out matches,
                asciiCaseInsensitive,
                invertMatch: false,
                lineRegexp: false,
                wordRegexp: false,
                maxMatchingLines: null,
                crlf: false,
                nullData: false,
                requireMatchColumn);
        }

        bufferedWriter.Flush();
        byte[] body = buffer.ToArray();
        TimeSpan searchElapsed = Stopwatch.GetElapsedTime(started);
        output.Write(body);

        var fileStats = new SearchStats();
        fileStats.AddElapsed(searchElapsed);
        fileStats.AddSearch();
        fileStats.AddBytesPrinted(CountPrintedBodyBytesForStats(body, color));
        fileStats.AddBytesSearched((ulong)bytes.Length);
        if (matchedLines > 0)
        {
            fileStats.AddMatchedLines(matchedLines);
            fileStats.AddSearchWithMatch();
        }

        fileStats.AddMatches((ulong)matches);
        stats.Add(fileStats);
        return true;
    }

    private static ulong CountPrintedBodyBytesForStats(byte[] body, OutputColor color)
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

    private static SearchStats CollectSearchStats(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        CliSearchMode searchMode,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool multiline,
        bool multilineDotall,
        bool crlf,
        bool nullData,
        ulong? maxCount,
        bool stopOnNonmatch,
        ulong bytesPrinted,
        TimeSpan elapsed)
    {
        var stats = new SearchStats();
        stats.AddElapsed(elapsed);
        stats.AddSearch();
        stats.AddBytesPrinted(bytesPrinted);

        bool statsInvertMatch = searchMode == CliSearchMode.FilesWithoutMatch ? false : invertMatch;
        if (multiline &&
            !crlf &&
            !nullData &&
            PatternPreparation.ShouldUseMultilineRegex(pattern, multilineDotall))
        {
            List<RegexMatch> matches = MultilineSearchOperations.TryCollectSignatureArityMatches(bytes, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, out List<RegexMatch> acceleratedMatches)
                ? acceleratedMatches
                : MultilineSearchOperations.CollectMultilineMatches(bytes, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
            List<ContextLineInfo> multilineLines = statsInvertMatch
                ? MultilineSearchOperations.BuildMultilineInvertedLines(bytes, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall)
                : MultilineSearchOperations.BuildMultilineContextLines(bytes, matches, stopOnNonmatch);
            ulong matchedLines = 0;
            for (int index = 0; index < multilineLines.Count; index++)
            {
                if (!multilineLines[index].SelectedMatch)
                {
                    continue;
                }

                matchedLines++;
                if (maxCount is ulong limit && matchedLines >= limit)
                {
                    break;
                }
            }

            if (matchedLines > 0)
            {
                stats.AddMatchedLines(matchedLines);
                stats.AddSearchWithMatch();
            }

            if (!statsInvertMatch)
            {
                ulong matchCount = (ulong)matches.Count;
                if (maxCount is ulong limit && matchCount > limit)
                {
                    matchCount = limit;
                }

                stats.AddMatches(matchCount);
            }

            stats.AddBytesSearched((ulong)bytes.Length);
            return stats;
        }

        if (!stopOnNonmatch && maxCount is null)
        {
            RegexSearchPlan? regexPlan = LiteralLineSearcher.CreateRegexSearchPlan(pattern, asciiCaseInsensitive, compileAutomata: true);
            long matchedLines = LiteralLineSearcher.CountMatchingLinesWithRegexPlan(
                bytes,
                pattern,
                regexPlan,
                asciiCaseInsensitive,
                statsInvertMatch,
                lineRegexp,
                wordRegexp,
                maxMatchingLines: null,
                crlf,
                nullData);
            if (matchedLines > 0)
            {
                stats.AddMatchedLines((ulong)matchedLines);
                stats.AddSearchWithMatch();
            }

            if (!statsInvertMatch)
            {
                long matches = LiteralLineSearcher.CountMatchesWithRegexPlan(
                    bytes,
                    pattern,
                    regexPlan,
                    asciiCaseInsensitive,
                    invertMatch: false,
                    lineRegexp,
                    wordRegexp,
                    maxMatchingLines: null,
                    crlf,
                    nullData);
                stats.AddMatches((ulong)matches);
            }

            stats.AddBytesSearched((ulong)bytes.Length);
            return stats;
        }

        List<ContextLineInfo> lines = ContextSearchOperations.BuildLines(bytes, pattern, asciiCaseInsensitive, statsInvertMatch, lineRegexp, wordRegexp, crlf, nullData, stopOnNonmatch);
        ulong primaryMatches = 0;
        ulong bytesSearched = (ulong)bytes.Length;
        for (int index = 0; index < lines.Count; index++)
        {
            ContextLineInfo line = lines[index];
            if (!line.SelectedMatch)
            {
                continue;
            }

            stats.AddMatchedLine();
            if (!statsInvertMatch)
            {
                ReadOnlySpan<byte> lineBytes = bytes.AsSpan(line.Start, line.Length);
                stats.AddMatches((ulong)LiteralLineSearcher.CountLineMatches(lineBytes, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData));
            }

            primaryMatches++;
            if (maxCount is ulong limit && primaryMatches >= limit)
            {
                bytesSearched = (ulong)(line.Start + line.Length);
                break;
            }
        }

        if (stats.MatchedLines > 0)
        {
            stats.AddSearchWithMatch();
        }

        stats.AddBytesSearched(bytesSearched);
        return stats;
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
        bool memoryMapped = false)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (TrySearchBinarySuppressed(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, memoryMapped, out bool binaryMatched, out bool convertBinaryNuls))
        {
            return binaryMatched;
        }

        byte[] searchBytes = convertBinaryNuls ? BinaryDetection.ConvertNulToLineFeed(bytes) : bytes;
        int stopLength = stopOnNonmatch
            ? ContextSearchOperations.GetStopOnNonmatchLength(searchBytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, separators.Crlf, separators.NullData)
            : searchBytes.Length;
        ReadOnlySpan<byte> searchSpan = searchBytes.AsSpan(0, stopLength);
        ReadOnlySpan<byte> outputSpan = ReferenceEquals(bytes, searchBytes)
            ? searchSpan
            : bytes.AsSpan(0, stopLength);

        if (multiline &&
            PatternPreparation.ShouldUseMultilineRegex(pattern, multilineDotall) &&
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
                lineRegexp,
                wordRegexp,
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
                out bool multilineMatched))
        {
            return multilineMatched;
        }

        if (quiet)
        {
            return SearchModeEvaluation.SearchQuiet(searchSpan, pattern, searchMode, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
        }

        if (searchMode == CliSearchMode.Count)
        {
            long count;
            if (onlyMatching && !invertMatch)
            {
                count = LiteralLineSearcher.CountMatches(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            }
            else
            {
                RegexSearchPlan? regexPlan = LiteralLineSearcher.CreateRegexSearchPlan(pattern, asciiCaseInsensitive, compileAutomata: true);
                count = LiteralLineSearcher.CountMatchingLinesWithRegexPlan(searchSpan, pattern, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            }

            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            long count = LiteralLineSearcher.CountMatches(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, LiteralLineSearcher.HasMatch(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData), nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !LiteralLineSearcher.HasMatch(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData), nullPathTerminator, separators.LineTerminator);
        }

        if (passthru || beforeContext > 0 || afterContext > 0)
        {
            return ContextSearchOperations.SearchBytes(bytes, pattern, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, vimgrep, onlyMatching, replacement, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator, stopOnNonmatch);
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
        {
            if (onlyMatching)
            {
                var replacementMatchSink = new ReplacementMatchSink(output, prefix, separators.FieldMatch, replacementValue, pattern, asciiCaseInsensitive, lineNumber, column, byteOffset, nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
                return LiteralLineSearcher.SearchMatches(outputSpan, pattern, ref replacementMatchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            }

            var replacementLineSink = new ReplacementLineSink(output, prefix, separators.FieldMatch, replacementValue, pattern, asciiCaseInsensitive, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, lineLimit, color: color, lineTerminator: separators.LineTerminator);
            bool matched = LiteralLineSearcher.SearchMatchLines(outputSpan, pattern, ref replacementLineSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            replacementLineSink.Flush();
            return matched;
        }

        if (vimgrep && !invertMatch)
        {
            var vimgrepSink = new VimgrepSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, onlyMatching, trim, nullPathTerminator, lineLimit, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, color, separators.LineTerminator);
            return LiteralLineSearcher.SearchMatchLines(outputSpan, pattern, ref vimgrepSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
        }

        if (onlyMatching && !invertMatch)
        {
            var matchSink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
            return LiteralLineSearcher.SearchMatches(outputSpan, pattern, ref matchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
        }

        if (color.Enabled && !invertMatch)
        {
            var coloredSink = new ColoredSearchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
            bool matched = LiteralLineSearcher.SearchMatchLines(outputSpan, pattern, ref coloredSink, asciiCaseInsensitive, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            coloredSink.Flush();
            return matched;
        }

        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        bool requireMatchColumn = column || prefix?.HasHyperlink == true;
        return LiteralLineSearcher.Search(outputSpan, pattern, ref sink, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData, requireMatchColumn);
    }

    private static bool TrySearchBinarySuppressed(
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
        out bool matched,
        out bool convertBinaryNuls)
    {
        matched = false;
        convertBinaryNuls = false;
        BinaryDetectionResult binaryDetection = BinaryDetection.Detect(bytes, textMode, separators.NullData, quitOnBinary);
        if (!binaryDetection.IsBinary)
        {
            return false;
        }

        if (binaryDetection.Kind == BinaryDetectionKind.Quit)
        {
            if (quiet)
            {
                matched = HasBinarySafePrefixMatch(bytes, binaryDetection.Offset, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf);
                return true;
            }

            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                matched = true;
                return true;
            }

            if (searchMode is not (CliSearchMode.Standard or CliSearchMode.FilesWithMatches))
            {
                return true;
            }

            matched = SearchBinarySafePrefix(bytes, binaryDetection.Offset, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch);
            if (matched && searchMode == CliSearchMode.Standard)
            {
                WriteBinaryFileStoppedWarning(output, prefix, color, binaryDetection.Offset);
            }

            return true;
        }

        if (quiet)
        {
            byte[] convertedBytes = BinaryDetection.ConvertNulToLineFeed(bytes);
            matched = LiteralLineSearcher.HasMatch(convertedBytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf);
            return true;
        }

        if (searchMode != CliSearchMode.Standard)
        {
            convertBinaryNuls = true;
            return false;
        }

        if (passthru || beforeContext > 0 || afterContext > 0)
        {
            if (memoryMapped)
            {
                byte[] convertedBytes = BinaryDetection.ConvertNulToLineFeed(bytes);
                matched = LiteralLineSearcher.HasMatch(convertedBytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf);
            }
            else
            {
                matched = LiteralLineSearcher.HasMatch(bytes.AsSpan(0, binaryDetection.Offset), pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf);
            }
        }
        else
        {
            byte[] convertedBytes = BinaryDetection.ConvertNulToLineFeed(bytes);
            matched = LiteralLineSearcher.HasMatch(convertedBytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf);
        }

        if (matched)
        {
            if (!passthru && beforeContext == 0 && afterContext == 0)
            {
                SearchBinarySafePrefix(bytes, binaryDetection.Offset, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch);
            }

            WriteBinaryFileMatches(output, prefix, color, binaryDetection.Offset);
        }

        return true;
    }

    private static bool HasBinarySafePrefixMatch(
        byte[] bytes,
        int binaryOffset,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        bool crlf)
    {
        int safeLength = GetBinarySafePrefixLength(bytes, binaryOffset);
        return safeLength > 0 &&
            LiteralLineSearcher.HasMatch(bytes.AsSpan(0, safeLength), pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, crlf);
    }

    private static bool SearchBinarySafePrefix(
        byte[] bytes,
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
        bool stopOnNonmatch)
    {
        int safeLength = GetBinarySafePrefixLength(bytes, binaryOffset);
        if (safeLength == 0)
        {
            return false;
        }

        byte[] safeBytes = bytes.AsSpan(0, safeLength).ToArray();
        return SearchBytes(safeBytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode: true, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary: false);
    }

    internal static int GetBinarySafePrefixLength(byte[] bytes, int binaryOffset)
    {
        int length = binaryOffset - (binaryOffset % BinaryDetectionBufferLength);
        if (length <= 0)
        {
            return 0;
        }

        length = Math.Min(length, bytes.Length);
        int lastLineFeed = bytes.AsSpan(0, length).LastIndexOf((byte)'\n');
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
        output.Write(Utf8.GetBytes(binaryOffset.ToString(CultureInfo.InvariantCulture)));
        output.Write(")"u8);
        output.Write(LineFeed);
    }

    internal static void WriteBinaryFileStoppedWarning(RawByteWriter output, OutputPath? prefix, OutputColor color, long binaryOffset)
    {
        if (prefix is not null)
        {
            prefix.WriteLabel(output, color);
            output.Write(": "u8);
        }

        output.Write("WARNING: stopped searching binary file after match (found \"\\0\" byte around offset "u8);
        output.Write(Utf8.GetBytes(binaryOffset.ToString(CultureInfo.InvariantCulture)));
        output.Write(")"u8);
        output.Write(LineFeed);
    }
}
