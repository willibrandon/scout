using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Scout;

internal static class ScoutApplication
{
    private static readonly byte[] NullByte = [0];
    private static readonly byte[] LineFeed = [(byte)'\n'];
    private static readonly byte[] CrlfLineTerminator = [(byte)'\r', (byte)'\n'];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private const int BinaryDetectionInitialBufferLength = 65_536;

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

            return FileListingOperations.Run(positional, lowArgs, fileTypes!, output, diagnostics);
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
            return JsonSearchOperations.Run(positional, firstPathIndex, patternsReadFromStandardInput, lowArgs, patterns, asciiCaseInsensitive, searchFileTypes!, output, diagnostics, standardInput, standardInputIsReadable);
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

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
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

        SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path, multiplePaths));
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
        if (LargeFileSearchOperations.TrySearch(
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
            MultilineSearchOperations.TrySearchBytes(
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
            return SearchModeEvaluation.SearchQuiet(searchSpan, pattern, searchMode, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
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

}
