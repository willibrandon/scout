using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Scout;

internal static unsafe class LargeFileSearchOperations
{
    private const int StreamingFileBufferLength = 131_072;
    private const int StreamingFileStreamBufferLength = 1;
    private const int ImplicitSearchStreamingFileBufferLength = 262_144;
    private const long ImplicitSearchStreamingFileThreshold = 65_536;
    private const long StreamingFileThreshold = int.MaxValue;

    internal static bool TrySearch(
        string path,
        long? knownLength,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool implicitSearch,
        bool isOneFile,
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
            if (!CanSearchStreaming(path, knownLength, lowArgs, implicitSearch, color, searchMode, vimgrep, onlyMatching, replacement, beforeContext, afterContext, passthru, heading))
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
                lowArgs.StopOnNonmatch,
                implicitSearch && !lowArgs.SearchBinaryFiles && !textMode,
                SearchWalkPlanning.GetLargeFileSearchThreadCount(lowArgs, isOneFile),
                implicitSearch ? ImplicitSearchStreamingFileBufferLength : StreamingFileBufferLength);
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
        long? knownLength,
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
        long length = knownLength ?? new FileInfo(path).Length;
        long streamingThreshold = implicitSearch
            ? ImplicitSearchStreamingFileThreshold
            : StreamingFileThreshold;
        if (length <= streamingThreshold)
        {
            return false;
        }

        if (implicitSearch &&
            ((lowArgs.MmapMode == CliMmapMode.AlwaysTryMmap && length <= StreamingFileThreshold) ||
            !CanStreamImplicitEncoding(path, lowArgs.EncodingMode)))
        {
            return false;
        }

        return
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

    private static bool CanStreamImplicitEncoding(string path, CliEncodingMode encodingMode)
    {
        if (encodingMode == CliEncodingMode.None)
        {
            return true;
        }

        if (encodingMode != CliEncodingMode.Auto)
        {
            return false;
        }

        Span<byte> prefix = stackalloc byte[3];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 3);
        int read = stream.Read(prefix);
        return !HasEncodingBom(prefix[..read]);
    }

    private static bool HasEncodingBom(ReadOnlySpan<byte> prefix)
    {
        return (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF) ||
            (prefix.Length >= 2 &&
            ((prefix[0] == 0xFF && prefix[1] == 0xFE) ||
            (prefix[0] == 0xFE && prefix[1] == 0xFF)));
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
        bool stopOnNonmatch,
        bool quitOnBinary,
        int threadCount,
        int bufferLength)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (!textMode &&
            !separators.NullData &&
            searchMode is CliSearchMode.Standard or CliSearchMode.FilesWithMatches or CliSearchMode.FilesWithoutMatch &&
            TryFindBinaryOffset(path, bufferLength, out long binaryOffset))
        {
            if (quitOnBinary)
            {
                return SearchStreamingBinarySafePrefix(
                    path,
                    binaryOffset,
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
                    quiet,
                    trim,
                    includeZero,
                    nullPathTerminator);
            }

            return SearchStreamingConvertedBinary(
                path,
                binaryOffset,
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
                quiet,
                trim,
                includeZero,
                nullPathTerminator,
                bufferLength);
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
                nullPathTerminator,
                quitOnBinary,
                threadCount,
                bufferLength);
        }

        if (CanSearchStreamingCountWithPlan(searchMode, stopOnNonmatch, quiet, separators))
        {
            RegexSearchPlan? regexPlan = LiteralLineSearcher.CreateRegexSearchPlan(pattern, asciiCaseInsensitive, compileAutomata: true);
            return SearchStreamingCountWithPlan(
                path,
                pattern,
                regexPlan,
                output,
                prefix,
                color,
                searchMode,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxCount,
                textMode,
                quitOnBinary,
                includeZero,
                nullPathTerminator,
                separators.LineTerminator,
                bufferLength);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileStreamBufferLength, FileOptions.SequentialScan);
        using var bufferOwner = new NativeByteBuffer(bufferLength);
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
            Span<byte> buffer = bufferOwner.Span;
            int read = stream.Read(buffer);
            if (read == 0)
            {
                break;
            }

            long chunkOffset = absoluteOffset;
            absoluteOffset += read;
            int segmentStart = 0;
            ReadOnlySpan<byte> chunk = buffer[..read];
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

    private static bool TryFindBinaryOffset(string path, int bufferLength, out long binaryOffset)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileStreamBufferLength, FileOptions.SequentialScan);
        using var bufferOwner = new NativeByteBuffer(bufferLength);
        long absoluteOffset = 0;
        while (true)
        {
            Span<byte> buffer = bufferOwner.Span;
            int read = stream.Read(buffer);
            if (read == 0)
            {
                binaryOffset = -1;
                return false;
            }

            int nulOffset = buffer[..read].IndexOf((byte)0);
            if (nulOffset >= 0)
            {
                binaryOffset = absoluteOffset + nulOffset;
                return true;
            }

            absoluteOffset += read;
        }
    }

    private static bool SearchStreamingBinarySafePrefix(
        string path,
        long binaryOffset,
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
        bool quiet,
        bool trim,
        bool includeZero,
        bool nullPathTerminator)
    {
        byte[] safePrefix = ReadBinarySafePrefix(path, binaryOffset);
        if (quiet)
        {
            return safePrefix.Length > 0 &&
                LiteralLineSearcher.HasMatch(safePrefix, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return true;
        }

        if (searchMode is not (CliSearchMode.Standard or CliSearchMode.FilesWithMatches))
        {
            return false;
        }

        bool wroteHeadingOutput = false;
        bool matched = safePrefix.Length > 0 &&
            StandardSearchByteOperations.SearchBytesWithOptionalHeading(
                safePrefix,
                pattern,
                output,
                prefix,
                separators,
                lineLimit,
                color,
                searchMode,
                vimgrep: false,
                lineNumber,
                column,
                byteOffset,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                multiline: false,
                multilineDotall: false,
                onlyMatching: false,
                replacement: null,
                maxCount,
                textMode: true,
                quiet: false,
                trim,
                beforeContext: 0,
                afterContext: 0,
                passthru: false,
                includeZero,
                nullPathTerminator,
                stopOnNonmatch: false,
                quitOnBinary: false,
                heading: false,
                ref wroteHeadingOutput);

        if (matched && searchMode == CliSearchMode.Standard)
        {
            StandardSearchByteOperations.WriteBinaryFileStoppedWarning(output, prefix, color, binaryOffset);
        }

        return matched;
    }

    private static bool SearchStreamingConvertedBinary(
        string path,
        long binaryOffset,
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
        bool quiet,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        int bufferLength)
    {
        bool matched = HasConvertedBinaryMatch(path, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, bufferLength);
        if (quiet)
        {
            return matched;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, matched, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !matched, nullPathTerminator, separators.LineTerminator);
        }

        if (!matched)
        {
            return false;
        }

        byte[] safePrefix = ReadBinarySafePrefix(path, binaryOffset);
        if (safePrefix.Length > 0)
        {
            bool wroteHeadingOutput = false;
            _ = StandardSearchByteOperations.SearchBytesWithOptionalHeading(
                safePrefix,
                pattern,
                output,
                prefix,
                separators,
                lineLimit,
                color,
                CliSearchMode.Standard,
                vimgrep: false,
                lineNumber,
                column,
                byteOffset,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                multiline: false,
                multilineDotall: false,
                onlyMatching: false,
                replacement: null,
                maxCount,
                textMode: true,
                quiet: false,
                trim,
                beforeContext: 0,
                afterContext: 0,
                passthru: false,
                includeZero,
                nullPathTerminator,
                stopOnNonmatch: false,
                quitOnBinary: false,
                heading: false,
                ref wroteHeadingOutput);
        }

        StandardSearchByteOperations.WriteBinaryFileMatches(output, prefix, color, binaryOffset);
        return true;
    }

    private static bool HasConvertedBinaryMatch(
        string path,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        int bufferLength)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileStreamBufferLength, FileOptions.SequentialScan);
        using var bufferOwner = new NativeByteBuffer(bufferLength);
        MemoryStream? pendingLine = null;

        bool HasMatch(ReadOnlySpan<byte> segment)
        {
            if (segment.IsEmpty)
            {
                return false;
            }

            byte[]? convertedSegment = null;
            if (segment.IndexOf((byte)0) >= 0)
            {
                convertedSegment = BinaryDetection.ConvertNulToLineFeed(segment);
                segment = convertedSegment;
            }

            return LiteralLineSearcher.HasMatch(segment, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, crlf: false);
        }

        while (true)
        {
            Span<byte> buffer = bufferOwner.Span;
            int read = stream.Read(buffer);
            if (read == 0)
            {
                break;
            }

            int segmentStart = 0;
            ReadOnlySpan<byte> chunk = buffer[..read];
            if (pendingLine is not null)
            {
                int firstTerminator = IndexOfCountTerminator(chunk, includeBinaryNul: true);
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
                if (HasMatch(line))
                {
                    return true;
                }

                segmentStart = pendingLength;
            }

            if (segmentStart < read)
            {
                int lastTerminator = LastIndexOfCountTerminator(chunk[segmentStart..], includeBinaryNul: true);
                if (lastTerminator >= 0)
                {
                    int completeLength = lastTerminator + 1;
                    if (HasMatch(chunk.Slice(segmentStart, completeLength)))
                    {
                        return true;
                    }

                    segmentStart += completeLength;
                }
            }

            if (segmentStart < read)
            {
                pendingLine = new MemoryStream();
                pendingLine.Write(chunk[segmentStart..]);
            }
        }

        if (pendingLine is not null)
        {
            byte[] line = pendingLine.ToArray();
            pendingLine.Dispose();
            return HasMatch(line);
        }

        return false;
    }

    private static byte[] ReadBinarySafePrefix(string path, long binaryOffset)
    {
        long safeLengthLimit = binaryOffset - (binaryOffset % StandardSearchByteOperations.BinaryDetectionBufferLength);
        if (safeLengthLimit <= 0)
        {
            return [];
        }

        int length = checked((int)Math.Min(safeLengthLimit, int.MaxValue));
        byte[] prefix = new byte[length];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileStreamBufferLength, FileOptions.SequentialScan);
        int totalRead = 0;
        while (totalRead < length)
        {
            int read = stream.Read(prefix.AsSpan(totalRead));
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        int safeLength = StandardSearchByteOperations.GetBinarySafePrefixLength(prefix, (int)Math.Min(binaryOffset, int.MaxValue));
        return safeLength == prefix.Length
            ? prefix
            : prefix.AsSpan(0, safeLength).ToArray();
    }

    private static bool CanSearchStreamingCountWithPlan(
        CliSearchMode searchMode,
        bool stopOnNonmatch,
        bool quiet,
        OutputSeparators separators)
    {
        return searchMode is CliSearchMode.Count or CliSearchMode.CountMatches &&
            !stopOnNonmatch &&
            !quiet &&
            !separators.NullData &&
            !separators.Crlf;
    }

    private static bool SearchStreamingCountWithPlan(
        string path,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan? regexPlan,
        RawByteWriter output,
        OutputPath? prefix,
        OutputColor color,
        CliSearchMode searchMode,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        bool textMode,
        bool quitOnBinary,
        bool includeZero,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator,
        int bufferLength)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileStreamBufferLength, FileOptions.SequentialScan);
        using var bufferOwner = new NativeByteBuffer(bufferLength);
        MemoryStream? pendingLine = null;
        long count = 0;
        ulong countedMatchingLines = 0;
        bool matched = false;
        bool binarySuppressed = false;
        bool convertBinaryNuls = !textMode && !quitOnBinary;

        bool CountSegment(ReadOnlySpan<byte> segment)
        {
            if (segment.IsEmpty)
            {
                return false;
            }

            byte[]? convertedSegment = null;
            if (convertBinaryNuls && segment.IndexOf((byte)0) >= 0)
            {
                convertedSegment = BinaryDetection.ConvertNulToLineFeed(segment);
                segment = convertedSegment;
            }

            ulong? remainingMatchingLines = null;
            if (maxCount is ulong limit)
            {
                if (countedMatchingLines >= limit)
                {
                    return true;
                }

                remainingMatchingLines = limit - countedMatchingLines;
            }

            long segmentCount;
            if (searchMode == CliSearchMode.Count)
            {
                segmentCount = LiteralLineSearcher.CountMatchingLinesWithRegexPlan(
                    segment,
                    pattern,
                    regexPlan,
                    asciiCaseInsensitive,
                    invertMatch,
                    lineRegexp,
                    wordRegexp,
                    remainingMatchingLines,
                    crlf: false,
                    nullData: false);
                countedMatchingLines += (ulong)segmentCount;
            }
            else
            {
                segmentCount = LiteralLineSearcher.CountMatches(
                    segment,
                    pattern,
                    asciiCaseInsensitive,
                    invertMatch,
                    lineRegexp,
                    wordRegexp,
                    remainingMatchingLines,
                    crlf: false,
                    nullData: false);
                if (maxCount is not null)
                {
                    long matchingLines = invertMatch
                        ? segmentCount
                        : LiteralLineSearcher.CountMatchingLinesWithRegexPlan(
                            segment,
                            pattern,
                            regexPlan,
                            asciiCaseInsensitive,
                            invertMatch,
                            lineRegexp,
                            wordRegexp,
                            remainingMatchingLines,
                            crlf: false,
                            nullData: false);
                    countedMatchingLines += (ulong)matchingLines;
                }
            }

            count += segmentCount;
            matched |= segmentCount != 0;
            return maxCount is ulong limitAfterCount && countedMatchingLines >= limitAfterCount;
        }

        bool ProcessSegment(ReadOnlySpan<byte> segment)
        {
            if (segment.IsEmpty)
            {
                return false;
            }

            if (!textMode)
            {
                bool binary = segment.IndexOf((byte)0) >= 0;
                if (binary && quitOnBinary)
                {
                    binarySuppressed = true;
                    return true;
                }
            }

            return CountSegment(segment);
        }

        while (true)
        {
            Span<byte> buffer = bufferOwner.Span;
            int read = stream.Read(buffer);
            if (read == 0)
            {
                break;
            }

            int segmentStart = 0;
            ReadOnlySpan<byte> chunk = buffer[..read];
            if (pendingLine is not null)
            {
                int firstTerminator = IndexOfCountTerminator(chunk, convertBinaryNuls);
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
                if (ProcessSegment(line))
                {
                    if (binarySuppressed)
                    {
                        return false;
                    }

                    return FinishSearch(output, prefix, color, searchMode, quiet: false, includeZero, nullPathTerminator, lineTerminator, matched, count);
                }

                segmentStart = pendingLength;
            }

            if (segmentStart < read)
            {
                int lastTerminator = LastIndexOfCountTerminator(chunk[segmentStart..], convertBinaryNuls);
                if (lastTerminator >= 0)
                {
                    int completeLength = lastTerminator + 1;
                    if (ProcessSegment(chunk.Slice(segmentStart, completeLength)))
                    {
                        if (binarySuppressed)
                        {
                            return false;
                        }

                        return FinishSearch(output, prefix, color, searchMode, quiet: false, includeZero, nullPathTerminator, lineTerminator, matched, count);
                    }

                    segmentStart += completeLength;
                }
            }

            if (segmentStart < read)
            {
                pendingLine = new MemoryStream();
                pendingLine.Write(chunk[segmentStart..]);
            }
        }

        if (pendingLine is not null)
        {
            byte[] line = pendingLine.ToArray();
            pendingLine.Dispose();
            if (line.Length != 0 && ProcessSegment(line))
            {
                if (binarySuppressed)
                {
                    return false;
                }

                return FinishSearch(output, prefix, color, searchMode, quiet: false, includeZero, nullPathTerminator, lineTerminator, matched, count);
            }
        }

        if (binarySuppressed)
        {
            return false;
        }

        return FinishSearch(output, prefix, color, searchMode, quiet: false, includeZero, nullPathTerminator, lineTerminator, matched, count);
    }

    private static int IndexOfCountTerminator(ReadOnlySpan<byte> segment, bool includeBinaryNul)
    {
        int lineFeed = segment.IndexOf((byte)'\n');
        if (!includeBinaryNul)
        {
            return lineFeed;
        }

        int nul = segment.IndexOf((byte)0);
        if (lineFeed < 0)
        {
            return nul;
        }

        return nul < 0 ? lineFeed : Math.Min(lineFeed, nul);
    }

    private static int LastIndexOfCountTerminator(ReadOnlySpan<byte> segment, bool includeBinaryNul)
    {
        int lineFeed = segment.LastIndexOf((byte)'\n');
        if (!includeBinaryNul)
        {
            return lineFeed;
        }

        return Math.Max(lineFeed, segment.LastIndexOf((byte)0));
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
        bool nullPathTerminator,
        bool quitOnBinary,
        int threadCount,
        int bufferLength)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamingFileStreamBufferLength, FileOptions.SequentialScan);
        using var bufferOwner = new NativeByteBuffer(bufferLength);
        MemoryStream? pendingLine = null;
        long pendingLineOffset = 0;
        long absoluteOffset = 0;
        long lineNumberValue = 1;
        ulong matchedLines = 0;
        bool matched = false;
        byte terminator = separators.NullData ? (byte)0 : (byte)'\n';
        byte[]? fastLiteralPattern = GetFastStreamingLiteralPattern(pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, terminator);
        bool searchSegmentsInParallel = threadCount > 1 &&
            fastLiteralPattern is null &&
            maxCount is null &&
            !quiet &&
            !invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            !separators.NullData &&
            !separators.Crlf;
        RegexSearchPlan? regexPlan = fastLiteralPattern is null
            ? LiteralLineSearcher.CreateRegexSearchPlan(pattern, asciiCaseInsensitive, compileAutomata: !searchSegmentsInParallel)
            : null;

        bool ProcessSegment(ReadOnlySpan<byte> segment, long segmentStartOffset, ulong lineCount)
        {
            if (segment.IsEmpty)
            {
                return false;
            }

            if (fastLiteralPattern is not null)
            {
                return ProcessFastLiteralSegment(segment, segmentStartOffset, fastLiteralPattern);
            }

            if (!textMode && !separators.NullData && segment.IndexOf((byte)0) >= 0)
            {
                return true;
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
            bool requireMatchColumn = column || prefix?.HasHyperlink == true;
            matched |= LiteralLineSearcher.SearchWithRegexPlan(segment, pattern, regexPlan, ref sink, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, remainingMatches, separators.Crlf, separators.NullData, requireMatchColumn);
            matchedLines += sink.MatchedLines;
            lineNumberValue += (long)lineCount;
            return maxCount is ulong limitAfterSearch && matchedLines >= limitAfterSearch;
        }

        bool ProcessFastLiteralSegment(ReadOnlySpan<byte> segment, long segmentStartOffset, ReadOnlySpan<byte> needle)
        {
            if (!textMode && !separators.NullData && segment.IndexOf((byte)0) >= 0)
            {
                return true;
            }

            if (quiet)
            {
                if (segment.IndexOf(needle) < 0)
                {
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
            int countedOffset = 0;
            long currentLineNumber = lineNumberValue;
            while (searchOffset < segment.Length)
            {
                int found = segment[searchOffset..].IndexOf(needle);
                if (found < 0)
                {
                    break;
                }

                int matchIndex = searchOffset + found;
                int currentLineStart = GetSegmentLineStart(segment, matchIndex, terminator);
                currentLineNumber += ByteCounter.Count(segment.Slice(countedOffset, currentLineStart - countedOffset), terminator);
                countedOffset = currentLineStart;
                int currentLineEnd = GetSegmentLineEnd(segment, currentLineStart, terminator);

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

            lineNumberValue = currentLineNumber + ByteCounter.Count(segment[countedOffset..], terminator);
            return false;
        }

        bool CanSearchSegmentsInParallel()
        {
            return searchSegmentsInParallel;
        }

        LargeFileSegmentSearchResult ProcessBufferedSegment(nint segmentAddress, int segmentLength, long segmentStartOffset, long segmentLineNumber)
        {
            ReadOnlySpan<byte> segment = new((void*)segmentAddress, segmentLength);
            var sink = new LargeFileSegmentMatchSink();
            bool segmentMatched = LiteralLineSearcher.SearchWithRegexPlan(
                segment,
                pattern,
                regexPlan,
                ref sink,
                asciiCaseInsensitive,
                invertMatch: false,
                lineRegexp: false,
                wordRegexp: false,
                maxMatchingLines: null,
                crlf: false,
                nullData: false,
                requireMatchColumn: column || prefix?.HasHyperlink == true);
            return new LargeFileSegmentSearchResult(
                segmentMatched,
                sink.MatchedLines,
                segmentAddress,
                segmentLength,
                segmentStartOffset,
                segmentLineNumber,
                sink.Matches);
        }

        bool SearchSegmentsInParallel()
        {
            int parallelism = Math.Clamp(threadCount, 1, 3);
            var pending = new Queue<Task<LargeFileSegmentSearchResult>>();
            int carriedLength = 0;
            long carriedOffset = 0;

            void DrainOne()
            {
                LargeFileSegmentSearchResult result = pending.Dequeue().GetAwaiter().GetResult();
                try
                {
                    if (result.Matches is not null)
                    {
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
                            result.SegmentLineNumber - 1,
                            result.SegmentStartOffset);
                        ReadOnlySpan<byte> segment = new((void*)result.SegmentAddress, result.SegmentLength);
                        for (int index = 0; index < result.Matches.Count; index++)
                        {
                            LargeFileSegmentMatch match = result.Matches[index];
                            sink.MatchedLine(
                                match.LineNumber,
                                match.LineStart,
                                match.MatchColumn,
                                segment.Slice(match.LineStart, match.LineLength));
                        }
                    }

                    matched |= result.Matched;
                    matchedLines += result.MatchedLines;
                }
                finally
                {
                    NativeMemory.Free((void*)result.SegmentAddress);
                }
            }

            void DrainAll()
            {
                while (pending.Count != 0)
                {
                    DrainOne();
                }
            }

            void DrainPendingAfterFailure()
            {
                while (pending.Count != 0)
                {
                    Task<LargeFileSegmentSearchResult> task = pending.Dequeue();
                    ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
                    if (task.IsCompletedSuccessfully)
                    {
                        LargeFileSegmentSearchResult result = task.Result;
                        NativeMemory.Free((void*)result.SegmentAddress);
                    }
                }
            }

            void QueueSegment(ReadOnlySpan<byte> segment, long segmentStartOffset, ulong lineCount)
            {
                int segmentLength = segment.Length;
                nint segmentAddress = (nint)NativeMemory.Alloc((nuint)segmentLength);
                if (segmentAddress == 0)
                {
                    throw new InvalidOperationException("Native memory allocation failed.");
                }

                segment.CopyTo(new Span<byte>((void*)segmentAddress, segmentLength));
                long segmentLineNumber = lineNumberValue;
                lineNumberValue += (long)lineCount;
                pending.Enqueue(Task.Run(() =>
                {
                    try
                    {
                        return ProcessBufferedSegment(segmentAddress, segmentLength, segmentStartOffset, segmentLineNumber);
                    }
                    catch
                    {
                        NativeMemory.Free((void*)segmentAddress);
                        throw;
                    }
                }));
                if (pending.Count >= parallelism)
                {
                    DrainOne();
                }
            }

            bool QueueCompleteLinesAndCarry(ReadOnlySpan<byte> chunk, long chunkOffset, int segmentStart, Span<byte> buffer)
            {
                if (segmentStart < chunk.Length)
                {
                    int lastTerminator = chunk[segmentStart..].LastIndexOf(terminator);
                    if (lastTerminator >= 0)
                    {
                        int completeLength = lastTerminator + 1;
                        ReadOnlySpan<byte> segment = chunk.Slice(segmentStart, completeLength);
                        if (!textMode && segment.IndexOf((byte)0) >= 0)
                        {
                            DrainAll();
                            return true;
                        }

                        QueueSegment(segment, chunkOffset + segmentStart, (ulong)ByteCounter.Count(segment, terminator));
                        segmentStart += completeLength;
                    }
                }

                if (segmentStart >= chunk.Length)
                {
                    carriedLength = 0;
                    return false;
                }

                int tailLength = chunk.Length - segmentStart;
                carriedOffset = chunkOffset + segmentStart;
                if (tailLength == buffer.Length)
                {
                    pendingLine = new MemoryStream();
                    pendingLineOffset = carriedOffset;
                    pendingLine.Write(chunk[segmentStart..]);
                    carriedLength = 0;
                    return false;
                }

                chunk.Slice(segmentStart, tailLength).CopyTo(buffer);
                carriedLength = tailLength;
                return false;
            }

            try
            {
                while (true)
                {
                    Span<byte> buffer = bufferOwner.Span;

                    if (pendingLine is not null)
                    {
                        int read = stream.Read(buffer);
                        if (read == 0)
                        {
                            break;
                        }

                        long chunkOffset = absoluteOffset;
                        absoluteOffset += read;
                        ReadOnlySpan<byte> chunk = buffer[..read];
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
                        if (!textMode && line.AsSpan().IndexOf((byte)0) >= 0)
                        {
                            DrainAll();
                            return matched;
                        }

                        QueueSegment(line, pendingLineOffset, lineCount: 1);
                        if (QueueCompleteLinesAndCarry(chunk, chunkOffset, pendingLength, buffer))
                        {
                            return matched;
                        }

                        continue;
                    }

                    if (carriedLength == buffer.Length)
                    {
                        pendingLine = new MemoryStream();
                        pendingLineOffset = carriedOffset;
                        pendingLine.Write(buffer);
                        carriedLength = 0;
                        continue;
                    }

                    int readIntoTail = stream.Read(buffer[carriedLength..]);
                    if (readIntoTail == 0)
                    {
                        break;
                    }

                    long combinedOffset = carriedLength == 0 ? absoluteOffset : carriedOffset;
                    absoluteOffset += readIntoTail;
                    int combinedLength = carriedLength + readIntoTail;
                    ReadOnlySpan<byte> combined = buffer[..combinedLength];
                    if (QueueCompleteLinesAndCarry(combined, combinedOffset, segmentStart: 0, buffer))
                    {
                        return matched;
                    }
                }

                if (pendingLine is not null)
                {
                    byte[] line = pendingLine.ToArray();
                    pendingLine.Dispose();
                    if (line.Length != 0)
                    {
                        if (!textMode && line.AsSpan().IndexOf((byte)0) >= 0)
                        {
                            DrainAll();
                            return matched;
                        }

                        QueueSegment(line, pendingLineOffset, lineCount: 1);
                    }
                }
                else if (carriedLength != 0)
                {
                    ReadOnlySpan<byte> line = bufferOwner.Span[..carriedLength];
                    if (!textMode && line.IndexOf((byte)0) >= 0)
                    {
                        DrainAll();
                        return matched;
                    }

                    QueueSegment(line, carriedOffset, lineCount: 1);
                }

                DrainAll();
                return matched;
            }
            catch
            {
                DrainPendingAfterFailure();
                throw;
            }
        }

        if (CanSearchSegmentsInParallel())
        {
            return SearchSegmentsInParallel();
        }

        int carriedLength = 0;
        long carriedOffset = 0;

        bool ProcessCompleteLinesAndCarry(ReadOnlySpan<byte> chunk, long chunkOffset, int segmentStart, Span<byte> buffer)
        {
            if (segmentStart < chunk.Length)
            {
                int lastTerminator = chunk[segmentStart..].LastIndexOf(terminator);
                if (lastTerminator >= 0)
                {
                    int completeLength = lastTerminator + 1;
                    ReadOnlySpan<byte> segment = chunk.Slice(segmentStart, completeLength);
                    ulong lineCount = fastLiteralPattern is null
                        ? (ulong)ByteCounter.Count(segment, terminator)
                        : 0;
                    if (ProcessSegment(segment, chunkOffset + segmentStart, lineCount))
                    {
                        return true;
                    }

                    segmentStart += completeLength;
                }
            }

            if (segmentStart >= chunk.Length)
            {
                carriedLength = 0;
                return false;
            }

            int tailLength = chunk.Length - segmentStart;
            carriedOffset = chunkOffset + segmentStart;
            if (tailLength == buffer.Length)
            {
                pendingLine = new MemoryStream();
                pendingLineOffset = carriedOffset;
                pendingLine.Write(chunk[segmentStart..]);
                carriedLength = 0;
                return false;
            }

            chunk.Slice(segmentStart, tailLength).CopyTo(buffer);
            carriedLength = tailLength;
            return false;
        }

        while (true)
        {
            Span<byte> buffer = bufferOwner.Span;

            if (pendingLine is not null)
            {
                int read = stream.Read(buffer);
                if (read == 0)
                {
                    break;
                }

                long chunkOffset = absoluteOffset;
                absoluteOffset += read;
                ReadOnlySpan<byte> chunk = buffer[..read];
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

                if (ProcessCompleteLinesAndCarry(chunk, chunkOffset, pendingLength, buffer))
                {
                    return matched;
                }

                continue;
            }

            if (carriedLength == buffer.Length)
            {
                pendingLine = new MemoryStream();
                pendingLineOffset = carriedOffset;
                pendingLine.Write(buffer);
                carriedLength = 0;
                continue;
            }

            int readIntoTail = stream.Read(buffer[carriedLength..]);
            if (readIntoTail == 0)
            {
                break;
            }

            long combinedOffset = carriedLength == 0 ? absoluteOffset : carriedOffset;
            absoluteOffset += readIntoTail;
            int combinedLength = carriedLength + readIntoTail;
            ReadOnlySpan<byte> combined = buffer[..combinedLength];
            if (ProcessCompleteLinesAndCarry(combined, combinedOffset, segmentStart: 0, buffer))
            {
                return matched;
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
        else if (carriedLength != 0 && ProcessSegment(bufferOwner.Span[..carriedLength], carriedOffset, lineCount: 1))
        {
            return matched;
        }

        return matched;
    }

    private static int GetSegmentLineEnd(ReadOnlySpan<byte> segment, int lineStart, byte terminator)
    {
        int terminatorOffset = segment[lineStart..].IndexOf(terminator);
        return terminatorOffset < 0 ? segment.Length : lineStart + terminatorOffset + 1;
    }

    private static int GetSegmentLineStart(ReadOnlySpan<byte> segment, int matchIndex, byte terminator)
    {
        int previousTerminator = segment[..matchIndex].LastIndexOf(terminator);
        return previousTerminator < 0 ? 0 : previousTerminator + 1;
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

}
