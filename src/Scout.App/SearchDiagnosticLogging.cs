using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Scout;

internal static class SearchDiagnosticLogging
{
    private const string FlagsCategory = "Scout.App.Flags";
    private const string RegexCategory = "Scout.Regex";
    private const string SearchCategory = "Scout.Searching";
    private const string SourceFile = "src/Scout.App/SearchDiagnosticLogging.cs";

    internal static void LogSearchConfiguration(
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
        // provenance: crates/core/flags/hiargs.rs:954
        logger.Debug(FlagsCategory, SourceFile, $"read CWD from environment: {Directory.GetCurrentDirectory()}");
        // provenance: crates/core/flags/hiargs.rs:1092
        logger.Debug(FlagsCategory, SourceFile, $"number of paths given to search: {pathCount}");
        // provenance: crates/core/flags/hiargs.rs:1103
        logger.Debug(FlagsCategory, SourceFile, $"is_one_file? {FormatBool(isOneFile)}");
        // provenance: crates/core/flags/hiargs.rs:1278
        logger.Debug(FlagsCategory, SourceFile, $"found hostname for hyperlink configuration: {GetLoggingHostname(lowArgs)}");
        // provenance: crates/core/flags/hiargs.rs:1288
        logger.Debug(FlagsCategory, SourceFile, $"hyperlink format: \"{EscapeLogString(lowArgs.HyperlinkFormat ?? string.Empty)}\"");
        // provenance: crates/core/flags/hiargs.rs:175
        logger.Debug(FlagsCategory, SourceFile, $"using {GetLoggingThreadCount(lowArgs, isOneFile)} thread(s)");
        LogDefaultDecompressionCommandAvailability(logger);
        if (patterns.Count > 0)
        {
            // provenance: crates/regex/src/config.rs:175
            logger.Debug(RegexCategory, SourceFile, $"assembling regex program from {patterns.Count} pattern(s)");
            // provenance: crates/regex/src/matcher.rs:66
            logger.Trace(RegexCategory, SourceFile, $"final regex: \"(?:{EscapeLogPattern(patterns[0])})\"");
            // provenance: crates/regex/src/literal.rs:74
            logger.Trace(RegexCategory, SourceFile, "skipping inner literal extraction, existing regex is believed to already be accelerated");
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

    private static bool TextEquals(OsString argument, string expected)
    {
        return argument.TryGetText(out string text) && string.Equals(text, expected, StringComparison.Ordinal);
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

    private static void LogDefaultDecompressionCommandAvailability(DiagnosticLogger logger)
    {
        for (int index = 0; index < CliDecompressionMatcher.DefaultCommands.Count; index++)
        {
            string program = CliDecompressionMatcher.DefaultCommands[index].Program;
            if (!CliDecompressionMatcher.TryResolveBinary(program, out _))
            {
                LogDecompressionCommandUnavailable(logger, program);
            }
        }
    }

    internal static void LogDecompressionCommandUnavailable(DiagnosticLogger logger, string program)
    {
        // provenance: crates/cli/src/decompress.rs:502
        logger.Debug(SearchCategory, SourceFile, $"{program}: could not find executable in PATH");
    }

    private static string EscapeLogString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string EscapeLogPattern(byte[] pattern)
    {
        return EscapeLogString(Encoding.UTF8.GetString(pattern));
    }

    internal static void LogTraceSearchPath(DiagnosticLogger logger, string path, SearchFileReadKind readKind)
    {
        if (!logger.IsTraceEnabled)
        {
            return;
        }

        // provenance: crates/core/search.rs:255
        logger.Trace(SearchCategory, SourceFile, $"{path}: binary detection: BinaryDetection(Convert(0))");
        if (readKind == SearchFileReadKind.MemoryMapped)
        {
            // provenance: crates/searcher/src/searcher/mod.rs:690
            logger.Trace(SearchCategory, SourceFile, $"Some(\"{EscapeLogString(path)}\"): searching via memory map");
            // provenance: crates/searcher/src/searcher/mod.rs:792
            logger.Trace(SearchCategory, SourceFile, "slice reader: searching via slice-by-line strategy");
        }
        else
        {
            // provenance: crates/searcher/src/searcher/mod.rs:711
            logger.Trace(SearchCategory, SourceFile, $"Some(\"{EscapeLogString(path)}\"): searching using generic reader");
            // provenance: crates/searcher/src/searcher/mod.rs:762
            logger.Trace(SearchCategory, SourceFile, "generic reader: searching via roll buffer strategy");
        }

        // provenance: crates/searcher/src/searcher/core.rs:67
        logger.Trace(SearchCategory, SourceFile, "searcher core: will use fast line searcher");
    }

    internal static string GetHyperlinkHost(CliLowArgs lowArgs, string? hyperlinkFormat)
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

}
