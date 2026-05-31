using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Scout;

internal static class Pcre2SearchOperations
{
    private static readonly byte[] StandardInputPath = "<stdin>"u8.ToArray();
    private static readonly byte[] NullByte = [0];
    private static readonly byte[] LineFeed = [(byte)'\n'];
    private static readonly byte[] CrlfLineTerminator = [(byte)'\r', (byte)'\n'];

    internal static int Run(
        IReadOnlyList<OsString> positional,
        int firstPathIndex,
        bool patternsReadFromStandardInput,
        CliLowArgs lowArgs,
        IReadOnlyList<byte[]> patterns,
        Stream standardInput,
        bool standardInputIsReadable,
        RawByteWriter output,
        DiagnosticMessenger diagnostics)
    {
        if (!Pcre2Library.IsAvailable)
        {
            diagnostics.ErrorMessage(new ScoutError(Pcre2Library.UnavailableErrorMessage).WithContext("rg"));
            return ExitCode.Error;
        }

        if (!CanRun(lowArgs))
        {
            diagnostics.ErrorMessage(new ScoutError("PCRE2 search does not support this option combination").WithContext("rg"));
            return ExitCode.Error;
        }

        if (lowArgs.MaxCount == 0)
        {
            output.Flush();
            return ExitCode.NoMatch;
        }

        if (!SearchWalkPlanning.TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? fileTypes, out ScoutError? error))
        {
            diagnostics.ErrorMessage(error!.WithContext("rg"));
            return ExitCode.Error;
        }

        OutputSeparators separators = GetOutputSeparators(lowArgs);
        OutputLineLimit lineLimit = GetOutputLineLimit(lowArgs);
        OutputColor color = GetOutputColor(lowArgs);
        List<byte[]> pcre2Patterns = PreparePcre2Patterns(patterns, lowArgs.FixedStrings);
        bool heading = ShouldUseHeading(lowArgs);
        bool wroteHeadingOutput = false;
        bool matched = false;
        bool errored = false;
        bool stats = lowArgs.Stats && lowArgs.SearchMode != CliSearchMode.Json && lowArgs.MaxCount != 0;
        long statsStarted = Stopwatch.GetTimestamp();
        SearchStats searchStats = default;
        bool useDefaultCurrentDirectory = positional.Count == firstPathIndex &&
            (patternsReadFromStandardInput || !standardInputIsReadable);

        var paths = new List<SearchPathArgument>(Math.Max(1, positional.Count - firstPathIndex));
        if (positional.Count == firstPathIndex && !useDefaultCurrentDirectory)
        {
            try
            {
                byte[] stdinBytes = SearchFileContentReader.ReadSearchStream(standardInput, lowArgs.EncodingMode);
                byte[] pattern = BuildPcre2Pattern(pcre2Patterns);
                using var regex = new Pcre2Regex(pattern, GetPcre2CompileOptions(lowArgs, pcre2Patterns));
                JsonSearchSummary? jsonSummary = lowArgs.SearchMode == CliSearchMode.Json ? new JsonSearchSummary() : null;
                OutputPath stdinPath = new(StandardInputPath, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty);
                OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, lowArgs.Vimgrep, lowArgs.WithFilename);
                matched = stats
                    ? RunPcre2SearchModeWithStats(stdinBytes, regex, output, separators, stdinPath, prefix, lineLimit, color, lowArgs, pcre2Patterns, jsonSummary, heading, ref wroteHeadingOutput, ref searchStats)
                    : RunPcre2SearchModeWithOptionalHeading(stdinBytes, regex, output, separators, stdinPath, prefix, lineLimit, color, lowArgs, pcre2Patterns, jsonSummary, heading, ref wroteHeadingOutput);
                jsonSummary?.WriteSummary(output);
                if (stats)
                {
                    StatsTextWriter.Write(output, searchStats, Stopwatch.GetElapsedTime(statsStarted));
                }

                output.Flush();
                return SearchOutputFormatting.GetSearchExitCode(matched, errored, lowArgs.Quiet);
            }
            catch (Pcre2Exception exception)
            {
                diagnostics.ErrorMessage(new ScoutError(exception.Message).WithContext("rg"));
                return ExitCode.Error;
            }
        }

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

        bool prefixPaths = lowArgs.Vimgrep || paths.Count > 1 || SearchPathArgument.ContainsDirectory(paths);
        bool autoMmapEligible = SearchPathArgument.IsAutoMmapEligible(paths);
        try
        {
            byte[] pattern = BuildPcre2Pattern(pcre2Patterns);
            Pcre2CompileOptions compileOptions = GetPcre2CompileOptions(lowArgs, pcre2Patterns);
            using var regex = new Pcre2Regex(pattern, compileOptions);
            JsonSearchSummary? jsonSummary = lowArgs.SearchMode == CliSearchMode.Json ? new JsonSearchSummary() : null;
            for (int index = 0; index < paths.Count; index++)
            {
                bool defaultRoot = useDefaultCurrentDirectory && index == 0;
                SearchPcre2Path(paths[index], standardInput, defaultRoot, prefixPaths, autoMmapEligible, lowArgs, regex, pattern, compileOptions, pcre2Patterns, jsonSummary, separators, lineLimit, color, fileTypes!, stats, ref searchStats, output, diagnostics, heading, ref wroteHeadingOutput, ref matched, ref errored);
                if (matched && lowArgs.Quiet)
                {
                    break;
                }
            }

            jsonSummary?.WriteSummary(output);
            if (stats)
            {
                StatsTextWriter.Write(output, searchStats, Stopwatch.GetElapsedTime(statsStarted));
            }

            output.Flush();
            return SearchOutputFormatting.GetSearchExitCode(matched, errored, lowArgs.Quiet);
        }
        catch (Pcre2Exception exception)
        {
            diagnostics.ErrorMessage(new ScoutError(exception.Message).WithContext("rg"));
            return ExitCode.Error;
        }
    }

    internal static bool CanRun(CliLowArgs lowArgs)
    {
        bool contextRequested = lowArgs.BeforeContext != 0 ||
            lowArgs.AfterContext != 0 ||
            lowArgs.Passthru;
        bool unsupportedContextSearchMode = contextRequested &&
            lowArgs.SearchMode != CliSearchMode.Standard &&
            lowArgs.SearchMode != CliSearchMode.Json;
        return (lowArgs.SearchMode is CliSearchMode.Standard
                or CliSearchMode.Json
                or CliSearchMode.Count
                or CliSearchMode.CountMatches
                or CliSearchMode.FilesWithMatches
                or CliSearchMode.FilesWithoutMatch) &&
            (!lowArgs.NullData || !lowArgs.Multiline) &&
            !unsupportedContextSearchMode &&
            (lowArgs.Replacement is null ||
                lowArgs.SearchMode == CliSearchMode.Json ||
                (lowArgs.SearchMode == CliSearchMode.Standard && (!lowArgs.OnlyMatching || lowArgs.Multiline)));
    }

    private static void SearchPcre2Path(
        SearchPathArgument pathArgument,
        Stream standardInput,
        bool defaultRoot,
        bool prefixPaths,
        bool autoMmapEligible,
        CliLowArgs lowArgs,
        Pcre2Regex regex,
        byte[] pcre2Pattern,
        Pcre2CompileOptions compileOptions,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        bool collectStats,
        ref SearchStats stats,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        string? path = pathArgument.Text;
        if (pathArgument.IsRawUnixPath)
        {
            OutputPath outputPath = SearchOutputFormatting.CreateRawUnixOutputPath(pathArgument);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchPcre2RawUnixFile(pathArgument, lowArgs, regex, patterns, jsonSummary, collectStats, ref stats, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, lowArgs.EncodingMode);
            OutputPath outputPath = new(StandardInputPath, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty);
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= collectStats
                ? RunPcre2SearchModeWithStats(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput, ref stats)
                : RunPcre2SearchModeWithOptionalHeading(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchPcre2Directory(path, defaultRoot, lowArgs, regex, pcre2Pattern, compileOptions, patterns, jsonSummary, separators, lineLimit, color, fileTypes, collectStats, ref stats, output, diagnostics, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchPcre2File(path, lowArgs, autoMmapEligible, regex, patterns, jsonSummary, collectStats, ref stats, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        SearchErrorMessage(lowArgs, diagnostics, MissingPathError(path, prefixPaths));
        errored = true;
    }

    private static void SearchPcre2Directory(
        string root,
        bool defaultRoot,
        CliLowArgs lowArgs,
        Pcre2Regex regex,
        byte[] pcre2Pattern,
        Pcre2CompileOptions compileOptions,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        bool collectStats,
        ref SearchStats stats,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        if (matched && lowArgs.Quiet)
        {
            return;
        }

        int threadCount = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            SearchPcre2DirectoryParallel(root, defaultRoot, lowArgs, pcre2Pattern, compileOptions, patterns, jsonSummary, separators, lineLimit, color, fileTypes, collectStats, ref stats, output, diagnostics, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics))
        {
            byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
            SearchPcre2DirectoryEntryFile(entry, displayPath, lowArgs, regex, patterns, jsonSummary, collectStats, ref stats, output, diagnostics, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
            if (matched && lowArgs.Quiet)
            {
                return;
            }
        }
    }

    private static void SearchPcre2DirectoryParallel(
        string root,
        bool defaultRoot,
        CliLowArgs lowArgs,
        byte[] pcre2Pattern,
        Pcre2CompileOptions compileOptions,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        bool collectStats,
        ref SearchStats stats,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        bool heading,
        int threadCount,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        string fullRoot = Path.GetFullPath(root);
        using var outputs = new BlockingCollection<byte[]>();
        using var workerRegexes = new ThreadLocal<Pcre2Regex>(() => new Pcre2Regex(pcre2Pattern, compileOptions), trackAllValues: true);
        object summaryLock = new();
        object statsLock = new();
        SearchStats aggregateStats = default;
        int matchedFlag = 0;
        int erroredFlag = 0;
        bool printedHeading = wroteHeadingOutput;
        var printTask = Task.Run(() =>
        {
            foreach (byte[] body in outputs.GetConsumingEnumerable())
            {
                if (body.Length == 0)
                {
                    continue;
                }

                if (heading && printedHeading)
                {
                    output.Write("\n"u8);
                }

                output.Write(body);
                if (heading)
                {
                    printedHeading = true;
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
                JsonSearchSummary? fileSummary = jsonSummary is null ? null : new JsonSearchSummary();
                bool fileWroteHeading = false;
                bool fileMatched = false;
                bool fileErrored = false;
                SearchStats fileStats = default;
                byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
                SearchPcre2DirectoryEntryFile(entry, displayPath, lowArgs, workerRegexes.Value!, patterns, fileSummary, collectStats, ref fileStats, writer, diagnostics, separators, lineLimit, color, heading, ref fileWroteHeading, ref fileMatched, ref fileErrored);
                writer.Flush();
                if (fileMatched)
                {
                    Interlocked.Exchange(ref matchedFlag, 1);
                }

                if (fileErrored)
                {
                    Interlocked.Exchange(ref erroredFlag, 1);
                }

                if (fileSummary is not null)
                {
                    lock (summaryLock)
                    {
                        jsonSummary!.Add(fileSummary);
                    }
                }

                if (collectStats)
                {
                    lock (statsLock)
                    {
                        aggregateStats.Add(fileStats);
                    }
                }

                byte[] body = buffer.ToArray();
                if (body.Length > 0)
                {
                    outputs.Add(body);
                }

                return !collectStats && fileMatched && lowArgs.Quiet ? WalkState.Quit : WalkState.Continue;
            });
        }
        finally
        {
            outputs.CompleteAdding();
        }

        printTask.GetAwaiter().GetResult();
        foreach (Pcre2Regex workerRegex in workerRegexes.Values)
        {
            workerRegex.Dispose();
        }

        wroteHeadingOutput = printedHeading;
        if (collectStats)
        {
            stats.Add(aggregateStats);
        }

        matched |= Volatile.Read(ref matchedFlag) != 0;
        errored |= Volatile.Read(ref erroredFlag) != 0;
    }

    private static void SearchPcre2DirectoryEntryFile(
        DirEntry entry,
        byte[] displayPath,
        CliLowArgs lowArgs,
        Pcre2Regex regex,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        bool collectStats,
        ref SearchStats stats,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        OutputPath outputPath = SearchOutputFormatting.CreateDirectoryEntryOutputPath(entry, displayPath, lowArgs, color);
        OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, autoPrefixPath: true, lowArgs.WithFilename, outputPath);
        if (entry.IsRawUnixPath)
        {
            var path = SearchPathArgument.FromUnixBytes(entry.UnixPathBytes, displayPath);
            SearchPcre2RawUnixFile(path, lowArgs, regex, patterns, jsonSummary, collectStats, ref stats, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        SearchPcre2File(entry.FullPath, lowArgs, autoMmapEligible: false, regex, patterns, jsonSummary, collectStats, ref stats, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
    }

    private static void SearchPcre2File(
        string path,
        CliLowArgs lowArgs,
        bool autoMmapEligible,
        Pcre2Regex regex,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        bool collectStats,
        ref SearchStats stats,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        OutputPath outputPath,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out _))
        {
            errored = true;
            return;
        }

        matched |= collectStats
            ? RunPcre2SearchModeWithStats(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput, ref stats)
            : RunPcre2SearchModeWithOptionalHeading(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
    }

    private static void SearchPcre2RawUnixFile(
        SearchPathArgument path,
        CliLowArgs lowArgs,
        Pcre2Regex regex,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        bool collectStats,
        ref SearchStats stats,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        OutputPath outputPath,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out _))
        {
            errored = true;
            return;
        }

        matched |= collectStats
            ? RunPcre2SearchModeWithStats(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput, ref stats)
            : RunPcre2SearchModeWithOptionalHeading(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
    }

    private static bool RunPcre2SearchModeWithOptionalHeading(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath path,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliLowArgs lowArgs,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        bool heading,
        ref bool wroteHeadingOutput)
    {
        if (lowArgs.SearchMode == CliSearchMode.Json)
        {
            return RunPcre2JsonSearch(bytes, regex, output, path.Display, lowArgs, patterns, jsonSummary!);
        }

        if (lowArgs.Quiet)
        {
            return SearchPcre2Quiet(bytes, regex, lowArgs.SearchMode, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.InvertMatch, lowArgs.MaxCount, separators.LineTerminator);
        }

        if (!heading)
        {
            return RunPcre2SearchMode(
                bytes,
                regex,
                output,
                separators,
                path,
                prefix,
                lineLimit,
                color,
                lowArgs.SearchMode,
                lowArgs.Vimgrep,
                lowArgs.OnlyMatching,
                lowArgs.Replacement,
                patterns,
                lowArgs.LineRegexp,
                lowArgs.WordRegexp,
                lowArgs.Multiline,
                lowArgs.InvertMatch,
                SearchOutputFormatting.EffectiveLineNumber(lowArgs),
                SearchOutputFormatting.EffectiveColumn(lowArgs),
                lowArgs.ByteOffset,
                lowArgs.Trim,
                lowArgs.IncludeZero,
                lowArgs.NullPathTerminator,
                lowArgs.BeforeContext,
                lowArgs.AfterContext,
                lowArgs.Passthru,
                lowArgs.StopOnNonmatch,
                lowArgs.MaxCount);
        }

        using MemoryStream bufferedOutput = new();
        var bufferedWriter = new RawByteWriter(bufferedOutput);
        bool matched = RunPcre2SearchMode(
            bytes,
            regex,
            bufferedWriter,
            separators,
            path,
            null,
            lineLimit,
            color,
            lowArgs.SearchMode,
            lowArgs.Vimgrep,
            lowArgs.OnlyMatching,
            lowArgs.Replacement,
            patterns,
            lowArgs.LineRegexp,
            lowArgs.WordRegexp,
            lowArgs.Multiline,
            lowArgs.InvertMatch,
            SearchOutputFormatting.EffectiveLineNumber(lowArgs),
            SearchOutputFormatting.EffectiveColumn(lowArgs),
            lowArgs.ByteOffset,
            lowArgs.Trim,
            lowArgs.IncludeZero,
            lowArgs.NullPathTerminator,
            lowArgs.BeforeContext,
            lowArgs.AfterContext,
            lowArgs.Passthru,
            lowArgs.StopOnNonmatch,
            lowArgs.MaxCount);
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
            SearchOutputFormatting.WriteSearchPathTerminator(output, lowArgs.NullPathTerminator, separators.LineTerminator);
        }

        output.Write(body);
        wroteHeadingOutput = true;
        return matched;
    }

    private static bool RunPcre2SearchModeWithStats(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath path,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliLowArgs lowArgs,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
        bool heading,
        ref bool wroteHeadingOutput,
        ref SearchStats stats)
    {
        long started = Stopwatch.GetTimestamp();
        using MemoryStream buffer = new();
        var bufferedWriter = new RawByteWriter(buffer);
        bool matched = RunPcre2SearchModeWithOptionalHeading(bytes, regex, bufferedWriter, separators, path, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
        bufferedWriter.Flush();
        byte[] body = buffer.ToArray();
        output.Write(body);

        SearchStats fileStats = CollectPcre2SearchStats(
            bytes,
            regex,
            lowArgs.SearchMode,
            lowArgs.LineRegexp,
            lowArgs.WordRegexp,
            lowArgs.Multiline,
            lowArgs.InvertMatch,
            lowArgs.MaxCount,
            lowArgs.StopOnNonmatch,
            separators.LineTerminator,
            lowArgs.SearchMode == CliSearchMode.Standard ? (ulong)body.Length : 0,
            Stopwatch.GetElapsedTime(started));
        stats.Add(fileStats);
        return matched;
    }

    private static SearchStats CollectPcre2SearchStats(
        byte[] bytes,
        Pcre2Regex regex,
        CliSearchMode searchMode,
        bool lineRegexp,
        bool wordRegexp,
        bool multiline,
        bool invertMatch,
        ulong? maxCount,
        bool stopOnNonmatch,
        ReadOnlyMemory<byte> lineTerminator,
        ulong bytesPrinted,
        TimeSpan elapsed)
    {
        var stats = new SearchStats();
        stats.AddElapsed(elapsed);
        stats.AddSearch();
        stats.AddBytesPrinted(bytesPrinted);

        ulong bytesSearched = (ulong)bytes.Length;
        bool statsInvertMatch = searchMode == CliSearchMode.FilesWithoutMatch ? false : invertMatch;
        if (multiline && !statsInvertMatch)
        {
            CollectPcre2MultilineStats(bytes, regex, lineRegexp, wordRegexp, maxCount, ref stats, ref bytesSearched);
        }
        else
        {
            CollectPcre2LineStats(bytes, regex, lineRegexp, wordRegexp, statsInvertMatch, maxCount, stopOnNonmatch, lineTerminator, ref stats, ref bytesSearched);
        }

        if (stats.MatchedLines > 0)
        {
            stats.AddSearchWithMatch();
        }

        stats.AddBytesSearched(bytesSearched);
        return stats;
    }

    private static void CollectPcre2LineStats(
        byte[] bytes,
        Pcre2Regex regex,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        ulong? maxCount,
        bool stopOnNonmatch,
        ReadOnlyMemory<byte> lineTerminator,
        ref SearchStats stats,
        ref ulong bytesSearched)
    {
        int lineStart = 0;
        ulong primaryMatches = 0;
        bool hasSelectedMatch = false;
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, lineTerminator, out ReadOnlySpan<byte> outputLine, out ReadOnlySpan<byte> matchLine, out int nextLineStart, out bool isLastLine);
            int matchCount = CountPcre2LineMatches(matchLine, regex, lineRegexp, wordRegexp);
            bool selected = invertMatch ? matchCount == 0 : matchCount > 0;
            if (stopOnNonmatch && hasSelectedMatch && !selected)
            {
                break;
            }

            if (selected)
            {
                stats.AddMatchedLine();
                if (!invertMatch)
                {
                    stats.AddMatches((ulong)matchCount);
                }

                hasSelectedMatch = true;
                primaryMatches++;
                if (maxCount is ulong limit && primaryMatches >= limit)
                {
                    bytesSearched = (ulong)(lineStart + outputLine.Length);
                    break;
                }
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
        }
    }

    private static void CollectPcre2MultilineStats(
        byte[] bytes,
        Pcre2Regex regex,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        ref SearchStats stats,
        ref ulong bytesSearched)
    {
        int offset = 0;
        ulong countedLines = 0;
        var lineStarts = new HashSet<int>();
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            stats.AddMatches(1);
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStarts.Add(lineStart))
                {
                    stats.AddMatchedLine();
                    countedLines++;
                    if (maxCount is ulong limit && countedLines >= limit)
                    {
                        bytesSearched = (ulong)lineEnd;
                        return;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
            }
        }
    }

    private static bool SearchPcre2Quiet(byte[] bytes, Pcre2Regex regex, CliSearchMode searchMode, bool lineRegexp, bool wordRegexp, bool multiline, bool invertMatch, ulong? maxCount, ReadOnlyMemory<byte> lineTerminator)
    {
        bool hasMatch = multiline
            ? invertMatch
                ? HasPcre2MultilineInvertedMatch(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : HasPcre2MultilineMatch(bytes, regex, lineRegexp, wordRegexp)
            : HasPcre2Match(bytes, regex, lineRegexp, wordRegexp, invertMatch, lineTerminator);
        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return !hasMatch;
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            long count = multiline
                ? invertMatch
                    ? CountPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp, maxCount)
                    : CountPcre2MultilineMatches(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : CountPcre2Matches(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount, lineTerminator);
            return count > 0;
        }

        if (searchMode == CliSearchMode.Count)
        {
            long count = multiline
                ? invertMatch
                    ? CountPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp, maxCount)
                    : CountPcre2MultilineMatchingLines(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : CountPcre2MatchingLines(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount, lineTerminator);
            return count > 0;
        }

        return hasMatch;
    }

    internal static bool ShouldAutoUse(CliLowArgs lowArgs, IReadOnlyList<byte[]> patterns)
    {
        return lowArgs.RegexEngine == CliRegexEngine.Auto &&
            Pcre2Library.IsAvailable &&
            !lowArgs.FixedStrings &&
            DefaultRegexCompileFails(lowArgs, patterns);
    }

    private static bool DefaultRegexCompileFails(CliLowArgs lowArgs, IReadOnlyList<byte[]> patterns)
    {
        var defaultPatterns = new List<byte[]>(patterns.Count);
        for (int index = 0; index < patterns.Count; index++)
        {
            defaultPatterns.Add((byte[])patterns[index].Clone());
        }

        PatternPreparation.WrapRegexPatterns(defaultPatterns);
        bool asciiCaseInsensitive = PatternPreparation.IsAsciiCaseInsensitive(defaultPatterns, lowArgs.CaseMode);
        if (!lowArgs.Unicode && asciiCaseInsensitive)
        {
            PatternPreparation.WrapNonAsciiPatterns(defaultPatterns);
        }

        try
        {
            for (int index = 0; index < defaultPatterns.Count; index++)
            {
                _ = RegexAutomaton.Compile(
                    defaultPatterns[index],
                    asciiCaseInsensitive,
                    lowArgs.Multiline,
                    lowArgs.MultilineDotall,
                    lowArgs.Crlf,
                    lowArgs.NullData ? (byte)'\0' : (byte)'\n',
                    lowArgs.Unicode,
                    unicodeClasses: lowArgs.Unicode,
                    dfaSizeLimit: lowArgs.DfaSizeLimit);
            }

            return false;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static Pcre2CompileOptions GetPcre2CompileOptions(CliLowArgs lowArgs, IReadOnlyList<byte[]> patterns)
    {
        Pcre2CompileOptions options = Pcre2CompileOptions.MultiLine;
        if (PatternPreparation.IsAsciiCaseInsensitive(patterns, lowArgs.CaseMode))
        {
            options |= Pcre2CompileOptions.CaseInsensitive;
        }

        if (lowArgs.MultilineDotall)
        {
            options |= Pcre2CompileOptions.DotMatchesNewline;
        }

        if (lowArgs.Pcre2Unicode)
        {
            options |= Pcre2CompileOptions.Utf |
                Pcre2CompileOptions.UnicodeProperties;
        }

        return options;
    }

    private static List<byte[]> PreparePcre2Patterns(IReadOnlyList<byte[]> patterns, bool fixedStrings)
    {
        var prepared = new List<byte[]>(patterns.Count);
        for (int index = 0; index < patterns.Count; index++)
        {
            prepared.Add((byte[])patterns[index].Clone());
        }

        if (fixedStrings)
        {
            PatternPreparation.EscapeFixedStringPatterns(prepared);
        }

        return prepared;
    }

    private static byte[] BuildPcre2Pattern(List<byte[]> patterns)
    {
        if (patterns.Count == 1)
        {
            return patterns[0];
        }

        using MemoryStream buffer = new();
        for (int index = 0; index < patterns.Count; index++)
        {
            if (index > 0)
            {
                buffer.WriteByte((byte)'|');
            }

            buffer.Write("(?:"u8);
            byte[] pattern = patterns[index];
            buffer.Write(pattern);
            buffer.WriteByte((byte)')');
        }

        return buffer.ToArray();
    }

    private static bool RunPcre2SearchMode(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath path,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        bool lineRegexp,
        bool wordRegexp,
        bool multiline,
        bool invertMatch,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch,
        ulong? maxCount)
    {
        if (multiline)
        {
            return RunPcre2MultilineSearchMode(bytes, regex, output, separators, path, prefix, lineLimit, color, searchMode, vimgrep, onlyMatching, replacement, patterns, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, includeZero, nullPathTerminator, beforeContext, afterContext, passthru, stopOnNonmatch, maxCount);
        }

        if (searchMode == CliSearchMode.Count)
        {
            long count = onlyMatching && !invertMatch
                ? CountPcre2Matches(bytes, regex, lineRegexp, wordRegexp, invertMatch: false, maxCount, separators.LineTerminator)
                : CountPcre2MatchingLines(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount, separators.LineTerminator);
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            return SearchOutputFormatting.WriteCount(output, prefix, color, CountPcre2Matches(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount, separators.LineTerminator), includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, path, color, HasPcre2Match(bytes, regex, lineRegexp, wordRegexp, invertMatch, separators.LineTerminator), nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, path, color, !HasPcre2Match(bytes, regex, lineRegexp, wordRegexp, invertMatch, separators.LineTerminator), nullPathTerminator, separators.LineTerminator);
        }

        if (beforeContext > 0 || afterContext > 0 || passthru)
        {
            return SearchPcre2ContextLines(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, onlyMatching, replacement, patterns, beforeContext, afterContext, passthru, stopOnNonmatch, maxCount);
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
        {
            return SearchPcre2ReplacedLines(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, replacementValue, patterns, maxCount);
        }

        if (vimgrep && !invertMatch)
        {
            return SearchPcre2VimgrepLines(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, onlyMatching, maxCount);
        }

        return SearchPcre2Lines(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, onlyMatching, maxCount);
    }

    private static bool RunPcre2JsonSearch(byte[] bytes, Pcre2Regex regex, RawByteWriter output, byte[] path, CliLowArgs lowArgs, IReadOnlyList<byte[]> patterns, JsonSearchSummary summary)
    {
        var writer = new JsonFileWriter(output, path, lowArgs.Quiet, binaryOffset: GetPcre2JsonBinaryOffset(bytes, lowArgs.TextMode, lowArgs.NullData));
        bool matched = lowArgs.Multiline
            ? SearchPcre2JsonMultilineBytes(bytes, regex, writer, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.InvertMatch, lowArgs.Replacement, patterns, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch)
            : SearchPcre2JsonLines(bytes, regex, writer, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.InvertMatch, lowArgs.Replacement, patterns, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch, lowArgs.NullData ? NullByte : LineFeed);
        writer.Finish((ulong)bytes.Length, summary);
        return matched;
    }

    private static int GetPcre2JsonBinaryOffset(byte[] bytes, bool textMode, bool nullData)
    {
        return textMode || nullData ? -1 : bytes.AsSpan().IndexOf((byte)0);
    }

    private static bool SearchPcre2JsonLines(
        byte[] bytes,
        Pcre2Regex regex,
        JsonFileWriter writer,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch,
        ReadOnlyMemory<byte> lineTerminator)
    {
        if (beforeContext == 0 && afterContext == 0 && !passthru)
        {
            return SearchPcre2JsonMatchingLines(bytes, regex, writer, lineRegexp, wordRegexp, invertMatch, replacement, patterns, maxCount, lineTerminator);
        }

        List<ContextLineInfo> lines = BuildPcre2ContextLines(bytes, regex, lineRegexp, wordRegexp, invertMatch, stopOnNonmatch, lineTerminator);
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
            ReadOnlySpan<byte> outputLine = bytes.AsSpan(line.Start, line.Length);
            ReadOnlySpan<byte> matchLine = GetPcre2MatchLine(outputLine, lineTerminator);
            if (line.SelectedMatch)
            {
                matches.Clear();
                if (!invertMatch)
                {
                    CollectPcre2LineMatches(matchLine, regex, matches, lineRegexp, wordRegexp, replacement, patterns);
                }

                writer.WriteMatchLine(line.LineNumber, line.Start, outputLine, matches);
            }
            else
            {
                matches.Clear();
                if (invertMatch && line.OriginalMatch)
                {
                    CollectPcre2LineMatches(matchLine, regex, matches, lineRegexp, wordRegexp, replacement, patterns);
                }

                writer.WriteContextLine(line.LineNumber, line.Start, outputLine, matches);
            }
        }

        return matched;
    }

    private static bool SearchPcre2JsonMatchingLines(byte[] bytes, Pcre2Regex regex, JsonFileWriter writer, bool lineRegexp, bool wordRegexp, bool invertMatch, ReadOnlyMemory<byte>? replacement, IReadOnlyList<byte[]> patterns, ulong? maxCount, ReadOnlyMemory<byte> lineTerminator)
    {
        bool matched = false;
        int lineStart = 0;
        long lineNumber = 1;
        ulong matchedLines = 0;
        var matches = new List<JsonMatchSpan>();
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, lineTerminator, out ReadOnlySpan<byte> outputLine, out ReadOnlySpan<byte> matchLine, out int nextLineStart, out bool isLastLine);

            matches.Clear();
            CollectPcre2LineMatches(matchLine, regex, matches, lineRegexp, wordRegexp, replacement, patterns);
            bool selected = invertMatch ? matches.Count == 0 : matches.Count > 0;
            if (selected)
            {
                if (invertMatch)
                {
                    matches.Clear();
                }

                writer.WriteMatchLine(lineNumber, lineStart, outputLine, matches);
                matched = true;
                matchedLines++;
                if (maxCount is ulong limit && matchedLines >= limit)
                {
                    break;
                }
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
            lineNumber++;
        }

        return matched;
    }

    private static void CollectPcre2LineMatches(ReadOnlySpan<byte> line, Pcre2Regex regex, List<JsonMatchSpan> matches, bool lineRegexp, bool wordRegexp, ReadOnlyMemory<byte>? replacement, IReadOnlyList<byte[]> patterns)
    {
        int startOffset = 0;
        while (startOffset <= line.Length && regex.TryFind(line, startOffset, out Pcre2Match match))
        {
            if (Pcre2MatchSatisfies(line, match, lineRegexp, wordRegexp))
            {
                byte[]? expandedReplacement = replacement is ReadOnlyMemory<byte> replacementValue
                    ? ReplacementFormatter.Expand(replacementValue.Span, line.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive: false)
                    : null;
                matches.Add(new JsonMatchSpan(match.Start, match.Start + match.Length, expandedReplacement));
            }

            startOffset = match.Length == 0 ? match.Start + 1 : match.Start + match.Length;
        }
    }

    private static bool SearchPcre2JsonMultilineBytes(
        byte[] bytes,
        Pcre2Regex regex,
        JsonFileWriter writer,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch)
    {
        if (invertMatch)
        {
            return beforeContext > 0 || afterContext > 0 || passthru
                ? SearchPcre2JsonMultilineInvertedContextBytes(bytes, regex, writer, lineRegexp, wordRegexp, maxCount, beforeContext, afterContext, passthru)
                : SearchPcre2JsonMultilineInvertedBytes(bytes, regex, writer, lineRegexp, wordRegexp, maxCount);
        }

        if (beforeContext > 0 || afterContext > 0 || passthru)
        {
            return SearchPcre2JsonMultilineContextBytes(bytes, regex, writer, lineRegexp, wordRegexp, replacement, patterns, maxCount, beforeContext, afterContext, passthru, stopOnNonmatch);
        }

        bool matched = false;
        int offset = 0;
        ulong emitted = 0;
        var matches = new List<JsonMatchSpan>(capacity: 1);
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            matched = true;
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
            int lineEnd = GetLineEnd(bytes, lastLineStart);
            matches.Clear();
            byte[]? expandedReplacement = replacement is ReadOnlyMemory<byte> replacementValue
                ? ReplacementFormatter.Expand(replacementValue.Span, bytes.AsSpan(match.Start, match.Length), patterns, asciiCaseInsensitive: false)
                : null;
            matches.Add(new JsonMatchSpan(match.Start - firstLineStart, match.Start - firstLineStart + match.Length, expandedReplacement));
            writer.WriteMatchLine(
                GetLineNumber(bytes, firstLineStart),
                firstLineStart,
                bytes.AsSpan(firstLineStart, lineEnd - firstLineStart),
                matches,
                (ulong)(1 + CountLineFeeds(bytes.AsSpan(firstLineStart, lastLineStart - firstLineStart))));
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return matched;
            }
        }

        return matched;
    }

    private static bool SearchPcre2JsonMultilineInvertedBytes(
        byte[] bytes,
        Pcre2Regex regex,
        JsonFileWriter writer,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount)
    {
        List<ContextLineInfo> lines = BuildPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp);
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
            writer.WriteMatchLine(line.LineNumber, line.Start, bytes.AsSpan(line.Start, line.Length), matches);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }
        }

        return matched;
    }

    private static bool SearchPcre2JsonMultilineContextBytes(
        byte[] bytes,
        Pcre2Regex regex,
        JsonFileWriter writer,
        bool lineRegexp,
        bool wordRegexp,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch)
    {
        List<Pcre2Match> pcre2Matches = CollectPcre2MultilineMatches(bytes, regex, lineRegexp, wordRegexp);
        List<ContextLineInfo> lines = BuildPcre2MultilineContextLines(bytes, pcre2Matches, stopOnNonmatch);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : IncludePcre2MultilineContextLines(bytes, lines, pcre2Matches, included, beforeContext, afterContext, maxCount);
        ulong? renderedMatchLimit = passthru ? maxCount : null;
        var contextMatches = new List<JsonMatchSpan>(capacity: 0);
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            ContextLineInfo line = lines[index];
            if (line.SelectedMatch && Pcre2MultilineLineHasRenderedMatch(bytes, line, pcre2Matches, renderedMatchLimit))
            {
                if (TryWritePcre2JsonMultilineMatchGroupStartingAtLine(bytes, lines, index, pcre2Matches, renderedMatchLimit, replacement, patterns, writer, out int consumedLineIndex))
                {
                    index = Math.Max(index, consumedLineIndex);
                }

                continue;
            }

            writer.WriteContextLine(line.LineNumber, line.Start, bytes.AsSpan(line.Start, line.Length), contextMatches);
        }

        return matched;
    }

    private static bool SearchPcre2JsonMultilineInvertedContextBytes(
        byte[] bytes,
        Pcre2Regex regex,
        JsonFileWriter writer,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        ulong beforeContext,
        ulong afterContext,
        bool passthru)
    {
        List<ContextLineInfo> lines = BuildPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp);
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
                writer.WriteMatchLine(line.LineNumber, line.Start, bytes.AsSpan(line.Start, line.Length), matches);
            }
            else
            {
                writer.WriteContextLine(line.LineNumber, line.Start, bytes.AsSpan(line.Start, line.Length), matches);
            }
        }

        return matched;
    }

    private static bool TryWritePcre2JsonMultilineMatchGroupStartingAtLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<Pcre2Match> pcre2Matches,
        ulong? renderedMatchLimit,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        JsonFileWriter writer,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        int groupStart = -1;
        int groupEnd = -1;
        int groupLastLineStart = -1;
        var matches = new List<JsonMatchSpan>(capacity: 1);
        for (int index = 0; index < pcre2Matches.Count; index++)
        {
            if (!IsPcre2MultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            Pcre2Match match = pcre2Matches[index];
            int firstLineStart = GetLineStart(bytes, match.Start);
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

            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
            int lineEnd = GetLineEnd(bytes, lastLineStart);
            if (lineEnd > groupEnd)
            {
                groupEnd = lineEnd;
                groupLastLineStart = lastLineStart;
            }

            byte[]? expandedReplacement = replacement is ReadOnlyMemory<byte> replacementValue
                ? ReplacementFormatter.Expand(replacementValue.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive: false)
                : null;
            matches.Add(new JsonMatchSpan(match.Start - groupStart, match.Start - groupStart + match.Length, expandedReplacement));
        }

        if (groupStart < 0)
        {
            return false;
        }

        writer.WriteMatchLine(
            GetLineNumber(bytes, groupStart),
            groupStart,
            bytes[groupStart..groupEnd],
            matches,
            (ulong)(1 + CountLineFeeds(bytes[groupStart..groupLastLineStart])));
        consumedLineIndex = Math.Max(consumedLineIndex, GetPcre2MultilineLineIndex(lines, groupLastLineStart));
        return true;
    }

    private static bool RunPcre2MultilineSearchMode(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath path,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch,
        ulong? maxCount)
    {
        if (searchMode == CliSearchMode.Count)
        {
            long count = invertMatch
                ? CountPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : onlyMatching
                ? CountPcre2MultilineMatches(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : CountPcre2MultilineMatchingLines(bytes, regex, lineRegexp, wordRegexp, maxCount);
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            long count = invertMatch
                ? CountPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : CountPcre2MultilineMatches(bytes, regex, lineRegexp, wordRegexp, maxCount);
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            bool hasMatch = invertMatch
                ? HasPcre2MultilineInvertedMatch(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : HasPcre2MultilineMatch(bytes, regex, lineRegexp, wordRegexp);
            return SearchOutputFormatting.WritePathIf(output, path, color, hasMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            bool hasMatch = invertMatch
                ? HasPcre2MultilineInvertedMatch(bytes, regex, lineRegexp, wordRegexp, maxCount)
                : HasPcre2MultilineMatch(bytes, regex, lineRegexp, wordRegexp);
            return SearchOutputFormatting.WritePathIf(output, path, color, !hasMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (beforeContext > 0 || afterContext > 0 || passthru)
        {
            return SearchPcre2MultilineContextBytes(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, onlyMatching, replacement, patterns, beforeContext, afterContext, passthru, stopOnNonmatch, maxCount);
        }

        if (invertMatch)
        {
            return SearchPcre2MultilineInvertedBytes(bytes, output, separators, prefix, lineLimit, color, searchMode, regex, lineRegexp, wordRegexp, lineNumber, column, byteOffset, maxCount, trim, includeZero, nullPathTerminator);
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                return SearchPcre2MultilineOnlyMatchingReplacements(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, maxCount);
            }

            return SearchPcre2MultilineReplacedBytes(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, maxCount);
        }

        if (onlyMatching)
        {
            return SearchPcre2MultilineOnlyMatches(bytes, regex, output, separators, prefix, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, maxCount);
        }

        if (vimgrep)
        {
            return SearchPcre2MultilineVimgrepMatches(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, maxCount);
        }

        return SearchPcre2MultilineBytes(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, maxCount);
    }

    private static bool SearchPcre2MultilineInvertedBytes(
        byte[] bytes,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        Pcre2Regex regex,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        ulong? maxCount,
        bool trim,
        bool includeZero,
        bool nullPathTerminator)
    {
        List<ContextLineInfo> lines = BuildPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp);
        long count = CountSelectedPcre2MultilineLines(lines, maxCount);
        bool hasSelectedMatch = count > 0;

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode is CliSearchMode.Count or CliSearchMode.CountMatches)
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

            sink.MatchedLine(line.LineNumber, line.Start, matchColumn: 0, bytes.AsSpan(line.Start, line.Length));
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }
        }

        return hasSelectedMatch;
    }

    private static bool SearchPcre2MultilineContextBytes(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch,
        ulong? maxCount)
    {
        if (invertMatch)
        {
            return SearchPcre2MultilineInvertedContextBytes(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, beforeContext, afterContext, passthru, maxCount);
        }

        List<Pcre2Match> matches = CollectPcre2MultilineMatches(bytes, regex, lineRegexp, wordRegexp);
        List<ContextLineInfo> lines = BuildPcre2MultilineContextLines(bytes, matches, stopOnNonmatch);
        if (lines.Count == 0)
        {
            return false;
        }

        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : IncludePcre2MultilineContextLines(bytes, lines, matches, included, beforeContext, afterContext, maxCount);
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

            bool wroteLine = WritePcre2MultilineContextOutputLine(
                bytes,
                lines,
                index,
                matches,
                renderedMatchLimit,
                output,
                separators,
                prefix,
                lineLimit,
                color,
                lineRegexp,
                wordRegexp,
                lineNumber,
                column,
                byteOffset,
                trim,
                nullPathTerminator,
                vimgrep,
                onlyMatching,
                replacement,
                patterns,
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

    private static bool SearchPcre2MultilineInvertedContextBytes(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        ulong? maxCount)
    {
        List<ContextLineInfo> lines = BuildPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp);
        if (lines.Count == 0)
        {
            return false;
        }

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

            ReadOnlySpan<byte> lineBytes = bytes.AsSpan(line.Start, line.Length);
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

    private static bool WritePcre2MultilineContextOutputLine(
        byte[] bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<Pcre2Match> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ref StandardSearchSink lineSink,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        bool selectedMatch = line.SelectedMatch &&
            Pcre2MultilineLineHasRenderedMatch(bytes, line, matches, renderedMatchLimit);
        ReadOnlySpan<byte> outputLine = bytes.AsSpan(line.Start, line.Length);
        if (!selectedMatch)
        {
            lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, outputLine);
            return true;
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                return WritePcre2MultilineOnlyMatchingReplacementsForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, out consumedLineIndex);
            }

            return TryWritePcre2MultilineContextReplacementRecord(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, out consumedLineIndex);
        }

        if (onlyMatching)
        {
            return WritePcre2MultilineOnlyMatchesForContextLine(bytes, line, matches, renderedMatchLimit, output, separators, prefix, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
        }

        if (vimgrep)
        {
            return WritePcre2MultilineVimgrepMatchesForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, out consumedLineIndex);
        }

        lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, outputLine);
        return true;
    }

    private static bool SearchPcre2MultilineBytes(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        bool matched = false;
        int offset = 0;
        int lastWrittenLineStart = -1;
        ulong emitted = 0;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            matched = true;
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart > lastWrittenLineStart)
                {
                    sink.MatchedLine(
                        firstLineNumber + CountLineFeeds(bytes.AsSpan(firstLineStart, lineStart - firstLineStart)),
                        lineStart,
                        matchColumn,
                        bytes.AsSpan(lineStart, lineEnd - lineStart));
                    lastWrittenLineStart = lineStart;
                    emitted++;
                    if (maxCount is ulong limit && emitted >= limit)
                    {
                        return matched;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
            }

        }

        return matched;
    }

    private static bool SearchPcre2MultilineReplacedBytes(
        ReadOnlySpan<byte> bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        ulong? maxCount)
    {
        bool matched = false;
        int offset = 0;
        ulong emitted = 0;
        int groupStart = -1;
        int groupEnd = -1;
        List<Pcre2Match> groupMatches = [];
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            matched = true;
            GetPcre2MultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
            if (groupStart >= 0 && rangeStart >= groupEnd)
            {
                WritePcre2MultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns);
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
        }

        if (groupStart >= 0)
        {
            WritePcre2MultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns);
        }

        return matched;
    }

    private static bool SearchPcre2MultilineOnlyMatchingReplacements(
        ReadOnlySpan<byte> bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        ulong? maxCount)
    {
        bool matched = false;
        int offset = 0;
        ulong emitted = 0;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            matched = true;
            byte[] body = ReplacementFormatter.Expand(replacement.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive: false);
            int lineStart = GetLineStart(bytes, match.Start);
            WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }
        }

        return matched;
    }

    private static bool SearchPcre2MultilineOnlyMatches(
        ReadOnlySpan<byte> bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount)
    {
        var sink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
        bool matched = false;
        int offset = 0;
        ulong emitted = 0;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            matched = true;
            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (IsPcre2EofEmptyMatch(bytes, match))
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
                return matched;
            }
        }

        return matched;
    }

    private static bool SearchPcre2MultilineVimgrepMatches(
        ReadOnlySpan<byte> bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        bool matched = false;
        int offset = 0;
        ulong emitted = 0;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            matched = true;
            int lineStart = GetLineStart(bytes, match.Start);
            int lineEnd = GetLineEnd(bytes, lineStart);
            sink.MatchedLine(GetLineNumber(bytes, lineStart), lineStart, match.Start - lineStart + 1L, bytes[lineStart..lineEnd]);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return matched;
            }
        }

        return matched;
    }

    private static bool SearchPcre2Lines(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool onlyMatching,
        ulong? maxCount)
    {
        bool matched = false;
        int lineStart = 0;
        long currentLineNumber = 1;
        ulong matchedLines = 0;
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, separators.LineTerminator, out ReadOnlySpan<byte> outputLine, out ReadOnlySpan<byte> matchLine, out int nextLineStart, out bool isLastLine);

            bool originalMatch = TryFindPcre2LineMatch(matchLine, regex, lineRegexp, wordRegexp, out Pcre2Match firstMatch);
            bool selected = invertMatch ? !originalMatch : originalMatch;
            if (selected)
            {
                if (onlyMatching && !invertMatch)
                {
                    WritePcre2OnlyMatches(matchLine, regex, output, separators.FieldMatch, prefix, color, currentLineNumber, lineStart, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, separators.LineTerminator);
                }
                else
                {
                    WritePcre2MatchedLine(outputLine, matchLine, regex, output, separators, prefix, lineLimit, color, currentLineNumber, lineStart, firstMatch, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator);
                }

                matched = true;
                matchedLines++;
                if (maxCount is ulong limit && matchedLines >= limit)
                {
                    break;
                }
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
            currentLineNumber++;
        }

        return matched;
    }

    private static bool SearchPcre2VimgrepLines(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool onlyMatching,
        ulong? maxCount)
    {
        bool matched = false;
        int lineStart = 0;
        long currentLineNumber = 1;
        ulong matchedLines = 0;
        while (lineStart < bytes.Length)
        {
            if (maxCount is ulong limit && matchedLines >= limit)
            {
                break;
            }

            GetPcre2LineSlices(bytes, lineStart, separators.LineTerminator, out ReadOnlySpan<byte> outputLine, out ReadOnlySpan<byte> matchLine, out int nextLineStart, out bool isLastLine);
            if (WritePcre2VimgrepMatchesForLine(outputLine, matchLine, regex, output, separators, prefix, lineLimit, color, currentLineNumber, lineStart, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, onlyMatching))
            {
                matched = true;
                matchedLines++;
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
            currentLineNumber++;
        }

        return matched;
    }

    private static bool WritePcre2VimgrepMatchesForLine(
        ReadOnlySpan<byte> outputLine,
        ReadOnlySpan<byte> matchLine,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        long lineNumber,
        int lineStart,
        bool lineRegexp,
        bool wordRegexp,
        bool printLineNumber,
        bool printColumn,
        bool printByteOffset,
        bool trim,
        bool nullPathTerminator,
        bool onlyMatching)
    {
        bool matched = false;
        int startOffset = 0;
        while (startOffset <= matchLine.Length && regex.TryFind(matchLine, startOffset, out Pcre2Match match))
        {
            if (Pcre2MatchSatisfies(matchLine, match, lineRegexp, wordRegexp))
            {
                matched = true;
                WritePcre2VimgrepMatch(outputLine, matchLine, regex, output, separators.FieldMatch, prefix, lineLimit, color, lineNumber, lineStart, match, lineRegexp, wordRegexp, printLineNumber, printColumn, printByteOffset, trim, nullPathTerminator, onlyMatching, separators.LineTerminator);
            }

            startOffset = AdvanceAfterPcre2Match(match, matchLine.Length);
        }

        return matched;
    }

    private static void WritePcre2VimgrepMatch(
        ReadOnlySpan<byte> outputLine,
        ReadOnlySpan<byte> matchLine,
        Pcre2Regex regex,
        RawByteWriter output,
        ReadOnlyMemory<byte> fieldSeparator,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        long lineNumber,
        int lineStart,
        Pcre2Match match,
        bool lineRegexp,
        bool wordRegexp,
        bool printLineNumber,
        bool printColumn,
        bool printByteOffset,
        bool trim,
        bool nullPathTerminator,
        bool onlyMatching,
        ReadOnlyMemory<byte> lineTerminator)
    {
        ReadOnlySpan<byte> body = onlyMatching ? matchLine.Slice(match.Start, match.Length) : outputLine;
        int trimOffset = trim ? GetPcre2TrimOffset(body) : 0;
        ReadOnlySpan<byte> displayBody = body[trimOffset..];
        long matchColumn = match.Start + 1L;
        long matchByteOffset = lineStart + match.Start;
        bool linked = false;
        bool hasLineNumber = printLineNumber;
        bool hasColumn = printColumn && matchColumn > 0;
        bool hasByteOffset = printByteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, lineNumber, matchColumn);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(nullPathTerminator ? NullByte : fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, lineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasColumn)
        {
            color.WriteNumberField(output, matchColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, matchByteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(fieldSeparator.Span);
        }

        WritePcre2VimgrepBody(displayBody, matchLine, regex, output, lineLimit, color, lineRegexp, wordRegexp, onlyMatching, match.Start - trimOffset, match.Length, lineTerminator);
    }

    private static void WritePcre2VimgrepBody(
        ReadOnlySpan<byte> displayBody,
        ReadOnlySpan<byte> matchLine,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool onlyMatching,
        int matchStart,
        int matchLength,
        ReadOnlyMemory<byte> lineTerminator)
    {
        if (!onlyMatching && lineLimit.IsExceeded(displayBody))
        {
            if (lineLimit.Preview)
            {
                ulong columns = lineLimit.MaxColumns.GetValueOrDefault();
                long remainingMatches = CountPcre2LineMatchesAfterColumn(matchLine, regex, columns, lineRegexp, wordRegexp);
                WritePcre2VimgrepHighlightedBody(displayBody[..lineLimit.GetPreviewLength(displayBody)], output, color, matchStart, matchLength);
                output.Write(" [... "u8);
                OutputColor.WriteNumber(output, remainingMatches);
                output.Write(" more match"u8);
                if (remainingMatches != 1)
                {
                    output.Write("es"u8);
                }

                output.Write("]"u8);
            }
            else
            {
                output.Write("[Omitted long line with "u8);
                OutputColor.WriteNumber(output, CountPcre2LineMatches(matchLine, regex, lineRegexp, wordRegexp));
                output.Write(" matches]"u8);
            }

            output.Write(lineTerminator.Span);
            return;
        }

        if (onlyMatching)
        {
            color.WriteMatch(output, displayBody);
        }
        else
        {
            WritePcre2VimgrepHighlightedBody(displayBody, output, color, matchStart, matchLength);
        }

        if (!HasPcre2InputTerminator(displayBody, lineTerminator))
        {
            output.Write(lineTerminator.Span);
        }
    }

    private static void WritePcre2VimgrepHighlightedBody(
        ReadOnlySpan<byte> displayBody,
        RawByteWriter output,
        OutputColor color,
        int matchStart,
        int matchLength)
    {
        if (!color.Enabled || matchStart < 0 || matchStart >= displayBody.Length)
        {
            output.Write(displayBody);
            return;
        }

        int length = Math.Min(matchLength, displayBody.Length - matchStart);
        ColoredLineWriter.Write(output, displayBody, [matchStart], [length], color);
    }

    private static bool SearchPcre2ContextLines(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch,
        ulong? maxCount)
    {
        List<ContextLineInfo> lines = BuildPcre2ContextLines(bytes, regex, lineRegexp, wordRegexp, invertMatch, stopOnNonmatch, separators.LineTerminator);
        if (lines.Count == 0)
        {
            return false;
        }

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

            WritePcre2ContextOutputLine(bytes, line, selectedMatch, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, onlyMatching, replacement, patterns, ref lineSink);
            previousLineIndex = index;
            wrote = true;
        }

        return matched;
    }

    private static List<ContextLineInfo> BuildPcre2ContextLines(
        byte[] bytes,
        Pcre2Regex regex,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        bool stopOnNonmatch,
        ReadOnlyMemory<byte> lineTerminator)
    {
        var lines = new List<ContextLineInfo>();
        bool hasSelectedMatch = false;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, lineTerminator, out ReadOnlySpan<byte> outputLine, out ReadOnlySpan<byte> matchLine, out int nextLineStart, out bool isLastLine);
            bool originalMatch = TryFindPcre2LineMatch(matchLine, regex, lineRegexp, wordRegexp, out Pcre2Match firstMatch);
            bool selectedMatch = invertMatch ? !originalMatch : originalMatch;
            long originalColumn = originalMatch ? firstMatch.Start + 1L : 0;
            long matchColumn = selectedMatch && !invertMatch ? originalColumn : 0;
            lines.Add(new ContextLineInfo(lineStart, outputLine.Length, lineNumber, selectedMatch, matchColumn, originalMatch, originalColumn));
            if (stopOnNonmatch && hasSelectedMatch && !selectedMatch)
            {
                break;
            }

            hasSelectedMatch |= selectedMatch;
            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
            lineNumber++;
        }

        return lines;
    }

    private static void WritePcre2ContextOutputLine(
        byte[] bytes,
        ContextLineInfo line,
        bool selectedMatch,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ref StandardSearchSink lineSink)
    {
        ReadOnlySpan<byte> outputLine = bytes.AsSpan(line.Start, line.Length);
        ReadOnlySpan<byte> matchLine = GetPcre2MatchLine(outputLine, separators.LineTerminator);
        if (selectedMatch)
        {
            if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
            {
                WritePcre2ReplacedContextLine(outputLine, matchLine, regex, output, separators, prefix, lineLimit, color, line.LineNumber, line.Start, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, replacementValue, patterns);
                return;
            }

            if (vimgrep && !invertMatch)
            {
                WritePcre2VimgrepMatchesForLine(outputLine, matchLine, regex, output, separators, prefix, lineLimit, color, line.LineNumber, line.Start, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, onlyMatching);
                return;
            }

            if (onlyMatching && !invertMatch)
            {
                WritePcre2OnlyMatches(matchLine, regex, output, separators.FieldMatch, prefix, color, line.LineNumber, line.Start, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, separators.LineTerminator);
                return;
            }

            if (TryFindPcre2LineMatch(matchLine, regex, lineRegexp, wordRegexp, out Pcre2Match firstMatch) || invertMatch)
            {
                WritePcre2MatchedLine(outputLine, matchLine, regex, output, separators, prefix, lineLimit, color, line.LineNumber, line.Start, firstMatch, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator);
            }

            return;
        }

        if (onlyMatching && invertMatch && line.OriginalMatch)
        {
            WritePcre2OnlyMatches(matchLine, regex, output, separators.FieldContext, prefix, color, line.LineNumber, line.Start, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, separators.LineTerminator);
            return;
        }

        lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, outputLine);
    }

    private static void WritePcre2ReplacedContextLine(
        ReadOnlySpan<byte> outputLine,
        ReadOnlySpan<byte> matchLine,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        long lineNumber,
        int lineStart,
        bool lineRegexp,
        bool wordRegexp,
        bool printLineNumber,
        bool printColumn,
        bool printByteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns)
    {
        var sink = new ReplacementLineSink(output, prefix, separators.FieldMatch, replacement, patterns, false, printLineNumber, printColumn, printByteOffset, trim, nullPathTerminator, vimgrep, lineLimit, color: color, lineTerminator: separators.LineTerminator);
        int startOffset = 0;
        while (startOffset <= matchLine.Length && regex.TryFind(matchLine, startOffset, out Pcre2Match match))
        {
            if (Pcre2MatchSatisfies(matchLine, match, lineRegexp, wordRegexp))
            {
                sink.MatchedLine(
                    lineNumber,
                    lineStart,
                    lineStart + match.Start,
                    match.Start + 1L,
                    outputLine,
                    matchLine.Slice(match.Start, match.Length));
            }

            startOffset = AdvanceAfterPcre2Match(match, matchLine.Length);
        }

        sink.Flush();
    }

    private static void GetPcre2LineSlices(
        byte[] bytes,
        int lineStart,
        ReadOnlyMemory<byte> lineTerminator,
        out ReadOnlySpan<byte> outputLine,
        out ReadOnlySpan<byte> matchLine,
        out int nextLineStart,
        out bool isLastLine)
    {
        ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
        byte terminator = GetPcre2LineTerminatorByte(lineTerminator);
        int terminatorIndex = remaining.IndexOf(terminator);
        int lineEnd = terminatorIndex < 0 ? bytes.Length : lineStart + terminatorIndex;
        int outputEnd = terminatorIndex < 0 ? lineEnd : lineEnd + 1;
        outputLine = bytes.AsSpan(lineStart, outputEnd - lineStart);
        matchLine = GetPcre2MatchLine(outputLine, lineTerminator);
        nextLineStart = outputEnd;
        isLastLine = terminatorIndex < 0;
    }

    private static ReadOnlySpan<byte> GetPcre2MatchLine(ReadOnlySpan<byte> outputLine, ReadOnlyMemory<byte> lineTerminator)
    {
        ReadOnlySpan<byte> matchLine = outputLine;
        byte terminator = GetPcre2LineTerminatorByte(lineTerminator);
        if (!matchLine.IsEmpty && matchLine[^1] == terminator)
        {
            matchLine = matchLine[..^1];
        }

        if (terminator == (byte)'\n' && !matchLine.IsEmpty && matchLine[^1] == (byte)'\r')
        {
            matchLine = matchLine[..^1];
        }

        return matchLine;
    }

    private static byte GetPcre2LineTerminatorByte(ReadOnlyMemory<byte> lineTerminator)
    {
        return IsPcre2NullLineTerminator(lineTerminator) ? (byte)0 : (byte)'\n';
    }

    private static void GetPcre2MultilineReplacementRange(ReadOnlySpan<byte> bytes, Pcre2Match match, out int start, out int end)
    {
        start = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
        end = GetLineEnd(bytes, lastLineStart);
    }

    private static void WritePcre2MultilineReplacementRecord(
        ReadOnlySpan<byte> bytes,
        int recordStart,
        int recordEnd,
        List<Pcre2Match> matches,
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
        IReadOnlyList<byte[]> patterns)
    {
        List<int> starts = [];
        List<int> lengths = [];
        for (int index = 0; index < matches.Count; index++)
        {
            starts.Add(matches[index].Start - recordStart);
            lengths.Add(matches[index].Length);
        }

        List<long> replacementColumns = [];
        byte[] body = ReplacementFormatter.ReplaceLine(bytes[recordStart..recordEnd], starts, lengths, replacement.Span, patterns, false, replacementColumns);
        int lineStart = GetLineStart(bytes, recordStart);
        WriteMultilineReplacementBody(body, recordStart, GetLineNumber(bytes, lineStart), replacementColumns.Count > 0 ? replacementColumns[0] : 1, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
    }

    private static bool SearchPcre2ReplacedLines(
        byte[] bytes,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        ulong? maxCount)
    {
        bool matched = false;
        int lineStart = 0;
        long currentLineNumber = 1;
        ulong matchedLines = 0;
        var sink = new ReplacementLineSink(output, prefix, separators.FieldMatch, replacement, patterns, false, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep, lineLimit, color: color, lineTerminator: separators.LineTerminator);
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, separators.LineTerminator, out ReadOnlySpan<byte> outputLine, out ReadOnlySpan<byte> matchLine, out int nextLineStart, out bool isLastLine);

            int startOffset = 0;
            bool lineMatched = false;
            while (startOffset <= matchLine.Length && regex.TryFind(matchLine, startOffset, out Pcre2Match match))
            {
                if (Pcre2MatchSatisfies(matchLine, match, lineRegexp, wordRegexp))
                {
                    lineMatched = true;
                    sink.MatchedLine(
                        currentLineNumber,
                        lineStart,
                        lineStart + match.Start,
                        match.Start + 1L,
                        outputLine,
                        matchLine.Slice(match.Start, match.Length));
                }

                startOffset = match.Length == 0 ? match.Start + 1 : match.Start + match.Length;
            }

            if (lineMatched)
            {
                matched = true;
                matchedLines++;
                if (maxCount is ulong limit && matchedLines >= limit)
                {
                    break;
                }
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
            currentLineNumber++;
        }

        sink.Flush();
        return matched;
    }

    private static void WritePcre2MatchedLine(
        ReadOnlySpan<byte> outputLine,
        ReadOnlySpan<byte> matchLine,
        Pcre2Regex regex,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        long lineNumber,
        int lineStart,
        Pcre2Match firstMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool invertMatch,
        bool printLineNumber,
        bool printColumn,
        bool printByteOffset,
        bool trim,
        bool nullPathTerminator)
    {
        if (invertMatch)
        {
            var invertedLineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, printLineNumber, printColumn, printByteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
            invertedLineSink.MatchedLine(lineNumber, lineStart, matchColumn: 0, outputLine);
            return;
        }

        if (color.Enabled)
        {
            var sink = new ColoredSearchSink(output, prefix, separators.FieldMatch, printLineNumber, printColumn, printByteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
            WritePcre2ColoredMatches(matchLine, regex, ref sink, lineNumber, lineStart, outputLine, lineRegexp, wordRegexp);
            sink.Flush();
            return;
        }

        var lineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, printLineNumber, printColumn, printByteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        lineSink.MatchedLine(lineNumber, lineStart, firstMatch.Start + 1L, outputLine);
    }

    private static void WritePcre2ColoredMatches(
        ReadOnlySpan<byte> matchLine,
        Pcre2Regex regex,
        ref ColoredSearchSink sink,
        long lineNumber,
        int lineStart,
        ReadOnlySpan<byte> outputLine,
        bool lineRegexp,
        bool wordRegexp)
    {
        int startOffset = 0;
        while (startOffset <= matchLine.Length && regex.TryFind(matchLine, startOffset, out Pcre2Match match))
        {
            if (Pcre2MatchSatisfies(matchLine, match, lineRegexp, wordRegexp))
            {
                sink.MatchedLine(
                    lineNumber,
                    lineStart,
                    lineStart + match.Start,
                    match.Start + 1L,
                    outputLine,
                    matchLine.Slice(match.Start, match.Length));
            }

            startOffset = match.Length == 0 ? match.Start + 1 : match.Start + match.Length;
        }
    }

    private static bool HasPcre2Match(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, bool invertMatch, ReadOnlyMemory<byte> lineTerminator)
    {
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, lineTerminator, out _, out ReadOnlySpan<byte> line, out int nextLineStart, out bool isLastLine);

            bool originalMatch = TryFindPcre2LineMatch(line, regex, lineRegexp, wordRegexp, out _);
            if (invertMatch ? !originalMatch : originalMatch)
            {
                return true;
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
        }

        return false;
    }

    private static long CountPcre2MatchingLines(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, bool invertMatch, ulong? maxCount, ReadOnlyMemory<byte> lineTerminator)
    {
        long count = 0;
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, lineTerminator, out _, out ReadOnlySpan<byte> line, out int nextLineStart, out bool isLastLine);

            bool originalMatch = TryFindPcre2LineMatch(line, regex, lineRegexp, wordRegexp, out _);
            if (invertMatch ? !originalMatch : originalMatch)
            {
                count++;
                if (maxCount is ulong limit && (ulong)count >= limit)
                {
                    break;
                }
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
        }

        return count;
    }

    private static long CountPcre2Matches(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, bool invertMatch, ulong? maxCount, ReadOnlyMemory<byte> lineTerminator)
    {
        long count = 0;
        int lineStart = 0;
        ulong matchedLines = 0;
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, lineTerminator, out _, out ReadOnlySpan<byte> line, out int nextLineStart, out bool isLastLine);

            int lineMatches = invertMatch
                ? (TryFindPcre2LineMatch(line, regex, lineRegexp, wordRegexp, out _) ? 0 : 1)
                : CountPcre2LineMatches(line, regex, lineRegexp, wordRegexp);
            if (lineMatches > 0)
            {
                count += lineMatches;
                matchedLines++;
                if (maxCount is ulong limit && matchedLines >= limit)
                {
                    break;
                }
            }

            if (isLastLine)
            {
                break;
            }

            lineStart = nextLineStart;
        }

        return count;
    }

    private static int CountPcre2LineMatches(ReadOnlySpan<byte> line, Pcre2Regex regex, bool lineRegexp, bool wordRegexp)
    {
        int count = 0;
        int startOffset = 0;
        while (startOffset <= line.Length && regex.TryFind(line, startOffset, out Pcre2Match match))
        {
            if (Pcre2MatchSatisfies(line, match, lineRegexp, wordRegexp))
            {
                count++;
            }

            startOffset = match.Length == 0 ? match.Start + 1 : match.Start + match.Length;
        }

        return count;
    }

    private static long CountPcre2LineMatchesAfterColumn(ReadOnlySpan<byte> line, Pcre2Regex regex, ulong column, bool lineRegexp, bool wordRegexp)
    {
        long count = 0;
        int startOffset = 0;
        while (startOffset <= line.Length && regex.TryFind(line, startOffset, out Pcre2Match match))
        {
            if ((ulong)match.Start >= column && Pcre2MatchSatisfies(line, match, lineRegexp, wordRegexp))
            {
                count++;
            }

            startOffset = AdvanceAfterPcre2Match(match, line.Length);
        }

        return count;
    }

    private static List<Pcre2Match> CollectPcre2MultilineMatches(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp)
    {
        List<Pcre2Match> matches = [];
        int offset = 0;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            matches.Add(match);
        }

        return matches;
    }

    private static bool IncludePcre2MultilineContextLines(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        List<Pcre2Match> matches,
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
            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(matches[index]));
            int firstLineIndex = GetPcre2MultilineLineIndex(lines, firstLineStart);
            int lastLineIndex = GetPcre2MultilineLineIndex(lines, lastLineStart);
            if (firstLineIndex < 0 || lastLineIndex < 0)
            {
                continue;
            }

            matched = true;
            primaryMatches++;
            IncludePcre2MultilineContextRange(included, firstLineIndex, lastLineIndex, beforeContext, afterContext);
        }

        return matched;
    }

    private static void IncludePcre2MultilineContextRange(bool[] included, int firstLineIndex, int lastLineIndex, ulong beforeContext, ulong afterContext)
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

    private static List<ContextLineInfo> BuildPcre2MultilineContextLines(byte[] bytes, List<Pcre2Match> matches, bool stopOnNonmatch)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes.AsSpan(lineStart), nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        long[] matchColumns = new long[physicalLines.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            MarkPcre2MultilineContextMatch(bytes, physicalLines, matchedLines, matchColumns, matches[index]);
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

    private static void MarkPcre2MultilineContextMatch(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, long[] matchColumns, Pcre2Match match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
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

    private static int GetPcre2MultilineLineIndex(List<ContextLineInfo> lines, int lineStart)
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

    private static bool Pcre2MultilineLineHasRenderedMatch(ReadOnlySpan<byte> bytes, ContextLineInfo line, List<Pcre2Match> matches, ulong? renderedMatchLimit)
    {
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsPcre2MultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            if (Pcre2MultilineMatchTouchesLine(bytes, matches[index], line))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Pcre2MultilineMatchTouchesLine(ReadOnlySpan<byte> bytes, Pcre2Match match, ContextLineInfo line)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
        return line.Start >= firstLineStart && line.Start <= lastLineStart;
    }

    private static bool IsPcre2MultilineContextMatchRendered(int matchIndex, ulong? renderedMatchLimit)
    {
        return renderedMatchLimit is not ulong limit || (ulong)matchIndex < limit;
    }

    private static bool WritePcre2MultilineOnlyMatchesForContextLine(
        ReadOnlySpan<byte> bytes,
        ContextLineInfo line,
        List<Pcre2Match> matches,
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
            if (!IsPcre2MultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            Pcre2Match match = matches[index];
            if (!Pcre2MultilineMatchTouchesLine(bytes, match, line))
            {
                continue;
            }

            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (IsPcre2EofEmptyMatch(bytes, match))
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

    private static bool WritePcre2MultilineOnlyMatchingReplacementsForContextLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<Pcre2Match> matches,
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
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        bool wrote = false;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsPcre2MultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            Pcre2Match match = matches[index];
            if (GetLineStart(bytes, match.Start) != line.Start)
            {
                continue;
            }

            byte[] body = ReplacementFormatter.Expand(replacement.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive: false);
            int lineStart = GetLineStart(bytes, match.Start);
            WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
            consumedLineIndex = Math.Max(consumedLineIndex, GetPcre2MultilineLineIndex(lines, lastLineStart));
            wrote = true;
        }

        return wrote;
    }

    private static bool TryWritePcre2MultilineContextReplacementRecord(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<Pcre2Match> matches,
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
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        int groupStart = -1;
        int groupEnd = -1;
        List<Pcre2Match> groupMatches = [];
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsPcre2MultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            Pcre2Match match = matches[index];
            GetPcre2MultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
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

        WritePcre2MultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns);
        int lastLineStart = GetLineStart(bytes, groupEnd > groupStart ? groupEnd - 1 : groupEnd);
        consumedLineIndex = Math.Max(consumedLineIndex, GetPcre2MultilineLineIndex(lines, lastLineStart));
        return true;
    }

    private static bool WritePcre2MultilineVimgrepMatchesForContextLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<Pcre2Match> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineRegexp,
        bool wordRegexp,
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
            if (!IsPcre2MultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            Pcre2Match match = matches[index];
            if (GetLineStart(bytes, match.Start) != line.Start)
            {
                continue;
            }

            int lineEnd = GetLineEnd(bytes, line.Start);
            sink.MatchedLine(line.LineNumber, line.Start, match.Start - line.Start + 1L, bytes[line.Start..lineEnd]);
            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
            consumedLineIndex = Math.Max(consumedLineIndex, GetPcre2MultilineLineIndex(lines, lastLineStart));
            wrote = true;
        }

        return wrote;
    }

    private static long CountPcre2MultilineMatchingLines(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, ulong? maxCount)
    {
        long count = 0;
        int offset = 0;
        int lastCountedLineStart = -1;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart > lastCountedLineStart)
                {
                    count++;
                    lastCountedLineStart = lineStart;
                    if (maxCount is ulong limit && (ulong)count >= limit)
                    {
                        return count;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
            }

        }

        return count;
    }

    private static long CountPcre2MultilineMatches(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, ulong? maxCount)
    {
        long count = 0;
        int offset = 0;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out _))
        {
            count++;
            if (maxCount is ulong limit && (ulong)count >= limit)
            {
                return count;
            }
        }

        return count;
    }

    private static bool HasPcre2MultilineMatch(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp)
    {
        int offset = 0;
        return TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out _);
    }

    private static bool HasPcre2MultilineInvertedMatch(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, ulong? maxCount)
    {
        return CountPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp, maxCount) > 0;
    }

    private static long CountPcre2MultilineInvertedLines(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, ulong? maxCount)
    {
        return CountSelectedPcre2MultilineLines(BuildPcre2MultilineInvertedLines(bytes, regex, lineRegexp, wordRegexp), maxCount);
    }

    private static List<ContextLineInfo> BuildPcre2MultilineInvertedLines(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes.AsSpan(lineStart), nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        int offset = 0;
        while (TryFindNextPcre2Match(bytes, regex, lineRegexp, wordRegexp, ref offset, out Pcre2Match match))
        {
            MarkPcre2MultilineMatchedLines(bytes, physicalLines, matchedLines, match);
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

    private static void MarkPcre2MultilineMatchedLines(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, Pcre2Match match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusivePcre2MatchEnd(match));
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

    private static long CountSelectedPcre2MultilineLines(List<ContextLineInfo> lines, ulong? maxCount)
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

    private static bool TryFindPcre2LineMatch(ReadOnlySpan<byte> line, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, out Pcre2Match match)
    {
        int startOffset = 0;
        while (startOffset <= line.Length && regex.TryFind(line, startOffset, out match))
        {
            if (Pcre2MatchSatisfies(line, match, lineRegexp, wordRegexp))
            {
                return true;
            }

            startOffset = AdvanceAfterPcre2Match(match, line.Length);
        }

        match = default;
        return false;
    }

    private static bool TryFindNextPcre2Match(ReadOnlySpan<byte> bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, ref int offset, out Pcre2Match match)
    {
        while (offset <= bytes.Length && regex.TryFind(bytes, offset, out match))
        {
            offset = AdvanceAfterPcre2Match(match, bytes.Length);
            if (Pcre2MatchSatisfies(bytes, match, lineRegexp, wordRegexp))
            {
                return true;
            }
        }

        match = default;
        return false;
    }

    private static bool Pcre2MatchSatisfies(ReadOnlySpan<byte> bytes, Pcre2Match match, bool lineRegexp, bool wordRegexp)
    {
        int end = match.Start + match.Length;
        return (!lineRegexp || IsLineMatch(bytes, match.Start, end)) &&
            (!wordRegexp || IsWordMatch(bytes, match.Start, end));
    }

    private static int GetInclusivePcre2MatchEnd(Pcre2Match match)
    {
        return match.Length == 0 ? match.Start : match.Start + match.Length - 1;
    }

    private static bool IsPcre2EofEmptyMatch(ReadOnlySpan<byte> bytes, Pcre2Match match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            !bytes.IsEmpty &&
            bytes[^1] != (byte)'\n';
    }

    private static int AdvanceAfterPcre2Match(Pcre2Match match, int length)
    {
        int next = match.Length == 0 ? match.Start + 1 : match.Start + match.Length;
        return Math.Min(next, length + 1);
    }

    private static void WritePcre2OnlyMatches(
        ReadOnlySpan<byte> line,
        Pcre2Regex regex,
        RawByteWriter output,
        ReadOnlyMemory<byte> fieldSeparator,
        OutputPath? prefix,
        OutputColor color,
        long lineNumber,
        int lineStart,
        bool lineRegexp,
        bool wordRegexp,
        bool printLineNumber,
        bool printColumn,
        bool printByteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator)
    {
        var sink = new StandardMatchSink(output, prefix, fieldSeparator, printLineNumber, printColumn, printByteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: lineTerminator);
        int startOffset = 0;
        while (startOffset <= line.Length && regex.TryFind(line, startOffset, out Pcre2Match match))
        {
            if (Pcre2MatchSatisfies(line, match, lineRegexp, wordRegexp))
            {
                sink.Matched(lineNumber, lineStart + match.Start, match.Start + 1L, line.Slice(match.Start, match.Length));
            }

            startOffset = AdvanceAfterPcre2Match(match, line.Length);
        }
    }

    private static bool HasPcre2InputTerminator(ReadOnlySpan<byte> bytes, ReadOnlyMemory<byte> lineTerminator)
    {
        return !bytes.IsEmpty &&
            (IsPcre2NullLineTerminator(lineTerminator)
                ? bytes[^1] == 0
                : bytes[^1] == (byte)'\n');
    }

    private static bool IsPcre2NullLineTerminator(ReadOnlyMemory<byte> lineTerminator)
    {
        return lineTerminator.Length == 1 && lineTerminator.Span[0] == 0;
    }

    private static int GetPcre2TrimOffset(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length && IsPcre2AsciiWhitespace(bytes[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsPcre2AsciiWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';
    }


    private static void SearchErrorMessage(CliLowArgs lowArgs, DiagnosticMessenger diagnostics, ScoutError error)
    {
        if (lowArgs.Messages)
        {
            diagnostics.ErrorMessage(error);
            return;
        }

        diagnostics.MarkErrored();
    }

    private static ScoutError MissingPathError(string path, bool simple = false)
    {
        string message = simple
            ? "No such file or directory (os error 2)"
            : $"IO error for operation on {path}: No such file or directory (os error 2)";
        return new ScoutError(message).WithContext($"rg: {path}");
    }

    private static OutputSeparators GetOutputSeparators(CliLowArgs lowArgs)
    {
        return new OutputSeparators(
            lowArgs.FieldMatchSeparator,
            lowArgs.FieldContextSeparator,
            lowArgs.ContextSeparator,
            lowArgs.ContextSeparatorEnabled,
            lowArgs.NullData ? NullByte : lowArgs.Crlf ? CrlfLineTerminator : LineFeed);
    }

    private static OutputLineLimit GetOutputLineLimit(CliLowArgs lowArgs)
    {
        return new OutputLineLimit(lowArgs.MaxColumns, lowArgs.MaxColumnsPreview);
    }

    private static OutputColor GetOutputColor(CliLowArgs lowArgs)
    {
        return OutputColor.Create(lowArgs.ColorMode is CliColorMode.Always or CliColorMode.Ansi, lowArgs.ColorSpecs);
    }

    private static bool ShouldUseHeading(CliLowArgs lowArgs)
    {
        return lowArgs.Heading && !lowArgs.Vimgrep && !lowArgs.Quiet && lowArgs.SearchMode == CliSearchMode.Standard;
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

    private static int GetLineStart(ReadOnlySpan<byte> bytes, int offset)
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

    private static int GetLineEnd(ReadOnlySpan<byte> bytes, int lineStart)
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

    private static int GetNextLineStart(int lineEnd, int length)
    {
        return lineEnd < length ? lineEnd : length + 1;
    }

    private static long GetLineNumber(ReadOnlySpan<byte> bytes, int lineStart)
    {
        return 1 + CountLineFeeds(bytes[..Math.Clamp(lineStart, 0, bytes.Length)]);
    }

    private static long CountLineFeeds(ReadOnlySpan<byte> bytes)
    {
        return ByteCounter.Count(bytes, (byte)'\n');
    }
}
