using System.Collections.Concurrent;

namespace Scout;

internal static class StandardSearchTargetOperations
{
    private const int ParallelOutputFlushThreshold = 128 * 1024;

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
            SearchRawUnixFile(pathArgument, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInput(pattern, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectory(path, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFile(path, null, pattern, lowArgs, false, !multiplePaths, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
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
            SearchRawUnixFileWithStats(pathArgument, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInputWithStats(pattern, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput, ref stats);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectoryWithStats(path, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFileWithStats(path, null, pattern, lowArgs, false, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
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
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored)
    {
        int threadCount = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            SearchDirectoryParallel(root, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored);
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
                SearchDirectoryEntryFile(
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
                    heading,
                    ref wroteHeadingOutput,
                    ref fileMatched,
                    ref fileErrored);
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
                heading,
                ref wroteHeadingOutput,
                ref matched,
                ref errored);
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
            SearchWalkPlanning.CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger).Threads(threadCount).BuildParallel().Run(() =>
            {
                MemoryStream buffer = new();
                RawByteWriter writer = CreateParallelOutputWriter(buffer);
                directOutputBuffers?.Add(buffer);
                return entry =>
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
                    byte[] displayPathBytes = displayPaths.GetBytes(entry);
                    SearchDirectoryEntryFile(
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
                        heading,
                        ref fileWroteHeading,
                        ref fileMatched,
                        ref fileErrored);
                    writer.Flush();

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
                        AddBufferedOutputIfAny(outputs, ref buffer, ref writer);
                    }

                    return fileMatched && lowArgs.Quiet ? WalkState.Quit : WalkState.Continue;
                };
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
        bool heading,
        ref bool wroteHeadingOutput,
        ref bool matched,
        ref bool errored,
        ref SearchStats stats)
    {
        int threadCount = SearchWalkPlanning.GetSearchWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            SearchDirectoryParallelWithStats(root, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
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
            SearchWalkPlanning.CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger).Threads(threadCount).BuildParallel().Run(() =>
            {
                MemoryStream buffer = new();
                RawByteWriter writer = CreateParallelOutputWriter(buffer);
                directOutputBuffers?.Add(buffer);
                return entry =>
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
                        heading,
                        ref fileWroteHeading,
                        ref fileMatched,
                        ref fileErrored,
                        ref fileStats);
                    writer.Flush();
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
                        AddBufferedOutputIfAny(outputs, ref buffer, ref writer);
                    }

                    return WalkState.Continue;
                };
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

    private static RawByteWriter CreateParallelOutputWriter(MemoryStream buffer)
    {
        return new RawByteWriter(buffer);
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
    }

    private static bool CanWriteParallelOutputDirectly(bool heading, bool interFileContextSeparator)
    {
        return !heading && !interFileContextSeparator;
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
            SearchRawUnixFile(path, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        SearchFile(entry.FullPath, entry.Length, pattern, lowArgs, implicitSearch: true, isOneFile: false, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
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
            SearchRawUnixFileWithStats(path, pattern, lowArgs, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        SearchFileWithStats(entry.FullPath, entry.Length, pattern, lowArgs, implicitSearch: true, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
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
        ref bool errored)
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

        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, logger, out byte[] bytes, out SearchFileReadKind readKind, knownLength))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path, readKind);
        bool memoryMapped = IsMemoryMapped(readKind);
        bool searchTextMode = textMode || SearchesBinaryAsText(readKind, lowArgs, searchMode, bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp);
        matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, searchTextMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch, searchTextMode), heading, ref wroteHeadingOutput, memoryMapped);
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
        ref bool errored)
    {
        if (!SearchFileContentReader.TryReadRawUnix(path, lowArgs, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path.DisplayText, readKind);
        matched |= StandardSearchByteOperations.SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, quitOnBinary: false, heading, ref wroteHeadingOutput);
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
