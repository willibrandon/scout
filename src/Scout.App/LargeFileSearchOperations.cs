using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Scout;

internal static class LargeFileSearchOperations
{
    private const int StreamingFileBufferLength = 16_777_216;
    private const long StreamingFileThreshold = int.MaxValue;

    internal static bool TrySearch(
        string path,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool implicitSearch,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
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
        bool heading,
        ref bool matched,
        ref bool errored)
    {
        try
        {
            if (!CanSearchStreaming(path, lowArgs, implicitSearch, color, searchMode, vimgrep, onlyMatching, replacement, beforeContext, afterContext, passthru, heading))
            {
                return false;
            }

            SearchDiagnosticLogging.LogTraceSearchPath(logger, path, SearchFileReadKind.Buffered);
            matched |= SearchStreaming(
                path,
                pattern,
                output,
                prefix,
                separators,
                lineLimit,
                color,
                searchMode,
                lineNumber,
                column,
                byteOffset,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxCount,
                textMode,
                quiet,
                trim,
                includeZero,
                nullPathTerminator,
                lowArgs.StopOnNonmatch);
        }
        catch (IOException exception)
        {
            SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            errored = true;
        }
        catch (UnauthorizedAccessException exception)
        {
            SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            errored = true;
        }

        return true;
    }

    private static bool CanSearchStreaming(
        string path,
        CliLowArgs lowArgs,
        bool implicitSearch,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool heading)
    {
        if (new FileInfo(path).Length <= StreamingFileThreshold)
        {
            return false;
        }

        return !implicitSearch &&
            !lowArgs.Multiline &&
            !lowArgs.SearchZip &&
            lowArgs.Preprocessor is null &&
            lowArgs.EncodingMode is CliEncodingMode.Auto or CliEncodingMode.None or CliEncodingMode.Utf8 &&
            searchMode is CliSearchMode.Standard or CliSearchMode.Count or CliSearchMode.CountMatches or CliSearchMode.FilesWithMatches or CliSearchMode.FilesWithoutMatch &&
            !color.Enabled &&
            !vimgrep &&
            !onlyMatching &&
            replacement is null &&
            beforeContext == 0 &&
            afterContext == 0 &&
            !passthru &&
            !heading;
    }

    private static bool SearchStreaming(
        string path,
        IReadOnlyList<byte[]> pattern,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (!stopOnNonmatch && searchMode == CliSearchMode.Standard)
        {
            return SearchStreamingStandard(
                path,
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
                maxCount,
                textMode,
                quiet,
                trim,
                nullPathTerminator);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileBufferLength, FileOptions.SequentialScan);
        byte[] buffer = new byte[StreamingFileBufferLength];
        MemoryStream? pendingLine = null;
        long pendingLineOffset = 0;
        long absoluteOffset = 0;
        long lineNumberValue = 1;
        ulong matchedLines = 0;
        long count = 0;
        bool matched = false;
        bool hasSelectedMatch = false;
        byte terminator = separators.NullData ? (byte)0 : (byte)'\n';
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);

        bool ProcessLine(ReadOnlySpan<byte> line, long lineStartOffset)
        {
            if (!textMode && !separators.NullData && line.IndexOf((byte)0) >= 0)
            {
                return true;
            }

            bool selectedMatch = ContextSearchOperations.TryFindLineMatch(line, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, out long matchColumn);
            if (stopOnNonmatch && hasSelectedMatch && !selectedMatch)
            {
                return true;
            }

            hasSelectedMatch |= selectedMatch;
            if (!selectedMatch)
            {
                lineNumberValue++;
                return false;
            }

            matched = true;
            matchedLines++;
            if (quiet)
            {
                return true;
            }

            if (searchMode == CliSearchMode.FilesWithMatches)
            {
                SearchOutputFormatting.WritePathIf(output, prefix, color, condition: true, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                return true;
            }

            if (searchMode == CliSearchMode.Count)
            {
                count++;
            }
            else if (searchMode == CliSearchMode.CountMatches && invertMatch)
            {
                count++;
            }
            else if (searchMode == CliSearchMode.CountMatches)
            {
                count += LiteralLineSearcher.CountLineMatches(line, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, separators.Crlf, separators.NullData);
            }
            else if (searchMode == CliSearchMode.Standard)
            {
                sink.MatchedLine(lineNumberValue, lineStartOffset, matchColumn, line);
            }

            lineNumberValue++;
            return maxCount is ulong limit && matchedLines >= limit;
        }

        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            long chunkOffset = absoluteOffset;
            absoluteOffset += read;
            int segmentStart = 0;
            ReadOnlySpan<byte> chunk = buffer.AsSpan(0, read);
            while (segmentStart < read)
            {
                int terminatorOffset = chunk[segmentStart..].IndexOf(terminator);
                if (terminatorOffset < 0)
                {
                    break;
                }

                int lineLength = terminatorOffset + 1;
                if (pendingLine is null)
                {
                    if (ProcessLine(chunk.Slice(segmentStart, lineLength), chunkOffset + segmentStart))
                    {
                        return FinishSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
                    }
                }
                else
                {
                    pendingLine.Write(chunk.Slice(segmentStart, lineLength));
                    byte[] line = pendingLine.ToArray();
                    pendingLine.Dispose();
                    pendingLine = null;
                    if (ProcessLine(line, pendingLineOffset))
                    {
                        return FinishSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
                    }
                }

                segmentStart += lineLength;
            }

            if (segmentStart < read)
            {
                pendingLine ??= new MemoryStream();
                if (pendingLine.Length == 0)
                {
                    pendingLineOffset = chunkOffset + segmentStart;
                }

                pendingLine.Write(chunk[segmentStart..]);
            }
        }

        if (pendingLine is not null)
        {
            byte[] line = pendingLine.ToArray();
            pendingLine.Dispose();
            if (line.Length != 0 && ProcessLine(line, pendingLineOffset))
            {
                return FinishSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
            }
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !matched, nullPathTerminator, separators.LineTerminator);
        }

        return FinishSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
    }

    private static bool FinishSearch(
        RawByteWriter output,
        OutputPath? prefix,
        OutputColor color,
        CliSearchMode searchMode,
        bool quiet,
        bool includeZero,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator,
        bool matched,
        long count)
    {
        if (quiet)
        {
            return matched;
        }

        return searchMode switch
        {
            CliSearchMode.Count or CliSearchMode.CountMatches => SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, lineTerminator),
            CliSearchMode.FilesWithoutMatch => false,
            _ => matched,
        };
    }

    private static bool SearchStreamingStandard(
        string path,
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
        ulong? maxCount,
        bool textMode,
        bool quiet,
        bool trim,
        bool nullPathTerminator)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileBufferLength, FileOptions.SequentialScan);
        byte[] buffer = new byte[StreamingFileBufferLength];
        MemoryStream? pendingLine = null;
        long pendingLineOffset = 0;
        long absoluteOffset = 0;
        long lineNumberValue = 1;
        ulong matchedLines = 0;
        bool matched = false;
        byte terminator = separators.NullData ? (byte)0 : (byte)'\n';
        byte[]? fastLiteralPattern = GetFastStreamingLiteralPattern(pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, terminator);

        bool ProcessSegment(ReadOnlySpan<byte> segment, long segmentStartOffset, ulong lineCount)
        {
            if (segment.IsEmpty)
            {
                return false;
            }

            if (!textMode && !separators.NullData && segment.IndexOf((byte)0) >= 0)
            {
                return true;
            }

            if (fastLiteralPattern is not null)
            {
                return ProcessFastLiteralSegment(segment, segmentStartOffset, lineCount, fastLiteralPattern);
            }

            ulong? remainingMatches = null;
            if (maxCount is ulong limit)
            {
                if (matchedLines >= limit)
                {
                    return true;
                }

                remainingMatches = limit - matchedLines;
            }

            if (quiet)
            {
                if (!SearchModeEvaluation.SearchQuiet(segment, pattern, CliSearchMode.Standard, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, remainingMatches, separators.Crlf, separators.NullData))
                {
                    lineNumberValue += (long)lineCount;
                    return false;
                }

                matched = true;
                return true;
            }

            var sink = new StandardSearchSink(
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
                separators.LineTerminator,
                lineNumberValue - 1,
                segmentStartOffset);
            matched |= LiteralLineSearcher.Search(segment, pattern, ref sink, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, remainingMatches, separators.Crlf, separators.NullData);
            matchedLines += sink.MatchedLines;
            lineNumberValue += (long)lineCount;
            return maxCount is ulong limitAfterSearch && matchedLines >= limitAfterSearch;
        }

        bool ProcessFastLiteralSegment(ReadOnlySpan<byte> segment, long segmentStartOffset, ulong lineCount, ReadOnlySpan<byte> needle)
        {
            if (quiet)
            {
                if (segment.IndexOf(needle) < 0)
                {
                    lineNumberValue += (long)lineCount;
                    return false;
                }

                matched = true;
                return true;
            }

            var sink = new StandardSearchSink(
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
            int searchOffset = 0;
            int currentLineStart = 0;
            int currentLineEnd = GetSegmentLineEnd(segment, currentLineStart, terminator);
            long currentLineNumber = lineNumberValue;
            while (searchOffset < segment.Length)
            {
                int found = segment[searchOffset..].IndexOf(needle);
                if (found < 0)
                {
                    break;
                }

                int matchIndex = searchOffset + found;
                while (matchIndex >= currentLineEnd && currentLineEnd < segment.Length)
                {
                    currentLineStart = currentLineEnd;
                    currentLineEnd = GetSegmentLineEnd(segment, currentLineStart, terminator);
                    currentLineNumber++;
                }

                long matchedByteOffset = segmentStartOffset + currentLineStart;
                long matchedColumn = matchIndex - currentLineStart + 1;
                sink.MatchedLine(currentLineNumber, matchedByteOffset, matchedColumn, segment.Slice(currentLineStart, currentLineEnd - currentLineStart));
                matched = true;
                matchedLines++;
                if (maxCount is ulong limit && matchedLines >= limit)
                {
                    return true;
                }

                searchOffset = currentLineEnd;
            }

            lineNumberValue += (long)lineCount;
            return false;
        }

        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            long chunkOffset = absoluteOffset;
            absoluteOffset += read;
            int segmentStart = 0;
            ReadOnlySpan<byte> chunk = buffer.AsSpan(0, read);

            if (pendingLine is not null)
            {
                int firstTerminator = chunk.IndexOf(terminator);
                if (firstTerminator < 0)
                {
                    pendingLine.Write(chunk);
                    continue;
                }

                int pendingLength = firstTerminator + 1;
                pendingLine.Write(chunk[..pendingLength]);
                byte[] line = pendingLine.ToArray();
                pendingLine.Dispose();
                pendingLine = null;
                if (ProcessSegment(line, pendingLineOffset, lineCount: 1))
                {
                    return matched;
                }

                segmentStart = pendingLength;
            }

            if (segmentStart < read)
            {
                int lastTerminator = chunk[segmentStart..].LastIndexOf(terminator);
                if (lastTerminator >= 0)
                {
                    int completeLength = lastTerminator + 1;
                    ReadOnlySpan<byte> segment = chunk.Slice(segmentStart, completeLength);
                    if (ProcessSegment(segment, chunkOffset + segmentStart, CountByte(segment, terminator)))
                    {
                        return matched;
                    }

                    segmentStart += completeLength;
                }
            }

            if (segmentStart < read)
            {
                pendingLine = new MemoryStream();
                pendingLineOffset = chunkOffset + segmentStart;
                pendingLine.Write(chunk[segmentStart..]);
            }
        }

        if (pendingLine is not null)
        {
            byte[] line = pendingLine.ToArray();
            pendingLine.Dispose();
            if (line.Length != 0 && ProcessSegment(line, pendingLineOffset, lineCount: 1))
            {
                return matched;
            }
        }

        return matched;
    }

    private static int GetSegmentLineEnd(ReadOnlySpan<byte> segment, int lineStart, byte terminator)
    {
        int terminatorOffset = segment[lineStart..].IndexOf(terminator);
        return terminatorOffset < 0 ? segment.Length : lineStart + terminatorOffset + 1;
    }

    private static byte[]? GetFastStreamingLiteralPattern(
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        byte terminator)
    {
        if (asciiCaseInsensitive || invertMatch || lineRegexp || wordRegexp || pattern.Count != 1)
        {
            return null;
        }

        byte[] candidate = pattern[0];
        if (candidate.Length == 0 || candidate.AsSpan().IndexOf(terminator) >= 0)
        {
            return null;
        }

        for (int index = 0; index < candidate.Length; index++)
        {
            if (PatternPreparation.IsRegexMetaByte(candidate[index]))
            {
                return null;
            }
        }

        return candidate;
    }

    private static ulong CountByte(ReadOnlySpan<byte> bytes, byte value)
    {
        ulong count = 0;
        int index = 0;
        if (bytes.Length >= sizeof(ulong))
        {
            ulong repeated = UInt64WithRepeatedByte(value);
            ref byte start = ref MemoryMarshal.GetReference(bytes);
            int wordLength = bytes.Length - sizeof(ulong) + 1;
            while (index < wordLength)
            {
                ulong word = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, index));
                ulong comparison = word ^ repeated;
                ulong zeroBytes = (comparison - 0x0101010101010101UL) & ~comparison & 0x8080808080808080UL;
                count += (ulong)BitOperations.PopCount(zeroBytes);
                index += sizeof(ulong);
            }
        }

        for (; index < bytes.Length; index++)
        {
            if (bytes[index] == value)
            {
                count++;
            }
        }

        return count;
    }

    private static ulong UInt64WithRepeatedByte(byte value)
    {
        return 0x0101010101010101UL * value;
    }
}
