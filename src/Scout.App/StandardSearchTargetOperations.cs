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
    internal static bool SearchStandardInput(
        IReadOnlyList<byte[]> pattern,
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
        return StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, SearchOutputFormatting.GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput);
    }

    internal static void SearchPath(
        SearchPathArgument pathArgument,
        IReadOnlyList<byte[]> pattern,
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
            SearchRawUnixFile(pathArgument, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInput(pattern, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectory(path, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFile(path, null, pattern, lowArgs, false, !multiplePaths, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
        errored = true;
    }

    internal static bool SearchStandardInputWithStats(
        IReadOnlyList<byte[]> pattern,
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
        return StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, SearchOutputFormatting.GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, ref stats);
    }

    internal static void SearchPathWithStats(
        SearchPathArgument pathArgument,
        IReadOnlyList<byte[]> pattern,
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
            SearchRawUnixFileWithStats(pathArgument, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInputWithStats(pattern, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput, ref stats);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectoryWithStats(path, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFileWithStats(path, null, pattern, lowArgs, false, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
        errored = true;
    }

    internal static bool SearchStandardInput(
        IReadOnlyList<byte[]> pattern,
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
        return StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput);
    }

    internal static bool SearchStandardInputWithStats(
        IReadOnlyList<byte[]> pattern,
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
        return StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, ref stats);
    }

    private static void SearchDirectory(
        string root,
        IReadOnlyList<byte[]> pattern,
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
            SearchDirectoryParallel(root, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        SearchDirectoryDisplayPathFormatter displayPaths =
            SearchPathArgument.CreateDirectoryDisplayPathFormatter(root, fullRoot, defaultRoot, lowArgs.PathSeparator);
        bool interFileContextSeparator = StandardSearchOperations.ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        var literalPrecheckState = new DirectoryLiteralPrecheckState();
        RegexSearchPlan? regexPlan = CreateReusableDirectoryRegexSearchPlan(pattern, lowArgs, color, asciiCaseInsensitive);
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
                RegexSearchPlan? regexPlan = CreateReusableDirectoryRegexSearchPlan(pattern, lowArgs, color, asciiCaseInsensitive);
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
            SearchDirectoryParallelWithStats(root, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
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
                    ref stats);
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
                ref stats);
        }
    }

    private static void SearchDirectoryParallelWithStats(
        string root,
        IReadOnlyList<byte[]> pattern,
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
                        ref fileStats);
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

    private static RegexSearchPlan? CreateReusableDirectoryRegexSearchPlan(
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        OutputColor color,
        bool asciiCaseInsensitive)
    {
        if (!CanReuseDirectoryRegexSearchPlan(pattern, lowArgs, color))
        {
            return null;
        }

        if (lowArgs.SearchMode == CliSearchMode.Standard &&
            pattern.Count == 1 &&
            pattern[0].Length != 0 &&
            LiteralLineSearcher.IsLiteralRegex(pattern[0]))
        {
            return null;
        }

        return LiteralLineSearcher.CreateRegexSearchPlan(pattern, asciiCaseInsensitive, compileAutomata: true);
    }

    private static bool CanReuseDirectoryRegexSearchPlan(
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        OutputColor color)
    {
        if (pattern.Count == 0 ||
            lowArgs.Multiline ||
            lowArgs.Vimgrep ||
            lowArgs.OnlyMatching ||
            lowArgs.Quiet ||
            lowArgs.BeforeContext != 0 ||
            lowArgs.AfterContext != 0 ||
            lowArgs.Passthru ||
            lowArgs.StopOnNonmatch ||
            color.Enabled)
        {
            return false;
        }

        return lowArgs.SearchMode is CliSearchMode.Standard
            or CliSearchMode.Count
            or CliSearchMode.FilesWithMatches
            or CliSearchMode.FilesWithoutMatch;
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
        DirectoryLiteralPrecheckState literalPrecheckState,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        RegexSearchPlan? regexPlan = null)
    {
        if (TrySearchDirectoryEntryFileWithStreamingRegexReplacement(
            entry,
            displayPaths,
            pattern,
            lowArgs,
            output,
            logger,
            separators,
            lineLimit,
            color,
            asciiCaseInsensitive,
            lineNumber,
            heading,
            ref matched,
            regexPlan))
        {
            return;
        }

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
        RegexSearchPlan? regexPlan)
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
        if (CanUseDirectoryEntryLiteralPrecheck(entry, pattern, lowArgs))
        {
            byte[] literal = pattern[0];
            if (!TryFileContainsLiteralWithoutAllocating(entry.FullPath, literal, asciiCaseInsensitive, lowArgs.EncodingMode, out containsLiteral))
            {
                return false;
            }
        }
        else if (CanUseDirectoryEntryRegexCandidatePrecheck(entry, pattern, lowArgs, regexPlan, out RegexCandidateLineAccelerator? candidateLineAccelerator) &&
            candidateLineAccelerator is not null)
        {
            if (!TryFileContainsRegexCandidateWithoutAllocating(entry.FullPath, pattern[0], candidateLineAccelerator, lowArgs.EncodingMode, rejectBinary: false, out containsLiteral))
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
        bool searchTextMode = lowArgs.TextMode || SearchesBinaryAsText(readKind, lowArgs, lowArgs.SearchMode, bytes, pattern, asciiCaseInsensitive, invertMatch: false, lineRegexp: false, wordRegexp: false);
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
            regexPlan);
        return true;
    }

    private static bool TrySearchDirectoryEntryFileWithStreamingRegexReplacement(
        DirEntry entry,
        SearchDirectoryDisplayPathFormatter displayPaths,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        RawByteWriter output,
        DiagnosticLogger logger,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool heading,
        ref bool matched,
        RegexSearchPlan? regexPlan)
    {
        if (!CanUseStreamingRegexReplacement(
            entry,
            pattern,
            lowArgs,
            separators,
            lineLimit,
            color,
            heading,
            regexPlan,
            out RegexCandidateLineAccelerator? accelerator,
            out ReadOnlyMemory<byte> replacement))
        {
            return false;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, entry.FullPath, SearchFileReadKind.Buffered);
        byte[] displayPath = displayPaths.GetBytes(entry);
        OutputPath outputPath = SearchOutputFormatting.CreateDirectoryEntryOutputPath(entry, displayPath, lowArgs, color);
        OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, autoPrefixPath: true, lowArgs.WithFilename, outputPath);
        var capturePlan = ReplacementCapturePlan.TryCreate(pattern, asciiCaseInsensitive);
        var replacementTemplate = ReplacementTemplate.Create(replacement.Span, pattern);
        int[] captureStarts = new int[Math.Max(1, replacementTemplate.HighestCapture + 1)];
        int[] captureLengths = new int[Math.Max(1, replacementTemplate.HighestCapture + 1)];
        bool streamMatched = SearchStreamingRegexReplacementFile(
            entry.FullPath,
            pattern,
            accelerator!,
            output,
            prefix,
            separators,
            replacement,
            asciiCaseInsensitive,
            lowArgs.EncodingMode,
            lineNumber,
            capturePlan,
            replacementTemplate,
            captureStarts,
            captureLengths,
            out bool streamCompleted);
        if (!streamCompleted)
        {
            return false;
        }

        matched |= streamMatched;
        return true;
    }

    private static bool CanUseStreamingRegexReplacement(
        DirEntry entry,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool heading,
        RegexSearchPlan? regexPlan,
        out RegexCandidateLineAccelerator? accelerator,
        out ReadOnlyMemory<byte> replacement)
    {
        accelerator = null;
        replacement = default;
        if (entry.IsRawUnixPath ||
            heading ||
            color.Enabled ||
            lineLimit.IsEnabled ||
            lowArgs.SearchMode != CliSearchMode.Standard ||
            lowArgs.InvertMatch ||
            lowArgs.LineRegexp ||
            lowArgs.WordRegexp ||
            lowArgs.Multiline ||
            lowArgs.MultilineDotall ||
            lowArgs.Vimgrep ||
            lowArgs.OnlyMatching ||
            lowArgs.ByteOffset ||
            SearchOutputFormatting.EffectiveColumn(lowArgs) ||
            lowArgs.Quiet ||
            lowArgs.Trim ||
            lowArgs.BeforeContext != 0 ||
            lowArgs.AfterContext != 0 ||
            lowArgs.Passthru ||
            lowArgs.IncludeZero ||
            lowArgs.NullPathTerminator ||
            lowArgs.StopOnNonmatch ||
            lowArgs.SearchZip ||
            lowArgs.Preprocessor is not null ||
            lowArgs.MaxCount is not null ||
            separators.NullData ||
            lowArgs.EncodingMode is not (CliEncodingMode.Auto or CliEncodingMode.None or CliEncodingMode.Utf8) ||
            pattern.Count != 1 ||
            pattern[0].Length == 0 ||
            lowArgs.Replacement is not ReadOnlyMemory<byte> replacementValue)
        {
            return false;
        }

        accelerator = regexPlan?.GetCandidateLineAccelerator(0);
        replacement = replacementValue;
        return accelerator is { HasVerifier: true };
    }

    private static bool SearchStreamingRegexReplacementFile(
        string path,
        IReadOnlyList<byte[]> pattern,
        RegexCandidateLineAccelerator accelerator,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        ReadOnlyMemory<byte> replacement,
        bool asciiCaseInsensitive,
        CliEncodingMode encodingMode,
        bool lineNumber,
        ReplacementCapturePlan? capturePlan,
        ReplacementTemplate replacementTemplate,
        int[] captureStarts,
        int[] captureLengths,
        out bool completed)
    {
        const int ReadBufferLength = 64 * 1024;
        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferLength);
        byte[] lineBuffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
        int bufferedLineLength = 0;
        long lineStartOffset = 0;
        long lineNumberValue = 1;
        bool matched = false;
        completed = false;

        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.SequentialScan);
            long fileOffset = 0;
            bool firstRead = true;
            while (true)
            {
                int read = RandomAccess.Read(handle, readBuffer.AsSpan(0, ReadBufferLength), fileOffset);
                if (read == 0)
                {
                    break;
                }

                ReadOnlySpan<byte> chunk = readBuffer.AsSpan(0, read);
                if (firstRead)
                {
                    firstRead = false;
                    if (encodingMode == CliEncodingMode.Auto && HasNonUtf8Bom(chunk))
                    {
                        return false;
                    }
                }

                int binaryOffset = chunk.IndexOf((byte)0);
                if (binaryOffset >= 0)
                {
                    if (matched)
                    {
                        StandardSearchByteOperations.WriteBinaryFileStoppedWarning(output, prefix, default, fileOffset + binaryOffset);
                        completed = true;
                        return true;
                    }

                    return false;
                }

                int chunkOffset = 0;
                while (chunkOffset < chunk.Length)
                {
                    int terminatorOffset = chunk[chunkOffset..].IndexOf((byte)'\n');
                    int segmentLength = terminatorOffset < 0
                        ? chunk.Length - chunkOffset
                        : terminatorOffset + 1;
                    ReadOnlySpan<byte> segment = chunk.Slice(chunkOffset, segmentLength);
                    bool completeLine = terminatorOffset >= 0;

                    if (bufferedLineLength == 0 && completeLine)
                    {
                        matched |= WriteStreamingRegexReplacementLine(segment, lineNumberValue, lineStartOffset, pattern, accelerator, output, prefix, separators, replacement, asciiCaseInsensitive, lineNumber, capturePlan, replacementTemplate, captureStarts, captureLengths);
                    }
                    else
                    {
                        EnsureLineBufferCapacity(ref lineBuffer, bufferedLineLength + segment.Length);
                        segment.CopyTo(lineBuffer.AsSpan(bufferedLineLength));
                        bufferedLineLength += segment.Length;
                        if (completeLine)
                        {
                            matched |= WriteStreamingRegexReplacementLine(lineBuffer.AsSpan(0, bufferedLineLength), lineNumberValue, lineStartOffset, pattern, accelerator, output, prefix, separators, replacement, asciiCaseInsensitive, lineNumber, capturePlan, replacementTemplate, captureStarts, captureLengths);
                            bufferedLineLength = 0;
                        }
                    }

                    chunkOffset += segmentLength;
                    if (completeLine)
                    {
                        lineNumberValue++;
                        lineStartOffset = fileOffset + chunkOffset;
                    }
                }

                fileOffset += read;
            }

            if (bufferedLineLength > 0)
            {
                matched |= WriteStreamingRegexReplacementLine(lineBuffer.AsSpan(0, bufferedLineLength), lineNumberValue, lineStartOffset, pattern, accelerator, output, prefix, separators, replacement, asciiCaseInsensitive, lineNumber, capturePlan, replacementTemplate, captureStarts, captureLengths);
            }

            completed = true;
        }
        catch (IOException)
        {
            completed = matched;
            return matched;
        }
        catch (UnauthorizedAccessException)
        {
            completed = matched;
            return matched;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
            ArrayPool<byte>.Shared.Return(lineBuffer);
        }

        return matched;
    }

    private static void EnsureLineBufferCapacity(ref byte[] buffer, int requiredLength)
    {
        if (requiredLength <= buffer.Length)
        {
            return;
        }

        int newLength = buffer.Length;
        while (newLength < requiredLength)
        {
            newLength *= 2;
        }

        byte[] resized = ArrayPool<byte>.Shared.Rent(newLength);
        buffer.AsSpan().CopyTo(resized);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = resized;
    }

    private static bool WriteStreamingRegexReplacementLine(
        ReadOnlySpan<byte> line,
        long lineNumber,
        long lineByteOffset,
        IReadOnlyList<byte[]> pattern,
        RegexCandidateLineAccelerator accelerator,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        ReadOnlyMemory<byte> replacement,
        bool asciiCaseInsensitive,
        bool lineNumberEnabled,
        ReplacementCapturePlan? capturePlan,
        ReplacementTemplate replacementTemplate,
        int[] captureStarts,
        int[] captureLengths)
    {
        int searchOffset = 0;
        int writtenUntil = 0;
        bool matched = false;
        while (searchOffset < line.Length)
        {
            int candidate = accelerator.FindCandidate(line, searchOffset);
            if (candidate < 0)
            {
                break;
            }

            if (!accelerator.TryMatchAt(line, candidate, out int matchLength, out bool completed))
            {
                if (!completed && !matched)
                {
                    return WriteFallbackStreamingReplacementLine(
                        line,
                        lineNumber,
                        lineByteOffset,
                        pattern,
                        output,
                        prefix,
                        separators,
                        replacement,
                        asciiCaseInsensitive,
                        lineNumberEnabled,
                        capturePlan);
                }

                searchOffset = candidate + 1;
                continue;
            }

            if (matchLength <= 0)
            {
                searchOffset = candidate + 1;
                continue;
            }

            if (!matched)
            {
                WriteStreamingReplacementPrefix(output, prefix, separators, lineNumberEnabled, lineNumber);
                matched = true;
            }

            if (candidate > writtenUntil)
            {
                output.Write(line.Slice(writtenUntil, candidate - writtenUntil));
            }

            ReplacementFormatter.WriteExpanded(output, replacement.Span, line.Slice(candidate, matchLength), pattern, asciiCaseInsensitive, capturePlan, replacementTemplate, captureStarts, captureLengths, captureNamesBuffer: null);
            writtenUntil = candidate + matchLength;
            searchOffset = writtenUntil;
        }

        if (!matched)
        {
            return false;
        }

        output.Write(line[writtenUntil..]);
        if (!HasInputTerminator(line))
        {
            output.Write(separators.LineTerminator.Span);
        }

        return true;
    }

    private static bool WriteFallbackStreamingReplacementLine(
        ReadOnlySpan<byte> line,
        long lineNumber,
        long lineByteOffset,
        IReadOnlyList<byte[]> pattern,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        ReadOnlyMemory<byte> replacement,
        bool asciiCaseInsensitive,
        bool lineNumberEnabled,
        ReplacementCapturePlan? capturePlan)
    {
        var sink = new ReplacementLineSink(
            output,
            prefix,
            separators.FieldMatch,
            replacement,
            pattern,
            asciiCaseInsensitive,
            lineNumberEnabled,
            column: false,
            byteOffset: false,
            trim: false,
            nullPathTerminator: false,
            vimgrep: false,
            default,
            lineNumberOffset: lineNumber - 1,
            byteOffsetOffset: lineByteOffset,
            color: default,
            separators.LineTerminator,
            capturePlan,
            streamPlainBodyDirectly: true);
        bool matched = LiteralLineSearcher.SearchMatchLines(line, pattern, ref sink, asciiCaseInsensitive);
        sink.Flush();
        return matched;
    }

    private static void WriteStreamingReplacementPrefix(
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        bool lineNumber,
        long outputLineNumber)
    {
        if (prefix is not null)
        {
            output.Write(prefix.Display);
            output.Write(separators.FieldMatch.Span);
        }

        if (lineNumber)
        {
            OutputColor.WriteNumber(output, outputLineNumber);
            output.Write(separators.FieldMatch.Span);
        }
    }

    private static bool HasInputTerminator(ReadOnlySpan<byte> line)
    {
        return !line.IsEmpty && line[^1] == (byte)'\n';
    }

    private static bool CanUseDirectoryEntryLiteralPrecheck(
        DirEntry entry,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs)
    {
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
            pattern.Count != 1)
        {
            return false;
        }

        byte[] literal = pattern[0];
        return literal.Length != 0 &&
            !literal.AsSpan().Contains((byte)'\n') &&
            !literal.AsSpan().Contains((byte)'\r') &&
            !literal.AsSpan().Contains((byte)0) &&
            LiteralLineSearcher.IsLiteralRegex(literal);
    }

    /// <summary>
    /// Determines whether a directory entry can use the bounded regex-candidate precheck.
    /// </summary>
    /// <param name="entry">The directory entry being searched.</param>
    /// <param name="pattern">The prepared search patterns.</param>
    /// <param name="lowArgs">The parsed low-level search options.</param>
    /// <param name="regexPlan">The reusable regex search plan.</param>
    /// <param name="accelerator">The candidate accelerator when the precheck is eligible.</param>
    /// <returns><see langword="true" /> when the precheck can be used.</returns>
    internal static bool CanUseDirectoryEntryRegexCandidatePrecheck(
        DirEntry entry,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        RegexSearchPlan? regexPlan,
        out RegexCandidateLineAccelerator? accelerator)
    {
        accelerator = null;
        if (entry.IsRawUnixPath ||
            lowArgs.SearchMode != CliSearchMode.Standard ||
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
            pattern.Count != 1 ||
            pattern[0].Length == 0 ||
            pattern[0].Length > DirectoryEntryLiteralPrecheckBufferLength ||
            pattern[0].AsSpan().Contains((byte)'\n') ||
            pattern[0].AsSpan().Contains((byte)'\r') ||
            pattern[0].AsSpan().Contains((byte)0))
        {
            return false;
        }

        accelerator = regexPlan?.GetCandidateLineAccelerator(0);
        return accelerator is not null;
    }

    private static bool TryFileContainsLiteralWithoutAllocating(
        string path,
        byte[] literal,
        bool asciiCaseInsensitive,
        CliEncodingMode encodingMode,
        out bool containsLiteral)
    {
        containsLiteral = false;
        if (encodingMode is not (CliEncodingMode.Auto or CliEncodingMode.None or CliEncodingMode.Utf8) ||
            literal.Length > DirectoryEntryLiteralPrecheckBufferLength / 2 ||
            (asciiCaseInsensitive && literal.AsSpan().IndexOfAnyExceptInRange((byte)0x00, (byte)0x7f) >= 0))
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

    private static bool TryFileContainsRegexCandidateWithoutAllocating(
        string path,
        byte[] pattern,
        RegexCandidateLineAccelerator accelerator,
        CliEncodingMode encodingMode,
        bool rejectBinary,
        out bool containsCandidate)
    {
        containsCandidate = false;
        if (encodingMode is not (CliEncodingMode.Auto or CliEncodingMode.None or CliEncodingMode.Utf8))
        {
            return false;
        }

        int overlapLength = Math.Max(0, pattern.Length - 1);
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

                if (rejectBinary && window.Contains((byte)0))
                {
                    return false;
                }

                if (accelerator.FindCandidate(window, 0) >= 0)
                {
                    containsCandidate = true;
                    if (!rejectBinary)
                    {
                        return true;
                    }
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
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        RegexSearchPlan? regexPlan = null)
    {
        OutputPath outputPath = SearchOutputFormatting.CreateDirectoryEntryOutputPath(entry, displayPath, lowArgs, color);
        OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, autoPrefixPath: true, lowArgs.WithFilename, outputPath);
        if (entry.IsRawUnixPath)
        {
            var path = SearchPathArgument.FromUnixBytes(entry.UnixPathBytes, displayPath);
            SearchRawUnixFile(path, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, regexPlan);
            return;
        }

        SearchFile(entry.FullPath, entry.KnownLength, pattern, lowArgs, implicitSearch: true, isOneFile: false, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, regexPlan);
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
        ref SearchStats stats)
    {
        OutputPath outputPath = SearchOutputFormatting.CreateDirectoryEntryOutputPath(entry, displayPath, lowArgs, color);
        OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, autoPrefixPath: true, lowArgs.WithFilename, outputPath);
        if (entry.IsRawUnixPath)
        {
            var path = SearchPathArgument.FromUnixBytes(entry.UnixPathBytes, displayPath);
            SearchRawUnixFileWithStats(path, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        SearchFileWithStats(entry.FullPath, entry.KnownLength, pattern, lowArgs, implicitSearch: true, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, lineNumber, SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
    }

    private static void SearchFile(
        string path,
        long? knownLength,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool implicitSearch,
        bool isOneFile,
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
        RegexSearchPlan? regexPlan = null)
    {
        if (LargeFileSearchOperations.TrySearch(
            path,
            knownLength,
            pattern,
            lowArgs,
            implicitSearch,
            isOneFile,
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
            ref errored))
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
        bool searchTextMode = textMode || SearchesBinaryAsText(readKind, lowArgs, searchMode, bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp);
        matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, searchTextMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch, searchTextMode), heading, ref wroteHeadingOutput, memoryMapped, regexPlan);
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
        RegexSearchPlan? regexPlan)
    {
        if (regexPlan is null ||
            !CanUsePooledRawFileRead(knownLength, lowArgs, autoMmapEligible))
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
        RegexSearchPlan? regexPlan = null)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path.DisplayText, readKind);
        matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, quitOnBinary: false, heading, ref wroteHeadingOutput, regexPlan: regexPlan);
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
        ref SearchStats stats)
    {
        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, logger, out byte[] bytes, out SearchFileReadKind readKind, knownLength))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path, readKind);
        bool memoryMapped = IsMemoryMapped(readKind);
        bool searchTextMode = textMode || SearchesBinaryAsText(readKind, lowArgs, searchMode, bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp);
        matched |= StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, searchTextMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch, searchTextMode), heading, ref wroteHeadingOutput, ref stats, memoryMapped);
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
        ref SearchStats stats)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path.DisplayText, readKind);
        matched |= StandardSearchByteOperations.SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, quitOnBinary: false, heading, ref wroteHeadingOutput, ref stats);
    }

    private static bool IsMemoryMapped(SearchFileReadKind readKind)
    {
        return readKind == SearchFileReadKind.MemoryMapped;
    }

    private static bool SearchesBinaryAsText(
        SearchFileReadKind readKind,
        CliLowArgs lowArgs,
        CliSearchMode searchMode,
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp)
    {
        if (!IsMemoryMapped(readKind))
        {
            return false;
        }

        if (searchMode != CliSearchMode.Standard)
        {
            return true;
        }

        if (lowArgs.MmapMode != CliMmapMode.AlwaysTryMmap)
        {
            return false;
        }

        if (lowArgs.Multiline || lowArgs.NullData)
        {
            return true;
        }

        return !HasSelectedMatchLineContainingNul(bytes, pattern, lowArgs, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp);
    }

    private static bool HasSelectedMatchLineContainingNul(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp)
    {
        List<ContextLineInfo> lines = ContextSearchOperations.BuildLines(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Crlf, lowArgs.NullData, lowArgs.StopOnNonmatch);
        for (int index = 0; index < lines.Count; index++)
        {
            ContextLineInfo line = lines[index];
            if (line.SelectedMatch && bytes.AsSpan(line.Start, line.Length).Contains((byte)0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldQuitOnBinary(CliLowArgs lowArgs, bool implicitSearch, bool searchTextMode)
    {
        return implicitSearch && !lowArgs.SearchBinaryFiles && !searchTextMode;
    }

}
