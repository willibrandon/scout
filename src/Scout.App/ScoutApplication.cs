using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const int BinaryDetectionInitialBufferLength = 65_536;
    private const ulong RegexCompiledBaseSize = 64;
    private const ulong RegexCompiledByteSize = 16;
    private const ulong RegexCompiledUnicodeDigitClassSize = 2_048;
    private const ulong RegexCompiledUnicodeNegatedDigitClassSize = 16_384;
    private const ulong RegexCompiledUnicodeWordClassSize = 16_384;
    private const ulong RegexCompiledUnicodeWhitespaceClassSize = 512;
    private const ulong RegexCompiledUnicodeNegatedWhitespaceClassSize = 2_048;

    internal static int Run(ReadOnlySpan<OsString> arguments, RawByteWriter output, RawByteWriter error)
    {
        using Stream standardInput = Console.OpenStandardInput();
        return Run(arguments, output, error, standardInput, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        string? configPath)
    {
        using Stream standardInput = Console.OpenStandardInput();
        return Run(arguments, output, error, standardInput, configPath, useConfigPathOverride: true);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput)
    {
        return Run(arguments, output, error, standardInput, configPathOverride: null, useConfigPathOverride: false);
    }

    internal static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        string? configPath)
    {
        return Run(arguments, output, error, standardInput, configPath, useConfigPathOverride: true);
    }

    private static int Run(
        ReadOnlySpan<OsString> arguments,
        RawByteWriter output,
        RawByteWriter error,
        Stream standardInput,
        string? configPathOverride,
        bool useConfigPathOverride)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(standardInput);

        var diagnostics = new DiagnosticMessenger(error);
        DiagnosticLogger earlyLogger = new(diagnostics, DetectLoggingMode(arguments[1..]));
        OsString[]? expandedArguments = BuildConfiguredArguments(arguments, diagnostics, earlyLogger, configPathOverride, useConfigPathOverride);
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

        return RunSearch(parseResult.LowArgs!, output, diagnostics, standardInput);
    }

    private static OsString[]? BuildConfiguredArguments(
        ReadOnlySpan<OsString> arguments,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        string? configPathOverride,
        bool useConfigPathOverride)
    {
        ReadOnlySpan<OsString> commandArguments = arguments[1..];
        if (HasSpecialArgument(commandArguments))
        {
            return null;
        }

        if (HasNoConfigArgument(commandArguments))
        {
            logger.Debug("rg::flags::parse", "crates/core/flags/parse.rs", 89, "not reading config files because --no-config is present");
            return null;
        }

        string? configPath = useConfigPathOverride
            ? configPathOverride
            : Environment.GetEnvironmentVariable("RIPGREP_CONFIG_PATH");
        if (string.IsNullOrEmpty(configPath))
        {
            logger.Debug("rg::flags::config", "crates/core/flags/config.rs", 19, "RIPGREP_CONFIG_PATH environment variable is not set, therefore not reading any config file");
            logger.Debug("rg::flags::parse", "crates/core/flags/parse.rs", 97, "no extra arguments found from configuration file");
            return null;
        }

        List<OsString> configArguments = ReadConfigArguments(configPath, diagnostics);
        if (configArguments.Count == 0)
        {
            logger.Debug("rg::flags::parse", "crates/core/flags/parse.rs", 97, "no extra arguments found from configuration file");
            return null;
        }

        var expanded = new OsString[configArguments.Count + commandArguments.Length];
        for (int index = 0; index < configArguments.Count; index++)
        {
            expanded[index] = configArguments[index];
        }

        for (int index = 0; index < commandArguments.Length; index++)
        {
            expanded[configArguments.Count + index] = commandArguments[index];
        }

        return expanded;
    }

    private static CliLoggingMode? DetectLoggingMode(ReadOnlySpan<OsString> arguments)
    {
        CliLoggingMode? mode = null;
        for (int index = 0; index < arguments.Length; index++)
        {
            OsString argument = arguments[index];
            if (argument.EqualsUnixBytes("--debug"u8) || TextEquals(argument, "--debug"))
            {
                mode = CliLoggingMode.Debug;
            }
            else if (argument.EqualsUnixBytes("--trace"u8) || TextEquals(argument, "--trace"))
            {
                mode = CliLoggingMode.Trace;
            }
        }

        return mode;
    }

    private static bool HasNoConfigArgument(ReadOnlySpan<OsString> arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            OsString argument = arguments[index];
            if (argument.EqualsUnixBytes("--no-config"u8) ||
                TextEquals(argument, "--no-config"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSpecialArgument(ReadOnlySpan<OsString> arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            if (IsSpecialArgument(arguments[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSpecialArgument(OsString argument)
    {
        return argument.EqualsUnixBytes("-h"u8) ||
            TextEquals(argument, "-h") ||
            argument.EqualsUnixBytes("--help"u8) ||
            TextEquals(argument, "--help") ||
            argument.EqualsUnixBytes("-V"u8) ||
            TextEquals(argument, "-V") ||
            argument.EqualsUnixBytes("--version"u8) ||
            TextEquals(argument, "--version") ||
            argument.EqualsUnixBytes("--pcre2-version"u8) ||
            TextEquals(argument, "--pcre2-version");
    }

    private static List<OsString> ReadConfigArguments(string configPath, DiagnosticMessenger diagnostics)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(configPath);
        }
        catch (FileNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {configPath}: No such file or directory (os error 2)").WithContext("rg"));
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {configPath}: No such file or directory (os error 2)").WithContext("rg"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {exception.Message}").WithContext("rg"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {exception.Message}").WithContext("rg"));
            return [];
        }

        var configArguments = new List<OsString>();
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line;
            if (lineFeed < 0)
            {
                line = remaining;
                lineStart = bytes.Length;
            }
            else
            {
                line = remaining[..lineFeed];
                lineStart += lineFeed + 1;
            }

            line = TrimAsciiWhitespace(line);
            if (line.IsEmpty || line[0] == (byte)'#')
            {
                continue;
            }

            configArguments.Add(OperatingSystem.IsWindows()
                ? OsString.FromText(Utf8.GetString(line))
                : OsString.FromUnixBytes(line));
        }

        return configArguments;
    }

    private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> bytes)
    {
        int start = 0;
        int end = bytes.Length;
        while (start < end && IsAsciiWhitespace(bytes[start]))
        {
            start++;
        }

        while (end > start && IsAsciiWhitespace(bytes[end - 1]))
        {
            end--;
        }

        return bytes[start..end];
    }

    private static bool IsAsciiWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';
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

    private static int RunSearch(CliLowArgs lowArgs, RawByteWriter output, DiagnosticMessenger diagnostics, Stream standardInput)
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
            return RunTypeList(lowArgs, output, diagnostics);
        }

        if (!TryValidateOverrideGlobs(lowArgs, diagnostics))
        {
            return ExitCode.Error;
        }

        if (lowArgs.SearchMode == CliSearchMode.Files)
        {
            if (!TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? fileTypes, out ScoutError? error))
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

            patterns.Add(GetPatternBytes(positional[0]));
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
                    if (!TryLoadPatternFile(source.Value, patterns, standardInput, diagnostics))
                    {
                        return ExitCode.Error;
                    }
                }
                else
                {
                    patterns.Add(GetPatternBytes(source.Value));
                }
            }
        }

        if (lowArgs.RegexEngine == CliRegexEngine.Pcre2)
        {
            return RunPcre2Search(positional, firstPathIndex, lowArgs, patterns, output, diagnostics);
        }

        if (!lowArgs.Multiline && ContainsLineTerminator(patterns, lowArgs.NullData, lowArgs.FixedStrings))
        {
            diagnostics.ErrorMessage(new ScoutError(BuildLineTerminatorPatternError(lowArgs.NullData)).WithContext("rg"));
            return ExitCode.Error;
        }

        if (!lowArgs.TextMode && !lowArgs.NullData && !lowArgs.FixedStrings && ContainsRegexNulLiteral(patterns))
        {
            diagnostics.ErrorMessage(new ScoutError("pattern contains \"\\0\" but it is impossible to match\n\nConsider enabling text mode with the --text flag (or -a for short). Otherwise,\nbinary detection is enabled and matching a NUL byte is impossible.").WithContext("rg"));
            return ExitCode.Error;
        }

        if (lowArgs.FixedStrings)
        {
            EscapeFixedStringPatterns(patterns);
        }
        else
        {
            if (!TryValidateRegexRepetitionExpressions(patterns, diagnostics))
            {
                return ExitCode.Error;
            }

            WrapRegexPatterns(patterns);
        }

        if (!TryValidateRegexSizeLimit(patterns, lowArgs, diagnostics))
        {
            return ExitCode.Error;
        }

        if (!TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? searchFileTypes, out ScoutError? searchError))
        {
            diagnostics.ErrorMessage(searchError!.WithContext("rg"));
            return ExitCode.Error;
        }

        bool asciiCaseInsensitive = IsAsciiCaseInsensitive(patterns, lowArgs.CaseMode);
        if (!lowArgs.Unicode && asciiCaseInsensitive)
        {
            WrapNonAsciiPatterns(patterns);
        }

        LogSearchConfiguration(logger, positional, firstPathIndex, lowArgs, patterns);
        if (lowArgs.SearchMode == CliSearchMode.Json)
        {
            return RunJsonSearch(positional, firstPathIndex, patternsReadFromStandardInput, lowArgs, patterns, asciiCaseInsensitive, searchFileTypes!, output, diagnostics, standardInput);
        }

        OutputSeparators separators = GetOutputSeparators(lowArgs);
        OutputLineLimit lineLimit = GetOutputLineLimit(lowArgs);
        OutputColor color = GetOutputColor(lowArgs);
        bool heading = ShouldUseHeading(lowArgs);
        bool lineNumber = EffectiveLineNumber(lowArgs);
        bool column = EffectiveColumn(lowArgs);
        bool wroteHeadingOutput = false;
        bool matched = false;
        bool errored = false;
        bool stats = lowArgs.Stats && lowArgs.MaxCount != 0;
        long statsStarted = Stopwatch.GetTimestamp();
        SearchStats searchStats = default;
        bool useDefaultCurrentDirectory = positional.Count == firstPathIndex && patternsReadFromStandardInput;
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

        var paths = new List<string>(positional.Count - firstPathIndex);
        if (useDefaultCurrentDirectory)
        {
            paths.Add(".");
        }

        for (int index = firstPathIndex; index < positional.Count; index++)
        {
            if (TryGetPathText(positional[index], diagnostics, out string path))
            {
                paths.Add(path);
            }
            else
            {
                errored = true;
            }
        }

        bool prefixPaths = lowArgs.Vimgrep || paths.Count > 1 || ContainsDirectory(paths);
        bool autoMmapEligible = IsAutoMmapEligible(paths);
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
        return GetSearchExitCode(matched, errored, lowArgs.Quiet);
    }

    private static int RunPcre2Search(
        IReadOnlyList<OsString> positional,
        int firstPathIndex,
        CliLowArgs lowArgs,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        DiagnosticMessenger diagnostics)
    {
        if (!Pcre2Library.IsAvailable)
        {
            diagnostics.ErrorMessage(new ScoutError(Pcre2Library.UnavailableErrorMessage).WithContext("rg"));
            return ExitCode.Error;
        }

        if (!CanRunSimplePcre2Search(positional, firstPathIndex, lowArgs))
        {
            diagnostics.ErrorMessage(new ScoutError("PCRE2 search currently supports standard single-file searches only").WithContext("rg"));
            return ExitCode.Error;
        }

        if (!TryGetPathText(positional[firstPathIndex], diagnostics, out string path))
        {
            return ExitCode.Error;
        }

        if (!File.Exists(path))
        {
            diagnostics.ErrorMessage(MissingPathError(path, simple: true));
            return ExitCode.Error;
        }

        OutputSeparators separators = GetOutputSeparators(lowArgs);
        bool autoMmapEligible = IsAutoMmapEligible([path]);
        if (!TryReadSearchFileBytes(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out _))
        {
            return ExitCode.Error;
        }

        try
        {
            byte[] pattern = BuildPcre2Pattern(patterns);
            using var regex = new Pcre2Regex(pattern, GetPcre2CompileOptions(lowArgs, patterns));
            bool matched = SearchPcre2Lines(bytes, regex, output, separators);
            output.Flush();
            return matched ? ExitCode.Success : ExitCode.NoMatch;
        }
        catch (Pcre2Exception exception)
        {
            diagnostics.ErrorMessage(new ScoutError(exception.Message).WithContext("rg"));
            return ExitCode.Error;
        }
    }

    private static bool CanRunSimplePcre2Search(IReadOnlyList<OsString> positional, int firstPathIndex, CliLowArgs lowArgs)
    {
        return lowArgs.SearchMode == CliSearchMode.Standard &&
            positional.Count == firstPathIndex + 1 &&
            !lowArgs.Stats &&
            !lowArgs.Quiet &&
            !lowArgs.FixedStrings &&
            !lowArgs.Multiline &&
            !lowArgs.LineRegexp &&
            !lowArgs.WordRegexp &&
            !lowArgs.Vimgrep &&
            !lowArgs.ByteOffset &&
            !lowArgs.Column &&
            !lowArgs.LineNumber &&
            !lowArgs.InvertMatch &&
            !lowArgs.OnlyMatching &&
            lowArgs.Replacement is null &&
            lowArgs.MaxCount is null &&
            lowArgs.BeforeContext == 0 &&
            lowArgs.AfterContext == 0 &&
            !lowArgs.Passthru &&
            lowArgs.WithFilename is not true;
    }

    private static Pcre2CompileOptions GetPcre2CompileOptions(CliLowArgs lowArgs, IReadOnlyList<byte[]> patterns)
    {
        Pcre2CompileOptions options = Pcre2CompileOptions.MultiLine;
        if (IsAsciiCaseInsensitive(patterns, lowArgs.CaseMode))
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

    private static bool SearchPcre2Lines(byte[] bytes, Pcre2Regex regex, RawByteWriter output, OutputSeparators separators)
    {
        bool matched = false;
        int lineStart = 0;
        while (lineStart <= bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            int lineEnd = lineFeed < 0 ? bytes.Length : lineStart + lineFeed;
            int outputEnd = lineFeed < 0 ? lineEnd : lineEnd + 1;
            ReadOnlySpan<byte> line = bytes.AsSpan(lineStart, lineEnd - lineStart);
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (regex.TryFind(line, out _))
            {
                output.Write(bytes.AsSpan(lineStart, outputEnd - lineStart));
                if (lineFeed < 0)
                {
                    output.Write(separators.LineTerminator.Span);
                }

                matched = true;
            }

            if (lineFeed < 0)
            {
                break;
            }

            lineStart = outputEnd;
        }

        return matched;
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
        Stream standardInput)
    {
        if (lowArgs.MaxCount == 0)
        {
            output.Flush();
            return ExitCode.NoMatch;
        }

        var summary = new JsonSearchSummary();
        bool matched = false;
        bool errored = false;
        bool useDefaultCurrentDirectory = positional.Count == firstPathIndex && patternsReadFromStandardInput;
        if (positional.Count == firstPathIndex && !useDefaultCurrentDirectory)
        {
            matched = SearchJsonStandardInput(pattern, standardInput, lowArgs, asciiCaseInsensitive, summary, output);
            summary.WriteSummary(output);
            output.Flush();
            return matched ? ExitCode.Success : ExitCode.NoMatch;
        }

        var paths = new List<string>(positional.Count - firstPathIndex);
        if (useDefaultCurrentDirectory)
        {
            paths.Add(".");
        }

        for (int index = firstPathIndex; index < positional.Count; index++)
        {
            if (TryGetPathText(positional[index], diagnostics, out string path))
            {
                paths.Add(path);
            }
            else
            {
                errored = true;
            }
        }

        bool autoMmapEligible = IsAutoMmapEligible(paths);
        for (int index = 0; index < paths.Count; index++)
        {
            bool defaultRoot = useDefaultCurrentDirectory && index == 0;
            SearchJsonPath(paths[index], pattern, standardInput, defaultRoot, paths.Count > 1, autoMmapEligible, lowArgs, fileTypes, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
        }

        summary.WriteSummary(output);
        output.Flush();
        return GetSearchExitCode(matched, errored, lowArgs.Quiet);
    }

    private static void LogSearchConfiguration(
        DiagnosticLogger logger,
        IReadOnlyList<OsString> positional,
        int firstPathIndex,
        CliLowArgs lowArgs,
        List<byte[]> patterns)
    {
        if (!logger.IsDebugEnabled)
        {
            return;
        }

        int pathCount = Math.Max(0, positional.Count - firstPathIndex);
        bool isOneFile = IsOneFileForLogging(positional, firstPathIndex);
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 954, $"read CWD from environment: {Directory.GetCurrentDirectory()}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1092, $"number of paths given to search: {pathCount}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1103, $"is_one_file? {FormatBool(isOneFile)}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1278, $"found hostname for hyperlink configuration: {GetLoggingHostname(lowArgs)}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1288, $"hyperlink format: \"{EscapeLogString(lowArgs.HyperlinkFormat ?? string.Empty)}\"");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 175, $"using {GetLoggingThreadCount(lowArgs, isOneFile)} thread(s)");
        LogGlobalIgnoreConfiguration(logger, lowArgs.RespectGitIgnoreFiles && lowArgs.RespectGlobalIgnoreFiles);
        if (patterns.Count > 0)
        {
            logger.Debug("grep_regex::config", "/Users/brandon/src/ripgrep/crates/regex/src/config.rs", 175, $"assembling HIR from {patterns.Count} fixed string literals");
            logger.Trace("grep_regex::matcher", "/Users/brandon/src/ripgrep/crates/regex/src/matcher.rs", 66, $"final regex: \"(?:{EscapeLogPattern(patterns[0])})\"");
            logger.Trace("grep_regex::literal", "crates/regex/src/literal.rs", 74, "skipping inner literal extraction, existing regex is believed to already be accelerated");
        }
    }

    private static bool IsOneFileForLogging(IReadOnlyList<OsString> positional, int firstPathIndex)
    {
        if (positional.Count - firstPathIndex != 1)
        {
            return false;
        }

        OsString path = positional[firstPathIndex];
        if (path.EqualsUnixBytes("-"u8) || TextEquals(path, "-"))
        {
            return true;
        }

        return path.TryGetText(out string text) && !Directory.Exists(text);
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string GetLoggingHostname(CliLowArgs lowArgs)
    {
        string program = string.IsNullOrEmpty(lowArgs.HostnameBin) ? "hostname" : lowArgs.HostnameBin;
        return TryRunHostnameCommand(program, out string host) ? host : string.Empty;
    }

    private static ulong GetLoggingThreadCount(CliLowArgs lowArgs, bool isOneFile)
    {
        return SearchThreadPlanner.Resolve(lowArgs.Threads, lowArgs.SortMode is not null, isOneFile);
    }

    private static void LogGlobalIgnoreConfiguration(DiagnosticLogger logger, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        string? globalIgnore = GlobalGitIgnore.ResolveFilePath();
        if (string.IsNullOrEmpty(globalIgnore) || !File.Exists(globalIgnore))
        {
            return;
        }

        logger.Debug("ignore::gitignore", "crates/ignore/src/gitignore.rs", 398, $"opened gitignore file: {globalIgnore}");
        logger.Debug("globset", "crates/globset/src/lib.rs", 515, "built glob set; 1 literals, 0 basenames, 0 extensions, 0 prefixes, 1 suffixes, 0 required extensions, 0 regexes");
    }

    private static string EscapeLogString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string EscapeLogPattern(byte[] pattern)
    {
        return EscapeLogString(Encoding.UTF8.GetString(pattern));
    }

    private static bool SearchJsonStandardInput(
        IReadOnlyList<byte[]> pattern,
        Stream standardInput,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive,
        JsonSearchSummary summary,
        RawByteWriter output)
    {
        byte[] bytes = ReadSearchStream(standardInput, lowArgs.EncodingMode);
        return SearchJsonBytes(bytes, pattern, output, StandardInputPath, summary, lowArgs.TextMode, lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
    }

    private static void SearchJsonPath(
        string path,
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
        if (path == "-")
        {
            matched |= SearchJsonStandardInput(pattern, standardInput, lowArgs, asciiCaseInsensitive, summary, output);
            return;
        }

        if (Directory.Exists(path))
        {
            int threadCount = GetSearchWalkThreadCount(lowArgs);
            if (threadCount > 1)
            {
                SearchJsonDirectoryParallel(path, pattern, defaultRoot, lowArgs, fileTypes, asciiCaseInsensitive, summary, output, diagnostics, threadCount, ref matched, ref errored);
                return;
            }

            string fullRoot = Path.GetFullPath(path);
            foreach (DirEntry entry in GetSortedFileEntries(path, lowArgs, fileTypes, diagnostics))
            {
                string displayPath = GetSearchDirectoryDisplayPath(path, fullRoot, entry.FullPath, defaultRoot);
                SearchJsonFile(entry.FullPath, Utf8.GetBytes(displayPath), pattern, false, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
            }

            return;
        }

        if (File.Exists(path))
        {
            SearchJsonFile(path, Utf8.GetBytes(path), pattern, autoMmapEligible, lowArgs, asciiCaseInsensitive, summary, output, diagnostics, ref matched, ref errored);
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
            CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
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
                string displayPath = GetSearchDirectoryDisplayPath(root, fullRoot, entry.FullPath, defaultRoot);
                SearchJsonFile(entry.FullPath, Utf8.GetBytes(displayPath), pattern, false, lowArgs, asciiCaseInsensitive, fileSummary, writer, diagnostics, ref fileMatched, ref fileErrored);
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
        if (!TryReadSearchFileBytes(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out _))
        {
            errored = true;
            return;
        }

        matched |= SearchJsonBytes(bytes, pattern, output, displayPath, summary, lowArgs.TextMode, lowArgs.Quiet, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.Crlf, lowArgs.NullData, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.StopOnNonmatch);
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
                if (TryGetPathText(positional[index], diagnostics, out string path))
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
        return GetSearchExitCode(emitted, errored, lowArgs.Quiet);
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
                WritePathTerminator(output, lowArgs.NullPathTerminator);
            }

            emitted = true;
            return;
        }

        if (File.Exists(path))
        {
            if (!lowArgs.Quiet)
            {
                output.Write(GetPathBytes(path, lowArgs.PathSeparator));
                WritePathTerminator(output, lowArgs.NullPathTerminator);
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
        int threadCount = GetFilesWalkThreadCount(lowArgs);
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
        foreach (DirEntry entry in GetSortedFileEntries(path, lowArgs, fileTypes, diagnostics))
        {
            string displayPath = defaultRoot
                ? Path.GetRelativePath(fullRoot, entry.FullPath)
                : GetDirectoryDisplayPath(path, fullRoot, entry.FullPath);
            if (!lowArgs.Quiet)
            {
                output.Write(GetPathBytes(displayPath, lowArgs.PathSeparator));
                WritePathTerminator(output, lowArgs.NullPathTerminator);
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
        int threadCount = GetFilesWalkThreadCount(lowArgs);
        using var entries = new BlockingCollection<DirEntry>();
        int found = 0;
        var printTask = Task.Run(() =>
        {
            foreach (DirEntry entry in entries.GetConsumingEnumerable())
            {
                string displayPath = defaultRoot
                    ? Path.GetRelativePath(fullRoot, entry.FullPath)
                    : GetDirectoryDisplayPath(path, fullRoot, entry.FullPath);
                output.Write(GetPathBytes(displayPath, lowArgs.PathSeparator));
                WritePathTerminator(output, lowArgs.NullPathTerminator);
            }
        });

        try
        {
            CreateWalkBuilder(path, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
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
        byte[] bytes = ReadSearchStream(standardInput, encodingMode);
        return SearchBytesWithOptionalHeading(bytes, pattern, output, GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput);
    }

    private static void SearchPath(
        string path,
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
        if (path == "-")
        {
            OutputPath? prefix = GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInput(pattern, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, EffectiveLineNumber(lowArgs), EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectory(path, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        if (File.Exists(path))
        {
            byte[] pathBytes = GetPathBytes(path, lowArgs.PathSeparator);
            OutputPath outputPath = CreateOutputPath(path, pathBytes, lowArgs, color);
            OutputPath? prefix = GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFile(path, pattern, lowArgs, false, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, EffectiveLineNumber(lowArgs), EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored);
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
        byte[] bytes = ReadSearchStream(standardInput, encodingMode);
        return SearchBytesWithStats(bytes, pattern, output, GetStandardInputPrefix(searchMode, autoPrefixPath, withFilename), separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multiline, multilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, stopOnNonmatch, false, heading, ref wroteHeadingOutput, ref stats);
    }

    private static void SearchPathWithStats(
        string path,
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
        if (path == "-")
        {
            OutputPath? prefix = GetStandardInputPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename);
            matched |= SearchStandardInputWithStats(pattern, standardInput, output, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, EffectiveLineNumber(lowArgs), EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.EncodingMode, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, lowArgs.StopOnNonmatch, heading, ref wroteHeadingOutput, ref stats);
            return;
        }

        if (Directory.Exists(path))
        {
            SearchDirectoryWithStats(path, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        if (File.Exists(path))
        {
            byte[] pathBytes = GetPathBytes(path, lowArgs.PathSeparator);
            OutputPath outputPath = CreateOutputPath(path, pathBytes, lowArgs, color);
            OutputPath? prefix = GetFileSearchPrefix(lowArgs.SearchMode, prefixPaths, lowArgs.WithFilename, outputPath);
            SearchFileWithStats(path, pattern, lowArgs, false, autoMmapEligible, output, diagnostics, logger, prefix, separators, lineLimit, color, lowArgs.SearchMode, lowArgs.Vimgrep, EffectiveLineNumber(lowArgs), EffectiveColumn(lowArgs), lowArgs.ByteOffset, asciiCaseInsensitive, lowArgs.InvertMatch, lowArgs.LineRegexp, lowArgs.WordRegexp, lowArgs.OnlyMatching, lowArgs.Replacement, lowArgs.MaxCount, lowArgs.TextMode, lowArgs.Quiet, lowArgs.Trim, lowArgs.BeforeContext, lowArgs.AfterContext, lowArgs.Passthru, lowArgs.IncludeZero, lowArgs.NullPathTerminator, heading, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
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
        byte[] bytes = ReadSearchStream(standardInput, encodingMode);
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
        byte[] bytes = ReadSearchStream(standardInput, encodingMode);
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
        int threadCount = GetSearchWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            SearchDirectoryParallel(root, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored);
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        bool interFileContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        foreach (DirEntry entry in GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics))
        {
            string displayPath = GetSearchDirectoryDisplayPath(root, fullRoot, entry.FullPath, defaultRoot);
            byte[] displayPathBytes = GetPathBytes(displayPath, lowArgs.PathSeparator);
            OutputPath outputPath = CreateOutputPath(entry.FullPath, displayPathBytes, lowArgs, color);
            if (interFileContextSeparator)
            {
                using MemoryStream buffer = new();
                var writer = new RawByteWriter(buffer);
                bool fileMatched = false;
                bool fileErrored = false;
                SearchFile(
                    entry.FullPath,
                    pattern,
                    lowArgs,
                    true,
                    false,
                    writer,
                    diagnostics,
                    logger,
                    GetFileSearchPrefix(lowArgs.SearchMode, true, lowArgs.WithFilename, outputPath),
                    separators,
                    lineLimit,
                    color,
                    lowArgs.SearchMode,
                    lowArgs.Vimgrep,
                    EffectiveLineNumber(lowArgs),
                    EffectiveColumn(lowArgs),
                    lowArgs.ByteOffset,
                    asciiCaseInsensitive,
                    lowArgs.InvertMatch,
                    lowArgs.LineRegexp,
                    lowArgs.WordRegexp,
                    lowArgs.OnlyMatching,
                    lowArgs.Replacement,
                    lowArgs.MaxCount,
                    lowArgs.TextMode,
                    lowArgs.Quiet,
                    lowArgs.Trim,
                    lowArgs.BeforeContext,
                    lowArgs.AfterContext,
                    lowArgs.Passthru,
                    lowArgs.IncludeZero,
                    lowArgs.NullPathTerminator,
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

            SearchFile(
                entry.FullPath,
                pattern,
                lowArgs,
                true,
                false,
                output,
                diagnostics,
                logger,
                GetFileSearchPrefix(lowArgs.SearchMode, true, lowArgs.WithFilename, outputPath),
                separators,
                lineLimit,
                color,
                lowArgs.SearchMode,
                lowArgs.Vimgrep,
                EffectiveLineNumber(lowArgs),
                EffectiveColumn(lowArgs),
                lowArgs.ByteOffset,
                asciiCaseInsensitive,
                lowArgs.InvertMatch,
                lowArgs.LineRegexp,
                lowArgs.WordRegexp,
                lowArgs.OnlyMatching,
                lowArgs.Replacement,
                lowArgs.MaxCount,
                lowArgs.TextMode,
                lowArgs.Quiet,
                lowArgs.Trim,
                lowArgs.BeforeContext,
                lowArgs.AfterContext,
                lowArgs.Passthru,
                lowArgs.IncludeZero,
                lowArgs.NullPathTerminator,
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
            CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
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
                string displayPath = GetSearchDirectoryDisplayPath(root, fullRoot, entry.FullPath, defaultRoot);
                byte[] displayPathBytes = GetPathBytes(displayPath, lowArgs.PathSeparator);
                OutputPath outputPath = CreateOutputPath(entry.FullPath, displayPathBytes, lowArgs, color);
                SearchFile(
                    entry.FullPath,
                    pattern,
                    lowArgs,
                    true,
                    false,
                    writer,
                    diagnostics,
                    logger,
                    GetFileSearchPrefix(lowArgs.SearchMode, true, lowArgs.WithFilename, outputPath),
                    separators,
                    lineLimit,
                    color,
                    lowArgs.SearchMode,
                    lowArgs.Vimgrep,
                    EffectiveLineNumber(lowArgs),
                    EffectiveColumn(lowArgs),
                    lowArgs.ByteOffset,
                    asciiCaseInsensitive,
                    lowArgs.InvertMatch,
                    lowArgs.LineRegexp,
                    lowArgs.WordRegexp,
                    lowArgs.OnlyMatching,
                    lowArgs.Replacement,
                    lowArgs.MaxCount,
                    lowArgs.TextMode,
                    lowArgs.Quiet,
                    lowArgs.Trim,
                    lowArgs.BeforeContext,
                    lowArgs.AfterContext,
                    lowArgs.Passthru,
                    lowArgs.IncludeZero,
                    lowArgs.NullPathTerminator,
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
        int threadCount = GetSearchWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            SearchDirectoryParallelWithStats(root, pattern, defaultRoot, lowArgs, separators, lineLimit, color, fileTypes, output, diagnostics, logger, asciiCaseInsensitive, heading, threadCount, ref wroteHeadingOutput, ref matched, ref errored, ref stats);
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        bool interFileContextSeparator = ShouldWriteInterFileContextSeparator(lowArgs, heading, separators);
        bool wroteContextBody = false;
        foreach (DirEntry entry in GetSortedFileEntries(root, lowArgs, fileTypes, diagnostics))
        {
            string displayPath = GetSearchDirectoryDisplayPath(root, fullRoot, entry.FullPath, defaultRoot);
            byte[] displayPathBytes = GetPathBytes(displayPath, lowArgs.PathSeparator);
            OutputPath outputPath = CreateOutputPath(entry.FullPath, displayPathBytes, lowArgs, color);
            if (interFileContextSeparator)
            {
                using MemoryStream buffer = new();
                var writer = new RawByteWriter(buffer);
                bool fileMatched = false;
                bool fileErrored = false;
                SearchFileWithStats(
                    entry.FullPath,
                    pattern,
                    lowArgs,
                    true,
                    false,
                    writer,
                    diagnostics,
                    logger,
                    GetFileSearchPrefix(lowArgs.SearchMode, true, lowArgs.WithFilename, outputPath),
                    separators,
                    lineLimit,
                    color,
                    lowArgs.SearchMode,
                    lowArgs.Vimgrep,
                    EffectiveLineNumber(lowArgs),
                    EffectiveColumn(lowArgs),
                    lowArgs.ByteOffset,
                    asciiCaseInsensitive,
                    lowArgs.InvertMatch,
                    lowArgs.LineRegexp,
                    lowArgs.WordRegexp,
                    lowArgs.OnlyMatching,
                    lowArgs.Replacement,
                    lowArgs.MaxCount,
                    lowArgs.TextMode,
                    lowArgs.Quiet,
                    lowArgs.Trim,
                    lowArgs.BeforeContext,
                    lowArgs.AfterContext,
                    lowArgs.Passthru,
                    lowArgs.IncludeZero,
                    lowArgs.NullPathTerminator,
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

            SearchFileWithStats(
                entry.FullPath,
                pattern,
                lowArgs,
                true,
                false,
                output,
                diagnostics,
                logger,
                GetFileSearchPrefix(lowArgs.SearchMode, true, lowArgs.WithFilename, outputPath),
                separators,
                lineLimit,
                color,
                lowArgs.SearchMode,
                lowArgs.Vimgrep,
                EffectiveLineNumber(lowArgs),
                EffectiveColumn(lowArgs),
                lowArgs.ByteOffset,
                asciiCaseInsensitive,
                lowArgs.InvertMatch,
                lowArgs.LineRegexp,
                lowArgs.WordRegexp,
                lowArgs.OnlyMatching,
                lowArgs.Replacement,
                lowArgs.MaxCount,
                lowArgs.TextMode,
                lowArgs.Quiet,
                lowArgs.Trim,
                lowArgs.BeforeContext,
                lowArgs.AfterContext,
                lowArgs.Passthru,
                lowArgs.IncludeZero,
                lowArgs.NullPathTerminator,
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
            CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
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
                string displayPath = GetSearchDirectoryDisplayPath(root, fullRoot, entry.FullPath, defaultRoot);
                byte[] displayPathBytes = GetPathBytes(displayPath, lowArgs.PathSeparator);
                OutputPath outputPath = CreateOutputPath(entry.FullPath, displayPathBytes, lowArgs, color);
                SearchFileWithStats(
                    entry.FullPath,
                    pattern,
                    lowArgs,
                    true,
                    false,
                    writer,
                    diagnostics,
                    logger,
                    GetFileSearchPrefix(lowArgs.SearchMode, true, lowArgs.WithFilename, outputPath),
                    separators,
                    lineLimit,
                    color,
                    lowArgs.SearchMode,
                    lowArgs.Vimgrep,
                    EffectiveLineNumber(lowArgs),
                    EffectiveColumn(lowArgs),
                    lowArgs.ByteOffset,
                    asciiCaseInsensitive,
                    lowArgs.InvertMatch,
                    lowArgs.LineRegexp,
                    lowArgs.WordRegexp,
                    lowArgs.OnlyMatching,
                    lowArgs.Replacement,
                    lowArgs.MaxCount,
                    lowArgs.TextMode,
                    lowArgs.Quiet,
                    lowArgs.Trim,
                    lowArgs.BeforeContext,
                    lowArgs.AfterContext,
                    lowArgs.Passthru,
                    lowArgs.IncludeZero,
                    lowArgs.NullPathTerminator,
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

    private static int RunTypeList(CliLowArgs lowArgs, RawByteWriter output, DiagnosticMessenger diagnostics)
    {
        if (!TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? fileTypes, out ScoutError? error))
        {
            diagnostics.ErrorMessage(error!.WithContext("rg"));
            return ExitCode.Error;
        }

        foreach (FileTypeDefinition definition in fileTypes!.Definitions)
        {
            output.Write(Utf8.GetBytes(definition.Name));
            output.Write(": "u8);
            for (int index = 0; index < definition.Globs.Count; index++)
            {
                if (index > 0)
                {
                    output.Write(", "u8);
                }

                output.Write(Utf8.GetBytes(definition.Globs[index]));
            }

            output.Write("\n"u8);
        }

        output.Flush();
        return ExitCode.Success;
    }

    private static WalkBuilder CreateWalkBuilder(string path, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics)
    {
        WalkBuilder builder = new WalkBuilder(path)
            .Hidden(!lowArgs.IncludeHidden)
            .FollowLinks(lowArgs.FollowLinks)
            .SameFileSystem(lowArgs.OneFileSystem)
            .MaxDepth(GetWalkMaxDepth(lowArgs.MaxDepth))
            .MaxFileSize(GetWalkMaxFileSize(lowArgs.MaxFileSize))
            .Overrides(BuildOverrides(lowArgs))
            .FileTypes(fileTypes)
            .Ignore(lowArgs.RespectDotIgnoreFiles)
            .GitIgnore(lowArgs.RespectGitIgnoreFiles)
            .GitExclude(lowArgs.RespectGitIgnoreFiles && lowArgs.RespectGitExcludeFiles)
            .GitGlobal(lowArgs.RespectGitIgnoreFiles && lowArgs.RespectGlobalIgnoreFiles)
            .Parents(lowArgs.RespectParentIgnoreFiles)
            .RequireGit(lowArgs.RequireGitRepository)
            .IgnoreCaseInsensitive(lowArgs.IgnoreFileCaseInsensitive);
        if (lowArgs.RespectExplicitIgnoreFiles)
        {
            for (int index = 0; index < lowArgs.IgnoreFiles.Count; index++)
            {
                if (!builder.TryAddIgnoreFile(lowArgs.IgnoreFiles[index], out string? errorMessage) && lowArgs.Messages)
                {
                    diagnostics.ErrorMessage(new ScoutError(errorMessage!).WithContext("rg"));
                }
            }
        }

        if (lowArgs.SortMode is { Reverse: false, Kind: CliSortKind.Path })
        {
            builder.SortByFileName();
        }

        return builder;
    }

    private static List<DirEntry> GetSortedFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics)
    {
        int threadCount = GetDirectoryWalkThreadCount(lowArgs);
        List<DirEntry> entries = threadCount > 1
            ? GetParallelFileEntries(root, lowArgs, fileTypes, diagnostics, threadCount)
            : GetSerialFileEntries(root, lowArgs, fileTypes, diagnostics);
        SortFileEntries(entries, lowArgs.SortMode);
        return entries;
    }

    private static List<DirEntry> GetSerialFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics)
    {
        List<DirEntry> entries = [];
        foreach (DirEntry entry in CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics).Build())
        {
            if (entry.IsFile)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static List<DirEntry> GetParallelFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, int threadCount)
    {
        List<DirEntry> entries = [];
        object entriesLock = new();
        CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
        {
            if (entry.IsFile)
            {
                lock (entriesLock)
                {
                    entries.Add(entry);
                }
            }

            return WalkState.Continue;
        });

        return entries;
    }

    private static int GetDirectoryWalkThreadCount(CliLowArgs lowArgs)
    {
        if (lowArgs.Threads is not ulong requestedThreads || requestedThreads <= 1)
        {
            return 1;
        }

        ulong resolvedThreads = SearchThreadPlanner.Resolve(requestedThreads, lowArgs.SortMode is not null, isOneFile: false);
        if (resolvedThreads <= 1)
        {
            return 1;
        }

        return resolvedThreads > int.MaxValue ? int.MaxValue : (int)resolvedThreads;
    }

    private static int GetFilesWalkThreadCount(CliLowArgs lowArgs)
    {
        ulong resolvedThreads = SearchThreadPlanner.Resolve(lowArgs.Threads, lowArgs.SortMode is not null, isOneFile: false);
        if (resolvedThreads <= 1)
        {
            return 1;
        }

        return resolvedThreads > int.MaxValue ? int.MaxValue : (int)resolvedThreads;
    }

    private static int GetSearchWalkThreadCount(CliLowArgs lowArgs)
    {
        ulong resolvedThreads = SearchThreadPlanner.Resolve(lowArgs.Threads, lowArgs.SortMode is not null, isOneFile: false);
        if (resolvedThreads <= 1)
        {
            return 1;
        }

        return resolvedThreads > int.MaxValue ? int.MaxValue : (int)resolvedThreads;
    }

    private static bool TryBuildFileTypeMatcher(CliLowArgs lowArgs, out FileTypeMatcher? fileTypes, out ScoutError? error)
    {
        FileTypeMatcherBuilder builder = new FileTypeMatcherBuilder().AddDefaults();
        try
        {
            for (int index = 0; index < lowArgs.TypeChanges.Count; index++)
            {
                CliTypeChange change = lowArgs.TypeChanges[index];
                ApplyTypeChange(builder, change);
            }

            fileTypes = builder.Build();
            error = null;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            fileTypes = null;
            error = new ScoutError(exception.Message);
            return false;
        }
        catch (ArgumentException)
        {
            fileTypes = null;
            error = new ScoutError("invalid definition (format is type:glob, e.g., html:*.html)");
            return false;
        }
    }

    private static void ApplyTypeChange(FileTypeMatcherBuilder builder, CliTypeChange change)
    {
        switch (change.Kind)
        {
            case CliTypeChangeKind.Select:
                builder.Select(change.Value);
                break;

            case CliTypeChangeKind.Negate:
                builder.Negate(change.Value);
                break;

            case CliTypeChangeKind.Add:
                builder.AddDefinition(change.Value);
                break;

            case CliTypeChangeKind.Clear:
                builder.Clear(change.Value);
                break;
        }
    }

    private static void SortFileEntries(List<DirEntry> entries, CliSortMode? sortMode)
    {
        if (sortMode is null || sortMode.Value is { Reverse: false, Kind: CliSortKind.Path })
        {
            return;
        }

        CliSortMode mode = sortMode.Value;
        if (mode.Kind == CliSortKind.Path)
        {
            entries.Sort((left, right) => ComparePath(left, right, mode.Reverse));
            return;
        }

        entries.Sort((left, right) => CompareTime(left, right, mode));
    }

    private static int ComparePath(DirEntry left, DirEntry right, bool reverse)
    {
        int comparison = StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
        return reverse ? -comparison : comparison;
    }

    private static int CompareTime(DirEntry left, DirEntry right, CliSortMode mode)
    {
        DateTime? leftTime = GetSortTime(left.FullPath, mode.Kind);
        DateTime? rightTime = GetSortTime(right.FullPath, mode.Kind);
        int comparison = CompareNullableTime(leftTime, rightTime);
        return mode.Reverse ? -comparison : comparison;
    }

    private static int CompareNullableTime(DateTime? left, DateTime? right)
    {
        if (left.HasValue && right.HasValue)
        {
            return left.Value.CompareTo(right.Value);
        }

        if (left.HasValue)
        {
            return -1;
        }

        return right.HasValue ? 1 : 0;
    }

    private static DateTime? GetSortTime(string path, CliSortKind kind)
    {
        try
        {
            var info = new FileInfo(path);
            return kind switch
            {
                CliSortKind.LastModified => info.LastWriteTimeUtc,
                CliSortKind.LastAccessed => info.LastAccessTimeUtc,
                CliSortKind.Created => info.CreationTimeUtc,
                _ => null,
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int? GetWalkMaxDepth(ulong? maxDepth)
    {
        if (maxDepth is null)
        {
            return null;
        }

        return maxDepth.Value > int.MaxValue ? int.MaxValue : (int)maxDepth.Value;
    }

    private static long? GetWalkMaxFileSize(ulong? maxFileSize)
    {
        if (maxFileSize is null)
        {
            return null;
        }

        return maxFileSize.Value > long.MaxValue ? long.MaxValue : (long)maxFileSize.Value;
    }

    private static Override BuildOverrides(CliLowArgs lowArgs)
    {
        if (lowArgs.GlobPatterns.Count == 0)
        {
            return Override.Empty;
        }

        var builder = new OverrideBuilder(Directory.GetCurrentDirectory());
        for (int index = 0; index < lowArgs.GlobPatterns.Count; index++)
        {
            CliGlobPattern pattern = lowArgs.GlobPatterns[index];
            builder.Add(pattern.Value, pattern.CaseInsensitive || lowArgs.GlobCaseInsensitive);
        }

        return builder.Build();
    }

    private static bool TryValidateOverrideGlobs(CliLowArgs lowArgs, DiagnosticMessenger diagnostics)
    {
        var builder = new OverrideBuilder(Directory.GetCurrentDirectory());
        for (int index = 0; index < lowArgs.GlobPatterns.Count; index++)
        {
            CliGlobPattern pattern = lowArgs.GlobPatterns[index];
            try
            {
                builder.Add(pattern.Value, pattern.CaseInsensitive || lowArgs.GlobCaseInsensitive);
            }
            catch (GlobParseException exception)
            {
                diagnostics.ErrorMessage(new ScoutError($"error parsing glob '{pattern.Value}': {exception.Message}").WithContext("rg"));
                return false;
            }
        }

        return true;
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
        if (!TryReadSearchFileBytes(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        LogTraceSearchPath(logger, path, readKind);
        matched |= SearchBytesWithOptionalHeading(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch), heading, ref wroteHeadingOutput);
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
        if (!TryReadSearchFileBytes(path, lowArgs, autoMmapEligible, diagnostics, out byte[] bytes, out SearchFileReadKind readKind))
        {
            errored = true;
            return;
        }

        LogTraceSearchPath(logger, path, readKind);
        matched |= SearchBytesWithStats(bytes, pattern, output, prefix, separators, lineLimit, color, searchMode, vimgrep, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, lowArgs.Multiline, lowArgs.MultilineDotall, onlyMatching, replacement, maxCount, textMode, quiet, trim, beforeContext, afterContext, passthru, includeZero, nullPathTerminator, lowArgs.StopOnNonmatch, ShouldQuitOnBinary(lowArgs, implicitSearch), heading, ref wroteHeadingOutput, ref stats);
    }

    private static void LogTraceSearchPath(DiagnosticLogger logger, string path, SearchFileReadKind readKind)
    {
        logger.Trace("rg::search", "crates/core/search.rs", 255, $"{path}: binary detection: BinaryDetection(Convert(0))");
        if (readKind == SearchFileReadKind.MemoryMapped)
        {
            logger.Trace("grep_searcher::searcher", "/Users/brandon/src/ripgrep/crates/searcher/src/searcher/mod.rs", 690, $"Some(\"{EscapeLogString(path)}\"): searching via memory map");
        }
        else
        {
            logger.Trace("grep_searcher::searcher", "/Users/brandon/src/ripgrep/crates/searcher/src/searcher/mod.rs", 711, $"Some(\"{EscapeLogString(path)}\"): searching using generic reader");
            logger.Trace("grep_searcher::searcher", "/Users/brandon/src/ripgrep/crates/searcher/src/searcher/mod.rs", 762, "generic reader: searching via roll buffer strategy");
        }

        logger.Trace("grep_searcher::searcher::core", "/Users/brandon/src/ripgrep/crates/searcher/src/searcher/core.rs", 67, "searcher core: will use fast line searcher");
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

    private static bool TryReadSearchFileBytes(
        string path,
        CliLowArgs lowArgs,
        bool autoMmapEligible,
        DiagnosticMessenger diagnostics,
        out byte[] bytes,
        out SearchFileReadKind readKind)
    {
        readKind = SearchFileReadKind.Buffered;
        if (!TryReadPreprocessedBytes(path, lowArgs, diagnostics, out bytes, out bool handled))
        {
            return false;
        }

        if (handled)
        {
            bytes = ApplySearchEncoding(bytes, lowArgs.EncodingMode);
            return true;
        }

        try
        {
            SearchFileReadResult result = SearchFileReader.Read(
                path,
                ToSearchEncodingKind(lowArgs.EncodingMode),
                ToSearchMmapMode(lowArgs.MmapMode),
                autoMmapEligible);
            bytes = result.GetBytes();
            readKind = result.Kind;
            return true;
        }
        catch (IOException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            bytes = [];
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            bytes = [];
            return false;
        }
    }

    private static bool TryReadPreprocessedBytes(
        string path,
        CliLowArgs lowArgs,
        DiagnosticMessenger diagnostics,
        out byte[] bytes,
        out bool handled)
    {
        bytes = [];
        handled = false;
        if (ShouldRunPreprocessor(path, lowArgs))
        {
            handled = true;
            if (TryRunSearchCommand(path, lowArgs.Preprocessor!, [path], pipeFileToStandardInput: true, fallbackOnStartError: false, out bytes, out ScoutError? error))
            {
                return true;
            }

            SearchErrorMessage(lowArgs, diagnostics, error!.WithContext($"rg: {path}"));
            return false;
        }

        if (!lowArgs.SearchZip || !TryGetDecompressionCommand(path, out string program, out string[] arguments))
        {
            return true;
        }

        if (TryRunSearchCommand(path, program, arguments, pipeFileToStandardInput: false, fallbackOnStartError: true, out bytes, out ScoutError? decompressionError))
        {
            handled = true;
            return true;
        }

        if (decompressionError is not null)
        {
            handled = true;
            SearchErrorMessage(lowArgs, diagnostics, decompressionError.WithContext($"rg: {path}"));
            return false;
        }

        return true;
    }

    private static bool ShouldRunPreprocessor(string path, CliLowArgs lowArgs)
    {
        if (lowArgs.Preprocessor is null)
        {
            return false;
        }

        if (lowArgs.PreprocessorGlobs.Count == 0)
        {
            return true;
        }

        string fullPath = Path.GetFullPath(path);
        string baseDirectory = Path.GetPathRoot(fullPath) ?? Directory.GetCurrentDirectory();
        var builder = new OverrideBuilder(baseDirectory);
        for (int index = 0; index < lowArgs.PreprocessorGlobs.Count; index++)
        {
            builder.Add(lowArgs.PreprocessorGlobs[index]);
        }

        Override matcher = builder.Build();
        return !matcher.IsIgnored(fullPath, isDirectory: false);
    }

    private static bool TryGetDecompressionCommand(string path, out string program, out string[] arguments)
    {
        if (CliDecompressionMatcher.TryGetAvailableDefaultCommand(path, out CliDecompressionCommand? command) &&
            command is not null)
        {
            program = command.Program;
            arguments = command.CreateArguments(path);
            return true;
        }

        program = string.Empty;
        arguments = [];
        return false;
    }

    private static bool TryRunSearchCommand(
        string path,
        string program,
        string[] arguments,
        bool pipeFileToStandardInput,
        bool fallbackOnStartError,
        out byte[] bytes,
        out ScoutError? error)
    {
        bytes = [];
        error = null;
        using var process = new Process();
        process.StartInfo.FileName = program;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardInput = pipeFileToStandardInput;
        process.StartInfo.UseShellExecute = false;
        for (int index = 0; index < arguments.Length; index++)
        {
            process.StartInfo.ArgumentList.Add(arguments[index]);
        }

        try
        {
            if (!process.Start())
            {
                if (fallbackOnStartError)
                {
                    return false;
                }

                error = new ScoutError($"preprocessor command could not start: '{program}': process did not start");
                return false;
            }
        }
        catch (Win32Exception exception)
        {
            if (fallbackOnStartError)
            {
                return false;
            }

            error = new ScoutError($"preprocessor command could not start: '{program}': {exception.Message}");
            return false;
        }
        catch (InvalidOperationException exception)
        {
            if (fallbackOnStartError)
            {
                return false;
            }

            error = new ScoutError($"preprocessor command could not start: '{program}': {exception.Message}");
            return false;
        }

        Task<byte[]> standardOutput = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (pipeFileToStandardInput)
        {
            CopyFileToProcessStandardInput(path, process);
        }

        process.WaitForExit();
        bytes = standardOutput.GetAwaiter().GetResult();
        string stderr = standardError.GetAwaiter().GetResult();
        if (process.ExitCode == 0)
        {
            return true;
        }

        string message = pipeFileToStandardInput
            ? $"preprocessor command failed: '{program}': {stderr.TrimEnd()}"
            : $"{program} command failed: {stderr.TrimEnd()}";
        error = new ScoutError(message);
        bytes = [];
        return false;
    }

    private static void CopyFileToProcessStandardInput(string path, Process process)
    {
        using Stream input = File.OpenRead(path);
        byte[] buffer = new byte[81920];
        try
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                try
                {
                    process.StandardInput.BaseStream.Write(buffer, 0, read);
                }
                catch (IOException)
                {
                    break;
                }
            }
        }
        finally
        {
            CloseProcessStandardInput(process);
        }
    }

    private static void CloseProcessStandardInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
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
            WriteSearchPathTerminator(output, nullPathTerminator, separators.LineTerminator);
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
        List<ContextLineInfo> lines = BuildContextLines(bytes, pattern, asciiCaseInsensitive, statsInvertMatch, lineRegexp, wordRegexp, crlf, nullData, stopOnNonmatch);
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
            ? GetStopOnNonmatchLength(searchBytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, separators.Crlf, separators.NullData)
            : searchBytes.Length;
        ReadOnlySpan<byte> searchSpan = searchBytes.AsSpan(0, stopLength);
        ReadOnlySpan<byte> outputSpan = ReferenceEquals(bytes, searchBytes)
            ? searchSpan
            : bytes.AsSpan(0, stopLength);

        if (multiline &&
            ShouldUseMultilineRegex(pattern, multilineDotall) &&
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
            return WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            long count = LiteralLineSearcher.CountMatches(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData);
            return WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return WritePathIf(output, prefix, color, LiteralLineSearcher.HasMatch(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData), nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return WritePathIf(output, prefix, color, !LiteralLineSearcher.HasMatch(searchSpan, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxCount, separators.Crlf, separators.NullData), nullPathTerminator, separators.LineTerminator);
        }

        if (passthru || beforeContext > 0 || afterContext > 0)
        {
            return SearchContextBytes(bytes, pattern, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, onlyMatching, replacement, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator, stopOnNonmatch);
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

        if (!TryFindMultilineMatch(searchSpan, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, startAt: 0, out RegexMatch firstMatch))
        {
            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                matched = WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
            {
                matched = WriteCount(output, prefix, color, 0, includeZero, nullPathTerminator, separators.LineTerminator);
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
            matched = WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            matched = WritePathIf(output, prefix, color, false, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        long count = CountMultilineMatches(searchSpan, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            matched = WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
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
            return WritePathIf(output, prefix, color, hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return WritePathIf(output, prefix, color, !hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            return WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
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
        IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
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
            if (match.Length == 0)
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
            int lineLength = GetLineLength(bytes[lineStart..], nullData: false);
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
            int lineLength = GetLineLength(bytes[lineStart..], nullData: false);
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

    private static byte[] ApplySearchEncoding(byte[] bytes, CliEncodingMode encodingMode)
    {
        return SearchEncoding.Decode(bytes, ToSearchEncodingKind(encodingMode));
    }

    private static byte[] ReadSearchStream(Stream stream, CliEncodingMode encodingMode)
    {
        return SearchEncodingReader.ReadToEnd(stream, ToSearchEncodingKind(encodingMode));
    }

    private static SearchEncodingKind ToSearchEncodingKind(CliEncodingMode encodingMode)
    {
        return encodingMode switch
        {
            CliEncodingMode.None => SearchEncodingKind.None,
            CliEncodingMode.Utf8 => SearchEncodingKind.Utf8,
            CliEncodingMode.Utf16 => SearchEncodingKind.Utf16,
            CliEncodingMode.Utf16Le => SearchEncodingKind.Utf16Le,
            CliEncodingMode.Utf16Be => SearchEncodingKind.Utf16Be,
            CliEncodingMode.EucKr => SearchEncodingKind.EucKr,
            CliEncodingMode.EucJp => SearchEncodingKind.EucJp,
            CliEncodingMode.Big5 => SearchEncodingKind.Big5,
            CliEncodingMode.Gb18030 => SearchEncodingKind.Gb18030,
            CliEncodingMode.Gbk => SearchEncodingKind.Gbk,
            CliEncodingMode.ShiftJis => SearchEncodingKind.ShiftJis,
            CliEncodingMode.Ibm866 => SearchEncodingKind.Ibm866,
            CliEncodingMode.Iso88592 => SearchEncodingKind.Iso88592,
            CliEncodingMode.Iso88593 => SearchEncodingKind.Iso88593,
            CliEncodingMode.Iso88594 => SearchEncodingKind.Iso88594,
            CliEncodingMode.Iso88595 => SearchEncodingKind.Iso88595,
            CliEncodingMode.Iso88596 => SearchEncodingKind.Iso88596,
            CliEncodingMode.Iso88597 => SearchEncodingKind.Iso88597,
            CliEncodingMode.Iso88598 => SearchEncodingKind.Iso88598,
            CliEncodingMode.Iso88598I => SearchEncodingKind.Iso88598I,
            CliEncodingMode.Iso885910 => SearchEncodingKind.Iso885910,
            CliEncodingMode.Iso885913 => SearchEncodingKind.Iso885913,
            CliEncodingMode.Iso885914 => SearchEncodingKind.Iso885914,
            CliEncodingMode.Iso885915 => SearchEncodingKind.Iso885915,
            CliEncodingMode.Iso885916 => SearchEncodingKind.Iso885916,
            CliEncodingMode.Iso2022Jp => SearchEncodingKind.Iso2022Jp,
            CliEncodingMode.Koi8R => SearchEncodingKind.Koi8R,
            CliEncodingMode.Koi8U => SearchEncodingKind.Koi8U,
            CliEncodingMode.Macintosh => SearchEncodingKind.Macintosh,
            CliEncodingMode.Windows874 => SearchEncodingKind.Windows874,
            CliEncodingMode.Windows1250 => SearchEncodingKind.Windows1250,
            CliEncodingMode.Windows1251 => SearchEncodingKind.Windows1251,
            CliEncodingMode.Windows1252 => SearchEncodingKind.Windows1252,
            CliEncodingMode.Windows1253 => SearchEncodingKind.Windows1253,
            CliEncodingMode.Windows1254 => SearchEncodingKind.Windows1254,
            CliEncodingMode.Windows1255 => SearchEncodingKind.Windows1255,
            CliEncodingMode.Windows1256 => SearchEncodingKind.Windows1256,
            CliEncodingMode.Windows1257 => SearchEncodingKind.Windows1257,
            CliEncodingMode.Windows1258 => SearchEncodingKind.Windows1258,
            CliEncodingMode.XMacCyrillic => SearchEncodingKind.XMacCyrillic,
            CliEncodingMode.XUserDefined => SearchEncodingKind.XUserDefined,
            _ => SearchEncodingKind.Auto,
        };
    }

    private static SearchMmapMode ToSearchMmapMode(CliMmapMode mmapMode)
    {
        return mmapMode switch
        {
            CliMmapMode.AlwaysTryMmap => SearchMmapMode.AlwaysTryMmap,
            CliMmapMode.Never => SearchMmapMode.Never,
            _ => SearchMmapMode.Auto,
        };
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
        bool matched = multiline && ShouldUseMultilineRegex(pattern, multilineDotall) && TrySearchJsonMultilineBytes(searchBytes, pattern, writer, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multilineDotall, crlf, nullData, replacement, maxCount, beforeContext, afterContext, passthru, out bool multilineMatched)
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
            nullData ||
            beforeContext > 0 ||
            afterContext > 0 ||
            passthru)
        {
            return false;
        }

        if (invertMatch)
        {
            matched = WriteJsonMultilineInvertedMatches(bytes, patterns, writer, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        var matches = new List<JsonMatchSpan>(capacity: 1);
        while (TryFindNextMultilineMatch(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            matched = true;
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            int lineEnd = GetLineEnd(bytes, lastLineStart);
            matches.Clear();
            matches.Add(new JsonMatchSpan(match.Start - firstLineStart, match.Start - firstLineStart + match.Length, replacement: null));
            writer.WriteMatchLine(
                GetLineNumber(bytes, firstLineStart),
                firstLineStart,
                bytes[firstLineStart..lineEnd],
                matches,
                (ulong)(1 + CountLineFeeds(bytes[firstLineStart..lastLineStart])));

            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return true;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        return true;
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
        List<ContextLineInfo> lines = BuildContextLines(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, stopOnNonmatch);
        if (lines.Count == 0)
        {
            return false;
        }

        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? IncludePassthruLines(lines, included)
            : IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
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

    private static bool SearchContextBytes(
        byte[] bytes,
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
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator,
        bool stopOnNonmatch)
    {
        List<ContextLineInfo> lines = BuildContextLines(bytes, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, separators.Crlf, separators.NullData, stopOnNonmatch);
        if (lines.Count == 0)
        {
            return false;
        }

        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? IncludePassthruLines(lines, included)
            : IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
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

            if (!passthru && wrote && index > previousLineIndex + 1)
            {
                if (separators.ContextEnabled)
                {
                    output.Write(separators.Context.Span);
                    output.Write(separators.LineTerminator.Span);
                }
            }

            WriteContextOutputLine(
                bytes,
                line,
                selectedMatch,
                output,
                prefix,
                lineNumber,
                column,
                byteOffset,
                trim,
                separators,
                lineLimit,
                color,
                onlyMatching,
                replacement,
                invertMatch,
                pattern,
                asciiCaseInsensitive,
                lineRegexp,
                wordRegexp,
                nullPathTerminator,
                ref lineSink);
            previousLineIndex = index;
            wrote = true;
        }

        return matched;
    }

    private static List<ContextLineInfo> BuildContextLines(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        bool stopOnNonmatch = false)
    {
        var lines = new List<ContextLineInfo>();
        bool hasSelectedMatch = false;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            bool originalMatch = TryFindLineMatch(line, pattern, asciiCaseInsensitive, false, lineRegexp, wordRegexp, crlf, nullData, out long originalColumn);
            bool selectedMatch = originalMatch;
            long matchColumn = originalColumn;
            if (invertMatch)
            {
                selectedMatch = TryFindLineMatch(line, pattern, asciiCaseInsensitive, true, lineRegexp, wordRegexp, crlf, nullData, out matchColumn);
            }

            lines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch, matchColumn, originalMatch, originalColumn));
            if (stopOnNonmatch && hasSelectedMatch && !selectedMatch)
            {
                break;
            }

            hasSelectedMatch |= selectedMatch;
            lineStart += lineLength;
            lineNumber++;
        }

        return lines;
    }

    private static bool TryFindLineMatch(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out long matchColumn)
    {
        var sink = new FirstLineMatchSink();
        bool matched = LiteralLineSearcher.Search(line, pattern, ref sink, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines: 1, crlf: crlf, nullData: nullData);
        matchColumn = matched ? sink.MatchColumn : 0;
        return matched;
    }

    private static int GetStopOnNonmatchLength(
        byte[] bytes,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        bool hasSelectedMatch = false;
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            bool selectedMatch = TryFindLineMatch(line, pattern, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out _);
            if (hasSelectedMatch && !selectedMatch)
            {
                return lineStart + lineLength;
            }

            hasSelectedMatch |= selectedMatch;
            lineStart += lineLength;
        }

        return bytes.Length;
    }

    private static int GetLineLength(ReadOnlySpan<byte> remaining, bool nullData)
    {
        int terminator = remaining.IndexOf(nullData ? (byte)0 : (byte)'\n');
        return terminator < 0 ? remaining.Length : terminator + 1;
    }

    private static bool IncludePassthruLines(List<ContextLineInfo> lines, bool[] included)
    {
        bool matched = false;
        for (int index = 0; index < lines.Count; index++)
        {
            included[index] = true;
            matched |= lines[index].SelectedMatch;
        }

        return matched;
    }

    private static bool IncludeContextLines(
        List<ContextLineInfo> lines,
        bool[] included,
        ulong beforeContext,
        ulong afterContext,
        ulong? maxCount)
    {
        bool matched = false;
        ulong primaryMatches = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!lines[index].SelectedMatch)
            {
                continue;
            }

            matched = true;
            if (maxCount is ulong limit && primaryMatches >= limit)
            {
                continue;
            }

            primaryMatches++;
            IncludeContextRange(included, index, beforeContext, afterContext);
        }

        return matched;
    }

    private static void IncludeContextRange(bool[] included, int matchIndex, ulong beforeContext, ulong afterContext)
    {
        int start = beforeContext > (ulong)matchIndex ? 0 : matchIndex - (int)beforeContext;
        int remainingAfter = included.Length - matchIndex - 1;
        int end = afterContext > (ulong)remainingAfter ? included.Length - 1 : matchIndex + (int)afterContext;
        for (int index = start; index <= end; index++)
        {
            included[index] = true;
        }
    }

    private static void WriteContextOutputLine(
        byte[] bytes,
        ContextLineInfo line,
        bool selectedMatch,
        RawByteWriter output,
        OutputPath? prefix,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        bool invertMatch,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool nullPathTerminator,
        ref StandardSearchSink lineSink)
    {
        ReadOnlySpan<byte> lineBytes = bytes.AsSpan(line.Start, line.Length);
        if (selectedMatch)
        {
            if (onlyMatching && !invertMatch)
            {
                WriteOnlyMatchesForContextLine(lineBytes, line, output, prefix, lineNumber, column, byteOffset, trim, separators.FieldMatch, replacement, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, nullPathTerminator, color, separators.LineTerminator, separators.Crlf, separators.NullData);
                return;
            }

            if (replacement is ReadOnlyMemory<byte> replacementValue && !invertMatch)
            {
                var replacementLineSink = new ReplacementLineSink(
                    output,
                    prefix,
                    separators.FieldMatch,
                    replacementValue,
                    pattern,
                    asciiCaseInsensitive,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    vimgrep: false,
                    lineLimit,
                    line.LineNumber - 1,
                    line.Start,
                    color,
                    separators.LineTerminator);
                LiteralLineSearcher.SearchMatchLines(lineBytes, pattern, ref replacementLineSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
                replacementLineSink.Flush();
                return;
            }

            if (color.Enabled && !invertMatch)
            {
                var coloredSink = new ColoredSearchSink(
                    output,
                    prefix,
                    separators.FieldMatch,
                    lineNumber,
                    column,
                    byteOffset,
                    trim,
                    nullPathTerminator,
                    lineLimit,
                    color,
                    separators.LineTerminator);
                LiteralLineSearcher.SearchMatchLines(lineBytes, pattern, ref coloredSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: separators.Crlf, nullData: separators.NullData);
                coloredSink.Flush();
                return;
            }

            lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, lineBytes);
            return;
        }

        if (onlyMatching && invertMatch && line.OriginalMatch)
        {
            WriteOnlyMatchesForContextLine(lineBytes, line, output, prefix, lineNumber, column, byteOffset, trim, separators.FieldContext, replacement, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, nullPathTerminator, color, separators.LineTerminator, separators.Crlf, separators.NullData);
            return;
        }

        lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, lineBytes);
    }

    private static void WriteOnlyMatchesForContextLine(
        ReadOnlySpan<byte> lineBytes,
        ContextLineInfo line,
        RawByteWriter output,
        OutputPath? prefix,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        ReadOnlyMemory<byte> fieldSeparator,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool nullPathTerminator,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator,
        bool crlf,
        bool nullData)
    {
        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            var replacementMatchSink = new ReplacementMatchSink(
                output,
                prefix,
                fieldSeparator,
                replacementValue,
                pattern,
                asciiCaseInsensitive,
                lineNumber,
                column,
                byteOffset,
                nullPathTerminator,
                line.LineNumber - 1,
                line.Start,
                color,
                lineTerminator);
            LiteralLineSearcher.SearchMatches(lineBytes, pattern, ref replacementMatchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: crlf, nullData: nullData);
        }
        else
        {
            var matchSink = new StandardMatchSink(
                output,
                prefix,
                fieldSeparator,
                lineNumber,
                column,
                byteOffset,
                trim,
                line.LineNumber - 1,
                line.Start,
                nullPathTerminator,
                color,
                lineTerminator);
            LiteralLineSearcher.SearchMatches(lineBytes, pattern, ref matchSink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf: crlf, nullData: nullData);
        }
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

    private static int GetSearchExitCode(bool matched, bool errored, bool quiet)
    {
        if (matched && (quiet || !errored))
        {
            return ExitCode.Success;
        }

        if (!matched && !errored)
        {
            return ExitCode.NoMatch;
        }

        return ExitCode.Error;
    }

    private static bool EffectiveLineNumber(CliLowArgs lowArgs)
    {
        return lowArgs.LineNumber || (EffectiveColumn(lowArgs) && !lowArgs.LineNumberSpecified) || (lowArgs.Vimgrep && !lowArgs.LineNumberSpecified);
    }

    private static bool EffectiveColumn(CliLowArgs lowArgs)
    {
        return lowArgs.Column || (lowArgs.Vimgrep && !lowArgs.ColumnSpecified);
    }

    private static bool WriteCount(
        RawByteWriter output,
        OutputPath? prefix,
        OutputColor color,
        long count,
        bool includeZero,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator)
    {
        bool matched = count != 0;
        if (!matched && !includeZero)
        {
            return false;
        }

        if (prefix is not null)
        {
            prefix.WriteLabel(output, color);
            WritePrefixTerminator(output, nullPathTerminator, matchSeparator: true);
        }

        WriteNumber(output, count);
        output.Write(lineTerminator.Span);
        return matched;
    }

    private static bool WritePathIf(
        RawByteWriter output,
        OutputPath? path,
        OutputColor color,
        bool condition,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator)
    {
        if (!condition)
        {
            return false;
        }

        if (path is null)
        {
            output.Write(StandardInputPath);
        }
        else
        {
            path.WriteLabel(output, color);
        }

        WriteSearchPathTerminator(output, nullPathTerminator, lineTerminator);
        return true;
    }

    private static void WriteSearchPathTerminator(
        RawByteWriter output,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator)
    {
        if (nullPathTerminator)
        {
            output.Write(NullByte);
            return;
        }

        output.Write(lineTerminator.Span);
    }

    private static void WritePathTerminator(RawByteWriter output, bool nullPathTerminator)
    {
        output.Write(nullPathTerminator ? NullByte : "\n"u8);
    }

    private static void WritePrefixTerminator(RawByteWriter output, bool nullPathTerminator, bool matchSeparator)
    {
        if (nullPathTerminator)
        {
            output.Write(NullByte);
            return;
        }

        output.Write(matchSeparator ? ":"u8 : "-"u8);
    }

    private static OutputPath CreateOutputPath(string physicalPath, byte[] displayPath, CliLowArgs lowArgs, OutputColor color)
    {
        string? hyperlinkFormat = color.Enabled ? lowArgs.HyperlinkFormat : null;
        byte[]? hyperlinkPath = string.IsNullOrEmpty(hyperlinkFormat)
            ? null
            : OutputPath.CreateHyperlinkPath(physicalPath);
        string host = GetHyperlinkHost(lowArgs, hyperlinkFormat);
        return new OutputPath(displayPath, hyperlinkPath, hyperlinkFormat, host);
    }

    private static string GetHyperlinkHost(CliLowArgs lowArgs, string? hyperlinkFormat)
    {
        if (string.IsNullOrEmpty(hyperlinkFormat) || !hyperlinkFormat.Contains("{host}", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(lowArgs.HostnameBin) && TryRunHostnameCommand(lowArgs.HostnameBin, out string host))
        {
            return host;
        }

        return Environment.MachineName;
    }

    private static bool TryRunHostnameCommand(string program, out string host)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(program)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            if (!process.Start())
            {
                host = string.Empty;
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            host = output.Trim();
            return process.ExitCode == 0;
        }
        catch (InvalidOperationException)
        {
            host = string.Empty;
            return false;
        }
        catch (Win32Exception)
        {
            host = string.Empty;
            return false;
        }
    }

    private static OutputPath? GetStandardInputPrefix(CliSearchMode searchMode, bool autoPrefixPath, bool? withFilename)
    {
        if (IsFileListMode(searchMode) || ShouldPrefixMatchFields(autoPrefixPath, withFilename))
        {
            return new OutputPath(StandardInputPath, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty);
        }

        return null;
    }

    private static OutputPath? GetFileSearchPrefix(CliSearchMode searchMode, bool autoPrefixPath, bool? withFilename, OutputPath path)
    {
        return IsFileListMode(searchMode) || ShouldPrefixMatchFields(autoPrefixPath, withFilename)
            ? path
            : null;
    }

    private static bool IsFileListMode(CliSearchMode searchMode)
    {
        return searchMode == CliSearchMode.FilesWithMatches || searchMode == CliSearchMode.FilesWithoutMatch;
    }

    private static bool ShouldPrefixMatchFields(bool autoPrefixPath, bool? withFilename)
    {
        return withFilename ?? autoPrefixPath;
    }

    private static void WriteNumber(RawByteWriter output, long value)
    {
        Span<byte> buffer = stackalloc byte[20];
        ulong number = (ulong)value;
        int index = buffer.Length;
        do
        {
            index--;
            buffer[index] = (byte)((number % 10) + (byte)'0');
            number /= 10;
        }
        while (number != 0);

        output.Write(buffer[index..]);
    }

    private static byte[] GetPatternBytes(OsString pattern)
    {
        return pattern.IsUnixBytes
            ? pattern.AsUnixBytes().ToArray()
            : Utf8.GetBytes(pattern.AsWindowsString());
    }

    private static bool ContainsLineFeed(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ContainsLiteralLineFeed(patterns[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLineTerminator(List<byte[]> patterns, bool nullData, bool fixedStrings)
    {
        byte terminator = nullData ? (byte)0 : (byte)'\n';
        for (int index = 0; index < patterns.Count; index++)
        {
            if (fixedStrings)
            {
                if (patterns[index].AsSpan().Contains(terminator))
                {
                    return true;
                }

                continue;
            }

            if (nullData ? ContainsRegexNulLiteral(patterns[index]) : ContainsLiteralLineFeed(patterns[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRegexNulLiteral(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ContainsRegexNulLiteral(patterns[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildLineTerminatorPatternError(bool nullData)
    {
        string literal = nullData ? "\\0" : "\\n";
        return "the literal \"" + literal + "\" is not allowed in a regex\n\n" +
            "Consider enabling multiline mode with the --multiline flag (or -U for short).\n" +
            "When multiline mode is enabled, new line characters can be matched.";
    }

    private static bool ShouldUseMultilineRegex(IReadOnlyList<byte[]> patterns, bool multilineDotall)
    {
        return multilineDotall || ContainsLineFeed(patterns) || ContainsLineFeedMatchingSyntax(patterns);
    }

    private static bool ContainsLineFeedMatchingSyntax(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (SyntaxCanMatchLineFeed(RegexSyntaxParser.Parse(patterns[index]).Root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SyntaxCanMatchLineFeed(RegexSyntaxNode node)
    {
        return node switch
        {
            RegexInlineFlagsNode flags => flags.EnabledFlags.Contains('s', StringComparison.Ordinal),
            RegexGroupNode group => group.EnabledFlags.Contains('s', StringComparison.Ordinal) || SyntaxCanMatchLineFeed(group.Child),
            RegexSequenceNode sequence => AnySyntaxCanMatchLineFeed(sequence.Nodes),
            RegexAlternationNode alternation => AnySyntaxCanMatchLineFeed(alternation.Alternatives),
            RegexRepetitionNode repetition => SyntaxCanMatchLineFeed(repetition.Child),
            RegexAtomNode atom => atom.Kind is RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.CharacterClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.WhitespaceClass,
            _ => false,
        };
    }

    private static bool AnySyntaxCanMatchLineFeed(IReadOnlyList<RegexSyntaxNode> nodes)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            if (SyntaxCanMatchLineFeed(nodes[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLiteralLineFeed(ReadOnlySpan<byte> pattern)
    {
        var scopeDepths = new List<int>();
        var scopeValues = new List<bool>();
        bool ignoreWhitespace = false;
        bool inClass = false;
        int depth = 0;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (inClass)
            {
                if (value == (byte)'\\' && index + 1 < pattern.Length)
                {
                    index++;
                }
                else if (value == (byte)']')
                {
                    inClass = false;
                }

                continue;
            }

            if (value == (byte)'\\')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == (byte)'n')
                {
                    return true;
                }

                index++;
                continue;
            }

            if (value == (byte)'[')
            {
                inClass = true;
                continue;
            }

            if (ignoreWhitespace)
            {
                if (value == (byte)'#')
                {
                    index = SkipRegexComment(pattern, index);
                    continue;
                }

                if (IsRegexWhitespace(value))
                {
                    continue;
                }
            }

            if (value == (byte)'\n')
            {
                return true;
            }

            if (value == (byte)'(')
            {
                if (index + 1 < pattern.Length &&
                    pattern[index + 1] == (byte)'?' &&
                    TryReadInlineFlagGroup(pattern, index, out int markerIndex, out bool scoped, out bool? scopedIgnoreWhitespace))
                {
                    if (scoped)
                    {
                        depth++;
                        if (scopedIgnoreWhitespace.HasValue)
                        {
                            scopeDepths.Add(depth);
                            scopeValues.Add(ignoreWhitespace);
                            ignoreWhitespace = scopedIgnoreWhitespace.Value;
                        }
                    }
                    else if (scopedIgnoreWhitespace.HasValue)
                    {
                        ignoreWhitespace = scopedIgnoreWhitespace.Value;
                    }

                    index = markerIndex;
                    continue;
                }

                depth++;
                continue;
            }

            if (value == (byte)')')
            {
                while (scopeDepths.Count > 0 && scopeDepths[^1] == depth)
                {
                    ignoreWhitespace = scopeValues[^1];
                    scopeDepths.RemoveAt(scopeDepths.Count - 1);
                    scopeValues.RemoveAt(scopeValues.Count - 1);
                }

                if (depth > 0)
                {
                    depth--;
                }
            }
        }

        return false;
    }

    private static bool ContainsRegexNulLiteral(ReadOnlySpan<byte> pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == 0)
            {
                return true;
            }

            if (value != (byte)'\\' || index + 1 >= pattern.Length)
            {
                continue;
            }

            byte escaped = pattern[index + 1];
            if (escaped == (byte)'x')
            {
                if (index + 3 < pattern.Length && TryReadHexByte(pattern[index + 2], pattern[index + 3], out byte byteValue))
                {
                    if (byteValue == 0)
                    {
                        return true;
                    }

                    index += 3;
                    continue;
                }

                if (TryReadBracedHexScalar(pattern, index + 2, out int scalarValue, out int endIndex))
                {
                    if (scalarValue == 0)
                    {
                        return true;
                    }

                    index = endIndex;
                    continue;
                }
            }
            else if (escaped == (byte)'u' && TryReadBracedHexScalar(pattern, index + 2, out int scalarValue, out int endIndex))
            {
                if (scalarValue == 0)
                {
                    return true;
                }

                index = endIndex;
                continue;
            }

            index++;
        }

        return false;
    }

    private static bool TryReadHexByte(byte high, byte low, out byte value)
    {
        value = 0;
        if (!TryGetHexDigit(high, out int highValue) || !TryGetHexDigit(low, out int lowValue))
        {
            return false;
        }

        value = (byte)((highValue << 4) | lowValue);
        return true;
    }

    private static bool TryReadBracedHexScalar(ReadOnlySpan<byte> pattern, int openBraceIndex, out int value, out int endIndex)
    {
        value = 0;
        endIndex = openBraceIndex;
        if (openBraceIndex >= pattern.Length || pattern[openBraceIndex] != (byte)'{')
        {
            return false;
        }

        int index = openBraceIndex + 1;
        int digits = 0;
        while (index < pattern.Length && pattern[index] != (byte)'}')
        {
            if (!TryGetHexDigit(pattern[index], out int digit))
            {
                return false;
            }

            value = (value * 16) + digit;
            digits++;
            index++;
        }

        if (digits == 0 || index >= pattern.Length || pattern[index] != (byte)'}')
        {
            return false;
        }

        endIndex = index;
        return true;
    }

    private static bool TryGetHexDigit(byte value, out int digit)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            digit = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            digit = value - (byte)'a' + 10;
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            digit = value - (byte)'A' + 10;
            return true;
        }

        digit = 0;
        return false;
    }

    private static int SkipRegexComment(ReadOnlySpan<byte> pattern, int commentStart)
    {
        for (int index = commentStart + 1; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\n')
            {
                return index;
            }
        }

        return pattern.Length - 1;
    }

    private static bool TryReadInlineFlagGroup(
        ReadOnlySpan<byte> pattern,
        int openParenIndex,
        out int markerIndex,
        out bool scoped,
        out bool? ignoreWhitespace)
    {
        markerIndex = openParenIndex;
        scoped = false;
        ignoreWhitespace = null;
        bool negated = false;
        bool sawFlag = false;
        for (int index = openParenIndex + 2; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'-')
            {
                negated = true;
                continue;
            }

            if (IsInlineRegexFlag(value))
            {
                sawFlag = true;
                if (value == (byte)'x')
                {
                    ignoreWhitespace = !negated;
                }

                continue;
            }

            if (value == (byte)':')
            {
                markerIndex = index;
                scoped = true;
                return true;
            }

            if (value == (byte)')')
            {
                markerIndex = index;
                return sawFlag;
            }

            return false;
        }

        return false;
    }

    private static bool IsInlineRegexFlag(byte value)
    {
        return value is (byte)'i' or (byte)'m' or (byte)'s' or (byte)'U' or (byte)'u' or (byte)'x' or (byte)'R';
    }

    private static bool IsRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0C;
    }

    private static void EscapeFixedStringPatterns(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            patterns[index] = EscapeFixedStringPattern(patterns[index]);
        }
    }

    private static byte[] EscapeFixedStringPattern(byte[] pattern)
    {
        int escapeCount = 0;
        for (int index = 0; index < pattern.Length; index++)
        {
            if (IsRegexMetaByte(pattern[index]))
            {
                escapeCount++;
            }
        }

        if (escapeCount == 0)
        {
            return pattern;
        }

        byte[] escaped = new byte[pattern.Length + escapeCount];
        int outputIndex = 0;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (IsRegexMetaByte(value))
            {
                escaped[outputIndex] = (byte)'\\';
                outputIndex++;
            }

            escaped[outputIndex] = value;
            outputIndex++;
        }

        return escaped;
    }

    private static void WrapNonAsciiPatterns(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ContainsNonAscii(patterns[index]))
            {
                patterns[index] = WrapNonCapturingGroup(patterns[index]);
            }
        }
    }

    private static void WrapRegexPatterns(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ShouldWrapRegexPattern(patterns[index]))
            {
                patterns[index] = WrapNonCapturingGroup(patterns[index]);
            }
        }
    }

    private static bool ShouldWrapRegexPattern(ReadOnlySpan<byte> pattern)
    {
        int rawDepth = 0;
        int wrappedDepth = 1;
        bool rawUnderflow = false;
        bool inClass = false;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (inClass)
            {
                if (value == (byte)'\\' && index + 1 < pattern.Length)
                {
                    index++;
                }
                else if (value == (byte)']')
                {
                    inClass = false;
                }

                continue;
            }

            if (value == (byte)'\\')
            {
                index++;
                continue;
            }

            if (value == (byte)'[')
            {
                inClass = true;
                continue;
            }

            if (value == (byte)'(')
            {
                rawDepth++;
                wrappedDepth++;
                continue;
            }

            if (value != (byte)')')
            {
                continue;
            }

            rawDepth--;
            if (rawDepth < 0)
            {
                rawUnderflow = true;
            }

            wrappedDepth--;
            if (wrappedDepth < 0)
            {
                return false;
            }
        }

        return rawUnderflow && wrappedDepth == 1;
    }

    private static byte[] WrapNonCapturingGroup(byte[] pattern)
    {
        byte[] wrapped = new byte[pattern.Length + 4];
        wrapped[0] = (byte)'(';
        wrapped[1] = (byte)'?';
        wrapped[2] = (byte)':';
        pattern.CopyTo(wrapped.AsSpan(3));
        wrapped[^1] = (byte)')';
        return wrapped;
    }

    private static bool ContainsNonAscii(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] >= 0x80)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRegexMetaByte(byte value)
    {
        return value is (byte)'\\'
            or (byte)'.'
            or (byte)'['
            or (byte)']'
            or (byte)'('
            or (byte)')'
            or (byte)'{'
            or (byte)'}'
            or (byte)'*'
            or (byte)'+'
            or (byte)'?'
            or (byte)'^'
            or (byte)'$'
            or (byte)'|';
    }

    private static bool TryValidateRegexRepetitionExpressions(List<byte[]> patterns, DiagnosticMessenger diagnostics)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (TryFindMissingRepetitionExpression(patterns[index], out int offset))
            {
                diagnostics.ErrorMessage(new ScoutError(BuildRegexParseError(
                    patterns[index],
                    offset,
                    "repetition operator missing expression")).WithContext("rg"));
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateRegexSizeLimit(List<byte[]> patterns, CliLowArgs lowArgs, DiagnosticMessenger diagnostics)
    {
        if (lowArgs.RegexSizeLimit is not ulong limit)
        {
            return true;
        }

        ulong compiledSize = 0;
        for (int index = 0; index < patterns.Count; index++)
        {
            compiledSize = SaturatingAdd(compiledSize, EstimateCompiledRegexSize(patterns[index], lowArgs.Unicode));
            if (compiledSize > limit)
            {
                diagnostics.ErrorMessage(new ScoutError($"compiled regex exceeds size limit of {limit}").WithContext("rg"));
                return false;
            }
        }

        return true;
    }

    private static ulong EstimateCompiledRegexSize(ReadOnlySpan<byte> pattern, bool unicode)
    {
        ulong size = SaturatingAdd(RegexCompiledBaseSize, SaturatingMultiply((ulong)pattern.Length, RegexCompiledByteSize));
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\\' && index + 1 < pattern.Length)
            {
                size = SaturatingAdd(size, GetEscapedClassCompiledSize(pattern[index + 1], unicode));
                index++;
            }
        }

        return size;
    }

    private static ulong GetEscapedClassCompiledSize(byte escaped, bool unicode)
    {
        if (!unicode)
        {
            return 0;
        }

        return escaped switch
        {
            (byte)'d' => RegexCompiledUnicodeDigitClassSize,
            (byte)'D' => RegexCompiledUnicodeNegatedDigitClassSize,
            (byte)'w' or (byte)'W' => RegexCompiledUnicodeWordClassSize,
            (byte)'s' => RegexCompiledUnicodeWhitespaceClassSize,
            (byte)'S' => RegexCompiledUnicodeNegatedWhitespaceClassSize,
            _ => 0,
        };
    }

    private static ulong SaturatingAdd(ulong left, ulong right)
    {
        return ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    }

    private static ulong SaturatingMultiply(ulong left, ulong right)
    {
        if (left == 0 || right == 0)
        {
            return 0;
        }

        return left > ulong.MaxValue / right ? ulong.MaxValue : left * right;
    }

    private static bool TryFindMissingRepetitionExpression(ReadOnlySpan<byte> pattern, out int offset)
    {
        bool expectingExpression = true;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (expectingExpression && IsRegexRepetitionOperator(value))
            {
                offset = index;
                return true;
            }

            if (value == (byte)'\\')
            {
                index = SkipRegexEscape(pattern, index);
                expectingExpression = false;
                continue;
            }

            if (value == (byte)'[')
            {
                index = SkipRegexCharacterClass(pattern, index);
                expectingExpression = false;
                continue;
            }

            if (value == (byte)'|')
            {
                expectingExpression = true;
                continue;
            }

            if (value == (byte)')')
            {
                expectingExpression = false;
                continue;
            }

            if (value == (byte)'(')
            {
                if (TryReadRegexGroupPrefix(pattern, index, out int contentStart))
                {
                    index = contentStart - 1;
                    expectingExpression = true;
                    continue;
                }

                expectingExpression = true;
                continue;
            }

            if (!expectingExpression && IsRegexRepetitionOperator(value))
            {
                index = SkipRegexRepetition(pattern, index);
                continue;
            }

            expectingExpression = false;
        }

        offset = -1;
        return false;
    }

    private static bool IsRegexRepetitionOperator(byte value)
    {
        return value is (byte)'?' or (byte)'*' or (byte)'+' or (byte)'{';
    }

    private static int SkipRegexEscape(ReadOnlySpan<byte> pattern, int escapeIndex)
    {
        if (escapeIndex + 2 < pattern.Length &&
            (pattern[escapeIndex + 1] == (byte)'x' || pattern[escapeIndex + 1] == (byte)'u') &&
            pattern[escapeIndex + 2] == (byte)'{')
        {
            for (int index = escapeIndex + 3; index < pattern.Length; index++)
            {
                if (pattern[index] == (byte)'}')
                {
                    return index;
                }
            }
        }

        return Math.Min(escapeIndex + 1, pattern.Length - 1);
    }

    private static int SkipRegexCharacterClass(ReadOnlySpan<byte> pattern, int classStart)
    {
        for (int index = classStart + 1; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\\')
            {
                index++;
                continue;
            }

            if (pattern[index] == (byte)']')
            {
                return index;
            }
        }

        return pattern.Length - 1;
    }

    private static bool TryReadRegexGroupPrefix(
        ReadOnlySpan<byte> pattern,
        int groupStart,
        out int contentStart)
    {
        contentStart = groupStart + 1;
        if (groupStart + 1 >= pattern.Length || pattern[groupStart + 1] != (byte)'?')
        {
            return false;
        }

        if (groupStart + 2 < pattern.Length && pattern[groupStart + 2] == (byte)':')
        {
            contentStart = groupStart + 3;
            return true;
        }

        if (TryReadRegexNamedCapturePrefix(pattern, groupStart + 2, out contentStart))
        {
            return true;
        }

        bool sawFlag = false;
        for (int index = groupStart + 2; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'-')
            {
                continue;
            }

            if (IsInlineRegexFlag(value))
            {
                sawFlag = true;
                continue;
            }

            if (value == (byte)':')
            {
                contentStart = index + 1;
                return sawFlag;
            }

            if (value == (byte)')')
            {
                contentStart = index + 1;
                return sawFlag;
            }

            return false;
        }

        return false;
    }

    private static bool TryReadRegexNamedCapturePrefix(ReadOnlySpan<byte> pattern, int prefixStart, out int contentStart)
    {
        contentStart = prefixStart;
        int nameStart;
        if (prefixStart + 1 < pattern.Length && pattern[prefixStart] == (byte)'P' && pattern[prefixStart + 1] == (byte)'<')
        {
            nameStart = prefixStart + 2;
        }
        else if (prefixStart < pattern.Length && pattern[prefixStart] == (byte)'<')
        {
            nameStart = prefixStart + 1;
        }
        else
        {
            return false;
        }

        for (int index = nameStart; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'>')
            {
                contentStart = index + 1;
                return true;
            }
        }

        return false;
    }

    private static int SkipRegexRepetition(ReadOnlySpan<byte> pattern, int repetitionStart)
    {
        int next = repetitionStart + 1;
        if (pattern[repetitionStart] == (byte)'{')
        {
            for (int index = repetitionStart + 1; index < pattern.Length; index++)
            {
                if (pattern[index] == (byte)'}')
                {
                    next = index + 1;
                    break;
                }
            }
        }

        if (next < pattern.Length && pattern[next] == (byte)'?')
        {
            next++;
        }

        return next - 1;
    }

    private static string BuildRegexParseError(ReadOnlySpan<byte> pattern, int offset, string error)
    {
        string displayPattern = "(?:" + BuildRegexErrorPatternDisplay(pattern) + ")";
        string caret = new string(' ', 4 + 3 + Math.Max(offset, 0)) + "^";
        return "regex parse error:\n    " + displayPattern + "\n" + caret + "\nerror: " + error;
    }

    private static string BuildRegexErrorPatternDisplay(ReadOnlySpan<byte> pattern)
    {
        var builder = new StringBuilder(pattern.Length);
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'\t')
            {
                builder.Append(@"\t");
            }
            else if (value == (byte)'\r')
            {
                builder.Append(@"\r");
            }
            else
            {
                builder.Append(value is >= 0x20 and <= 0x7e ? (char)value : '\uFFFD');
            }
        }

        return builder.ToString();
    }

    private static bool TryLoadPatternFile(OsString argument, List<byte[]> patterns, Stream standardInput, DiagnosticMessenger diagnostics)
    {
        if (!TryGetPathText(argument, diagnostics, out string path))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = path == "-"
                ? ReadAllBytes(standardInput)
                : File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: No such file or directory (os error 2)").WithContext("rg"));
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: No such file or directory (os error 2)").WithContext("rg"));
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: Permission denied (os error 13)").WithContext("rg"));
            return false;
        }
        catch (IOException exception)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: {exception.Message}").WithContext("rg"));
            return false;
        }

        return TryAddPatternFilePatterns(path, bytes, patterns, diagnostics);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool TryAddPatternFilePatterns(
        string path,
        byte[] bytes,
        List<byte[]> patterns,
        DiagnosticMessenger diagnostics)
    {
        int lineStart = 0;
        int lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line;
            if (lineFeed < 0)
            {
                line = remaining;
                lineStart = bytes.Length;
            }
            else
            {
                line = remaining[..lineFeed];
                if (!line.IsEmpty && line[^1] == (byte)'\r')
                {
                    line = line[..^1];
                }

                lineStart += lineFeed + 1;
            }

            if (!TryValidatePatternFileLine(path, lineNumber, line, diagnostics))
            {
                return false;
            }

            patterns.Add(line.ToArray());
            lineNumber++;
        }

        return true;
    }

    private static bool TryValidatePatternFileLine(
        string path,
        int lineNumber,
        ReadOnlySpan<byte> line,
        DiagnosticMessenger diagnostics)
    {
        if (!TryGetUtf8InvalidOffset(line, out int invalidOffset))
        {
            return true;
        }

        string escaped = EscapePatternFileLine(line);
        diagnostics.ErrorMessage(new ScoutError(
            $"{path}:{lineNumber}: found invalid UTF-8 in pattern at byte offset {invalidOffset}: {escaped} (disable Unicode mode and use hex escape sequences to match arbitrary bytes in a pattern, e.g., '(?-u)\\xFF')").WithContext("rg"));
        return false;
    }

    private static bool TryGetUtf8InvalidOffset(ReadOnlySpan<byte> bytes, out int invalidOffset)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                index++;
                continue;
            }

            int length = GetUtf8SequenceLength(bytes[index..]);
            if (length == 0 || index + length > bytes.Length)
            {
                invalidOffset = index;
                return true;
            }

            for (int continuation = 1; continuation < length; continuation++)
            {
                if (!IsUtf8Continuation(bytes[index + continuation]))
                {
                    invalidOffset = index;
                    return true;
                }
            }

            index += length;
        }

        invalidOffset = -1;
        return false;
    }

    private static int GetUtf8SequenceLength(ReadOnlySpan<byte> bytes)
    {
        byte first = bytes[0];
        if (first is >= 0xC2 and <= 0xDF)
        {
            return 2;
        }

        if (first == 0xE0)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0xA0 and <= 0xBF ? 3 : 0;
        }

        if (first is >= 0xE1 and <= 0xEC or >= 0xEE and <= 0xEF)
        {
            return 3;
        }

        if (first == 0xED)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0x80 and <= 0x9F ? 3 : 0;
        }

        if (first == 0xF0)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0x90 and <= 0xBF ? 4 : 0;
        }

        if (first is >= 0xF1 and <= 0xF3)
        {
            return 4;
        }

        if (first == 0xF4)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0x80 and <= 0x8F ? 4 : 0;
        }

        return 0;
    }

    private static bool IsUtf8Continuation(byte value)
    {
        return value is >= 0x80 and <= 0xBF;
    }

    private static string EscapePatternFileLine(ReadOnlySpan<byte> line)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < line.Length; index++)
        {
            byte value = line[index];
            if (value is >= 0x20 and <= 0x7E && value != (byte)'\\')
            {
                builder.Append((char)value);
            }
            else if (value == (byte)'\\')
            {
                builder.Append(@"\\");
            }
            else
            {
                builder.Append(@"\x");
                builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static byte[] GetPathBytes(string path, byte? pathSeparator)
    {
        byte[] bytes = Utf8.GetBytes(path);
        if (pathSeparator is not byte separator)
        {
            return bytes;
        }

        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'/' || (OperatingSystem.IsWindows() && bytes[index] == (byte)'\\'))
            {
                bytes[index] = separator;
            }
        }

        return bytes;
    }

    private static bool IsAsciiCaseInsensitive(IReadOnlyList<byte[]> pattern, CliCaseMode caseMode)
    {
        return caseMode == CliCaseMode.Insensitive
            || (caseMode == CliCaseMode.Smart && !ContainsAsciiUppercase(pattern));
    }

    private static bool ContainsAsciiUppercase(byte[] bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] is >= (byte)'A' and <= (byte)'Z')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAsciiUppercase(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ContainsAsciiUppercase(patterns[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPathText(OsString argument, DiagnosticMessenger diagnostics, out string path)
    {
        if (argument.TryGetText(out path))
        {
            return true;
        }

        diagnostics.ErrorMessage(new ScoutError("invalid CLI arguments").WithContext("rg"));
        return false;
    }

    private static bool ContainsDirectory(List<string> paths)
    {
        for (int index = 0; index < paths.Count; index++)
        {
            if (paths[index] != "-" && Directory.Exists(paths[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAutoMmapEligible(List<string> paths)
    {
        if (paths.Count == 0 || paths.Count > 10)
        {
            return false;
        }

        for (int index = 0; index < paths.Count; index++)
        {
            if (!File.Exists(paths[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetDirectoryDisplayPath(string rootArgument, string fullRoot, string fullPath)
    {
        string relative = Path.GetRelativePath(fullRoot, fullPath);
        string root = Path.TrimEndingDirectorySeparator(rootArgument);
        if (root.Length == 0)
        {
            root = rootArgument;
        }

        if (root == ".")
        {
            return "." + Path.DirectorySeparatorChar + relative;
        }

        return Path.Combine(root, relative);
    }

    private static string GetSearchDirectoryDisplayPath(string rootArgument, string fullRoot, string fullPath, bool defaultRoot)
    {
        if (defaultRoot)
        {
            return Path.GetRelativePath(fullRoot, fullPath);
        }

        return GetDirectoryDisplayPath(rootArgument, fullRoot, fullPath);
    }
}
