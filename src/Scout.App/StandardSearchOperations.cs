using System.Diagnostics;

namespace Scout;

internal static class StandardSearchOperations
{
    private static readonly byte[] NullByte = [0];
    private static readonly byte[] LineFeed = [(byte)'\n'];
    private static readonly byte[] CrlfLineTerminator = [(byte)'\r', (byte)'\n'];

    internal static int Run(
        IReadOnlyList<OsString> positional,
        int firstPathIndex,
        bool patternsReadFromStandardInput,
        CliLowArgs lowArgs,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        FileTypeMatcher searchFileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        Stream standardInput,
        bool standardInputIsReadable,
        bool standardOutputIsTerminal)
    {
        OutputSeparators separators = GetOutputSeparators(lowArgs);
        OutputLineLimit lineLimit = GetOutputLineLimit(lowArgs);
        OutputColor color = GetOutputColor(lowArgs);
        bool lineNumber = SearchOutputFormatting.EffectiveLineNumber(lowArgs, standardOutputIsTerminal, automaticLineNumberTarget: false);
        bool column = SearchOutputFormatting.EffectiveColumn(lowArgs);
        bool wroteHeadingOutput = false;
        bool matched = false;
        bool errored = false;
        bool stats = lowArgs.Stats && lowArgs.SearchMode != CliSearchMode.Json && lowArgs.MaxCount != 0;
        long statsStarted = Stopwatch.GetTimestamp();
        SearchStats searchStats = default;
        bool useDefaultCurrentDirectory = positional.Count == firstPathIndex &&
            (patternsReadFromStandardInput || !standardInputIsReadable);
        int explicitPathArgumentCount = positional.Count - firstPathIndex;
        if (positional.Count == firstPathIndex && !useDefaultCurrentDirectory)
        {
            bool stdinHeading = ShouldUseHeading(lowArgs, standardOutputIsTerminal, autoPrefixPath: false);
            matched = stats
                ? StandardSearchTargetOperations.SearchStandardInputWithStats(patterns, standardInput, output, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, false, lineNumber, column, lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.WithFilename, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, stdinHeading, ref wroteHeadingOutput, ref searchStats)
                : StandardSearchTargetOperations.SearchStandardInput(patterns, standardInput, output, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, false, lineNumber, column, lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.WithFilename, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, stdinHeading, ref wroteHeadingOutput);
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

        lineNumber = SearchOutputFormatting.EffectiveLineNumber(
            lowArgs,
            standardOutputIsTerminal,
            SearchOutputFormatting.ShouldUseAutomaticLineNumberTarget(useDefaultCurrentDirectory, explicitPathArgumentCount, paths));
        bool prefixPaths = lowArgs.Vimgrep || paths.Count > 1 || SearchPathArgument.ContainsDirectory(paths);
        bool autoMmapEligible = SearchPathArgument.IsAutoMmapEligible(paths);
        bool pathHeading = ShouldUseHeading(lowArgs, standardOutputIsTerminal, prefixPaths);
        bool interPathContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, pathHeading, separators);
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
                    StandardSearchTargetOperations.SearchPathWithStats(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, writer, diagnostics, logger, asciiCaseInsensitive, lineNumber, pathHeading, ref wroteHeadingOutput, ref pathMatched, ref pathErrored, ref pathStats);
                    searchStats.Add(pathStats);
                }
                else
                {
                    StandardSearchTargetOperations.SearchPath(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, writer, diagnostics, logger, asciiCaseInsensitive, lineNumber, pathHeading, ref wroteHeadingOutput, ref pathMatched, ref pathErrored);
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
                StandardSearchTargetOperations.SearchPathWithStats(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, pathHeading, ref wroteHeadingOutput, ref matched, ref errored, ref searchStats);
            }
            else
            {
                StandardSearchTargetOperations.SearchPath(paths[index], patterns, standardInput, defaultRoot, prefixPaths, paths.Count > 1, autoMmapEligible, lowArgs, separators, lineLimit, color, searchFileTypes!, output, diagnostics, logger, asciiCaseInsensitive, lineNumber, pathHeading, ref wroteHeadingOutput, ref matched, ref errored);
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

    internal static bool ShouldWriteInterFileContextSeparator(CliLowArgs lowArgs, bool heading, OutputSeparators separators)
    {
        return !heading &&
            separators.ContextEnabled &&
            lowArgs.SearchMode == CliSearchMode.Standard &&
            !lowArgs.Passthru &&
            (lowArgs.BeforeContext > 0 || lowArgs.AfterContext > 0);
    }

    internal static void WriteInterFileContextSeparatorIfNeeded(RawByteWriter output, OutputSeparators separators, ref bool wroteContextBody)
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

    private static bool ShouldUseHeading(CliLowArgs lowArgs, bool standardOutputIsTerminal, bool autoPrefixPath)
    {
        if (lowArgs.Vimgrep || lowArgs.Quiet || lowArgs.SearchMode != CliSearchMode.Standard)
        {
            return false;
        }

        if (lowArgs.HeadingSpecified)
        {
            return lowArgs.Heading;
        }

        return standardOutputIsTerminal && (lowArgs.WithFilename ?? autoPrefixPath);
    }
}
