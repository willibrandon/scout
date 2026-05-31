namespace Scout;

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
