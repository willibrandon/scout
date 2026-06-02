using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Scout;

internal static class SearchDiagnosticLogging
{
    private const string DefaultRipgrepSourceRoot = "/Users/brandon/src/ripgrep";

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
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 954, $"read CWD from environment: {Directory.GetCurrentDirectory()}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1092, $"number of paths given to search: {pathCount}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1103, $"is_one_file? {FormatBool(isOneFile)}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1278, $"found hostname for hyperlink configuration: {GetLoggingHostname(lowArgs)}");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 1288, $"hyperlink format: \"{EscapeLogString(lowArgs.HyperlinkFormat ?? string.Empty)}\"");
        logger.Debug("rg::flags::hiargs", "crates/core/flags/hiargs.rs", 175, $"using {GetLoggingThreadCount(lowArgs, isOneFile)} thread(s)");
        LogGlobalIgnoreConfiguration(logger, lowArgs.RespectGitIgnoreFiles && lowArgs.RespectGlobalIgnoreFiles);
        if (patterns.Count > 0)
        {
            logger.Debug("grep_regex::config", RipgrepSourcePath("crates/regex/src/config.rs"), 175, $"assembling HIR from {patterns.Count} fixed string literals");
            logger.Trace("grep_regex::matcher", RipgrepSourcePath("crates/regex/src/matcher.rs"), 66, $"final regex: \"(?:{EscapeLogPattern(patterns[0])})\"");
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

    internal static void LogTraceSearchPath(DiagnosticLogger logger, string path, SearchFileReadKind readKind)
    {
        logger.Trace("rg::search", "crates/core/search.rs", 255, $"{path}: binary detection: BinaryDetection(Convert(0))");
        if (readKind == SearchFileReadKind.MemoryMapped)
        {
            logger.Trace("grep_searcher::searcher", RipgrepSourcePath("crates/searcher/src/searcher/mod.rs"), 690, $"Some(\"{EscapeLogString(path)}\"): searching via memory map");
            return;
        }

        logger.Trace("grep_searcher::searcher", RipgrepSourcePath("crates/searcher/src/searcher/mod.rs"), 711, $"Some(\"{EscapeLogString(path)}\"): searching using generic reader");
        logger.Trace("grep_searcher::searcher", RipgrepSourcePath("crates/searcher/src/searcher/mod.rs"), 762, "generic reader: searching via roll buffer strategy");
        logger.Trace("grep_searcher::searcher::core", RipgrepSourcePath("crates/searcher/src/searcher/core.rs"), 67, "searcher core: will use fast line searcher");
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

    private static string RipgrepSourcePath(string relativePath)
    {
        string sourceRoot = ProcessEnvironment.GetVariable("SCOUT_RIPGREP_SOURCE_ROOT")
            ?? ProcessEnvironment.GetVariable("SCOUT_RIPGREP_REFERENCE")
            ?? DefaultRipgrepSourceRoot;
        sourceRoot = sourceRoot.TrimEnd('/', '\\');
        if (sourceRoot.Length == 0)
        {
            return relativePath;
        }

        return sourceRoot.Replace('\\', '/') + "/" + relativePath;
    }
}
