namespace Scout;

internal static class IgnoreDiagnosticLogging
{
    private const string IgnoreCategory = "Scout.Ignore";
    private const string GlobbingCategory = "Scout.Globbing";
    private const string IgnoreStackSourceFile = "src/Scout.Ignore/IgnoreStack.cs";
    private const string WalkSourceFile = "src/Scout.Ignore/Walk.cs";

    internal static void LogOpenedIgnoreFile(DiagnosticLogger logger, string path)
    {
        if (logger.IsDebugEnabled)
        {
            logger.Debug(IgnoreCategory, IgnoreStackSourceFile, $"opened ignore file: {path}");
        }
    }

    internal static void LogBuiltGlobSet(DiagnosticLogger logger, IgnoreGlobSetSummary summary)
    {
        if (logger.IsDebugEnabled)
        {
            logger.Debug(GlobbingCategory, IgnoreStackSourceFile, $"built glob set; {summary}");
        }
    }

    internal static void LogIgnoredByRule(DiagnosticLogger logger, DirEntry entry, IgnoreRule? rule)
    {
        if (!logger.IsDebugEnabled)
        {
            return;
        }

        string path = FormatPath(entry.FullPath);
        if (rule is null)
        {
            logger.Debug(IgnoreCategory, WalkSourceFile, $"ignoring {path}: matched ignore rule");
            return;
        }

        string source = string.IsNullOrEmpty(rule.SourcePath) ? "<unknown>" : rule.SourcePath;
        string kind = rule.IsWhitelist ? "whitelist" : "ignore";
        string directoryOnly = rule.IsDirectoryOnly ? ", directory-only" : string.Empty;
        logger.Debug(IgnoreCategory, WalkSourceFile, $"ignoring {path}: matched {kind} rule from {source}: \"{Escape(rule.OriginalText)}\" -> \"{Escape(rule.PatternText)}\"{directoryOnly}");
    }

    internal static void LogIgnored(DiagnosticLogger logger, DirEntry entry, string reason)
    {
        if (logger.IsDebugEnabled)
        {
            logger.Debug(IgnoreCategory, WalkSourceFile, $"ignoring {FormatPath(entry.FullPath)}: {reason}");
        }
    }

    private static string FormatPath(string path)
    {
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        return PathUtil.IsRelativePathOutsideBase(relative) ? path : relative;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
