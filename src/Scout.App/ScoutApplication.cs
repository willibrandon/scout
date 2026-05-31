using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Scout;

internal static class ScoutApplication
{
    private static readonly byte[] StandardInputPath = "<stdin>"u8.ToArray();
    private static readonly byte[] NullByte = [0];
    private static readonly byte[] LineFeed = [(byte)'\n'];
    private static readonly byte[] CrlfLineTerminator = [(byte)'\r', (byte)'\n'];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private const int BinaryDetectionInitialBufferLength = 65_536;
    private const int StreamingFileBufferLength = 16_777_216;
    private const long StreamingFileThreshold = int.MaxValue;

    internal static int Run(ReadOnlySpan<OsString> arguments, RawByteWriter output, RawByteWriter error)
    {
        using Stream standardInput = Console.OpenStandardInput();
        return Run(arguments, output, error, standardInput, StandardInputProbe.IsReadable(), configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        string? configPath)
    {
        using Stream standardInput = Console.OpenStandardInput();
        return Run(arguments, output, error, standardInput, StandardInputProbe.IsReadable(), configPath, useConfigPathOverride: true);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput)
    {
        return Run(arguments, output, error, standardInput, standardInputIsReadable: true, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        string? configPath)
    {
        return Run(arguments, output, error, standardInput, standardInputIsReadable: true, configPath, useConfigPathOverride: true);
    }

    private static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        bool standardInputIsReadable,
        string? configPathOverride,
        bool useConfigPathOverride)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(standardInput);

        var diagnostics = new DiagnosticMessenger(error);
        DiagnosticLogger earlyLogger = new(diagnostics, ConfigArgumentExpander.DetectLoggingMode(arguments[1..]));
        OsString[]? expandedArguments = ConfigArgumentExpander.BuildConfiguredArguments(arguments, diagnostics, earlyLogger, configPathOverride, useConfigPathOverride);
        ReadOnlySpan<OsString> parserArguments = expandedArguments is null
            ? arguments[1..]
            : expandedArguments;
        CliParseResult parseResult = CliParser.Parse(parserArguments);

        if (parseResult.Status == CliParseStatus.Error)
        {
            diagnostics.ErrorMessage(parseResult.Error!.WithContext("rg"));
            return ExitCode.Error;
        }

        if (parseResult.Status == CliParseStatus.Special)
        {
            return RunSpecial(parseResult.SpecialMode, output);
        }

        return RunSearch(parseResult.LowArgs!, output, diagnostics, standardInput, standardInputIsReadable);
    }

    private static bool TextEquals(OsString argument, string expected)
    {
        return argument.TryGetText(out string text) && string.Equals(text, expected, StringComparison.Ordinal);
    }

    private static bool IsStandardInputArgument(OsString argument)
    {
        return argument.EqualsUnixBytes("-"u8) || TextEquals(argument, "-");
    }

    private static int RunSpecial(CliSpecialMode specialMode, RawByteWriter output)
    {
        switch (specialMode)
        {
            case CliSpecialMode.VersionShort:
                output.Write(VersionOutput.Short);
                output.Flush();
                return ExitCode.Success;

            case CliSpecialMode.VersionLong:
                output.Write(VersionOutput.GetLong());
                output.Flush();
                return ExitCode.Success;

            case CliSpecialMode.Pcre2Version:
                output.Write(Pcre2Library.GetVersionOutput());
                output.Flush();
                return Pcre2Library.IsAvailable ? ExitCode.Success : ExitCode.NoMatch;

            case CliSpecialMode.HelpShort:
                output.Write(HelpOutput.Short);
                output.Flush();
                return ExitCode.Success;

            case CliSpecialMode.HelpLong:
                output.Write(HelpOutput.Long);
                output.Flush();
                return ExitCode.Success;

            default:
                return ExitCode.Error;
        }
    }

    private static int RunSearch(
        CliLowArgs lowArgs,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        Stream standardInput,
        bool standardInputIsReadable)
    {
        DiagnosticLogger logger = new(diagnostics, lowArgs.LoggingMode);
        IReadOnlyList<OsString> positional = lowArgs.Positional;
        if (lowArgs.GenerateMode is CliGenerateMode generateMode)
        {
            output.Write(GenerateOutput.Get(generateMode));
            output.Flush();
            return ExitCode.Success;
        }

        if (lowArgs.TypeList)
        {
            return SearchWalkPlanning.RunTypeList(lowArgs, output, diagnostics);
        }

        if (!SearchWalkPlanning.TryValidateOverrideGlobs(lowArgs, diagnostics))
        {
            return ExitCode.Error;
        }

        if (lowArgs.SearchMode == CliSearchMode.Files)
        {
            if (!SearchWalkPlanning.TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? fileTypes, out ScoutError? error))
            {
                diagnostics.ErrorMessage(error!.WithContext("rg"));
                return ExitCode.Error;
            }

            return RunFiles(positional, lowArgs, fileTypes!, output, diagnostics);
        }

        var patterns = new List<byte[]>();
        int firstPathIndex = 0;
        bool patternsReadFromStandardInput = false;
        if (lowArgs.PatternSources.Count == 0)
        {
            if (positional.Count == 0)
            {
                diagnostics.ErrorMessage(new ScoutError("ripgrep requires at least one pattern to execute a search").WithContext("rg"));
                return ExitCode.Error;
            }

            patterns.Add(PatternPreparation.GetPatternBytes(positional[0]));
            firstPathIndex = 1;
        }
        else
        {
            for (int index = 0; index < lowArgs.PatternSources.Count; index++)
            {
                CliPatternSource source = lowArgs.PatternSources[index];
                if (source.IsFile)
                {
                    patternsReadFromStandardInput |= IsStandardInputArgument(source.Value);
                    if (!PatternFileLoader.TryLoad(source.Value, patterns, standardInput, diagnostics))
                    {
                        return ExitCode.Error;
                    }
                }
                else
                {
                    patterns.Add(PatternPreparation.GetPatternBytes(source.Value));
                }
            }
        }

        if (lowArgs.RegexEngine == CliRegexEngine.Pcre2)
        {
            return Pcre2SearchOperations.Run(positional, firstPathIndex, patternsReadFromStandardInput, lowArgs, patterns, standardInput, standardInputIsReadable, output, diagnostics);
        }

        if (Pcre2SearchOperations.ShouldAutoUse(lowArgs, patterns))
        {
            return Pcre2SearchOperations.Run(positional, firstPathIndex, patternsReadFromStandardInput, lowArgs, patterns, standardInput, standardInputIsReadable, output, diagnostics);
        }

        if (!lowArgs.Multiline && PatternPreparation.ContainsLineTerminator(patterns, lowArgs.NullData, lowArgs.FixedStrings))
        {
            diagnostics.ErrorMessage(new ScoutError(PatternPreparation.BuildLineTerminatorPatternError(lowArgs.NullData)).WithContext("rg"));
            return ExitCode.Error;
        }

        if (!lowArgs.TextMode && !lowArgs.NullData && !lowArgs.FixedStrings && PatternPreparation.ContainsRegexNulLiteral(patterns))
        {
            diagnostics.ErrorMessage(new ScoutError("pattern contains \"\\0\" but it is impossible to match\n\nConsider enabling text mode with the --text flag (or -a for short). Otherwise,\nbinary detection is enabled and matching a NUL byte is impossible.").WithContext("rg"));
            return ExitCode.Error;
        }

        if (lowArgs.FixedStrings)
        {
            PatternPreparation.EscapeFixedStringPatterns(patterns);
        }
        else
        {
            if (!PatternPreparation.TryValidateRegexRepetitionExpressions(patterns, diagnostics))
            {
                return ExitCode.Error;
            }

            PatternPreparation.WrapRegexPatterns(patterns);
        }

        if (!PatternPreparation.TryValidateRegexSizeLimit(patterns, lowArgs, diagnostics))
        {
            return ExitCode.Error;
        }

        if (!SearchWalkPlanning.TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? searchFileTypes, out ScoutError? searchError))
        {
            diagnostics.ErrorMessage(searchError!.WithContext("rg"));
            return ExitCode.Error;
        }

        bool asciiCaseInsensitive = PatternPreparation.IsAsciiCaseInsensitive(patterns, lowArgs.CaseMode);
        if (!lowArgs.Unicode && asciiCaseInsensitive)
        {
            PatternPreparation.WrapNonAsciiPatterns(patterns);
        }

        if (!lowArgs.FixedStrings && !lowArgs.Unicode)
        {
            PatternPreparation.WrapNoUnicodePatterns(patterns);
        }

        SearchDiagnosticLogging.LogSearchConfiguration(logger, positional, firstPathIndex, lowArgs, patterns);
        if (lowArgs.SearchMode == CliSearchMode.Json)
        {
            return RunJsonSearch(positional, firstPathIndex, patternsReadFromStandardInput, lowArgs, patterns, asciiCaseInsensitive, searchFileTypes!, output, diagnostics, standardInput, standardInputIsReadable);
        }

        OutputSeparators separators = GetOutputSeparators(lowArgs);
        OutputLineLimit lineLimit = GetOutputLineLimit(lowArgs);
        OutputColor color = GetOutputColor(lowArgs);
        bool heading = ShouldUseHeading(lowArgs);
        bool lineNumber = SearchOutputFormatting.EffectiveLineNumber(lowArgs);
        bool column = SearchOutputFormatting.EffectiveColumn(lowArgs);
        bool wroteHeadingOutput = false;
        bool matched = false;
        bool errored = false;
        bool stats = lowArgs.Stats && lowArgs.MaxCount != 0;
        long statsStarted = Stopwatch.GetTimestamp();
        SearchStats searchStats = default;
        bool useDefaultCurrentDirectory = positional.Count == firstPathIndex &&
            (patternsReadFromStandardInput || !standardInputIsReadable);
        if (positional.Count == firstPathIndex && !useDefaultCurrentDirectory)
        {
            matched = stats
                ? SearchStandardInputWithStats(patterns, standardInput, output, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, false, lineNumber, column, lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.WithFilename, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput, ref searchStats)
                : SearchStandardInput(patterns, standardInput, output, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, false, lineNumber, column, lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.WithFilename, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput);
            if (stats)
            {
                StatsTextWriter.Write(output, searchStats, Stopwatch.GetElapsedTime(statsStarted));
            }

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

        bool prefixPaths = lowArgs.Vimgrep || paths.Count > 1 || SearchPathArgument.ContainsDirectory(paths);
        bool autoMmapEligible = SearchPathArgument.IsAutoMmapEligible(paths);
        bool interPathContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        for (int index = 0; index < paths.Count; index++)
        {
            bool defaultRoot = useDefaultCurrentDirectory && index == 0;
            if (interPathContextSeparator)
            {
                using MemoryStream buffer = new();
                var writer = new RawByteWriter(buffer);
                bool pathMatched = false;
                bool pathErrored = false;
                if (stats)
                {
                    SearchStats pathStats = default;
                    SearchPathWithStats(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, writer, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref pathMatched, ref pathErrored, ref pathStats);
                    searchStats.Add(pathStats);
                }
                else
                {
                    SearchPath(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, writer, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref pathMatched, ref pathErrored);
                }

                writer.Flush();
                byte[] body = buffer.ToArray();
                if (body.Length > 0)
                {
                    WriteInterFileContextSeparatorIfNeeded(output, separators, ref wroteContextBody);
                    output.Write(body);
                }

                matched |= pathMatched;
                errored |= pathErrored;
                continue;
            }

            if (stats)
            {
                SearchPathWithStats(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, output, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref matched, ref errored, ref searchStats);
            }
            else
            {
                SearchPath(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, output, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref matched, ref errored);
            }
        }

        if (stats)
        {
            if (interPathContextSeparator && wroteContextBody)
            {
                output.Write(separators.Context.Span);
                output.Write(separators.LineTerminator.Span);
            }

            StatsTextWriter.Write(output, searchStats, Stopwatch.GetElapsedTime(statsStarted));
        }

        output.Flush();
        return SearchOutputFormatting.GetSearchExitCode(matched, errored, lowArgs.Quiet);
    }

    private static int RunJsonSearch(
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

        SearchErrorMessage(lowArgs, diagnostics, MissingPathError(path, multiplePaths));
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

    private static int RunFiles(
        IReadOnlyList<OsString> positional,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics)
    {
        bool emitted = false;
        bool errored = false;
        if (positional.Count == 0)
        {
            ListFilesPath(path: ".", defaultRoot: true, lowArgs, fileTypes, output, diagnostics, ref emitted, ref errored);
        }
        else
        {
            for (int index = 0; index < positional.Count; index++)
            {
                if (SearchPathArgument.TryGetText(positional[index], diagnostics, out string path))
                {
                    ListFilesPath(path, defaultRoot: false, lowArgs, fileTypes, output, diagnostics, ref emitted, ref errored);
                }
                else
                {
                    errored = true;
                }
            }
        }

        output.Flush();
        return SearchOutputFormatting.GetSearchExitCode(emitted, errored, lowArgs.Quiet);
    }

    private static void ListFilesPath(
        string path,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool emitted,
        ref bool errored)
    {
        if (path == "-")
        {
            if (!lowArgs.Quiet)
            {
                output.Write(StandardInputPath);
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }

            emitted = true;
            return;
        }

        if (File.Exists(path))
        {
            if (!lowArgs.Quiet)
            {
                output.Write(SearchPathArgument.GetPathBytes(path, lowArgs.PathSeparator));
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }

            emitted = true;
            return;
        }

        if (!Directory.Exists(path))
        {
            SearchErrorMessage(lowArgs, diagnostics, MissingPathError(path));
            errored = true;
            return;
        }

        string fullRoot = Path.GetFullPath(path);
        int threadCount = SearchWalkPlanning.GetFilesWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            ListFilesDirectoryParallel(path, fullRoot, defaultRoot, lowArgs, fileTypes, output, diagnostics, ref emitted);
            return;
        }

        ListFilesDirectorySerial(path, fullRoot, defaultRoot, lowArgs, fileTypes, output, diagnostics, ref emitted);
    }

    private static void ListFilesDirectorySerial(
        string path,
        string fullRoot,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool emitted)
    {
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(path, lowArgs, fileTypes, diagnostics))
        {
            string displayPath = defaultRoot
                ? SearchPathArgument.GetSearchDirectoryDisplayPath(path, fullRoot, entry.FullPath, defaultRoot: true)
                : SearchPathArgument.GetDirectoryDisplayPath(path, fullRoot, entry.FullPath);
            byte[] displayPathBytes = entry.IsRawUnixPath
                ? SearchPathArgument.GetSearchDirectoryDisplayPathBytes(path, fullRoot, entry, defaultRoot, lowArgs.PathSeparator)
                : SearchPathArgument.GetPathBytes(displayPath, lowArgs.PathSeparator);
            if (!lowArgs.Quiet)
            {
                output.Write(displayPathBytes);
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }

            emitted = true;
        }
    }

    private static void ListFilesDirectoryParallel(
        string path,
        string fullRoot,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool emitted)
    {
        int threadCount = SearchWalkPlanning.GetFilesWalkThreadCount(lowArgs);
        using var entries = new BlockingCollection<DirEntry>();
        int found = 0;
        var printTask = Task.Run(() =>
        {
            foreach (DirEntry entry in entries.GetConsumingEnumerable())
            {
                string displayPath = defaultRoot
                    ? SearchPathArgument.GetSearchDirectoryDisplayPath(path, fullRoot, entry.FullPath, defaultRoot: true)
                    : SearchPathArgument.GetDirectoryDisplayPath(path, fullRoot, entry.FullPath);
                byte[] displayPathBytes = entry.IsRawUnixPath
                    ? SearchPathArgument.GetSearchDirectoryDisplayPathBytes(path, fullRoot, entry, defaultRoot, lowArgs.PathSeparator)
                    : SearchPathArgument.GetPathBytes(displayPath, lowArgs.PathSeparator);
                output.Write(displayPathBytes);
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }
        });

        try
        {
            SearchWalkPlanning.CreateWalkBuilder(path, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
            {
                if (!entry.IsFile)
                {
                    return WalkState.Continue;
                }

                Interlocked.Exchange(ref found, 1);
                if (lowArgs.Quiet)
                {
                    return WalkState.Quit;
                }

                entries.Add(entry);
                return WalkState.Continue;
            });
        }
        finally
        {
            entries.CompleteAdding();
        }

        printTask.GetAwaiter().GetResult();
        emitted |= Volatile.Read(ref found) != 0;
    }

    private static bool SearchStandardInput(
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
        return SearchBytesWithOptionalHeading(bytes, pattern, output, SearchOutputFormatting.GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput);
    }

    private static void SearchPath(
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
            SearchFile(path, pattern, lowArgs, false, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        SearchErrorMessage(lowArgs, diagnostics, MissingPathError(path, multiplePaths));
        errored = true;
    }

    private static bool SearchStandardInputWithStats(
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
        return SearchBytesWithStats(bytes, pattern, output, SearchOutputFormatting.GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, ref stats);
    }

    private static void SearchPathWithStats(
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
            SearchFileWithStats(path, pattern, lowArgs, false, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        SearchErrorMessage(lowArgs, diagnostics, MissingPathError(path, multiplePaths));
        errored = true;
    }

    private static bool SearchStandardInput(
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
        return SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput);
    }

    private static bool SearchStandardInputWithStats(
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
        return SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, ref stats);
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
        bool interFileContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics))
        {
            byte[] displayPathBytes = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
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
                    WriteInterFileContextSeparatorIfNeeded(output, separators, ref wroteContextBody);
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
        using var outputs = new BlockingCollection<byte[]>();
        int matchedFlag = 0;
        int erroredFlag = 0;
        bool printedHeading = wroteHeadingOutput;
        bool interFileContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool printedContextBody = false;
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

                if (interFileContextSeparator)
                {
                    WriteInterFileContextSeparatorIfNeeded(output, separators, ref printedContextBody);
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
                bool fileWroteHeading = false;
                bool fileMatched = false;
                bool fileErrored = false;
                byte[] displayPathBytes = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
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
        bool interFileContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics))
        {
            byte[] displayPathBytes = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
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
                    WriteInterFileContextSeparatorIfNeeded(output, separators, ref wroteContextBody);
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
        using var outputs = new BlockingCollection<byte[]>();
        object statsLock = new();
        SearchStats aggregateStats = default;
        int matchedFlag = 0;
        int erroredFlag = 0;
        bool printedHeading = wroteHeadingOutput;
        bool interFileContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool printedContextBody = false;
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

                if (interFileContextSeparator)
                {
                    WriteInterFileContextSeparatorIfNeeded(output, separators, ref printedContextBody);
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
                bool fileWroteHeading = false;
                bool fileMatched = false;
                bool fileErrored = false;
                SearchStats fileStats = default;
                byte[] displayPathBytes = SearchPathArgument.GetSearchDirectoryDisplayPathBytes(root, fullRoot, entry, defaultRoot, lowArgs.PathSeparator);
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
        wroteHeadingOutput = printedHeading;
        stats.Add(aggregateStats);
        matched |= Volatile.Read(ref matchedFlag) != 0;
        errored |= Volatile.Read(ref erroredFlag) != 0;
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

        SearchFile(entry.FullPath, pattern, lowArgs, implicitSearch: true, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
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

        SearchFileWithStats(entry.FullPath, pattern, lowArgs, implicitSearch: true, autoMmapEligible: false, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, SearchOutputFormatting.EffectiveLineNumber(lowArgs), SearchOutputFormatting.EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
    }

    private static bool ShouldWriteInterFileContextSeparator(CliLowArgs lowArgs, bool heading, OutputSeparators separators)
    {
        return !heading &&
            separators.ContextEnabled &&
            lowArgs.SearchMode == CliSearchMode.Standard &&
            !lowArgs.Passthru &&
            (lowArgs.BeforeContext > 0 || lowArgs.AfterContext > 0);
    }

    private static void WriteInterFileContextSeparatorIfNeeded(RawByteWriter output, OutputSeparators separators, ref bool wroteContextBody)
    {
        if (wroteContextBody)
        {
            output.Write(separators.Context.Span);
            output.Write(separators.LineTerminator.Span);
        }

        wroteContextBody = true;
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

    private static void SearchFile(
        string path,
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
        ref bool errored)
    {
        if (TrySearchLargeFile(
            path,
            pattern,
            lowArgs,
            implicitSearch,
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

        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path, readKind);
        matched |= SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch), heading, ref wroteHeadingOutput);
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
        matched |= SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, quitOnBinary: false, heading, ref wroteHeadingOutput);
    }

    private static bool TrySearchLargeFile(
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
            if (!CanSearchLargeFileStreaming(path, lowArgs, implicitSearch, color, searchMode, vimgrep, onlyMatching, replacement, beforeContext, afterContext, passthru, heading))
            {
                return false;
            }

            SearchDiagnosticLogging.LogTraceSearchPath(logger, path, SearchFileReadKind.Buffered);
            matched |= SearchLargeFileStreaming(
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
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            errored = true;
        }
        catch (UnauthorizedAccessException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            errored = true;
        }

        return true;
    }

    private static bool CanSearchLargeFileStreaming(
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

    private static bool SearchLargeFileStreaming(
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
            return SearchLargeFileStreamingStandard(
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
                        return FinishStreamingSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
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
                        return FinishStreamingSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
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
                return FinishStreamingSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
            }
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !matched, nullPathTerminator, separators.LineTerminator);
        }

        return FinishStreamingSearch(output, prefix, color, searchMode, quiet, includeZero, nullPathTerminator, separators.LineTerminator, matched, count);
    }

    private static bool FinishStreamingSearch(
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

    private static bool SearchLargeFileStreamingStandard(
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
                if (!SearchQuiet(segment, pattern, CliSearchMode.Standard, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, remainingMatches, separators.Crlf, separators.NullData))
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

    private static void SearchFileWithStats(
        string path,
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
        if (!SearchFileContentReader.TryRead(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        SearchDiagnosticLogging.LogTraceSearchPath(logger, path, readKind);
        matched |= SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch), heading, ref wroteHeadingOutput, ref stats);
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
        matched |= SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, quitOnBinary: false, heading, ref wroteHeadingOutput, ref stats);
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

    private static bool ShouldQuitOnBinary(CliLowArgs lowArgs, bool implicitSearch)
    {
        return implicitSearch && !lowArgs.SearchBinaryFiles && !lowArgs.TextMode;
    }

    private static bool SearchBytesWithOptionalHeading(
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
        ref bool wroteHeadingOutput)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (!heading)
        {
            return SearchBytes(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary);
        }

        if (TrySearchBinarySuppressed(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, out bool binaryMatched))
        {
            return binaryMatched;
        }

        using MemoryStream bufferedOutput = new();
        var bufferedWriter = new RawByteWriter(bufferedOutput);
        bool matched = SearchBytes(bytes, pattern, bufferedWriter, prefix: null, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary);
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

    private static bool SearchBytesWithStats(
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
        ref SearchStats stats)
    {
        long started = Stopwatch.GetTimestamp();
        using MemoryStream buffer = new();
        var bufferedWriter = new RawByteWriter(buffer);
        bool matched = SearchBytesWithOptionalHeading(bytes, pattern, bufferedWriter, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, heading, ref wroteHeadingOutput);
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
        bool quitOnBinary)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (TrySearchBinarySuppressed(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, quitOnBinary, out bool binaryMatched))
        {
            return binaryMatched;
        }

        byte[] searchBytes = GetBinaryConvertedSearchBytes(bytes, textMode, separators.NullData);
        int stopLength = stopOnNonmatch
            ? ContextSearchOperations.GetStopOnNonmatchLength(searchBytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, separators.Crlf, separators.NullData)
            : searchBytes.Length;
        ReadOnlySpan<byte> searchSpan = searchBytes.AsSpan(0, stopLength);
        ReadOnlySpan<byte> outputSpan = ReferenceEquals(bytes, searchBytes)
            ? searchSpan
            : bytes.AsSpan(0, stopLength);

        if (multiline &&
            PatternPreparation.ShouldUseMultilineRegex(pattern, multilineDotall) &&
            TrySearchMultilineBytes(
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
            return SearchQuiet(searchSpan, pattern, searchMode, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
        }

        if (searchMode == CliSearchMode.Count)
        {
            long count = onlyMatching && !invertMatch
                ? LiteralLineSearcher.CountMatches(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData)
                : LiteralLineSearcher.CountMatchingLines(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
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
        return LiteralLineSearcher.Search(outputSpan, pattern, ref sink, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
    }

    private static bool TrySearchMultilineBytes(
        ReadOnlySpan<byte> searchSpan,
        ReadOnlySpan<byte> outputSpan,
        IReadOnlyList<byte[]> patterns,
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
        out bool matched)
    {
        matched = false;
        bool contextOutputRequested = (beforeContext > 0 || afterContext > 0) && searchMode == CliSearchMode.Standard;
        if ((contextOutputRequested && (onlyMatching || invertMatch)) ||
            passthru ||
            separators.Crlf ||
            separators.NullData)
        {
            return false;
        }

        if (invertMatch)
        {
            matched = SearchMultilineInvertedBytes(searchSpan, patterns, output, prefix, separators, lineLimit, color, searchMode, lineNumber, column, byteOffset, maxCount, quiet, trim, includeZero, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
            return true;
        }

        if (!TryFindMultilineMatch(searchSpan, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, startAt: 0, out RegexMatch firstMatch) ||
            IsTrailingEmptyLineMatch(searchSpan, firstMatch))
        {
            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
            {
                matched = SearchOutputFormatting.WriteCount(output, prefix, color, 0, includeZero, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            return true;
        }

        if (quiet)
        {
            matched = searchMode != CliSearchMode.FilesWithoutMatch;
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, false, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        long count = CountMultilineMatches(searchSpan, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            matched = SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        matched = true;
        if (contextOutputRequested)
        {
            WriteMultilineContextMatchedLines(outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, beforeContext, afterContext, maxCount);
            return true;
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatchingReplacements(outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
                return true;
            }

            WriteMultilineReplacedLines(outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        if (onlyMatching && !vimgrep)
        {
            WriteMultilineOnlyMatches(outputSpan, patterns, output, prefix, separators, color, lineNumber, column, byteOffset, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        if (vimgrep)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatches(outputSpan, patterns, output, prefix, separators, color, lineNumber: true, column: true, byteOffset, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
                return true;
            }

            WriteMultilineVimgrepMatches(outputSpan, patterns, output, prefix, separators, lineLimit, color, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        WriteMultilineMatchedLines(outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
        return true;
    }

    private static bool SearchMultilineInvertedBytes(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool lineNumber,
        bool column,
        bool byteOffset,
        ulong? maxCount,
        bool quiet,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall)
    {
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
        long count = CountSelectedMultilineLines(lines, maxCount);
        bool hasSelectedMatch = count > 0;

        if (quiet)
        {
            return searchMode == CliSearchMode.FilesWithoutMatch ? !hasSelectedMatch : hasSelectedMatch;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
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

            sink.MatchedLine(line.LineNumber, line.Start, matchColumn: 0, bytes.Slice(line.Start, line.Length));
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }
        }

        return hasSelectedMatch;
    }

    private static long CountMultilineMatches(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall, ulong? maxCount)
    {
        long count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            count++;
            if (maxCount is ulong limit && (ulong)count >= limit)
            {
                return count;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        return count;
    }

    private static bool TryFindNextMultilineMatch(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ref int offset,
        ref int suppressedEmptyStart,
        out RegexMatch match)
    {
        while (offset <= bytes.Length && TryFindMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, offset, out match))
        {
            if (IsTrailingEmptyLineMatch(bytes, match))
            {
                offset = bytes.Length + 1;
                break;
            }

            var matcherMatch = new MatcherMatch(match.Start, match.Length);
            if (!MatchIterator.IsSuppressedEmpty(matcherMatch, suppressedEmptyStart))
            {
                return true;
            }

            offset = MatchIterator.AdvanceAfterSuppressedEmpty(matcherMatch, bytes.Length);
            suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        }

        match = default;
        return false;
    }

    private static bool IsTrailingEmptyLineMatch(ReadOnlySpan<byte> bytes, RegexMatch match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            (bytes.IsEmpty || bytes[^1] == (byte)'\n');
    }

    private static bool IsEofEmptyMatch(ReadOnlySpan<byte> bytes, RegexMatch match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            !bytes.IsEmpty &&
            bytes[^1] != (byte)'\n';
    }

    private static int AdvanceAfterReportedMultilineMatch(RegexMatch match, int haystackLength, ref int suppressedEmptyStart)
    {
        return MatchIterator.AdvanceAfterReported(new MatcherMatch(match.Start, match.Length), haystackLength, ref suppressedEmptyStart);
    }

    private static void WriteMultilineContextMatchedLines(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong beforeContext,
        ulong afterContext,
        ulong? maxCount)
    {
        List<ContextLineInfo> lines = BuildMultilineContextLines(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
        bool[] included = new bool[lines.Count];
        ContextSearchOperations.IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int previousLineIndex = -1;
        bool wrote = false;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            if (wrote && index > previousLineIndex + 1 && separators.ContextEnabled)
            {
                output.Write(separators.Context.Span);
                output.Write(separators.LineTerminator.Span);
            }

            ContextLineInfo line = lines[index];
            ReadOnlySpan<byte> lineBytes = bytes.Slice(line.Start, line.Length);
            if (line.SelectedMatch)
            {
                sink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, lineBytes);
            }
            else
            {
                sink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, lineBytes);
            }

            previousLineIndex = index;
            wrote = true;
        }
    }

    private static void WriteMultilineMatchedLines(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        int lastWrittenLineStart = -1;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart > lastWrittenLineStart)
                {
                    sink.MatchedLine(firstLineNumber + CountLineFeeds(bytes[firstLineStart..lineStart]), lineStart, matchColumn, bytes[lineStart..lineEnd]);
                    lastWrittenLineStart = lineStart;
                    emitted++;
                    if (maxCount is ulong limit && emitted >= limit)
                    {
                        return;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteMultilineReplacedLines(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        int groupStart = -1;
        int groupEnd = -1;
        List<RegexMatch> groupMatches = [];
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            GetMultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
            if (groupStart >= 0 && rangeStart >= groupEnd)
            {
                WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
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

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        if (groupStart >= 0)
        {
            WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
        }
    }

    private static void WriteMultilineOnlyMatchingReplacements(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
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
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            byte[] body = ReplacementFormatter.Expand(replacement.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive);
            int lineStart = GetLineStart(bytes, match.Start);
            WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteMultilineReplacementRecord(
        ReadOnlySpan<byte> bytes,
        int recordStart,
        int recordEnd,
        List<RegexMatch> matches,
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
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive)
    {
        List<int> starts = [];
        List<int> lengths = [];
        for (int index = 0; index < matches.Count; index++)
        {
            starts.Add(matches[index].Start - recordStart);
            lengths.Add(matches[index].Length);
        }

        List<long> replacementColumns = [];
        byte[] body = ReplacementFormatter.ReplaceLine(bytes[recordStart..recordEnd], starts, lengths, replacement.Span, patterns, asciiCaseInsensitive, replacementColumns);
        int lineStart = GetLineStart(bytes, recordStart);
        WriteMultilineReplacementBody(body, recordStart, GetLineNumber(bytes, lineStart), replacementColumns.Count > 0 ? replacementColumns[0] : 1, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
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

    private static void GetMultilineReplacementRange(ReadOnlySpan<byte> bytes, RegexMatch match, out int start, out int end)
    {
        start = GetLineStart(bytes, match.Start);
        int endAnchor = match.Length == 0 ? match.Start : match.Start + match.Length;
        int lastLineStart = GetLineStart(bytes, endAnchor);
        end = GetLineEnd(bytes, lastLineStart);
    }

    private static void WriteMultilineOnlyMatches(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        var sink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (IsEofEmptyMatch(bytes, match))
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
                return;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteMultilineVimgrepMatches(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool trim,
        bool nullPathTerminator,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber: true, column: true, byteOffset: false, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int lineStart = GetLineStart(bytes, match.Start);
            int lineEnd = GetLineEnd(bytes, lineStart);
            long lineNumber = GetLineNumber(bytes, lineStart);
            long matchColumn = match.Start - lineStart + 1L;
            sink.MatchedLine(lineNumber, lineStart, matchColumn, bytes[lineStart..lineEnd]);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static bool TryFindMultilineMatch(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall, int startAt, out RegexMatch match)
    {
        match = default;
        bool found = false;
        for (int index = 0; index < patterns.Count; index++)
        {
            var automaton = RegexAutomaton.Compile(patterns[index], asciiCaseInsensitive, multiLine: true, dotMatchesNewline: multilineDotall);
            int candidateStartAt = startAt;
            RegexMatch? candidate = null;
            while (candidateStartAt <= bytes.Length)
            {
                RegexMatch? next = automaton.Find(bytes, candidateStartAt);
                if (!next.HasValue)
                {
                    break;
                }

                int end = next.Value.Start + next.Value.Length;
                if ((!lineRegexp || IsLineMatch(bytes, next.Value.Start, end)) &&
                    (!wordRegexp || IsWordMatch(bytes, next.Value.Start, end)))
                {
                    candidate = next.Value;
                    break;
                }

                candidateStartAt = MatchIterator.AdvanceAfter(new MatcherMatch(next.Value.Start, next.Value.Length), bytes.Length);
            }

            if (candidate.HasValue &&
                (!found ||
                candidate.Value.Start < match.Start ||
                (candidate.Value.Start == match.Start && candidate.Value.Length > match.Length)))
            {
                match = candidate.Value;
                found = true;
            }
        }

        return found;
    }

    private static List<ContextLineInfo> BuildMultilineContextLines(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes[lineStart..], nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        long[] matchColumns = new long[physicalLines.Count];
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            MarkMultilineContextMatch(bytes, physicalLines, matchedLines, matchColumns, match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        var lines = new List<ContextLineInfo>(physicalLines.Count);
        for (int index = 0; index < physicalLines.Count; index++)
        {
            ContextLineInfo line = physicalLines[index];
            lines.Add(new ContextLineInfo(line.Start, line.Length, line.LineNumber, matchedLines[index], matchColumns[index], matchedLines[index], contextColumn: 0));
        }

        return lines;
    }

    private static List<ContextLineInfo> BuildMultilineInvertedLines(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes[lineStart..], nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            MarkMultilineMatchedLines(bytes, physicalLines, matchedLines, match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
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

    private static void MarkMultilineContextMatch(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, long[] matchColumns, RegexMatch match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
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

    private static void MarkMultilineMatchedLines(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, RegexMatch match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
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

    private static long CountSelectedMultilineLines(List<ContextLineInfo> lines, ulong? maxCount)
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

    private static int GetInclusiveMatchEnd(RegexMatch match)
    {
        return match.Length == 0 ? match.Start : match.Start + match.Length - 1;
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
        long count = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n')
            {
                count++;
            }
        }

        return count;
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
        out bool matched)
    {
        matched = false;
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
            return false;
        }

        if (passthru || beforeContext > 0 || afterContext > 0)
        {
            matched = LiteralLineSearcher.HasMatch(bytes.AsSpan(0, binaryDetection.Offset), pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf);
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

    private static int GetBinarySafePrefixLength(byte[] bytes, int binaryOffset)
    {
        if (binaryOffset <= BinaryDetectionInitialBufferLength)
        {
            return 0;
        }

        int length = Math.Min(binaryOffset, BinaryDetectionInitialBufferLength);
        int lastLineFeed = bytes.AsSpan(0, length).LastIndexOf((byte)'\n');
        return lastLineFeed < 0 ? 0 : lastLineFeed + 1;
    }

    private static byte[] GetBinaryConvertedSearchBytes(byte[] bytes, bool textMode, bool nullData)
    {
        return BinaryDetection.GetSearchBytes(bytes, textMode, nullData);
    }

    private static void WriteBinaryFileMatches(RawByteWriter output, OutputPath? prefix, OutputColor color, int binaryOffset)
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

    private static void WriteBinaryFileStoppedWarning(RawByteWriter output, OutputPath? prefix, OutputColor color, int binaryOffset)
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
        byte[] searchBytes = GetBinaryConvertedSearchBytes(bytes, textMode, nullData);
        var writer = new JsonFileWriter(output, path, quiet, binaryOffset);
        bool matched = multiline && (nullData || PatternPreparation.ShouldUseJsonMultilineRegex(pattern, multilineDotall)) && TrySearchJsonMultilineBytes(searchBytes, pattern, writer, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multilineDotall, crlf, nullData, replacement, maxCount, beforeContext, afterContext, passthru, out bool multilineMatched)
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
        out bool matched)
    {
        matched = false;
        if ((replacement is not null && !invertMatch) ||
            crlf ||
            beforeContext > 0 ||
            afterContext > 0 ||
            passthru)
        {
            return false;
        }

        if (bytes.IsEmpty)
        {
            return true;
        }

        if (invertMatch)
        {
            matched = WriteJsonMultilineInvertedMatches(bytes, patterns, writer, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        int groupStart = -1;
        int groupEnd = -1;
        int groupLastLineStart = -1;
        var matches = new List<JsonMatchSpan>(capacity: 1);
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
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
                matches.Add(new JsonMatchSpan(match.Start - groupStart, match.Start - groupStart + match.Length, replacement: null));
            }

            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                WriteJsonMultilineMatchGroup(bytes, writer, groupStart, groupEnd, groupLastLineStart, matches, nullData);
                return true;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        if (groupStart >= 0)
        {
            WriteJsonMultilineMatchGroup(bytes, writer, groupStart, groupEnd, groupLastLineStart, matches, nullData);
        }

        return true;
    }

    private static int GetJsonMultilineMatchLastLineStart(ReadOnlySpan<byte> bytes, RegexMatch match, bool nullData)
    {
        int lastLineStart = GetJsonMultilineLineStart(bytes, GetInclusiveMatchEnd(match), nullData);
        if (match.Length != 0 || match.Start >= bytes.Length)
        {
            return lastLineStart;
        }

        int lineEnd = GetJsonMultilineLineEnd(bytes, lastLineStart, nullData);
        if (!IsJsonLineTerminatorStart(bytes, match.Start, nullData))
        {
            return lastLineStart;
        }

        int nextLineStart = GetNextLineStart(lineEnd, bytes.Length);
        return nextLineStart < bytes.Length ? nextLineStart : lastLineStart;
    }

    private static int GetJsonMultilineLineStart(ReadOnlySpan<byte> bytes, int offset, bool nullData)
    {
        return nullData ? GetTerminatedLineStart(bytes, offset, terminator: 0) : GetLineStart(bytes, offset);
    }

    private static int GetJsonMultilineLineEnd(ReadOnlySpan<byte> bytes, int lineStart, bool nullData)
    {
        return nullData ? GetTerminatedLineEnd(bytes, lineStart, terminator: 0) : GetLineEnd(bytes, lineStart);
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
        return nullData ? 1 + CountBytes(bytes[..Math.Clamp(lineStart, 0, bytes.Length)], 0) : GetLineNumber(bytes, lineStart);
    }

    private static long CountJsonMultilineLineTerminators(ReadOnlySpan<byte> bytes, bool nullData)
    {
        return nullData ? CountBytes(bytes, 0) : CountLineFeeds(bytes);
    }

    private static long CountBytes(ReadOnlySpan<byte> bytes, byte value)
    {
        long count = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == value)
            {
                count++;
            }
        }

        return count;
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
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
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

    private static bool SearchQuiet(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> pattern,
        CliSearchMode searchMode,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        bool crlf,
        bool nullData)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return !LiteralLineSearcher.HasMatch(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, crlf, nullData);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            return LiteralLineSearcher.CountMatches(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, crlf, nullData) > 0;
        }

        if (searchMode == CliSearchMode.Count)
        {
            return LiteralLineSearcher.CountMatchingLines(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, crlf, nullData) > 0;
        }

        return LiteralLineSearcher.HasMatch(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, crlf, nullData);
    }

}
