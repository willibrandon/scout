using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        long started = Stopwatch.GetTimestamp();
        using MemoryStream buffer = new();
        var bufferedWriter = new RawByteWriter(buffer);
        bool matched = SearchBytesWithOptionalHeading(bytes, pattern, bufferedWriter, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, heading, ref wroteHeadingOutput, memoryMapped);
        bufferedWriter.Flush();
        byte[] body = buffer.ToArray();
        output.Write(body);

        SearchStats fileStats = CollectSearchStats(bytes, pattern, searchMode, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, maxCount, stopOnNonmatch, searchMode == CliSearchMode.Standard ? (ulong)body.Length : 0, Stopwatch.GetElapsedTime(started));
        stats.Add(fileStats);
        return matched;
    }

    private static SearchStats CollectSearchStats(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        CliSearchMode searchMode,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
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
            return ContextSearchOperations.SearchBytes(bytes, pattern, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, onlyMatching, replacement, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator, stopOnNonmatch);
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
