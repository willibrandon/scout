using System.Collections.Concurrent;

namespace Scout;

/// <summary>
/// Coordinates JSON-formatted searches across standard input, files, and directories.
/// </summary>
internal static class JsonSearchOperations
{
    private static readonly byte[] s_standardInputPath = "<stdin>"u8.ToArray();

    internal static int Run(
        IReadOnlyList<OsString> positional,
        int firstPathIndex,
        bool patternsReadFromStandardInput,
        CliLowArgs lowArgs,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool asciiCaseInsensitive,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        Stream standardInput,
        bool standardInputIsReadable)
    {
        if (lowArgs.MaxCount == 0)
        {
            output.Flush();
            return ExitCode.NoMatch;
        }

        var summary = new JsonSearchSummary();
        bool matched = false;
        bool errored = false;
        bool useDefaultCurrentDirectory = positional.Count == firstPathIndex &&
            (patternsReadFromStandardInput || !standardInputIsReadable);
        if (positional.Count == firstPathIndex && !useDefaultCurrentDirectory)
        {
            matched = SearchJsonStandardInput(pattern, regexPlan, standardInput, lowArgs, asciiCaseInsensitive, summary, output);
            summary.WriteSummary(output);
            output.Flush();
            return matched ? ExitCode.Success : ExitCode.NoMatch;
        }

        var paths = new List<SearchPathArgument>(positional.Count - firstPathIndex);
        if (useDefaultCurrentDirectory)
        {
            paths.Add(SearchPathArgument.CreateText("."));
        }

        for (int index = firstPathIndex; index < positional.Count; index++)
        {
            if (SearchPathArgument.TryCreate(positional[index], lowArgs.PathSeparator, diagnostics, out SearchPathArgument path))
            {
                paths.Add(path);
            }
            else
            {
                errored = true;
            }
        }

        bool autoMmapEligible = SearchPathArgument.IsAutoMmapEligible(paths);
        for (int index = 0; index < paths.Count; index++)
        {
            bool defaultRoot = useDefaultCurrentDirectory && index == 0;
            SearchJsonPath(paths[index], pattern, regexPlan, standardInput, defaultRoot, paths.Count > 1, autoMmapEligible, lowArgs, fileTypes, asciiCaseInsensitive, summary, output, diagnostics, logger, ref matched, ref errored);
        }

        summary.WriteSummary(output);
        output.Flush();
        return SearchOutputFormatting.GetSearchExitCode(matched, errored, lowArgs.Quiet);
    }

    private static bool SearchJsonStandardInput(
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output)
    {
        byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, lowArgs.EncodingMode);
        return SearchJsonBytes(bytes, pattern, regexPlan, output, s_standardInputPath, summary, lowArgs.TextMode, searchBinaryAsText: false, lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
    }

    private static void SearchJsonPath(
        SearchPathArgument pathArgument,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
        bool defaultRoot,
        bool multiplePaths,
        bool autoMmapEligible,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        ref bool matched,
        ref bool errored)
    {
        string? path = pathArgument.Text;
        if (pathArgument.IsRawUnixPath)
        {
            SearchJsonRawUnixFile(pathArgument, pattern, regexPlan, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, logger, ref matched, ref errored);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            matched |= SearchJsonStandardInput(pattern, regexPlan, standardInput, lowArgs, asciiCaseInsensitive, summary, output);
            return;
        }

        if (Directory.Exists(path))
        {
            int threadCount = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);
            if (threadCount > 1)
            {
                SearchJsonDirectoryParallel(path, pattern, regexPlan, defaultRoot, lowArgs, fileTypes, asciiCaseInsensitive, summary, output, diagnostics, logger, threadCount, ref matched, ref errored);
                return;
            }

            string fullRoot = Path.GetFullPath(path);
            foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(path, lowArgs, fileTypes, diagnostics, logger))
            {
                byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(path, fullRoot, entry, defaultRoot, pathSeparator: null);
                SearchJsonDirectoryEntryFile(entry, displayPath, pattern, regexPlan, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, logger, ref matched, ref errored);
            }

            return;
        }

        if (File.Exists(path))
        {
            SearchJsonFile(path, pathArgument.DisplayBytes, pattern, regexPlan, autoMmapEligible, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, logger, ref matched, ref errored);
            return;
        }

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
        errored = true;
    }

    private static void SearchJsonDirectoryParallel(
        string root,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        int threadCount,
        ref bool matched,
        ref bool errored)
    {
        string fullRoot = Path.GetFullPath(root);
        using var outputs = new BlockingCollection<byte[]>();
        object summaryLock = new();
        int matchedFlag = 0;
        int erroredFlag = 0;
        using var printTask = BackgroundWorkItem.Queue(() =>
        {
            foreach (byte[] body in outputs.GetConsumingEnumerable())
            {
                if (body.Length > 0)
                {
                    output.Write(body);
                }
            }
        });

        try
        {
            SearchWalkPlanning.CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger).Threads(threadCount).BuildParallel().Run(() => entry =>
            {
                if (!entry.IsFile)
                {
                    return WalkState.Continue;
                }

                using MemoryStream buffer = new();
                var writer = new RawByteWriter(buffer);
                var fileSummary = new JsonSearchSummary();
                bool fileMatched = false;
                bool fileErrored = false;
                byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, pathSeparator: null);
                SearchJsonDirectoryEntryFile(entry, displayPath, pattern, regexPlan, lowArgs, asciiCaseInsensitive, fileSummary, writer, diagnostics, logger, ref fileMatched, ref fileErrored);
                writer.Flush();
                if (fileMatched)
                {
                    Interlocked.Exchange(ref matchedFlag, 1);
                }

                if (fileErrored)
                {
                    Interlocked.Exchange(ref erroredFlag, 1);
                }

                lock (summaryLock)
                {
                    summary.Add(fileSummary);
                }

                byte[] body = buffer.ToArray();
                if (body.Length > 0)
                {
                    outputs.Add(body);
                }

                return WalkState.Continue;
            });
        }
        finally
        {
            outputs.CompleteAdding();
        }

        printTask.Join();
        matched |= Volatile.Read(ref matchedFlag) != 0;
        errored |= Volatile.Read(ref erroredFlag) != 0;
    }

    private static void SearchJsonFile(
        string path,
        byte[] displayPath,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool autoMmapEligible,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        ref bool matched,
        ref bool errored)
    {
        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, logger, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path, readKind);
        matched |= SearchJsonBytes(bytes, pattern, regexPlan, output, displayPath, summary, lowArgs.TextMode, SearchesBinaryAsText(readKind), lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
    }

    private static void SearchJsonRawUnixFile(
        SearchPathArgument path,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        ref bool matched,
        ref bool errored)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out _))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path.DisplayText, SearchFileReadKind.Buffered);
        matched |= SearchJsonBytes(bytes, pattern, regexPlan, output, path.DisplayBytes, summary, lowArgs.TextMode, searchBinaryAsText: false, lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
    }

    private static void SearchJsonDirectoryEntryFile(
        DirEntry entry,
        byte[] displayPath,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        ref bool matched,
        ref bool errored)
    {
        if (entry.IsRawUnixPath)
        {
            var path = SearchPathArgument.FromUnixBytes(entry.UnixPathBytes, displayPath);
            SearchJsonRawUnixFile(path, pattern, regexPlan, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, logger, ref matched, ref errored);
            return;
        }

        SearchJsonFile(entry.FullPath, displayPath, pattern, regexPlan, false, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, logger, ref matched, ref errored);
    }

    private static bool SearchJsonBytes(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        RawByteWriter output,
        byte[] path,
        JsonSearchSummary summary,
        bool textMode,
        bool searchBinaryAsText,
        bool quiet,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch)
    {
        int binaryOffset = textMode || nullData ? -1 : bytes.AsSpan().IndexOf((byte)0);
        byte[] searchBytes = BinaryDetection.GetSearchBytes(bytes, textMode || searchBinaryAsText, nullData);
        var writer = new JsonFileWriter(output, path, quiet, binaryOffset);
        bool matched = regexPlan.Options.Multiline && TrySearchJsonMultilineBytes(searchBytes, regexPlan, writer, invertMatch, nullData, replacement, maxCount, beforeContext, afterContext, passthru, stopOnNonmatch, out bool multilineMatched)
            ? multilineMatched
            : SearchJsonLines(searchBytes, pattern, regexPlan, writer, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, replacement, maxCount, beforeContext, afterContext, passthru, stopOnNonmatch);
        ulong bytesSearched = searchBinaryAsText && binaryOffset >= 0 ? (ulong)binaryOffset : (ulong)bytes.Length;
        writer.Finish(bytesSearched, summary);
        return matched;
    }

    private static bool SearchesBinaryAsText(SearchFileReadKind readKind)
    {
        return readKind == SearchFileReadKind.MemoryMapped;
    }

    private static bool TrySearchJsonMultilineBytes(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan searchPlan,
        JsonFileWriter writer,
        bool invertMatch,
        bool nullData,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch,
        out bool matched)
    {
        matched = false;
        bool contextOutputRequested = beforeContext > 0 || afterContext > 0 || passthru;
        if (nullData && contextOutputRequested)
        {
            return false;
        }

        if (bytes.IsEmpty)
        {
            return true;
        }

        if (invertMatch)
        {
            matched = contextOutputRequested
                ? SearchJsonMultilineInvertedContextBytes(bytes, searchPlan, writer, maxCount, beforeContext, afterContext, passthru)
                : WriteJsonMultilineInvertedMatches(bytes, searchPlan, writer, maxCount);
            return true;
        }

        matched = contextOutputRequested
            ? SearchJsonMultilineContextBytes(bytes, searchPlan, writer, replacement, maxCount, beforeContext, afterContext, passthru, stopOnNonmatch)
            : SearchJsonMultilineMatchBytes(bytes, searchPlan, writer, nullData, replacement, maxCount);
        return true;
    }

    private static bool SearchJsonMultilineMatchBytes(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan searchPlan,
        JsonFileWriter writer,
        bool nullData,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount)
    {
        bool matched = false;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        int groupStart = -1;
        int groupEnd = -1;
        int groupLastLineStart = -1;
        var matches = new List<JsonMatchSpan>(capacity: 1);
        using RegexReplacementSession? replacementSession =
            replacement is ReadOnlyMemory<byte> replacementValue
                ? new RegexReplacementSession(replacementValue, searchPlan)
                : null;
        while (MultilineSearchOperations.TryFindNextMultilineMatch(bytes, searchPlan, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            bool selectionOnly = MultilineSearchOperations.IsSelectionOnlyEofMatch(bytes, match, offset);
            bool omitSubmatch = match.Length == 0 && match.Start == bytes.Length;
            int firstLineStart = GetJsonMultilineLineStart(bytes, match.Start, nullData);
            if (omitSubmatch && firstLineStart >= bytes.Length)
            {
                break;
            }

            matched = true;
            int lastLineStart = GetJsonMultilineMatchLastLineStart(bytes, match, nullData);
            int lineEnd = GetJsonMultilineLineEnd(bytes, lastLineStart, nullData);
            if (groupStart >= 0 && firstLineStart > groupEnd)
            {
                WriteJsonMultilineMatchGroup(bytes, writer, groupStart, groupEnd, groupLastLineStart, matches, nullData);
                matches.Clear();
                groupStart = -1;
                groupEnd = -1;
                groupLastLineStart = -1;
            }

            if (groupStart < 0)
            {
                groupStart = firstLineStart;
            }

            if (lineEnd > groupEnd)
            {
                groupEnd = lineEnd;
                groupLastLineStart = lastLineStart;
            }

            if (!omitSubmatch)
            {
                byte[]? expandedReplacement = replacementSession?.Expand(
                    bytes,
                    match.Start,
                    match.Length);
                matches.Add(new JsonMatchSpan(match.Start - groupStart, match.Start - groupStart + match.Length, expandedReplacement));
            }

            if (!selectionOnly)
            {
                emitted++;
                if (maxCount is ulong limit && emitted >= limit)
                {
                    WriteJsonMultilineMatchGroup(bytes, writer, groupStart, groupEnd, groupLastLineStart, matches, nullData);
                    return matched;
                }
            }

            offset = MultilineSearchOperations.AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        if (groupStart >= 0)
        {
            WriteJsonMultilineMatchGroup(bytes, writer, groupStart, groupEnd, groupLastLineStart, matches, nullData);
        }

        return matched;
    }

    private static bool SearchJsonMultilineContextBytes(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan searchPlan,
        JsonFileWriter writer,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch)
    {
        List<RegexMatch> matches = MultilineSearchOperations.CollectMultilineMatches(bytes, searchPlan);
        List<ContextLineInfo> lines = MultilineSearchOperations.BuildMultilineContextLines(bytes, matches, stopOnNonmatch);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : MultilineSearchOperations.IncludeMultilineContextLines(bytes, lines, matches, included, beforeContext, afterContext, maxCount);
        ulong? renderedMatchLimit = passthru ? maxCount : null;
        var contextMatches = new List<JsonMatchSpan>(capacity: 0);
        using RegexReplacementSession? replacementSession =
            replacement is ReadOnlyMemory<byte> replacementValue
                ? new RegexReplacementSession(replacementValue, searchPlan)
                : null;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            ContextLineInfo line = lines[index];
            if (line.SelectedMatch && MultilineSearchOperations.MultilineLineHasRenderedMatch(bytes, line, matches, renderedMatchLimit))
            {
                if (TryWriteJsonMultilineMatchGroupStartingAtLine(bytes, lines, index, matches, renderedMatchLimit, replacementSession, writer, out int consumedLineIndex))
                {
                    index = Math.Max(index, consumedLineIndex);
                }

                continue;
            }

            writer.WriteContextLine(line.LineNumber, line.Start, bytes.Slice(line.Start, line.Length), contextMatches);
        }

        return matched;
    }

    private static bool SearchJsonMultilineInvertedContextBytes(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan searchPlan,
        JsonFileWriter writer,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru)
    {
        List<ContextLineInfo> lines = MultilineSearchOperations.BuildMultilineInvertedLines(bytes, searchPlan);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : ContextSearchOperations.IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
        var matches = new List<JsonMatchSpan>(capacity: 0);
        ulong passthruPrimaryMatches = 0;
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

            if (selectedMatch)
            {
                writer.WriteMatchLine(line.LineNumber, line.Start, bytes.Slice(line.Start, line.Length), matches);
            }
            else
            {
                writer.WriteContextLine(line.LineNumber, line.Start, bytes.Slice(line.Start, line.Length), matches);
            }
        }

        return matched;
    }

    private static bool TryWriteJsonMultilineMatchGroupStartingAtLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> regexMatches,
        ulong? renderedMatchLimit,
        RegexReplacementSession? replacementSession,
        JsonFileWriter writer,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        int groupStart = -1;
        int groupEnd = -1;
        int groupLastLineStart = -1;
        var matches = new List<JsonMatchSpan>(capacity: 1);
        for (int index = 0; index < regexMatches.Count; index++)
        {
            if (!MultilineSearchOperations.IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = regexMatches[index];
            int firstLineStart = MultilineSearchOperations.GetLineStart(bytes, match.Start);
            if (groupStart < 0)
            {
                if (firstLineStart != line.Start)
                {
                    continue;
                }

                groupStart = firstLineStart;
            }
            else if (firstLineStart > groupEnd)
            {
                break;
            }

            int lastLineStart = MultilineSearchOperations.GetLineStart(bytes, MultilineSearchOperations.GetInclusiveMatchEnd(match));
            int lineEnd = MultilineSearchOperations.GetLineEnd(bytes, lastLineStart);
            if (lineEnd > groupEnd)
            {
                groupEnd = lineEnd;
                groupLastLineStart = lastLineStart;
            }

            if (match.Length != 0 || match.Start != bytes.Length)
            {
                byte[]? expandedReplacement = replacementSession?.Expand(
                    bytes,
                    match.Start,
                    match.Length);
                matches.Add(new JsonMatchSpan(match.Start - groupStart, match.Start - groupStart + match.Length, expandedReplacement));
            }
        }

        if (groupStart < 0)
        {
            return false;
        }

        writer.WriteMatchLine(
            MultilineSearchOperations.GetLineNumber(bytes, groupStart),
            groupStart,
            bytes[groupStart..groupEnd],
            matches,
            (ulong)(1 + MultilineSearchOperations.CountLineFeeds(bytes[groupStart..groupLastLineStart])));
        consumedLineIndex = Math.Max(consumedLineIndex, MultilineSearchOperations.GetMultilineLineIndex(lines, groupLastLineStart));
        return true;
    }

    private static int GetJsonMultilineMatchLastLineStart(ReadOnlySpan<byte> bytes, RegexMatch match, bool nullData)
    {
        int lastLineStart = GetJsonMultilineLineStart(bytes, MultilineSearchOperations.GetInclusiveMatchEnd(match), nullData);
        if (match.Length != 0 || match.Start >= bytes.Length)
        {
            return lastLineStart;
        }

        int lineEnd = GetJsonMultilineLineEnd(bytes, lastLineStart, nullData);
        if (!IsJsonLineTerminatorStart(bytes, match.Start, nullData))
        {
            return lastLineStart;
        }

        int nextLineStart = MultilineSearchOperations.GetNextLineStart(lineEnd, bytes.Length);
        return nextLineStart < bytes.Length ? nextLineStart : lastLineStart;
    }

    private static int GetJsonMultilineLineStart(ReadOnlySpan<byte> bytes, int offset, bool nullData)
    {
        return nullData ? GetTerminatedLineStart(bytes, offset, terminator: 0) : MultilineSearchOperations.GetLineStart(bytes, offset);
    }

    private static int GetJsonMultilineLineEnd(ReadOnlySpan<byte> bytes, int lineStart, bool nullData)
    {
        return nullData ? GetTerminatedLineEnd(bytes, lineStart, terminator: 0) : MultilineSearchOperations.GetLineEnd(bytes, lineStart);
    }

    private static bool IsJsonLineTerminatorStart(ReadOnlySpan<byte> bytes, int offset, bool nullData)
    {
        if (nullData)
        {
            return bytes[offset] == 0;
        }

        return bytes[offset] == (byte)'\n' ||
            bytes[offset] == (byte)'\r' && offset + 1 < bytes.Length && bytes[offset + 1] == (byte)'\n';
    }

    private static int GetTerminatedLineStart(ReadOnlySpan<byte> bytes, int offset, byte terminator)
    {
        int boundedOffset = Math.Clamp(offset, 0, bytes.Length);
        for (int index = boundedOffset - 1; index >= 0; index--)
        {
            if (bytes[index] == terminator)
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static int GetTerminatedLineEnd(ReadOnlySpan<byte> bytes, int lineStart, byte terminator)
    {
        int boundedStart = Math.Clamp(lineStart, 0, bytes.Length);
        int relativeEnd = bytes[boundedStart..].IndexOf(terminator);
        return relativeEnd < 0 ? bytes.Length : boundedStart + relativeEnd + 1;
    }

    private static void WriteJsonMultilineMatchGroup(
        ReadOnlySpan<byte> bytes,
        JsonFileWriter writer,
        int groupStart,
        int groupEnd,
        int groupLastLineStart,
        IReadOnlyList<JsonMatchSpan> matches,
        bool nullData)
    {
        writer.WriteMatchLine(
            GetJsonMultilineLineNumber(bytes, groupStart, nullData),
            groupStart,
            bytes[groupStart..groupEnd],
            matches,
            (ulong)(1 + CountJsonMultilineLineTerminators(bytes[groupStart..groupLastLineStart], nullData)));
    }

    private static long GetJsonMultilineLineNumber(ReadOnlySpan<byte> bytes, int lineStart, bool nullData)
    {
        return nullData ? 1 + ByteCounter.Count(bytes[..Math.Clamp(lineStart, 0, bytes.Length)], 0) : MultilineSearchOperations.GetLineNumber(bytes, lineStart);
    }

    private static long CountJsonMultilineLineTerminators(ReadOnlySpan<byte> bytes, bool nullData)
    {
        return nullData ? ByteCounter.Count(bytes, 0) : MultilineSearchOperations.CountLineFeeds(bytes);
    }

    private static bool WriteJsonMultilineInvertedMatches(
        ReadOnlySpan<byte> bytes,
        RegexSearchPlan searchPlan,
        JsonFileWriter writer,
        ulong? maxCount)
    {
        List<ContextLineInfo> lines = MultilineSearchOperations.BuildMultilineInvertedLines(bytes, searchPlan);
        var matches = new List<JsonMatchSpan>(capacity: 0);
        ulong emitted = 0;
        bool matched = false;
        for (int index = 0; index < lines.Count; index++)
        {
            ContextLineInfo line = lines[index];
            if (!line.SelectedMatch)
            {
                continue;
            }

            matched = true;
            writer.WriteMatchLine(line.LineNumber, line.Start, bytes.Slice(line.Start, line.Length), matches);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }
        }

        return matched;
    }

    private static bool SearchJsonLines(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan searchPlan,
        JsonFileWriter writer,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch)
    {
        ContextSearchResult searchResult = ContextSearchOperations.BuildSearchResult(
            bytes,
            pattern,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            stopOnNonmatch,
            searchPlan);
        List<ContextLineInfo> lines = searchResult.Lines;
        if (lines.Count == 0)
        {
            return false;
        }

        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : ContextSearchOperations.IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
        var matches = new List<JsonMatchSpan>();
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            ContextLineInfo line = lines[index];
            ReadOnlySpan<byte> lineBytes = bytes.AsSpan(line.Start, line.Length);
            if (line.SelectedMatch)
            {
                matches.Clear();
                if (!invertMatch)
                {
                    CollectJsonMatches(
                        lineBytes,
                        searchResult.GetMatches(line),
                        searchPlan,
                        matches,
                        replacement);
                }

                writer.WriteMatchLine(line.LineNumber, line.Start, lineBytes, matches);
            }
            else
            {
                matches.Clear();
                if (invertMatch && line.OriginalMatch)
                {
                    CollectJsonMatches(
                        lineBytes,
                        searchResult.GetMatches(line),
                        searchPlan,
                        matches,
                        replacement);
                }

                writer.WriteContextLine(line.LineNumber, line.Start, lineBytes, matches);
            }
        }

        return matched;
    }

    private static void CollectJsonMatches(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<ContextLineMatch> retainedMatches,
        RegexSearchPlan searchPlan,
        List<JsonMatchSpan> matches,
        ReadOnlyMemory<byte>? replacement)
    {
        var collector = new JsonMatchCollector(matches, replacement, searchPlan);
        try
        {
            for (int index = 0; index < retainedMatches.Length; index++)
            {
                ContextLineMatch match = retainedMatches[index];
                if (ContextSearchOperations.IsSelectionOnlyRecordEndMatch(line, match))
                {
                    continue;
                }

                collector.MatchedLine(
                    lineNumber: 1,
                    lineByteOffset: 0,
                    matchByteOffset: match.Start,
                    match.Column,
                    line,
                    line.Slice(match.Start, match.Length));
            }
        }
        finally
        {
            collector.Dispose();
        }
    }
}
