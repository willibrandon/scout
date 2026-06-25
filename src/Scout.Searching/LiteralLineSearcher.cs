using System.Text;

namespace Scout;

/// <summary>
/// Searches byte slices line-by-line for byte patterns.
/// </summary>
public static class LiteralLineSearcher
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const int RegexAtomLiteral = 0;
    private const int RegexAtomDot = 1;
    private const int RegexAtomClass = 2;
    private const int RegexAtomStartAnchor = 3;
    private const int RegexAtomEndAnchor = 4;
    private const int RegexAtomGroup = 5;
    private const int RegexAtomDigit = 6;
    private const int RegexAtomNotDigit = 7;
    private const int RegexAtomWord = 8;
    private const int RegexAtomNotWord = 9;
    private const int RegexAtomWhitespace = 10;
    private const int RegexAtomNotWhitespace = 11;
    private const int RegexAtomWordBoundary = 12;
    private const int RegexAtomNotWordBoundary = 13;
    private const int RegexAtomWordStartBoundary = 14;
    private const int RegexAtomWordEndBoundary = 15;
    private const int RegexAtomWordStartHalfBoundary = 16;
    private const int RegexAtomWordEndHalfBoundary = 17;
    private const int RegexAtomEnableCaseInsensitive = 18;
    private const int RegexAtomDisableCaseInsensitive = 19;
    private const int RegexAtomCaseInsensitiveGroup = 20;
    private const int RegexAtomCaseSensitiveGroup = 21;
    private const int RegexAtomEnableIgnoreWhitespace = 22;
    private const int RegexAtomDisableIgnoreWhitespace = 23;
    private const int RegexAtomIgnoreWhitespaceGroup = 24;
    private const int RegexAtomSignificantWhitespaceGroup = 25;
    private const int RegexAtomEnableSwapGreed = 26;
    private const int RegexAtomDisableSwapGreed = 27;
    private const int RegexAtomSwapGreedGroup = 28;
    private const int RegexAtomStandardGreedGroup = 29;
    private const int RegexClassAlnum = 30;
    private const int RegexClassAlpha = 31;
    private const int RegexClassAscii = 32;
    private const int RegexClassBlank = 33;
    private const int RegexClassControl = 34;
    private const int RegexClassGraph = 35;
    private const int RegexClassLower = 36;
    private const int RegexClassPrint = 37;
    private const int RegexClassPunct = 38;
    private const int RegexClassUpper = 39;
    private const int RegexClassHexDigit = 40;

    /// <summary>
    /// Searches <paramref name="haystack" /> for lines containing <paramref name="needle" />.
    /// </summary>
    /// <typeparam name="TSink">The line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="sink">The sink that receives matching lines.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should match.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one line matched.</returns>
    public static bool Search<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, ILineSink
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            needle.Length != 0)
        {
            return SearchLiteralRegexLines(haystack, needle, ref sink, asciiCaseInsensitive, maxMatchingLines, nullData);
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (TryMatchLine(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out int matchStart))
            {
                sink.MatchedLine(lineNumber, lineStart, matchStart < 0 ? 0 : matchStart + 1, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> for lines containing any pattern in <paramref name="needles" />.
    /// </summary>
    /// <typeparam name="TSink">The line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="sink">The sink that receives matching lines.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should match.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <param name="requireMatchColumn">Whether the sink requires the earliest match column for selected lines.</param>
    /// <returns><see langword="true" /> when at least one line matched.</returns>
    public static bool Search<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            needles.Count == 1 &&
            IsLiteralRegex(needles[0]) &&
            needles[0].Length != 0)
        {
            return SearchLiteralRegexLines(haystack, needles[0], ref sink, asciiCaseInsensitive, maxMatchingLines, nullData);
        }

        var regexPlan = RegexSearchPlan.Create(needles, asciiCaseInsensitive);
        return SearchWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData,
            requireMatchColumn);
    }

    internal static RegexSearchPlan? CreateRegexSearchPlan(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive, bool compileAutomata)
    {
        return RegexSearchPlan.Create(needles, asciiCaseInsensitive, compileAutomata);
    }

    internal static bool SearchWithRegexPlan<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (TryGetWholeHaystackLiteralSetRegexPlan(needles, regexPlan, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out RegexLiteralSetEngine literalSetEngine))
        {
            return SearchLiteralSetRegexLines(
                haystack,
                literalSetEngine,
                ref sink,
                out _,
                countSearchedLines: false,
                maxMatchingLines,
                requireMatchColumn);
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            !crlf &&
            !nullData &&
            needles.Count == 1 &&
            regexPlan?.GetAccelerator(0) is RegexClassSequenceAccelerator accelerator)
        {
            return SearchAcceleratedRegexLines(haystack, needles, regexPlan, accelerator, ref sink, asciiCaseInsensitive, maxMatchingLines, requireMatchColumn);
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            !crlf &&
            !nullData &&
            needles.Count == 1 &&
            regexPlan?.GetCandidateLineAccelerator(0) is RegexCandidateLineAccelerator candidateLineAccelerator)
        {
            return SearchCandidateRegexLines(
                haystack,
                needles,
                regexPlan,
                candidateLineAccelerator,
                ref sink,
                out _,
                countSearchedLines: false,
                asciiCaseInsensitive,
                maxMatchingLines,
                requireMatchColumn);
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (TryMatchLine(line, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out int matchStart))
            {
                sink.MatchedLine(lineNumber, lineStart, matchStart < 0 ? 0 : matchStart + 1, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    internal static bool SearchWithRegexPlanAndCountMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        out ulong matchedLines,
        out long matches,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        var countingSink = new RegexPlanCountingLineSink<TSink>(
            sink,
            needles,
            regexPlan,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            countMatches: !invertMatch);
        bool matched = SearchWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref countingSink,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData,
            requireMatchColumn);

        sink = countingSink.Inner;
        matchedLines = countingSink.MatchedLines;
        matches = countingSink.Matches;
        return matched;
    }

    internal static bool SearchWithRegexPlanAndCountLines<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        out long searchedLines,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false,
        bool requireMatchColumn = true)
        where TSink : struct, ILineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        searchedLines = 0;
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (TryGetWholeHaystackLiteralSetRegexPlan(needles, regexPlan, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out RegexLiteralSetEngine literalSetEngine))
        {
            return SearchLiteralSetRegexLines(
                haystack,
                literalSetEngine,
                ref sink,
                out searchedLines,
                countSearchedLines: true,
                maxMatchingLines,
                requireMatchColumn);
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            !crlf &&
            !nullData &&
            needles.Count == 1 &&
            regexPlan?.GetAccelerator(0) is RegexClassSequenceAccelerator accelerator)
        {
            return SearchAcceleratedRegexLines(
                haystack,
                needles,
                regexPlan,
                accelerator,
                ref sink,
                out searchedLines,
                countSearchedLines: true,
                asciiCaseInsensitive: asciiCaseInsensitive,
                maxMatchingLines: maxMatchingLines,
                requireMatchColumn: requireMatchColumn);
        }

        if (!invertMatch &&
            !lineRegexp &&
            !wordRegexp &&
            !crlf &&
            !nullData &&
            needles.Count == 1 &&
            regexPlan?.GetCandidateLineAccelerator(0) is RegexCandidateLineAccelerator candidateLineAccelerator)
        {
            return SearchCandidateRegexLines(
                haystack,
                needles,
                regexPlan,
                candidateLineAccelerator,
                ref sink,
                out searchedLines,
                countSearchedLines: true,
                asciiCaseInsensitive,
                maxMatchingLines,
                requireMatchColumn);
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (TryMatchLine(line, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out int matchStart))
            {
                sink.MatchedLine(lineNumber, lineStart, matchStart < 0 ? 0 : matchStart + 1, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    searchedLines = lineNumber;
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        searchedLines = lineNumber - 1;
        return matched;
    }

    internal static long CountMatchingLinesWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        var sink = new CountingLineSink();
        if (TryGetWholeHaystackLiteralSetRegexPlan(needles, regexPlan, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out RegexLiteralSetEngine literalSetEngine))
        {
            return CountMatchingLinesWithLiteralSetRegex(haystack, literalSetEngine, maxMatchingLines);
        }

        _ = SearchWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            invertMatch,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData,
            requireMatchColumn: false);
        return (long)sink.MatchedLines;
    }

    private static bool SearchAcceleratedRegexLines<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan regexPlan,
        RegexClassSequenceAccelerator accelerator,
        ref TSink sink,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool requireMatchColumn)
        where TSink : struct, ILineSink
    {
        return SearchAcceleratedRegexLines(
            haystack,
            needles,
            regexPlan,
            accelerator,
            ref sink,
            out _,
            countSearchedLines: false,
            asciiCaseInsensitive: asciiCaseInsensitive,
            maxMatchingLines: maxMatchingLines,
            requireMatchColumn: requireMatchColumn);
    }

    private static bool SearchAcceleratedRegexLines<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan regexPlan,
        RegexClassSequenceAccelerator accelerator,
        ref TSink sink,
        out long searchedLines,
        bool countSearchedLines,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool requireMatchColumn)
        where TSink : struct, ILineSink
    {
        if (!requireMatchColumn)
        {
            return SearchAcceleratedRegexLinesWithoutMatchColumn(
                haystack,
                needles,
                regexPlan,
                accelerator,
                ref sink,
                out searchedLines,
                countSearchedLines,
                asciiCaseInsensitive,
                maxMatchingLines);
        }

        if (accelerator.CanUseLineByLineSearch && requireMatchColumn)
        {
            return SearchAcceleratedRegexLinesByLine(
                haystack,
                needles,
                regexPlan,
                accelerator,
                ref sink,
                out searchedLines,
                countSearchedLines,
                asciiCaseInsensitive,
                maxMatchingLines,
                requireMatchColumn);
        }

        searchedLines = 0;
        bool matched = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int countedOffset = 0;
        int nextNonAscii = IndexOfNonAscii(haystack, offset: 0);
        long lineNumber = 1;
        while (searchOffset < haystack.Length)
        {
            bool foundMatch = accelerator.TryFind(haystack, searchOffset, out int matchStart, out _);
            int nonAscii = nextNonAscii >= searchOffset ? nextNonAscii : -1;
            if (!foundMatch && nonAscii < 0)
            {
                if (countSearchedLines)
                {
                    searchedLines = lineNumber - 1 + CountSearchLines(haystack[countedOffset..]);
                }

                return matched;
            }

            bool canUseAsciiMatch = foundMatch && CanUseAsciiRegexMatch(haystack, matchStart, nonAscii, requireMatchColumn);
            int eventOffset = canUseAsciiMatch ? matchStart : nonAscii;
            int lineStart = GetSearchLineStart(haystack, eventOffset);
            int lineEnd = GetSearchLineEnd(haystack, lineStart);
            lineNumber += CountLineTerminators(haystack.Slice(countedOffset, lineStart - countedOffset));

            bool lineMatched;
            int lineMatchStart;
            if (foundMatch && matchStart < lineEnd && canUseAsciiMatch)
            {
                lineMatched = true;
                lineMatchStart = matchStart - lineStart;
            }
            else
            {
                ReadOnlySpan<byte> line = haystack.Slice(lineStart, lineEnd - lineStart);
                lineMatched = accelerator.TryFindUnicode(line, offset: 0, out lineMatchStart, out _, out bool completedUnicodeSearch);
                if (!completedUnicodeSearch)
                {
                    lineMatched = TryMatchLine(
                        line,
                        needles,
                        regexPlan,
                        asciiCaseInsensitive,
                        invertMatch: false,
                        lineRegexp: false,
                        wordRegexp: false,
                        crlf: false,
                        nullData: false,
                        out lineMatchStart);
                }
            }

            if (lineMatched)
            {
                sink.MatchedLine(lineNumber, lineStart, lineMatchStart < 0 ? 0 : lineMatchStart + 1, haystack.Slice(lineStart, lineEnd - lineStart));
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    if (countSearchedLines)
                    {
                        searchedLines = lineNumber;
                    }

                    return true;
                }
            }

            searchOffset = lineEnd;
            countedOffset = lineEnd;
            if (nextNonAscii >= 0 && nextNonAscii < searchOffset)
            {
                nextNonAscii = IndexOfNonAscii(haystack, searchOffset);
            }

            lineNumber++;
        }

        if (countSearchedLines)
        {
            searchedLines = lineNumber - 1 + CountSearchLines(haystack[countedOffset..]);
        }

        return matched;
    }

    private static bool SearchAcceleratedRegexLinesWithoutMatchColumn<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan regexPlan,
        RegexClassSequenceAccelerator accelerator,
        ref TSink sink,
        out long searchedLines,
        bool countSearchedLines,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines)
        where TSink : struct, ILineSink
    {
        searchedLines = 0;
        bool matched = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int countedOffset = 0;
        int nextNonAscii = IndexOfNonAscii(haystack, offset: 0);
        long lineNumber = 1;
        while (searchOffset < haystack.Length)
        {
            int asciiSearchEnd = nextNonAscii >= searchOffset
                ? GetSearchLineStart(haystack, nextNonAscii)
                : haystack.Length;
            if (asciiSearchEnd > searchOffset &&
                accelerator.TryFind(haystack[..asciiSearchEnd], searchOffset, out int matchStart, out _))
            {
                int lineStart = GetSearchLineStart(haystack, matchStart);
                int lineEnd = GetSearchLineEnd(haystack, lineStart);
                lineNumber += CountLineTerminators(haystack.Slice(countedOffset, lineStart - countedOffset));
                sink.MatchedLine(lineNumber, lineStart, matchColumn: 0, haystack.Slice(lineStart, lineEnd - lineStart));
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    if (countSearchedLines)
                    {
                        searchedLines = lineNumber;
                    }

                    return true;
                }

                searchOffset = lineEnd;
                countedOffset = lineEnd;
                if (nextNonAscii >= 0 && nextNonAscii < searchOffset)
                {
                    nextNonAscii = IndexOfNonAscii(haystack, searchOffset);
                }

                lineNumber++;
                continue;
            }

            if (nextNonAscii < searchOffset)
            {
                if (countSearchedLines)
                {
                    searchedLines = lineNumber - 1 + CountSearchLines(haystack[countedOffset..]);
                }

                return matched;
            }

            int nonAsciiLineStart = GetSearchLineStart(haystack, nextNonAscii);
            int nonAsciiLineEnd = GetSearchLineEnd(haystack, nonAsciiLineStart);
            lineNumber += CountLineTerminators(haystack.Slice(countedOffset, nonAsciiLineStart - countedOffset));
            ReadOnlySpan<byte> line = haystack.Slice(nonAsciiLineStart, nonAsciiLineEnd - nonAsciiLineStart);
            bool lineMatched = accelerator.TryFind(line, offset: 0, out int lineMatchStart, out _);
            if (!lineMatched)
            {
                lineMatched = accelerator.TryFindUnicode(line, offset: 0, out lineMatchStart, out _, out bool completedUnicodeSearch);
                if (!completedUnicodeSearch)
                {
                    lineMatched = TryMatchLine(
                        line,
                        needles,
                        regexPlan,
                        asciiCaseInsensitive,
                        invertMatch: false,
                        lineRegexp: false,
                        wordRegexp: false,
                        crlf: false,
                        nullData: false,
                        out lineMatchStart);
                }
            }

            if (lineMatched)
            {
                sink.MatchedLine(lineNumber, nonAsciiLineStart, lineMatchStart < 0 ? 0 : lineMatchStart + 1, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    if (countSearchedLines)
                    {
                        searchedLines = lineNumber;
                    }

                    return true;
                }
            }

            searchOffset = nonAsciiLineEnd;
            countedOffset = nonAsciiLineEnd;
            do
            {
                nextNonAscii = IndexOfNonAscii(haystack, nextNonAscii + 1);
            }
            while (nextNonAscii >= 0 && nextNonAscii < searchOffset);

            lineNumber++;
        }

        if (countSearchedLines)
        {
            searchedLines = lineNumber - 1 + CountSearchLines(haystack[countedOffset..]);
        }

        return matched;
    }

    private static bool SearchCandidateRegexLines<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan regexPlan,
        RegexCandidateLineAccelerator accelerator,
        ref TSink sink,
        out long searchedLines,
        bool countSearchedLines,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool requireMatchColumn)
        where TSink : struct, ILineSink
    {
        searchedLines = 0;
        bool matched = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (searchOffset < haystack.Length)
        {
            int candidate = accelerator.FindCandidate(haystack, searchOffset);
            if (candidate < 0)
            {
                if (countSearchedLines)
                {
                    searchedLines = lineNumber - 1 + CountSearchLines(haystack[lineStart..]);
                }

                return matched;
            }

            if (candidate > lineStart)
            {
                ReadOnlySpan<byte> skipped = haystack[lineStart..candidate];
                int previousTerminator = skipped.LastIndexOf((byte)'\n');
                if (previousTerminator >= 0)
                {
                    lineNumber += CountLineTerminators(skipped);
                    lineStart += previousTerminator + 1;
                }
            }

            int lineLength = GetLineLength(haystack[lineStart..], nullData: false);
            int lineEnd = lineStart + lineLength;
            ReadOnlySpan<byte> line = haystack[lineStart..lineEnd];
            if (TryMatchCandidateRegexLine(
                    haystack,
                    lineStart,
                    lineEnd,
                    candidate,
                    needles,
                    regexPlan,
                    accelerator,
                    asciiCaseInsensitive,
                    requireMatchColumn,
                    out int matchStart))
            {
                int matchColumn = requireMatchColumn && matchStart >= 0 ? matchStart + 1 : 0;
                sink.MatchedLine(lineNumber, lineStart, matchColumn, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    if (countSearchedLines)
                    {
                        searchedLines = lineNumber;
                    }

                    return true;
                }
            }

            searchOffset = lineEnd;
            lineStart = lineEnd;
            lineNumber++;
        }

        if (countSearchedLines)
        {
            searchedLines = lineNumber - 1 + CountSearchLines(haystack[lineStart..]);
        }

        return matched;
    }

    private static bool TryMatchCandidateRegexLine(
        ReadOnlySpan<byte> haystack,
        int lineStart,
        int lineEnd,
        int firstCandidate,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan regexPlan,
        RegexCandidateLineAccelerator accelerator,
        bool asciiCaseInsensitive,
        bool requireMatchColumn,
        out int matchStart)
    {
        RegexAutomaton? automaton = regexPlan.GetAutomaton(0);
        ReadOnlySpan<byte> line = haystack[lineStart..lineEnd];
        ReadOnlySpan<byte> automatonLine = TrimAutomatonLineTerminator(line);
        for (int candidate = firstCandidate; candidate >= 0 && candidate < lineEnd;)
        {
            int offset = candidate - lineStart;
            if (accelerator.HasVerifier)
            {
                if (accelerator.TryMatchAt(automatonLine, offset, out _, out bool completed))
                {
                    matchStart = requireMatchColumn ? offset : 0;
                    return true;
                }

                if (completed)
                {
                    candidate = FindNextCandidateInLine(accelerator, line, lineStart, offset);
                    continue;
                }
            }

            if (automaton is not null && offset <= automatonLine.Length)
            {
                RegexMatch? match = automaton.MatchAt(automatonLine, offset);
                if (match.HasValue)
                {
                    matchStart = requireMatchColumn ? match.Value.Start : 0;
                    return true;
                }

                candidate = FindNextCandidateInLine(accelerator, line, lineStart, offset);
                continue;
            }

            if (TryFindPatternMatch(
                    haystack: line,
                    needles: needles,
                    offset: offset,
                    asciiCaseInsensitive: asciiCaseInsensitive,
                    lineRegexp: false,
                    wordRegexp: false,
                    crlf: false,
                    nullData: false,
                    regexPlan: regexPlan,
                    out matchStart,
                    out _))
            {
                return true;
            }

            candidate = FindNextCandidateInLine(accelerator, line, lineStart, offset);
        }

        matchStart = -1;
        return false;
    }

    private static int FindNextCandidateInLine(
        RegexCandidateLineAccelerator accelerator,
        ReadOnlySpan<byte> line,
        int lineStart,
        int offset)
    {
        int nextOffset = accelerator.FindCandidate(line, offset + 1);
        return nextOffset >= 0 ? lineStart + nextOffset : -1;
    }

    private static bool SearchAcceleratedRegexLinesByLine<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan regexPlan,
        RegexClassSequenceAccelerator accelerator,
        ref TSink sink,
        out long searchedLines,
        bool countSearchedLines,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool requireMatchColumn)
        where TSink : struct, ILineSink
    {
        searchedLines = 0;
        bool matched = false;
        ulong matchedLines = 0;
        long lineNumber = 1;
        int lineStart = 0;
        int nextNonAscii = IndexOfNonAscii(haystack, offset: 0);
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData: false);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            bool lineMatched = accelerator.TryFind(line, offset: 0, out int lineMatchStart, out _);
            int lineEnd = lineStart + lineLength;
            if (nextNonAscii >= lineStart &&
                nextNonAscii < lineEnd &&
                (!lineMatched || (requireMatchColumn && nextNonAscii - lineStart < lineMatchStart)))
            {
                lineMatched = accelerator.TryFindUnicode(line, offset: 0, out lineMatchStart, out _, out bool completedUnicodeSearch);
                if (!completedUnicodeSearch)
                {
                    lineMatched = TryMatchLine(
                        line,
                        needles,
                        regexPlan,
                        asciiCaseInsensitive,
                        invertMatch: false,
                        lineRegexp: false,
                        wordRegexp: false,
                        crlf: false,
                        nullData: false,
                        out lineMatchStart);
                }
            }

            while (nextNonAscii >= 0 && nextNonAscii < lineEnd)
            {
                nextNonAscii = IndexOfNonAscii(haystack, nextNonAscii + 1);
            }

            if (lineMatched)
            {
                sink.MatchedLine(lineNumber, lineStart, lineMatchStart < 0 ? 0 : lineMatchStart + 1, line);
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    if (countSearchedLines)
                    {
                        searchedLines = lineNumber;
                    }

                    return true;
                }
            }

            lineStart = lineEnd;
            lineNumber++;
        }

        if (countSearchedLines)
        {
            searchedLines = lineNumber - 1;
        }

        return matched;
    }

    private static bool SearchLiteralSetRegexLines<TSink>(
        ReadOnlySpan<byte> haystack,
        RegexLiteralSetEngine literalSetEngine,
        ref TSink sink,
        out long searchedLines,
        bool countSearchedLines,
        ulong? maxMatchingLines,
        bool requireMatchColumn)
        where TSink : struct, ILineSink
    {
        searchedLines = 0;
        bool matched = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (searchOffset < haystack.Length)
        {
            RegexMatch? match = literalSetEngine.Find(haystack, searchOffset);
            if (!match.HasValue)
            {
                if (countSearchedLines)
                {
                    searchedLines = lineNumber - 1 + CountSearchLines(haystack[lineStart..]);
                }

                return matched;
            }

            int matchStart = match.Value.Start;
            ReadOnlySpan<byte> skipped = haystack.Slice(lineStart, matchStart - lineStart);
            int previousTerminator = skipped.LastIndexOf((byte)'\n');
            if (previousTerminator >= 0)
            {
                lineNumber += ByteCounter.Count(skipped, (byte)'\n');
                lineStart += previousTerminator + 1;
            }

            int lineLength = GetLineLength(haystack[lineStart..], nullData: false);
            int matchColumn = requireMatchColumn ? matchStart - lineStart + 1 : 0;
            sink.MatchedLine(lineNumber, lineStart, matchColumn, haystack.Slice(lineStart, lineLength));
            matched = true;
            matchedLines++;
            if (maxMatchingLines is ulong limit && matchedLines >= limit)
            {
                if (countSearchedLines)
                {
                    searchedLines = lineNumber;
                }

                return true;
            }

            searchOffset = lineStart + lineLength;
            lineStart = searchOffset;
            lineNumber++;
        }

        if (countSearchedLines)
        {
            searchedLines = lineNumber - 1;
        }

        return matched;
    }

    private static long CountMatchingLinesWithLiteralSetRegex(
        ReadOnlySpan<byte> haystack,
        RegexLiteralSetEngine literalSetEngine,
        ulong? maxMatchingLines)
    {
        long count = 0;
        int searchOffset = 0;
        int lineStart = 0;
        while (searchOffset < haystack.Length)
        {
            RegexMatch? match = literalSetEngine.Find(haystack, searchOffset);
            if (!match.HasValue)
            {
                return count;
            }

            int matchStart = match.Value.Start;
            ReadOnlySpan<byte> skipped = haystack.Slice(lineStart, matchStart - lineStart);
            int previousTerminator = skipped.LastIndexOf((byte)'\n');
            if (previousTerminator >= 0)
            {
                lineStart += previousTerminator + 1;
            }

            count++;
            if (maxMatchingLines is ulong limit && (ulong)count >= limit)
            {
                return count;
            }

            int lineLength = GetLineLength(haystack[lineStart..], nullData: false);
            searchOffset = lineStart + lineLength;
            lineStart = searchOffset;
        }

        return count;
    }

    private static bool SearchLiteralRegexLines<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool nullData)
        where TSink : struct, ILineSink
    {
        bool matched = false;
        ulong matchedLines = 0;
        int searchOffset = 0;
        int lineStart = 0;
        long lineNumber = 1;
        byte terminator = GetLineTerminator(nullData);
        while (searchOffset < haystack.Length)
        {
            int found = Find(haystack[searchOffset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matched;
            }

            int matchStart = searchOffset + found;
            ReadOnlySpan<byte> skipped = haystack.Slice(lineStart, matchStart - lineStart);
            int previousTerminator = skipped.LastIndexOf(terminator);
            if (previousTerminator >= 0)
            {
                lineNumber += ByteCounter.Count(skipped, terminator);
                lineStart += previousTerminator + 1;
            }

            int lineLength = GetLineLength(haystack[lineStart..], nullData);
            sink.MatchedLine(lineNumber, lineStart, matchStart - lineStart + 1, haystack.Slice(lineStart, lineLength));
            matched = true;
            matchedLines++;
            if (maxMatchingLines is ulong limit && matchedLines >= limit)
            {
                return true;
            }

            searchOffset = lineStart + lineLength;
            lineStart = searchOffset;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Tests whether any line contains <paramref name="needle" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should match.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to consider, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when any line matches.</returns>
    public static bool HasMatch(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (invertMatch || lineRegexp || wordRegexp)
        {
            return HasMatchingLine(haystack, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData);
        }

        return Find(haystack, needle, asciiCaseInsensitive) >= 0;
    }

    /// <summary>
    /// Tests whether any line contains any pattern in <paramref name="needles" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should match.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to consider, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when any line matches.</returns>
    public static bool HasMatch(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        var regexPlan = RegexSearchPlan.Create(needles, asciiCaseInsensitive);
        return HasMatchWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    internal static bool HasMatchWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        if (TryGetWholeHaystackLiteralSetRegexPlan(needles, regexPlan, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out RegexLiteralSetEngine literalSetEngine))
        {
            return literalSetEngine.Find(haystack, startAt: 0).HasValue;
        }

        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (TryMatchLine(line, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out _))
            {
                return true;
            }

            lineStart += lineLength;
        }

        return false;
    }

    /// <summary>
    /// Counts lines containing <paramref name="needle" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should match.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matching lines.</returns>
    public static long CountMatchingLines(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (LineMatches(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData))
            {
                count++;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    /// <summary>
    /// Counts lines containing any pattern in <paramref name="needles" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should match.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matching lines.</returns>
    public static long CountMatchingLines(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        var regexPlan = RegexSearchPlan.Create(needles, asciiCaseInsensitive);
        return CountMatchingLinesWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="needle" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without the pattern should be counted instead of occurrences.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count within, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public static long CountMatches(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        if (invertMatch || lineRegexp)
        {
            return CountMatchingLines(haystack, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
        }

        if (wordRegexp)
        {
            return CountWordMatchesByLine(haystack, needle, asciiCaseInsensitive, maxMatchingLines, crlf, nullData);
        }

        if (maxMatchingLines is not null)
        {
            return CountLiteralMatchesByLine(haystack, needle, asciiCaseInsensitive, maxMatchingLines.Value, nullData);
        }

        if (needle.IsEmpty)
        {
            return haystack.Length;
        }

        long count = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return count;
            }

            count++;
            offset += found + needle.Length;
        }

        return count;
    }

    /// <summary>
    /// Counts non-overlapping occurrences of any pattern in <paramref name="needles" />.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="invertMatch">Whether lines without any pattern should be counted instead of occurrences.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to count within, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public static long CountMatches(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        var regexPlan = RegexSearchPlan.Create(needles, asciiCaseInsensitive);
        return CountMatchesWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    internal static long CountMatchesWithRegexPlan(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive = false,
        bool invertMatch = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return 0;
        }

        if (invertMatch || lineRegexp)
        {
            return CountMatchingLinesWithRegexPlan(haystack, needles, regexPlan, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, maxMatchingLines, crlf, nullData);
        }

        if (!wordRegexp &&
            TryGetWholeHaystackLiteralSetRegexPlan(needles, regexPlan, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out RegexLiteralSetEngine literalSetEngine))
        {
            return maxMatchingLines.HasValue
                ? CountLiteralSetMatchesByLine(haystack, literalSetEngine, maxMatchingLines.Value)
                : literalSetEngine.CountMatches(haystack, startAt: 0);
        }

        return CountPatternMatchesByLine(haystack, needles, regexPlan, asciiCaseInsensitive, wordRegexp, maxMatchingLines, crlf, nullData);
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match.
    /// </summary>
    /// <typeparam name="TSink">The match sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="sink">The sink that receives matching bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchSink
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (SearchLineMatches(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData))
            {
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match for any pattern in <paramref name="needles" />.
    /// </summary>
    /// <typeparam name="TSink">The match sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="sink">The sink that receives matching bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatches<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        var regexPlan = RegexSearchPlan.Create(needles, asciiCaseInsensitive, compileAutomata: true);
        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (SearchLineMatches(line, lineStart, lineNumber, needles, regexPlan, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData))
            {
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match with its containing line.
    /// </summary>
    /// <typeparam name="TSink">The match-line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="sink">The sink that receives matching lines and bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatchLines<TSink>(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchLineSink
    {
        if (maxMatchingLines == 0)
        {
            return false;
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (SearchLineMatchLines(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData))
            {
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Searches <paramref name="haystack" /> and emits each non-overlapping match with its containing line.
    /// </summary>
    /// <typeparam name="TSink">The match-line sink type.</typeparam>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="sink">The sink that receives matching lines and bytes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="maxMatchingLines">The maximum matching lines to emit matches from, or <see langword="null" /> for no limit.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns><see langword="true" /> when at least one match was emitted.</returns>
    public static bool SearchMatchLines<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchLineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        var regexPlan = RegexSearchPlan.Create(needles, asciiCaseInsensitive, compileAutomata: true);
        return SearchMatchLinesWithRegexPlan(
            haystack,
            needles,
            regexPlan,
            ref sink,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            maxMatchingLines,
            crlf,
            nullData);
    }

    internal static bool SearchMatchLinesWithRegexPlan<TSink>(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        ulong? maxMatchingLines = null,
        bool crlf = false,
        bool nullData = false)
        where TSink : struct, IMatchLineSink
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (maxMatchingLines == 0)
        {
            return false;
        }

        bool matched = false;
        ulong matchedLines = 0;
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (SearchLineMatchLines(line, lineStart, lineNumber, needles, regexPlan, ref sink, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData))
            {
                matched = true;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return true;
                }
            }

            lineStart += lineLength;
            lineNumber++;
        }

        return matched;
    }

    /// <summary>
    /// Counts non-overlapping matches in a single line.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches in the line.</returns>
    public static long CountLineMatches(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        return CountLineMatchesAfterColumn(line, needle, column: 0, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
    }

    /// <summary>
    /// Counts non-overlapping matches for any pattern in <paramref name="needles" /> in a single line.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches in the line.</returns>
    public static long CountLineMatches(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        return CountLineMatchesAfterColumn(line, needles, column: 0, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
    }

    /// <summary>
    /// Counts non-overlapping matches in a single line whose one-based column is greater than <paramref name="column" />.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needle">The literal byte pattern to find.</param>
    /// <param name="column">The one-based column threshold.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether the pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether the pattern must match at word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches starting after the column threshold.</returns>
    public static long CountLineMatchesAfterColumn(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        ulong column,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        if (lineRegexp)
        {
            return TryFindLineRegexpMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out int matchStart, out _) &&
                IsMatchStartAfterColumn(matchStart, column)
                ? 1
                : 0;
        }

        if (wordRegexp)
        {
            return CountWordMatchesAfterColumn(LineContent(line, crlf, nullData), needle, column, asciiCaseInsensitive);
        }

        return CountLiteralMatchesAfterColumn(line, needle, column, asciiCaseInsensitive);
    }

    /// <summary>
    /// Counts non-overlapping matches in a single line whose one-based column is greater than <paramref name="column" />.
    /// </summary>
    /// <param name="line">The line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="needles">The ordered byte regex patterns to find.</param>
    /// <param name="column">The one-based column threshold.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters should compare case-insensitively.</param>
    /// <param name="lineRegexp">Whether a pattern must match the full line.</param>
    /// <param name="wordRegexp">Whether a pattern must be surrounded by word boundaries.</param>
    /// <param name="crlf">Whether CRLF should be treated as a line terminator for line-boundary matching.</param>
    /// <param name="nullData">Whether NUL should be treated as the line terminator.</param>
    /// <returns>The number of matches starting after the column threshold.</returns>
    public static long CountLineMatchesAfterColumn(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        ulong column,
        bool asciiCaseInsensitive = false,
        bool lineRegexp = false,
        bool wordRegexp = false,
        bool crlf = false,
        bool nullData = false)
    {
        ArgumentNullException.ThrowIfNull(needles);
        if (lineRegexp)
        {
            return TryFindPatternMatch(line, needles, offset: 0, asciiCaseInsensitive, lineRegexp: true, wordRegexp: false, crlf, nullData, out int matchStart, out _) &&
                IsMatchStartAfterColumn(matchStart, column)
                ? 1
                : 0;
        }

        ReadOnlySpan<byte> haystack = PatternContent(line, wordRegexp, crlf, nullData);
        return CountPatternMatchesAfterColumn(haystack, needles, column, asciiCaseInsensitive, wordRegexp);
    }

    internal static long CountLineMatchesWithRegexPlan(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        if (lineRegexp)
        {
            return TryFindPatternMatch(line, needles, offset: 0, asciiCaseInsensitive, lineRegexp: true, wordRegexp: false, crlf, nullData, regexPlan, out _, out _)
                ? 1
                : 0;
        }

        ReadOnlySpan<byte> haystack = PatternContent(line, wordRegexp, crlf, nullData);
        if (!wordRegexp &&
            !crlf &&
            !nullData &&
            TryGetWholeHaystackLiteralSetRegexPlan(needles, regexPlan, invertMatch: false, lineRegexp: false, wordRegexp: false, crlf: false, nullData: false, out RegexLiteralSetEngine literalSetEngine))
        {
            return literalSetEngine.CountMatches(haystack, startAt: 0);
        }

        return CountPatternMatches(haystack, needles, regexPlan, asciiCaseInsensitive, wordRegexp);
    }

    private static bool HasMatchingLine(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (LineMatches(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData))
            {
                return true;
            }

            lineStart += lineLength;
        }

        return false;
    }

    private static bool HasMatchingLine(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            if (LineMatches(line, needles, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData))
            {
                return true;
            }

            lineStart += lineLength;
        }

        return false;
    }

    private static bool SearchLineMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchSink
    {
        if (lineRegexp)
        {
            if (!TryFindLineRegexpMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out int matchStart, out int matchLength))
            {
                return false;
            }

            sink.Matched(lineNumber, lineStart + matchStart, matchStart + 1, line.Slice(matchStart, matchLength));
            return true;
        }

        if (wordRegexp)
        {
            return SearchWordMatches(LineContent(line, crlf, nullData), lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
        }

        return SearchLiteralMatches(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
    }

    private static bool SearchLineMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchSink
    {
        if (lineRegexp)
        {
            if (!TryFindPatternMatch(line, needles, offset: 0, asciiCaseInsensitive, lineRegexp: true, wordRegexp: false, crlf, nullData, regexPlan, out int matchStart, out int matchLength))
            {
                return false;
            }

            sink.Matched(lineNumber, lineStart + matchStart, matchStart + 1, line.Slice(matchStart, matchLength));
            return true;
        }

        return SearchPatternMatches(PatternContent(line, wordRegexp, crlf, nullData), lineStart, lineNumber, needles, regexPlan, ref sink, asciiCaseInsensitive, wordRegexp);
    }

    private static bool SearchLineMatchLines<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchLineSink
    {
        if (lineRegexp)
        {
            if (!TryFindLineRegexpMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out int matchStart, out int matchLength))
            {
                return false;
            }

            sink.MatchedLine(lineNumber, lineStart, lineStart + matchStart, matchStart + 1, line, line.Slice(matchStart, matchLength));
            return true;
        }

        if (wordRegexp)
        {
            return SearchWordMatchLines(LineContent(line, crlf, nullData), line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
        }

        return SearchLiteralMatchLines(line, lineStart, lineNumber, needle, ref sink, asciiCaseInsensitive);
    }

    private static bool SearchLineMatchLines<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
        where TSink : struct, IMatchLineSink
    {
        if (lineRegexp)
        {
            if (!TryFindPatternMatch(line, needles, offset: 0, asciiCaseInsensitive, lineRegexp: true, wordRegexp: false, crlf, nullData, regexPlan, out int matchStart, out int matchLength))
            {
                return false;
            }

            sink.MatchedLine(lineNumber, lineStart, lineStart + matchStart, matchStart + 1, line, line.Slice(matchStart, matchLength));
            return true;
        }

        return SearchPatternMatchLines(PatternContent(line, wordRegexp, crlf, nullData), line, lineStart, lineNumber, needles, regexPlan, ref sink, asciiCaseInsensitive, wordRegexp);
    }

    private static bool SearchLiteralMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchSink
    {
        if (needle.IsEmpty)
        {
            for (int index = 0; index < line.Length; index++)
            {
                sink.Matched(lineNumber, lineStart + index, index + 1, []);
            }

            return !line.IsEmpty;
        }

        bool matched = false;
        int offset = 0;
        while (offset <= line.Length)
        {
            int found = Find(line[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matched;
            }

            int start = offset + found;
            sink.Matched(lineNumber, lineStart + start, start + 1, line.Slice(start, needle.Length));
            matched = true;
            offset = start + needle.Length;
        }

        return matched;
    }

    private static bool SearchLiteralMatchLines<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchLineSink
    {
        if (needle.IsEmpty)
        {
            for (int index = 0; index < line.Length; index++)
            {
                sink.MatchedLine(lineNumber, lineStart, lineStart + index, index + 1, line, []);
            }

            return !line.IsEmpty;
        }

        bool matched = false;
        int offset = 0;
        while (offset <= line.Length)
        {
            int found = Find(line[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matched;
            }

            int start = offset + found;
            sink.MatchedLine(lineNumber, lineStart, lineStart + start, start + 1, line, line.Slice(start, needle.Length));
            matched = true;
            offset = start + needle.Length;
        }

        return matched;
    }

    private static bool SearchWordMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchSink
    {
        if (needle.IsEmpty)
        {
            bool matched = false;
            for (int index = 0; index <= line.Length; index++)
            {
                if (IsWordBoundary(line, index, index))
                {
                    sink.Matched(lineNumber, lineStart + index, index + 1, []);
                    matched = true;
                }
            }

            return matched;
        }

        bool foundMatch = false;
        int offset = 0;
        while (offset <= line.Length)
        {
            int found = FindWord(line[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return foundMatch;
            }

            int start = offset + found;
            sink.Matched(lineNumber, lineStart + start, start + 1, line.Slice(start, needle.Length));
            foundMatch = true;
            offset = start + needle.Length;
        }

        return foundMatch;
    }

    private static bool SearchWordMatchLines<TSink>(
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        ReadOnlySpan<byte> needle,
        ref TSink sink,
        bool asciiCaseInsensitive)
        where TSink : struct, IMatchLineSink
    {
        if (needle.IsEmpty)
        {
            bool matched = false;
            for (int index = 0; index <= content.Length; index++)
            {
                if (IsWordBoundary(content, index, index))
                {
                    sink.MatchedLine(lineNumber, lineStart, lineStart + index, index + 1, line, []);
                    matched = true;
                }
            }

            return matched;
        }

        bool foundMatch = false;
        int offset = 0;
        while (offset <= content.Length)
        {
            int found = FindWord(content[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return foundMatch;
            }

            int start = offset + found;
            sink.MatchedLine(lineNumber, lineStart, lineStart + start, start + 1, line, content.Slice(start, needle.Length));
            foundMatch = true;
            offset = start + needle.Length;
        }

        return foundMatch;
    }

    private static bool SearchPatternMatches<TSink>(
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool wordRegexp)
        where TSink : struct, IMatchSink
    {
        bool matched = false;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= line.Length)
        {
            if (!TryFindPatternMatch(line, needles, offset, asciiCaseInsensitive, lineRegexp: false, wordRegexp, crlf: false, nullData: false, regexPlan, out int start, out int length))
            {
                return matched;
            }

            var match = new MatcherMatch(start, length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, line.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!HasAnyEmptyPattern(needles) && IsLineEndEmptyMatch(line, start, length))
            {
                return matched;
            }

            sink.Matched(lineNumber, lineStart + start, start + 1, line.Slice(start, length));
            matched = true;
            offset = MatchIterator.AdvanceAfterReported(match, line.Length, ref suppressedEmptyStart);
            if (offset > line.Length)
            {
                return matched;
            }
        }

        return matched;
    }

    private static bool SearchPatternMatchLines<TSink>(
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> line,
        int lineStart,
        long lineNumber,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        ref TSink sink,
        bool asciiCaseInsensitive,
        bool wordRegexp)
        where TSink : struct, IMatchLineSink
    {
        bool matched = false;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= content.Length)
        {
            if (!TryFindPatternMatch(content, needles, offset, asciiCaseInsensitive, lineRegexp: false, wordRegexp, crlf: false, nullData: false, regexPlan, out int start, out int length))
            {
                return matched;
            }

            var match = new MatcherMatch(start, length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, content.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!HasAnyEmptyPattern(needles) && IsLineEndEmptyMatch(content, start, length))
            {
                return matched;
            }

            sink.MatchedLine(lineNumber, lineStart, lineStart + start, start + 1, line, content.Slice(start, length));
            matched = true;
            offset = MatchIterator.AdvanceAfterReported(match, content.Length, ref suppressedEmptyStart);
            if (offset > content.Length)
            {
                return matched;
            }
        }

        return matched;
    }

    private static bool LineMatches(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        return TryMatchLine(line, needle, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out _);
    }

    private static bool LineMatches(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData)
    {
        return TryMatchLine(line, needles, regexPlan: null, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, crlf, nullData, out _);
    }

    private static bool TryMatchLine(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out int matchStart)
    {
        bool matched;
        if (lineRegexp)
        {
            matched = TryFindLineRegexpMatch(line, needle, asciiCaseInsensitive, crlf, nullData, out matchStart, out _);
        }
        else if (wordRegexp)
        {
            matchStart = FindWord(LineContent(line, crlf, nullData), needle, asciiCaseInsensitive);
            matched = matchStart >= 0;
        }
        else
        {
            matchStart = Find(line, needle, asciiCaseInsensitive);
            matched = matchStart >= 0;
        }

        bool lineMatched = matched != invertMatch;
        if (!lineMatched || invertMatch)
        {
            matchStart = -1;
        }

        return lineMatched;
    }

    private static bool TryMatchLine(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out int matchStart)
    {
        bool matched = TryFindPatternMatch(
            lineRegexp ? line : PatternContent(line, wordRegexp, crlf, nullData),
            needles,
            offset: 0,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            regexPlan,
            out matchStart,
            out _);

        bool lineMatched = matched != invertMatch;
        if (!lineMatched || invertMatch)
        {
            matchStart = -1;
        }

        return lineMatched;
    }

    private static bool TryFindLineRegexpPatternMatch(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive,
        bool crlf,
        bool nullData,
        out int matchStart,
        out int matchLength)
    {
        matchStart = -1;
        matchLength = 0;
        if (!nullData)
        {
            for (int index = 0; index < needles.Count; index++)
            {
                byte[] needle = needles[index];
                ArgumentNullException.ThrowIfNull(needle);
                ReadOnlySpan<byte> content = LineContent(line, crlf, nullData);
                if (TryMatchFullRegex(content, needle, asciiCaseInsensitive))
                {
                    matchStart = 0;
                    matchLength = content.Length;
                    return true;
                }
            }

            return false;
        }

        return TryFindNullDataLineRegexpPatternMatch(line, needles, asciiCaseInsensitive, crlf, out matchStart, out matchLength);
    }

    private static bool TryFindLineRegexpMatch(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        bool crlf,
        bool nullData,
        out int matchStart,
        out int matchLength)
    {
        matchStart = -1;
        matchLength = 0;
        if (!nullData)
        {
            ReadOnlySpan<byte> lineContent = LineContent(line, crlf, nullData);
            if (!TryMatchFullRegex(lineContent, needle, asciiCaseInsensitive))
            {
                return false;
            }

            matchStart = 0;
            matchLength = lineContent.Length;
            return true;
        }

        ReadOnlySpan<byte> content = LineContent(line, crlf, nullData);
        int segmentStart = 0;
        while (segmentStart <= content.Length)
        {
            ReadOnlySpan<byte> remaining = content[segmentStart..];
            int lineFeed = remaining.IndexOf((byte)'\n');
            ReadOnlySpan<byte> segment = lineFeed < 0 ? remaining : remaining[..lineFeed];
            if (crlf && !segment.IsEmpty && segment[^1] == (byte)'\r')
            {
                segment = segment[..^1];
            }

            if (TryMatchFullRegex(segment, needle, asciiCaseInsensitive))
            {
                matchStart = segmentStart;
                matchLength = segment.Length;
                return true;
            }

            if (lineFeed < 0)
            {
                return false;
            }

            segmentStart += lineFeed + 1;
        }

        return false;
    }

    private static bool TryFindNullDataLineRegexpPatternMatch(
        ReadOnlySpan<byte> line,
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive,
        bool crlf,
        out int matchStart,
        out int matchLength)
    {
        ReadOnlySpan<byte> content = LineContent(line, crlf, nullData: true);
        int segmentStart = 0;
        while (segmentStart <= content.Length)
        {
            ReadOnlySpan<byte> remaining = content[segmentStart..];
            int lineFeed = remaining.IndexOf((byte)'\n');
            ReadOnlySpan<byte> segment = lineFeed < 0 ? remaining : remaining[..lineFeed];
            if (crlf && !segment.IsEmpty && segment[^1] == (byte)'\r')
            {
                segment = segment[..^1];
            }

            for (int index = 0; index < needles.Count; index++)
            {
                byte[] needle = needles[index];
                ArgumentNullException.ThrowIfNull(needle);
                if (TryMatchFullRegex(segment, needle, asciiCaseInsensitive))
                {
                    matchStart = segmentStart;
                    matchLength = segment.Length;
                    return true;
                }
            }

            if (lineFeed < 0)
            {
                break;
            }

            segmentStart += lineFeed + 1;
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private static long CountLiteralMatchesByLine(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        ulong maxMatchingLines,
        bool nullData)
    {
        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = remaining[..lineLength];
            long lineMatches = CountLiteralMatches(line, needle, asciiCaseInsensitive);
            if (lineMatches > 0)
            {
                count += lineMatches;
                matchedLines++;
                if (matchedLines >= maxMatchingLines)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    private static long CountLiteralMatches(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            return haystack.Length;
        }

        long count = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return count;
            }

            count++;
            offset += found + needle.Length;
        }

        return count;
    }

    private static long CountLiteralMatchesAfterColumn(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ulong column,
        bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            long emptyCount = 0;
            for (int index = 0; index < haystack.Length; index++)
            {
                if (IsMatchStartAfterColumn(index, column))
                {
                    emptyCount++;
                }
            }

            return emptyCount;
        }

        long count = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return count;
            }

            int start = offset + found;
            if (IsMatchStartAfterColumn(start, column))
            {
                count++;
            }

            offset = start + needle.Length;
        }

        return count;
    }

    private static long CountWordMatchesByLine(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        bool asciiCaseInsensitive,
        ulong? maxMatchingLines,
        bool crlf,
        bool nullData)
    {
        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = LineContent(remaining[..lineLength], crlf, nullData);
            long lineMatches = CountWordMatches(line, needle, asciiCaseInsensitive);
            if (lineMatches > 0)
            {
                count += lineMatches;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    private static long CountWordMatches(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            long count = 0;
            for (int index = 0; index <= haystack.Length; index++)
            {
                if (IsWordBoundary(haystack, index, index))
                {
                    count++;
                }
            }

            return count;
        }

        long matches = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = FindWord(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matches;
            }

            matches++;
            offset += found + needle.Length;
        }

        return matches;
    }

    private static long CountWordMatchesAfterColumn(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ulong column,
        bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            long count = 0;
            for (int index = 0; index <= haystack.Length; index++)
            {
                if (IsWordBoundary(haystack, index, index) && IsMatchStartAfterColumn(index, column))
                {
                    count++;
                }
            }

            return count;
        }

        long matches = 0;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = FindWord(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return matches;
            }

            int start = offset + found;
            if (IsMatchStartAfterColumn(start, column))
            {
                matches++;
            }

            offset = start + needle.Length;
        }

        return matches;
    }

    private static long CountLiteralSetMatchesByLine(
        ReadOnlySpan<byte> haystack,
        RegexLiteralSetEngine literalSetEngine,
        ulong maxMatchingLines)
    {
        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData: false);
            long lineMatches = literalSetEngine.CountMatches(remaining[..lineLength], startAt: 0);
            if (lineMatches > 0)
            {
                count += lineMatches;
                matchedLines++;
                if (matchedLines >= maxMatchingLines)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    private static long CountPatternMatchesByLine(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        ulong? maxMatchingLines,
        bool crlf,
        bool nullData)
    {
        long count = 0;
        ulong matchedLines = 0;
        int lineStart = 0;
        while (lineStart < haystack.Length)
        {
            ReadOnlySpan<byte> remaining = haystack[lineStart..];
            int lineLength = GetLineLength(remaining, nullData);
            ReadOnlySpan<byte> line = PatternContent(remaining[..lineLength], wordRegexp, crlf, nullData);
            long lineMatches = CountPatternMatches(line, needles, regexPlan, asciiCaseInsensitive, wordRegexp);
            if (lineMatches > 0)
            {
                count += lineMatches;
                matchedLines++;
                if (maxMatchingLines is ulong limit && matchedLines >= limit)
                {
                    return count;
                }
            }

            lineStart += lineLength;
        }

        return count;
    }

    private static long CountPatternMatches(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool wordRegexp)
    {
        long count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= haystack.Length)
        {
            if (!TryFindPatternMatch(haystack, needles, offset, asciiCaseInsensitive, lineRegexp: false, wordRegexp, crlf: false, nullData: false, regexPlan, out int start, out int length))
            {
                return count;
            }

            var match = new MatcherMatch(start, length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, haystack.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!HasAnyEmptyPattern(needles) && IsLineEndEmptyMatch(haystack, start, length))
            {
                return count;
            }

            count++;
            offset = MatchIterator.AdvanceAfterReported(match, haystack.Length, ref suppressedEmptyStart);
            if (offset > haystack.Length)
            {
                return count;
            }
        }

        return count;
    }

    private static long CountPatternMatchesAfterColumn(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        ulong column,
        bool asciiCaseInsensitive,
        bool wordRegexp)
    {
        long count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= haystack.Length)
        {
            if (!TryFindPatternMatch(haystack, needles, offset, asciiCaseInsensitive, lineRegexp: false, wordRegexp, crlf: false, nullData: false, out int start, out int length))
            {
                return count;
            }

            var match = new MatcherMatch(start, length);
            if (MatchIterator.IsSuppressedEmpty(match, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(match, haystack.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!HasAnyEmptyPattern(needles) && IsLineEndEmptyMatch(haystack, start, length))
            {
                return count;
            }

            if (IsMatchStartAfterColumn(start, column))
            {
                count++;
            }

            offset = MatchIterator.AdvanceAfterReported(match, haystack.Length, ref suppressedEmptyStart);
            if (offset > haystack.Length)
            {
                return count;
            }
        }

        return count;
    }

    private static bool IsMatchStartAfterColumn(int start, ulong column)
    {
        return (ulong)start >= column;
    }

    private static bool TryFindPatternMatch(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        int offset,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out int matchStart,
        out int matchLength)
    {
        return TryFindPatternMatch(
            haystack,
            needles,
            offset,
            asciiCaseInsensitive,
            lineRegexp,
            wordRegexp,
            crlf,
            nullData,
            regexPlan: null,
            out matchStart,
            out matchLength);
    }

    private static bool TryFindPatternMatch(
        ReadOnlySpan<byte> haystack,
        IReadOnlyList<byte[]> needles,
        int offset,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        RegexSearchPlan? regexPlan,
        out int matchStart,
        out int matchLength)
    {
        matchStart = -1;
        matchLength = 0;
        if (needles.Count == 0)
        {
            return false;
        }

        if (lineRegexp)
        {
            return TryFindLineRegexpPatternMatch(haystack, needles, asciiCaseInsensitive, crlf, nullData, out matchStart, out matchLength);
        }

        bool foundAny = false;
        int bestPattern = needles.Count;
        int bestStart = int.MaxValue;
        int bestLength = 0;
        for (int index = 0; index < needles.Count; index++)
        {
            byte[] needle = needles[index];
            ArgumentNullException.ThrowIfNull(needle);
            if (!TryFindSinglePattern(
                haystack,
                needle,
                offset,
                asciiCaseInsensitive,
                wordRegexp,
                regexPlan?.GetLiteralSetEngine(index),
                regexPlan?.GetAutomaton(index),
                regexPlan?.GetAccelerator(index),
                regexPlan?.GetLeadingLiteralCandidateAccelerator(index),
                out int start,
                out int length))
            {
                continue;
            }

            if (!foundAny || start < bestStart || (start == bestStart && index < bestPattern))
            {
                foundAny = true;
                bestPattern = index;
                bestStart = start;
                bestLength = length;
            }
        }

        if (!foundAny)
        {
            return false;
        }

        matchStart = bestStart;
        matchLength = bestLength;
        return true;
    }

    private static bool TryFindSinglePattern(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        int offset,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        RegexLiteralSetEngine? literalSetEngine,
        RegexAutomaton? automaton,
        RegexClassSequenceAccelerator? accelerator,
        RegexLeadingLiteralCandidateAccelerator? leadingLiteralCandidateAccelerator,
        out int matchStart,
        out int matchLength)
    {
        matchStart = -1;
        matchLength = 0;
        if (offset > haystack.Length)
        {
            return false;
        }

        if (needle.IsEmpty)
        {
            if (wordRegexp)
            {
                for (int index = offset; index <= haystack.Length; index++)
                {
                    if (IsWordBoundary(haystack, index, index))
                    {
                        matchStart = index;
                        matchLength = 0;
                        return true;
                    }
                }

                return false;
            }

            if (offset < haystack.Length)
            {
                matchStart = offset;
                matchLength = 0;
                return true;
            }

            return false;
        }

        if (literalSetEngine is not null && !wordRegexp)
        {
            RegexMatch? match = literalSetEngine.Find(haystack, offset);
            if (!match.HasValue)
            {
                return false;
            }

            matchStart = match.Value.Start;
            matchLength = match.Value.Length;
            return true;
        }

        if (accelerator is not null && !ContainsNonAscii(haystack))
        {
            return TryFindAcceleratedPattern(haystack, accelerator, offset, wordRegexp, out matchStart, out matchLength);
        }

        if (leadingLiteralCandidateAccelerator is not null && !ContainsNonAscii(haystack))
        {
            return TryFindLeadingLiteralCandidatePattern(
                haystack,
                needle,
                leadingLiteralCandidateAccelerator,
                offset,
                asciiCaseInsensitive,
                wordRegexp,
                out matchStart,
                out matchLength);
        }

        int searchOffset = offset;
        while (searchOffset <= haystack.Length)
        {
            if (!TryFindRegex(haystack, needle, searchOffset, asciiCaseInsensitive, automaton, out int start, out int length))
            {
                return false;
            }

            int end = start + length;
            if (!wordRegexp || IsWordBoundary(haystack, start, end))
            {
                matchStart = start;
                matchLength = length;
                return true;
            }

            searchOffset = start + 1;
        }

        return false;
    }

    private static bool TryFindLeadingLiteralCandidatePattern(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        RegexLeadingLiteralCandidateAccelerator accelerator,
        int offset,
        bool asciiCaseInsensitive,
        bool wordRegexp,
        out int matchStart,
        out int matchLength)
    {
        int searchOffset = offset;
        while (searchOffset <= haystack.Length)
        {
            if (!accelerator.TryFind(haystack, needle, searchOffset, asciiCaseInsensitive, out int start, out int length))
            {
                matchStart = -1;
                matchLength = 0;
                return false;
            }

            int end = start + length;
            if (!wordRegexp || IsWordBoundary(haystack, start, end))
            {
                matchStart = start;
                matchLength = length;
                return true;
            }

            searchOffset = start + 1;
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private static bool TryFindAcceleratedPattern(
        ReadOnlySpan<byte> haystack,
        RegexClassSequenceAccelerator accelerator,
        int offset,
        bool wordRegexp,
        out int matchStart,
        out int matchLength)
    {
        int searchOffset = offset;
        while (searchOffset <= haystack.Length)
        {
            if (!accelerator.TryFind(haystack, searchOffset, out int start, out int length))
            {
                matchStart = -1;
                matchLength = 0;
                return false;
            }

            int end = start + length;
            if (!wordRegexp || IsWordBoundary(haystack, start, end))
            {
                matchStart = start;
                matchLength = length;
                return true;
            }

            searchOffset = start + 1;
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private static bool TryFindRegex(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        int offset,
        bool asciiCaseInsensitive,
        RegexAutomaton? automaton,
        out int matchStart,
        out int matchLength)
    {
        matchStart = -1;
        matchLength = 0;
        if (IsLiteralRegex(pattern))
        {
            if (asciiCaseInsensitive &&
                ContainsNonAscii(pattern) &&
                TryFindUtf8IgnoreCase(haystack[offset..], pattern, out int literalUtf8Start, out int literalUtf8Length))
            {
                matchStart = offset + literalUtf8Start;
                matchLength = literalUtf8Length;
                return true;
            }

            int found = Find(haystack[offset..], pattern, asciiCaseInsensitive);
            if (found < 0)
            {
                return false;
            }

            matchStart = offset + found;
            matchLength = pattern.Length;
            return true;
        }

        if (asciiCaseInsensitive &&
            ContainsNonAscii(pattern) &&
            TryFindUtf8IgnoreCase(haystack[offset..], pattern, out int utf8Start, out int utf8Length))
        {
            matchStart = offset + utf8Start;
            matchLength = utf8Length;
            return true;
        }

        if (ContainsAutomatonOnlyRegexSyntax(pattern) || RequiresAutomatonRegex(haystack, pattern, asciiCaseInsensitive))
        {
            automaton ??= RegexAutomaton.Compile(pattern, asciiCaseInsensitive, multiLine: false, dotMatchesNewline: false);
            ReadOnlySpan<byte> automatonHaystack = TrimAutomatonLineTerminator(haystack);
            if (offset > automatonHaystack.Length)
            {
                return false;
            }

            RegexMatch? match = automaton.Find(automatonHaystack, offset);
            if (match.HasValue)
            {
                matchStart = match.Value.Start;
                matchLength = match.Value.Length;
                return true;
            }

            return false;
        }

        for (int index = offset; index <= haystack.Length; index++)
        {
            if (TryMatchRegexAt(haystack, pattern, index, asciiCaseInsensitive, ignoreWhitespace: false, swapGreed: false, out int length))
            {
                matchStart = index;
                matchLength = length;
                return true;
            }
        }

        return false;
    }

    private static ReadOnlySpan<byte> TrimAutomatonLineTerminator(ReadOnlySpan<byte> haystack)
    {
        if (!haystack.IsEmpty &&
            (haystack[^1] == (byte)'\n' || haystack[^1] == 0))
        {
            return haystack[..^1];
        }

        return haystack;
    }

    private static bool RequiresAutomatonRegex(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> pattern, bool asciiCaseInsensitive)
    {
        if (!ContainsNonAscii(haystack))
        {
            return false;
        }

        return MayRequireAutomatonRegex(pattern, asciiCaseInsensitive);
    }

    private static bool MayRequireAutomatonRegex(ReadOnlySpan<byte> pattern, bool asciiCaseInsensitive)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'\\' && index + 1 < pattern.Length)
            {
                if (IsUnicodeSensitiveEscape(pattern[index + 1]))
                {
                    return true;
                }

                index++;
                continue;
            }

            if (value == (byte)'[' && TryFindClassEnd(pattern, index, out int classEnd))
            {
                if (asciiCaseInsensitive ||
                    (index + 1 < classEnd && pattern[index + 1] == (byte)'^') ||
                    ClassContainsUnicodeSensitiveEscape(pattern[(index + 1)..classEnd]))
                {
                    return true;
                }

                index = classEnd;
            }
        }

        return false;
    }

    internal static bool ShouldPrecompileRegexAutomaton(ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        return needle.Length != 0 &&
            !IsLiteralRegex(needle) &&
            (ContainsAutomatonOnlyRegexSyntax(needle) || MayRequireAutomatonRegex(needle, asciiCaseInsensitive));
    }

    private static bool ClassContainsUnicodeSensitiveEscape(ReadOnlySpan<byte> expression)
    {
        for (int index = 0; index + 1 < expression.Length; index++)
        {
            if (expression[index] == (byte)'\\')
            {
                if (IsUnicodeSensitiveEscape(expression[index + 1]))
                {
                    return true;
                }

                index++;
            }
        }

        return false;
    }

    private static bool IsUnicodeSensitiveEscape(byte value)
    {
        return value is (byte)'b'
            or (byte)'B'
            or (byte)'d'
            or (byte)'D'
            or (byte)'s'
            or (byte)'S'
            or (byte)'w'
            or (byte)'W'
            or (byte)'p'
            or (byte)'P';
    }

    private static bool ContainsAutomatonOnlyRegexSyntax(ReadOnlySpan<byte> pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\\' &&
                index + 1 < pattern.Length &&
                pattern[index + 1] is (byte)'A' or (byte)'z' or (byte)'p' or (byte)'P')
            {
                return true;
            }

            if (pattern[index] == (byte)'[' && TryFindClassEnd(pattern, index, out int classEnd))
            {
                if (ClassContainsUnicodePropertyEscape(pattern[(index + 1)..classEnd]))
                {
                    return true;
                }

                index = classEnd;
            }

            if (pattern[index] == (byte)'(' &&
                index + 2 < pattern.Length &&
                pattern[index + 1] == (byte)'?' &&
                (pattern[index + 2] == (byte)':' ||
                    IsScopedInlineFlagGroup(pattern, index + 2) ||
                    IsUnicodeModeFlagGroup(pattern, index + 2)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ClassContainsUnicodePropertyEscape(ReadOnlySpan<byte> expression)
    {
        for (int index = 0; index + 1 < expression.Length; index++)
        {
            if (expression[index] == (byte)'\\')
            {
                if (expression[index + 1] is (byte)'p' or (byte)'P')
                {
                    return true;
                }

                index++;
            }
        }

        return false;
    }

    private static bool IsScopedInlineFlagGroup(ReadOnlySpan<byte> pattern, int index)
    {
        bool sawFlag = false;
        while (index < pattern.Length)
        {
            byte value = pattern[index];
            if (value == (byte)':')
            {
                return sawFlag;
            }

            if (value == (byte)')')
            {
                return false;
            }

            if (value == (byte)'-')
            {
                index++;
                continue;
            }

            if (!IsRegexFlagByte(value))
            {
                return false;
            }

            sawFlag = true;
            index++;
        }

        return false;
    }

    private static bool IsUnicodeModeFlagGroup(ReadOnlySpan<byte> pattern, int index)
    {
        bool sawFlag = false;
        bool sawUnicodeFlag = false;
        while (index < pattern.Length)
        {
            byte value = pattern[index];
            if (value == (byte)':')
            {
                return false;
            }

            if (value == (byte)')')
            {
                return sawFlag && sawUnicodeFlag;
            }

            if (value == (byte)'-')
            {
                index++;
                continue;
            }

            if (!IsRegexFlagByte(value))
            {
                return false;
            }

            sawFlag = true;
            sawUnicodeFlag |= value == (byte)'u';
            index++;
        }

        return false;
    }

    internal static bool TryMatchRegexAt(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        int haystackIndex,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool swapGreed,
        out int matchLength)
    {
        int alternativeStart = 0;
        bool alternativeIgnoreWhitespace = ignoreWhitespace;
        while (alternativeStart <= pattern.Length)
        {
            int alternativeEnd = FindAlternativeEnd(pattern, alternativeStart, alternativeIgnoreWhitespace, out bool nextIgnoreWhitespace);
            if (TryMatchRegexSequenceAt(
                haystack,
                pattern[alternativeStart..alternativeEnd],
                haystackIndex,
                asciiCaseInsensitive,
                alternativeIgnoreWhitespace,
                swapGreed,
                out matchLength))
            {
                return true;
            }

            if (alternativeEnd == pattern.Length)
            {
                break;
            }

            alternativeStart = alternativeEnd + 1;
            alternativeIgnoreWhitespace = nextIgnoreWhitespace;
        }

        matchLength = 0;
        return false;
    }

    private static bool TryMatchFullRegex(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        bool asciiCaseInsensitive)
    {
        int alternativeStart = 0;
        bool alternativeIgnoreWhitespace = false;
        while (alternativeStart <= pattern.Length)
        {
            int alternativeEnd = FindAlternativeEnd(pattern, alternativeStart, alternativeIgnoreWhitespace, out bool nextIgnoreWhitespace);
            if (TryMatchRegexFrom(
                    haystack,
                    pattern[alternativeStart..alternativeEnd],
                    haystackIndex: 0,
                    patternIndex: 0,
                    asciiCaseInsensitive,
                    alternativeIgnoreWhitespace,
                    swapGreed: false,
                    requireEnd: true,
                    out _))
            {
                return true;
            }

            if (alternativeEnd == pattern.Length)
            {
                break;
            }

            alternativeStart = alternativeEnd + 1;
            alternativeIgnoreWhitespace = nextIgnoreWhitespace;
        }

        return false;
    }

    private static bool TryMatchRegexSequenceAt(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        int haystackIndex,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool swapGreed,
        out int matchLength)
    {
        if (TryMatchRegexFrom(haystack, pattern, haystackIndex, patternIndex: 0, asciiCaseInsensitive, ignoreWhitespace, swapGreed, out int end))
        {
            matchLength = end - haystackIndex;
            return true;
        }

        matchLength = 0;
        return false;
    }

    private static int FindAlternativeEnd(ReadOnlySpan<byte> pattern, int start, bool ignoreWhitespace, out bool nextIgnoreWhitespace)
    {
        int classDepth = 0;
        int groupDepth = 0;
        nextIgnoreWhitespace = ignoreWhitespace;
        for (int index = start; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (classDepth == 0 && groupDepth == 0)
            {
                if (nextIgnoreWhitespace && value == (byte)'#')
                {
                    while (index < pattern.Length && pattern[index] != (byte)'\n')
                    {
                        index++;
                    }

                    continue;
                }

                if (TryReadInlineIgnoreWhitespaceFlag(pattern, index, out bool newIgnoreWhitespace, out int nextIndex))
                {
                    nextIgnoreWhitespace = newIgnoreWhitespace;
                    index = nextIndex - 1;
                    continue;
                }
            }

            if (value == (byte)'\\')
            {
                index++;
                continue;
            }

            if (value == (byte)'[')
            {
                classDepth++;
                continue;
            }

            if (value == (byte)']' && classDepth > 0)
            {
                classDepth--;
                continue;
            }

            if (value == (byte)'(' && classDepth == 0)
            {
                groupDepth++;
                continue;
            }

            if (value == (byte)')' && classDepth == 0 && groupDepth > 0)
            {
                groupDepth--;
                continue;
            }

            if (value == (byte)'|' && classDepth == 0 && groupDepth == 0)
            {
                return index;
            }
        }

        return pattern.Length;
    }

    private static bool TryMatchRegexFrom(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        int haystackIndex,
        int patternIndex,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool swapGreed,
        out int end)
    {
        return TryMatchRegexFrom(
            haystack,
            pattern,
            haystackIndex,
            patternIndex,
            asciiCaseInsensitive,
            ignoreWhitespace,
            swapGreed,
            requireEnd: false,
            out end);
    }

    private static bool TryMatchRegexFrom(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        int haystackIndex,
        int patternIndex,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool swapGreed,
        bool requireEnd,
        out int end)
    {
        SkipIgnoredPatternBytes(pattern, ref patternIndex, ignoreWhitespace);
        if (patternIndex >= pattern.Length)
        {
            end = haystackIndex;
            return !requireEnd || haystackIndex == haystack.Length;
        }

        if (!TryParseRegexAtom(pattern, patternIndex, out int atomKind, out byte literal, out int classStart, out int classEnd, out int nextPatternIndex))
        {
            end = 0;
            return false;
        }

        if (atomKind is RegexAtomEnableCaseInsensitive
            or RegexAtomDisableCaseInsensitive
            or RegexAtomEnableIgnoreWhitespace
            or RegexAtomDisableIgnoreWhitespace
            or RegexAtomEnableSwapGreed
            or RegexAtomDisableSwapGreed)
        {
            return TryMatchRegexFrom(
                haystack,
                pattern,
                haystackIndex,
                nextPatternIndex,
                atomKind == RegexAtomEnableCaseInsensitive ? true :
                atomKind == RegexAtomDisableCaseInsensitive ? false :
                asciiCaseInsensitive,
                atomKind == RegexAtomEnableIgnoreWhitespace ? true :
                atomKind == RegexAtomDisableIgnoreWhitespace ? false :
                ignoreWhitespace,
                atomKind == RegexAtomEnableSwapGreed ? true :
                atomKind == RegexAtomDisableSwapGreed ? false :
                swapGreed,
                requireEnd,
                out end);
        }

        int quantifierIndex = nextPatternIndex;
        SkipIgnoredPatternBytes(pattern, ref quantifierIndex, ignoreWhitespace);
        TryParseRegexQuantifier(
            pattern,
            quantifierIndex,
            out byte quantifier,
            out int minRepetitions,
            out int maxRepetitions,
            out bool lazy,
            out int suffixPatternIndex);
        if (quantifier != 0 && swapGreed)
        {
            lazy = !lazy;
        }

        if (atomKind is RegexAtomGroup
            or RegexAtomCaseInsensitiveGroup
            or RegexAtomCaseSensitiveGroup
            or RegexAtomIgnoreWhitespaceGroup
            or RegexAtomSignificantWhitespaceGroup
            or RegexAtomSwapGreedGroup
            or RegexAtomStandardGreedGroup)
        {
            ReadOnlySpan<byte> group = pattern[classStart..classEnd];
            bool groupCaseInsensitive = GetGroupCaseSensitivity(atomKind, asciiCaseInsensitive);
            bool groupIgnoreWhitespace = GetGroupIgnoreWhitespace(atomKind, ignoreWhitespace);
            bool groupSwapGreed = GetGroupSwapGreed(atomKind, swapGreed);
            return quantifier switch
            {
                0 => TryMatchRegexGroupThen(haystack, group, haystackIndex, pattern, suffixPatternIndex, groupCaseInsensitive, groupIgnoreWhitespace, groupSwapGreed, asciiCaseInsensitive, ignoreWhitespace, swapGreed, requireEnd, out end),
                _ => TryMatchRepeatedRegexGroup(haystack, group, haystackIndex, minRepetitions, maxRepetitions, lazy, pattern, suffixPatternIndex, groupCaseInsensitive, groupIgnoreWhitespace, groupSwapGreed, asciiCaseInsensitive, ignoreWhitespace, swapGreed, requireEnd, out end),
            };
        }

        if (quantifier == 0)
        {
            if (IsZeroWidthRegexAtom(atomKind))
            {
                if (!RegexAssertionMatches(haystack, haystackIndex, atomKind))
                {
                    end = 0;
                    return false;
                }

                return TryMatchRegexFrom(haystack, pattern, haystackIndex, suffixPatternIndex, asciiCaseInsensitive, ignoreWhitespace, swapGreed, requireEnd, out end);
            }

            if (!RegexAtomMatches(haystack, haystackIndex, atomKind, literal, pattern[classStart..classEnd], asciiCaseInsensitive))
            {
                end = 0;
                return false;
            }

            return TryMatchRegexFrom(haystack, pattern, haystackIndex + 1, suffixPatternIndex, asciiCaseInsensitive, ignoreWhitespace, swapGreed, requireEnd, out end);
        }

        return TryMatchRepeatedRegexAtom(
            haystack,
            atomKind,
            literal,
            pattern[classStart..classEnd],
            haystackIndex,
            minRepetitions,
            maxRepetitions,
            lazy,
            pattern,
            suffixPatternIndex,
            asciiCaseInsensitive,
            ignoreWhitespace,
            swapGreed,
            requireEnd,
            out end);
    }

    private static bool TryMatchRegexGroupThen(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> group,
        int haystackIndex,
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        bool groupCaseInsensitive,
        bool groupIgnoreWhitespace,
        bool groupSwapGreed,
        bool suffixCaseInsensitive,
        bool suffixIgnoreWhitespace,
        bool suffixSwapGreed,
        bool requireEnd,
        out int end)
    {
        int alternativeStart = 0;
        bool alternativeIgnoreWhitespace = groupIgnoreWhitespace;
        while (alternativeStart <= group.Length)
        {
            int alternativeEnd = FindAlternativeEnd(group, alternativeStart, alternativeIgnoreWhitespace, out bool nextIgnoreWhitespace);
            if (TryMatchRegexSequenceAt(
                    haystack,
                    group[alternativeStart..alternativeEnd],
                    haystackIndex,
                    groupCaseInsensitive,
                    alternativeIgnoreWhitespace,
                    groupSwapGreed,
                    out int groupLength) &&
                TryMatchRegexFrom(haystack, pattern, haystackIndex + groupLength, suffixPatternIndex, suffixCaseInsensitive, suffixIgnoreWhitespace, suffixSwapGreed, requireEnd, out end))
            {
                return true;
            }

            int maxGroupLength = haystack.Length - haystackIndex;
            for (int candidateGroupLength = maxGroupLength; candidateGroupLength >= 0; candidateGroupLength--)
            {
                ReadOnlySpan<byte> candidate = haystack.Slice(haystackIndex, candidateGroupLength);
                if (TryMatchRegexFrom(
                        candidate,
                        group[alternativeStart..alternativeEnd],
                        haystackIndex: 0,
                        patternIndex: 0,
                        groupCaseInsensitive,
                        alternativeIgnoreWhitespace,
                        groupSwapGreed,
                        requireEnd: true,
                        out _) &&
                    TryMatchRegexFrom(haystack, pattern, haystackIndex + candidateGroupLength, suffixPatternIndex, suffixCaseInsensitive, suffixIgnoreWhitespace, suffixSwapGreed, requireEnd, out end))
                {
                    return true;
                }
            }

            if (alternativeEnd == group.Length)
            {
                break;
            }

            alternativeStart = alternativeEnd + 1;
            alternativeIgnoreWhitespace = nextIgnoreWhitespace;
        }

        end = 0;
        return false;
    }

    private static bool TryMatchOptionalRegexGroup(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> group,
        int haystackIndex,
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        bool groupCaseInsensitive,
        bool groupIgnoreWhitespace,
        bool groupSwapGreed,
        bool suffixCaseInsensitive,
        bool suffixIgnoreWhitespace,
        bool suffixSwapGreed,
        out int end)
    {
        return TryMatchRegexGroupThen(haystack, group, haystackIndex, pattern, suffixPatternIndex, groupCaseInsensitive, groupIgnoreWhitespace, groupSwapGreed, suffixCaseInsensitive, suffixIgnoreWhitespace, suffixSwapGreed, requireEnd: false, out end) ||
            TryMatchRegexFrom(haystack, pattern, haystackIndex, suffixPatternIndex, suffixCaseInsensitive, suffixIgnoreWhitespace, suffixSwapGreed, out end);
    }

    private static bool TryMatchRepeatedRegexAtom(
        ReadOnlySpan<byte> haystack,
        int atomKind,
        byte literal,
        ReadOnlySpan<byte> expression,
        int haystackIndex,
        int minRepetitions,
        int maxRepetitions,
        bool lazy,
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool swapGreed,
        bool requireEnd,
        out int end)
    {
        if (IsZeroWidthRegexAtom(atomKind))
        {
            end = 0;
            return false;
        }

        int repetitions = 0;
        int maxIndex = haystackIndex;
        while (repetitions < maxRepetitions && RegexAtomMatches(haystack, maxIndex, atomKind, literal, expression, asciiCaseInsensitive))
        {
            maxIndex++;
            repetitions++;
        }

        if (repetitions < minRepetitions)
        {
            end = 0;
            return false;
        }

        int minIndex = haystackIndex + minRepetitions;
        if (lazy)
        {
            for (int candidate = minIndex; candidate <= maxIndex; candidate++)
            {
                if (TryMatchRegexFrom(haystack, pattern, candidate, suffixPatternIndex, asciiCaseInsensitive, ignoreWhitespace, swapGreed, requireEnd, out end))
                {
                    return true;
                }
            }

            end = 0;
            return false;
        }

        for (int candidate = maxIndex; candidate >= minIndex; candidate--)
        {
            if (TryMatchRegexFrom(haystack, pattern, candidate, suffixPatternIndex, asciiCaseInsensitive, ignoreWhitespace, swapGreed, requireEnd, out end))
            {
                return true;
            }
        }

        end = 0;
        return false;
    }

    private static bool TryMatchRepeatedRegexGroup(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> group,
        int haystackIndex,
        int minRepetitions,
        int maxRepetitions,
        bool lazy,
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        bool groupCaseInsensitive,
        bool groupIgnoreWhitespace,
        bool groupSwapGreed,
        bool suffixCaseInsensitive,
        bool suffixIgnoreWhitespace,
        bool suffixSwapGreed,
        bool requireEnd,
        out int end)
    {
        var groupEndCache = new Dictionary<int, List<int>>();
        var failedStates = new HashSet<(int Current, int Repetitions)>();
        return TryMatchRepeatedRegexGroupFrom(
            haystack,
            group,
            haystackIndex,
            repetitions: 0,
            minRepetitions,
            maxRepetitions,
            lazy,
            pattern,
            suffixPatternIndex,
            groupCaseInsensitive,
            groupIgnoreWhitespace,
            groupSwapGreed,
            suffixCaseInsensitive,
            suffixIgnoreWhitespace,
            suffixSwapGreed,
            requireEnd,
            groupEndCache,
            failedStates,
            out end);
    }

    private static bool TryMatchRepeatedRegexGroupFrom(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> group,
        int current,
        int repetitions,
        int minRepetitions,
        int maxRepetitions,
        bool lazy,
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        bool groupCaseInsensitive,
        bool groupIgnoreWhitespace,
        bool groupSwapGreed,
        bool suffixCaseInsensitive,
        bool suffixIgnoreWhitespace,
        bool suffixSwapGreed,
        bool requireEnd,
        Dictionary<int, List<int>> groupEndCache,
        HashSet<(int Current, int Repetitions)> failedStates,
        out int end)
    {
        if (failedStates.Contains((current, repetitions)))
        {
            end = 0;
            return false;
        }

        if (repetitions >= minRepetitions && lazy &&
            TryMatchRegexFrom(haystack, pattern, current, suffixPatternIndex, suffixCaseInsensitive, suffixIgnoreWhitespace, suffixSwapGreed, requireEnd, out end))
        {
            return true;
        }

        if (repetitions < maxRepetitions)
        {
            List<int> groupEnds = GetRegexGroupEndCandidates(
                haystack,
                group,
                current,
                groupCaseInsensitive,
                groupIgnoreWhitespace,
                groupSwapGreed,
                groupEndCache);
            for (int index = 0; index < groupEnds.Count; index++)
            {
                int next = groupEnds[index];
                if (next == current && repetitions >= minRepetitions)
                {
                    continue;
                }

                if (TryMatchRepeatedRegexGroupFrom(
                    haystack,
                    group,
                    next,
                    repetitions + 1,
                    minRepetitions,
                    maxRepetitions,
                    lazy,
                    pattern,
                    suffixPatternIndex,
                    groupCaseInsensitive,
                    groupIgnoreWhitespace,
                    groupSwapGreed,
                    suffixCaseInsensitive,
                    suffixIgnoreWhitespace,
                    suffixSwapGreed,
                    requireEnd,
                    groupEndCache,
                    failedStates,
                    out end))
                {
                    return true;
                }
            }
        }

        if (repetitions >= minRepetitions &&
            !lazy &&
            TryMatchRegexFrom(haystack, pattern, current, suffixPatternIndex, suffixCaseInsensitive, suffixIgnoreWhitespace, suffixSwapGreed, requireEnd, out end))
        {
            return true;
        }

        failedStates.Add((current, repetitions));
        end = 0;
        return false;
    }

    private static List<int> GetRegexGroupEndCandidates(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> group,
        int haystackIndex,
        bool groupCaseInsensitive,
        bool groupIgnoreWhitespace,
        bool groupSwapGreed,
        Dictionary<int, List<int>> groupEndCache)
    {
        if (groupEndCache.TryGetValue(haystackIndex, out List<int>? cached))
        {
            return cached;
        }

        var ends = new List<int>();
        int alternativeStart = 0;
        bool alternativeIgnoreWhitespace = groupIgnoreWhitespace;
        while (alternativeStart <= group.Length)
        {
            int alternativeEnd = FindAlternativeEnd(group, alternativeStart, alternativeIgnoreWhitespace, out bool nextIgnoreWhitespace);
            ReadOnlySpan<byte> alternative = group[alternativeStart..alternativeEnd];
            if (!TryMatchRegexSequenceAt(
                haystack,
                alternative,
                haystackIndex,
                groupCaseInsensitive,
                alternativeIgnoreWhitespace,
                groupSwapGreed,
                out int preferredLength))
            {
                if (alternativeEnd == group.Length)
                {
                    break;
                }

                alternativeStart = alternativeEnd + 1;
                alternativeIgnoreWhitespace = nextIgnoreWhitespace;
                continue;
            }

            AddRegexGroupEndCandidate(ends, haystackIndex + preferredLength);

            int maxGroupLength = haystack.Length - haystackIndex;
            for (int candidateGroupLength = maxGroupLength; candidateGroupLength >= 0; candidateGroupLength--)
            {
                ReadOnlySpan<byte> candidate = haystack.Slice(haystackIndex, candidateGroupLength);
                if (TryMatchRegexFrom(
                    candidate,
                    alternative,
                    haystackIndex: 0,
                    patternIndex: 0,
                    groupCaseInsensitive,
                    alternativeIgnoreWhitespace,
                    groupSwapGreed,
                    requireEnd: true,
                    out _))
                {
                    AddRegexGroupEndCandidate(ends, haystackIndex + candidateGroupLength);
                }
            }

            if (alternativeEnd == group.Length)
            {
                break;
            }

            alternativeStart = alternativeEnd + 1;
            alternativeIgnoreWhitespace = nextIgnoreWhitespace;
        }

        groupEndCache.Add(haystackIndex, ends);
        return ends;
    }

    private static void AddRegexGroupEndCandidate(List<int> ends, int end)
    {
        if (!ends.Contains(end))
        {
            ends.Add(end);
        }
    }

    private static void TryParseRegexQuantifier(
        ReadOnlySpan<byte> pattern,
        int quantifierIndex,
        out byte quantifier,
        out int minRepetitions,
        out int maxRepetitions,
        out bool lazy,
        out int suffixPatternIndex)
    {
        quantifier = 0;
        minRepetitions = 1;
        maxRepetitions = 1;
        lazy = false;
        suffixPatternIndex = quantifierIndex;
        if (quantifierIndex >= pattern.Length)
        {
            return;
        }

        byte token = pattern[quantifierIndex];
        if (token == (byte)'?')
        {
            quantifier = token;
            minRepetitions = 0;
            maxRepetitions = 1;
            suffixPatternIndex = quantifierIndex + 1;
            ConsumeLazyQuantifierSuffix(pattern, ref suffixPatternIndex, ref lazy);
            return;
        }

        if (token == (byte)'*')
        {
            quantifier = token;
            minRepetitions = 0;
            maxRepetitions = int.MaxValue;
            suffixPatternIndex = quantifierIndex + 1;
            ConsumeLazyQuantifierSuffix(pattern, ref suffixPatternIndex, ref lazy);
            return;
        }

        if (token == (byte)'+')
        {
            quantifier = token;
            minRepetitions = 1;
            maxRepetitions = int.MaxValue;
            suffixPatternIndex = quantifierIndex + 1;
            ConsumeLazyQuantifierSuffix(pattern, ref suffixPatternIndex, ref lazy);
            return;
        }

        if (token != (byte)'{')
        {
            return;
        }

        int index = quantifierIndex + 1;
        if (!TryReadRegexDecimal(pattern, ref index, out int min))
        {
            return;
        }

        int max = min;
        if (index < pattern.Length && pattern[index] == (byte)',')
        {
            index++;
            max = int.MaxValue;
            if (index < pattern.Length && IsAsciiDigitByte(pattern[index]))
            {
                if (!TryReadRegexDecimal(pattern, ref index, out max))
                {
                    return;
                }
            }
        }

        if (index >= pattern.Length || pattern[index] != (byte)'}' || max < min)
        {
            return;
        }

        quantifier = token;
        minRepetitions = min;
        maxRepetitions = max;
        suffixPatternIndex = index + 1;
        ConsumeLazyQuantifierSuffix(pattern, ref suffixPatternIndex, ref lazy);
    }

    private static void ConsumeLazyQuantifierSuffix(ReadOnlySpan<byte> pattern, ref int suffixPatternIndex, ref bool lazy)
    {
        if (suffixPatternIndex < pattern.Length && pattern[suffixPatternIndex] == (byte)'?')
        {
            lazy = true;
            suffixPatternIndex++;
        }
    }

    private static bool TryReadRegexDecimal(ReadOnlySpan<byte> pattern, ref int index, out int value)
    {
        value = 0;
        int start = index;
        while (index < pattern.Length && IsAsciiDigitByte(pattern[index]))
        {
            int digit = pattern[index] - (byte)'0';
            if (value > (int.MaxValue - digit) / 10)
            {
                value = int.MaxValue;
            }
            else
            {
                value = (value * 10) + digit;
            }

            index++;
        }

        return index > start;
    }

    private static bool TryParseRegexAtom(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        out int atomKind,
        out byte literal,
        out int classStart,
        out int classEnd,
        out int nextPatternIndex)
    {
        byte token = pattern[patternIndex];
        if (token == (byte)'\\')
        {
            byte escaped = patternIndex + 1 < pattern.Length ? pattern[patternIndex + 1] : (byte)'\\';
            if (TryParseRegexByteEscape(pattern, patternIndex, escaped, out literal, out nextPatternIndex))
            {
                atomKind = RegexAtomLiteral;
                classStart = 0;
                classEnd = 0;
                return true;
            }

            atomKind = GetEscapedRegexAtomKind(pattern, patternIndex, escaped, out int escapedNextPatternIndex);
            literal = escaped switch
            {
                (byte)'t' => (byte)'\t',
                (byte)'r' => (byte)'\r',
                (byte)'f' => (byte)'\f',
                _ => escaped,
            };
            classStart = 0;
            classEnd = 0;
            nextPatternIndex = escapedNextPatternIndex;
            return true;
        }

        if (token == (byte)'.')
        {
            atomKind = RegexAtomDot;
            literal = 0;
            classStart = 0;
            classEnd = 0;
            nextPatternIndex = patternIndex + 1;
            return true;
        }

        if (token == (byte)'[' && TryFindClassEnd(pattern, patternIndex, out int end))
        {
            atomKind = RegexAtomClass;
            literal = 0;
            classStart = patternIndex + 1;
            classEnd = end;
            nextPatternIndex = end + 1;
            return true;
        }

        if (token == (byte)'(' && TryParseInlineCaseFlag(pattern, patternIndex, out atomKind, out nextPatternIndex))
        {
            literal = 0;
            classStart = 0;
            classEnd = 0;
            return true;
        }

        if (token == (byte)'(' && TryFindGroupEnd(pattern, patternIndex, out int groupStart, out int groupEnd, out int groupAtomKind))
        {
            atomKind = groupAtomKind;
            literal = 0;
            classStart = groupStart;
            classEnd = groupEnd;
            nextPatternIndex = groupEnd + 1;
            return true;
        }

        if (token == (byte)'^')
        {
            atomKind = RegexAtomStartAnchor;
            literal = 0;
            classStart = 0;
            classEnd = 0;
            nextPatternIndex = patternIndex + 1;
            return true;
        }

        if (token == (byte)'$')
        {
            atomKind = RegexAtomEndAnchor;
            literal = 0;
            classStart = 0;
            classEnd = 0;
            nextPatternIndex = patternIndex + 1;
            return true;
        }

        atomKind = RegexAtomLiteral;
        literal = token;
        classStart = 0;
        classEnd = 0;
        nextPatternIndex = patternIndex + 1;
        return true;
    }

    private static bool TryParseRegexByteEscape(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        byte escaped,
        out byte literal,
        out int nextPatternIndex)
    {
        literal = 0;
        nextPatternIndex = patternIndex;
        if (escaped == (byte)'x')
        {
            if (patternIndex + 3 < pattern.Length &&
                TryReadHexByte(pattern[patternIndex + 2], pattern[patternIndex + 3], out literal))
            {
                nextPatternIndex = patternIndex + 4;
                return true;
            }

            return TryParseBracedRegexByteEscape(pattern, patternIndex + 2, out literal, out nextPatternIndex);
        }

        if (escaped == (byte)'u')
        {
            return TryParseBracedRegexByteEscape(pattern, patternIndex + 2, out literal, out nextPatternIndex);
        }

        return false;
    }

    private static bool TryParseBracedRegexByteEscape(
        ReadOnlySpan<byte> pattern,
        int braceIndex,
        out byte literal,
        out int nextPatternIndex)
    {
        literal = 0;
        nextPatternIndex = braceIndex;
        if (braceIndex >= pattern.Length || pattern[braceIndex] != (byte)'{')
        {
            return false;
        }

        int index = braceIndex + 1;
        int value = 0;
        int digits = 0;
        while (index < pattern.Length && pattern[index] != (byte)'}')
        {
            if (!TryGetHexValue(pattern[index], out int digit))
            {
                return false;
            }

            value = (value * 16) + digit;
            if (value > byte.MaxValue)
            {
                return false;
            }

            digits++;
            index++;
        }

        if (digits == 0 || index >= pattern.Length || pattern[index] != (byte)'}')
        {
            return false;
        }

        literal = (byte)value;
        nextPatternIndex = index + 1;
        return true;
    }

    private static bool TryReadHexByte(byte high, byte low, out byte value)
    {
        value = 0;
        if (!TryGetHexValue(high, out int highValue) || !TryGetHexValue(low, out int lowValue))
        {
            return false;
        }

        value = (byte)((highValue << 4) | lowValue);
        return true;
    }

    private static bool TryGetHexValue(byte value, out int digit)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            digit = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            digit = value - (byte)'A' + 10;
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            digit = value - (byte)'a' + 10;
            return true;
        }

        digit = 0;
        return false;
    }

    private static bool GetGroupCaseSensitivity(int atomKind, bool asciiCaseInsensitive)
    {
        return atomKind switch
        {
            RegexAtomCaseInsensitiveGroup => true,
            RegexAtomCaseSensitiveGroup => false,
            _ => asciiCaseInsensitive,
        };
    }

    private static bool GetGroupIgnoreWhitespace(int atomKind, bool ignoreWhitespace)
    {
        return atomKind switch
        {
            RegexAtomIgnoreWhitespaceGroup => true,
            RegexAtomSignificantWhitespaceGroup => false,
            _ => ignoreWhitespace,
        };
    }

    private static bool GetGroupSwapGreed(int atomKind, bool swapGreed)
    {
        return atomKind switch
        {
            RegexAtomSwapGreedGroup => true,
            RegexAtomStandardGreedGroup => false,
            _ => swapGreed,
        };
    }

    private static bool TryReadInlineIgnoreWhitespaceFlag(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        out bool ignoreWhitespace,
        out int nextPatternIndex)
    {
        if (patternIndex + 4 <= pattern.Length &&
            pattern.Slice(patternIndex, 4).SequenceEqual("(?x)"u8))
        {
            ignoreWhitespace = true;
            nextPatternIndex = patternIndex + 4;
            return true;
        }

        if (patternIndex + 5 <= pattern.Length &&
            pattern.Slice(patternIndex, 5).SequenceEqual("(?-x)"u8))
        {
            ignoreWhitespace = false;
            nextPatternIndex = patternIndex + 5;
            return true;
        }

        ignoreWhitespace = false;
        nextPatternIndex = patternIndex;
        return false;
    }

    private static void SkipIgnoredPatternBytes(ReadOnlySpan<byte> pattern, ref int patternIndex, bool ignoreWhitespace)
    {
        if (!ignoreWhitespace)
        {
            return;
        }

        while (patternIndex < pattern.Length)
        {
            if (IsRegexIgnoredWhitespaceByte(pattern[patternIndex]))
            {
                patternIndex++;
                continue;
            }

            if (pattern[patternIndex] != (byte)'#')
            {
                return;
            }

            patternIndex++;
            while (patternIndex < pattern.Length && pattern[patternIndex] != (byte)'\n')
            {
                patternIndex++;
            }
        }
    }

    private static bool IsRegexIgnoredWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f';
    }

    private static bool IsRegexFlagByte(byte value)
    {
        return value is (byte)'i' or (byte)'m' or (byte)'R' or (byte)'s' or (byte)'U' or (byte)'u' or (byte)'x';
    }

    private static bool TryParseInlineCaseFlag(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        out int atomKind,
        out int nextPatternIndex)
    {
        atomKind = 0;
        nextPatternIndex = patternIndex;
        if (patternIndex + 4 <= pattern.Length &&
            pattern.Slice(patternIndex, 4).SequenceEqual("(?i)"u8))
        {
            atomKind = RegexAtomEnableCaseInsensitive;
            nextPatternIndex = patternIndex + 4;
            return true;
        }

        if (patternIndex + 5 <= pattern.Length &&
            pattern.Slice(patternIndex, 5).SequenceEqual("(?-i)"u8))
        {
            atomKind = RegexAtomDisableCaseInsensitive;
            nextPatternIndex = patternIndex + 5;
            return true;
        }

        if (patternIndex + 4 <= pattern.Length &&
            pattern.Slice(patternIndex, 4).SequenceEqual("(?x)"u8))
        {
            atomKind = RegexAtomEnableIgnoreWhitespace;
            nextPatternIndex = patternIndex + 4;
            return true;
        }

        if (patternIndex + 5 <= pattern.Length &&
            pattern.Slice(patternIndex, 5).SequenceEqual("(?-x)"u8))
        {
            atomKind = RegexAtomDisableIgnoreWhitespace;
            nextPatternIndex = patternIndex + 5;
            return true;
        }

        if (patternIndex + 4 <= pattern.Length &&
            pattern.Slice(patternIndex, 4).SequenceEqual("(?U)"u8))
        {
            atomKind = RegexAtomEnableSwapGreed;
            nextPatternIndex = patternIndex + 4;
            return true;
        }

        if (patternIndex + 5 <= pattern.Length &&
            pattern.Slice(patternIndex, 5).SequenceEqual("(?-U)"u8))
        {
            atomKind = RegexAtomDisableSwapGreed;
            nextPatternIndex = patternIndex + 5;
            return true;
        }

        return false;
    }

    private static int GetEscapedRegexAtomKind(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        byte escaped,
        out int nextPatternIndex)
    {
        nextPatternIndex = patternIndex + (patternIndex + 1 < pattern.Length ? 2 : 1);
        if (escaped == (byte)'b' &&
            TryParseNamedWordBoundary(pattern, nextPatternIndex, out int atomKind, out int boundaryEnd))
        {
            nextPatternIndex = boundaryEnd;
            return atomKind;
        }

        return escaped switch
        {
            (byte)'b' => RegexAtomWordBoundary,
            (byte)'B' => RegexAtomNotWordBoundary,
            (byte)'d' => RegexAtomDigit,
            (byte)'D' => RegexAtomNotDigit,
            (byte)'w' => RegexAtomWord,
            (byte)'W' => RegexAtomNotWord,
            (byte)'s' => RegexAtomWhitespace,
            (byte)'S' => RegexAtomNotWhitespace,
            _ => RegexAtomLiteral,
        };
    }

    private static bool TryParseNamedWordBoundary(
        ReadOnlySpan<byte> pattern,
        int index,
        out int atomKind,
        out int nextPatternIndex)
    {
        atomKind = 0;
        nextPatternIndex = index;
        if (index >= pattern.Length || pattern[index] != (byte)'{')
        {
            return false;
        }

        if (index + 12 <= pattern.Length &&
            pattern.Slice(index, 12).SequenceEqual("{start-half}"u8))
        {
            atomKind = RegexAtomWordStartHalfBoundary;
            nextPatternIndex = index + 12;
            return true;
        }

        if (index + 10 <= pattern.Length &&
            pattern.Slice(index, 10).SequenceEqual("{end-half}"u8))
        {
            atomKind = RegexAtomWordEndHalfBoundary;
            nextPatternIndex = index + 10;
            return true;
        }

        if (index + 7 <= pattern.Length &&
            pattern.Slice(index, 7).SequenceEqual("{start}"u8))
        {
            atomKind = RegexAtomWordStartBoundary;
            nextPatternIndex = index + 7;
            return true;
        }

        if (index + 5 <= pattern.Length &&
            pattern.Slice(index, 5).SequenceEqual("{end}"u8))
        {
            atomKind = RegexAtomWordEndBoundary;
            nextPatternIndex = index + 5;
            return true;
        }

        return false;
    }

    private static bool RegexAtomMatches(
        ReadOnlySpan<byte> haystack,
        int haystackIndex,
        int atomKind,
        byte literal,
        ReadOnlySpan<byte> expression,
        bool asciiCaseInsensitive)
    {
        if (haystackIndex >= haystack.Length)
        {
            return false;
        }

        byte value = haystack[haystackIndex];
        return atomKind switch
        {
            RegexAtomDot => value != (byte)'\n',
            RegexAtomClass => ClassMatches(value, expression, asciiCaseInsensitive),
            RegexAtomDigit => IsAsciiDigitByte(value),
            RegexAtomNotDigit => value != (byte)'\n' && !IsAsciiDigitByte(value),
            RegexAtomWord => IsAsciiWordByte(value),
            RegexAtomNotWord => value != (byte)'\n' && !IsAsciiWordByte(value),
            RegexAtomWhitespace => value != (byte)'\n' && IsRegexWhitespaceByte(value),
            RegexAtomNotWhitespace => value != (byte)'\n' && !IsRegexWhitespaceByte(value),
            _ => ByteEquals(value, literal, asciiCaseInsensitive),
        };
    }

    private static bool IsZeroWidthRegexAtom(int atomKind)
    {
        return atomKind is RegexAtomStartAnchor
            or RegexAtomEndAnchor
            or RegexAtomWordBoundary
            or RegexAtomNotWordBoundary
            or RegexAtomWordStartBoundary
            or RegexAtomWordEndBoundary
            or RegexAtomWordStartHalfBoundary
            or RegexAtomWordEndHalfBoundary;
    }

    private static bool RegexAssertionMatches(ReadOnlySpan<byte> haystack, int haystackIndex, int atomKind)
    {
        return atomKind switch
        {
            RegexAtomStartAnchor => haystackIndex == 0,
            RegexAtomEndAnchor => IsRegexEndAnchorPosition(haystack, haystackIndex),
            RegexAtomWordBoundary => IsRegexWordBoundary(haystack, haystackIndex),
            RegexAtomNotWordBoundary => !IsRegexWordBoundary(haystack, haystackIndex),
            RegexAtomWordStartBoundary => IsRegexWordStartBoundary(haystack, haystackIndex),
            RegexAtomWordEndBoundary => IsRegexWordEndBoundary(haystack, haystackIndex),
            RegexAtomWordStartHalfBoundary => IsRegexWordStartHalfBoundary(haystack, haystackIndex),
            RegexAtomWordEndHalfBoundary => IsRegexWordEndHalfBoundary(haystack, haystackIndex),
            _ => false,
        };
    }

    private static bool IsRegexEndAnchorPosition(ReadOnlySpan<byte> haystack, int haystackIndex)
    {
        if (haystackIndex == haystack.Length)
        {
            return true;
        }

        return haystackIndex + 1 == haystack.Length &&
            (haystack[haystackIndex] == (byte)'\n' || haystack[haystackIndex] == 0);
    }

    private static bool IsRegexWordBoundary(ReadOnlySpan<byte> haystack, int haystackIndex)
    {
        bool leftIsWord = haystackIndex > 0 && IsAsciiWordByte(haystack[haystackIndex - 1]);
        bool rightIsWord = haystackIndex < haystack.Length && IsAsciiWordByte(haystack[haystackIndex]);
        return leftIsWord != rightIsWord;
    }

    private static bool IsRegexWordStartBoundary(ReadOnlySpan<byte> haystack, int haystackIndex)
    {
        bool leftIsWord = haystackIndex > 0 && IsAsciiWordByte(haystack[haystackIndex - 1]);
        bool rightIsWord = haystackIndex < haystack.Length && IsAsciiWordByte(haystack[haystackIndex]);
        return !leftIsWord && rightIsWord;
    }

    private static bool IsRegexWordEndBoundary(ReadOnlySpan<byte> haystack, int haystackIndex)
    {
        bool leftIsWord = haystackIndex > 0 && IsAsciiWordByte(haystack[haystackIndex - 1]);
        bool rightIsWord = haystackIndex < haystack.Length && IsAsciiWordByte(haystack[haystackIndex]);
        return leftIsWord && !rightIsWord;
    }

    private static bool IsRegexWordStartHalfBoundary(ReadOnlySpan<byte> haystack, int haystackIndex)
    {
        bool leftIsWord = haystackIndex > 0 && IsAsciiWordByte(haystack[haystackIndex - 1]);
        return !leftIsWord;
    }

    private static bool IsRegexWordEndHalfBoundary(ReadOnlySpan<byte> haystack, int haystackIndex)
    {
        bool rightIsWord = haystackIndex < haystack.Length && IsAsciiWordByte(haystack[haystackIndex]);
        return !rightIsWord;
    }

    private static bool IsRegexQuantifier(byte value)
    {
        return value is (byte)'?' or (byte)'*' or (byte)'+';
    }

    private static bool TryFindClassEnd(ReadOnlySpan<byte> pattern, int classStart, out int classEnd)
    {
        for (int index = classStart + 1; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\\')
            {
                index++;
                continue;
            }

            if (pattern[index] == (byte)'[' &&
                index + 1 < pattern.Length &&
                pattern[index + 1] == (byte)':' &&
                TryFindPosixClassEnd(pattern, index + 2, out int posixClassEnd))
            {
                index = posixClassEnd;
                continue;
            }

            if (pattern[index] == (byte)']')
            {
                classEnd = index;
                return true;
            }
        }

        classEnd = -1;
        return false;
    }

    private static bool TryFindPosixClassEnd(ReadOnlySpan<byte> pattern, int index, out int classEnd)
    {
        while (index + 1 < pattern.Length)
        {
            if (pattern[index] == (byte)':' && pattern[index + 1] == (byte)']')
            {
                classEnd = index + 1;
                return true;
            }

            index++;
        }

        classEnd = -1;
        return false;
    }

    private static bool TryFindGroupEnd(
        ReadOnlySpan<byte> pattern,
        int groupStart,
        out int contentStart,
        out int groupEnd,
        out int atomKind)
    {
        contentStart = groupStart + 1;
        groupEnd = -1;
        atomKind = RegexAtomGroup;
        if (contentStart < pattern.Length && pattern[contentStart] == (byte)'?')
        {
            if (contentStart + 1 < pattern.Length && pattern[contentStart + 1] == (byte)':')
            {
                contentStart += 2;
            }
            else if (contentStart + 2 < pattern.Length && pattern[contentStart + 1] == (byte)'i' && pattern[contentStart + 2] == (byte)':')
            {
                atomKind = RegexAtomCaseInsensitiveGroup;
                contentStart += 3;
            }
            else if (contentStart + 2 < pattern.Length && pattern[contentStart + 1] == (byte)'x' && pattern[contentStart + 2] == (byte)':')
            {
                atomKind = RegexAtomIgnoreWhitespaceGroup;
                contentStart += 3;
            }
            else if (contentStart + 2 < pattern.Length && pattern[contentStart + 1] == (byte)'U' && pattern[contentStart + 2] == (byte)':')
            {
                atomKind = RegexAtomSwapGreedGroup;
                contentStart += 3;
            }
            else if (contentStart + 3 < pattern.Length &&
                pattern[contentStart + 1] == (byte)'-' &&
                pattern[contentStart + 2] == (byte)'i' &&
                pattern[contentStart + 3] == (byte)':')
            {
                atomKind = RegexAtomCaseSensitiveGroup;
                contentStart += 4;
            }
            else if (contentStart + 3 < pattern.Length &&
                pattern[contentStart + 1] == (byte)'-' &&
                pattern[contentStart + 2] == (byte)'x' &&
                pattern[contentStart + 3] == (byte)':')
            {
                atomKind = RegexAtomSignificantWhitespaceGroup;
                contentStart += 4;
            }
            else if (contentStart + 3 < pattern.Length &&
                pattern[contentStart + 1] == (byte)'-' &&
                pattern[contentStart + 2] == (byte)'U' &&
                pattern[contentStart + 3] == (byte)':')
            {
                atomKind = RegexAtomStandardGreedGroup;
                contentStart += 4;
            }
            else if (TryParseNamedGroupPrefix(pattern, contentStart, out int namedContentStart))
            {
                contentStart = namedContentStart;
            }
            else
            {
                return false;
            }
        }

        int depth = 1;
        for (int index = groupStart + 1; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'\\')
            {
                index++;
                continue;
            }

            if (value == (byte)'[' && TryFindClassEnd(pattern, index, out int classEnd))
            {
                index = classEnd;
                continue;
            }

            if (value == (byte)'(')
            {
                depth++;
                continue;
            }

            if (value == (byte)')')
            {
                depth--;
                if (depth == 0)
                {
                    groupEnd = index;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseNamedGroupPrefix(ReadOnlySpan<byte> pattern, int questionIndex, out int contentStart)
    {
        contentStart = 0;
        int nameStart;
        if (questionIndex + 2 < pattern.Length &&
            pattern[questionIndex + 1] == (byte)'P' &&
            pattern[questionIndex + 2] == (byte)'<')
        {
            nameStart = questionIndex + 3;
        }
        else if (questionIndex + 1 < pattern.Length && pattern[questionIndex + 1] == (byte)'<')
        {
            nameStart = questionIndex + 2;
        }
        else
        {
            return false;
        }

        int nameEnd = nameStart;
        while (nameEnd < pattern.Length && IsCaptureNameByte(pattern[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd == nameStart ||
            nameStart >= pattern.Length ||
            !IsCaptureNameStartByte(pattern[nameStart]) ||
            nameEnd >= pattern.Length ||
            pattern[nameEnd] != (byte)'>')
        {
            return false;
        }

        contentStart = nameEnd + 1;
        return true;
    }

    private static bool IsCaptureNameByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value is >= (byte)'0' and <= (byte)'9' ||
            value == (byte)'_';
    }

    private static bool IsCaptureNameStartByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value == (byte)'_';
    }

    private static bool ClassMatches(byte value, ReadOnlySpan<byte> expression, bool asciiCaseInsensitive)
    {
        if (value == (byte)'\n')
        {
            return false;
        }

        bool negated = !expression.IsEmpty && expression[0] == (byte)'^';
        int index = negated ? 1 : 0;
        bool matched = false;
        while (index < expression.Length)
        {
            if (!TryReadClassToken(expression, ref index, out int tokenKind, out byte literal, out bool tokenNegated))
            {
                break;
            }

            if (!tokenNegated &&
                tokenKind == RegexAtomLiteral &&
                index + 1 < expression.Length &&
                expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (!TryReadClassToken(expression, ref rangeEndIndex, out int rangeEndKind, out byte rangeEndLiteral, out bool rangeEndNegated) ||
                    rangeEndNegated ||
                    rangeEndKind != RegexAtomLiteral)
                {
                    if (ClassTokenMatches(value, tokenKind, literal, tokenNegated, asciiCaseInsensitive))
                    {
                        matched = true;
                    }

                    continue;
                }

                index = rangeEndIndex;
                byte foldedValue = FoldMaybe(value, asciiCaseInsensitive);
                byte foldedStart = FoldMaybe(literal, asciiCaseInsensitive);
                byte foldedEnd = FoldMaybe(rangeEndLiteral, asciiCaseInsensitive);
                if (foldedStart <= foldedValue && foldedValue <= foldedEnd)
                {
                    matched = true;
                }

                continue;
            }

            if (ClassTokenMatches(value, tokenKind, literal, tokenNegated, asciiCaseInsensitive))
            {
                matched = true;
            }
        }

        return negated ? !matched : matched;
    }

    private static bool TryReadClassToken(
        ReadOnlySpan<byte> expression,
        ref int index,
        out int tokenKind,
        out byte literal,
        out bool tokenNegated)
    {
        tokenKind = RegexAtomLiteral;
        literal = 0;
        tokenNegated = false;
        if (index >= expression.Length)
        {
            return false;
        }

        if (TryParsePosixClass(expression, index, out tokenKind, out tokenNegated, out int nextIndex))
        {
            index = nextIndex;
            return true;
        }

        if (expression[index] == (byte)'\\' && index + 1 < expression.Length)
        {
            byte escaped = expression[index + 1];
            if (TryParseRegexByteEscape(expression, index, escaped, out literal, out nextIndex))
            {
                index = nextIndex;
                return true;
            }

            switch (escaped)
            {
                case (byte)'d':
                    tokenKind = RegexAtomDigit;
                    index += 2;
                    return true;
                case (byte)'D':
                    tokenKind = RegexAtomDigit;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'w':
                    tokenKind = RegexAtomWord;
                    index += 2;
                    return true;
                case (byte)'W':
                    tokenKind = RegexAtomWord;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'s':
                    tokenKind = RegexAtomWhitespace;
                    index += 2;
                    return true;
                case (byte)'S':
                    tokenKind = RegexAtomWhitespace;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'t':
                    literal = (byte)'\t';
                    index += 2;
                    return true;
                case (byte)'r':
                    literal = (byte)'\r';
                    index += 2;
                    return true;
                case (byte)'f':
                    literal = (byte)'\f';
                    index += 2;
                    return true;
                default:
                    literal = escaped;
                    index += 2;
                    return true;
            }
        }

        literal = expression[index];
        index++;
        return true;
    }

    private static bool TryParsePosixClass(
        ReadOnlySpan<byte> expression,
        int index,
        out int tokenKind,
        out bool tokenNegated,
        out int nextIndex)
    {
        tokenKind = RegexAtomLiteral;
        tokenNegated = false;
        nextIndex = index;
        if (index + 4 >= expression.Length ||
            expression[index] != (byte)'[' ||
            expression[index + 1] != (byte)':')
        {
            return false;
        }

        int nameStart = index + 2;
        if (nameStart < expression.Length && expression[nameStart] == (byte)'^')
        {
            tokenNegated = true;
            nameStart++;
        }

        int nameEnd = nameStart;
        while (nameEnd + 1 < expression.Length &&
            !(expression[nameEnd] == (byte)':' && expression[nameEnd + 1] == (byte)']'))
        {
            nameEnd++;
        }

        if (nameEnd == nameStart || nameEnd + 1 >= expression.Length)
        {
            return false;
        }

        if (!TryGetPosixClassKind(expression[nameStart..nameEnd], out tokenKind))
        {
            return false;
        }

        nextIndex = nameEnd + 2;
        return true;
    }

    private static bool TryGetPosixClassKind(ReadOnlySpan<byte> name, out int tokenKind)
    {
        tokenKind = 0;
        if (name.SequenceEqual("alnum"u8))
        {
            tokenKind = RegexClassAlnum;
            return true;
        }

        if (name.SequenceEqual("alpha"u8))
        {
            tokenKind = RegexClassAlpha;
            return true;
        }

        if (name.SequenceEqual("ascii"u8))
        {
            tokenKind = RegexClassAscii;
            return true;
        }

        if (name.SequenceEqual("blank"u8))
        {
            tokenKind = RegexClassBlank;
            return true;
        }

        if (name.SequenceEqual("cntrl"u8))
        {
            tokenKind = RegexClassControl;
            return true;
        }

        if (name.SequenceEqual("digit"u8))
        {
            tokenKind = RegexAtomDigit;
            return true;
        }

        if (name.SequenceEqual("graph"u8))
        {
            tokenKind = RegexClassGraph;
            return true;
        }

        if (name.SequenceEqual("lower"u8))
        {
            tokenKind = RegexClassLower;
            return true;
        }

        if (name.SequenceEqual("print"u8))
        {
            tokenKind = RegexClassPrint;
            return true;
        }

        if (name.SequenceEqual("punct"u8))
        {
            tokenKind = RegexClassPunct;
            return true;
        }

        if (name.SequenceEqual("space"u8))
        {
            tokenKind = RegexAtomWhitespace;
            return true;
        }

        if (name.SequenceEqual("upper"u8))
        {
            tokenKind = RegexClassUpper;
            return true;
        }

        if (name.SequenceEqual("word"u8))
        {
            tokenKind = RegexAtomWord;
            return true;
        }

        if (name.SequenceEqual("xdigit"u8))
        {
            tokenKind = RegexClassHexDigit;
            return true;
        }

        return false;
    }

    private static bool ClassTokenMatches(
        byte value,
        int tokenKind,
        byte literal,
        bool tokenNegated,
        bool asciiCaseInsensitive)
    {
        bool matched = tokenKind switch
        {
            RegexAtomDigit => IsAsciiDigitByte(value),
            RegexAtomWord => IsAsciiWordByte(value),
            RegexAtomWhitespace => IsRegexWhitespaceByte(value),
            RegexClassAlnum => IsAsciiAlphaByte(value) || IsAsciiDigitByte(value),
            RegexClassAlpha => IsAsciiAlphaByte(value),
            RegexClassAscii => value <= 0x7f,
            RegexClassBlank => value is (byte)' ' or (byte)'\t',
            RegexClassControl => value < 0x20 || value == 0x7f,
            RegexClassGraph => value is >= 0x21 and <= 0x7e,
            RegexClassLower => asciiCaseInsensitive
                ? IsAsciiAlphaByte(value)
                : value is >= (byte)'a' and <= (byte)'z',
            RegexClassPrint => value is >= 0x20 and <= 0x7e,
            RegexClassPunct => (value is >= 0x21 and <= 0x7e) && !IsAsciiAlphaByte(value) && !IsAsciiDigitByte(value),
            RegexClassUpper => asciiCaseInsensitive
                ? IsAsciiAlphaByte(value)
                : value is >= (byte)'A' and <= (byte)'Z',
            RegexClassHexDigit => IsAsciiHexDigitByte(value),
            _ => ByteEquals(value, literal, asciiCaseInsensitive),
        };
        return tokenNegated ? !matched : matched;
    }

    private static bool IsAsciiAlphaByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    private static bool IsAsciiHexDigitByte(byte value)
    {
        return IsAsciiDigitByte(value) ||
            value is >= (byte)'A' and <= (byte)'F'
                or >= (byte)'a' and <= (byte)'f';
    }

    private static bool ByteEquals(byte left, byte right, bool asciiCaseInsensitive)
    {
        return FoldMaybe(left, asciiCaseInsensitive) == FoldMaybe(right, asciiCaseInsensitive);
    }

    private static byte FoldMaybe(byte value, bool asciiCaseInsensitive)
    {
        return asciiCaseInsensitive ? FoldAscii(value) : value;
    }

    private static bool HasAnyEmptyPattern(IReadOnlyList<byte[]> needles)
    {
        for (int index = 0; index < needles.Count; index++)
        {
            byte[] needle = needles[index];
            ArgumentNullException.ThrowIfNull(needle);
            if (needle.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int FindWord(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (needle.IsEmpty)
        {
            for (int index = 0; index <= haystack.Length; index++)
            {
                if (IsWordBoundary(haystack, index, index))
                {
                    return index;
                }
            }

            return -1;
        }

        int offset = 0;
        while (offset <= haystack.Length)
        {
            int found = Find(haystack[offset..], needle, asciiCaseInsensitive);
            if (found < 0)
            {
                return -1;
            }

            int start = offset + found;
            int end = start + needle.Length;
            if (IsWordBoundary(haystack, start, end))
            {
                return start;
            }

            offset = start + 1;
        }

        return -1;
    }

    private static bool IsWordBoundary(ReadOnlySpan<byte> haystack, int start, int end)
    {
        bool leftIsWord = start > 0 && IsAsciiWordByte(haystack[start - 1]);
        bool rightIsWord = end < haystack.Length && IsAsciiWordByte(haystack[end]);
        return !leftIsWord && !rightIsWord;
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_'
            || (value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z');
    }

    private static bool IsAsciiDigitByte(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsRegexWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static bool TryGetWholeHaystackLiteralSetRegexPlan(
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        out RegexLiteralSetEngine literalSetEngine)
    {
        literalSetEngine = null!;
        if (invertMatch ||
            lineRegexp ||
            wordRegexp ||
            crlf ||
            nullData ||
            needles.Count != 1 ||
            PatternContainsLineTerminator(needles[0], nullData) ||
            regexPlan?.GetLiteralSetEngine(0) is not RegexLiteralSetEngine engine)
        {
            return false;
        }

        literalSetEngine = engine;
        return true;
    }

    private static bool PatternContainsLineTerminator(ReadOnlySpan<byte> pattern, bool nullData)
    {
        return pattern.Contains(GetLineTerminator(nullData));
    }

    private static ReadOnlySpan<byte> LineContent(ReadOnlySpan<byte> line, bool crlf, bool nullData)
    {
        if (!line.IsEmpty && line[^1] == GetLineTerminator(nullData))
        {
            line = line[..^1];
            if (!nullData && crlf && !line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }
        }

        return line;
    }

    private static ReadOnlySpan<byte> PatternContent(ReadOnlySpan<byte> line, bool wordRegexp, bool crlf, bool nullData)
    {
        return wordRegexp || crlf || nullData ? LineContent(line, crlf, nullData) : line;
    }

    private static int GetLineLength(ReadOnlySpan<byte> remaining, bool nullData)
    {
        int terminator = remaining.IndexOf(GetLineTerminator(nullData));
        return terminator < 0 ? remaining.Length : terminator + 1;
    }

    private static int GetSearchLineStart(ReadOnlySpan<byte> haystack, int offset)
    {
        int previousTerminator = haystack[..offset].LastIndexOf((byte)'\n');
        return previousTerminator < 0 ? 0 : previousTerminator + 1;
    }

    private static int GetSearchLineEnd(ReadOnlySpan<byte> haystack, int lineStart)
    {
        int terminator = haystack[lineStart..].IndexOf((byte)'\n');
        return terminator < 0 ? haystack.Length : lineStart + terminator + 1;
    }

    private static long CountLineTerminators(ReadOnlySpan<byte> bytes)
    {
        return ByteCounter.Count(bytes, (byte)'\n');
    }

    private static long CountSearchLines(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        long terminators = CountLineTerminators(bytes);
        return bytes[^1] == (byte)'\n' ? terminators : terminators + 1;
    }

    private static byte GetLineTerminator(bool nullData)
    {
        return nullData ? (byte)0 : (byte)'\n';
    }

    private static int IndexOfNonAscii(ReadOnlySpan<byte> bytes, int offset)
    {
        int relativeIndex = bytes[offset..].IndexOfAnyExceptInRange((byte)0x00, (byte)0x7f);
        return relativeIndex < 0 ? -1 : offset + relativeIndex;
    }

    private static bool CanUseAsciiRegexMatch(ReadOnlySpan<byte> haystack, int matchStart, int nonAscii, bool requireMatchColumn)
    {
        if (nonAscii < 0 || matchStart <= nonAscii)
        {
            return true;
        }

        return !requireMatchColumn && haystack.Slice(nonAscii, matchStart - nonAscii).IndexOf((byte)'\n') < 0;
    }

    internal static int Find(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, bool asciiCaseInsensitive)
    {
        if (!asciiCaseInsensitive)
        {
            return MemmemSearch.Find(haystack, needle);
        }

        if (needle.IsEmpty)
        {
            return 0;
        }

        if (needle.Length > haystack.Length)
        {
            return -1;
        }

        if (ContainsNonAscii(needle) && TryFindUtf8IgnoreCase(haystack, needle, out int utf8Start, out _))
        {
            return utf8Start;
        }

        int lastStart = haystack.Length - needle.Length;
        for (int index = 0; index <= lastStart; index++)
        {
            if (EqualsAsciiIgnoreCase(haystack.Slice(index, needle.Length), needle))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (int index = 0; index < left.Length; index++)
        {
            if (FoldAscii(left[index]) != FoldAscii(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLineEndEmptyMatch(ReadOnlySpan<byte> line, int start, int length)
    {
        return line.Length > 0 && length == 0 && start == line.Length;
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

    internal static bool IsLiteralRegex(ReadOnlySpan<byte> pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] is (byte)'\\'
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
                or (byte)'|'
                or (byte)'^'
                or (byte)'$')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindUtf8IgnoreCase(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, out int start, out int length)
    {
        start = -1;
        length = 0;
        try
        {
            string haystackText = StrictUtf8.GetString(haystack);
            string needleText = StrictUtf8.GetString(needle);
            int charStart = haystackText.IndexOf(needleText, StringComparison.OrdinalIgnoreCase);
            if (charStart < 0)
            {
                return false;
            }

            start = StrictUtf8.GetByteCount(haystackText.AsSpan(0, charStart));
            length = StrictUtf8.GetByteCount(haystackText.AsSpan(charStart, needleText.Length));
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }
}
