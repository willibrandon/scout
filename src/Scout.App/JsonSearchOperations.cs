using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Scout;

internal static class JsonSearchOperations
{
    private static readonly byte[] StandardInputPath = "<stdin>"u8.ToArray();

    internal static int Run(
        IReadOnlyList<OsString> positional,
        int firstPathIndex,
        bool patternsReadFromStandardInput,
        CliLowArgs lowArgs,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
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
            matched = SearchJsonStandardInput(pattern, standardInput, lowArgs, asciiCaseInsensitive, summary, output);
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
            SearchJsonPath(paths[index], pattern, standardInput, defaultRoot, paths.Count > 1, autoMmapEligible, lowArgs, fileTypes, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
        }

        summary.WriteSummary(output);
        output.Flush();
        return SearchOutputFormatting.GetSearchExitCode(matched, errored, lowArgs.Quiet);
    }

    private static bool SearchJsonStandardInput(
        IReadOnlyList<byte[]> pattern,
        Stream standardInput,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output)
    {
        byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, lowArgs.EncodingMode);
        return SearchJsonBytes(bytes, pattern, output, StandardInputPath, summary, lowArgs.TextMode, lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
    }

    private static void SearchJsonPath(
        SearchPathArgument pathArgument,
        IReadOnlyList<byte[]> pattern,
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
        ref bool matched,
        ref bool errored)
    {
        string? path = pathArgument.Text;
        if (pathArgument.IsRawUnixPath)
        {
            SearchJsonRawUnixFile(pathArgument, pattern, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            matched |= SearchJsonStandardInput(pattern, standardInput, lowArgs, asciiCaseInsensitive, summary, output);
            return;
        }

        if (Directory.Exists(path))
        {
            int threadCount = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);
            if (threadCount > 1)
            {
                SearchJsonDirectoryParallel(path, pattern, defaultRoot, lowArgs, fileTypes, asciiCaseInsensitive, summary, output, diagnostics, threadCount, ref matched, ref errored);
                return;
            }

            string fullRoot = Path.GetFullPath(path);
            foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(path, lowArgs, fileTypes, diagnostics))
            {
                byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(path, fullRoot, entry, defaultRoot, pathSeparator: null);
                SearchJsonDirectoryEntryFile(entry, displayPath, pattern, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
            }

            return;
        }

        if (File.Exists(path))
        {
            SearchJsonFile(path, pathArgument.DisplayBytes, pattern, autoMmapEligible, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
            return;
        }

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
        errored = true;
    }

    private static void SearchJsonDirectoryParallel(
        string root,
        IReadOnlyList<byte[]> pattern,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        int threadCount,
        ref bool matched,
        ref bool errored)
    {
        string fullRoot = Path.GetFullPath(root);
        using var outputs = new BlockingCollection<byte[]>();
        object summaryLock = new();
        int matchedFlag = 0;
        int erroredFlag = 0;
        var printTask = Task.Run(() =>
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
            SearchWalkPlanning.CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
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
                SearchJsonDirectoryEntryFile(entry, displayPath, pattern, lowArgs, asciiCaseInsensitive, fileSummary, writer, diagnostics, ref fileMatched, ref fileErrored);
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

        printTask.GetAwaiter().GetResult();
        matched |= Volatile.Read(ref matchedFlag) != 0;
        errored |= Volatile.Read(ref erroredFlag) != 0;
    }

    private static void SearchJsonFile(
        string path,
        byte[] displayPath,
        IReadOnlyList<byte[]> pattern,
        bool autoMmapEligible,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool matched,
        ref bool errored)
    {
        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out _))
        {
            errored = true;
            return;
        }

        matched |= SearchJsonBytes(bytes, pattern, output, displayPath, summary, lowArgs.TextMode, lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
    }

    private static void SearchJsonRawUnixFile(
        SearchPathArgument path,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool matched,
        ref bool errored)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out _))
        {
            errored = true;
            return;
        }

        matched |= SearchJsonBytes(bytes, pattern, output, path.DisplayBytes, summary, lowArgs.TextMode, lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
    }

    private static void SearchJsonDirectoryEntryFile(
        DirEntry entry,
        byte[] displayPath,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool matched,
        ref bool errored)
    {
        if (entry.IsRawUnixPath)
        {
            var path = SearchPathArgument.FromUnixBytes(entry.UnixPathBytes, displayPath);
            SearchJsonRawUnixFile(path, pattern, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
            return;
        }

        SearchJsonFile(entry.FullPath, displayPath, pattern, false, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
    }

    private static bool SearchJsonBytes(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        RawByteWriter output,
        byte[] path,
        JsonSearchSummary summary,
        bool textMode,
        bool quiet,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool multiline,
        bool multilineDotall,
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
        byte[] searchBytes = BinaryDetection.GetSearchBytes(bytes, textMode, nullData);
        var writer = new JsonFileWriter(output, path, quiet, binaryOffset);
        bool matched = multiline && (nullData || PatternPreparation.ShouldUseJsonMultilineRegex(pattern, multilineDotall)) && TrySearchJsonMultilineBytes(searchBytes, pattern, writer, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multilineDotall, crlf, nullData, replacement, maxCount, beforeContext, afterContext, passthru, stopOnNonmatch, out bool multilineMatched)
            ? multilineMatched
            : SearchJsonLines(searchBytes, pattern, writer, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, replacement, maxCount, beforeContext, afterContext, passthru, stopOnNonmatch);
        writer.Finish((ulong)bytes.Length, summary);
        return matched;
    }

    private static bool TrySearchJsonMultilineBytes(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        JsonFileWriter writer,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        bool crlf,
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
        if (crlf || (nullData && contextOutputRequested))
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
                ? SearchJsonMultilineInvertedContextBytes(bytes, patterns, writer, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount, beforeContext, afterContext, passthru)
                : WriteJsonMultilineInvertedMatches(bytes, patterns, writer, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        matched = contextOutputRequested
            ? SearchJsonMultilineContextBytes(bytes, patterns, writer, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, replacement, maxCount, beforeContext, afterContext, passthru, stopOnNonmatch)
            : SearchJsonMultilineMatchBytes(bytes, patterns, writer, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, nullData, replacement, maxCount);
        return true;
    }

    private static bool SearchJsonMultilineMatchBytes(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        JsonFileWriter writer,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
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
        while (MultilineSearchOperations.TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
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
                byte[]? expandedReplacement = replacement is ReadOnlyMemory<byte> replacementValue
                    ? ReplacementFormatter.Expand(replacementValue.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive)
                    : null;
                matches.Add(new JsonMatchSpan(match.Start - groupStart, match.Start - groupStart + match.Length, expandedReplacement));
            }

            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                WriteJsonMultilineMatchGroup(bytes, writer, groupStart, groupEnd, groupLastLineStart, matches, nullData);
                return matched;
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
        IReadOnlyList<byte[]> patterns,
        JsonFileWriter writer,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch)
    {
        List<RegexMatch> matches = MultilineSearchOperations.CollectMultilineMatches(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
        List<ContextLineInfo> lines = MultilineSearchOperations.BuildMultilineContextLines(bytes, matches, stopOnNonmatch);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : MultilineSearchOperations.IncludeMultilineContextLines(bytes, lines, matches, included, beforeContext, afterContext, maxCount);
        ulong? renderedMatchLimit = passthru ? maxCount : null;
        var contextMatches = new List<JsonMatchSpan>(capacity: 0);
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            ContextLineInfo line = lines[index];
            if (line.SelectedMatch && MultilineSearchOperations.MultilineLineHasRenderedMatch(bytes, line, matches, renderedMatchLimit))
            {
                if (TryWriteJsonMultilineMatchGroupStartingAtLine(bytes, lines, index, matches, renderedMatchLimit, replacement, patterns, asciiCaseInsensitive, writer, out int consumedLineIndex))
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
        IReadOnlyList<byte[]> patterns,
        JsonFileWriter writer,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru)
    {
        List<ContextLineInfo> lines = MultilineSearchOperations.BuildMultilineInvertedLines(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
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
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
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

            byte[]? expandedReplacement = replacement is ReadOnlyMemory<byte> replacementValue
                ? ReplacementFormatter.Expand(replacementValue.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive)
                : null;
            matches.Add(new JsonMatchSpan(match.Start - groupStart, match.Start - groupStart + match.Length, expandedReplacement));
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
        IReadOnlyList<byte[]> patterns,
        JsonFileWriter writer,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        List<ContextLineInfo> lines = MultilineSearchOperations.BuildMultilineInvertedLines(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
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
        List<ContextLineInfo> lines = ContextSearchOperations.BuildLines(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, stopOnNonmatch);
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
                    CollectJsonMatches(lineBytes, pattern, matches, replacement, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
                }

                writer.WriteMatchLine(line.LineNumber, line.Start, lineBytes, matches);
            }
            else
            {
                matches.Clear();
                if (invertMatch && line.OriginalMatch)
                {
                    CollectJsonMatches(lineBytes, pattern, matches, replacement, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
                }

                writer.WriteContextLine(line.LineNumber, line.Start, lineBytes, matches);
            }
        }

        return matched;
    }

    private static void CollectJsonMatches(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> pattern,
        List<JsonMatchSpan> matches,
        ReadOnlyMemory<byte>? replacement,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        var collector = new JsonMatchCollector(matches, replacement, pattern, asciiCaseInsensitive);
        LiteralLineSearcher.SearchMatchLines(line, pattern, ref collector, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: crlf, nullData: nullData);
    }
}
