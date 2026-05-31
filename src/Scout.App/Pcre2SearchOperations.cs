using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        bool heading = ShouldUseHeading(lowArgs);
        bool wroteHeadingOutput = false;
        bool matched = false;
        bool errored = false;
        bool useDefaultCurrentDirectory = positional.Count == firstPathIndex &&
            (patternsReadFromStandardInput || !standardInputIsReadable);

        var paths = new List<SearchPathArgument>(Math.Max(1, positional.Count - firstPathIndex));
        if (positional.Count == firstPathIndex && !useDefaultCurrentDirectory)
        {
            try
            {
                byte[] stdinBytes = SearchFileContentReader.ReadSearchStream(standardInput, lowArgs.EncodingMode);
                byte[] pattern = BuildPcre2Pattern(patterns);
                using var regex = new Pcre2Regex(pattern, GetPcre2CompileOptions(lowArgs, patterns));
                JsonSearchSummary? jsonSummary = lowArgs.SearchMode == CliSearchMode.Json ? new JsonSearchSummary() : null;
                OutputPath stdinPath = new(StandardInputPath, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty);
                OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, autoPrefixPath: false, lowArgs.WithFilename);
                matched = RunPcre2SearchModeWithOptionalHeading(stdinBytes, regex, output, separators, stdinPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
                jsonSummary?.WriteSummary(output);
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

        bool prefixPaths = paths.Count > 1 || SearchPathArgument.ContainsDirectory(paths);
        bool autoMmapEligible = SearchPathArgument.IsAutoMmapEligible(paths);
        try
        {
            byte[] pattern = BuildPcre2Pattern(patterns);
            Pcre2CompileOptions compileOptions = GetPcre2CompileOptions(lowArgs, patterns);
            using var regex = new Pcre2Regex(pattern, compileOptions);
            JsonSearchSummary? jsonSummary = lowArgs.SearchMode == CliSearchMode.Json ? new JsonSearchSummary() : null;
            for (int index = 0; index < paths.Count; index++)
            {
                bool defaultRoot = useDefaultCurrentDirectory && index == 0;
                SearchPcre2Path(paths[index], standardInput, defaultRoot, prefixPaths, autoMmapEligible, lowArgs, regex, pattern, compileOptions, patterns, jsonSummary, separators, lineLimit, color, fileTypes!, output, diagnostics, heading, ref wroteHeadingOutput, ref matched, ref errored);
                if (matched && lowArgs.Quiet)
                {
                    break;
                }
            }

            jsonSummary?.WriteSummary(output);
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
        return (lowArgs.SearchMode is CliSearchMode.Standard
                or CliSearchMode.Json
                or CliSearchMode.Count
                or CliSearchMode.CountMatches
                or CliSearchMode.FilesWithMatches
                or CliSearchMode.FilesWithoutMatch) &&
            !lowArgs.Stats &&
            !lowArgs.FixedStrings &&
            !lowArgs.NullData &&
            !lowArgs.Vimgrep &&
            !(lowArgs.Multiline && lowArgs.InvertMatch) &&
            !(lowArgs.Multiline && lowArgs.OnlyMatching) &&
            !(contextRequested && (lowArgs.SearchMode != CliSearchMode.Standard || lowArgs.Multiline)) &&
            (lowArgs.Replacement is null || (lowArgs.SearchMode == CliSearchMode.Standard && !lowArgs.OnlyMatching));
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
            SearchPcre2RawUnixFile(pathArgument, lowArgs, regex, patterns, jsonSummary, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        path ??= pathArgument.DisplayText;
        if (path == "-")
        {
            byte[] bytes = SearchFileContentReader.ReadSearchStream(standardInput, lowArgs.EncodingMode);
            OutputPath outputPath = new(StandardInputPath, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty);
            OutputPath? prefix = SearchOutputFormatting.GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= RunPcre2SearchModeWithOptionalHeading(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchPcre2Directory(path, defaultRoot, lowArgs, regex, pcre2Pattern, compileOptions, patterns, jsonSummary, separators, lineLimit, color, fileTypes, output, diagnostics, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        if (File.Exists(path))
        {
            OutputPath outputPath = SearchOutputFormatting.CreateOutputPath(path, pathArgument.DisplayBytes, lowArgs, color);
            OutputPath? prefix = SearchOutputFormatting.GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchPcre2File(path, lowArgs, autoMmapEligible, regex, patterns, jsonSummary, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
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
            SearchPcre2DirectoryParallel(root, defaultRoot, lowArgs, pcre2Pattern, compileOptions, patterns, jsonSummary, separators, lineLimit, color, fileTypes, output, diagnostics, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics))
        {
            byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
            SearchPcre2DirectoryEntryFile(entry, displayPath, lowArgs, regex, patterns, jsonSummary, output, diagnostics, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
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
                byte[] displayPath = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
                SearchPcre2DirectoryEntryFile(entry, displayPath, lowArgs, workerRegexes.Value!, patterns, fileSummary, writer, diagnostics, separators, lineLimit, color, heading, ref fileWroteHeading, ref fileMatched, ref fileErrored);
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

                byte[] body = buffer.ToArray();
                if (body.Length > 0)
                {
                    outputs.Add(body);
                }

                return fileMatched && lowArgs.Quiet ? WalkState.Quit : WalkState.Continue;
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
            SearchPcre2RawUnixFile(path, lowArgs, regex, patterns, jsonSummary, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        SearchPcre2File(entry.FullPath, lowArgs, autoMmapEligible: false, regex, patterns, jsonSummary, output, diagnostics, outputPath, prefix, separators, lineLimit, color, heading, ref wroteHeadingOutput, ref matched, ref errored);
    }

    private static void SearchPcre2File(
        string path,
        CliLowArgs lowArgs,
        bool autoMmapEligible,
        Pcre2Regex regex,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
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

        matched |= RunPcre2SearchModeWithOptionalHeading(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
    }

    private static void SearchPcre2RawUnixFile(
        SearchPathArgument path,
        CliLowArgs lowArgs,
        Pcre2Regex regex,
        IReadOnlyList<byte[]> patterns,
        JsonSearchSummary? jsonSummary,
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

        matched |= RunPcre2SearchModeWithOptionalHeading(bytes, regex, output, separators, outputPath, prefix, lineLimit, color, lowArgs, patterns, jsonSummary, heading, ref wroteHeadingOutput);
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
            return RunPcre2JsonSearch(bytes, regex, output, path.Display, lowArgs, jsonSummary!);
        }

        if (lowArgs.Quiet)
        {
            return SearchPcre2Quiet(bytes, regex, lowArgs.SearchMode, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.InvertMatch, lowArgs.MaxCount);
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

    private static bool SearchPcre2Quiet(byte[] bytes, Pcre2Regex regex, CliSearchMode searchMode, bool lineRegexp, bool wordRegexp, bool multiline, bool invertMatch, ulong? maxCount)
    {
        bool hasMatch = multiline ? HasPcre2MultilineMatch(bytes, regex, lineRegexp, wordRegexp) : HasPcre2Match(bytes, regex, lineRegexp, wordRegexp, invertMatch);
        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return !hasMatch;
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            long count = multiline ? CountPcre2MultilineMatches(bytes, regex, lineRegexp, wordRegexp, maxCount) : CountPcre2Matches(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount);
            return count > 0;
        }

        if (searchMode == CliSearchMode.Count)
        {
            long count = multiline ? CountPcre2MultilineMatchingLines(bytes, regex, lineRegexp, wordRegexp, maxCount) : CountPcre2MatchingLines(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount);
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

    private static byte[] BuildPcre2Pattern(IReadOnlyList<byte[]> patterns)
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
            return RunPcre2MultilineSearchMode(bytes, regex, output, separators, path, prefix, lineLimit, color, searchMode, replacement, patterns, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, includeZero, nullPathTerminator, maxCount);
        }

        if (searchMode == CliSearchMode.Count)
        {
            long count = onlyMatching && !invertMatch
                ? CountPcre2Matches(bytes, regex, lineRegexp, wordRegexp, invertMatch: false, maxCount)
                : CountPcre2MatchingLines(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount);
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            return SearchOutputFormatting.WriteCount(output, prefix, color, CountPcre2Matches(bytes, regex, lineRegexp, wordRegexp, invertMatch, maxCount), includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, path, color, HasPcre2Match(bytes, regex, lineRegexp, wordRegexp, invertMatch), nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, path, color, !HasPcre2Match(bytes, regex, lineRegexp, wordRegexp, invertMatch), nullPathTerminator, separators.LineTerminator);
        }

        if (beforeContext > 0 || afterContext > 0 || passthru)
        {
            return SearchPcre2ContextLines(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, onlyMatching, replacement, patterns, beforeContext, afterContext, passthru, stopOnNonmatch, maxCount);
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
        {
            return SearchPcre2ReplacedLines(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, maxCount);
        }

        return SearchPcre2Lines(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, onlyMatching, maxCount);
    }

    private static bool RunPcre2JsonSearch(byte[] bytes, Pcre2Regex regex, RawByteWriter output, byte[] path, CliLowArgs lowArgs, JsonSearchSummary summary)
    {
        var writer = new JsonFileWriter(output, path, lowArgs.Quiet, binaryOffset: GetPcre2JsonBinaryOffset(bytes, lowArgs.TextMode));
        bool matched = lowArgs.Multiline
            ? SearchPcre2JsonMultilineBytes(bytes, regex, writer, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.MaxCount)
            : SearchPcre2JsonLines(bytes, regex, writer, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.InvertMatch, lowArgs.MaxCount);
        writer.Finish((ulong)bytes.Length, summary);
        return matched;
    }

    private static int GetPcre2JsonBinaryOffset(byte[] bytes, bool textMode)
    {
        return textMode ? -1 : bytes.AsSpan().IndexOf((byte)0);
    }

    private static bool SearchPcre2JsonLines(byte[] bytes, Pcre2Regex regex, JsonFileWriter writer, bool lineRegexp, bool wordRegexp, bool invertMatch, ulong? maxCount)
    {
        bool matched = false;
        int lineStart = 0;
        long lineNumber = 1;
        ulong matchedLines = 0;
        var matches = new List<JsonMatchSpan>();
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
            int outputEnd = lineFeed < 0 ? lineEnd : lineEnd + 1;
            ReadOnlySpan<byte> outputLine = bytes.AsSpan(lineStart, outputEnd - lineStart);
            ReadOnlySpan<byte> matchLine = bytes.AsSpan(lineStart, lineEnd - lineStart);
            if (matchLine.Length > 0 && matchLine[^1] == (byte)'\r')
            {
                matchLine = matchLine[..^1];
            }

            matches.Clear();
            CollectPcre2LineMatches(matchLine, regex, matches, lineRegexp, wordRegexp);
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

            if (lineFeed < 0)
            {
                break;
            }

            lineStart = outputEnd;
            lineNumber++;
        }

        return matched;
    }

    private static void CollectPcre2LineMatches(ReadOnlySpan<byte> line, Pcre2Regex regex, List<JsonMatchSpan> matches, bool lineRegexp, bool wordRegexp)
    {
        int startOffset = 0;
        while (startOffset <= line.Length && regex.TryFind(line, startOffset, out Pcre2Match match))
        {
            if (Pcre2MatchSatisfies(line, match, lineRegexp, wordRegexp))
            {
                matches.Add(new JsonMatchSpan(match.Start, match.Start + match.Length, replacement: null));
            }

            startOffset = match.Length == 0 ? match.Start + 1 : match.Start + match.Length;
        }
    }

    private static bool SearchPcre2JsonMultilineBytes(byte[] bytes, Pcre2Regex regex, JsonFileWriter writer, bool lineRegexp, bool wordRegexp, ulong? maxCount)
    {
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
            matches.Add(new JsonMatchSpan(match.Start - firstLineStart, match.Start - firstLineStart + match.Length, replacement: null));
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
        ulong? maxCount)
    {
        if (searchMode == CliSearchMode.Count)
        {
            return SearchOutputFormatting.WriteCount(output, prefix, color, CountPcre2MultilineMatchingLines(bytes, regex, lineRegexp, wordRegexp, maxCount), includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            return SearchOutputFormatting.WriteCount(output, prefix, color, CountPcre2MultilineMatches(bytes, regex, lineRegexp, wordRegexp, maxCount), includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, path, color, HasPcre2MultilineMatch(bytes, regex, lineRegexp, wordRegexp), nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, path, color, !HasPcre2MultilineMatch(bytes, regex, lineRegexp, wordRegexp), nullPathTerminator, separators.LineTerminator);
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            return SearchPcre2MultilineReplacedBytes(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, maxCount);
        }

        return SearchPcre2MultilineBytes(bytes, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, maxCount);
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
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
            int outputEnd = lineFeed < 0 ? lineEnd : lineEnd + 1;
            ReadOnlySpan<byte> outputLine = bytes.AsSpan(lineStart, outputEnd - lineStart);
            ReadOnlySpan<byte> matchLine = bytes.AsSpan(lineStart, lineEnd - lineStart);
            if (matchLine.Length > 0 && matchLine[^1] == (byte)'\r')
            {
                matchLine = matchLine[..^1];
            }

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

            if (lineFeed < 0)
            {
                break;
            }

            lineStart = outputEnd;
            currentLineNumber++;
        }

        return matched;
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
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool stopOnNonmatch,
        ulong? maxCount)
    {
        List<ContextLineInfo> lines = BuildPcre2ContextLines(bytes, regex, lineRegexp, wordRegexp, invertMatch, stopOnNonmatch);
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

            WritePcre2ContextOutputLine(bytes, line, selectedMatch, regex, output, separators, prefix, lineLimit, color, lineRegexp, wordRegexp, invertMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, onlyMatching, replacement, patterns, ref lineSink);
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
        bool stopOnNonmatch)
    {
        var lines = new List<ContextLineInfo>();
        bool hasSelectedMatch = false;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            GetPcre2LineSlices(bytes, lineStart, out ReadOnlySpan<byte> outputLine, out ReadOnlySpan<byte> matchLine, out int nextLineStart, out bool isLastLine);
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
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        ref StandardSearchSink lineSink)
    {
        ReadOnlySpan<byte> outputLine = bytes.AsSpan(line.Start, line.Length);
        ReadOnlySpan<byte> matchLine = GetPcre2MatchLine(outputLine);
        if (selectedMatch)
        {
            if (onlyMatching && !invertMatch)
            {
                WritePcre2OnlyMatches(matchLine, regex, output, separators.FieldMatch, prefix, color, line.LineNumber, line.Start, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, separators.LineTerminator);
                return;
            }

            if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
            {
                WritePcre2ReplacedContextLine(outputLine, matchLine, regex, output, separators, prefix, lineLimit, color, line.LineNumber, line.Start, lineRegexp, wordRegexp, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns);
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
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns)
    {
        var sink = new ReplacementLineSink(output, prefix, separators.FieldMatch, replacement, patterns, false, printLineNumber, printColumn, printByteOffset, trim, nullPathTerminator, vimgrep: false, lineLimit, color: color, lineTerminator: separators.LineTerminator);
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
        out ReadOnlySpan<byte> outputLine,
        out ReadOnlySpan<byte> matchLine,
        out int nextLineStart,
        out bool isLastLine)
    {
        ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
        int lineFeed = remaining.IndexOf((byte)'\n');
        int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
        int outputEnd = lineFeed < 0 ? lineEnd : lineEnd + 1;
        outputLine = bytes.AsSpan(lineStart, outputEnd - lineStart);
        matchLine = GetPcre2MatchLine(outputLine);
        nextLineStart = outputEnd;
        isLastLine = lineFeed < 0;
    }

    private static ReadOnlySpan<byte> GetPcre2MatchLine(ReadOnlySpan<byte> outputLine)
    {
        ReadOnlySpan<byte> matchLine = outputLine;
        if (!matchLine.IsEmpty && matchLine[^1] == (byte)'\n')
        {
            matchLine = matchLine[..^1];
        }

        if (!matchLine.IsEmpty && matchLine[^1] == (byte)'\r')
        {
            matchLine = matchLine[..^1];
        }

        return matchLine;
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
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        ulong? maxCount)
    {
        bool matched = false;
        int lineStart = 0;
        long currentLineNumber = 1;
        ulong matchedLines = 0;
        var sink = new ReplacementLineSink(output, prefix, separators.FieldMatch, replacement, patterns, false, lineNumber, column, byteOffset, trim, nullPathTerminator, vimgrep: false, lineLimit, color: color, lineTerminator: separators.LineTerminator);
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
            int outputEnd = lineFeed < 0 ? lineEnd : lineEnd + 1;
            ReadOnlySpan<byte> outputLine = bytes.AsSpan(lineStart, outputEnd - lineStart);
            ReadOnlySpan<byte> matchLine = bytes.AsSpan(lineStart, lineEnd - lineStart);
            if (matchLine.Length > 0 && matchLine[^1] == (byte)'\r')
            {
                matchLine = matchLine[..^1];
            }

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

            if (lineFeed < 0)
            {
                break;
            }

            lineStart = outputEnd;
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

    private static bool HasPcre2Match(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, bool invertMatch)
    {
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
            ReadOnlySpan<byte> line = bytes.AsSpan(lineStart, lineEnd - lineStart);
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            bool originalMatch = TryFindPcre2LineMatch(line, regex, lineRegexp, wordRegexp, out _);
            if (invertMatch ? !originalMatch : originalMatch)
            {
                return true;
            }

            if (lineFeed < 0)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        return false;
    }

    private static long CountPcre2MatchingLines(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, bool invertMatch, ulong? maxCount)
    {
        long count = 0;
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
            ReadOnlySpan<byte> line = bytes.AsSpan(lineStart, lineEnd - lineStart);
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            bool originalMatch = TryFindPcre2LineMatch(line, regex, lineRegexp, wordRegexp, out _);
            if (invertMatch ? !originalMatch : originalMatch)
            {
                count++;
                if (maxCount is ulong limit && (ulong)count >= limit)
                {
                    break;
                }
            }

            if (lineFeed < 0)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        return count;
    }

    private static long CountPcre2Matches(byte[] bytes, Pcre2Regex regex, bool lineRegexp, bool wordRegexp, bool invertMatch, ulong? maxCount)
    {
        long count = 0;
        int lineStart = 0;
        ulong matchedLines = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
            ReadOnlySpan<byte> line = bytes.AsSpan(lineStart, lineEnd - lineStart);
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

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

            if (lineFeed < 0)
            {
                break;
            }

            lineStart = lineEnd + 1;
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
