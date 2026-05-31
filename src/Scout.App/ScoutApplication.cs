using System;
using System.Collections.Generic;
using System.IO;

namespace Scout;

internal static class ScoutApplication
{
    internal static int Run(ReadOnlySpan<OsString> arguments, RawByteWriter output, RawByteWriter error)
    {
        using Stream standardInput = Console.OpenStandardInput();
        return Run(arguments, output, error, standardInput, StandardInputProbe.IsReadable(), standardOutputIsTerminal: false, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        bool standardOutputIsTerminal)
    {
        using Stream standardInput = Console.OpenStandardInput();
        return Run(arguments, output, error, standardInput, StandardInputProbe.IsReadable(), standardOutputIsTerminal, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        string? configPath)
    {
        using Stream standardInput = Console.OpenStandardInput();
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

        return StandardSearchOperations.Run(
            positional,
            firstPathIndex,
            patternsReadFromStandardInput,
            lowArgs,
            patterns,
            asciiCaseInsensitive,
            searchFileTypes!,
            output,
            diagnostics,
            logger,
            standardInput,
            standardInputIsReadable);
    }
}
