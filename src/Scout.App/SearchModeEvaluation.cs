namespace Scout;

/// <summary>
/// Evaluates quiet search modes without producing formatted output.
/// </summary>
internal static class SearchModeEvaluation
{
    internal static bool SearchQuiet(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> pattern,
        CliSearchMode searchMode,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount,
        bool crlf,
        bool nullData,
        RegexSearchPlan regexPlan)
    {
        if (maxCount == 0)
        {
            return false;
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return !LiteralLineSearcher.HasMatchWithRegexPlan(
                bytes,
                pattern,
                regexPlan,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxCount,
                crlf,
                nullData);
        }

        if (searchMode == CliSearchMode.CountMatches)
        {
            return LiteralLineSearcher.CountMatchesWithRegexPlan(
                bytes,
                pattern,
                regexPlan,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxCount,
                crlf,
                nullData) > 0;
        }

        if (searchMode == CliSearchMode.Count)
        {
            return LiteralLineSearcher.CountMatchingLinesWithRegexPlan(
                bytes,
                pattern,
                regexPlan,
                asciiCaseInsensitive,
                invertMatch,
                lineRegexp,
                wordRegexp,
                maxCount,
                crlf,
                nullData) > 0;
        }

        return LiteralLineSearcher.HasMatchWithRegexPlan(
            bytes,
            pattern,
            regexPlan,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxCount,
            crlf,
            nullData);
    }
}
