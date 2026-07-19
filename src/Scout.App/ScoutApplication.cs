
namespace Scout;

/// <summary>
/// Coordinates command-line parsing, search-plan construction, and operation dispatch.
/// </summary>
internal static class ScoutApplication
{
    internal static int Run(ReadOnlySpan<OsString> arguments, RawByteWriter output, RawByteWriter error)
    {
        using Stream standardInput = RawStandardStreams.OpenInput();
        return Run(arguments, output, error, standardInput, StandardInputProbe.IsReadable(), standardOutputIsTerminal: false, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        bool standardOutputIsTerminal)
    {
        using Stream standardInput = RawStandardStreams.OpenInput();
        return Run(arguments, output, error, standardInput, StandardInputProbe.IsReadable(), standardOutputIsTerminal, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        string? configPath)
    {
        using Stream standardInput = RawStandardStreams.OpenInput();
        return Run(arguments, output, error, standardInput, StandardInputProbe.IsReadable(), standardOutputIsTerminal: false, configPath, useConfigPathOverride: true);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput)
    {
        return Run(arguments, output, error, standardInput, standardInputIsReadable: true, standardOutputIsTerminal: false, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        bool standardOutputIsTerminal)
    {
        return Run(arguments, output, error, standardInput, standardInputIsReadable: true, standardOutputIsTerminal, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        string? configPath)
    {
        return Run(arguments, output, error, standardInput, standardInputIsReadable: true, standardOutputIsTerminal: false, configPath, useConfigPathOverride: true);
    }

    private static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        bool standardInputIsReadable,
        bool standardOutputIsTerminal,
        string? configPathOverride,
        bool useConfigPathOverride)
    {
        try
        {
            return RunCore(
                arguments,
                output,
                error,
                standardInput,
                standardInputIsReadable,
                standardOutputIsTerminal,
                configPathOverride,
                useConfigPathOverride);
        }
        catch (IOException exception) when (RawStandardStreams.IsBrokenPipe(exception))
        {
            return ExitCode.Success;
        }
    }

    private static int RunCore(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        bool standardInputIsReadable,
        bool standardOutputIsTerminal,
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
        if (parserArguments.Length == 0)
        {
            return RunSpecial(CliSpecialMode.HelpShort, output);
        }

        CliParseResult parseResult = CliParser.Parse(parserArguments);

        if (parseResult.Status == CliParseStatus.Error)
        {
            diagnostics.ErrorMessage(parseResult.Error!.WithContext(ScoutErrorContext.ProgramContext()));
            return ExitCode.Error;
        }

        if (parseResult.Status == CliParseStatus.Special)
        {
            return RunSpecial(parseResult.SpecialMode, output);
        }

        return RunSearch(parseResult.LowArgs!, output, diagnostics, standardInput, standardInputIsReadable, standardOutputIsTerminal);
    }

    private static bool TextEquals(OsString argument, string expected)
    {
        return argument.TryGetText(out string text) && string.Equals(text, expected, StringComparison.Ordinal);
    }

    private static bool IsStandardInputArgument(OsString argument)
    {
        return argument.EqualsUnixBytes("-"u8) || TextEquals(argument, "-");
    }

    private static bool TryAddInlinePattern(OsString pattern, List<byte[]> patterns, DiagnosticMessenger diagnostics)
    {
        if (!PatternPreparation.TryGetPatternBytes(pattern, out byte[] bytes))
        {
            diagnostics.ErrorMessage(new ScoutError("pattern given is not valid UTF-8").WithContext(ScoutErrorContext.ProgramContext()));
            return false;
        }

        patterns.Add(bytes);
        return true;
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
        bool standardInputIsReadable,
        bool standardOutputIsTerminal)
    {
        if (!RegexSpecializationModeEnvironment.TryResolve(out RegexSpecializationMode specializationMode, out ScoutError? specializationModeError))
        {
            diagnostics.ErrorMessage(specializationModeError!.WithContext(ScoutErrorContext.ProgramContext()));
            return ExitCode.Error;
        }

        using RegexSpecializationModeScope specializationScope = RegexSpecializationModeDefaults.Use(specializationMode);
        output.SetBufferMode(OutputBuffering.Resolve(lowArgs.BufferMode, standardOutputIsTerminal));
        lowArgs.SetColorMode(TerminalColor.Resolve(lowArgs.ColorMode, standardOutputIsTerminal));
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
                diagnostics.ErrorMessage(error!.WithContext(ScoutErrorContext.ProgramContext()));
                return ExitCode.Error;
            }

            return FileListingOperations.Run(positional, lowArgs, fileTypes!, output, diagnostics, logger);
        }

        var patterns = new List<byte[]>();
        int firstPathIndex = 0;
        bool patternsReadFromStandardInput = false;
        if (lowArgs.PatternSources.Count == 0)
        {
            if (positional.Count == 0)
            {
                diagnostics.ErrorMessage(new ScoutError("scout requires at least one pattern to execute a search").WithContext(ScoutErrorContext.ProgramContext()));
                return ExitCode.Error;
            }

            if (!TryAddInlinePattern(positional[0], patterns, diagnostics))
            {
                return ExitCode.Error;
            }

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
                    if (!TryAddInlinePattern(source.Value, patterns, diagnostics))
                    {
                        return ExitCode.Error;
                    }
                }
            }
        }

        RegexEnginePlan? enginePlan = null;
        try
        {
            if (!RegexEnginePlanner.TryCreate(patterns, lowArgs, out enginePlan, out ScoutError? engineError))
            {
                diagnostics.ErrorMessage(engineError!.WithContext(ScoutErrorContext.ProgramContext()));
                return ExitCode.Error;
            }

            RegexEnginePlan selectedEnginePlan = enginePlan!;
            if (selectedEnginePlan.UsesPcre2)
            {
                return Pcre2SearchOperations.Run(positional, firstPathIndex, patternsReadFromStandardInput, lowArgs, selectedEnginePlan.Pcre2Plan!, standardInput, standardInputIsReadable, standardOutputIsTerminal, output, diagnostics, logger);
            }

            if (!SearchWalkPlanning.TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? searchFileTypes, out ScoutError? searchError))
            {
                diagnostics.ErrorMessage(searchError!.WithContext(ScoutErrorContext.ProgramContext()));
                return ExitCode.Error;
            }

            List<byte[]> preparedPatterns = selectedEnginePlan.Patterns;
            RegexSearchPlan regexPlan = selectedEnginePlan.NativePlan!;
            bool asciiCaseInsensitive = selectedEnginePlan.AsciiCaseInsensitive;

            SearchDiagnosticLogging.LogSearchConfiguration(logger, positional, firstPathIndex, lowArgs, preparedPatterns);
            if (lowArgs.SearchMode == CliSearchMode.Json)
            {
                return JsonSearchOperations.Run(positional, firstPathIndex, patternsReadFromStandardInput, lowArgs, preparedPatterns, regexPlan, asciiCaseInsensitive, searchFileTypes!, output, diagnostics, logger, standardInput, standardInputIsReadable);
            }

            return StandardSearchOperations.Run(
                positional,
                firstPathIndex,
                patternsReadFromStandardInput,
                lowArgs,
                preparedPatterns,
                regexPlan,
                asciiCaseInsensitive,
                searchFileTypes!,
                output,
                diagnostics,
                logger,
                standardInput,
                standardInputIsReadable,
                standardOutputIsTerminal);
        }
        finally
        {
            enginePlan?.Dispose();
        }
    }
}
