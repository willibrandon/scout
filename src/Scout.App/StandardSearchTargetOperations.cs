using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Win32.SafeHandles;

namespace Scout;

/// <summary>
/// Searches standard input, files, and directory trees with Scout's standard matcher pipeline.
/// </summary>
internal static class StandardSearchTargetOperations
{
    private const int ParallelOutputFlushThreshold = 128 * 1024;
    private const int ParallelDirectOutputFlushThreshold = 128 * 1024;
    private const int DirectoryEntryLiteralPrecheckBufferLength = 16 * 1024;
    private const int PooledRawFileReadMaxLength = 2 * 1024 * 1024;
    private const int MemoryMappedCountWindowLength = 4 * 1024 * 1024;
    private const int MemoryMappedCountMaximumPendingLineLength = 8 * 1024 * 1024;

    internal static bool SearchStandardInput(
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
        RawByteWriter output,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool autoPrefixPath,
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
        bool? withFilename,
        CliEncodingMode encodingMode,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool heading,
        ref bool wroteHeadingOutput)
    {
        byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, encodingMode);
        return StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, SearchOutputFormatting.GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, memoryMapped: false, regexPlan);
    }

    internal static void SearchPath(
        SearchPathArgument pathArgument,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
        bool defaultRoot,
        bool prefixPaths,
        bool multiplePaths,
        bool autoMmapEligible,
        CliLowArgs lowArgs,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        bool asciiCaseInsensitive,
        bool lineNumber,
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
            SearchRawUnixFile(pathArgument, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, regexPlan);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInput(pattern, regexPlan, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectory(path, pattern, regexPlan, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFile(path, null, pattern, lowArgs, implicitSearch: false, allowSegmentParallelism: true, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, regexPlan);
            return;
        }

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
        errored = true;
    }

    internal static bool SearchStandardInputWithStats(
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
        RawByteWriter output,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool autoPrefixPath,
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
        bool? withFilename,
        CliEncodingMode encodingMode,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool heading,
        ref bool wroteHeadingOutput,
        ref SearchStats stats)
    {
        byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, encodingMode);
        return StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, SearchOutputFormatting.GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, ref stats, memoryMapped: false, regexPlan);
    }

    internal static void SearchPathWithStats(
        SearchPathArgument pathArgument,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
        bool defaultRoot,
        bool prefixPaths,
        bool multiplePaths,
        bool autoMmapEligible,
        CliLowArgs lowArgs,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        ref SearchStats stats)
    {
        string? path = pathArgument.Text;
        if (pathArgument.IsRawUnixPath)
        {
            OutputPath outputPath = SearchOutputFormatting.CreateRawUnixOutputPath(pathArgument);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchRawUnixFileWithStats(pathArgument, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats, regexPlan);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInputWithStats(pattern, regexPlan, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput, ref stats);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectoryWithStats(path, pattern, regexPlan, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFileWithStats(path, null, pattern, lowArgs, false, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats, regexPlan);
            return;
        }

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
        errored = true;
    }

    internal static bool SearchStandardInput(
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
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
        CliEncodingMode encodingMode,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool heading,
        ref bool wroteHeadingOutput)
    {
        byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, encodingMode);
        return StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, memoryMapped: false, regexPlan);
    }

    internal static bool SearchStandardInputWithStats(
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        Stream standardInput,
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
        CliEncodingMode encodingMode,
        bool textMode,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        bool stopOnNonmatch,
        bool heading,
        ref bool wroteHeadingOutput,
        ref SearchStats stats)
    {
        byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, encodingMode);
        return StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, ref stats, memoryMapped: false, regexPlan);
    }

    private static void SearchDirectory(
        string root,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool defaultRoot,
        CliLowArgs lowArgs,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        int threadCount = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            SearchDirectoryParallel(root, pattern, regexPlan, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        SearchDirectoryDisplayPathFormatter displayPaths =
            SearchPathArgument.CreateDirectoryDisplayPathFormatter(root, fullRoot, defaultRoot, lowArgs.PathSeparator);
        bool interFileContextSeparator = StandardSearchOperations.ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        var literalPrecheckState = new DirectoryLiteralPrecheckState();
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics, logger))
        {
            if (interFileContextSeparator)
            {
                using MemoryStream buffer = new();
                var writer = new RawByteWriter(buffer);
                bool fileMatched = false;
                bool fileErrored = false;
                SearchDirectoryEntryFile(
                    entry,
                    displayPaths,
                    pattern,
                    lowArgs,
                    writer,
                    diagnostics,
                    logger,
                    separators,
                    lineLimit,
                    color,
                    asciiCaseInsensitive,
                    lineNumber,
                    heading,
                    allowSegmentParallelism: true,
                    literalPrecheckState,
                    ref wroteHeadingOutput,
                    ref fileMatched,
                    ref fileErrored,
                    regexPlan);
                writer.Flush();
                byte[] body = buffer.ToArray();
                if (body.Length > 0)
                {
                    StandardSearchOperations.WriteInterFileContextSeparatorIfNeeded(output, separators, ref wroteContextBody);
                    output.Write(body);
                }

                matched |= fileMatched;
                errored |= fileErrored;
                continue;
            }

            SearchDirectoryEntryFile(
                entry,
                displayPaths,
                pattern,
                lowArgs,
                output,
                diagnostics,
                logger,
                separators,
                lineLimit,
                color,
                asciiCaseInsensitive,
                lineNumber,
                heading,
                allowSegmentParallelism: true,
                literalPrecheckState,
                ref wroteHeadingOutput,
                ref matched,
                ref errored,
                regexPlan);
        }
    }

    private static void SearchDirectoryParallel(
        string root,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool defaultRoot,
        CliLowArgs lowArgs,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        int threadCount,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        string fullRoot = Path.GetFullPath(root);
        SearchDirectoryDisplayPathFormatter displayPaths =
            SearchPathArgument.CreateDirectoryDisplayPathFormatter(root, fullRoot, defaultRoot, lowArgs.PathSeparator);
        int matchedFlag = 0;
        int erroredFlag = 0;
        bool printedHeading = wroteHeadingOutput;
        bool interFileContextSeparator = StandardSearchOperations.ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool directOutput = CanWriteParallelOutputDirectly(heading, interFileContextSeparator);
        bool printedContextBody = false;
        object outputLock = new();
        var literalPrecheckState = new DirectoryLiteralPrecheckState();
        ConcurrentBag<MemoryStream>? directOutputBuffers = directOutput ? [] : null;
        using BlockingCollection<BufferedSearchOutput>? outputs = directOutput ? null : new BlockingCollection<BufferedSearchOutput>();
        using BackgroundWorkItem? printTask = directOutput ? null : BackgroundWorkItem.Queue(() =>
        {
            foreach (BufferedSearchOutput body in outputs!.GetConsumingEnumerable())
            {
                using (body)
                {
                    if (body.Length == 0)
                    {
                        continue;
                    }

                    if (heading && printedHeading)
                    {
                        output.Write("\n"u8);
                    }

                    if (interFileContextSeparator)
                    {
                        StandardSearchOperations.WriteInterFileContextSeparatorIfNeeded(output, separators, ref printedContextBody);
                    }

                    body.WriteTo(output);
                    if (heading)
                    {
                        printedHeading = true;
                    }
                }
            }
        });

        try
        {
            SearchWalkPlanning.CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger).Threads(threadCount).BuildParallel().RunWithCompletion(() =>
            {
                MemoryStream buffer = directOutput
                    ? new LineFlushingMemoryStream(output, outputLock, GetParallelOutputLineFlushTerminator(separators), ParallelDirectOutputFlushThreshold)
                    : new MemoryStream();
                RawByteWriter writer = CreateParallelOutputWriter(buffer, directOutput);
                directOutputBuffers?.Add(buffer);
                Func<DirEntry, WalkState> visitor = entry =>
                {
                    if (!entry.IsFile)
                    {
                        return WalkState.Continue;
                    }

                    if (!directOutput)
                    {
                        buffer.Position = 0;
                        buffer.SetLength(0);
                    }

                    bool fileWroteHeading = false;
                    bool fileMatched = false;
                    bool fileErrored = false;
                    SearchDirectoryEntryFile(
                        entry,
                        displayPaths,
                        pattern,
                        lowArgs,
                        writer,
                        diagnostics,
                        logger,
                        separators,
                        lineLimit,
                        color,
                        asciiCaseInsensitive,
                        lineNumber,
                        heading,
                        allowSegmentParallelism: false,
                        literalPrecheckState,
                        ref fileWroteHeading,
                        ref fileMatched,
                        ref fileErrored,
                        regexPlan);
                    if (fileMatched)
                    {
                        Interlocked.Exchange(ref matchedFlag, 1);
                    }

                    if (fileErrored)
                    {
                        Interlocked.Exchange(ref erroredFlag, 1);
                    }

                    if (directOutput)
                    {
                        FlushBufferedOutputIfThresholdExceeded(output, outputLock, buffer);
                    }
                    else
                    {
                        writer.Flush();
                        AddBufferedOutputIfAny(outputs, ref buffer, ref writer);
                    }

                    return fileMatched && lowArgs.Quiet ? WalkState.Quit : WalkState.Continue;
                };
                return (visitor, () => writer.Flush());
            });
        }
        finally
        {
            outputs?.CompleteAdding();
        }

        FlushBufferedOutputs(output, outputLock, directOutputBuffers);
        printTask?.Join();
        wroteHeadingOutput = printedHeading;
        matched |= Volatile.Read(ref matchedFlag) != 0;
        errored |= Volatile.Read(ref erroredFlag) != 0;
    }

    private static void SearchDirectoryWithStats(
        string root,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool defaultRoot,
        CliLowArgs lowArgs,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        ref SearchStats stats)
    {
        int threadCount = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            SearchDirectoryParallelWithStats(root, pattern, regexPlan, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        SearchDirectoryDisplayPathFormatter displayPaths =
            SearchPathArgument.CreateDirectoryDisplayPathFormatter(root, fullRoot, defaultRoot, lowArgs.PathSeparator);
        bool interFileContextSeparator = StandardSearchOperations.ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics, logger))
        {
            byte[] displayPathBytes = displayPaths.GetBytes(entry);
            if (interFileContextSeparator)
            {
                using MemoryStream buffer = new();
                var writer = new RawByteWriter(buffer);
                bool fileMatched = false;
                bool fileErrored = false;
                SearchDirectoryEntryFileWithStats(
                    entry,
                    displayPathBytes,
                    pattern,
                    lowArgs,
                    writer,
                    diagnostics,
                    logger,
                    separators,
                    lineLimit,
                    color,
                    asciiCaseInsensitive,
                    lineNumber,
                    heading,
                    ref wroteHeadingOutput,
                    ref fileMatched,
                    ref fileErrored,
                    ref stats,
                    regexPlan);
                writer.Flush();
                byte[] body = buffer.ToArray();
                if (body.Length > 0)
                {
                    StandardSearchOperations.WriteInterFileContextSeparatorIfNeeded(output, separators, ref wroteContextBody);
                    output.Write(body);
                }

                matched |= fileMatched;
                errored |= fileErrored;
                continue;
            }

            SearchDirectoryEntryFileWithStats(
                entry,
                displayPathBytes,
                pattern,
                lowArgs,
                output,
                diagnostics,
                logger,
                separators,
                lineLimit,
                color,
                asciiCaseInsensitive,
                lineNumber,
                heading,
                ref wroteHeadingOutput,
                ref matched,
                ref errored,
                ref stats,
                regexPlan);
        }
    }

    private static void SearchDirectoryParallelWithStats(
        string root,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        bool defaultRoot,
        CliLowArgs lowArgs,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        int threadCount,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        ref SearchStats stats)
    {
        string fullRoot = Path.GetFullPath(root);
        SearchDirectoryDisplayPathFormatter displayPaths =
            SearchPathArgument.CreateDirectoryDisplayPathFormatter(root, fullRoot, defaultRoot, lowArgs.PathSeparator);
        object statsLock = new();
        SearchStats aggregateStats = default;
        int matchedFlag = 0;
        int erroredFlag = 0;
        bool printedHeading = wroteHeadingOutput;
        bool interFileContextSeparator = StandardSearchOperations.ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool directOutput = CanWriteParallelOutputDirectly(heading, interFileContextSeparator);
        bool printedContextBody = false;
        object outputLock = new();
        ConcurrentBag<MemoryStream>? directOutputBuffers = directOutput ? [] : null;
        using BlockingCollection<BufferedSearchOutput>? outputs = directOutput ? null : new BlockingCollection<BufferedSearchOutput>();
        using BackgroundWorkItem? printTask = directOutput ? null : BackgroundWorkItem.Queue(() =>
        {
            foreach (BufferedSearchOutput body in outputs!.GetConsumingEnumerable())
            {
                using (body)
                {
                    if (body.Length == 0)
                    {
                        continue;
                    }

                    if (heading && printedHeading)
                    {
                        output.Write("\n"u8);
                    }

                    if (interFileContextSeparator)
                    {
                        StandardSearchOperations.WriteInterFileContextSeparatorIfNeeded(output, separators, ref printedContextBody);
                    }

                    body.WriteTo(output);
                    if (heading)
                    {
                        printedHeading = true;
                    }
                }
            }
        });

        try
        {
            SearchWalkPlanning.CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger).Threads(threadCount).BuildParallel().RunWithCompletion(() =>
            {
                MemoryStream buffer = directOutput
                    ? new LineFlushingMemoryStream(output, outputLock, GetParallelOutputLineFlushTerminator(separators), ParallelDirectOutputFlushThreshold)
                    : new MemoryStream();
                RawByteWriter writer = CreateParallelOutputWriter(buffer, directOutput);
                directOutputBuffers?.Add(buffer);
                Func<DirEntry, WalkState> visitor = entry =>
                {
                    if (!entry.IsFile)
                    {
                        return WalkState.Continue;
                    }

                    if (!directOutput)
                    {
                        buffer.Position = 0;
                        buffer.SetLength(0);
                    }

                    bool fileWroteHeading = false;
                    bool fileMatched = false;
                    bool fileErrored = false;
                    SearchStats fileStats = default;
                    byte[] displayPathBytes = displayPaths.GetBytes(entry);
                    SearchDirectoryEntryFileWithStats(
                        entry,
                        displayPathBytes,
                        pattern,
                        lowArgs,
                        writer,
                        diagnostics,
                        logger,
                        separators,
                        lineLimit,
                        color,
                        asciiCaseInsensitive,
                        lineNumber,
                        heading,
                        ref fileWroteHeading,
                        ref fileMatched,
                        ref fileErrored,
                        ref fileStats,
                        regexPlan);
                    if (fileMatched)
                    {
                        Interlocked.Exchange(ref matchedFlag, 1);
                    }

                    if (fileErrored)
                    {
                        Interlocked.Exchange(ref erroredFlag, 1);
                    }

                    lock (statsLock)
                    {
                        aggregateStats.Add(fileStats);
                    }

                    if (directOutput)
                    {
                        FlushBufferedOutputIfThresholdExceeded(output, outputLock, buffer);
                    }
                    else
                    {
                        writer.Flush();
                        AddBufferedOutputIfAny(outputs, ref buffer, ref writer);
                    }

                    return WalkState.Continue;
                };
                return (visitor, () => writer.Flush());
            });
        }
        finally
        {
            outputs?.CompleteAdding();
        }

        FlushBufferedOutputs(output, outputLock, directOutputBuffers);
        printTask?.Join();
        wroteHeadingOutput = printedHeading;
        stats.Add(aggregateStats);
        matched |= Volatile.Read(ref matchedFlag) != 0;
        errored |= Volatile.Read(ref erroredFlag) != 0;
    }

    private static void AddBufferedOutputIfAny(
        BlockingCollection<BufferedSearchOutput>? outputs,
        ref MemoryStream buffer,
        ref RawByteWriter writer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        outputs!.Add(new BufferedSearchOutput(buffer));
        buffer = new MemoryStream();
        writer = CreateParallelOutputWriter(buffer);
    }

    private static RawByteWriter CreateParallelOutputWriter(MemoryStream buffer, bool blockBuffered = false)
    {
        return new RawByteWriter(
            buffer,
            blockBuffered ? RawByteWriterBufferMode.Block : RawByteWriterBufferMode.None);
    }

    private static void WriteBufferedOutputIfAny(RawByteWriter output, object outputLock, MemoryStream buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        lock (outputLock)
        {
            if (buffer.TryGetBuffer(out ArraySegment<byte> segment))
            {
                output.Write(segment.AsSpan(0, checked((int)buffer.Length)));
                return;
            }

            output.Write(buffer.ToArray());
        }
    }

    private static void FlushBufferedOutputIfThresholdExceeded(RawByteWriter output, object outputLock, MemoryStream buffer)
    {
        if (buffer.Length < ParallelOutputFlushThreshold)
        {
            return;
        }

        FlushBufferedOutput(output, outputLock, buffer);
    }

    private static void FlushBufferedOutputs(
        RawByteWriter output,
        object outputLock,
        ConcurrentBag<MemoryStream>? buffers)
    {
        if (buffers is null)
        {
            return;
        }

        foreach (MemoryStream buffer in buffers)
        {
            FlushBufferedOutput(output, outputLock, buffer);
        }
    }

    private static void FlushBufferedOutput(RawByteWriter output, object outputLock, MemoryStream buffer)
    {
        WriteBufferedOutputIfAny(output, outputLock, buffer);
        buffer.Position = 0;
        buffer.SetLength(0);
        if (buffer.Capacity > ParallelOutputFlushThreshold)
        {
            buffer.Capacity = 0;
        }
    }

    private static bool CanWriteParallelOutputDirectly(bool heading, bool interFileContextSeparator)
    {
        return !heading && !interFileContextSeparator;
    }

    private static byte GetParallelOutputLineFlushTerminator(OutputSeparators separators)
    {
        return separators.NullData ? (byte)0 : (byte)'\n';
    }

    private static void SearchDirectoryEntryFile(
        DirEntry entry,
        SearchDirectoryDisplayPathFormatter displayPaths,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        bool allowSegmentParallelism,
        DirectoryLiteralPrecheckState literalPrecheckState,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        RegexSearchPlan regexPlan)
    {
        if (TrySearchDirectoryEntryFileAfterLiteralPrecheck(
            entry,
            displayPaths,
            pattern,
            lowArgs,
            output,
            diagnostics,
            logger,
            separators,
            lineLimit,
            color,
            asciiCaseInsensitive,
            lineNumber,
            heading,
            literalPrecheckState,
            ref wroteHeadingOutput,
            ref matched,
            ref errored,
            regexPlan))
        {
            return;
        }

        byte[] displayPath = displayPaths.GetBytes(entry);
        SearchDirectoryEntryFile(
            entry,
            displayPath,
            pattern,
            lowArgs,
            output,
            diagnostics,
            logger,
            separators,
            lineLimit,
            color,
            asciiCaseInsensitive,
            lineNumber,
            heading,
            allowSegmentParallelism,
            ref wroteHeadingOutput,
            ref matched,
            ref errored,
            regexPlan);
    }

    private static bool TrySearchDirectoryEntryFileAfterLiteralPrecheck(
        DirEntry entry,
        SearchDirectoryDisplayPathFormatter displayPaths,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        DirectoryLiteralPrecheckState literalPrecheckState,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        RegexSearchPlan regexPlan)
    {
        if (lowArgs.MaxCount == 0)
        {
            return true;
        }

        if (!literalPrecheckState.Enabled)
        {
            return false;
        }

        bool containsLiteral;
        if (TryGetDirectoryEntryLiteralPrecheck(
                entry,
                lowArgs,
                regexPlan,
                out ReadOnlyMemory<byte> literal))
        {
            if (!TryFileContainsLiteralWithoutAllocating(
                    entry.FullPath,
                    literal.Span,
                    asciiCaseInsensitive,
                    lowArgs.EncodingMode,
                    out containsLiteral))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        literalPrecheckState.Record(containsLiteral);
        if (!containsLiteral)
        {
            return true;
        }

        if (!SearchFileContentReader.TryRead(entry.FullPath, lowArgs, autoMmapEligible: false, diagnostics, logger, out byte[] bytes, out SearchFileReadKind readKind, entry.KnownLength))
        {
            errored = true;
            return true;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, entry.FullPath, readKind);
        byte[] displayPath = displayPaths.GetBytes(entry);
        OutputPath outputPath = SearchOutputFormatting.CreateDirectoryEntryOutputPath(entry, displayPath, lowArgs, color);
        OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, autoPrefixPath: true, lowArgs.WithFilename, outputPath);
        bool memoryMapped = IsMemoryMapped(readKind);
        bool searchTextMode = lowArgs.TextMode ||
            TreatMemoryMappedInputAsText(readKind, lowArgs, lowArgs.SearchMode);
        matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(
            bytes,
            pattern,
            output,
            prefix,
            separators,
            lineLimit,
            color,
            lowArgs.SearchMode,
            lowArgs.Vimgrep,
            lineNumber,
            SearchOutputFormatting.EffectiveColumn(lowArgs),
            lowArgs.ByteOffset,
            asciiCaseInsensitive,
            false,
            false,
            false,
            lowArgs.Multiline,
            lowArgs.MultilineDotall,
            lowArgs.OnlyMatching,
            lowArgs.Replacement,
            lowArgs.MaxCount,
            searchTextMode,
            lowArgs.Quiet,
            lowArgs.Trim,
            lowArgs.BeforeContext,
            lowArgs.AfterContext,
            lowArgs.Passthru,
            lowArgs.IncludeZero,
            lowArgs.NullPathTerminator,
            lowArgs.StopOnNonmatch,
            ShouldQuitOnBinary(lowArgs, true, searchTextMode),
            heading,
            ref wroteHeadingOutput,
            memoryMapped,
            regexPlan,
            GetBinaryDetectionScope(
                readKind,
                lowArgs,
                lowArgs.SearchMode,
                searchTextMode));
        return true;
    }

    private static bool TryGetDirectoryEntryLiteralPrecheck(
        DirEntry entry,
        CliLowArgs lowArgs,
        RegexSearchPlan regexPlan,
        out ReadOnlyMemory<byte> literal)
    {
        literal = default;
        if (entry.IsRawUnixPath)
        {
            return false;
        }

        if (lowArgs.SearchMode != CliSearchMode.Standard ||
            lowArgs.InvertMatch ||
            lowArgs.LineRegexp ||
            lowArgs.WordRegexp ||
            lowArgs.Multiline ||
            lowArgs.Vimgrep ||
            lowArgs.OnlyMatching ||
            lowArgs.BeforeContext != 0 ||
            lowArgs.AfterContext != 0 ||
            lowArgs.Passthru ||
            lowArgs.StopOnNonmatch ||
            lowArgs.SearchZip ||
            lowArgs.Preprocessor is not null ||
            regexPlan.PatternCount != 1 ||
            !regexPlan.TryGetSingleCaseSensitiveLiteral(out literal))
        {
            return false;
        }

        return !literal.IsEmpty &&
            !literal.Span.Contains((byte)'\n') &&
            !literal.Span.Contains((byte)'\r') &&
            !literal.Span.Contains((byte)0);
    }

    private static bool TryFileContainsLiteralWithoutAllocating(
        string path,
        ReadOnlySpan<byte> literal,
        bool asciiCaseInsensitive,
        CliEncodingMode encodingMode,
        out bool containsLiteral)
    {
        containsLiteral = false;
        if (encodingMode is not (CliEncodingMode.Auto or CliEncodingMode.None or CliEncodingMode.Utf8) ||
            literal.Length > DirectoryEntryLiteralPrecheckBufferLength / 2 ||
            (asciiCaseInsensitive && literal.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7f) >= 0))
        {
            return false;
        }

        int overlapLength = literal.Length - 1;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(DirectoryEntryLiteralPrecheckBufferLength + overlapLength);
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.SequentialScan);
            int preserved = 0;
            long offset = 0;
            bool firstRead = true;
            while (true)
            {
                int read = RandomAccess.Read(handle, buffer.AsSpan(preserved, DirectoryEntryLiteralPrecheckBufferLength), offset);
                if (read == 0)
                {
                    return true;
                }

                offset += read;
                ReadOnlySpan<byte> window = buffer.AsSpan(0, preserved + read);
                if (firstRead)
                {
                    firstRead = false;
                    if (encodingMode == CliEncodingMode.Auto && HasNonUtf8Bom(window))
                    {
                        return false;
                    }
                }

                if (LiteralLineSearcher.Find(window, literal, asciiCaseInsensitive) >= 0)
                {
                    containsLiteral = true;
                    return true;
                }

                preserved = Math.Min(overlapLength, window.Length);
                if (preserved > 0)
                {
                    window[^preserved..].CopyTo(buffer);
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool HasNonUtf8Bom(ReadOnlySpan<byte> bytes)
    {
        return (bytes.Length >= 2 &&
            ((bytes[0] == 0xff && bytes[1] == 0xfe) ||
            (bytes[0] == 0xfe && bytes[1] == 0xff))) ||
            (bytes.Length >= 4 &&
            ((bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0x00 && bytes[3] == 0x00) ||
            (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xfe && bytes[3] == 0xff)));
    }

    private static bool HasSearchEncodingBom(ReadOnlySpan<byte> bytes)
    {
        return (bytes.Length >= 3 &&
            bytes[0] == 0xef &&
            bytes[1] == 0xbb &&
            bytes[2] == 0xbf) ||
            HasNonUtf8Bom(bytes);
    }

    private static void SearchDirectoryEntryFile(
        DirEntry entry,
        byte[] displayPath,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        bool allowSegmentParallelism,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        RegexSearchPlan regexPlan)
    {
        OutputPath outputPath = SearchOutputFormatting.CreateDirectoryEntryOutputPath(entry, displayPath, lowArgs, color);
        OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, autoPrefixPath: true, lowArgs.WithFilename, outputPath);
        if (entry.IsRawUnixPath)
        {
            var path = SearchPathArgument.FromUnixBytes(entry.UnixPathBytes, displayPath);
            SearchRawUnixFile(path, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, regexPlan);
            return;
        }

        SearchFile(entry.FullPath, entry.KnownLength, pattern, lowArgs, implicitSearch: true, allowSegmentParallelism, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, regexPlan);
    }

    private static void SearchDirectoryEntryFileWithStats(
        DirEntry entry,
        byte[] displayPath,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        ref SearchStats stats,
        RegexSearchPlan regexPlan)
    {
        OutputPath outputPath = SearchOutputFormatting.CreateDirectoryEntryOutputPath(entry, displayPath, lowArgs, color);
        OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, autoPrefixPath: true, lowArgs.WithFilename, outputPath);
        if (entry.IsRawUnixPath)
        {
            var path = SearchPathArgument.FromUnixBytes(entry.UnixPathBytes, displayPath);
            SearchRawUnixFileWithStats(path, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats, regexPlan);
            return;
        }

        SearchFileWithStats(entry.FullPath, entry.KnownLength, pattern, lowArgs, implicitSearch: true, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats, regexPlan);
    }

    private static void SearchFile(
        string path,
        long? knownLength,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool implicitSearch,
        bool allowSegmentParallelism,
        bool autoMmapEligible,
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
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        RegexSearchPlan regexPlan)
    {
        if (LargeFileSearchOperations.TrySearch(
            path,
            knownLength,
            pattern,
            lowArgs,
            implicitSearch,
            allowSegmentParallelism,
            output,
            diagnostics,
            logger,
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
            heading,
            ref matched,
            ref errored,
            regexPlan))
        {
            return;
        }

        if (TrySearchFileMemoryMapped(
            path,
            pattern,
            lowArgs,
            output,
            logger,
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
            heading,
            ref matched,
            regexPlan))
        {
            return;
        }

        if (TrySearchFileWithPooledRawRead(
            path,
            knownLength,
            pattern,
            lowArgs,
            implicitSearch,
            autoMmapEligible,
            output,
            logger,
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
            heading,
            ref wroteHeadingOutput,
            ref matched,
            regexPlan))
        {
            return;
        }

        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, logger, out byte[] bytes, out SearchFileReadKind readKind, knownLength))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path, readKind);
        bool memoryMapped = IsMemoryMapped(readKind);
        bool searchTextMode = textMode ||
            TreatMemoryMappedInputAsText(readKind, lowArgs, searchMode);
        matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, searchTextMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch, searchTextMode), heading, ref wroteHeadingOutput, memoryMapped, regexPlan, GetBinaryDetectionScope(readKind, lowArgs, searchMode, searchTextMode));
    }

    /// <summary>
    /// Searches an explicitly mapped file directly from its read-only view when the selected
    /// output mode does not require mutable binary conversion or buffered heading output.
    /// </summary>
    private static bool TrySearchFileMemoryMapped(
        string path,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        RawByteWriter output,
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
        RegexSearchPlan regexPlan)
    {
        if (lowArgs.MmapMode != CliMmapMode.AlwaysTryMmap ||
            searchMode == CliSearchMode.Standard ||
            heading ||
            lowArgs.Preprocessor is not null ||
            lowArgs.SearchZip ||
            lowArgs.EncodingMode is not (CliEncodingMode.Auto or CliEncodingMode.None))
        {
            return false;
        }

        MemoryMappedSearchFile? mappedSearchFile = null;
        try
        {
            if (!MemoryMappedSearchFile.TryOpenFile(path, out mappedSearchFile))
            {
                return false;
            }

            if (lowArgs.EncodingMode == CliEncodingMode.Auto &&
                (!mappedSearchFile!.TryMapView(
                    offset: 0,
                    checked((int)Math.Min(mappedSearchFile.Length, 4))) ||
                HasSearchEncodingBom(mappedSearchFile.Bytes)))
            {
                return false;
            }

            bool canCountMacOsMemoryMappedWindows = OperatingSystem.IsMacOS() &&
                !textMode &&
                !separators.NullData &&
                (searchMode is CliSearchMode.Count or CliSearchMode.CountMatches) &&
                !quiet &&
                !lowArgs.StopOnNonmatch &&
                maxCount is null;
            bool canFuseMacOsMatchCount = canCountMacOsMemoryMappedWindows &&
                searchMode == CliSearchMode.CountMatches &&
                !lowArgs.Multiline &&
                !lowArgs.MultilineDotall;
            if (canCountMacOsMemoryMappedWindows &&
                mappedSearchFile!.Length > MemoryMappedCountWindowLength &&
                TryCountMemoryMappedWindows(
                    mappedSearchFile,
                    pattern,
                    regexPlan,
                    searchMode,
                    asciiCaseInsensitive,
                    invertMatch,
                    lineRegexp,
                    wordRegexp,
                    separators.Crlf,
                    lowArgs.Multiline,
                    lowArgs.MultilineDotall,
                    out long windowedCount,
                    out bool windowedContainsNul))
            {
                if (windowedContainsNul)
                {
                    return false;
                }

                SearchDiagnosticLogging.LogTraceSearchPath(
                    logger,
                    path,
                    SearchFileReadKind.MemoryMapped);
                matched |= SearchOutputFormatting.WriteCount(
                    output,
                    prefix,
                    color,
                    windowedCount,
                    includeZero,
                    nullPathTerminator,
                    separators.LineTerminator);
                return true;
            }

            if (mappedSearchFile!.Length > int.MaxValue ||
                !mappedSearchFile.TryMapView(offset: 0, checked((int)mappedSearchFile.Length)))
            {
                return false;
            }

            ReadOnlySpan<byte> bytes = mappedSearchFile!.Bytes;
            if (lowArgs.EncodingMode == CliEncodingMode.Auto && HasSearchEncodingBom(bytes))
            {
                return false;
            }

            if (OperatingSystem.IsMacOS() && !textMode && !separators.NullData)
            {
                if (canFuseMacOsMatchCount &&
                    LiteralLineSearcher.TryCountMatchesAndDetectNulWithRegexPlan(
                        bytes,
                        pattern,
                        regexPlan,
                        asciiCaseInsensitive,
                        invertMatch,
                        lineRegexp,
                        wordRegexp,
                        maxCount,
                        separators.Crlf,
                        separators.NullData,
                        out long count,
                        out bool containsNul))
                {
                    if (containsNul)
                    {
                        return false;
                    }

                    SearchDiagnosticLogging.LogTraceSearchPath(
                        logger,
                        path,
                        SearchFileReadKind.MemoryMapped);
                    matched |= SearchOutputFormatting.WriteCount(
                        output,
                        prefix,
                        color,
                        count,
                        includeZero,
                        nullPathTerminator,
                        separators.LineTerminator);
                    return true;
                }

                if (bytes.Contains((byte)0))
                {
                    return false;
                }
            }

            SearchDiagnosticLogging.LogTraceSearchPath(logger, path, SearchFileReadKind.MemoryMapped);
            bool searchTextMode = textMode || searchMode != CliSearchMode.Standard;
            matched |= StandardSearchByteOperations.SearchMemoryMappedBytes(
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
                lowArgs.Multiline,
                lowArgs.MultilineDotall,
                onlyMatching,
                replacement,
                maxCount,
                searchTextMode,
                quiet,
                trim,
                beforeContext,
                afterContext,
                passthru,
                includeZero,
                nullPathTerminator,
                lowArgs.StopOnNonmatch,
                ShouldQuitOnBinary(lowArgs, implicitSearch: false, searchTextMode),
                regexPlan);
            return true;
        }
        finally
        {
            mappedSearchFile?.Dispose();
        }
    }

    /// <summary>
    /// Counts matching records or non-overlapping matches through bounded sequential views while
    /// authoritative regex matching observes binary NUL bytes.
    /// </summary>
    /// <param name="mappedSearchFile">The file whose views are replaced as counting advances.</param>
    /// <param name="pattern">The ordered regex patterns.</param>
    /// <param name="regexPlan">The authoritative regex plan.</param>
    /// <param name="searchMode">Whether to count matching records or individual matches.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII matching is case-insensitive.</param>
    /// <param name="invertMatch">Whether non-matching records are selected.</param>
    /// <param name="lineRegexp">Whether matches must span complete records.</param>
    /// <param name="wordRegexp">Whether matches must satisfy word boundaries.</param>
    /// <param name="crlf">Whether CRLF terminates records.</param>
    /// <param name="multiline">Whether matches may span records.</param>
    /// <param name="multilineDotall">Whether dot matches record terminators in multiline mode.</param>
    /// <param name="count">Receives the complete count when bounded counting succeeds.</param>
    /// <param name="containsNul">Receives whether any bounded view contains a NUL byte.</param>
    /// <returns>
    /// <see langword="true" /> when every complete-record segment was counted without changing
    /// regex semantics; otherwise, <see langword="false" />.
    /// </returns>
    internal static bool TryCountMemoryMappedWindows(
        MemoryMappedSearchFile mappedSearchFile,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        CliSearchMode searchMode,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool multiline,
        bool multilineDotall,
        out long count,
        out bool containsNul)
    {
        count = 0;
        containsNul = false;
        if (searchMode is not (CliSearchMode.Count or CliSearchMode.CountMatches) ||
            multiline ||
            multilineDotall)
        {
            return false;
        }

        MemoryStream? pendingLine = null;
        try
        {
            long offset = 0;
            while (offset < mappedSearchFile.Length)
            {
                if (!mappedSearchFile.TryMapView(offset, MemoryMappedCountWindowLength))
                {
                    return false;
                }

                ReadOnlySpan<byte> window = mappedSearchFile.Bytes;
                int segmentStart = 0;
                if (pendingLine is not null)
                {
                    int firstLineFeed = window.IndexOf((byte)'\n');
                    if (firstLineFeed < 0)
                    {
                        if (pendingLine.Length >
                            MemoryMappedCountMaximumPendingLineLength - window.Length)
                        {
                            return false;
                        }

                        pendingLine.Write(window);
                        offset += window.Length;
                        continue;
                    }

                    int pendingLength = firstLineFeed + 1;
                    if (pendingLine.Length >
                        MemoryMappedCountMaximumPendingLineLength - pendingLength)
                    {
                        return false;
                    }

                    pendingLine.Write(window[..pendingLength]);
                    ReadOnlySpan<byte> line = pendingLine.GetBuffer().AsSpan(
                        start: 0,
                        checked((int)pendingLine.Length));
                    if (!TryCountMemoryMappedSegment(
                        line,
                        pattern,
                        regexPlan,
                        searchMode,
                        asciiCaseInsensitive,
                        invertMatch,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        ref count,
                        ref containsNul))
                    {
                        return false;
                    }

                    pendingLine.Dispose();
                    pendingLine = null;
                    segmentStart = pendingLength;
                }

                int lastLineFeed = window[segmentStart..].LastIndexOf((byte)'\n');
                if (lastLineFeed >= 0)
                {
                    int completeLength = lastLineFeed + 1;
                    if (!TryCountMemoryMappedSegment(
                        window.Slice(segmentStart, completeLength),
                        pattern,
                        regexPlan,
                        searchMode,
                        asciiCaseInsensitive,
                        invertMatch,
                        lineRegexp,
                        wordRegexp,
                        crlf,
                        ref count,
                        ref containsNul))
                    {
                        return false;
                    }

                    segmentStart += completeLength;
                }

                if (segmentStart < window.Length)
                {
                    pendingLine = new MemoryStream();
                    pendingLine.Write(window[segmentStart..]);
                }

                offset += window.Length;
            }

            if (pendingLine is not null)
            {
                ReadOnlySpan<byte> line = pendingLine.GetBuffer().AsSpan(
                    start: 0,
                    checked((int)pendingLine.Length));
                if (!TryCountMemoryMappedSegment(
                    line,
                    pattern,
                    regexPlan,
                    searchMode,
                    asciiCaseInsensitive,
                    invertMatch,
                    lineRegexp,
                    wordRegexp,
                    crlf,
                    ref count,
                    ref containsNul))
                {
                    return false;
                }

                pendingLine.Dispose();
                pendingLine = null;
            }

            return true;
        }
        finally
        {
            pendingLine?.Dispose();
        }
    }

    /// <summary>
    /// Adds one complete-record segment's authoritative count and NUL observation.
    /// </summary>
    private static bool TryCountMemoryMappedSegment(
        ReadOnlySpan<byte> segment,
        IReadOnlyList<byte[]> pattern,
        RegexSearchPlan regexPlan,
        CliSearchMode searchMode,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        ref long count,
        ref bool containsNul)
    {
        if (segment.IsEmpty)
        {
            return true;
        }

        long segmentCount;
        bool segmentContainsNul;
        bool counted;
        if (searchMode == CliSearchMode.Count)
        {
            counted = LiteralLineSearcher.TryCountMatchingLinesAndDetectNulWithRegexPlan(
                segment,
                pattern,
                regexPlan,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                crlf,
                nullData: false,
                out segmentCount,
                out segmentContainsNul);
        }
        else
        {
            counted = LiteralLineSearcher.TryCountMatchesAndDetectNulWithRegexPlan(
                segment,
                pattern,
                regexPlan,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxMatchingLines: null,
                crlf,
                nullData: false,
                out segmentCount,
                out segmentContainsNul) ||
                LiteralLineSearcher.TryCountNonEmptyMatchesAndDetectNulWithRegexPlan(
                    segment,
                    pattern,
                    regexPlan,
                    asciiCaseInsensitive,
                    invertMatch,
                    lineRegexp,
                    wordRegexp,
                    crlf,
                    nullData: false,
                    out segmentCount,
                    out segmentContainsNul);
        }

        if (!counted)
        {
            return false;
        }

        count += segmentCount;
        containsNul |= segmentContainsNul;
        return true;
    }

    private static bool TrySearchFileWithPooledRawRead(
        string path,
        long? knownLength,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool implicitSearch,
        bool autoMmapEligible,
        RawByteWriter output,
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
        ref bool wroteHeadingOutput,
        ref bool matched,
        RegexSearchPlan regexPlan)
    {
        if (!CanUsePooledRawFileRead(knownLength, lowArgs, autoMmapEligible))
        {
            return false;
        }

        if (!TryReadPooledRawFile(path, knownLength, lowArgs.EncodingMode, out byte[] rentedBytes, out int byteLength))
        {
            return false;
        }

        try
        {
            SearchDiagnosticLogging.LogTraceSearchPath(logger, path, SearchFileReadKind.Buffered);
            bool searchTextMode = textMode;
            matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(
                rentedBytes,
                byteLength,
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
                lowArgs.Multiline,
                lowArgs.MultilineDotall,
                onlyMatching,
                replacement,
                maxCount,
                searchTextMode,
                quiet,
                trim,
                beforeContext,
                afterContext,
                passthru,
                includeZero,
                nullPathTerminator,
                lowArgs.StopOnNonmatch,
                ShouldQuitOnBinary(lowArgs, implicitSearch, searchTextMode),
                heading,
                ref wroteHeadingOutput,
                memoryMapped: false,
                regexPlan);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBytes);
        }
    }

    /// <summary>
    /// Determines whether a raw file can use the pooled buffered-read path.
    /// </summary>
    /// <param name="knownLength">The file length when directory traversal already resolved it.</param>
    /// <param name="lowArgs">The parsed low-level command-line arguments.</param>
    /// <param name="autoMmapEligible">Whether automatic memory mapping remains eligible.</param>
    /// <returns><see langword="true" /> when the pooled buffered-read path can inspect the file.</returns>
    internal static bool CanUsePooledRawFileRead(
        long? knownLength,
        CliLowArgs lowArgs,
        bool autoMmapEligible)
    {
        return (knownLength is null or >= 0 and <= PooledRawFileReadMaxLength) &&
            !autoMmapEligible &&
            lowArgs.MmapMode != CliMmapMode.AlwaysTryMmap &&
            lowArgs.Preprocessor is null &&
            !lowArgs.SearchZip &&
            lowArgs.EncodingMode is CliEncodingMode.Auto or CliEncodingMode.None;
    }

    /// <summary>
    /// Reads a small raw file into a shared pooled buffer, discovering its length after opening when necessary.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="knownLength">The file length when directory traversal already resolved it.</param>
    /// <param name="encodingMode">The requested input encoding mode.</param>
    /// <param name="bytes">Receives the rented buffer. The caller must return it to <see cref="ArrayPool{T}.Shared" />.</param>
    /// <param name="byteLength">Receives the number of bytes read into <paramref name="bytes" />.</param>
    /// <returns><see langword="true" /> when the file was read into a pooled buffer.</returns>
    internal static bool TryReadPooledRawFile(
        string path,
        long? knownLength,
        CliEncodingMode encodingMode,
        out byte[] bytes,
        out int byteLength)
    {
        bytes = [];
        byteLength = 0;
        byte[]? rented = null;
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.SequentialScan);
            long fileLength = knownLength ?? RandomAccess.GetLength(handle);
            if (fileLength is < 0 or > PooledRawFileReadMaxLength)
            {
                return false;
            }

            int length = checked((int)fileLength);
            rented = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = RandomAccess.Read(handle, rented.AsSpan(totalRead, length - totalRead), totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (encodingMode == CliEncodingMode.Auto && HasSearchEncodingBom(rented.AsSpan(0, totalRead)))
            {
                return false;
            }

            bytes = rented;
            byteLength = totalRead;
            rented = null;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static void SearchRawUnixFile(
        SearchPathArgument path,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
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
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        RegexSearchPlan regexPlan)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path.DisplayText, readKind);
        matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, quitOnBinary: false, heading, ref wroteHeadingOutput, memoryMapped: false, regexPlan);
    }

    private static void SearchFileWithStats(
        string path,
        long? knownLength,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool implicitSearch,
        bool autoMmapEligible,
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
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        ref SearchStats stats,
        RegexSearchPlan regexPlan)
    {
        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, logger, out byte[] bytes, out SearchFileReadKind readKind, knownLength))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path, readKind);
        bool memoryMapped = IsMemoryMapped(readKind);
        bool searchTextMode = textMode ||
            TreatMemoryMappedInputAsText(readKind, lowArgs, searchMode);
        matched |= StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, searchTextMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch, searchTextMode), heading, ref wroteHeadingOutput, ref stats, memoryMapped, regexPlan, GetBinaryDetectionScope(readKind, lowArgs, searchMode, searchTextMode));
    }

    private static void SearchRawUnixFileWithStats(
        SearchPathArgument path,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
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
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        ref SearchStats stats,
        RegexSearchPlan regexPlan)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path.DisplayText, readKind);
        matched |= StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, quitOnBinary: false, heading, ref wroteHeadingOutput, ref stats, memoryMapped: false, regexPlan);
    }

    private static bool IsMemoryMapped(SearchFileReadKind readKind)
    {
        return readKind == SearchFileReadKind.MemoryMapped;
    }

    private static bool TreatMemoryMappedInputAsText(
        SearchFileReadKind readKind,
        CliLowArgs lowArgs,
        CliSearchMode searchMode)
    {
        if (!IsMemoryMapped(readKind))
        {
            return false;
        }

        return searchMode != CliSearchMode.Standard ||
            lowArgs.Multiline ||
            lowArgs.NullData;
    }

    private static StandardBinaryDetectionScope GetBinaryDetectionScope(
        SearchFileReadKind readKind,
        CliLowArgs lowArgs,
        CliSearchMode searchMode,
        bool searchTextMode)
    {
        return IsMemoryMapped(readKind) &&
            lowArgs.MmapMode == CliMmapMode.AlwaysTryMmap &&
            searchMode == CliSearchMode.Standard &&
            !searchTextMode &&
            !lowArgs.Multiline &&
            !lowArgs.NullData
                ? StandardBinaryDetectionScope.SelectedLines
                : StandardBinaryDetectionScope.WholeInput;
    }

    private static bool ShouldQuitOnBinary(CliLowArgs lowArgs, bool implicitSearch, bool searchTextMode)
    {
        return implicitSearch && !lowArgs.SearchBinaryFiles && !searchTextMode;
    }

}
